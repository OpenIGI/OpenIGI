using System.Diagnostics.CodeAnalysis;
using OpenIGI.Core.Titles;
using OpenIGI.Engine.Titles;
using OpenIGI.Game.Level;

namespace OpenIGI.Modding;

/// <summary>The modding API's version, which a manifest names.</summary>
public static class ModApi
{
    /// <summary>The version of the API this build offers. A mod written against another is refused.</summary>
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

    /// <summary>
    /// Adds a content module, which registers after the game's own: task types (and replacements of the game's, through
    /// <see cref="Engine.Tasks.TaskTypeRegistry.Replace(string, Func{Engine.Tasks.GameTask})"/>), script natives, and services.
    /// </summary>
    /// <param name="contentModule">The module.</param>
    /// <returns>This builder.</returns>
    IModBuilder AddModule(IContentModule contentModule);

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
