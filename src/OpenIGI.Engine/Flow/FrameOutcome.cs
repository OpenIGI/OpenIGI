namespace OpenIGI.Engine.Flow;

/// <summary>
/// What one pass through the frame loop actually did.
/// </summary>
/// <remarks>
/// The original leaves this implicit in which branch it took. Naming the outcomes makes the frame
/// schedule assertable: a determinism test can pin the exact tick/render sequence a clock produces,
/// which is the only way the guards in <see cref="OpenIGI.Engine.Time.GameClock"/> can be shown to be
/// doing anything.
/// </remarks>
public enum FrameOutcome
{
    /// <summary>Nothing: the deadline had not passed and this tick was already rendered.</summary>
    Idle,

    /// <summary>One simulation tick over both trees.</summary>
    Tick,

    /// <summary>One render pass over the level tree.</summary>
    Render,

    /// <summary>An off-schedule update and render, because the game is paused or loading.</summary>
    Suspended,

    /// <summary>An off-schedule update with no render, because a movie owns the screen.</summary>
    SuspendedWithoutRender,

    /// <summary>The frame was abandoned by a <see cref="FlowEvent.Quit"/> request.</summary>
    Aborted,
}
