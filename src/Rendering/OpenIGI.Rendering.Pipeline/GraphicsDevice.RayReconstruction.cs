using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Pipeline.Compute;
using OpenIGI.Rendering.Pipeline.Reconstruction;
using OpenIGI.Rendering.Pipeline.Targets;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Temporal;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>DLSS ray reconstruction: reconstruction and denoising in one network, fed the raw traced light.</summary>
/// <remarks>
/// <para>
/// Ray reconstruction replaces the engine's denoisers as well as its resolve, so it changes what the frame
/// traces, not only what reconstructs it. While it runs (<see cref="RawLighting"/>) the sun, the lamps, the
/// flashlight, the indirect light and the reflections each fire their one ray a pixel from white noise and
/// hand it to the shading as it came back: no penumbra filter, no accumulation, no probe grid, no spread.
/// The ambient occlusion stands down, since the gather's own rays darken the corners it would.
/// </para>
/// <para>
/// Beside that colour it reads the depth and motion the super-resolution path reads, three guides of what
/// each pixel is made of (Post/ReconstructionGuides.slang), and how far each reflection's ray travelled
/// with the camera's world transforms -- the alternative its guide offers to specular motion vectors,
/// which this renderer does not make.
/// </para>
/// <para>
/// A frame whose evaluation fails is resolved by the engine from that raw colour -- noisy, once -- and the
/// fault is remembered: the next plan comes back as the engine's own with the reason, and the denoisers
/// take over again. Asking for a different reconstruction clears it.
/// </para>
/// </remarks>
public sealed partial class GraphicsDevice
{
    private const int GuideFloats = 16 + (2 * 4);

    private ComputePass? _guides;
    private ComputeParameters? _guideParameters;
    private IRhiTexture? _diffuseAlbedo;
    private IRhiTexture? _specularAlbedo;
    private IRhiTexture? _normalRoughness;

    // Whether this frame's reflection traced, and so wrote every pixel's hit distance.
    private bool _specularHitDistanceWritten;

    // The origin the world transforms are described about: moved only on a frame whose history is reset
    // anyway, so the network never sees the world jump under a history it is still using.
    private Vector3d _rayReconstructionOrigin;
    private bool _rayReconstructionOriginSet;

    // The frame ray reconstruction last ran in, for the exposure meter.
    private ulong _rayReconstructionFrame = ulong.MaxValue;

    // Why ray reconstruction stopped, once an evaluation failed; empty while it has not.
    private string _rayReconstructionFault = string.Empty;

    /// <inheritdoc/>
    public bool RawLighting =>
        RenderCVars.RayReconstructionRaw.Value
        && _reconstructionPlan.Active == ReconstructionKind.RayReconstruction
        && _rayReconstructionFault.Length == 0
        && _device.RayTracing is not null;

    /// <summary>Gets a value indicating whether ray reconstruction wrote this frame's resolve output.</summary>
    private bool RayReconstructionRanThisFrame => _rayReconstructionFrame == _frameCounter && _temporalResolved;

