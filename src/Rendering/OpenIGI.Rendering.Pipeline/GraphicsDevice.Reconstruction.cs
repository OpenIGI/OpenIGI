using OpenIGI.Rendering.Pipeline.Compute;
using OpenIGI.Rendering.Pipeline.Reconstruction;
using OpenIGI.Rendering.Pipeline.Targets;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>NVIDIA's reconstructions in the engine resolve's place: DLSS, DLAA and ray reconstruction.</summary>
/// <remarks>
/// <para>
/// The frame is planned before it is drawn: the host says what it wants and the output size, the device
/// answers with what will actually run and the render size that needs, and the host sizes the scene, the
/// jitter and the mip bias from that answer (see <see cref="RenderResolutionPolicy"/>). Then, where the engine's
/// temporal resolve would run, the reconstruction runs instead and writes the same output target — so
/// everything after it, from the motion blur to the tone map, is unchanged and does not know which ran.
/// </para>
/// <para>
/// Every way it can fail to run falls back to the engine's own resolve for that frame, and every reason is
/// said once, in a line naming it: no runtime, an adapter that cannot, a size the runtime would not give,
/// an evaluation that was refused. A reconstruction that silently did not run looks exactly like one that
/// did not help.
/// </para>
/// </remarks>
public sealed partial class GraphicsDevice
{
    /// <summary>The viewport the reconstructions run on. One view, so one viewport.</summary>
    private const int ReconstructionViewport = 0;

    private ComputePass? _reactiveMask;
    private ReconstructionPlan _reconstructionPlan = ReconstructionPlan.Engine();
    private RhiDlssMode _reconstructionMode;
    private int _reconstructionOutputWidth;
    private int _reconstructionOutputHeight;
    private Dictionary<ReconstructionKind, string>? _reconstructionSupport;
    private readonly Dictionary<(RhiStreamlineFeature Feature, RhiDlssMode Mode, int Width, int Height), RhiDlssRenderSize> _renderSizes = [];
    private readonly float[] _exposureTexel = new float[1];

    /// <inheritdoc/>
    public IReadOnlyDictionary<ReconstructionKind, string> ReconstructionSupport =>
        _reconstructionSupport ??= BuildReconstructionSupport();

    /// <inheritdoc/>
    public ReconstructionPlan PlanReconstruction(in ReconstructionRequest request)
    {
        ThrowIfDisposed();

        ReconstructionPlan plan = Plan(request, out RhiDlssMode mode);
        bool changed = plan.Active != _reconstructionPlan.Active
            || plan.RenderWidth != _reconstructionPlan.RenderWidth
            || plan.RenderHeight != _reconstructionPlan.RenderHeight
            || mode != _reconstructionMode;

        if (changed)
        {
            // What the history was accumulated under is gone: a different network, a different render size.
            ResetHistory(HistoryResetReason.ReconstructionChanged);

            // A reconstruction switched away from gives its memory back. Not every frame: only when it
            // stops being the one that runs.
            if (_device.Streamline is { } streamline && _reconstructionPlan.Active != plan.Active)
            {
                if (_reconstructionPlan.Active == ReconstructionKind.Dlss)
                {
                    streamline.FreeResources(RhiStreamlineFeature.SuperResolution, ReconstructionViewport);
                }
                else if (_reconstructionPlan.Active == ReconstructionKind.RayReconstruction)
                {
                    streamline.FreeResources(RhiStreamlineFeature.RayReconstruction, ReconstructionViewport);
                }
            }
        }

        _reconstructionPlan = plan;
        _reconstructionMode = mode;
        _reconstructionOutputWidth = request.OutputWidth;
        _reconstructionOutputHeight = request.OutputHeight;

        ReportOnChange(
            "Reconstruction",
            plan.Active switch
            {
                ReconstructionKind.Engine when request.Kind == ReconstructionKind.Engine || !request.Temporal =>
                    request.Temporal ? "engine" : "off (no temporal reconstruction)",
                ReconstructionKind.Engine => $"engine ({request.Kind} was asked for: {plan.Reason})",
                _ => $"{plan.Active} {mode}, rendering {plan.RenderWidth}x{plan.RenderHeight} to {request.OutputWidth}x{request.OutputHeight}",
            });

        return plan;
    }

