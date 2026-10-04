using OpenIGI.Rendering.Pipeline.Deferred;
using OpenIGI.Rendering.Pipeline.Targets;
using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>The camera shutter, between the temporal resolve and the lens.</summary>
public sealed partial class GraphicsDevice
{
    private MotionBlur? _motionBlur;

    /// <summary>Smears the resolved frame along its motion, where the setting asks for any.</summary>
    /// <returns>The smeared colour, or null where the frame is not blurred.</returns>
    /// <remarks>
    /// Some amount asked for, and a picture rather than a debug view -- a view isolating one term is not a
    /// moving photograph -- save the two views that are still a shaded scene.
    /// </remarks>
    private IRhiTextureView? RecordMotionBlur(SceneTargetSet scene, in PostProcessSettings post)
    {
        if (post.MotionBlur <= 0.001f || _debugView is not (DebugView.None or DebugView.Lighting or DebugView.LampLight))
        {
            return null;
        }

        _motionBlur ??= new MotionBlur(_device, FramesInFlight, _retired);

        using IRhiComputePassEncoder pass = Encoder().BeginComputePass(new RhiComputePassDescriptor(
            _profiler.Time(RenderStage.CameraEffects, "motion-blur"),
            "motionblur"));

        // At the size of the picture it smears, which the resolve has already reconstructed to the
        // presented size under a render scale; the motion and the depth are read by position, not texel.
        (int width, int height) = PostSize(scene);

        return _motionBlur.Record(
            pass,
            FrameSlot,
            ResolvedScene(scene),
            scene.View(SceneTargetId.Velocity),
            scene.View(SceneTargetId.Depth),
            width,
            height,
            post,
            _unprojection);
    }
}
