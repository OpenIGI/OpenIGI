using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using OpenIGI.Core.Titles;
using OpenIGI.Launcher.Services;
using OpenIGI.Releases;

namespace OpenIGI.Launcher;

/// <summary>Where a game stands in the launcher: what the main button offers to do about it.</summary>
internal enum GameState
{
    /// <summary>Installed, current, and its game folder found: PLAY.</summary>
    Ready,

    /// <summary>Installed with a newer version out: UPDATE.</summary>
    UpdateAvailable,

    /// <summary>Not on disk: INSTALL.</summary>
    NotInstalled,

    /// <summary>Installed, but the original game's folder is not known: SET FOLDER.</summary>
    NeedsGameFolder,

    /// <summary>Installed files failed a verify: REPAIR.</summary>
    Damaged,

    /// <summary>Running now.</summary>
    Playing,

    /// <summary>Not released yet: nothing to do.</summary>
    Unavailable,
}

/// <summary>One game the launcher manages.</summary>
/// <remarks>Both titles run on the same OpenIGI build, so they share its install and its updates.</remarks>
internal sealed class LauncherGame
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required string Tagline { get; init; }

    /// <summary>Gets the original game this entry runs, or <see langword="null"/> for one with no build yet.</summary>
    public GameTitle? Retail { get; init; }

    /// <summary>Gets whether this entry can be installed and played yet.</summary>
    /// <remarks>
    /// An entry that is not is still listed, so players can see what is coming, but cannot be chosen: it never
    /// offers an install, an update or PLAY, and never counts towards the update badge.
    /// </remarks>
    public bool Released { get; init; }

    public GameState State { get; set; }

    public string Version { get; set; } = string.Empty;

    public string LatestVersion { get; set; } = string.Empty;

    public int SizeMegabytes { get; set; }

    public int UpdateMegabytes { get; set; }

    public int ModsEnabled { get; init; }

    /// <summary>Gets the pack names in the content list, for the home page's summary.</summary>
    public IReadOnlyList<string> OptionalPacks { get; init; } = [];

    public int PacksInstalled { get; init; }

    public PatchNotes? Notes { get; set; }

    /// <summary>Gets or sets the address of the newest release's full notes.</summary>
    public string? NotesUrl { get; set; }
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
/// The launcher's state: what the update service offers, what is installed, and what is being downloaded.
/// </summary>
/// <remarks>
/// Everything slow (asking the service, downloading, unpacking, verifying) runs off the frame loop; its
/// results are queued and applied in <see cref="Tick"/>, so the pages only ever read state the loop owns.
/// The service is asked once a minute (see <see cref="PollSchedule"/>), when the window regains focus, and
/// when the channel changes. A release that comes out while the launcher is open turns the main button into
/// UPDATE, or the footer's into a restart for a launcher release, and is announced once by a desktop
/// notification when the launcher is in the background.
/// </remarks>
internal sealed class LauncherModel : IDisposable
{
    private const int NotesShown = 6;

    private readonly ConcurrentQueue<Action> _results = new();
    private readonly HttpClient _http;
    private readonly ReleaseFeedClient _feedClient;
    private BuildInstaller _installer;
    private readonly LauncherUpdater _selfUpdater;
    private readonly string _settingsPath = Path.Combine(LauncherPaths.ConfigDirectory, "settings.json");

    private InstalledBuild? _installed;
    private ChannelFeed? _feed;
    private ReleaseManifest? _latest;
    private bool _checking;
    private bool _damaged;
    private readonly PollSchedule _schedule = new();
    private Process? _running;
    private string? _stagingLauncher;
    private CancellationTokenSource? _download;

    public LauncherModel()
    {
        Settings = LauncherSettings.Load(_settingsPath);
        _http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"OpenIGI-Launcher/{LauncherUpdater.OwnVersion}");
        _feedClient = new ReleaseFeedClient(_http);
        _installer = new BuildInstaller(_http, Path.Combine(Settings.Library, "openigi"));
        _selfUpdater = new LauncherUpdater(new BuildInstaller(_http, Path.Combine(LauncherPaths.ConfigDirectory, "launcher-update")));
        _installed = _installer.Current();
        LauncherUpdater.CleanUp();

