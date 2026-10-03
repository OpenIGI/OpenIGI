using OpenIGI.Core.Content;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Titles;
using OpenIGI.Launcher.Services;
using OpenIGI.Releases;

namespace OpenIGI.Launcher;

/// <summary>Where an optional content pack stands: what its card offers to do about it.</summary>
internal enum ContentState
{
    /// <summary>Published and not installed: INSTALL.</summary>
    Available,

    /// <summary>Installed at the newest version.</summary>
    Installed,

    /// <summary>Installed, with a newer version published: UPDATE.</summary>
    UpdateAvailable,

    /// <summary>Installed files failed a verify: REPAIR.</summary>
    Damaged,

    /// <summary>The installed game is too old to read it.</summary>
    Incompatible,

    /// <summary>Being installed, updated, repaired or verified now.</summary>
    Working,
}

/// <summary>One optional content pack, as the Content page shows it.</summary>
internal sealed class ContentEntry
{
    /// <summary>Gets or sets the newest version the service lists (or, offline, the installed one).</summary>
    public required ContentPack Pack { get; set; }

    /// <summary>Gets or sets the installed version's manifest, or <see langword="null"/> when it is not installed.</summary>
    public ContentManifest? Installed { get; set; }

    public ContentState State { get; set; }

    /// <summary>Gets or sets whether the game is started with the pack.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets what the next install or update downloads, or -1 while it is being worked out.</summary>
    public long DownloadBytes { get; set; } = -1;

    /// <summary>Gets or sets the files the last verify found missing or changed.</summary>
    public IReadOnlySet<string> Damaged { get; set; } = new HashSet<string>();

    /// <summary>Gets or sets whether the service still offers the pack; one it stopped offering stays installed.</summary>
    public bool Listed { get; set; } = true;

    /// <summary>Gets or sets the pack's picture on disk, or <see langword="null"/> while it has none (or it is downloading).</summary>
    public string? ThumbnailPath { get; set; }
}

/// <summary>One entry of the selected game's load order, as the Content page shows it.</summary>
internal sealed class LoadOrderItem
{
    /// <summary>Gets the entry's id: a pack id, or <see cref="ContentLayerSpec.LocalId"/>.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the pack, or <see langword="null"/> for the game folder's own overrides.</summary>
    public ContentEntry? Entry { get; init; }

    /// <summary>Gets a value indicating whether this is the game folder's own overrides folder.</summary>
    public bool IsLocal => Entry is null;

    /// <summary>Gets or sets whether the game is started with it.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets, for the local entry, the folder; <see langword="null"/> while the game's folder is unknown.</summary>
    public string? Folder { get; set; }

    /// <summary>Gets or sets, for the local entry, how many files the folder holds.</summary>
    public int LocalFiles { get; set; }

    /// <summary>Gets or sets, for the local entry, the folder's size in bytes.</summary>
    public long LocalBytes { get; set; }

    /// <summary>Gets or sets where its files overlap the other enabled entries.</summary>
    public ContentConflict Conflict { get; set; } = ContentConflict.None;
}

/// <content>The optional content packs: listing them, installing, updating, verifying and removing them.</content>
internal sealed partial class LauncherModel
{
    private const string ContentFolder = "content";
    private const string ListingFile = "listing.json";
    private const string ThumbnailFolder = ".thumbnails";
    private const string ProfileFile = "profile.json";

    // Where mods will be installed beside content, once there is a mod loader. Kept apart on purpose: content
    // is asset overlays the game stacks through its overrides; mods are code, with their own manifest, profile
    // and loader. See "Content packs and mods" in CONTRIBUTING.md.
    internal const string ModsFolder = "mods";

    private readonly ContentFeedClient _contentClient;
    private readonly List<ContentEntry> _packs = [];
    private readonly Dictionary<string, ContentManifest> _targets = new(StringComparer.Ordinal);
    private readonly HashSet<string> _busy = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LocalListing> _localListings = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<LoadOrderItem> _loadOrder = [];
    private readonly List<ContentEntry> _available = [];
    private ContentIndex? _contentIndex;
    private bool _checkingContent;

