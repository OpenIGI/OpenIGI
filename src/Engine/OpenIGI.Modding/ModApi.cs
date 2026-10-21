using System.Diagnostics.CodeAnalysis;
using OpenIGI.Core.Titles;
using OpenIGI.Engine.Titles;
using OpenIGI.Game.Level;

namespace OpenIGI.Modding;

/// <summary>The modding API's version, which a manifest names.</summary>
public static class ModApi
{
    /// <summary>The version of the API this build offers. A mod written against another is refused.</summary>
    /// <remarks>
    /// Version 1 is shipped: its surface is <c>PublicAPI.Shipped.txt</c>, which the analyzer holds the build to. Adding to it
    /// is allowed; changing or removing anything in it is a new version.
    /// </remarks>
    public const int Version = 1;
}

/// <summary>A mod's code: what it changes in a game, said once, before the game starts.</summary>
/// <remarks>
/// A mod assembly names its implementation with <see cref="OpenIgiModAttribute"/>. <see cref="Configure"/> runs once for each
/// game the mod applies to, before that game's content is registered; everything the mod changes it changes through the
/// builder, so a game played without the mod is the game as it ships.
/// </remarks>
public interface IOpenIgiMod
{
    /// <summary>Says what the mod changes in one game.</summary>
    /// <param name="builder">What the mod can change.</param>
    void Configure(IModBuilder builder);
}

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
    /// <see cref="Engine.Tasks.TaskTypeRegistry.Replace(string, Func{Engine.Tasks.GameTask})"/>), script natives, and services.
    /// </summary>
    /// <param name="contentModule">The module.</param>
    /// <returns>This builder.</returns>
    IModBuilder AddModule(IContentModule contentModule);

    /// <summary>Mounts another game's installed data as a device of its own, for the mod's content to read that game's files.</summary>
    /// <param name="title">The game whose data is mounted.</param>
    /// <param name="deviceName">The device's name, without its colon: files are read as <c>NAME:path</c>.</param>
    /// <returns>This builder.</returns>
    /// <remarks>
    /// Mounted when the session loads its tables, before any level. When the game is not installed nothing is mounted and a
    /// warning says so; what the mod reads from the device is then missing, as any absent file is.
    /// </remarks>
    IModBuilder MountTitleData(GameTitle title, string deviceName);

    /// <summary>Makes the level's tasks of one of the game's types out of a class of the mod's, after the game registers its own.</summary>
    /// <param name="typeName">The task type's name, as the level's data spawns it.</param>
    /// <param name="factory">Creates an instance of the mod's class.</param>
    /// <param name="adapter">
    /// Sets the class from the parameters the level writes for the type, when the class's own are not those; or
    /// <see langword="null"/> when the class reads the same parameters the game's did.
    /// </param>
    /// <returns>This builder.</returns>
    IModBuilder ReplaceTask(string typeName, Func<Engine.Tasks.GameTask> factory, Engine.Tasks.ITaskParameterAdapter? adapter = null);

    /// <summary>Edits the systems that bring each of the game's levels up: inserts one, replaces one, removes one.</summary>
    /// <typeparam name="TBuild">The game's level build type, which names the game's systems.</typeparam>
    /// <param name="edit">The edit, run over a fresh list for every level.</param>
    /// <returns>This builder.</returns>
    IModBuilder EditLevelSystems<TBuild>(Action<LevelSystems<TBuild>> edit)
        where TBuild : LevelBuild;
}

/// <summary>Names the class that implements a mod assembly's <see cref="IOpenIgiMod"/>.</summary>
/// <param name="modType">The class, which has a public parameterless constructor.</param>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class OpenIgiModAttribute(
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type modType) : Attribute
{
    /// <summary>Gets the class.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public Type ModType { get; } = modType ?? throw new ArgumentNullException(nameof(modType));
}

/// <summary>A mod could not be read or loaded.</summary>
public sealed class ModLoadException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="ModLoadException"/> class.</summary>
    public ModLoadException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ModLoadException"/> class.</summary>
    /// <param name="message">What went wrong.</param>
    public ModLoadException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ModLoadException"/> class.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The cause.</param>
    public ModLoadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