        Games =
        [
            new LauncherGame { Id = "igi1", Title = "Project I.G.I.", Tagline = "I'M GOING IN", Retail = GameTitle.Igi1, Released = true },
            new LauncherGame { Id = "igi2", Title = "I.G.I. 2", Tagline = "COVERT STRIKE", Retail = GameTitle.Igi2, State = GameState.Unavailable },
            new LauncherGame { Id = "editor", Title = "Editor", Tagline = "MISSION TOOLS", State = GameState.Unavailable },
        ];
        Recompute();
    }

    public IReadOnlyList<LauncherGame> Games { get; }

    public int Selected { get; set; }

    public LauncherGame Current => Games[Selected];

    public LauncherSettings Settings { get; }

    /// <summary>Gets the running download, or <see langword="null"/>.</summary>
    public DownloadJob? Job { get; private set; }

    /// <summary>Gets what went wrong last (the service unreachable, a download failing), for the footer.</summary>
    public string? Problem { get; private set; }

    /// <summary>Gets the launcher release that is downloaded and waiting for a restart.</summary>
    public string? LauncherUpdateReady { get; private set; }

    /// <summary>Gets a number that changes whenever the pages need re-reading.</summary>
    public int Revision { get; private set; }

    /// <summary>Gets whether the launcher asked to close itself (after starting the game).</summary>
    public bool CloseRequested { get; private set; }

    /// <summary>Gets or sets whether the launcher's window has focus; a notification is only for when it has not.</summary>
    public bool WindowFocused { get; set; } = true;

    /// <summary>Gets or sets what shows a desktop notification (title, message); none until the window gives one.</summary>
    public Action<string, string>? Notify { get; set; }

    public int UpdatesAvailable => Games.Any(static g => g.State == GameState.UpdateAvailable) ? 1 : 0;

    public static string FormatSize(double megabytes) =>
        megabytes >= 1000
            ? (megabytes / 1000).ToString("0.0", CultureInfo.InvariantCulture) + " GB"
            : Math.Round(megabytes).ToString(CultureInfo.InvariantCulture) + " MB";

    /// <summary>Asks the update service again as soon as the spacing between checks allows.</summary>
    public void CheckSoon() => _schedule.Soon(DateTime.UtcNow);

    /// <summary>Does what the selected game's main button says.</summary>
    /// <param name="openSettings">Shows the settings, for a game whose folder is still to be chosen.</param>
    public void StartMainAction(Action openSettings)
    {
        ArgumentNullException.ThrowIfNull(openSettings);
        LauncherGame game = Current;
        switch (game.State)
        {
            case GameState.Ready:
                Play(game);
                break;
            case GameState.NeedsGameFolder:
                openSettings();
                break;
            case GameState.NotInstalled or GameState.UpdateAvailable or GameState.Damaged:
                StartDownload();
                break;
        }
    }

    /// <summary>Pauses or resumes the running download; a resume picks up where the download stopped.</summary>
    public void TogglePause()
    {
        if (Job is not { } job)
        {
            return;
        }

        if (job.Paused)
        {
            StartDownload();
        }
        else
        {
            job.Paused = true;
            _download?.Cancel();
        }
    }

    /// <summary>Cancels the running download.</summary>
    public void Cancel()
    {
        _download?.Cancel();
        Job = null;
        Recompute();
    }

    /// <summary>Checks the installed build against the hashes it was installed with.</summary>
    public void Verify()
    {
        if (_installed is not { } build || Job is not null)
        {
            return;
        }

        Problem = "Verifying the installed files…";
        Changed();
        _ = Task.Run(() =>
        {
            IReadOnlyList<string> damaged = BuildInstaller.Verify(build);
            _results.Enqueue(() =>
            {
                _damaged = damaged.Count > 0;
                Problem = _damaged ? $"{damaged.Count} file(s) are missing or changed: repair to download them again." : "Every installed file is intact.";
                Recompute();
            });
        });
    }

    /// <summary>Opens the installed build's folder in the system's file manager.</summary>
    public void OpenFolder() => Open(_installed?.Folder ?? Settings.Library);

    /// <summary>Opens the newest release's notes in the browser.</summary>
    public void OpenNotes()
    {
        if (Current.NotesUrl is { } url)
        {
            Open(url);
        }
    }

    /// <summary>Swaps in the downloaded launcher and restarts into it.</summary>
    /// <returns>Whether the launcher should now exit.</returns>
    public bool RestartIntoUpdate() => _selfUpdater.ApplyAndRestart();

    /// <summary>Records a settings change, saves it, and re-reads what depends on it.</summary>
    /// <param name="change">The change.</param>
    public void ChangeSettings(Action<LauncherSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        ReleaseChannel channel = Settings.Channel;
        string library = Settings.Library;
        change(Settings);
        try
        {
            Settings.Save(_settingsPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Problem = $"Settings could not be saved: {ex.Message}";
        }

        if (Settings.Channel != channel)
        {
            _feed = null;
            CheckSoon();
        }

        // A different library is a different install: whatever is (or is not) installed there now counts.
        if (!string.Equals(Settings.Library, library, StringComparison.Ordinal) && Job is null && !string.IsNullOrWhiteSpace(Settings.Library))
        {
            _installer = new BuildInstaller(_http, Path.Combine(Settings.Library.Trim(), "openigi"));
            _installed = _installer.Current();
            CheckSoon();
        }

        Recompute();
    }

    /// <summary>Applies finished background work and starts what is due.</summary>
    /// <param name="elapsedSeconds">The real time since the last tick.</param>
    public void Tick(float elapsedSeconds)
    {
        while (_results.TryDequeue(out Action? result))
        {
            result();
        }

        if (_running is { HasExited: true })
        {
            _running.Dispose();
            _running = null;
            Recompute();
        }

        Job?.Sample(elapsedSeconds);

        if (!_checking && DateTime.UtcNow >= _schedule.Next)
        {
            Check();
        }
    }

    public void Dispose()
    {
        _download?.Cancel();
        _download?.Dispose();
        _running?.Dispose();
        _http.Dispose();
    }

    private void Check()
    {
        _checking = true;
        _schedule.Started(DateTime.UtcNow);
        ReleaseChannel channel = Settings.Channel;
        string? since = _installed?.Version;

        _ = Task.Run(async () =>
        {
            try
            {
                using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));
                ChannelFeed feed = await _feedClient.FetchAsync(ReleaseFeedClient.DefaultService, channel, since, timeout.Token).ConfigureAwait(false);
                _results.Enqueue(() =>
                {
                    _checking = false;
                    _schedule.Finished(DateTime.UtcNow, succeeded: true);
                    if (channel != Settings.Channel)
                    {
                        return;
                    }

                    if (Problem?.StartsWith("The update service", StringComparison.Ordinal) == true)
                    {
                        Problem = null;
                    }

                    _feed = feed;
                    Recompute();
                    AnnounceGameUpdate();
                    MaybeStartAutomatically();
                    MaybeStageLauncher();
                });
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                _results.Enqueue(() =>
                {
                    _checking = false;
                    _schedule.Finished(DateTime.UtcNow, succeeded: false);
                    int minutes = Math.Max(1, (int)Math.Round((_schedule.Next - DateTime.UtcNow).TotalMinutes));
                    Problem = $"The update service cannot be reached; trying again in {minutes} minute{(minutes == 1 ? string.Empty : "s")}.";
                    Changed();
                });
            }
        });
    }

    // The smaller build when this machine has the .NET runtime it needs, the self-contained one otherwise (and
    // whenever a release has no smaller build for this platform).
    private static ReleaseAsset? GameAsset(ReleaseManifest? release) =>
        release is null ? null
        : (DotnetRuntime.IsInstalled() ? release.Find(ReleaseComponent.Game, LauncherPaths.Runtime, selfContained: false) : null)
          ?? release.Find(ReleaseComponent.Game, LauncherPaths.Runtime);

    private void StartDownload()
    {
        if (_latest is not { } release || GameAsset(release) is not { } asset)
        {
            Problem = _latest is null
                ? "No release is available yet."
                : $"This release has no build for {LauncherPaths.Runtime}.";
            Changed();
            return;
        }

        string version = release.Version;
        DownloadJob job = Job is { Paused: true } paused && paused.Version == version
            ? paused
            : new DownloadJob(Current, version, _installed is null ? $"Install OpenIGI {version}" : $"OpenIGI {version}", asset.Size);
        job.Paused = false;
        Job = job;
        Problem = null;

        _download?.Dispose();
        _download = new CancellationTokenSource();
        CancellationToken token = _download.Token;
        Progress<InstallProgress> progress = new(job.Report);

        _ = Task.Run(async () =>
        {
            try
            {
                InstalledBuild build = await _installer.InstallAsync(version, asset, GameStarter.ExecutableName, progress, token).ConfigureAwait(false);
                _results.Enqueue(() =>
                {
                    _installed = build;
                    _damaged = false;
                    Job = null;
                    CheckSoon();
                    Recompute();
                });
            }
            catch (OperationCanceledException)
            {
                // Paused or cancelled: the partial download stays for the resume.
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException)
            {
                _results.Enqueue(() =>
                {
                    Job = null;
                    Problem = $"The download failed: {ex.Message}";
                    Recompute();
                });
            }
        });
        Changed();
    }

    private void Play(LauncherGame game)
    {
        if (_installed is not { } build || game.Retail is not { } title || _running is not null)
        {
            return;
        }

        string? folder = GameStarter.Locate(title, Settings.GameFolders.GetValueOrDefault(title.Id));
        if (folder is null)
        {
            return;
        }

        try
        {
            _running = GameStarter.Start(build.Folder, GameStarter.Arguments(title, folder, Settings.LaunchArguments));
            Problem = null;
            if (Settings.CloseOnLaunch)
            {
                CloseRequested = true;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Problem = $"The game did not start: {ex.Message}";
        }

        Recompute();
    }

    private void MaybeStartAutomatically()
    {
        if (Settings.AutomaticUpdates && Job is null && _running is null && _installed is not null
            && Games.Any(static g => g.State == GameState.UpdateAvailable))
        {
            StartDownload();
        }
    }

    private void MaybeStageLauncher()
    {
        // The launcher has one channel, stable, whichever channel the games follow: it updates to the launcher
        // of the newest stable release, never to a beta's.
        if (!LauncherUpdater.CanUpdateSelf || _feed?.Launcher is not { } latest || _stagingLauncher == latest.Version
            || latest.ParsedVersion <= LauncherUpdater.OwnVersion
            || latest.Find(ReleaseComponent.Launcher, LauncherPaths.Runtime) is not { } asset)
        {
            return;
        }

        _stagingLauncher = latest.Version;
        _ = Task.Run(async () =>
        {
            try
            {
                await _selfUpdater.StageAsync(latest.Version, asset, CancellationToken.None).ConfigureAwait(false);
                _results.Enqueue(() =>
                {
                    LauncherUpdateReady = latest.Version;
                    Announce("launcher", latest.Version, "OpenIGI Launcher update ready",
                        $"Launcher {latest.Version} is downloaded. Restart the launcher to update it.");
                    Changed();
                });
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException)
            {
                _results.Enqueue(() => _stagingLauncher = null);
            }
        });
    }

    // Re-derives every game's state from the install, the feed, and what is running.
    private void Recompute()
    {
        _latest = _feed?.Latest;
        int installedMegabytes = _installed is null ? 0 : (int)(_installed.Files.Sum(static f => f.Size) / 1_000_000);
        int downloadMegabytes = (int)((GameAsset(_latest)?.Size ?? 0) / 1_000_000);

        // A build that needs the .NET runtime cannot start once the runtime is gone: repairing it fetches the
        // self-contained one.
        bool runtimeGone = _installed is { SelfContained: false } && !DotnetRuntime.IsInstalled();
        bool newer = _latest is not null && (_installed is null || _latest.ParsedVersion > ReleaseVersion.Parse(_installed.Version));

        foreach (LauncherGame game in Games)
        {
            if (!game.Released || game.Retail is not { } title)
            {
                game.State = GameState.Unavailable;
                continue;
            }

            game.Version = _installed?.Version ?? string.Empty;
            game.LatestVersion = _latest?.Version ?? game.Version;
            game.SizeMegabytes = _installed is null ? downloadMegabytes : installedMegabytes;
            game.UpdateMegabytes = downloadMegabytes;
            game.Notes = BuildNotes();
            game.NotesUrl = _latest is null ? null : $"https://github.com/OpenIGI/OpenIGI/releases/tag/v{_latest.Version}";
            game.State = _running is not null ? GameState.Playing
                : _installed is null ? GameState.NotInstalled
                : _damaged || runtimeGone ? GameState.Damaged
                : newer ? GameState.UpdateAvailable
                : GameStarter.Locate(title, Settings.GameFolders.GetValueOrDefault(title.Id)) is null ? GameState.NeedsGameFolder
                : GameState.Ready;
        }

        Changed();
    }

    // The changes the player is about to receive (every release since theirs, newest first), or the newest
    // release's own when they are up to date.
    private PatchNotes? BuildNotes()
    {
        if (_latest is not { } latest)
        {
            return null;
        }

        IReadOnlyList<ReleaseManifest> releases = _feed!.Releases.Count > 0 ? _feed.Releases : [latest];
        List<PatchNote> items = [];
        foreach (ReleaseManifest release in releases)
        {
            foreach (ReleaseNoteSection section in release.Notes)
            {
                PatchNoteKind kind = section.Title switch
                {
                    "New" => PatchNoteKind.New,
                    "Fixes" => PatchNoteKind.Fix,
                    _ => PatchNoteKind.Change,
                };
                items.AddRange(section.Entries.Select(entry => new PatchNote(kind, entry.Text)));
            }
        }

        // New things first, then fixes, then the rest: the card shows only the first few.
        List<PatchNote> shown = [.. items.OrderBy(static n => n.Kind switch { PatchNoteKind.New => 0, PatchNoteKind.Fix => 1, _ => 2 }).Take(NotesShown)];
        if (shown.Count == 0)
        {
            shown.Add(new PatchNote(PatchNoteKind.Change, "Maintenance release"));
        }

        string title = (latest.Channel == ReleaseChannel.Beta ? "BETA " : "PATCH ") + latest.Version.ToUpperInvariant();
        string date = latest.Published.ToString("MMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant();
        return new PatchNotes(title, date, shown);
    }

    // A game update the player has not been told about: told once, by notification if the window is not in
    // front (if it is, the UPDATE button already says it).
    private void AnnounceGameUpdate()
    {
        if (_latest is { } latest && Games.Any(static g => g.State == GameState.UpdateAvailable))
        {
            Announce("game", latest.Version, $"OpenIGI {latest.Version} is out",
                Settings.AutomaticUpdates && _running is null
                    ? "Downloading it now; it will be ready to play when the launcher says so."
                    : "Open the launcher to update.");
        }
    }

    private void Announce(string key, string version, string title, string message)
    {
        if (!UpdateNotices.ShouldAnnounce(Settings, key, version))
        {
            return;
        }

        try
        {
            Settings.Save(_settingsPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not remembered: at worst the same release is announced again after a restart.
        }

        if (!WindowFocused)
        {
            Notify?.Invoke(title, message);
        }
    }

    private void Changed() => Revision++;

    private static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Nothing registered to open it; there is nothing more the launcher can do.
        }
    }
}

