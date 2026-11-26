using OpenIGI.Core.Numerics;
using OpenIGI.Game.Physics;
using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>
/// A weapon's line of sight to what it points at, its laser designator's or its laser dot's: one line through the level's
/// world past the weapon's holder, everything a bullet would strike stopping it at the full chance of striking
/// (<c>World_TraceLines</c> with <c>GameFunctions_eWeaponCollisionEvaluateQTaskEvent</c>, the holder ignored, the hit probability one).
/// </summary>
internal static class Igi2WeaponSightLine
{
    /// <summary>Casts the line and gives what it met first.</summary>
    /// <param name="world">The level's world.</param>
    /// <param name="trace">A one-line trace to cast with.</param>
    /// <param name="from">Where the line starts.</param>
    /// <param name="to">Where it ends.</param>
    /// <param name="holder">Who holds the weapon, which the line passes.</param>
    /// <returns>The line's intersection, <see cref="WorldIntersection.IsHit"/> false when it met nothing.</returns>
    public static WorldIntersection Cast(IIgi2GunShotWorld world, WorldLineTrace trace, Vector3d from, Vector3d to, object? holder)
    {
        trace.Starts[0] = from;
        trace.Ends[0] = to;
        trace.Mesh = 0;
        trace.TracePortals = false;
        trace.IgnoreMaterial = WorldLineTrace.AirMaterial;
        WeaponCollisionQuery query = new(holder, null, 1f);
        trace.Includes = owner => !ReferenceEquals(owner, holder)
            && (owner is not IWeaponCollisionTarget target || target.EvaluateWeaponCollision(query));
        world.Collision.TraceLines(trace, uint.MaxValue);
        return trace.Intersections[0];
    }
}