    /// <summary>Runs ray reconstruction in the engine resolve's place.</summary>
    /// <param name="scene">The frame's targets.</param>
    /// <param name="streamline">The NVIDIA features.</param>
    /// <returns><see langword="true"/> when it ran and wrote the resolve's output.</returns>
    private bool TryRecordRayReconstruction(SceneTargetSet scene, IRhiStreamline streamline)
    {
        if (_rayReconstructionFault.Length > 0 || _device.RayTracing is null)
        {
            return false;
        }

        _ = streamline.BeginFrame();

        IRhiCommandEncoder encoder = Encoder();
        RecordReconstructionInputs(scene, encoder);
        RecordRigGuides(scene, encoder);
        RecordGuides(scene, encoder);

        if (HistoryResetThisFrame || !_rayReconstructionOriginSet)
        {
            _rayReconstructionOrigin = _cameraPositionPrecise;
            _rayReconstructionOriginSet = true;
        }

        (System.Numerics.Matrix4x4 worldToView, System.Numerics.Matrix4x4 viewToWorld) =
            ReconstructionCamera.WorldTransforms(_viewToWorld, _cameraPositionPrecise, _rayReconstructionOrigin);

        EnsureFrameConstants(streamline);
        streamline.SetRayReconstructionOptions(
            ReconstructionViewport,
            _reconstructionMode,
            _reconstructionOutputWidth,
            _reconstructionOutputHeight,
            worldToView,
            viewToWorld);

        // The colour, the guides and the hit distance only until this evaluation; depth and motion until the
        // present, which frame generation reads them at. The hit distance last, and only where the reflection
        // traced: a marched frame wrote none, and an image of stale ones is worse than none.
        Span<RhiStreamlineTag> tags =
        [
            new(RhiStreamlineBuffer.ScalingInputColor, scene[SceneTargetId.Lit], RhiStreamlineLifecycle.UntilEvaluate),
            new(RhiStreamlineBuffer.ScalingOutputColor, scene[SceneTargetId.ResolveOutput], RhiStreamlineLifecycle.UntilEvaluate),
            new(RhiStreamlineBuffer.Depth, scene[SceneTargetId.Depth], RhiStreamlineLifecycle.UntilPresent),
            new(RhiStreamlineBuffer.MotionVectors, scene[SceneTargetId.Velocity], RhiStreamlineLifecycle.UntilPresent),
            new(RhiStreamlineBuffer.DiffuseAlbedo, _diffuseAlbedo!, RhiStreamlineLifecycle.UntilEvaluate),
            new(RhiStreamlineBuffer.SpecularAlbedo, _specularAlbedo!, RhiStreamlineLifecycle.UntilEvaluate),
            new(RhiStreamlineBuffer.NormalRoughness, _normalRoughness!, RhiStreamlineLifecycle.UntilEvaluate),
            new(RhiStreamlineBuffer.ColorBeforeTransparency, _colourBeforeTransparency!, RhiStreamlineLifecycle.UntilEvaluate),
            new(RhiStreamlineBuffer.Exposure, scene[SceneTargetId.Exposure], RhiStreamlineLifecycle.UntilEvaluate),
            new(RhiStreamlineBuffer.BiasCurrentColorHint, scene[SceneTargetId.ReactiveMask], RhiStreamlineLifecycle.UntilEvaluate),
            new(RhiStreamlineBuffer.SpecularHitDistance, scene[SceneTargetId.SpecularHitDistance], RhiStreamlineLifecycle.UntilEvaluate),
        ];

        streamline.Tag(ReconstructionViewport, _specularHitDistanceWritten ? tags : tags[..^1], encoder);

        if (!streamline.Evaluate(RhiStreamlineFeature.RayReconstruction, ReconstructionViewport, encoder, out string? failure))
        {
            _rayReconstructionFault = $"ray reconstruction did not run ({failure})";
            ResetHistory(HistoryResetReason.ReconstructionChanged);
            ReportOnChange("Reconstruction evaluate", $"{_rayReconstructionFault}; the engine resolves from now on");
            return false;
        }

        _rayReconstructionFrame = _frameCounter;
        ShowRayReconstructionInput(scene, encoder);

        ReportOnChange(
            "Reconstruction evaluate",
            _specularHitDistanceWritten
                ? "ray reconstruction ok; guides with the rig's, raw lighting, colour before transparency, reflection hit distance"
                : "ray reconstruction ok; guides with the rig's, raw lighting, colour before transparency, no reflection hit distance (reflections not traced)");
        return true;
    }

    /// <summary>Puts one of the evaluation's inputs where its output went, for <c>r_rr_guide_view</c>.</summary>
    /// <param name="scene">The frame's targets.</param>
    /// <param name="encoder">The frame's encoder.</param>
    private void ShowRayReconstructionInput(SceneTargetSet scene, IRhiCommandEncoder encoder)
    {
        int view = RenderCVars.RayReconstructionGuideView.Value;
        IRhiTexture? shown = view switch
        {
            1 => _diffuseAlbedo,
            2 => _specularAlbedo,
            3 => _normalRoughness,
            4 => _colourBeforeTransparency,
            _ => null,
        };

        IRhiTexture output = scene[SceneTargetId.ResolveOutput];
        if (shown is null || shown.Width != output.Width || shown.Height != output.Height)
        {
            ReportOnChange(
                "Ray reconstruction input view",
                shown is null ? "off" : "needs DLAA: the inputs are at the render size and the output is not");
            return;
        }

        encoder.CopyTextureToTexture(
            new RhiTextureRegion(shown),
            new RhiTextureRegion(output),
            new RhiExtent3D(output.Width, output.Height));
        ReportOnChange("Ray reconstruction input view", $"showing input {view}");
    }

