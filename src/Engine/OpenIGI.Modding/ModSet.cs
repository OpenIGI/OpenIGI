using OpenIGI.Core.Content;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Titles;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Titles;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats;
using OpenIGI.Game.Level;

namespace OpenIGI.Modding;

/// <summary>One mod as found: its manifest, where it lives, and its code when it has any.</summary>
/// <param name="Manifest">The mod's manifest.</param>
/// <param name="Directory">The mod's directory.</param>
/// <param name="Code">The mod's code, or <see langword="null"/> for a mod made of content alone.</param>
public sealed record InstalledMod(ModManifest Manifest, string Directory, IOpenIgiMod? Code);

/// <summary>The mods a run plays with, in the order they apply: a later mod's changes are made over an earlier one's.</summary>
/// <remarks>
/// <see cref="Extend"/> is the one place the mods reach a game: the game's runtime comes back with the mods' content modules
/// after its own and a module that publishes their <see cref="LevelSystemEdits"/>, so every session of that game -- the one
/// played and the menu's preview -- registers them the same way.
/// </remarks>
public sealed class ModSet
{
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
    /// <returns>The same runtime when no mod with code applies; otherwise one whose modules are the game's and then the mods'.</returns>
    public IGameTitleRuntime Extend(IGameTitleRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        List<IContentModule> added = [];
        LevelSystemEdits edits = new();
        foreach (InstalledMod mod in For(runtime.Title))
        {
            mod.Code?.Configure(new Builder(mod, runtime.Title, added, edits));
        }

        if (added.Count == 0 && edits.Count == 0)
        {
            return runtime;
        }

        return new ModdedRuntime(runtime, [.. runtime.Modules, .. added, new EditsModule(edits)]);
    }

    private sealed class Builder(InstalledMod mod, GameTitle title, List<IContentModule> modules, LevelSystemEdits edits) : IModBuilder
    {
        public ModManifest Manifest => mod.Manifest;

        public GameTitle Title => title;

        public string Directory => mod.Directory;

        public IModBuilder AddModule(IContentModule contentModule)
        {
            ArgumentNullException.ThrowIfNull(contentModule);
            modules.Add(contentModule);
            return this;
        }

        public IModBuilder MountTitleData(GameTitle other, string deviceName)
        {
            ArgumentNullException.ThrowIfNull(other);
            ArgumentException.ThrowIfNullOrWhiteSpace(deviceName);
            modules.Add(new MountTitleDataModule(mod.Manifest.Id, other, deviceName.TrimEnd(':')));
            return this;
        }

        public IModBuilder ReplaceTask(string typeName, Func<GameTask> factory, ITaskParameterAdapter? adapter = null)
        {
            ArgumentException.ThrowIfNullOrEmpty(typeName);
            ArgumentNullException.ThrowIfNull(factory);
            modules.Add(new ReplaceTaskModule(mod.Manifest.Id, typeName, factory, adapter));
            return this;
        }

        public IModBuilder EditLevelSystems<TBuild>(Action<LevelSystems<TBuild>> edit)
            where TBuild : LevelBuild
        {
            edits.Add(edit);
            return this;
        }
    }

    // A game as it ships, with the mods' modules after its own.
    private sealed class ModdedRuntime(IGameTitleRuntime inner, IReadOnlyList<IContentModule> modules) : IGameTitleRuntime
    {
        public GameTitle Title => inner.Title;

        public ITitleDataLayout Layout => inner.Layout;

        public AssetFormatProfile Formats => inner.Formats;

        public IReadOnlyList<IContentModule> Modules => modules;
    }

    // Another game's installation as a device, found through the host's locator when the session loads its tables.
    private sealed class MountTitleDataModule(string modId, GameTitle other, string deviceName) : IContentModule
    {
        public string Name => $"{modId}.Mount.{deviceName}";

        public void Register(ContentRegistrationContext context)
        {
        }

        public void LoadTables(ContentLoadContext context)
        {
            if (context.Services.TryGet(out ITitleDataLocator? locator) && locator.TryGetDataRoot(other, out string? root))
            {
                context.FileSystem.RegisterDevice(new LocalFileDevice(root, deviceName));
                return;
            }

            context.Logger.Warning("Mods", $"The mod '{modId}' reads {other.DisplayName}'s data, which is not installed; '{deviceName}:' is not mounted.");
        }
    }

    // One of the game's task types made by a mod's class, replaced once the game's own modules have registered it.
    private sealed class ReplaceTaskModule(string modId, string typeName, Func<GameTask> factory, ITaskParameterAdapter? adapter)
        : IContentModule
    {
        public string Name => $"{modId}.Replace.{typeName}";

        public void Register(ContentRegistrationContext context)
        {
            if (adapter is null)
            {
                context.Types.Replace(typeName, factory);
            }
            else
            {
                context.Types.Replace(typeName, factory, adapter);
            }
        }

        public void LoadTables(ContentLoadContext context)
        {
        }
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
