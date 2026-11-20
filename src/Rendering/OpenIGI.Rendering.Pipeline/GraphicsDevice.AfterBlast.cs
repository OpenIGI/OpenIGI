using OpenIGI.Rendering.Pipeline.Deferred;
using OpenIGI.Rendering.Pipeline.Targets;
using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>A bomb's after-blast over the lit scene. See <see cref="BombAfterBlastPass"/>.</summary>
public sealed partial class GraphicsDevice
{
    private BombAfterBlastPass? _afterBlast;

    /// <summary>Draws the after-blast over the lit scene, where a bomb's front is out this frame.</summary>
    /// <param name="scene">The frame's targets.</param>
    /// <param name="colour">The lit scene.</param>
    /// <param name="post">What the frame asked for.</param>
    /// <param name="exposure">The exposure the resolve will apply this frame.</param>
    /// <returns>The picture, or null where there is no front.</returns>
    /// <remarks>Not for a debug view, which isolates one term of the picture.</remarks>
    private IRhiTextureView? RecordAfterBlast(SceneTargetSet scene, IRhiTextureView colour, in PostProcessSettings post, float exposure)
    {
        if (!post.AfterBlast.IsActive || _debugView is not (DebugView.None or DebugView.Lighting or DebugView.LampLight))
        {
            return null;
        }

        _afterBlast ??= new BombAfterBlastPass(_device, FramesInFlight, _retired);
        (int width, int height) = PostSize(scene);
        using IRhiComputePassEncoder pass = Encoder().BeginComputePass(new RhiComputePassDescriptor(
            _profiler.Time(RenderStage.ToneMapGrade, "afterblast"),
            "afterblast"));
        return _afterBlast.Record(
            pass,
            colour,
            scene.View(SceneTargetId.Depth),
            new BombAfterBlastFrame(width, height, scene.Width, scene.Height, _unprojection, exposure, (int)(_frameCounter % 1024)),
            post.AfterBlast,
            FrameSlot);
    }

    private void DisposeAfterBlast()
    {
        _afterBlast?.Dispose();
        _afterBlast = null;
    }
}
