using OpenIGI.Core.Content;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Titles;
using OpenIGI.Launcher.Services;
using OpenIGI.Releases;

namespace OpenIGI.Launcher;

/// <summary>A mod a developer placed in the library's local mods folder: played as it is, never updated or removed by the launcher.</summary>
/// <param name="Manifest">Its <c>mod.json</c>.</param>
/// <param name="Folder">Its folder.</param>
internal sealed record LocalMod(ModManifest Manifest, string Folder)
{
    /// <summary>Gets the mod's id.</summary>
    public string Id => Manifest.Id;
}

/// <content>
/// The mods: each game's order of them, the ones published for it, the ones in the library's local folder, and the profile
/// and game paths the game is started with. Installing, updating, verifying and removing one is a content pack's.
/// </content>
internal sealed partial class LauncherModel
{
    // Where a mod's author drops a build to try: beside the installed mods, a folder no published mod can be named.
    private const string LocalModsFolder = ModPacks.LocalFolder;
    private const string ModProfileFile = "profile.json";

    private readonly List<LoadOrderItem> _modOrder = [];
    private readonly List<ContentEntry> _availableMods = [];
    private readonly List<LocalMod> _localMods = [];

    /// <summary>Gets the selected game's mod order, in the order they apply: its installed and local mods.</summary>
    public IReadOnlyList<LoadOrderItem> ModOrder => _modOrder;

    /// <summary>Gets the selected game's published mods that are not installed.</summary>
    public IReadOnlyList<ContentEntry> AvailableMods => _availableMods;

    /// <summary>Gets the library's local mods folder, where a mod's author drops a build to try.</summary>
    public string LocalModsPath => Path.Combine(Settings.Library.Trim(), ModsFolder, LocalModsFolder);

    /// <summary>Gets a value indicating whether the selected game is started with its mods.</summary>
    public bool PlaysMods => Current.Retail is { } title && !Settings.ModsOff.Contains(title.Id, StringComparer.Ordinal);

    /// <summary>Gets whether the player has agreed to run a mod's code.</summary>
    /// <param name="id">The mod.</param>
    /// <returns><see langword="true"/> for a mod without code, or one agreed to.</returns>
    public bool IsTrusted(string id) =>
        Find(id) is not { Pack.Mod.RunsCode: true } || Settings.TrustedMods.Contains(id, StringComparer.Ordinal);

    /// <summary>Records that the player agreed to run a mod's code, and installs it.</summary>
    /// <param name="id">The mod.</param>
    public void TrustAndInstallMod(string id)
    {
        if (!Settings.TrustedMods.Contains(id, StringComparer.Ordinal))
        {
            ChangeSettings(s => s.TrustedMods.Add(id));
        }

        InstallContent(id);
    }

    /// <summary>Starts the selected game with its mods, or without any whatever its order says.</summary>
    /// <param name="plays">Whether it plays its mods.</param>
    public void SetPlaysMods(bool plays)
    {
        if (Current.Retail is { } title)
        {
            ChangeSettings(s =>
            {
                s.ModsOff.RemoveAll(id => id == title.Id);
                if (!plays)
                {
                    s.ModsOff.Add(title.Id);
                }
            });
        }
    }

    /// <summary>Turns a mod of the selected game's order on or off for the next game start.</summary>
    /// <param name="id">The mod.</param>
    /// <param name="enabled">Whether the game is started with it.</param>
    public void SetModEnabled(string id, bool enabled)
    {
        if (Current.Retail is { } title)
        {
            ChangeSettings(s => ContentLoadOrder.SetEnabled(ModOrderFor(s, title.Id), id, enabled));
        }
    }

    /// <summary>Moves a mod of the selected game's order, as the Mods page's list shows it.</summary>
    /// <param name="from">Its index in <see cref="ModOrder"/>.</param>
    /// <param name="to">The index it is dropped at.</param>
    public void MoveMod(int from, int to)
    {
        if (Current.Retail is not { } title || from < 0 || to < 0 || from >= _modOrder.Count || to >= _modOrder.Count || from == to)
        {
            return;
        }

        string moved = _modOrder[from].Id;
        string target = _modOrder[to].Id;
        ChangeSettings(s =>
        {
            List<ContentOrderEntry> order = ModOrderFor(s, title.Id);
            ContentLoadOrder.Move(order, moved, order.FindIndex(entry => entry.Id == target));
        });
    }

