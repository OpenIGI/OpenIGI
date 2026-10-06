using OpenIGI.Rendering.Pipeline.Reconstruction;
using OpenIGI.Rendering.Pipeline.Targets;
using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>DLSS frame generation: frames generated between the rendered ones, at present time.</summary>
/// <remarks>
/// <para>
/// Unlike every other feature here it does not run where it is called. It runs inside the present, from what
/// the frame tagged as living until then: the depth and the motion, the picture without the interface, and
/// the interface by itself — so every rendered frame that wants it tags all four and the camera constants,
/// and the backend keeps them in the layout their tags named until the present has read them.
/// </para>
/// <para>
/// Three states, said apart in the log. What was <em>requested</em> is the setting. What is <em>effective</em>
/// stands down for menus, loading, pauses and cuts — frames with no continuous motion to interpolate along —
/// and while the plugin is loading. What is <em>presenting</em> is what the plugin itself reports after the
/// present. Switching it off for good unloads the plugin and rebuilds the swapchain, because while loaded the
/// host renders into the plugin's images and pays a copy every frame even with it off; a pause only sets it
/// off and keeps its resources, so play resumes without a rebuild.
/// </para>
/// </remarks>
public sealed partial class GraphicsDevice
{
    private FrameGenerationMode _frameGenerationRequested;
    private bool _frameGenerationGameplay;
    private bool _frameGenerationEnabled;
    private int _frameGenerationMax = 1;
    private bool _frameGenerationVsyncSupported;
    private bool _frameGenerationForcesVsyncOff;

    // The constants every feature reads, built once a frame in the temporal stage while the previous camera is
    // still the previous one, and sent once: Streamline refuses a second set for the same frame and viewport.
    private RhiStreamlineConstants? _frameConstants;
    private ulong _constantsSentFrame = ulong.MaxValue;

    /// <inheritdoc/>
    public string FrameGenerationUnavailable =>
        _device.Streamline is { } streamline
            ? streamline.Support(RhiStreamlineFeature.FrameGeneration) is { Supported: false } support ? support.Reason : string.Empty
            : NvidiaUnavailable;

    /// <summary>Gets a value indicating whether frame generation needs this frame's interface drawn apart.</summary>
    private bool FrameGenerationWantsLayer =>
        _frameGenerationRequested != FrameGenerationMode.Off && _device.Streamline is { FrameGenerationLoaded: true };

    /// <inheritdoc/>
    public FrameGenerationStatus SetFrameGeneration(FrameGenerationMode mode, bool gameplay)
    {
        ThrowIfDisposed();

        _frameGenerationRequested = mode;
        _frameGenerationGameplay = gameplay;

        if (mode == FrameGenerationMode.Off)
        {
            UnloadFrameGenerationWhenIdle();
            return Report(new FrameGenerationStatus(mode, false, 1, string.Empty));
        }

        string unavailable = FrameGenerationUnavailable;
        if (unavailable.Length > 0 || _device.Streamline is not { } streamline)
        {
            return Report(new FrameGenerationStatus(mode, false, 1, unavailable));
        }

        if (!streamline.FrameGenerationLoaded)
        {
            // Loaded between frames only: it rebuilds the swapchain the frame would be drawing into.
            if (_inFrame)
            {
                return Report(new FrameGenerationStatus(mode, false, 1, "loading"));
            }

            if (!LoadFrameGeneration(streamline, load: true))
            {
                return Report(new FrameGenerationStatus(mode, false, 1, "the plugin did not load"));
            }
        }

        int multiplier = Math.Clamp((int)mode + 1, 2, _frameGenerationMax + 1);
        string reason =
            !gameplay ? "standing down outside play (menus, loading, cuts)"
            : multiplier < (int)mode + 1 ? $"clamped to {multiplier}x, the most this GPU generates"
            : string.Empty;

        return Report(new FrameGenerationStatus(mode, gameplay && _frameGenerationEnabled, multiplier, reason));
    }

    /// <summary>Sends this frame's camera constants, if nothing has yet.</summary>
    /// <param name="streamline">The NVIDIA features.</param>
    private void EnsureFrameConstants(IRhiStreamline streamline)
    {
        if (_constantsSentFrame == streamline.FrameIndex)
        {
            return;
        }

        RhiStreamlineConstants constants = _frameConstants ?? ReconstructionCamera.Build(ReconstructionFrameState());
        streamline.SetConstants(ReconstructionViewport, constants);
        _constantsSentFrame = streamline.FrameIndex;
    }

