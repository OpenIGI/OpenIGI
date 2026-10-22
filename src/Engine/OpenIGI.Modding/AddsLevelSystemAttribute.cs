using OpenIGI.Game.Level;

namespace OpenIGI.Modding;

/// <summary>Makes a method of a mod's class one of the systems that bring each of a game's levels up.</summary>
/// <typeparam name="TBuild">The game's level build, which the method takes: <c>MissionLevelBuild</c> or <c>Igi2LevelBuild</c>.</typeparam>
/// <remarks>
/// The method, static or not, takes the build: <c>void Name(TBuild build)</c>. Exactly one of <see cref="Before"/>,
/// <see cref="After"/> and <see cref="Replaces"/> places it among the game's systems, by their names.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class AddsLevelSystemAttribute<TBuild> : Attribute
    where TBuild : LevelBuild
{
    /// <summary>Gets or sets the name of the game's system this one runs just before.</summary>
    public string? Before { get; set; }

    /// <summary>Gets or sets the name of the game's system this one runs just after.</summary>
    public string? After { get; set; }

    /// <summary>Gets or sets the name of the game's system this one runs in place of.</summary>
    public string? Replaces { get; set; }

    /// <summary>Gets or sets the system's own name, which other mods place theirs by; the class's and the method's names by default.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the id of the game this is for, or <see langword="null"/> for every game the mod applies to.</summary>
    public string? Title { get; set; }
}