    /// <inheritdoc/>
    public void SetTextureLodBias(float bias)
    {
        ThrowIfDisposed();

        IReadOnlyList<uint> changed = _textures.Rebias(Math.Clamp(bias, -4f, 4f));
        if (changed.Count == 0)
        {
            return;
        }

        // Every group that captured the old sampler goes, and is rebuilt with the new one on its next draw.
        foreach (uint id in changed)
        {
            _sprites.ForgetTexture(id);
            _materials.Forget(id);
        }

        ReportOnChange("Texture mip bias", $"{_textures.MipLodBias:0.00}");
    }

    /// <summary>Runs an NVIDIA reconstruction in the engine resolve's place, where one is planned and can run.</summary>
    /// <param name="scene">The frame's targets.</param>
    /// <returns><see langword="true"/> when it ran and wrote the resolve's output.</returns>
    private bool TryRecordReconstruction(SceneTargetSet scene)
    {
        if (_reconstructionPlan.Active == ReconstructionKind.Engine || _device.Streamline is not { } streamline)
        {
            return false;
        }

        // The frame planned at one size and captured at another: the first frame after a change, before the
        // scene was rebuilt. The engine resolves that one.
        if (scene.Width != _reconstructionPlan.RenderWidth || scene.Height != _reconstructionPlan.RenderHeight)
        {
            return false;
        }

        if (_reconstructionPlan.Active == ReconstructionKind.RayReconstruction)
        {
            return TryRecordRayReconstruction(scene, streamline);
        }

        // A frame the host did not open for latency still needs a token: everything below names one.
        _ = streamline.BeginFrame();

        IRhiCommandEncoder encoder = Encoder();
        RecordReconstructionInputs(scene, encoder);

        EnsureFrameConstants(streamline);
        streamline.SetSuperResolutionOptions(
            ReconstructionViewport,
            _reconstructionMode,
            _reconstructionOutputWidth,
            _reconstructionOutputHeight);

        // Depth and motion live until the present: frame generation reads them there, long after this
        // evaluation is done with them. Everything else only until this evaluation.
        Span<RhiStreamlineTag> tags =
        [
            new(RhiStreamlineBuffer.ScalingInputColor, scene[SceneTargetId.Lit], RhiStreamlineLifecycle.UntilEvaluate),
            new(RhiStreamlineBuffer.ScalingOutputColor, scene[SceneTargetId.ResolveOutput], RhiStreamlineLifecycle.UntilEvaluate),
            new(RhiStreamlineBuffer.Depth, scene[SceneTargetId.Depth], RhiStreamlineLifecycle.UntilPresent),
            new(RhiStreamlineBuffer.MotionVectors, scene[SceneTargetId.Velocity], RhiStreamlineLifecycle.UntilPresent),
            new(RhiStreamlineBuffer.Exposure, scene[SceneTargetId.Exposure], RhiStreamlineLifecycle.UntilEvaluate),
            new(RhiStreamlineBuffer.BiasCurrentColorHint, scene[SceneTargetId.ReactiveMask], RhiStreamlineLifecycle.UntilEvaluate),
        ];
        streamline.Tag(ReconstructionViewport, tags, encoder);

        if (!streamline.Evaluate(RhiStreamlineFeature.SuperResolution, ReconstructionViewport, encoder, out string? failure))
        {
            ReportOnChange("Reconstruction evaluate", $"DLSS did not run ({failure}); the engine resolved the frame");
            return false;
        }

        ReportOnChange("Reconstruction evaluate", "ok");
        return true;
    }

    /// <summary>Writes the exposure texel and the reactive mask every reconstruction reads beside the colour.</summary>
    /// <param name="scene">The frame's targets.</param>
    /// <param name="encoder">The frame's encoder.</param>
    private void RecordReconstructionInputs(SceneTargetSet scene, IRhiCommandEncoder encoder)
    {
        // The exposure the tone mapper multiplies by: last frame's, which the adaptation moves by a sixtieth
        // of a second's worth at most. It is what lets the network see the image the player will.
        _exposureTexel[0] = _sceneExposure;
        _device.WriteTexture<float>(
            new RhiTextureRegion(scene[SceneTargetId.Exposure]),
            _exposureTexel,
            sizeof(float),
            new RhiExtent3D(1, 1));

        _reactiveMask ??= new ComputePass(_device, ShaderModuleId.ReactiveMask, "reactive-mask");
        using IRhiComputePassEncoder pass = encoder.BeginComputePass(new RhiComputePassDescriptor(
            _profiler.Time(RenderStage.TemporalResolve, "reactive-mask"),
            "reactive-mask"));
        _reactiveMask.Bind("uLit", scene.View(SceneTargetId.Lit));
        _reactiveMask.Bind("uMask", scene.View(SceneTargetId.ReactiveMask));
        _reactiveMask.Dispatch(pass, scene.Width, scene.Height);
    }

