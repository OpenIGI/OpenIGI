using System.Numerics;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Streamline;

/// <summary>Streamline's features behind the RHI, whichever graphics API the device runs.</summary>
/// <remarks>
/// <para>
/// Everything here is the same on Vulkan and on Direct3D 12: which features run, the options and constants,
/// Reflex, frame generation's state, and which frame every call belongs to. What Streamline needs that the
/// RHI does not say -- the native image, its view, its format, the state it is in, the command list -- is
/// asked of the backend through <see cref="IStreamlineGraphicsAdapter"/>, so nothing above the backend ever
/// holds a native handle and no backend repeats any of this.
/// </para>
/// <para>
/// Evaluation happens between passes, never inside one: the encoder opens each pass as an object of its
/// own and binds everything that pass uses when it opens, so no pipeline or descriptor state survives
/// across Streamline's work for it to disturb. A full barrier either side makes what was written before
/// visible to Streamline and what Streamline wrote visible after. That is what lets the session run with
/// Streamline's own state tracking switched off.
/// </para>
/// </remarks>
internal sealed unsafe class StreamlineSession : IRhiStreamline, IDisposable
{
    private const string LogCategory = StreamlineRuntime.LogCategory;

    private readonly StreamlineRuntime _runtime;
    private readonly IStreamlineGraphicsAdapter _adapter;
    private readonly IOpenIgiLogger _logger;
    private readonly SlFeatureApi _api;
    private readonly Dictionary<RhiStreamlineFeature, RhiStreamlineSupport> _support = [];

    // Options already sent, so a frame that asks for the same again sends nothing. Streamline warns for
    // every redundant set, and frame generation in particular is documented as not a per-frame call.
    private readonly Dictionary<int, (RhiDlssMode Mode, int Width, int Height)> _superResolution = [];
    private readonly Dictionary<int, RhiFrameGenerationOptions> _frameGeneration = [];
    private (RhiLowLatencyMode Mode, int Limit)? _latency;

    // Textures tagged to live until the present. Frame generation reads them then, in the layout the tag
    // named, so the present puts them back into it whatever happened to them since.
    private readonly List<IRhiTexture> _untilPresent = [];

    // How often a failure has been reported, so one that repeats every frame is said once and then counted.
    private readonly Dictionary<string, int> _reported = [];
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="StreamlineSession"/> class.</summary>
    /// <param name="runtime">The runtime, already started, with the device handed to it.</param>
    /// <param name="adapter">The backend's native side.</param>
    /// <param name="logger">Where to report.</param>
    public StreamlineSession(StreamlineRuntime runtime, IStreamlineGraphicsAdapter adapter, IOpenIgiLogger logger)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(logger);

        _runtime = runtime;
        _adapter = adapter;
        _logger = logger;

        // Which features this adapter runs, asked before anything is bound: a plugin Streamline declined to
        // load answers every later call about it with an error in the log.
        Dictionary<uint, bool> supported = [];
        foreach (uint feature in runtime.Features)
        {
            supported[feature] = runtime.IsFeatureSupported(feature, adapter.Adapter) == SlResult.Ok;
        }

        _api = runtime.BindFeatureFunctions(feature => supported.TryGetValue(feature, out bool yes) && yes);

        foreach (RhiStreamlineFeature feature in Enum.GetValues<RhiStreamlineFeature>())
        {
            _support[feature] = Query(feature);
        }

        // Reflex wants its options set once at start-up whatever the mode, so the driver knows the
        // application is managing latency at all; the markers are measured from then on.
        SetLatencyOptions(RhiLowLatencyMode.Off, 0);

