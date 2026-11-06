using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Multiplayer.Modes;

/// <summary>A campaign level made into a multiplayer arena: its soldiers and the generators that make more taken out of it.</summary>
/// <remarks>
/// Done to the loaded task tree before the soldiers are read from it, so nothing of them is made at all; the soldier
/// simulation still runs (it is what a round meets a player's body through) with nobody of its own. The level's doors, lifts,
/// pickups and the rest stay.
/// </remarks>
public static class ArenaLevel
{
    /// <summary>Gets whether a mode plays players against each other, so a level it is played on is an arena.</summary>
    /// <param name="mode">The mode's id.</param>
    /// <returns><see langword="true"/> for the versus modes.</returns>
    public static bool IsVersus(string? mode) => mode is GameModes.TeamDeathmatch or GameModes.FreeForAll;

    /// <summary>Takes a level's soldiers and soldier generators out of it.</summary>
    /// <param name="root">The level's root task.</param>
    /// <returns>How many were taken out.</returns>
    public static int RemoveSoldiers(GameTask root)
    {
        ArgumentNullException.ThrowIfNull(root);
        List<GameTask> doomed = [.. root.Descendants().Where(IsSoldierOrMaker)];

        // A generator's soldiers lie beneath it: deleting it takes them with it, so only the outermost of each is deleted.
        foreach (GameTask task in doomed)
        {
            if (!task.IsDeleted)
            {
                root.Tree.Delete(task);
            }
        }

        return doomed.Count;
    }

    private static bool IsSoldierOrMaker(GameTask task) =>
        task is GuardGeneratorTask || HumanSoldierClass.All.Any(soldier => string.Equals(soldier.TypeName, task.Type.Name, StringComparison.Ordinal));
}
