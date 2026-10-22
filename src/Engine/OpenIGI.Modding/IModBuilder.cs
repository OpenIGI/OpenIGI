using OpenIGI.Core.Content;
using OpenIGI.Core.Titles;
using OpenIGI.Engine.Events;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Titles;
using OpenIGI.Game.Level;

namespace OpenIGI.Modding;

/// <summary>What a mod can change in a game.</summary>
public interface IModBuilder
{
    /// <summary>Gets the mod's manifest.</summary>
    ModManifest Manifest { get; }

    /// <summary>Gets the game being configured.</summary>
    GameTitle Title { get; }

    /// <summary>Gets the mod's directory, which holds its <c>mod.json</c> and whatever files it ships.</summary>
    string Directory { get; }

    /// <summary>
    /// Adds a content module, which registers after the game's own: task types (and replacements of the game's, through
    /// <see cref="TaskTypeRegistry.Replace(string, Func{GameTask})"/>), script natives, and services.
    /// </summary>
    /// <param name="contentModule">The module.</param>
    /// <returns>This builder.</returns>
    IModBuilder AddModule(IContentModule contentModule);

    /// <summary>Adds a task type of the mod's own, which the level data and scripts spawn by its name.</summary>
    /// <param name="typeName">The type's name.</param>
    /// <param name="factory">Creates an instance of the mod's class.</param>
    /// <returns>This builder.</returns>
    IModBuilder AddTask(string typeName, Func<GameTask> factory);

    /// <summary>Makes the level's tasks of one of the game's types out of a class of the mod's, after the game registers its own.</summary>
    /// <param name="typeName">The task type's name, as the level's data spawns it.</param>
    /// <param name="factory">Creates an instance of the mod's class.</param>
    /// <param name="adapter">
    /// Sets the class from the parameters the level writes for the type, when the class's own are not those; or
    /// <see langword="null"/> when the class reads the same parameters the game's did.
    /// </param>
    /// <returns>This builder.</returns>
    IModBuilder ReplaceTask(string typeName, Func<GameTask> factory, ITaskParameterAdapter? adapter = null);

    /// <summary>Adds an integer constant the game's scripts can read by name.</summary>
    /// <param name="name">The constant's name.</param>
    /// <param name="value">Its value.</param>
    /// <returns>This builder.</returns>
    IModBuilder AddConstant(string name, int value);

    /// <summary>Mounts another game's installed data as a device of its own, for the mod's content to read that game's files.</summary>
    /// <param name="title">The game whose data is mounted.</param>
    /// <param name="deviceName">The device's name, without its colon: files are read as <c>NAME:path</c>.</param>
    /// <returns>This builder.</returns>
    /// <remarks>
    /// Mounted when the session loads its tables, before any level. When the game is not installed nothing is mounted and a
    /// warning says so; what the mod reads from the device is then missing, as any absent file is.
    /// </remarks>
    IModBuilder MountTitleData(GameTitle title, string deviceName);

    /// <summary>Edits the systems that bring each of the game's levels up: inserts one, replaces one, removes one.</summary>
    /// <typeparam name="TBuild">The game's level build type, which names the game's systems.</typeparam>
    /// <param name="edit">The edit, run over a fresh list for every level.</param>
    /// <returns>This builder.</returns>
    IModBuilder EditLevelSystems<TBuild>(Action<LevelSystems<TBuild>> edit)
        where TBuild : LevelBuild;

    /// <summary>Asks to be told of one kind of event in every level the game plays.</summary>
    /// <typeparam name="TEvent">The kind, one of <c>OpenIGI.Game.Events</c>.</typeparam>
    /// <param name="listener">What is told.</param>
    /// <returns>This builder.</returns>
    IModBuilder Listen<TEvent>(GameEventListener<TEvent> listener)
        where TEvent : struct, IGameEvent;

    /// <summary>Runs once a session of the game has loaded its tables, with what the mod can read of it from then on.</summary>
    /// <param name="loaded">What runs.</param>
    /// <returns>This builder.</returns>
    /// <remarks>Every session runs it: the one played, and the menu's preview of a mission.</remarks>
    IModBuilder OnLoaded(Action<IModContext> loaded);
}
