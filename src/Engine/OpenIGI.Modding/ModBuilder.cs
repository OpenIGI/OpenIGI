using OpenIGI.Core.Content;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Titles;
using OpenIGI.Engine.Events;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Titles;
using OpenIGI.Game.Level;

namespace OpenIGI.Modding;

/// <summary>What one mod says it changes in one game, gathered into the content modules the game's sessions register.</summary>
/// <remarks>
/// The task types, constants, mounts, listeners and loaded hooks the mod names go into one module of its own, made where the
/// first of them is said, so they register in the order the mod said them among the modules it adds itself.
/// </remarks>
/// <param name="mod">The mod.</param>
/// <param name="title">The game.</param>
/// <param name="modules">The modules the mods add, in order.</param>
/// <param name="edits">The level-system edits the mods make.</param>
internal sealed class ModBuilder(InstalledMod mod, GameTitle title, List<IContentModule> modules, LevelSystemEdits edits) : IModBuilder
{
    private ModContentModule? _own;

    public ModManifest Manifest => mod.Manifest;

    public GameTitle Title => title;

    public string Directory => mod.Directory;

    public IModBuilder AddModule(IContentModule contentModule)
    {
        ArgumentNullException.ThrowIfNull(contentModule);
        modules.Add(contentModule);
        return this;
    }

    public IModBuilder AddTask(string typeName, Func<GameTask> factory)
    {
        ArgumentException.ThrowIfNullOrEmpty(typeName);
        ArgumentNullException.ThrowIfNull(factory);
        Own().Registrations.Add(context => context.Types.Register(typeName, factory));
        return this;
    }

    public IModBuilder ReplaceTask(string typeName, Func<GameTask> factory, ITaskParameterAdapter? adapter = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(typeName);
        ArgumentNullException.ThrowIfNull(factory);
        Own().Registrations.Add(context =>
        {
            if (adapter is null)
            {
                context.Types.Replace(typeName, factory);
            }
            else
            {
                context.Types.Replace(typeName, factory, adapter);
            }
        });
        return this;
    }

    public IModBuilder AddConstant(string name, int value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Own().Registrations.Add(context => context.Natives.RegisterConstant(name, value));
        return this;
    }

    public IModBuilder MountTitleData(GameTitle other, string deviceName)
    {
        ArgumentNullException.ThrowIfNull(other);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceName);
        string device = deviceName.TrimEnd(':');
        Own().Loads.Add(context =>
        {
            if (context.TryGetDataRoot(other, out string? root))
            {
                context.FileSystem.RegisterDevice(new Engine.Vfs.LocalFileDevice(root, device));
                return;
            }

            context.Logger.Warning(
                ModContentModule.LogCategory,
                $"The mod '{mod.Manifest.Id}' reads {other.DisplayName}'s data, which is not installed; '{device}:' is not mounted.");
        });
        return this;
    }

    public IModBuilder EditLevelSystems<TBuild>(Action<LevelSystems<TBuild>> edit)
        where TBuild : LevelBuild
    {
        edits.Add(edit);
        return this;
    }

    public IModBuilder Listen<TEvent>(GameEventListener<TEvent> listener)
        where TEvent : struct, IGameEvent
    {
        ArgumentNullException.ThrowIfNull(listener);
        Own().Registrations.Add(context => context.Services.GetOrProvide(() => new GameEvents(context.Logger)).Subscribe(listener));
        return this;
    }

    public IModBuilder OnLoaded(Action<IModContext> loaded)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        Own().Loads.Add(loaded);
        return this;
    }

    // The mod's own module, made where the mod first needs it.
    private ModContentModule Own()
    {
        if (_own is null)
        {
            _own = new ModContentModule(mod, title);
            modules.Add(_own);
        }

        return _own;
    }
}
