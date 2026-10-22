using OpenIGI.Core.Content;
using OpenIGI.Core.Titles;
using OpenIGI.Engine.Titles;
using OpenIGI.Formats;
using OpenIGI.Game.Level;

namespace OpenIGI.Modding;

/// <summary>The mods a run plays with, in the order they apply: a later mod's changes are made over an earlier one's.</summary>
/// <remarks>
/// <see cref="Extend"/> is the one place the mods reach a game: the game's runtime comes back with the mods' content modules
/// after its own and a module that publishes their <see cref="LevelSystemEdits"/>, so every session of that game -- the one
/// played and the menu's preview -- registers them the same way.
/// </remarks>
public sealed class ModSet
{
    /// <summary>The folder in a mod's directory whose files stand in for the game's own at the same paths.</summary>
    public const string DataDirectoryName = "data";

    /// <summary>The folder in a mod's directory that holds its compiled shaders.</summary>
    public const string ShadersDirectoryName = "shaders";

    private readonly List<InstalledMod> _mods;

    /// <summary>Initializes a new instance of the <see cref="ModSet"/> class.</summary>
    /// <param name="mods">The mods, in the order they apply.</param>
    /// <exception cref="ModLoadException">Two mods share an id, or one was written against another API.</exception>
    public ModSet(IEnumerable<InstalledMod> mods)
    {
        ArgumentNullException.ThrowIfNull(mods);
        _mods = [.. mods];
        HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
        foreach (InstalledMod mod in _mods)
        {
            if (!ids.Add(mod.Manifest.Id))
            {
                throw new ModLoadException($"Two mods are named '{mod.Manifest.Id}'.");
            }

            if (mod.Manifest.ApiVersion != ModApi.Version)
            {
                throw new ModLoadException(
                    $"The mod '{mod.Manifest.Id}' was written for modding API {mod.Manifest.ApiVersion}; this build offers {ModApi.Version}.");
            }
        }
    }

    /// <summary>Gets a set of no mods: the games as they ship.</summary>
    public static ModSet None { get; } = new([]);

    /// <summary>Gets the mods, in the order they apply.</summary>
    public IReadOnlyList<InstalledMod> Mods => _mods;

    /// <summary>Gets the mods that apply to a game.</summary>
    /// <param name="title">The game.</param>
    /// <returns>The mods, in order.</returns>
    public IEnumerable<InstalledMod> For(GameTitle title) => _mods.Where(mod => mod.Manifest.AppliesTo(title));

    /// <summary>Gets a game's runtime with the mods that apply to it configured in.</summary>
    /// <param name="runtime">The game's runtime as it ships.</param>
    /// <returns>
    /// The same runtime when no mod applies; otherwise one whose modules are the game's and then the mods', whose content
    /// folders are the mods' data, the last mod's first, and whose campaigns are the mods', in their order.
    /// </returns>
    public IGameTitleRuntime Extend(IGameTitleRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        List<IContentModule> added = [];
        LevelSystemEdits edits = new();
        List<TitleContentFolder> folders = [];
        List<TitleCampaign> campaigns = [];
        List<ModReference> played = [];
        List<TitlePostPass> passes = [];
        foreach (InstalledMod mod in For(runtime.Title))
        {
            string shaders = Path.Combine(mod.Directory, ShadersDirectoryName);
            passes.AddRange(mod.Manifest.PostPasses.Select(p => new TitlePostPass($"{mod.Manifest.Id}.{p.Name}", shaders, p.Module, p.Toggle, p.Parameters)));

            if (mod.Manifest.AffectsSimulation)
            {
                played.Add(new ModReference(mod.Manifest.Id, mod.Manifest.Version));
            }

            campaigns.AddRange(mod.Manifest.Campaigns.Select(static c => new TitleCampaign(c.Name, c.Missions)));
            mod.Code?.Configure(new ModBuilder(mod, runtime.Title, added, edits));
            string data = Path.Combine(mod.Directory, DataDirectoryName);
            if (System.IO.Directory.Exists(data))
            {
                folders.Add(new TitleContentFolder($"mod-{mod.Manifest.Id}", mod.Manifest.Name, data));
            }
        }

        if (added.Count == 0 && edits.Count == 0 && folders.Count == 0 && campaigns.Count == 0 && played.Count == 0 && passes.Count == 0)
        {
            return runtime;
        }

        // A later mod's files win over an earlier one's, as its changes are made over the earlier's.
        folders.Reverse();
        return new ModdedRuntime(
            runtime,
            added.Count == 0 && edits.Count == 0 ? runtime.Modules : [.. runtime.Modules, .. added, new EditsModule(edits)],
            [.. folders, .. runtime.ContentFolders],
            [.. runtime.Campaigns, .. campaigns],
            [.. runtime.Mods, .. played],
            [.. runtime.PostPasses, .. passes]);
    }

    // A game as it ships, with the mods' modules after its own, their data over its files and their campaigns beside its own.
    private sealed class ModdedRuntime(
        IGameTitleRuntime inner,
        IReadOnlyList<IContentModule> modules,
        IReadOnlyList<TitleContentFolder> folders,
        IReadOnlyList<TitleCampaign> campaigns,
        IReadOnlyList<ModReference> mods,
        IReadOnlyList<TitlePostPass> postPasses)
        : IGameTitleRuntime
    {
        public GameTitle Title => inner.Title;

        public ITitleDataLayout Layout => inner.Layout;

        public AssetFormatProfile Formats => inner.Formats;

        public IReadOnlyList<IContentModule> Modules => modules;

        public IReadOnlyList<TitleContentFolder> ContentFolders => folders;

        public IReadOnlyList<TitleCampaign> Campaigns => campaigns;

        public IReadOnlyList<ModReference> Mods => mods;

        public IReadOnlyList<TitlePostPass> PostPasses => postPasses;
    }

    // Publishes the mods' level-system edits to every session, the preview's sharing the played one's.
    private sealed class EditsModule(LevelSystemEdits edits) : IContentModule
    {
        public string Name => "Mods";

        public void Register(ContentRegistrationContext context) => context.Services.GetOrProvide(() => edits);

        public void LoadTables(ContentLoadContext context)
        {
        }
    }
}