    /// <summary>Gets every pack: the ones the service lists and the ones installed, by title.</summary>
    public IReadOnlyList<ContentEntry> Packs => _packs;

    /// <summary>Gets the selected game's load order, highest priority first: its installed packs and its own overrides folder.</summary>
    public IReadOnlyList<LoadOrderItem> LoadOrder => _loadOrder;

    /// <summary>Gets the selected game's packs that are offered and not installed.</summary>
    public IReadOnlyList<ContentEntry> AvailableContent => _available;

    /// <summary>Installs a pack, updates it to its newest version, or repairs the files a verify found damaged.</summary>
    /// <param name="id">The pack.</param>
    public void InstallContent(string id)
    {
        if (Find(id) is not { } entry || _contentIndex is not { } index)
        {
            Report("The content list has not been fetched yet.");
            Changed();
            return;
        }

        if (Job is { } running && !(running.Paused && running.ContentId == id))
        {
            Report("Another download is running; this one can start when it is done.");
            Changed();
            return;
        }

        if (_running is not null)
        {
            Report("Close the game first: it is reading the pack's files.");
            Changed();
            return;
        }

        ContentPack pack = entry.Pack;
        IReadOnlySet<string> rebuild = entry.Damaged;
        string verb = entry.Installed is null ? "Install" : entry.State == ContentState.Damaged ? "Repair" : "Update";
        DownloadJob job = Job is { Paused: true } paused && paused.ContentId == id
            ? paused
            : new DownloadJob(Current, pack.Version, $"{verb} {pack.Title}", Math.Max(0, entry.DownloadBytes)) { ContentId = id };
        job.Paused = false;
        Job = job;
        Problem = null;

        _download?.Dispose();
        _download = new CancellationTokenSource();
        CancellationToken token = _download.Token;
        Progress<InstallProgress> progress = new(job.Report);
        ContentInstaller installer = ContentInstallerFor(id);

        _ = Task.Run(async () =>
        {
            try
            {
                ContentManifest target = await TargetAsync(pack, token).ConfigureAwait(false);
                ContentManifest installed = await installer.InstallAsync(index, target, rebuild, progress, token).ConfigureAwait(false);
                _results.Enqueue(() =>
                {
                    entry.Installed = installed;
                    entry.Damaged = new HashSet<string>();
                    entry.DownloadBytes = 0;
                    ChangeSettings(s => ContentLoadOrder.AddInstalled(OrderFor(s, pack.Game), id));

                    Job = null;
                    Recompute();
                    MaybeStartAutomatically();
                });
            }
            catch (OperationCanceledException)
            {
                // Paused or cancelled: the chunks fetched so far stay for the resume.
                _results.Enqueue(Recompute);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException
                                           or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                _results.Enqueue(() =>
                {
                    Job = null;
                    Report($"{pack.Title} could not be installed: {ex.Message}", ex);
                    Recompute();
                });
            }
        });
        Recompute();
    }

