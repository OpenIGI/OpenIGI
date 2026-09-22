using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Pipeline.Compute;
using OpenIGI.Rendering.Pipeline.Deferred;
using OpenIGI.Rendering.Pipeline.Targets;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Temporal;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>The temporal resolve: this frame's picture and the history of the ones before it, made one.</summary>
/// <remarks>
/// <para>
/// After the forward pass, over the finished lit image: the camera's motion derived from the depth, the
/// eight passes of the resolve, and a copy of this frame's depth for the next one to measure against.
/// The resolve then reads the resolved image rather than the raw one.
/// </para>
/// <para>
/// What moves under its own power — a soldier, a door, the held weapon — is not rasterised into the
/// motion field yet, so the camera's motion stands in for it. That is the difference between this and
/// the Vulkan original, and it shows as a trail behind a figure crossing the view.
/// </para>
/// </remarks>
public sealed partial class GraphicsDevice
{
    /// <summary>The vertical jitter's sign once it reaches history space.</summary>
    /// <remarks>
    /// The game hands over a jitter in OpenGL's clip convention. Both renderers displace the rasterised
    /// image UP the target for a positive one — the legacy one by flipping Y in its vertex stages, this
    /// one by flipping the viewport — so the history looks for that sample the other way, as the Vulkan
    /// device's <c>VerticalJitterSign</c> says.
    /// </remarks>
    private const float VerticalJitterSign = -1f;

    private ScreenSpacePasses? _screenPasses;
    private TemporalResolve? _temporal;
    private ComputePass? _copyDepth;

    private TemporalAaSettings _taa = TemporalAaSettings.Off;
    private Vector3d _cameraPositionPrecise;
    private Vector3d _previousCameraPosition;
    private Matrix3f _previousViewToWorld = Matrix3f.Identity;
    private DepthUnprojection _previousUnprojection = new(1f, 1f, 0.1f, 1000f);
    private float _previousJitterX;
    private float _previousJitterY;
    private bool _hasPreviousFrame;
    private bool _temporalResolved;
    private bool _occlusionRan;
    private AmbientOcclusionSettings _occlusionSettings = AmbientOcclusionSettings.Off;

    /// <summary>Builds the temporal passes, once, with the rest of the deferred frame.</summary>
    /// <remarks>
    /// The screen-space passes and the depth copy on every backend, because the occlusion reads them
    /// both; the resolve's chain only where the backend can run all of it. Elsewhere <c>r_taa</c> is named
    /// among the settings this device does not act on, and the resolve reads the lit image.
    /// </remarks>
    private void EnsureTemporal()
    {
        _screenPasses ??= new ScreenSpacePasses(_device, FramesInFlight);
        _copyDepth ??= new ComputePass(_device, ShaderModuleId.CopyDepth, "copy-depth");

        if (TemporalResolve.RunsOn(_device))
        {
            _temporal ??= new TemporalResolve(_device, FramesInFlight);
        }
    }

    /// <summary>Records the ambient occlusion over the finished depth, before the shading reads it.</summary>
    /// <remarks>
    /// Measured against the previous frame's depth and occlusion for its history, which is why it runs
    /// before the frame state moves on. The shading is told whether it ran, and reads nothing it did not.
    /// </remarks>
    private void RecordOcclusion(SceneTargetSet scene)
    {
        _occlusionRan = false;

        if (!_occlusionSettings.Enabled || _screenPasses is not { } screen)
        {
            return;
        }

        using (IRhiComputePassEncoder pass = Encoder().BeginComputePass(new RhiComputePassDescriptor(
            _profiler.Time(RenderStage.AmbientOcclusionSearch),
            "occlusion")))
        {
            screen.RecordOcclusion(
                pass,
                scene,
                TemporalState(scene),
                _occlusionSettings,
                (long)_frameCounter,
                FrameSlot,
                _hasPreviousFrame);
        }

        _occlusionRan = true;
    }

    /// <summary>This frame and the one before it, as the temporal passes measure them.</summary>
    private TemporalFrameState TemporalState(SceneTargetSet scene)
    {
        float jitterY = _taa.JitterY * VerticalJitterSign;

        // The first frame has no previous one, so it is its own: nothing moved.
        return new TemporalFrameState(
            _viewToWorld,
            _hasPreviousFrame ? _previousViewToWorld : _viewToWorld,
            _hasPreviousFrame
                ? new Vector3f(
                    (float)(_cameraPositionPrecise.X - _previousCameraPosition.X),
                    (float)(_cameraPositionPrecise.Y - _previousCameraPosition.Y),
                    (float)(_cameraPositionPrecise.Z - _previousCameraPosition.Z))
                : Vector3f.Zero,
            _unprojection,
            _hasPreviousFrame ? _previousUnprojection : _unprojection,
            _taa.JitterX,
            jitterY,
            _hasPreviousFrame ? _previousJitterX : _taa.JitterX,
            _hasPreviousFrame ? _previousJitterY : jitterY,
            scene.Width,
            scene.Height);
    }

    /// <summary>Records the motion, the resolve and the depth copy, once the lit image is finished.</summary>
    /// <remarks>
    /// Everything temporal measures against the previous frame, which moves on exactly once per captured
    /// frame, here, whether or not the resolve itself was asked for — so that turning it on mid-mission
    /// does not measure the first frame against the start of the level.
    /// </remarks>
    private void RecordTemporal()
    {
        _temporalResolved = false;

        if (_scene is not { } scene || _screenPasses is not { } screen || _copyDepth is not { } copy)
        {
            return;
        }

        TemporalFrameState state = TemporalState(scene);
        TemporalResolveSettings settings = TemporalResolveSettings.ForFrame(
            _taa,
            scene.Width,
            CurrentMode.Width,
            _debugView);

        ClosePass();
        CloseScenePass();

        using (IRhiComputePassEncoder pass = Encoder().BeginComputePass(new RhiComputePassDescriptor(
            _profiler.Time(RenderStage.TemporalResolve),
            "temporal")))
        {
            if (settings.Enabled && _temporal is { } temporal)
            {
                screen.RecordVelocity(pass, scene, state, FrameSlot);
                temporal.Record(pass, scene, state, settings, FrameSlot);
                _temporalResolved = true;
            }

            copy.Bind("uDepth", scene.View(SceneTargetId.Depth));
            copy.Bind("uPreviousDepth", scene.View(SceneTargetId.PreviousDepth));
            copy.Dispatch(pass, scene.Width, scene.Height);
        }

        _previousViewToWorld = _viewToWorld;
        _previousCameraPosition = _cameraPositionPrecise;
        _previousUnprojection = _unprojection;
        _previousJitterX = state.JitterX;
        _previousJitterY = state.JitterY;
        _hasPreviousFrame = true;
    }

    /// <summary>The image the resolve reads: the temporal output when the resolve ran, the lit one otherwise.</summary>
    private IRhiTextureView ResolvedScene(SceneTargetSet scene) =>
        scene.View(_temporalResolved ? SceneTargetId.ResolveOutput : SceneTargetId.Lit);

    private void DisposeTemporal()
    {
        _copyDepth?.Dispose();
        _copyDepth = null;
        _temporal?.Dispose();
        _temporal = null;
        _screenPasses?.Dispose();
        _screenPasses = null;
    }
}
