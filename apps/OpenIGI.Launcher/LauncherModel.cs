using System.Globalization;

namespace OpenIGI.Launcher;

/// <summary>Where a game stands in the launcher: what the main button offers to do about it.</summary>
internal enum GameState
{
    /// <summary>Installed and current: PLAY.</summary>
    Ready,

    /// <summary>Installed with a newer version out: UPDATE.</summary>
    UpdateAvailable,

    /// <summary>Not on disk: INSTALL.</summary>
    NotInstalled,
}

/// <summary>One game the launcher manages.</summary>
internal sealed class LauncherGame
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required string Tagline { get; init; }

    public GameState State { get; set; }

    public string Version { get; set; } = string.Empty;

    public required string LatestVersion { get; init; }

    public int SizeMegabytes { get; init; }

    public int UpdateMegabytes { get; init; }

    public int ModsEnabled { get; init; }

    /// <summary>Gets the pack names in the content list, for the home page's summary.</summary>
    public IReadOnlyList<string> OptionalPacks { get; init; } = [];

    public int PacksInstalled { get; init; }

    public required PatchNotes Notes { get; init; }
}

/// <summary>A release's notes: what the home page's card shows.</summary>
internal sealed record PatchNotes(string Title, string Date, IReadOnlyList<PatchNote> Items);

/// <summary>One line of release notes.</summary>
internal sealed record PatchNote(PatchNoteKind Kind, string Text);

/// <summary>What a release note is about, which decides its bullet's colour.</summary>
internal enum PatchNoteKind
{
    /// <summary>Something new.</summary>
    New,

    /// <summary>Something that works differently.</summary>
    Change,

    /// <summary>Something that was broken.</summary>
    Fix,
}

/// <summary>
/// The launcher's state. Placeholder data for now: the library, update and mod services it will read
/// from are not written yet, so this holds what they would report and a fake download that ticks along.
/// </summary>
internal sealed class LauncherModel
{
    public LauncherModel()
    {
        Games =
        [
            new LauncherGame
            {
                Id = "igi1",
                Title = "Project I.G.I.",
                Tagline = "I'M GOING IN",
                State = GameState.UpdateAvailable,
                Version = "1.4.2",
                LatestVersion = "1.5.0",
                SizeMegabytes = 1240,
                UpdateMegabytes = 2340,
                ModsEnabled = 6,
                OptionalPacks = ["HD texture pack", "Enhanced audio", "HD cutscenes", "Language packs", "Remastered HUD assets"],
                PacksInstalled = 2,
                Notes = new PatchNotes(
                    "PATCH 1.5.0",
                    "SEP 2026",
                    [
                        new(PatchNoteKind.New, "Remastered HUD with scalable icons and fonts"),
                        new(PatchNoteKind.New, "Third person camera with shoulder and height offsets"),
                        new(PatchNoteKind.Change, "Ballistics simulation now applies to AI fire"),
                        new(PatchNoteKind.Fix, "Trainyard: guards no longer clip through the fence near the tower"),
                    ]),
            },
            new LauncherGame
            {
                Id = "igi2",
                Title = "I.G.I. 2",
                Tagline = "COVERT STRIKE",
                State = GameState.NotInstalled,
                LatestVersion = "1.0.3",
                SizeMegabytes = 2150,
                OptionalPacks = ["HD texture pack", "Enhanced audio"],
                Notes = new PatchNotes(
                    "VERSION 1.0.3",
                    "AUG 2026",
                    [
                        new(PatchNoteKind.New, "Widescreen and high-refresh support"),
                        new(PatchNoteKind.Fix, "Controller dead-zone settings not saving"),
                    ]),
            },
            new LauncherGame
            {
                Id = "editor",
                Title = "Editor",
                Tagline = "MISSION TOOLS",
                State = GameState.Ready,
                Version = "0.9.1",
                LatestVersion = "0.9.1",
                SizeMegabytes = 180,
                OptionalPacks = ["Sample missions", "Scripting SDK & docs"],
                PacksInstalled = 1,
                Notes = new PatchNotes(
                    "VERSION 0.9.1",
                    "JUL 2026",
                    [
                        new(PatchNoteKind.New, "Waypoint graph editor"),
                        new(PatchNoteKind.Change, "Faster terrain preview"),
                        new(PatchNoteKind.Fix, "Undo history lost after saving"),
                    ]),
            },
        ];
    }

    public IReadOnlyList<LauncherGame> Games { get; }

    public int Selected { get; set; }

    public LauncherGame Current => Games[Selected];

    /// <summary>Gets the running download, or <see langword="null"/>.</summary>
    public DownloadJob? Job { get; private set; }

    public int UpdatesAvailable => Games.Count(g => g.State == GameState.UpdateAvailable);

    public static string FormatSize(double megabytes) =>
        megabytes >= 1000
            ? (megabytes / 1000).ToString("0.0", CultureInfo.InvariantCulture) + " GB"
            : Math.Round(megabytes).ToString(CultureInfo.InvariantCulture) + " MB";

    /// <summary>Starts the current game's install or update as a pretend download.</summary>
    public void StartMainAction()
    {
        LauncherGame game = Current;
        if (Job is not null || game.State == GameState.Ready)
        {
            return;
        }

        bool install = game.State == GameState.NotInstalled;
        Job = new DownloadJob(
            game,
            install ? $"Install {game.Title}" : $"{game.Title} {game.LatestVersion}",
            install ? game.SizeMegabytes : game.UpdateMegabytes);
    }

    /// <summary>Advances the pretend download.</summary>
    /// <param name="elapsedSeconds">The real time since the last tick.</param>
    public void Tick(float elapsedSeconds)
    {
        if (Job is not { } job || job.Paused)
        {
            return;
        }

        job.Speed = 150f + (20f * MathF.Sin(job.Done * 0.01f));
        job.Done = MathF.Min(job.Size, job.Done + (job.Speed * elapsedSeconds * 2.2f));
        if (job.Done >= job.Size)
        {
            job.Game.State = GameState.Ready;
            job.Game.Version = job.Game.LatestVersion;
            Job = null;
        }
    }

    /// <summary>Cancels the running download.</summary>
    public void Cancel() => Job = null;
}

/// <summary>A pretend download, standing in for the update service.</summary>
internal sealed class DownloadJob(LauncherGame game, string label, float size)
{
    public LauncherGame Game { get; } = game;

    public string Label { get; } = label;

    public float Size { get; } = size;

    public float Done { get; set; }

    public float Speed { get; set; }

    public bool Paused { get; set; }

    public float Progress => Size <= 0f ? 1f : Done / Size;
}
