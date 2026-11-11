namespace OpenIGI.Game.Hosting;

/// <summary>How a mission stands, as the host that ends it reads it: whether its level has decided it, and its messages' hold.</summary>
/// <remarks>
/// The original's <c>LevelFlow</c> decides a level: its <c>Complete</c> and <c>Failed</c> expressions, run each tick. A title
/// answers this from wherever it keeps that task and the status messages beside it.
/// </remarks>
public interface IMissionProgress
{
    /// <summary>Gets whether the level's flow has completed the mission.</summary>
    bool IsComplete { get; }

    /// <summary>Gets whether the level's flow has failed the mission.</summary>
    bool IsFailed { get; }

    /// <summary>Gets or sets whether the mission's objectives and messages are kept off the screen while it still runs them.</summary>
    bool Silenced { get; set; }

    /// <summary>Gets or sets extra seconds every status message is held on screen for.</summary>
    float ExtraMessageHoldSeconds { get; set; }

    /// <summary>Evaluates the objectives, messages and level flow after the tick's level state has advanced.</summary>
    void TickAfterLevelState();
}