    /// <summary>Writes the three guides from this frame's G-buffer.</summary>
    /// <param name="scene">The frame's targets.</param>
    /// <param name="encoder">The frame's encoder.</param>
    private void RecordGuides(SceneTargetSet scene, IRhiCommandEncoder encoder)
    {
        EnsureGuides(scene.Width, scene.Height);

        _guides ??= new ComputePass(_device, ShaderModuleId.ReconstructionGuides, "reconstruction-guides");
        _guideParameters ??= new ComputeParameters(_device, GuideFloats * sizeof(float), FramesInFlight, "reconstruction-guides");

        Span<float> block = stackalloc float[GuideFloats];
        TemporalParameters.WriteRotation(block, 0, _viewToWorld);
        block[16] = _unprojection.XScale;
        block[17] = _unprojection.YScale;
        block[18] = _unprojection.Near;
        block[19] = _unprojection.Far;
        block[20] = scene.Width;
        block[21] = scene.Height;
        block[22] = 1f / scene.Width;
        block[23] = 1f / scene.Height;

        using IRhiComputePassEncoder pass = encoder.BeginComputePass(new RhiComputePassDescriptor(
            _profiler.Time(RenderStage.TemporalResolve, "reconstruction-guides"),
            "reconstruction-guides"));
        _guides.Bind("uDepth", scene.View(SceneTargetId.Depth));
        _guides.Bind("uBaseColour", scene.View(SceneTargetId.BaseColour));
        _guides.Bind("uNormal", scene.View(SceneTargetId.Normal));
        _guides.Bind("uMaterial", scene.View(SceneTargetId.Material));
        _guides.Bind("uDiffuseAlbedo", _diffuseAlbedo!.View);
        _guides.Bind("uSpecularAlbedo", _specularAlbedo!.View);
        _guides.Bind("uNormalRoughness", _normalRoughness!.View);
        _guides.Bind("uRigBaseColour", _rigBaseColour!.View);
        _guides.Bind("uRigNormal", _rigNormal!.View);
        _guides.Bind("uLit", scene.View(SceneTargetId.Lit));
        _guides.Bind("uBeforeTransparency", _colourBeforeTransparency!.View);
        _guides.Bind("p", _guideParameters.Write<float>(FrameSlot, block));
        _guides.Dispatch(pass, scene.Width, scene.Height);
    }

    /// <summary>Makes the guide images at the render size, the first time and whenever it changes.</summary>
    /// <remarks>Apart from the scene's own targets: only a frame under ray reconstruction pays for them.</remarks>
    private void EnsureGuides(int width, int height)
    {
        if (_diffuseAlbedo is { } existing && existing.Width == width && existing.Height == height)
        {
            return;
        }

        ReleaseGuides();

        const RhiTextureUsage usage = RhiTextureUsage.Storage | RhiTextureUsage.Sampled | RhiTextureUsage.CopySource;
        const RhiTextureUsage drawn = RhiTextureUsage.RenderTarget | RhiTextureUsage.Sampled;
        _diffuseAlbedo = _device.CreateTexture(new RhiTextureDescriptor(width, height, RhiTextureFormat.Rgba16Float, usage, Label: "rr.diffuse-albedo"));
        _specularAlbedo = _device.CreateTexture(new RhiTextureDescriptor(width, height, RhiTextureFormat.Rgba16Float, usage, Label: "rr.specular-albedo"));
        _normalRoughness = _device.CreateTexture(new RhiTextureDescriptor(width, height, RhiTextureFormat.Rgba16Float, usage, Label: "rr.normal-roughness"));
        _rigBaseColour = _device.CreateTexture(new RhiTextureDescriptor(width, height, RhiTextureFormat.Rgba16Float, drawn, Label: "rr.rig-base-colour"));
        _rigNormal = _device.CreateTexture(new RhiTextureDescriptor(width, height, RhiTextureFormat.Rgba16Float, drawn, Label: "rr.rig-normal"));

        // The lit image's own format, which the guide asks for: a precision difference reads as transparency.
        _colourBeforeTransparency = _device.CreateTexture(new RhiTextureDescriptor(
            width,
            height,
            SceneTargetSet.FormatOf(SceneTargetId.Lit),
            usage | drawn | RhiTextureUsage.CopyDestination,
            Label: "rr.colour-before-transparency"));
        _colourBeforeTransparencyFrame = ulong.MaxValue;
    }

    private void ReleaseGuides()
    {
        foreach (IRhiTexture? guide in (IRhiTexture?[])[_diffuseAlbedo, _specularAlbedo, _normalRoughness, _rigBaseColour, _rigNormal, _colourBeforeTransparency])
        {
            if (guide is not null)
            {
                _retired.Retire(guide);
            }
        }

        _diffuseAlbedo = null;
        _specularAlbedo = null;
        _normalRoughness = null;
        _rigBaseColour = null;
        _rigNormal = null;
        _colourBeforeTransparency = null;
    }

    private void DisposeRayReconstruction()
    {
        DisposeRayReconstructionInputs();
        foreach (IRhiTexture? guide in (IRhiTexture?[])[_diffuseAlbedo, _specularAlbedo, _normalRoughness, _rigBaseColour, _rigNormal, _colourBeforeTransparency])
        {
            guide?.Dispose();
        }

        _diffuseAlbedo = null;
        _specularAlbedo = null;
        _normalRoughness = null;
        _rigBaseColour = null;
        _rigNormal = null;
        _colourBeforeTransparency = null;
        _guideParameters?.Dispose();
        _guideParameters = null;
        _guides?.Dispose();
        _guides = null;
    }
}
