using OpenIGI.Core.Diagnostics;
using OpenIGI.Game.Level;

namespace OpenIGI.Modding;

/// <summary>The systems that bring each of a game's levels up, as a mod edits them.</summary>
/// <typeparam name="TBuild">The game's level build type.</typeparam>
/// <remarks>
/// The game names its systems (<c>MissionSystemNames</c>, <c>Igi2LevelSystems</c>); a mod places its own among them by those
/// names, and names its own so another mod can place itself beside them. The edits are made over a fresh list for every level,
/// in the order the mods said them.
/// <para>
/// A mod's system fails on its own: what it throws is logged and the level goes on without it, as a mod's event handlers do.
/// A mod built against another version of the game fails this way the first time its code reaches a type or member that has
/// moved, which is a mission's load; before, that ended the process.
/// </para>
/// </remarks>
public sealed class ModLevelSystems<TBuild>
    where TBuild : LevelBuild
{
    private const string LogCategory = "Mods";

    private readonly LevelSystemEdits _edits;
    private readonly string _modId;

    internal ModLevelSystems(LevelSystemEdits edits, string modId)
    {
        _edits = edits;
        _modId = modId;
    }

    /// <summary>Adds a system after every other.</summary>
    /// <param name="name">The system's name.</param>
    /// <param name="build">What it does to the level being brought up.</param>
    /// <returns>This group.</returns>
    public ModLevelSystems<TBuild> Add(string name, Action<TBuild> build) =>
        Edit(systems => systems.Add(new LevelSystem<TBuild>(name, Contain(name, build))), build);

    /// <summary>Adds a system just before one of the game's, or another mod's.</summary>
    /// <param name="existing">The system it goes before.</param>
    /// <param name="name">The system's name.</param>
    /// <param name="build">What it does to the level being brought up.</param>
    /// <returns>This group.</returns>
    public ModLevelSystems<TBuild> InsertBefore(string existing, string name, Action<TBuild> build) =>
        Edit(systems => systems.InsertBefore(existing, new LevelSystem<TBuild>(name, Contain(name, build))), build, existing);

    /// <summary>Adds a system just after one of the game's, or another mod's.</summary>
    /// <param name="existing">The system it goes after.</param>
    /// <param name="name">The system's name.</param>
    /// <param name="build">What it does to the level being brought up.</param>
    /// <returns>This group.</returns>
    public ModLevelSystems<TBuild> InsertAfter(string existing, string name, Action<TBuild> build) =>
        Edit(systems => systems.InsertAfter(existing, new LevelSystem<TBuild>(name, Contain(name, build))), build, existing);

    /// <summary>Does a system's work the mod's way instead, under its name.</summary>
    /// <param name="existing">The system replaced.</param>
    /// <param name="build">What it does instead.</param>
    /// <returns>This group.</returns>
    public ModLevelSystems<TBuild> Replace(string existing, Action<TBuild> build) =>
        Edit(systems => systems.Replace(existing, new LevelSystem<TBuild>(existing, Contain(existing, build))), build, existing);

    /// <summary>Leaves a system out of every level.</summary>
    /// <param name="existing">The system left out.</param>
    /// <returns>This group.</returns>
    public ModLevelSystems<TBuild> Remove(string existing)
    {
        ArgumentException.ThrowIfNullOrEmpty(existing);
        _edits.Add<TBuild>(systems => systems.Remove(existing));
        return this;
    }

    // The mod's work, run so that its failure is the mod's alone: logged, and the level brought up without it.
    private Action<TBuild> Contain(string name, Action<TBuild> build) => level =>
    {
        try
        {
            build(level);
        }
#pragma warning disable CA1031 // Code the engine does not own fails on its own; the level goes on.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // These are what code compiled against another build of the game throws as it is first run.
            string why = ex is TypeLoadException or MissingMemberException or FileNotFoundException or FileLoadException
                ? " It was built for another version of OpenIGI: update the mod, or turn it off."
                : string.Empty;
            level.Logger.Error(LogCategory, $"The mod '{_modId}' failed bringing the level up in '{name}', which is left out: {ex.Message}.{why}", ex);
        }
    };

    private ModLevelSystems<TBuild> Edit(Action<LevelSystems<TBuild>> edit, Action<TBuild> build, string? existing = null)
    {
        ArgumentNullException.ThrowIfNull(build);
        if (existing is not null)
        {
            ArgumentException.ThrowIfNullOrEmpty(existing);
        }

        _edits.Add(edit);
        return this;
    }
}
