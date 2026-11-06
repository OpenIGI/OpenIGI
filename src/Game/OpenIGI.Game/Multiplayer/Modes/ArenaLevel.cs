using OpenIGI.Core.Numerics;
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
    public static int RemoveSoldiers(GameTask root) => RemoveSoldiers(root, []);

    /// <summary>Takes a level's soldiers and soldier generators out of it, noting where each soldier stood.</summary>
    /// <param name="root">The level's root task.</param>
    /// <param name="stood">Where the soldiers stood, added to: the level's own ground a player can stand on.</param>
    /// <returns>How many were taken out.</returns>
    public static int RemoveSoldiers(GameTask root, List<Vector3d> stood)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(stood);
        List<GameTask> doomed = [.. root.Descendants().Where(IsSoldierOrMaker)];
        foreach (GameTask task in doomed)
        {
            if (task.TryGetScriptValue("Position", out object? at) && at is Vector3d position)
            {
                stood.Add(position);
            }
        }

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

    /// <summary>
    /// Lays out a level nobody laid out by hand, around where its soldiers stand thickest -- the base a campaign level is
    /// fought over: its players start where those soldiers stood, the two teams at the two ends of that ground, and play is
    /// the box around it a margin wide. Soldiers posted far from it (a lone patrol, a sniper on a far ridge, one below the
    /// level) and the level's own start (often a long walk away) are no part of it.
    /// </summary>
    /// <param name="title">The game.</param>
    /// <param name="map">The level.</param>
    /// <param name="stood">Where the level's soldiers stood.</param>
    /// <returns>The layout, or <see langword="null"/> for a level with too few soldiers together to lay one out from.</returns>
    public static MpLayout? Generated(string title, string map, IReadOnlyList<Vector3d> stood)
    {
        ArgumentNullException.ThrowIfNull(stood);
        if (stood.Count == 0)
        {
            return null;
        }

        // The heart of the level: the soldier with the most others near it.
        Vector3d heart = stood.MaxBy(at => stood.Count(other => Near(at, other, ClusterRadius)));
        List<Vector3d> spots = [];
        foreach (Vector3d at in stood.Where(at => Near(heart, at, ArenaRadius)))
        {
            if (spots.All(s => Vector3d.Distance(s, at) > MinimumSpawnSpacing))
            {
                spots.Add(at);
            }
        }

        if (spots.Count < 4)
        {
            return null;
        }

        double minX = spots.Min(s => s.X), maxX = spots.Max(s => s.X);
        double minY = spots.Min(s => s.Y), maxY = spots.Max(s => s.Y);
        double minZ = spots.Min(s => s.Z), maxZ = spots.Max(s => s.Z);

        // The teams at the two ends of the longer side, split at its middle.
        bool alongX = maxX - minX >= maxY - minY;
        double middle = alongX ? (minX + maxX) / 2 : (minY + maxY) / 2;
        MpSpawn[] spawns = [.. spots.Select(s => new MpSpawn(
            [s.X, s.Y, s.Z], 0, (alongX ? s.X : s.Y) < middle ? TeamRoster.Good : TeamRoster.Bad))];
        return new MpLayout
        {
            Title = title,
            Map = map,
            Name = $"Mission {map}",
            Bounds = [new MpBox([minX - Margin, minY - Margin, minZ - Margin], [maxX + Margin, maxY + Margin, maxZ + (Margin * 2)])],
            Spawns = spawns,
        };
    }

    // Within a reach across the ground, and not far above or below.
    private static bool Near(Vector3d a, Vector3d b, double reach) =>
        Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y))) <= reach && Math.Abs(a.Z - b.Z) <= HeightReach;

    // Spawns closer than this are one; the box reaches this far past the outermost.
    private const double MinimumSpawnSpacing = 4 * WorldUnits.PerMetre;
    private const double Margin = 30 * WorldUnits.PerMetre;

    // What counts as near in finding the level's heart, how far from it the arena reaches, and how far up or down.
    private const double ClusterRadius = 60 * WorldUnits.PerMetre;
    private const double ArenaRadius = 150 * WorldUnits.PerMetre;
    private const double HeightReach = 40 * WorldUnits.PerMetre;

    private static bool IsSoldierOrMaker(GameTask task) =>
        task is GuardGeneratorTask || HumanSoldierClass.All.Any(soldier => string.Equals(soldier.TypeName, task.Type.Name, StringComparison.Ordinal));
}
