using OpenIGI.Game.Level;

namespace OpenIGI.Modding;

/// <summary>The systems that bring each of a game's levels up, as a mod edits them.</summary>
/// <typeparam name="TBuild">The game's level build type.</typeparam>
/// <remarks>
/// The game names its systems (<c>MissionSystemNames</c>, <c>Igi2LevelSystems</c>); a mod places its own among them by those
/// names, and names its own so another mod can place itself beside them. The edits are made over a fresh list for every level,
/// in the order the mods said them.
/// </remarks>
public sealed class ModLevelSystems<TBuild>
    where TBuild : LevelBuild
{
    private readonly LevelSystemEdits _edits;

    internal ModLevelSystems(LevelSystemEdits edits) => _edits = edits;

    /// <summary>Adds a system after every other.</summary>
    /// <param name="name">The system's name.</param>
    /// <param name="build">What it does to the level being brought up.</param>
    /// <returns>This group.</returns>
    public ModLevelSystems<TBuild> Add(string name, Action<TBuild> build) =>
        Edit(systems => systems.Add(new LevelSystem<TBuild>(name, build)), build);

    /// <summary>Adds a system just before one of the game's, or another mod's.</summary>
    /// <param name="existing">The system it goes before.</param>
    /// <param name="name">The system's name.</param>
    /// <param name="build">What it does to the level being brought up.</param>
    /// <returns>This group.</returns>
    public ModLevelSystems<TBuild> InsertBefore(string existing, string name, Action<TBuild> build) =>
        Edit(systems => systems.InsertBefore(existing, new LevelSystem<TBuild>(name, build)), build, existing);

    /// <summary>Adds a system just after one of the game's, or another mod's.</summary>
    /// <param name="existing">The system it goes after.</param>
    /// <param name="name">The system's name.</param>
    /// <param name="build">What it does to the level being brought up.</param>
    /// <returns>This group.</returns>
    public ModLevelSystems<TBuild> InsertAfter(string existing, string name, Action<TBuild> build) =>
        Edit(systems => systems.InsertAfter(existing, new LevelSystem<TBuild>(name, build)), build, existing);

    /// <summary>Does a system's work the mod's way instead, under its name.</summary>
    /// <param name="existing">The system replaced.</param>
    /// <param name="build">What it does instead.</param>
    /// <returns>This group.</returns>
    public ModLevelSystems<TBuild> Replace(string existing, Action<TBuild> build) =>
        Edit(systems => systems.Replace(existing, new LevelSystem<TBuild>(existing, build)), build, existing);

    /// <summary>Leaves a system out of every level.</summary>
    /// <param name="existing">The system left out.</param>
    /// <returns>This group.</returns>
    public ModLevelSystems<TBuild> Remove(string existing)
    {
        ArgumentException.ThrowIfNullOrEmpty(existing);
        _edits.Add<TBuild>(systems => systems.Remove(existing));
        return this;
    }

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