    /// <summary>Opens the public source a mod's code was built from, at the commit it was built from where the host shows one.</summary>
    /// <param name="id">The mod.</param>
    public void OpenModSource(string id)
    {
        if (Find(id)?.Pack.Mod is not { Source: { } source } mod
            || !Uri.TryCreate(source, UriKind.Absolute, out Uri? address)
            || address.Scheme != Uri.UriSchemeHttps)
        {
            return;
        }

        // GitHub and its kind show a commit at <repository>/tree/<commit>; any other address is opened as it is.
        string repository = address.AbsoluteUri.TrimEnd('/');
        if (repository.EndsWith(".git", StringComparison.Ordinal))
        {
            repository = repository[..^4];
        }

        Open(mod.Commit is { Length: > 0 } commit ? $"{repository}/tree/{Uri.EscapeDataString(commit)}" : repository);
    }

    /// <summary>Opens a mod's folder: its installed files, or the local folder it was dropped in.</summary>
    /// <param name="id">The mod.</param>
    public void OpenModFolder(string id)
    {
        if (_localMods.Find(mod => mod.Id == id) is { } local)
        {
            Open(local.Folder);
        }
        else
        {
            OpenContentFolder(id);
        }
    }

    /// <summary>Opens the library's local mods folder, creating it when it is not there yet.</summary>
    public void OpenLocalMods()
    {
        try
        {
            Directory.CreateDirectory(LocalModsPath);
            Open(LocalModsPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Report($"The local mods folder could not be opened: {ex.Message}", ex);
            Changed();
        }
    }

    /// <summary>Reads the local mods folder again, for a build its author just dropped there.</summary>
    public void RefreshLocalMods()
    {
        LoadLocalMods();
        Recompute();
    }

    /// <summary>Builds the mod profile the game is started with: every game's mod order, as the settings hold it.</summary>
    /// <returns>The profile.</returns>
    /// <remarks>
    /// Every game, as the content profile is, and switched-off mods too, which the game reads as off. A mod whose files are
    /// damaged, that the installed game is too old for, or that is being worked on is left out: it was never going to load
    /// right.
    /// </remarks>
    internal ModProfile BuildModProfile() =>
        ModLoadOrder.ToProfile(Settings.ModOrder, ModFolder, Settings.ModsOff);

    // Where a mod's mod.json is, for a game, when it can be played: a local one's folder, an installed one's files.
    private string? ModFolder(string game, string id)
    {
        if (_localMods.Find(mod => mod.Id == id) is { } local)
        {
            return Title(game) is { } title && local.Manifest.AppliesTo(title) ? local.Folder : null;
        }

        return Find(id) is { Installed: not null, Pack.Kind: ContentKind.Mod } mod
               && mod.Pack.IsFor(game)
               && mod.State is not (ContentState.Incompatible or ContentState.Damaged or ContentState.Working)
            ? ContentInstallerFor(id).Files
            : null;
    }

    // The mod profile beside the mods, and where each game an enabled mod reads is installed: the game finds another
    // game's data by its variable, and the launcher knows where the player keeps it.
    private void AddModEnvironment(Dictionary<string, string> environment, GameTitle title)
    {
        string path = Path.Combine(Settings.Library.Trim(), ModsFolder, ModProfileFile);
        ModProfile profile = BuildModProfile();
        foreach (string id in ModLoadOrder.GamesRead(profile, title.Id, ModReads))
        {
            if (Title(id) is { } other && InstalledAt(other) is { } folder)
            {
                environment[other.GamePathVariable] = folder;
            }
        }

        try
        {
            ModProfile.Write(path, profile);
            environment[ModProfile.EnvironmentVariable] = path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Without a profile the game scans its own mods folder, which holds none: it starts as it ships.
            Report($"The mods could not be handed to {title.DisplayName}: {ex.Message}", ex);
            Changed();
        }
    }

    // The other games a mod reads, as its listing or its local manifest says.
    private IReadOnlyList<string> ModReads(string id) =>
        _localMods.Find(mod => mod.Id == id)?.Manifest.RequiresTitleData
        ?? Find(id)?.Pack.Mod?.RequiresTitleData
        ?? [];

    private static GameTitle? Title(string id) =>
        GameTitle.All.FirstOrDefault(title => string.Equals(title.Id, id, StringComparison.OrdinalIgnoreCase));

    // Where a game is installed, as the player chose it or as it was found.
    private string? InstalledAt(GameTitle title) => GameStarter.Locate(title, Settings.GameFolders.GetValueOrDefault(title.Id));

    // The first game a mod reads that is not installed here, by name; an id no game has is named as it is.
    private string? MissingGame(ContentPack pack)
    {
        foreach (string id in pack.Mod?.RequiresTitleData ?? [])
        {
            if (Title(id) is not { } title)
            {
                return id.ToUpperInvariant();
            }

            if (InstalledAt(title) is null)
            {
                return title.DisplayName;
            }
        }

        return null;
    }

    private bool ModEnabled(string game, string id) =>
        Settings.ModOrder.TryGetValue(game, out List<ContentOrderEntry>? order) && order.Find(e => e.Id == id) is { Enabled: true };

    // A game's mod order in the settings, created on first use.
    private static List<ContentOrderEntry> ModOrderFor(LauncherSettings settings, string game)
    {
        if (!settings.ModOrder.TryGetValue(game, out List<ContentOrderEntry>? order))
        {
            order = [];
            settings.ModOrder[game] = order;
        }

        return order;
    }

    // A mod just installed goes at the end of the order of every game it is for.
    private static void AddInstalledMod(LauncherSettings settings, ContentPack pack)
    {
        foreach (GameTitle title in GameTitle.All.Where(title => pack.IsFor(title.Id)))
        {
            ModLoadOrder.AddInstalled(ModOrderFor(settings, title.Id), pack.Id);
        }
    }

    // The folders in the local mods folder holding a mod.json that reads.
    private void LoadLocalMods()
    {
        _localMods.Clear();
        if (!Directory.Exists(LocalModsPath))
        {
            return;
        }

        foreach (string folder in Directory.EnumerateDirectories(LocalModsPath).Order(StringComparer.OrdinalIgnoreCase))
        {
            string manifest = Path.Combine(folder, ModManifest.FileName);
            try
            {
                if (File.Exists(manifest) && ModManifest.Parse(File.ReadAllText(manifest)) is { } read && _localMods.TrueForAll(m => m.Id != read.Id))
                {
                    _localMods.Add(new LocalMod(read, folder));
                }
            }
            catch (Exception ex) when (ex is ModLoadException or IOException or UnauthorizedAccessException)
            {
                _logger.Warning(LogCategory, $"The local mod in '{folder}' is left out: {ex.Message}");
            }
        }
    }

    // Keeps every game's mod order in step with the mods installed and dropped in for it.
    private bool ReconcileModOrders()
    {
        bool reachable = Directory.Exists(Path.Combine(Settings.Library.Trim(), ModsFolder));
        bool changed = false;
        foreach (LauncherGame game in Games)
        {
            if (game.Retail is not { } title)
            {
                continue;
            }

            string[] mods =
            [
                .. _packs.Where(p => p.Pack.Kind == ContentKind.Mod && p.Installed is not null && p.Pack.IsFor(title.Id)).Select(static p => p.Pack.Id),
                .. _localMods.Where(m => m.Manifest.AppliesTo(title)).Select(static m => m.Id),
            ];
            if (mods.Length == 0 && !Settings.ModOrder.ContainsKey(title.Id))
            {
                continue;
            }

            changed |= ModLoadOrder.Reconcile(ModOrderFor(Settings, title.Id), mods, prune: reachable);
        }

        return changed;
    }

    // The selected game's mods as the page lists them, and what each one still needs.
    private void BuildModOrder()
    {
        _modOrder.Clear();
        _availableMods.Clear();
        if (Current.Retail is not { } title)
        {
            return;
        }

        _availableMods.AddRange(_packs.Where(p => p.Pack.Kind == ContentKind.Mod && p.Installed is null && p.Pack.IsFor(title.Id)));
        if (!Settings.ModOrder.TryGetValue(title.Id, out List<ContentOrderEntry>? order))
        {
            return;
        }

        HashSet<string> on = new(order.Where(static e => e.Enabled).Select(static e => e.Id), StringComparer.Ordinal);
        foreach (ContentOrderEntry entry in order)
        {
            if (_localMods.Find(mod => mod.Id == entry.Id) is { } local)
            {
                _modOrder.Add(new LoadOrderItem { Id = entry.Id, Mod = local, Enabled = entry.Enabled, Folder = local.Folder });
            }
            else if (Find(entry.Id) is { Installed: not null, Pack.Kind: ContentKind.Mod } mod && mod.Pack.IsFor(title.Id))
            {
                mod.NeedsGame = MissingGame(mod.Pack);
                mod.NeedsMod = mod.Pack.Mod?.Dependencies?.Keys.FirstOrDefault(id => !on.Contains(id));
                _modOrder.Add(new LoadOrderItem { Id = entry.Id, Entry = mod, Enabled = entry.Enabled });
            }
        }
    }
}
