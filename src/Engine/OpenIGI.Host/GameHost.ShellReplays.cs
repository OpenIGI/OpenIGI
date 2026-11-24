using OpenIGI.Frontend;
using OpenIGI.Game.Replays;
using OpenIGI.Host.Replays;

namespace OpenIGI.Host;

/// <content>The player's replays, as the menu lists them and watches one: read from the library, played back over the menu's level.</content>
public sealed partial class GameHost
{
    /// <summary>Puts the menu away for a mission about to come up: shut, the game's own motion blur back.</summary>
    /// <param name="state">The frame loop's state.</param>
    private void LeaveMenuForMission(FrameLoopState state)
    {
        state.Shell?.ClosePauseMenu();
        _menuFlow.MenuMode = false;
        _menuFlow.PauseMenu = false;
        ApplyGameplayMotionBlur();
    }

    // A mission's kept replays as the menu lists them, newest first: the fastest completed at each difficulty and rules marked.
    private IReadOnlyList<ShellReplay> ShellReplaysOf(int missionId)
    {
        string title = _titleRuntime.Title.Id;
        IReadOnlyList<LibraryReplay> kept = Replays.Of(title, missionId);
        HashSet<string> best = new(StringComparer.OrdinalIgnoreCase);
        foreach ((int difficulty, string ruleset) in kept.Select(static r => (r.Header.Difficulty, r.Header.Ruleset)).Distinct())
        {
            if (Replays.Best(title, missionId, difficulty, ruleset) is { } fastest)
            {
                best.Add(fastest.Path);
            }
        }

        return [.. kept.Select(r => new ShellReplay(
            r.Path,
            DateTimeOffset.FromUnixTimeMilliseconds(r.Header.RecordedAt),
            ((Game.Ai.AiDifficulty)Math.Clamp(r.Header.Difficulty, 0, 2)).ToString(),
            Ruleset.FromId(r.Header.Ruleset).Name,
            r.Result.Outcome switch { ReplayOutcome.Completed => "Completed", ReplayOutcome.Failed => "Failed", _ => "Left" },
            (r.Result.EndTick + 1) / 30.0,
            best.Contains(r.Path),
            string.Equals(r.Header.GameVersion, BuildVersion, StringComparison.Ordinal) ? string.Empty : "Another build",
            r.HasGhost))];
    }

    // A replay watched from the menu: read, checked, the menu put away and its mission brought up to play it.
    private string? WatchFromMenu(FrameLoopState state, string path) => FromMenu(state, path, race: false);

    // A replay's ghost raced from the menu: read, checked, the menu put away and its mission brought up to race it.
    private string? RaceFromMenu(FrameLoopState state, string path) => FromMenu(state, path, race: true);

    // A replay read from the library, then watched or raced from the menu.
    private string? FromMenu(FrameLoopState state, string path, bool race)
    {
        if (_loadSequence.IsActive)
        {
            return "a mission is loading";
        }

        ReplayFile replay;
        try
        {
            replay = ReplayFile.Read(File.ReadAllBytes(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return ex.Message.TrimEnd('.');
        }

        if (ReplayProblem(replay) is { } problem)
        {
            return problem;
        }

        if (race && replay.Ghost is not { Length: > 0 })
        {
            return "the replay carries no ghost to race";
        }

        LeaveMenuForMission(state);
        BeginWatchingFramePasses($"{(race ? "Racing" : "Watching")} a replay of mission {replay.Header.Mission}");
        string? started = race ? Race(replay) : PlayReplay(replay);
        ReportWatchedFramePassesIn(5);
        return started;
    }

    /// <summary>The menu's view of the player's replays.</summary>
    /// <param name="host">The host.</param>
    /// <param name="state">The frame loop's state, whose menu watching puts away.</param>
    private sealed class ShellReplays(GameHost host, FrameLoopState state) : IShellReplays
    {
        public IReadOnlyList<ShellReplay> KeptOf(int missionId) => host.ShellReplaysOf(missionId);

        public string? Watch(string id) => host.WatchFromMenu(state, id);

        public string? Race(string id) => host.RaceFromMenu(state, id);
    }
}
