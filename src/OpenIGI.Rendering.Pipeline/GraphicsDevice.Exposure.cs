using OpenIGI.Rendering.Pipeline.Deferred;
using OpenIGI.Rendering.Pipeline.Targets;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>The eye adaptation: what the frame is exposed by, metered from the lit scene.</summary>
public sealed partial class GraphicsDevice
{
    private AutoExposure? _autoExposure;

    /// <summary>Meters the lit scene and returns what the eye has adapted to, or one where it does not adapt.</summary>
    /// <remarks>
    /// Only with the setting on, only for the picture and the two debug views that are still a shaded scene,
    /// and eased by a fixed sixtieth of a second a frame. A device that cannot build the histogram exposes by
    /// the setting alone, and says so at start-up.
    /// </remarks>
    private float MeterExposure(SceneTargetSet scene, in PostProcessSettings post)
    {
        if (!post.AutoExposure
            || _debugView is not (DebugView.None or DebugView.Lighting or DebugView.LampLight)
            || !AutoExposure.RunsOn(_device))
        {
            return 1f;
        }

        _autoExposure ??= new AutoExposure(_device, FramesInFlight);

        // The histogram this slot carried two frames ago, read before this frame's is recorded over it.
        float adapted = _autoExposure.Adapt(FrameSlot, post.ExposureDeltaSeconds);

        _autoExposure.Record(
            Encoder(),
            FrameSlot,
            scene.View(SceneTargetId.Lit),
            scene.Width,
            scene.Height,
            _profiler.Time(RenderStage.Exposure));

        return adapted;
    }
}
