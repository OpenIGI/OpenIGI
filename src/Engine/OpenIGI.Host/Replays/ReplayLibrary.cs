using System.Globalization;
using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Game.Replays;
using OpenIGI.Game.Scoring;

namespace OpenIGI.Host.Replays;

/// <summary>A replay kept in the library: where its file is, and what its file says.</summary>
/// <param name="Path">The file.</param>
/// <param name="Header">What it is of.</param>
/// <param name="Result">How it ended.</param>
/// <param name="HasGhost">Whether it carries the ghost a race against it shows.</param>
public sealed record LibraryReplay(string Path, ReplayHeader Header, ReplayResult Result, bool HasGhost = false);

/// <summary>
/// The player's own replays, a folder for each mission of each game under the settings (<c>replays/igi1/m03</c>), and what is
/// kept of them: a mission's latest runs, so many as the player asks (<see cref="ReplayCVars.Keep"/>), and whatever the latest,
/// its best completed run by time and by rank at each difficulty under each ruleset -- what the player races and submits.
/// </summary>
/// <param name="root">The folder the library is kept in.</param>
/// <param name="logger">Where a file that cannot be read or written is said.</param>
public sealed class ReplayLibrary(string root, IOpenIgiLogger logger)
{
    /// <summary>Initializes a new instance of the <see cref="ReplayLibrary"/> class, in the player's settings folder.</summary>
    /// <param name="logger">Where a file that cannot be read or written is said.</param>
    public ReplayLibrary(IOpenIgiLogger logger)
        : this(Path.Combine(SettingsPaths.GetUserDirectory(), "replays"), logger)
    {
    }

    /// <summary>The folder a mission's replays are kept in.</summary>
    /// <param name="title">The game, by title id.</param>
    /// <param name="mission">The mission.</param>
    /// <returns>The folder.</returns>
    public string FolderOf(string title, int mission) =>
        Path.Combine(root, string.Concat(title.Split(Path.GetInvalidFileNameChars())), "m" + mission.ToString("00", CultureInfo.InvariantCulture));

    /// <summary>Keeps a replay, then lets go of what is no longer kept beside it.</summary>
    /// <param name="replay">The replay.</param>
    /// <param name="keep">How many of the mission's latest to keep.</param>
    /// <param name="rankOf">The rank a score earns in the mission, for its best by rank; <see langword="null"/> where it has no ranks.</param>
    /// <returns>Where it was kept, or <see langword="null"/> where it could not be written.</returns>
    public string? Save(ReplayFile replay, int keep, Func<MissionScore, int>? rankOf)
    {
        ArgumentNullException.ThrowIfNull(replay);
        string folder = FolderOf(replay.Header.Title, replay.Header.Mission);
        string name = DateTimeOffset.FromUnixTimeMilliseconds(replay.Header.RecordedAt).UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
            + "-" + replay.Result.Outcome.ToString().ToLowerInvariant() + ReplayFile.Extension;
        string path = Path.Combine(folder, name);
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(path, replay.ToBytes());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.Warning("Replays", $"The replay could not be kept at {path}: {ex.Message}");
            return null;
        }

        Prune(folder, keep, rankOf);
        return path;
    }

    /// <summary>The replays kept of a mission, newest first.</summary>
    /// <param name="title">The game, by title id.</param>
    /// <param name="mission">The mission.</param>
    /// <returns>Them; one whose file cannot be read is left out.</returns>
    public IReadOnlyList<LibraryReplay> Of(string title, int mission) => Read(FolderOf(title, mission));

    /// <summary>
    /// A mission's best completed run kept, at a difficulty under a ruleset: the fastest, or the highest rank with the fastest
    /// among those.
    /// </summary>
    /// <param name="title">The game, by title id.</param>
    /// <param name="mission">The mission.</param>
    /// <param name="difficulty">The difficulty.</param>
    /// <param name="ruleset">The ruleset's id.</param>
    /// <param name="rankOf">The rank a score earns, for the best by rank; <see langword="null"/> for the fastest.</param>
    /// <returns>The run, or <see langword="null"/> for none.</returns>
    public LibraryReplay? Best(string title, int mission, int difficulty, string ruleset, Func<MissionScore, int>? rankOf = null) =>
        Completed(Of(title, mission))
            .Where(r => r.Header.Difficulty == difficulty && r.Header.Ruleset == ruleset)
            .OrderByDescending(r => rankOf is null ? 0 : rankOf(r.Result.Score!.Value))
            .ThenBy(static r => r.Result.Score!.Value.Ticks)
            .FirstOrDefault();

    // What is kept of a folder: the latest so many, and each difficulty and ruleset's best by time and by rank.
    private void Prune(string folder, int keep, Func<MissionScore, int>? rankOf)
    {
        IReadOnlyList<LibraryReplay> all = Read(folder);
        HashSet<string> kept = new(StringComparer.OrdinalIgnoreCase);
        foreach (LibraryReplay latest in all.Take(Math.Max(1, keep)))
        {
            kept.Add(latest.Path);
        }

        foreach (IGrouping<(int Difficulty, string Ruleset), LibraryReplay> rules in Completed(all).GroupBy(static r => (r.Header.Difficulty, r.Header.Ruleset)))
        {
            kept.Add(rules.OrderBy(static r => r.Result.Score!.Value.Ticks).First().Path);
            if (rankOf is not null)
            {
                kept.Add(rules.OrderByDescending(r => rankOf(r.Result.Score!.Value)).ThenBy(static r => r.Result.Score!.Value.Ticks).First().Path);
            }
        }

        foreach (LibraryReplay replay in all.Where(r => !kept.Contains(r.Path)))
        {
            try
            {
                File.Delete(replay.Path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.Warning("Replays", $"An old replay could not be let go of at {replay.Path}: {ex.Message}");
            }
        }
    }

    // A folder's readable replays, newest first. A file it cannot read is left where it is: it may be a newer build's.
    private IReadOnlyList<LibraryReplay> Read(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        List<LibraryReplay> replays = [];
        foreach (string path in Directory.EnumerateFiles(folder, "*" + ReplayFile.Extension))
        {
            try
            {
                ReplayFile file = ReplayFile.Read(File.ReadAllBytes(path));
                replays.Add(new LibraryReplay(path, file.Header, file.Result, file.Ghost is { Length: > 0 }));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                logger.Debug("Replays", $"Left a replay unread at {path}: {ex.Message}");
            }
        }

        return [.. replays.OrderByDescending(static r => r.Header.RecordedAt).ThenByDescending(static r => r.Path, StringComparer.Ordinal)];
    }

    private static IEnumerable<LibraryReplay> Completed(IEnumerable<LibraryReplay> replays) =>
        replays.Where(static r => r.Result is { Outcome: ReplayOutcome.Completed, Score: not null });
}