/// <summary>A download of a build, as the home page and the footer show it.</summary>
internal sealed class DownloadJob(LauncherGame game, string version, string label, long bytes)
{
    private long _done;
    private int _stage;
    private long _lastDone;
    private float _window;

    public LauncherGame Game { get; } = game;

    public string Version { get; } = version;

    public string Label { get; } = label;

    /// <summary>Gets the archive's size, in megabytes.</summary>
    public float Size { get; } = bytes / 1_000_000f;

    /// <summary>Gets how much has arrived, in megabytes.</summary>
    public float Done => Interlocked.Read(ref _done) / 1_000_000f;

    /// <summary>Gets the recent download speed, in megabytes a second.</summary>
    public float Speed { get; private set; }

    public bool Paused { get; set; }

    /// <summary>Gets what the install is doing now.</summary>
    public InstallStage Stage => (InstallStage)Volatile.Read(ref _stage);

    public float Progress => Size <= 0f ? 1f : Math.Clamp(Done / Size, 0f, 1f);

    /// <summary>Whether this download covers a game: both titles share one build.</summary>
    /// <param name="other">The game.</param>
    /// <returns>Whether its progress belongs on that game's page.</returns>
    public bool Covers(LauncherGame other) => other.Retail is not null && Game.Retail is not null;

    /// <summary>Takes a progress report from the installer, on whichever thread it arrives.</summary>
    /// <param name="progress">The report.</param>
    public void Report(InstallProgress progress)
    {
        Interlocked.Exchange(ref _done, progress.Done);
        Volatile.Write(ref _stage, (int)progress.Stage);
    }

    /// <summary>Updates the speed from what arrived since the last frame, smoothed over about a second.</summary>
    /// <param name="elapsedSeconds">The frame time.</param>
    public void Sample(float elapsedSeconds)
    {
        _window += elapsedSeconds;
        if (_window < 0.5f)
        {
            return;
        }

        long done = Interlocked.Read(ref _done);
        float instant = (done - _lastDone) / 1_000_000f / _window;
        Speed = Speed <= 0f ? instant : (Speed * 0.6f) + (instant * 0.4f);
        _lastDone = done;
        _window = 0f;
    }
}
