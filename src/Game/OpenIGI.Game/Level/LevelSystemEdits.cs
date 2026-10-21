namespace OpenIGI.Game.Level;

/// <summary>
/// Changes to a title's level systems, gathered before any level is brought up and applied to each level's list as it is made.
/// </summary>
/// <remarks>
/// A session publishes one of these as a service; the title that brings a level up asks for it and runs every edit made
/// for its build type over a fresh list of its systems, in the order the edits were added. An edit for another title's
/// build is never run. This is how a mod inserts, replaces or removes a system without the title naming the mod.
/// </remarks>
public sealed class LevelSystemEdits
{
    private readonly List<(Type Build, Delegate Edit)> _edits = [];

    /// <summary>Gets the number of edits.</summary>
    public int Count => _edits.Count;

    /// <summary>Adds an edit to the systems of every level brought up with builds of a type.</summary>
    /// <typeparam name="TBuild">The title's build type.</typeparam>
    /// <param name="edit">The edit.</param>
    /// <returns>This instance.</returns>
    public LevelSystemEdits Add<TBuild>(Action<LevelSystems<TBuild>> edit)
        where TBuild : LevelBuild
    {
        ArgumentNullException.ThrowIfNull(edit);
        _edits.Add((typeof(TBuild), edit));
        return this;
    }

    /// <summary>Runs every edit made for a build type over a list of its systems, in the order they were added.</summary>
    /// <typeparam name="TBuild">The title's build type.</typeparam>
    /// <param name="systems">The list, which is edited in place.</param>
    /// <returns>The same list.</returns>
    public LevelSystems<TBuild> Apply<TBuild>(LevelSystems<TBuild> systems)
        where TBuild : LevelBuild
    {
        ArgumentNullException.ThrowIfNull(systems);
        foreach ((Type build, Delegate edit) in _edits)
        {
            if (build == typeof(TBuild))
            {
                ((Action<LevelSystems<TBuild>>)edit)(systems);
            }
        }

        return systems;
    }
}
