namespace OpenIGI.Frontend;

/// <summary>A replay the player keeps of a mission, as the replays page lists it.</summary>
/// <param name="Id">What the host knows it by, to watch it.</param>
/// <param name="RecordedAt">When the run began.</param>
/// <param name="Difficulty">The difficulty it was played at, by name.</param>
/// <param name="Rules">The rules it was played by, by name: Classic, Modern or Unranked.</param>
/// <param name="Outcome">How it ended, by name: completed, failed, left.</param>
/// <param name="Seconds">How long the run is.</param>
/// <param name="Best">Whether it is the fastest completed run kept at its difficulty under its rules.</param>
/// <param name="Note">Why it may not play as it was played here -- another build recorded it -- or empty.</param>
public sealed record ShellReplay(
    string Id,
    DateTimeOffset RecordedAt,
    string Difficulty,
    string Rules,
    string Outcome,
    double Seconds,
    bool Best,
    string Note);

/// <summary>The player's own replays, for the menu: what it keeps of a mission, and watching one.</summary>
public interface IShellReplays
{
    /// <summary>The replays kept of a mission, newest first.</summary>
    /// <param name="missionId">The mission.</param>
    /// <returns>Them; none for a mission never played.</returns>
    IReadOnlyList<ShellReplay> KeptOf(int missionId);

    /// <summary>Leaves the menu and watches a replay from its start.</summary>
    /// <param name="id">The replay, by its id.</param>
    /// <returns>Why it cannot be watched, or <see langword="null"/> once it is under way.</returns>
    string? Watch(string id);
}
