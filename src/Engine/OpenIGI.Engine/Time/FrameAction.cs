namespace OpenIGI.Engine.Time;

/// <summary>
/// What a single pass through the frame loop is allowed to do.
/// </summary>
/// <remarks>
/// The original decides this inline in <c>Flow_Update</c> (<c>0x402260</c>) with two branches and a
/// short-circuit; naming the three outcomes makes the decision testable on its own.
/// </remarks>
public enum FrameAction
{
    /// <summary>Do nothing: the tick deadline has not passed and this tick has already been rendered.</summary>
    /// <remarks>
    /// The original's <c>goto</c> past both branches. It still processes the pending flow event and
    /// advances the time base, so an idle frame is not a no-op at the frame level — only at the
    /// tree-walk level.
    /// </remarks>
    Idle,

    /// <summary>Run one simulation tick over both task trees.</summary>
    Tick,

    /// <summary>Render one frame from the level tree.</summary>
    Render,
}
