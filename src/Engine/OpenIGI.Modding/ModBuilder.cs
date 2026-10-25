using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using OpenIGI.Core.Configuration;
using OpenIGI.Core.Content;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Titles;
using OpenIGI.Engine.Titles;
using OpenIGI.Game.Level;

namespace OpenIGI.Modding;

/// <summary>What one mod says it changes in one game, gathered into the content modules the game's sessions register.</summary>
/// <remarks>
/// The task types, constants, mounts, listeners and start hooks the mod names go into one module of its own, made where the
/// first of them is said, so they register in the order the mod said them among the modules it adds itself.
/// </remarks>
internal sealed class ModBuilder : IModBuilder
{
    private readonly InstalledMod _mod;
    private readonly List<IContentModule> _modules;
    private readonly LevelSystemEdits _edits;
    private ModContentModule? _own;

    /// <summary>Initializes a new instance of the <see cref="ModBuilder"/> class.</summary>
    /// <param name="mod">The mod.</param>
    /// <param name="title">The game.</param>
    /// <param name="modules">The modules the mods add, in order.</param>
    /// <param name="edits">The level-system edits the mods make.</param>
    /// <param name="settings">The registry the mod's settings are declared in.</param>
    public ModBuilder(InstalledMod mod, GameTitle title, List<IContentModule> modules, LevelSystemEdits edits, CVarRegistry settings)
    {
        _mod = mod;
        Title = title;
        _modules = modules;
        _edits = edits;
        Settings = new ModSettings(mod.Manifest.Id, settings);
        Tasks = new ModTasks(Own);
        Scripts = new ModScripts(Own);
        Events = new ModEvents(Own);
    }

    public ModManifest Manifest => _mod.Manifest;

    public GameTitle Title { get; }

    public string Directory => _mod.Directory;

    public ModSettings Settings { get; }

    public ModTasks Tasks { get; }

    public ModScripts Scripts { get; }

    public ModEvents Events { get; }

    public IDictionary<string, object?> Properties { get; } = new Dictionary<string, object?>(StringComparer.Ordinal);

    public ModLevelSystems<TBuild> LevelSystems<TBuild>()
        where TBuild : LevelBuild => new(_edits);

    public T ReadConfig<T>(string fileName, JsonTypeInfo<T> typeInfo)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        ArgumentNullException.ThrowIfNull(typeInfo);
        string path = Path.Combine(Directory, fileName);
        try
        {
            if (!string.IsNullOrEmpty(Directory) && File.Exists(path))
            {
                using FileStream file = File.OpenRead(path);
                return JsonSerializer.Deserialize(file, typeInfo) ?? throw new ModLoadException($"'{fileName}' is empty.");
            }

            System.Reflection.Assembly assembly = _mod.Code?.GetType().Assembly ?? typeof(T).Assembly;
            using Stream stream = assembly.GetManifestResourceStream(fileName)
                ?? throw new ModLoadException($"'{fileName}' is neither beside the mod nor embedded in {assembly.GetName().Name}.");
            return JsonSerializer.Deserialize(stream, typeInfo) ?? throw new ModLoadException($"'{fileName}' is empty.");
        }
        catch (JsonException ex)
        {
            throw new ModLoadException($"'{fileName}' is not valid: {ex.Message}", ex);
        }
    }

    public IModBuilder AddModule(IContentModule contentModule)
    {
        ArgumentNullException.ThrowIfNull(contentModule);
        _modules.Add(contentModule);
        return this;
    }

    public IModBuilder MountTitleData(GameTitle title, string deviceName)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceName);
        string device = deviceName.TrimEnd(':');
        Own().Loads.Add(context =>
        {
            if (context.TryGetDataRoot(title, out string? root))
            {
                context.FileSystem.RegisterDevice(new Engine.Vfs.LocalFileDevice(root, device));
                return;
            }

            context.Logger.Warning(
                ModContentModule.LogCategory,
                $"The mod '{_mod.Manifest.Id}' reads {title.DisplayName}'s data, which is not installed; '{device}:' is not mounted.");
        });
        return this;
    }

    public IModBuilder OnStarted(Action<IModContext> started)
    {
        ArgumentNullException.ThrowIfNull(started);
        Own().Loads.Add(started);
        return this;
    }

    // The mod's own module, made where the mod first needs it.
    private ModContentModule Own()
    {
        if (_own is null)
        {
            _own = new ModContentModule(_mod, Title);
            _modules.Add(_own);
        }

        return _own;
    }
}