    /// <summary>This frame's camera and the previous one's, as the reconstruction constants are built from.</summary>
    private ReconstructionFrame ReconstructionFrameState() => new(
        _unprojection,
        _hasPreviousFrame ? _previousUnprojection : _unprojection,
        _viewToWorld,
        _hasPreviousFrame ? _previousViewToWorld : _viewToWorld,
        _cameraPositionPrecise,
        _hasPreviousFrame ? _previousCameraPosition : _cameraPositionPrecise,
        _taa.JitterX,
        _taa.JitterY,
        Reset: HistoryResetThisFrame || !_hasPreviousFrame);

    private ReconstructionPlan Plan(in ReconstructionRequest request, out RhiDlssMode mode)
    {
        mode = request.Quality switch
        {
            DlssQuality.Dlaa => RhiDlssMode.Dlaa,
            DlssQuality.Quality => RhiDlssMode.Quality,
            DlssQuality.Balanced => RhiDlssMode.Balanced,
            DlssQuality.Performance => RhiDlssMode.Performance,
            DlssQuality.UltraPerformance => RhiDlssMode.UltraPerformance,
            _ => RhiDlssMode.Dlaa,
        };

        // A failed ray reconstruction stays failed until something else is asked for.
        if (request.Kind != ReconstructionKind.RayReconstruction)
        {
            _rayReconstructionFault = string.Empty;
        }

        if (request.Kind == ReconstructionKind.Engine || !request.Temporal)
        {
            return ReconstructionPlan.Engine();
        }

        if (request.Kind == ReconstructionKind.RayReconstruction && _rayReconstructionFault.Length > 0)
        {
            return ReconstructionPlan.Engine(_rayReconstructionFault);
        }

        if (ReconstructionSupport.TryGetValue(request.Kind, out string? unavailable) && unavailable.Length > 0)
        {
            return ReconstructionPlan.Engine(unavailable);
        }

        RhiStreamlineFeature feature = request.Kind == ReconstructionKind.RayReconstruction
            ? RhiStreamlineFeature.RayReconstruction
            : RhiStreamlineFeature.SuperResolution;

        (RhiStreamlineFeature, RhiDlssMode, int, int) key = (feature, mode, request.OutputWidth, request.OutputHeight);
        if (!_renderSizes.TryGetValue(key, out RhiDlssRenderSize size))
        {
            if (_device.Streamline is not { } streamline
                || !streamline.TryGetRenderSize(feature, mode, request.OutputWidth, request.OutputHeight, out size))
            {
                return ReconstructionPlan.Engine($"the runtime gave no render size for {mode} at {request.OutputWidth}x{request.OutputHeight}");
            }

            _renderSizes[key] = size;
        }

        return new ReconstructionPlan(request.Kind, size.Width, size.Height, string.Empty);
    }

    private Dictionary<ReconstructionKind, string> BuildReconstructionSupport()
    {
        Dictionary<ReconstructionKind, string> support = new() { [ReconstructionKind.Engine] = string.Empty };

        if (_device.Streamline is not { } streamline)
        {
            string reason = _device.ShaderTarget == ShaderTarget.Wgsl
                ? "NVIDIA's features run on Vulkan only"
                : "the NVIDIA DLSS runtime is not installed";
            support[ReconstructionKind.Dlss] = reason;
            support[ReconstructionKind.RayReconstruction] = reason;
            return support;
        }

        RhiStreamlineSupport dlss = streamline.Support(RhiStreamlineFeature.SuperResolution);
        support[ReconstructionKind.Dlss] = dlss.Supported ? string.Empty : dlss.Reason;

        RhiStreamlineSupport rayReconstruction = streamline.Support(RhiStreamlineFeature.RayReconstruction);
        support[ReconstructionKind.RayReconstruction] =
            !rayReconstruction.Supported ? rayReconstruction.Reason
            : _device.RayTracing is null ? "it needs hardware ray tracing, which this adapter does not offer"
            : string.Empty;

        return support;
    }

    private void DisposeReconstruction()
    {
        DisposeRayReconstruction();
        _reactiveMask?.Dispose();
        _reactiveMask = null;
    }
}