    /// <summary>Builds this frame's constants while the previous camera is still the previous one.</summary>
    /// <remarks>Called from the temporal stage, just before it rolls the camera state forward.</remarks>
    private void CaptureFrameConstants()
    {
        _frameConstants = _device.Streamline is null
            ? null
            : ReconstructionCamera.Build(ReconstructionFrameState());
    }

    /// <summary>Tags what frame generation reads at present time, and turns it on or off for this frame.</summary>
    /// <remarks>At the end of the frame, once the interface has been drawn into its layer and before it is composited.</remarks>
    private void TagFrameGeneration()
    {
        if (_device.Streamline is not { FrameGenerationLoaded: true } streamline)
        {
            _frameGenerationEnabled = false;
            return;
        }

        SceneTargetSet? scene = _scene;
        IRhiTexture? hudless = HudlessColour;
        IRhiTexture? layer = InterfaceLayer;

        // Only a frame that is play, drew a scene and drew it apart from its interface has everything the
        // interpolation reads. Any other frame turns it off and keeps its resources.
        bool enabled = _frameGenerationRequested != FrameGenerationMode.Off
            && _frameGenerationGameplay
            && scene is not null
            && hudless is not null
            && layer is not null
            && _frameConstants is not null;

        int framesToGenerate = Math.Clamp((int)_frameGenerationRequested, 1, Math.Max(1, _frameGenerationMax));
        streamline.SetFrameGenerationOptions(
            ReconstructionViewport,
            new RhiFrameGenerationOptions(
                enabled,
                framesToGenerate,
                RenderCVars.FrameGenerationUiRecomposition.Value,
                scene?.Width ?? 0,
                scene?.Height ?? 0));
        _frameGenerationEnabled = enabled;

        if (!enabled)
        {
            return;
        }

        _ = streamline.BeginFrame();
        ClosePass();
        IRhiCommandEncoder encoder = Encoder();
        EnsureFrameConstants(streamline);

        streamline.Tag(
            ReconstructionViewport,
            [
                new(RhiStreamlineBuffer.Depth, scene![SceneTargetId.Depth], RhiStreamlineLifecycle.UntilPresent),
                new(RhiStreamlineBuffer.MotionVectors, scene[SceneTargetId.Velocity], RhiStreamlineLifecycle.UntilPresent),
                new(RhiStreamlineBuffer.HudLessColor, hudless, RhiStreamlineLifecycle.UntilPresent),
                new(RhiStreamlineBuffer.UiColorAndAlpha, layer, RhiStreamlineLifecycle.UntilPresent),
            ],
            encoder);
    }

    /// <summary>Reads what frame generation reports, after the present.</summary>
    private void ReadFrameGenerationState()
    {
        if (_device.Streamline is not { FrameGenerationLoaded: true } streamline)
        {
            return;
        }

        RhiFrameGenerationState state = streamline.FrameGenerationState(ReconstructionViewport);
        _frameGenerationMax = Math.Max(1, state.FramesToGenerateMax);

        if (state.VsyncSupported != _frameGenerationVsyncSupported)
        {
            _frameGenerationVsyncSupported = state.VsyncSupported;
            ApplyEffectiveVSync();
        }

        ReportOnChange(
            "Frame generation present",
            !_frameGenerationEnabled ? "off"
            : state.Ok ? $"on, up to {_frameGenerationMax + 1}x on this GPU"
            : $"not presenting ({state.Status}; smallest side it accepts {state.MinWidthOrHeight})");
    }

    private bool LoadFrameGeneration(IRhiStreamline streamline, bool load)
    {
        bool done = streamline.SetFrameGenerationLoaded(load);

        // Vertical sync with frame generation is a Direct3D 12 arrangement today; on Vulkan the generated
        // frames need a present that does not wait, until the plugin says otherwise.
        _frameGenerationForcesVsyncOff = streamline.FrameGenerationLoaded && !_frameGenerationVsyncSupported;
        ApplyEffectiveVSync();
        return done;
    }

    private void UnloadFrameGenerationWhenIdle()
    {
        _frameGenerationEnabled = false;
        if (_device.Streamline is { FrameGenerationLoaded: true } streamline && !_inFrame)
        {
            _ = LoadFrameGeneration(streamline, load: false);
        }
    }

    private FrameGenerationStatus Report(in FrameGenerationStatus status)
    {
        ReportOnChange(
            "Frame generation",
            status.Requested == FrameGenerationMode.Off ? "off"
            : status.Reason.Length > 0 ? $"{status.Requested} asked for: {status.Reason}"
            : $"{status.Multiplier}x");
        return status;
    }
}