    /// <summary>Removes a pack's files.</summary>
    /// <param name="id">The pack.</param>
    public void RemoveContent(string id)
    {
        if (Find(id) is not { Installed: not null } entry || Job?.ContentId == id || _busy.Contains(id))
        {
            return;
        }

        if (_running is not null)
        {
            Report("Close the game first: it is reading the pack's files.");
            Changed();
            return;
        }

        _busy.Add(id);
        ContentInstaller installer = ContentInstallerFor(id);
        Recompute();
        _ = Task.Run(() =>
        {
            try
            {
                installer.Uninstall();
                _results.Enqueue(() =>
                {
                    _busy.Remove(id);
                    entry.Installed = null;
                    entry.Damaged = new HashSet<string>();
                    entry.DownloadBytes = entry.Pack.Download;
                    ChangeSettings(s =>
                    {
                        s.Content.Remove(id);
                        foreach (List<ContentOrderEntry> order in s.ContentOrder.Values)
                        {
                            order.RemoveAll(entry => entry.Id == id);
                        }
                    });
                    Report($"{entry.Pack.Title} was removed.");
                    Recompute();
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _results.Enqueue(() =>
                {
                    _busy.Remove(id);
                    Report($"{entry.Pack.Title} could not be removed: {ex.Message}", ex);
                    Recompute();
                });
            }
        });
    }

    /// <summary>Checks a pack's files against the manifest they were installed from.</summary>
    /// <param name="id">The pack.</param>
    public void VerifyContent(string id)
    {
        if (Find(id) is not { Installed: { } manifest } entry || Job?.ContentId == id || _busy.Contains(id))
        {
            return;
        }

        _busy.Add(id);
        Report($"Verifying {entry.Pack.Title}…");
        ContentInstaller installer = ContentInstallerFor(id);
        Recompute();
        _ = Task.Run(() =>
        {
            try
            {
                IReadOnlyList<string> damaged = installer.Verify(manifest);
                _results.Enqueue(() =>
                {
                    _busy.Remove(id);
                    entry.Damaged = new HashSet<string>(damaged, StringComparer.Ordinal);
                    entry.DownloadBytes = -1;
                    Report(damaged.Count > 0
                        ? $"{damaged.Count} file(s) of {entry.Pack.Title} are missing or changed: repair to restore them."
                        : $"Every file of {entry.Pack.Title} is intact.");
                    Recompute();
                    PlanContent(entry);
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _results.Enqueue(() =>
                {
                    _busy.Remove(id);
                    Report($"{entry.Pack.Title} could not be verified: {ex.Message}", ex);
                    Recompute();
                });
            }
        });
    }

    /// <summary>Turns an entry of the selected game's load order on or off for the next game start, without touching its files.</summary>
    /// <param name="id">The pack, or <see cref="ContentLayerSpec.LocalId"/>.</param>
    /// <param name="enabled">Whether the game is started with it.</param>
    public void SetContentEnabled(string id, bool enabled)
    {
        if (Current.Retail is { } title)
        {
            ChangeSettings(s => ContentLoadOrder.SetEnabled(OrderFor(s, title.Id), id, enabled));
        }
    }

    /// <summary>Moves an entry of the selected game's load order, as the Content page's list shows it.</summary>
    /// <param name="from">Its index in <see cref="LoadOrder"/>.</param>
    /// <param name="to">The index it is dropped at.</param>
    /// <remarks>
    /// By the entry now at <paramref name="to"/> rather than by index, because the saved order may also hold
    /// packs the page does not show (a library on a drive that is not plugged in) and those keep their places.
    /// </remarks>
    public void MoveContent(int from, int to)
    {
        if (Current.Retail is not { } title || from < 0 || to < 0 || from >= _loadOrder.Count || to >= _loadOrder.Count || from == to)
        {
            return;
        }

        string moved = _loadOrder[from].Id;
        string target = _loadOrder[to].Id;
        ChangeSettings(s =>
        {
            List<ContentOrderEntry> order = OrderFor(s, title.Id);
            ContentLoadOrder.Move(order, moved, order.FindIndex(entry => entry.Id == target));
        });
    }

    /// <summary>Opens a pack's folder in the system's file manager.</summary>
    /// <param name="id">The pack.</param>
    public void OpenContentFolder(string id) => Open(ContentInstallerFor(id).Files);

    /// <summary>Opens the selected game's own overrides folder, creating it when it is not there yet.</summary>
    public void OpenLocalOverrides()
    {
        if (_loadOrder.Find(static item => item.IsLocal)?.Folder is not { } folder)
        {
            Report("Choose the game's folder in Settings first.");
            Changed();
            return;
        }

        try
        {
            Directory.CreateDirectory(folder);
            Open(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Report($"The overrides folder could not be opened: {ex.Message}", ex);
            Changed();
        }
    }

    /// <summary>Builds the content profile the game is started with: every game's load order, as the settings hold it.</summary>
    /// <returns>The profile.</returns>
    /// <remarks>
    /// Every game rather than the one being started, so a title switch inside the game finds the other game's
    /// order; and disabled entries too, so the game's console can switch one on. A pack whose files are damaged,
    /// or that the installed game is too old for, is left out: it was never going to load right.
    /// </remarks>
    internal ContentProfile BuildContentProfile() =>
        ContentLoadOrder.ToProfile(
            Settings.ContentOrder,
            id => Find(id) is { Installed: not null } pack
                  && pack.State is not (ContentState.Incompatible or ContentState.Damaged or ContentState.Working)
                ? new ContentLoadOrder.PackLayer(pack.Pack.Title, pack.Pack.Game, ContentInstallerFor(id).Files)
                : null);

    // The variables the game is started with: the content profile, written beside the packs. The game stacks
    // the layers it lists for whichever title it is running, in the player's order.
    private Dictionary<string, string> GameEnvironment(GameTitle title)
    {
        Dictionary<string, string> environment = new(StringComparer.Ordinal);
        string path = Path.Combine(Settings.Library.Trim(), ContentFolder, ProfileFile);
        ContentProfile profile = BuildContentProfile();

        // For a build from before load orders, which reads only this variable and only one folder: the top
        // enabled pack, as the launcher used to hand over. A build that reads the profile ignores it.
        if (ContentLoadOrder.LegacyFolder(profile, title.Id) is { } legacy)
        {
            environment[GameStarter.LegacyOverridesVariable] = legacy;
        }

        try
        {
            ContentProfile.Write(path, profile);
            environment[GameStarter.ContentProfileVariable] = path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The game still starts, with its own overrides folder only; the reason is on the page.
            Report($"The content load order could not be handed to {title.DisplayName}: {ex.Message}", ex);
            Changed();
        }

        return environment;
    }

    // A game's order in the settings, created on first use.
    private static List<ContentOrderEntry> OrderFor(LauncherSettings settings, string game)
    {
        if (!settings.ContentOrder.TryGetValue(game, out List<ContentOrderEntry>? order))
        {
            order = [new ContentOrderEntry(ContentLayerSpec.LocalId, true)];
            settings.ContentOrder[game] = order;
        }

        return order;
    }

    private ContentInstaller ContentInstallerFor(string id) =>
        new(_http, Path.Combine(Settings.Library.Trim(), ContentFolder, id));

    private ContentEntry? Find(string id) => _packs.Find(p => p.Pack.Id == id);

    // What is installed in the library, so installed packs are listed (and can be played with) before, or
    // without, the service answering.
    private void LoadInstalledContent()
    {
        _packs.Clear();
        _targets.Clear();
        string folder = Path.Combine(Settings.Library.Trim(), ContentFolder);
        if (!Directory.Exists(folder))
        {
            return;
        }

        foreach (string directory in Directory.EnumerateDirectories(folder).Order(StringComparer.Ordinal))
        {
            string id = Path.GetFileName(directory);
            if (ContentManifest.IsValidId(id) && ContentInstallerFor(id).Installed() is { } installed && installed.Id == id)
            {
                // The listing last seen, so a renamed pack keeps its name and picture offline too.
                ContentPack pack = RememberedListing(id) ?? installed.Listing;
                _packs.Add(new ContentEntry { Pack = pack, Installed = installed, DownloadBytes = 0, ThumbnailPath = CachedThumbnail(pack) });
            }
        }

        if (_contentIndex is not null)
        {
            MergeIndex(_contentIndex);
        }
    }

    private void CheckContent()
    {
        if (_checkingContent)
        {
            return;
        }

        _checkingContent = true;
        _ = Task.Run(async () =>
        {
            try
            {
                using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));
                ContentIndex index = await _contentClient.FetchIndexAsync(ReleaseFeedClient.DefaultService, timeout.Token).ConfigureAwait(false);
                _results.Enqueue(() =>
                {
                    _checkingContent = false;
                    _contentIndex = index;
                    MergeIndex(index);
                    Recompute();
                    foreach (ContentEntry entry in _packs.Where(static p => p.State == ContentState.UpdateAvailable && p.DownloadBytes < 0))
                    {
                        PlanContent(entry);
                    }

                    MaybeStartAutomatically();
                });
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                // The game's own check reports the service being unreachable; the content list keeps what it had.
                _logger.Debug(LogCategory, $"The content list could not be fetched: {ex.Message}");
                _results.Enqueue(() => _checkingContent = false);
            }
        });
    }

    private void MergeIndex(ContentIndex index)
    {
        foreach (ContentEntry entry in _packs)
        {
            entry.Listed = index.Packs.Any(p => p.Id == entry.Pack.Id);
        }

        foreach (ContentPack pack in index.Packs)
        {
            if (Find(pack.Id) is { } entry)
            {
                if (entry.Pack.Version != pack.Version)
                {
                    entry.DownloadBytes = entry.Installed is null ? pack.Download : -1;
                }

                entry.Pack = pack;
            }
            else
            {
                _packs.Add(new ContentEntry { Pack = pack, DownloadBytes = pack.Download });
            }
        }

        // A pack the service stopped offering stays listed only while it is installed: its files are the
        // player's, and the game may still be started with them.
        _packs.RemoveAll(static p => !p.Listed && p.Installed is null && p.State != ContentState.Working);
        _packs.Sort(static (a, b) => string.CompareOrdinal(a.Pack.Title, b.Pack.Title));
        foreach (ContentEntry entry in _packs.Where(static p => p.Listed))
        {
            if (entry.Installed is not null)
            {
                RememberListing(entry.Pack);
            }

            entry.ThumbnailPath = CachedThumbnail(entry.Pack);
            if (entry.ThumbnailPath is null && entry.Pack.Thumbnail is not null)
            {
                FetchThumbnail(entry);
            }
        }
    }

    // Where a pack's picture is kept once downloaded: by its file name, which changes with the picture.
    private string? ThumbnailFile(ContentPack pack)
    {
        string? name = pack.Thumbnail is { } address && Uri.TryCreate(address, UriKind.Absolute, out Uri? uri)
            ? Path.GetFileName(uri.AbsolutePath)
            : null;
        return ContentPackInfo.IsValidThumbnail(name)
            ? Path.Combine(Settings.Library.Trim(), ContentFolder, ThumbnailFolder, name!)
            : null;
    }

    private string? CachedThumbnail(ContentPack pack) => ThumbnailFile(pack) is { } path && File.Exists(path) ? path : null;

    private void FetchThumbnail(ContentEntry entry)
    {
        if (ThumbnailFile(entry.Pack) is not { } path || !_busy.Add("thumbnail:" + path))
        {
            return;
        }

        string address = entry.Pack.Thumbnail!;
        _ = Task.Run(async () =>
        {
            try
            {
                using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
                byte[] data = await _http.GetByteArrayAsync(address, timeout.Token).ConfigureAwait(false);
                if (data.Length <= ContentManifest.MaxThumbnailBytes && ContentPackInfo.PictureExtension(data) is not null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    await File.WriteAllBytesAsync(path + ".tmp", data, timeout.Token).ConfigureAwait(false);
                    File.Move(path + ".tmp", path, overwrite: true);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
            {
                // No picture: the card draws its plain panel, and the next check tries again.
                _logger.Debug(LogCategory, $"A content thumbnail could not be fetched: {ex.Message}");
            }

            _results.Enqueue(() =>
            {
                _busy.Remove("thumbnail:" + path);
                if (File.Exists(path) && entry.Pack.Thumbnail == address)
                {
                    entry.ThumbnailPath = path;
                    Changed();
                }
            });
        });
    }

    private ContentPack? RememberedListing(string id)
    {
        string path = Path.Combine(Settings.Library.Trim(), ContentFolder, id, ListingFile);
        try
        {
            return File.Exists(path)
                && System.Text.Json.JsonSerializer.Deserialize(File.ReadAllText(path), ReleaseJsonContext.Default.ContentPack) is { } pack
                && pack.Id == id
                    ? pack
                    : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private void RememberListing(ContentPack pack)
    {
        string path = Path.Combine(Settings.Library.Trim(), ContentFolder, pack.Id, ListingFile);
        try
        {
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(pack, ReleaseJsonContext.Default.ContentPack));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only the offline name and picture depend on it.
        }
    }

    // Works out what updating or repairing a pack downloads, so its card can say so before it starts.
    private void PlanContent(ContentEntry entry)
    {
        if (entry.Installed is null)
        {
            entry.DownloadBytes = entry.Pack.Download;
            return;
        }

        ContentPack pack = entry.Pack;
        IReadOnlySet<string> rebuild = entry.Damaged;
        ContentInstaller installer = ContentInstallerFor(pack.Id);
        _ = Task.Run(async () =>
        {
            try
            {
                using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
                ContentManifest target = await TargetAsync(pack, timeout.Token).ConfigureAwait(false);
                long bytes = installer.Plan(target, rebuild).DownloadBytes;
                _results.Enqueue(() =>
                {
                    if (entry.Pack.Version == pack.Version)
                    {
                        entry.DownloadBytes = bytes;
                        Changed();
                    }
                });
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException
                                           or InvalidDataException or System.Text.Json.JsonException)
            {
                // Left unknown; the card says so, and the update itself works it out again.
                _logger.Debug(LogCategory, $"A content update could not be planned: {ex.Message}");
            }
        });
    }

    // The manifest of the version a pack is going to, fetched once per version. Repairing a pack at the newest
    // version uses the installed manifest, which is that version's.
    private async Task<ContentManifest> TargetAsync(ContentPack pack, CancellationToken cancellationToken)
    {
        lock (_targets)
        {
            if (_targets.TryGetValue(pack.Id, out ContentManifest? cached) && cached.Version == pack.Version)
            {
                return cached;
            }
        }

        ContentManifest? installed = ContentInstallerFor(pack.Id).Installed();
        ContentManifest target = installed is not null && installed.Version == pack.Version
            ? installed
            : await _contentClient.FetchManifestAsync(ReleaseFeedClient.DefaultService, pack, cancellationToken).ConfigureAwait(false);

        lock (_targets)
        {
            _targets[pack.Id] = target;
        }

        return target;
    }

    private void RecomputeContent()
    {
        ReconcileLoadOrders();

        foreach (ContentEntry entry in _packs)
        {
            entry.Enabled = Settings.ContentOrder.TryGetValue(entry.Pack.Game, out List<ContentOrderEntry>? order)
                && order.Find(e => e.Id == entry.Pack.Id) is { Enabled: true };
            if ((Job?.ContentId == entry.Pack.Id && !Job.Paused) || _busy.Contains(entry.Pack.Id))
            {
                entry.State = ContentState.Working;
                continue;
            }

            bool compatible = _installed is null || entry.Pack.SupportsGame(_installed.Version);
            entry.State = !compatible ? ContentState.Incompatible
                : entry.Installed is null ? ContentState.Available
                : entry.Damaged.Count > 0 ? ContentState.Damaged
                : entry.Pack.ParsedVersion > entry.Installed.ParsedVersion ? ContentState.UpdateAvailable
                : ContentState.Installed;
        }
    }

    // Keeps every game's load order in step with what is installed, carries the old per-pack switches into it,
    // and rebuilds the selected game's list and its conflicts for the page.
    private void ReconcileLoadOrders()
    {
        bool libraryReachable = Directory.Exists(Path.Combine(Settings.Library.Trim(), ContentFolder));
        bool changed = false;

        foreach (LauncherGame game in Games)
        {
            if (game.Retail is not { } title)
            {
                continue;
            }

            string[] installed = [.. _packs.Where(p => p.Pack.Game == title.Id && p.Installed is not null).Select(static p => p.Pack.Id)];
            if (installed.Length == 0 && !Settings.ContentOrder.ContainsKey(title.Id))
            {
                continue;
            }

            changed |= ContentLoadOrder.Reconcile(OrderFor(Settings, title.Id), installed, Settings.Content, prune: libraryReachable);
        }

        // The old switches are read once: whatever the orders now hold has been carried over.
        foreach (string id in Settings.Content.Keys.ToArray())
        {
            if (Settings.ContentOrder.Values.Any(order => order.Exists(entry => entry.Id == id)))
            {
                Settings.Content.Remove(id);
                changed = true;
            }
        }

        if (changed)
        {
            try
            {
                Settings.Save(_settingsPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Report($"Settings could not be saved: {ex.Message}", ex);
            }
        }

        BuildLoadOrder();
    }

    private void BuildLoadOrder()
    {
        _loadOrder.Clear();
        _available.Clear();
        if (Current.Retail is not { } title)
        {
            return;
        }

        _available.AddRange(_packs.Where(p => p.Pack.Game == title.Id && p.Installed is null));
        if (!Settings.ContentOrder.TryGetValue(title.Id, out List<ContentOrderEntry>? order))
        {
            order = [new ContentOrderEntry(ContentLayerSpec.LocalId, true)];
        }

        string? gameFolder = GameStarter.Locate(title, Settings.GameFolders.GetValueOrDefault(title.Id));
        foreach (ContentOrderEntry entry in order)
        {
            if (entry.Id == ContentLayerSpec.LocalId)
            {
                string? folder = gameFolder is null ? null : Path.Combine(gameFolder, OverridesFolder);
                LocalListing listing = ListLocal(folder);
                _loadOrder.Add(new LoadOrderItem
                {
                    Id = entry.Id,
                    Enabled = entry.Enabled,
                    Folder = folder,
                    LocalFiles = listing.Files.Count,
                    LocalBytes = listing.Bytes,
                });
            }
            else if (Find(entry.Id) is { Installed: not null } pack && pack.Pack.Game == title.Id)
            {
                _loadOrder.Add(new LoadOrderItem { Id = entry.Id, Entry = pack, Enabled = entry.Enabled });
            }
        }

        IReadOnlyDictionary<string, ContentConflict> conflicts = ContentConflicts.Compute(
        [
            .. _loadOrder.Select(item => new ContentLayerFiles(
                item.Id,
                item.Entry?.Pack.Title ?? "Local overrides",
                item.Enabled,
                item.Entry?.Installed is { } manifest
                    ? manifest.Files.Select(static f => f.Path).ToArray()
                    : ListLocal(item.Folder).Files)),
        ]);

        foreach (LoadOrderItem item in _loadOrder)
        {
            item.Conflict = conflicts.GetValueOrDefault(item.Id, ContentConflict.None);
        }
    }

    // What the local overrides folder holds, listed at most every few seconds: the page asks on every change,
    // and a developer's folder can hold thousands of files.
    private LocalListing ListLocal(string? folder)
    {
        if (folder is null)
        {
            return LocalListing.Empty;
        }

        long now = Environment.TickCount64;
        if (_localListings.TryGetValue(folder, out LocalListing? cached) && now - cached.Taken < LocalListingSeconds * 1000)
        {
            return cached;
        }

        LocalListing listing = LocalListing.Empty with { Taken = now };
        try
        {
            if (Directory.Exists(folder))
            {
                List<string> files = [];
                long bytes = 0;
                foreach (FileInfo file in new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    files.Add(Path.GetRelativePath(folder, file.FullName).Replace('\\', '/'));
                    bytes += file.Length;
                }

                listing = new LocalListing(files, bytes, now);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Listed as empty; the game reads the folder itself either way.
        }

        _localListings[folder] = listing;
        return listing;
    }

    private const string OverridesFolder = "overrides";
    private const int LocalListingSeconds = 5;

    private sealed record LocalListing(IReadOnlyList<string> Files, long Bytes, long Taken)
    {
        public static LocalListing Empty { get; } = new([], 0, 0);
    }
}
