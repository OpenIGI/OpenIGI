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
/// <param name="CanRace">Whether it carries the ghost a race against it shows.</param>
public sealed record ShellReplay(
    string Id,
    DateTimeOffset RecordedAt,
    string Difficulty,
    string Rules,
    string Outcome,
    double Seconds,
    bool Best,
    string Note,
    bool CanRace = false);

/// <summary>The record of a mission's board: whose run it is, how long it took, and the run to watch or race.</summary>
/// <param name="Run">The run, by the id the boards know it by.</param>
/// <param name="Name">Who ran it.</param>
/// <param name="Seconds">How long it took.</param>
/// <param name="Rules">The rules of the board, by name.</param>
/// <param name="YourSeconds">The player's own best on the board, or null where they have none there.</param>
/// <param name="YourPlace">Their place on it, or null.</param>
/// <param name="Players">How many the board ranks.</param>
public sealed record ShellRecord(string Run, string Name, double Seconds, string Rules, double? YourSeconds = null, int? YourPlace = null, int Players = 0);

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

    /// <summary>Leaves the menu and races a replay's ghost: its mission played from the start, the run beside the player.</summary>
    /// <param name="id">The replay, by its id.</param>
    /// <returns>Why it cannot be raced, or <see langword="null"/> once the race is under way.</returns>
    string? Race(string id);

    /// <summary>Deletes a kept replay for good.</summary>
    /// <param name="id">The replay, by its id.</param>
    /// <returns>Why it could not be deleted, or <see langword="null"/> once it is gone.</returns>
    string? Delete(string id) => "replays cannot be deleted here";

    /// <summary>
    /// The fastest run on a mission's board at a difficulty, under the rules the player plays by now, where its replay is kept:
    /// asked of the master the first time and taken in on a later frame, so it is null until then, and when there is none.
    /// </summary>
    /// <param name="missionId">The mission.</param>
    /// <param name="difficulty">The difficulty, nought to two.</param>
    /// <returns>The record, or null.</returns>
    ShellRecord? RecordOf(int missionId, int difficulty) => null;

    /// <summary>Leaves the menu and watches or races a board's run, once its replay has come down.</summary>
    /// <param name="run">The run, by the id the boards know it by.</param>
    /// <param name="race">Whether to race its ghost rather than watch it.</param>
    void PlayRecord(string run, bool race)
    {
    }
}
