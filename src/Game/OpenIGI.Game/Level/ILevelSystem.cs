namespace OpenIGI.Game.Level;

/// <summary>
/// One concern of bringing a level up: it finds the tasks it is about in the tree, hands them what they need, and leaves on
/// the build what later systems use.
/// </summary>
/// <typeparam name="TBuild">The build it reads: a title's own, or <see cref="LevelBuild"/> for a system that runs in any title.</typeparam>
/// <remarks>
/// The systems run once, in their list's order (<see cref="LevelSystems{TBuild}"/>), after the tree is loaded and before the
/// first tick -- the original's <c>UpdateInternalData</c> and <c>AfterLoad</c> handlers. The order is the contract: a system
/// that reads what another leaves must come after it, and a mod inserts its own by the names of those around it.
/// </remarks>
public interface ILevelSystem<in TBuild>
    where TBuild : LevelBuild
{
    /// <summary>Gets the system's name, which a list is ordered and edited by.</summary>
    string Name { get; }

    /// <summary>Brings this system's part of the level up.</summary>
    /// <param name="build">The level being built.</param>
    void Build(TBuild build);
}
