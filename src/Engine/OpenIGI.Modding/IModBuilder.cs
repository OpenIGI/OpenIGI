using System.Text.Json.Serialization.Metadata;
using OpenIGI.Core.Content;
using OpenIGI.Core.Titles;
using OpenIGI.Engine.Titles;
using OpenIGI.Game.Level;

namespace OpenIGI.Modding;

/// <summary>What a mod can change in a game, grouped by what it changes.</summary>
/// <remarks>
/// Every member records what the mod says; nothing of the game is touched until the game starts with the mod on. The
/// groups' methods return their group, so a mod's calls to one chain.
/// </remarks>
public interface IModBuilder
{
    /// <summary>Gets the mod's manifest.</summary>
    ModManifest Manifest { get; }

    /// <summary>Gets the game being configured, one of those the manifest names.</summary>
    GameTitle Title { get; }

    /// <summary>Gets the mod's directory, which holds its <c>mod.json</c> and whatever files it ships.</summary>
    string Directory { get; }

    /// <summary>Gets the mod's settings, which the settings screen lists on its Mods page.</summary>
    ModSettings Settings { get; }

    /// <summary>Gets the task types the mod adds, and the game's it makes of its own classes.</summary>
    ModTasks Tasks { get; }

    /// <summary>Gets what the mod gives the game's scripts.</summary>
    ModScripts Scripts { get; }

    /// <summary>Gets the game's events, for the mod to be told of.</summary>
    ModEvents Events { get; }

    /// <summary>
    /// Gets what other assemblies that extend the builder keep for the mod, by key: the presenters the modding presentation
    /// library's <c>Presenters</c> adds, for one.
    /// </summary>
    IDictionary<string, object?> Properties { get; }

    /// <summary>Gets the systems that bring each of the game's levels up, for the mod to add to, replace or remove.</summary>
    /// <typeparam name="TBuild">The game's level build type, whose systems' names its game names.</typeparam>
    /// <returns>The systems, as the mod edits them.</returns>
    ModLevelSystems<TBuild> LevelSystems<TBuild>()
        where TBuild : LevelBuild;

    /// <summary>Reads a JSON file of the mod's: the player's copy in the mod's directory when there is one, else the one it embeds.</summary>
    /// <typeparam name="T">What the file holds.</typeparam>
    /// <param name="fileName">The file's name; the embedded copy is the mod assembly's resource of that name.</param>
    /// <param name="typeInfo">The file's generated JSON shape.</param>
    /// <returns>What the file holds.</returns>
    /// <exception cref="ModLoadException">Neither copy is there, or the one read is not valid.</exception>
    T ReadConfig<T>(string fileName, JsonTypeInfo<T> typeInfo);

    /// <summary>
    /// Adds a content module, which registers after the game's own: what the groups do not reach -- natives, services, tables.
    /// </summary>
    /// <param name="contentModule">The module.</param>
    /// <returns>This builder.</returns>
    IModBuilder AddModule(IContentModule contentModule);

    /// <summary>Mounts another game's installed data as a device of its own, for the mod to read that game's files.</summary>
    /// <param name="title">The game whose data is mounted.</param>
    /// <param name="deviceName">The device's name, without its colon: files are read as <c>NAME:path</c>.</param>
    /// <returns>This builder.</returns>
    /// <remarks>
    /// Mounted when the session loads its tables, before any level. When the game is not installed nothing is mounted and a
    /// warning says so; what the mod reads from the device is then missing, as any absent file is.
    /// </remarks>
    IModBuilder MountTitleData(GameTitle title, string deviceName);

    /// <summary>Runs once a session of the game has loaded its tables, with what the mod can read of it from then on.</summary>
    /// <param name="started">What runs.</param>
    /// <returns>This builder.</returns>
    /// <remarks>Every session runs it: the one played, and the menu's preview of a mission.</remarks>
    IModBuilder OnStarted(Action<IModContext> started);
}
