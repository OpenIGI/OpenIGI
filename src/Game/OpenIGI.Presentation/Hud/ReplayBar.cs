namespace OpenIGI.Presentation.Hud;

/// <summary>What the replay viewer's transport bar shows: where the replay stands in its run, how it plays, and its marks.</summary>
/// <param name="Heading">What is being watched: the mission and the rules it was played by.</param>
/// <param name="Seconds">How far into the run the level stands.</param>
/// <param name="Length">How long the run is.</param>
/// <param name="Paused">Whether the level is held where it stands.</param>
/// <param name="Speed">How fast it plays, as a share of real time.</param>
/// <param name="Camera">Whose view the picture is: the player's eyes, behind them, or the free camera.</param>
/// <param name="Marks">What happened along the run, with when: a guard's first sight, an objective, a kill.</param>
/// <param name="Seeking">Whether the level is being run on to a moment asked for, faster than it plays.</param>
/// <param name="PartedAt">Where the level went another way than the recording, in seconds; <see langword="null"/> while it agrees.</param>
/// <param name="Ending">How the run ended, once the replay has reached its end; <see langword="null"/> before.</param>
public sealed record ReplayBar(
    string Heading,
    double Seconds,
    double Length,
    bool Paused,
    float Speed,
    string Camera,
    IReadOnlyList<(double Seconds, string Text)> Marks,
    bool Seeking = false,
    double? PartedAt = null,
    string? Ending = null)
{
    /// <summary>Gets a value indicating whether the mission's opening cutscene is being run through, before the run is shown.</summary>
    public bool Opening { get; init; }
}
