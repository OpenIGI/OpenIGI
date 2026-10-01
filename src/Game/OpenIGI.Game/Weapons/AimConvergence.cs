using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Weapons;

/// <summary>
/// Works out what a crosshair is over, so a shot fired from somewhere else can be pointed at it.
/// </summary>
/// <remarks>
/// <para>
/// A third-person camera does not share the eye's sight line, so the round has to be turned onto
/// whatever the middle of the screen covers (<see cref="AimBasis"/> does the turning). This is the
/// other half: finding that thing.
/// </para>
/// <para>
/// <b>It has to look for the same things a round would meet.</b> The first attempt asked the renderer,
/// whose wall query sees placed objects and terrain and looks straight <em>through</em> people — so
/// every shot aimed at a guard converged on whatever stood behind him and landed the camera's own half
/// metre wide. Aiming at a person is the whole game, so a convergence that cannot see people is not a
/// convergence. Three sources, nearest wins:
/// </para>
/// <list type="bullet">
/// <item>the placed objects, through the same <see cref="WorldTraceMask.World"/> query a round marches;</item>
/// <item>the terrain, which the object index deliberately does not carry and which is most of what a
/// player aims at outdoors;</item>
/// <item>the humans, through the <b>non-damaging</b> probe — this is a sight line, and looking down it
/// must not hurt anybody.</item>
/// </list>
/// <para>
/// A ray that meets none of them keeps its far end, where the turn is a fraction of a degree and so is
/// no turn at all.
/// </para>
/// </remarks>
public static class AimConvergence
{
    /// <summary>Finds the nearest thing on a ray.</summary>
    /// <param name="originUnits">Where the ray starts — the camera, in engine units.</param>
    /// <param name="direction">The ray's unit direction.</param>
    /// <param name="reachInUnits">How far to look before giving up.</param>
    /// <param name="world">The placed-object trace, or <see langword="null"/> for none.</param>
    /// <param name="terrain">The ground trace, or <see langword="null"/> for none.</param>
    /// <param name="humans">The non-damaging human probe, or <see langword="null"/> for none.</param>
    /// <returns>The nearest contact, or the ray's far end when it meets nothing.</returns>
    public static Vector3d Resolve(
        Vector3d originUnits,
        Vector3f direction,
        double reachInUnits,
        WorldTraceCast? world,
        WorldTraceCast? terrain,
        HumanRayCast? humans)
    {
        Vector3d end = new(
            originUnits.X + (direction.X * reachInUnits),
            originUnits.Y + (direction.Y * reachInUnits),
            originUnits.Z + (direction.Z * reachInUnits));

        Vector3d point = end;
        double nearest = double.PositiveInfinity;

        Consider(world, WorldTraceMask.World, originUnits, end, ref point, ref nearest);
        Consider(terrain, WorldTraceMask.Terrain, originUnits, end, ref point, ref nearest);

        if (humans is not null
            && humans(originUnits, end, out HumanHit human)
            && double.IsFinite(human.Distance)
            && human.Distance >= 0.0
            && human.Distance < nearest)
        {
            point = human.Position;
        }

        return point;
    }

    private static void Consider(
        WorldTraceCast? cast,
        WorldTraceMask mask,
        Vector3d origin,
        Vector3d end,
        ref Vector3d point,
        ref double nearest)
    {
        if (cast is null
            || !cast(new WorldTraceRequest(origin, end, mask), out WorldTraceHit hit)
            || !double.IsFinite(hit.Distance)
            || hit.Distance < 0.0
            || hit.Distance >= nearest)
        {
            return;
        }

        nearest = hit.Distance;
        point = hit.RayHit.Position;
    }
}