        FrameGenerationLoaded = supported.TryGetValue(SlFeature.DlssG, out bool generates) && generates
            && runtime.IsFeatureLoaded(SlFeature.DlssG);
    }

    /// <inheritdoc/>
    public string Description => $"Streamline from '{_runtime.Directory}'";

    /// <inheritdoc/>
    public ulong FrameIndex => _runtime.FrameIndex;

    /// <inheritdoc/>
    public bool FrameOpen => _runtime.FrameOpen;

    /// <inheritdoc/>
    public bool FrameGenerationLoaded { get; private set; }

    /// <summary>Gets a value indicating whether the frame's image may still be written when it is handed out.</summary>
    /// <remarks>
    /// While frame generation is loaded the image handed out is Streamline's, and it may still be writing it,
    /// so a backend waits for it at every stage rather than at colour output alone.
    /// </remarks>
    internal bool WaitAcquireOnAllStages => FrameGenerationLoaded;

    /// <inheritdoc/>
    public RhiStreamlineSupport Support(RhiStreamlineFeature feature) =>
        _support.TryGetValue(feature, out RhiStreamlineSupport support)
            ? support
            : new RhiStreamlineSupport(false, "unknown feature");

    /// <inheritdoc/>
    public bool BeginFrame() => _runtime.BeginFrame();

    /// <inheritdoc/>
    public void Sleep()
    {
        // Called in every mode while Reflex is loaded, Off included: the sleep is also how the driver
        // learns where the frame starts.
        if (!_runtime.FrameOpen || _api.ReflexSleep == null)
        {
            return;
        }

        SlResult result = _api.ReflexSleep(_runtime.CurrentFrame);
        Report(result, "slReflexSleep");
    }

    /// <inheritdoc/>
    public void Mark(RhiLatencyMarker marker)
    {
        SlPclMarker native = marker switch
        {
            RhiLatencyMarker.SimulationStart => SlPclMarker.SimulationStart,
            RhiLatencyMarker.SimulationEnd => SlPclMarker.SimulationEnd,
            RhiLatencyMarker.RenderSubmitStart => SlPclMarker.RenderSubmitStart,
            RhiLatencyMarker.RenderSubmitEnd => SlPclMarker.RenderSubmitEnd,
            RhiLatencyMarker.TriggerFlash => SlPclMarker.TriggerFlash,
            _ => throw new ArgumentOutOfRangeException(nameof(marker), marker, null),
        };

        MarkNative(native);
    }

    /// <inheritdoc/>
    public void SetLatencyOptions(RhiLowLatencyMode mode, int frameLimitMicroseconds)
    {
        if (_api.ReflexSetOptions == null)
        {
            return;
        }

        int limit = Math.Max(0, frameLimitMicroseconds);
        if (_latency == (mode, limit))
        {
            return;
        }

        // Low latency may be unavailable while the markers still work; asking for it then is refused,
        // so the mode that is actually sent falls back to Off and says so once.
        RhiLowLatencyMode effective = mode;
        if (mode != RhiLowLatencyMode.Off && !Support(RhiStreamlineFeature.LowLatency).Supported)
        {
            effective = RhiLowLatencyMode.Off;
        }

        SlReflexOptions options = SlReflexOptions.Create();
        options.Mode = effective switch
        {
            RhiLowLatencyMode.On => SlReflexMode.LowLatency,
            RhiLowLatencyMode.Boost => SlReflexMode.LowLatencyWithBoost,
            _ => SlReflexMode.Off,
        };
        options.FrameLimitUs = (uint)limit;

        SlResult result = _api.ReflexSetOptions(&options);
        if (result == SlResult.Ok)
        {
            _latency = (mode, limit);
            _logger.Information(
                LogCategory,
                $"Low latency {effective}{(effective != mode ? $" ({mode} was asked for; {Support(RhiStreamlineFeature.LowLatency).Reason})" : string.Empty)}"
                    + (limit > 0 ? $", capped at {limit} µs a frame." : "."));
        }
        else
        {
            Report(result, "slReflexSetOptions");
        }
    }

    /// <inheritdoc/>
    public RhiLatencyState LatencyState()
    {
        if (_api.ReflexGetState == null)
        {
            return default;
        }

        SlReflexState state = SlReflexState.Create();
        if (_api.ReflexGetState(&state) != SlResult.Ok)
        {
            return default;
        }

        long latency = 0;
        long gpu = 0;
        if (state.LatencyReportAvailable != 0)
        {
            // The reports run oldest to newest; the newest with a frame in it is the one worth showing.
            for (int i = SlReflexState.ReportCount - 1; i >= 0; i--)
            {
                SlReflexReport report = state.Report(i);
                if (report.FrameId != 0 && report.GpuRenderEndTime > report.SimStartTime)
                {
                    latency = (long)(report.GpuRenderEndTime - report.SimStartTime);
                    gpu = report.GpuFrameTimeUs;
                    break;
                }
            }
        }

        return new RhiLatencyState(state.LowLatencyAvailable != 0, state.LatencyReportAvailable != 0, latency, gpu);
    }

    /// <inheritdoc/>
    public void SetConstants(int viewport, in RhiStreamlineConstants constants)
    {
        if (!_runtime.FrameOpen)
        {
            return;
        }

        SlConstants native = SlConstants.Create();
        Copy(constants.CameraViewToClip, ref native.CameraViewToClip);
        Copy(constants.ClipToCameraView, ref native.ClipToCameraView);
        Copy(Matrix4x4.Identity, ref native.ClipToLensClip);
        Copy(constants.ClipToPrevClip, ref native.ClipToPrevClip);
        Copy(constants.PrevClipToClip, ref native.PrevClipToClip);
        native.JitterOffset = new SlFloat2(constants.Jitter.X, constants.Jitter.Y);
        native.MvecScale = new SlFloat2(constants.MotionVectorScale.X, constants.MotionVectorScale.Y);
        native.CameraPinholeOffset = new SlFloat2(0f, 0f);
        native.CameraPos = Float3(constants.CameraPosition);
        native.CameraUp = Float3(constants.CameraUp);
        native.CameraRight = Float3(constants.CameraRight);
        native.CameraFwd = Float3(constants.CameraForward);
        native.CameraNear = constants.Near;
        native.CameraFar = constants.Far;
        native.CameraFov = constants.VerticalFieldOfView;
        native.CameraAspectRatio = constants.AspectRatio;
        native.DepthInverted = Bool(constants.DepthInverted);
        native.CameraMotionIncluded = Bool(constants.CameraMotionIncluded);
        native.MotionVectors3D = SlBoolean.False;
        native.Reset = Bool(constants.Reset);
        native.OrthographicProjection = SlBoolean.False;
        native.MotionVectorsDilated = SlBoolean.False;
        native.MotionVectorsJittered = Bool(constants.MotionVectorsJittered);

        SlViewportHandle handle = SlViewportHandle.For((uint)viewport);
        Report(_runtime.Api.SetConstants(&native, _runtime.CurrentFrame, &handle), "slSetConstants");
    }

    /// <inheritdoc/>
    public void Tag(int viewport, ReadOnlySpan<RhiStreamlineTag> tags, IRhiCommandEncoder encoder)
    {
        if (!_runtime.FrameOpen || tags.IsEmpty)
        {
            return;
        }

        SlResource* resources = stackalloc SlResource[tags.Length];
        SlResourceTag* native = stackalloc SlResourceTag[tags.Length];

        for (int i = 0; i < tags.Length; i++)
        {
            RhiStreamlineTag tag = tags[i];
            nint resource = 0;

            if (tag.Texture is { } texture)
            {
                // Into the state the tag will name, and described in it: the guarantee rather than the hope
                // that it rests there between passes already.
                resources[i] = _adapter.PrepareTag(texture, encoder);
                resource = (nint)(&resources[i]);

                if (tag.Lifecycle == RhiStreamlineLifecycle.UntilPresent && !_untilPresent.Contains(texture))
                {
                    _untilPresent.Add(texture);
                }
            }

            native[i] = SlResourceTag.For(resource, BufferType(tag.Buffer), Lifecycle(tag.Lifecycle));
            if (tag.Width > 0 && tag.Height > 0)
            {
                native[i].Extent = new SlExtent { Top = 0, Left = 0, Width = (uint)tag.Width, Height = (uint)tag.Height };
            }
        }

        SlViewportHandle handle = SlViewportHandle.For((uint)viewport);
        Report(
            _runtime.Api.SetTagForFrame(_runtime.CurrentFrame, &handle, native, (uint)tags.Length, _adapter.CommandList(encoder)),
            "slSetTagForFrame");
    }

    /// <inheritdoc/>
    public bool TryGetRenderSize(RhiStreamlineFeature feature, RhiDlssMode mode, int outputWidth, int outputHeight, out RhiDlssRenderSize size)
    {
        size = new RhiDlssRenderSize(outputWidth, outputHeight, outputWidth, outputHeight, outputWidth, outputHeight);
        if (mode == RhiDlssMode.Off || outputWidth < 1 || outputHeight < 1)
        {
            return false;
        }

        if (feature == RhiStreamlineFeature.RayReconstruction)
        {
            if (_api.DlssdGetOptimalSettings == null)
            {
                return false;
            }

            SlDlssDOptions options = SlDlssDOptions.Create();
            options.Mode = Mode(mode);
            options.OutputWidth = (uint)outputWidth;
            options.OutputHeight = (uint)outputHeight;
            SlDlssDOptimalSettings settings = SlDlssDOptimalSettings.Create();
            if (_api.DlssdGetOptimalSettings(&options, &settings) != SlResult.Ok)
            {
                return false;
            }

            size = new RhiDlssRenderSize(
                (int)settings.OptimalRenderWidth, (int)settings.OptimalRenderHeight,
                (int)settings.RenderWidthMin, (int)settings.RenderHeightMin,
                (int)settings.RenderWidthMax, (int)settings.RenderHeightMax);
        }
        else
        {
            if (_api.DlssGetOptimalSettings == null)
            {
                return false;
            }

            SlDlssOptions options = SlDlssOptions.Create();
            options.Mode = Mode(mode);
            options.OutputWidth = (uint)outputWidth;
            options.OutputHeight = (uint)outputHeight;
            SlDlssOptimalSettings settings = SlDlssOptimalSettings.Create();
            if (_api.DlssGetOptimalSettings(&options, &settings) != SlResult.Ok)
            {
                return false;
            }

            size = new RhiDlssRenderSize(
                (int)settings.OptimalRenderWidth, (int)settings.OptimalRenderHeight,
                (int)settings.RenderWidthMin, (int)settings.RenderHeightMin,
                (int)settings.RenderWidthMax, (int)settings.RenderHeightMax);
        }

        return size.Width > 0 && size.Height > 0;
    }

    /// <inheritdoc/>
    public void SetSuperResolutionOptions(int viewport, RhiDlssMode mode, int outputWidth, int outputHeight)
    {
        if (_api.DlssSetOptions == null
            || (_superResolution.TryGetValue(viewport, out var sent) && sent == (mode, outputWidth, outputHeight)))
        {
            return;
        }

        SlDlssOptions options = SlDlssOptions.Create();
        options.Mode = Mode(mode);
        options.OutputWidth = (uint)outputWidth;
        options.OutputHeight = (uint)outputHeight;

        // The colour is linear HDR, and the 1x1 exposure texture says what the tone mapper will multiply
        // it by — which is what lets the network see the image the player will, not the raw radiance.
        options.ColorBuffersHdr = SlBoolean.True;
        options.UseAutoExposure = SlBoolean.False;

        SlViewportHandle handle = SlViewportHandle.For((uint)viewport);
        if (Report(_api.DlssSetOptions(&handle, &options), "slDLSSSetOptions"))
        {
            _superResolution[viewport] = (mode, outputWidth, outputHeight);
        }
    }

    /// <inheritdoc/>
    public void SetRayReconstructionOptions(
        int viewport,
        RhiDlssMode mode,
        int outputWidth,
        int outputHeight,
        in Matrix4x4 worldToCameraView,
        in Matrix4x4 cameraViewToWorld)
    {
        if (_api.DlssdSetOptions == null)
        {
            return;
        }

        SlDlssDOptions options = SlDlssDOptions.Create();
        options.Mode = Mode(mode);
        options.OutputWidth = (uint)outputWidth;
        options.OutputHeight = (uint)outputHeight;
        options.ColorBuffersHdr = SlBoolean.True;
        Copy(worldToCameraView, ref options.WorldToCameraView);
        Copy(cameraViewToWorld, ref options.CameraViewToWorld);

        SlViewportHandle handle = SlViewportHandle.For((uint)viewport);
        Report(_api.DlssdSetOptions(&handle, &options), "slDLSSDSetOptions");
    }

    /// <inheritdoc/>
    public bool SetFrameGenerationLoaded(bool loaded)
    {
        if (FrameGenerationLoaded == loaded)
        {
            return true;
        }

        if (loaded && !Support(RhiStreamlineFeature.FrameGeneration).Supported)
        {
            return false;
        }

        bool done = false;

        // The guide's own sequence: idle, release the swapchain, change the plugin, build the swapchain
        // again. While loaded the host renders off-screen into Streamline's images, so a swapchain built
        // either side of the change is built for the wrong arrangement.
        _adapter.RebuildSwapchain(() =>
        {
            SlResult result = _runtime.SetFeatureLoaded(SlFeature.DlssG, loaded);
            done = Report(result, "slSetFeatureLoaded");
        });

        if (done)
        {
            FrameGenerationLoaded = loaded;
            _frameGeneration.Clear();
            _logger.Information(LogCategory, $"Frame generation {(loaded ? "loaded" : "unloaded")}; swapchain rebuilt around it.");
        }

        return done && FrameGenerationLoaded == loaded;
    }

    /// <inheritdoc/>
    public void SetFrameGenerationOptions(int viewport, in RhiFrameGenerationOptions options)
    {
        if (_api.DlssgSetOptions == null || !FrameGenerationLoaded
            || (_frameGeneration.TryGetValue(viewport, out RhiFrameGenerationOptions sent) && sent == options))
        {
            return;
        }

        SlDlssGOptions native = SlDlssGOptions.Create();
        native.Mode = options.Enabled ? SlDlssGMode.On : SlDlssGMode.Off;
        native.NumFramesToGenerate = (uint)Math.Max(1, options.FramesToGenerate);

        // Kept across a menu or a loading screen, so switching back on is immediate; and the plugin is
        // allowed to notice a full-screen menu the host did not tell it about.
        native.Flags = SlDlssGFlags.RetainResourcesWhenOff | SlDlssGFlags.EnableFullscreenMenuDetection;
        native.MvecDepthWidth = (uint)Math.Max(0, options.RenderWidth);
        native.MvecDepthHeight = (uint)Math.Max(0, options.RenderHeight);
        native.EnableUserInterfaceRecomposition = Bool(options.UserInterfaceRecomposition);
        native.QueueParallelismMode = options.BlockNoClientQueues
            ? SlDlssGQueueParallelismMode.BlockNoClientQueues
            : SlDlssGQueueParallelismMode.BlockPresentingClientQueue;

        SlViewportHandle handle = SlViewportHandle.For((uint)viewport);
        if (Report(_api.DlssgSetOptions(&handle, &native), "slDLSSGSetOptions"))
        {
            _frameGeneration[viewport] = options;
            _logger.Information(
                LogCategory,
                options.Enabled
                    ? $"Frame generation on: {options.FramesToGenerate + 1}x, inputs {options.RenderWidth}x{options.RenderHeight}"
                        + (options.UserInterfaceRecomposition ? ", interface recomposed." : ".")
                    : "Frame generation off (resources kept).");
        }
    }

    /// <inheritdoc/>
    public RhiFrameGenerationState FrameGenerationState(int viewport)
    {
        if (_api.DlssgGetState == null || !FrameGenerationLoaded)
        {
            return new RhiFrameGenerationState(false, "not loaded", 0, 0, 0, false);
        }

        SlDlssGState state = SlDlssGState.Create();
        SlViewportHandle handle = SlViewportHandle.For((uint)viewport);
        if (_api.DlssgGetState(&handle, &state, null) != SlResult.Ok)
        {
            return new RhiFrameGenerationState(false, "state unavailable", 0, 0, 0, false);
        }

        if (state.InputsProcessingCompletionFence != 0)
        {
            // The plugin is done with a frame's inputs once its fence reaches this value. Reusing them
            // before then on the presenting queue is ordered anyway; waiting is what makes it explicit.
            _adapter.AddFrameWait(state.InputsProcessingCompletionFence, state.LastPresentInputsProcessingCompletionFenceValue);
        }

        return new RhiFrameGenerationState(
            state.Status == SlDlssGStatus.Ok,
            state.Status == SlDlssGStatus.Ok ? string.Empty : state.Status.ToString(),
            (int)state.MinWidthOrHeight,
            (int)state.NumFramesToGenerateMax,
            (int)state.NumFramesActuallyPresented,
            state.IsVsyncSupportAvailable == SlBoolean.True);
    }

    /// <inheritdoc/>
    public bool Evaluate(RhiStreamlineFeature feature, int viewport, IRhiCommandEncoder encoder, out string? failure)
    {
        failure = null;
        if (!_runtime.FrameOpen)
        {
            failure = "no frame is open";
            return false;
        }

        uint id = Feature(feature);
        // Everything written so far must be visible to Streamline, and everything it writes to what follows.
        _adapter.ExternalBarrier(encoder);

        SlViewportHandle handle = SlViewportHandle.For((uint)viewport);
        nint input = (nint)(&handle);
        SlResult result = _runtime.Api.EvaluateFeature(id, _runtime.CurrentFrame, &input, 1, _adapter.CommandList(encoder));

        _adapter.ExternalBarrier(encoder);

        if (result != SlResult.Ok)
        {
            failure = StreamlineRuntime.Describe(result);
            Report(result, $"slEvaluateFeature({StreamlineRuntime.Describe(id)})");
            return false;
        }

        return true;
    }

    /// <inheritdoc/>
    public void FreeResources(RhiStreamlineFeature feature, int viewport)
    {
        _adapter.WaitIdle();
        SlViewportHandle handle = SlViewportHandle.For((uint)viewport);
        _ = _runtime.Api.FreeResources(Feature(feature), &handle);
        _superResolution.Remove(viewport);
        _frameGeneration.Remove(viewport);
    }

    /// <summary>Hands over the textures tagged until the present, for the backend to put back in their tagged state.</summary>
    /// <param name="restore">What puts one back, recorded last before the present.</param>
    internal void PrepareForPresent(Action<IRhiTexture> restore)
    {
        ArgumentNullException.ThrowIfNull(restore);
        foreach (IRhiTexture texture in _untilPresent)
        {
            restore(texture);
        }

        _untilPresent.Clear();
    }

    /// <summary>Marks the start of the present.</summary>
    internal void BeforePresent() => MarkNative(SlPclMarker.PresentStart);

    /// <summary>Marks the end of the present and closes the frame.</summary>
    internal void AfterPresent()
    {
        MarkNative(SlPclMarker.PresentEnd);
        _runtime.EndFrame();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Reflex back to Off, so the driver stops pacing a process that is about to have no frames.
        if (_api.ReflexSetOptions != null)
        {
            SlReflexOptions options = SlReflexOptions.Create();
            _ = _api.ReflexSetOptions(&options);
        }
    }

    private void MarkNative(SlPclMarker marker)
    {
        if (!_runtime.FrameOpen || _api.PclSetMarker == null)
        {
            return;
        }

        Report(_api.PclSetMarker(marker, _runtime.CurrentFrame), $"slPCLSetMarker({marker})");
    }

    private RhiStreamlineSupport Query(RhiStreamlineFeature feature)
    {
        uint id = Feature(feature);
        if (!_runtime.Features.Contains(id))
        {
            return new RhiStreamlineSupport(false, "not requested");
        }

        SlResult supported = _runtime.IsFeatureSupported(id, _adapter.Adapter);
        if (supported != SlResult.Ok)
        {
            string reason = StreamlineRuntime.Describe(supported);
            _logger.Information(LogCategory, $"{StreamlineRuntime.Describe(id)} is not available: {reason}.");
            return new RhiStreamlineSupport(false, reason);
        }

        if (feature == RhiStreamlineFeature.LowLatency && _api.ReflexGetState != null)
        {
            SlReflexState state = SlReflexState.Create();
            if (_api.ReflexGetState(&state) == SlResult.Ok && state.LowLatencyAvailable == 0)
            {
                _logger.Information(LogCategory, "Reflex low latency is not available on this system; latency is still measured.");
                return new RhiStreamlineSupport(false, "low latency is not available on this system");
            }
        }

        string version = _runtime.DescribeVersion(id) ?? "version unknown";
        _logger.Information(LogCategory, $"{StreamlineRuntime.Describe(id)} is available ({version}).");
        return new RhiStreamlineSupport(true, version);
    }

    private bool Report(SlResult result, string call)
    {
        if (result == SlResult.Ok)
        {
            return true;
        }

        string key = $"{call}:{result}";
        int count = _reported.TryGetValue(key, out int seen) ? seen + 1 : 1;
        _reported[key] = count;

        // Once, then at each power of ten, so a failure that repeats every frame stays visible without
        // drowning the log.
        if (count == 1 || IsPowerOfTen(count))
        {
            _logger.Warning(LogCategory, $"{call} failed: {StreamlineRuntime.Describe(result)}{(count > 1 ? $" ({count} times)" : string.Empty)}.");
        }

        return false;
    }

    private static bool IsPowerOfTen(int value)
    {
        while (value >= 10 && value % 10 == 0)
        {
            value /= 10;
        }

        return value == 1;
    }

    private static uint Feature(RhiStreamlineFeature feature) => feature switch
    {
        RhiStreamlineFeature.SuperResolution => SlFeature.Dlss,
        RhiStreamlineFeature.RayReconstruction => SlFeature.DlssRr,
        RhiStreamlineFeature.FrameGeneration => SlFeature.DlssG,
        RhiStreamlineFeature.LowLatency => SlFeature.Reflex,
        RhiStreamlineFeature.LatencyMarkers => SlFeature.Pcl,
        _ => throw new ArgumentOutOfRangeException(nameof(feature), feature, null),
    };

    private static uint BufferType(RhiStreamlineBuffer buffer) => buffer switch
    {
        RhiStreamlineBuffer.Depth => SlBufferType.Depth,
        RhiStreamlineBuffer.LinearDepth => SlBufferType.LinearDepth,
        RhiStreamlineBuffer.MotionVectors => SlBufferType.MotionVectors,
        RhiStreamlineBuffer.ScalingInputColor => SlBufferType.ScalingInputColor,
        RhiStreamlineBuffer.ScalingOutputColor => SlBufferType.ScalingOutputColor,
        RhiStreamlineBuffer.Exposure => SlBufferType.Exposure,
        RhiStreamlineBuffer.BiasCurrentColorHint => SlBufferType.BiasCurrentColorHint,
        RhiStreamlineBuffer.DiffuseAlbedo => SlBufferType.Albedo,
        RhiStreamlineBuffer.SpecularAlbedo => SlBufferType.SpecularAlbedo,
        RhiStreamlineBuffer.NormalRoughness => SlBufferType.NormalRoughness,
        RhiStreamlineBuffer.SpecularHitDistance => SlBufferType.SpecularHitDistance,
        RhiStreamlineBuffer.ColorBeforeTransparency => SlBufferType.ColorBeforeTransparency,
        RhiStreamlineBuffer.HudLessColor => SlBufferType.HudLessColor,
        RhiStreamlineBuffer.UiColorAndAlpha => SlBufferType.UiColorAndAlpha,
        _ => throw new ArgumentOutOfRangeException(nameof(buffer), buffer, null),
    };

    private static SlResourceLifecycle Lifecycle(RhiStreamlineLifecycle lifecycle) => lifecycle switch
    {
        RhiStreamlineLifecycle.UntilEvaluate => SlResourceLifecycle.ValidUntilEvaluate,
        RhiStreamlineLifecycle.UntilPresent => SlResourceLifecycle.ValidUntilPresent,
        _ => SlResourceLifecycle.OnlyValidNow,
    };

    private static SlDlssMode Mode(RhiDlssMode mode) => mode switch
    {
        RhiDlssMode.Dlaa => SlDlssMode.Dlaa,
        RhiDlssMode.Quality => SlDlssMode.MaxQuality,
        RhiDlssMode.Balanced => SlDlssMode.Balanced,
        RhiDlssMode.Performance => SlDlssMode.MaxPerformance,
        RhiDlssMode.UltraPerformance => SlDlssMode.UltraPerformance,
        _ => SlDlssMode.Off,
    };

    private static SlBoolean Bool(bool value) => value ? SlBoolean.True : SlBoolean.False;

    private static SlFloat3 Float3(Vector3 value) => new(value.X, value.Y, value.Z);

    /// <summary>Copies a matrix across. <see cref="Matrix4x4"/> and Streamline share a convention, so nothing is transposed.</summary>
    private static void Copy(in Matrix4x4 source, ref SlFloat4x4 target)
    {
        target.Set(0, 0, source.M11); target.Set(0, 1, source.M12); target.Set(0, 2, source.M13); target.Set(0, 3, source.M14);
        target.Set(1, 0, source.M21); target.Set(1, 1, source.M22); target.Set(1, 2, source.M23); target.Set(1, 3, source.M24);
        target.Set(2, 0, source.M31); target.Set(2, 1, source.M32); target.Set(2, 2, source.M33); target.Set(2, 3, source.M34);
        target.Set(3, 0, source.M41); target.Set(3, 1, source.M42); target.Set(3, 2, source.M43); target.Set(3, 3, source.M44);
    }
}
