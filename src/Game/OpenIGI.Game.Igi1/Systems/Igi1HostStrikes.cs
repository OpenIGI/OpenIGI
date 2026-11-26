using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Physics;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Systems;

/// <summary>
/// What a Project I.G.I. round does to the things another game's level holds: the level's own collision is asked what the
/// round met, and that takes the round through its collider's handler (<see cref="IWorldCollider.Handler"/>) as a hit both
/// games know (<see cref="WeaponHit"/>).
/// </summary>
/// <remarks>
/// The soldiers' rounds march through the level (<see cref="Igi1HostRounds"/>) and the player's are resolved against its
/// placed objects (<c>PlayerShotResolver</c>); either way, a pane of glass, a camera or a barrel of the level's is the level's
/// to break, and it is told here.
/// </remarks>
/// <param name="world">The level's collision.</param>
internal sealed class Igi1HostStrikes(IHostCollisionWorld world)
{
    private readonly WorldLineTrace _trace = new(1);

    /// <summary>Hands a round to what it struck of the level's.</summary>
    /// <param name="target">What takes the hit.</param>
    /// <param name="position">Where it was struck, in world units.</param>
    /// <param name="normal">The surface's normal there.</param>
    /// <param name="damage">The round's damage, scaled to the level's game.</param>
    /// <param name="travel">The way the round was going; any length.</param>
    /// <param name="shooter">Who fired it.</param>
    /// <param name="category">The kind of weapon it came from.</param>
    public static void Deliver(
        IWeaponHitTarget target, Vector3d position, Vector3f normal, float damage, Vector3f travel, object? shooter, WeaponCategory category)
    {
        ArgumentNullException.ThrowIfNull(target);
        float length = MathF.Sqrt((travel.X * travel.X) + (travel.Y * travel.Y) + (travel.Z * travel.Z));
        Vector3f along = length > 0f ? new Vector3f(travel.X / length, travel.Y / length, travel.Z / length) : default;
        target.WeaponHit(new WeaponHit(position, normal, damage, along, shooter, Context: null, category));
    }

    /// <summary>
    /// Casts a round the level's humans did not stop through the level's collision, as far as it went, and has the first thing it
    /// met take it. A human of either game is left out: those are met body by body before this is asked.
    /// </summary>
    /// <param name="origin">Where the round started, in world units.</param>
    /// <param name="end">Where it would stop with nothing in its way.</param>
    /// <param name="damage">The round's damage, scaled to the level's game.</param>
    /// <param name="shooter">Who fired it, whose own body the round does not meet.</param>
    /// <param name="category">The kind of weapon it came from.</param>
    /// <returns><see langword="true"/> when something of the level's took the round.</returns>
    public bool Strike(Vector3d origin, Vector3d end, float damage, object? shooter, WeaponCategory category)
    {
        _trace.Starts[0] = origin;
        _trace.Ends[0] = end;
        _trace.Includes = owner => !ReferenceEquals(owner, shooter) && owner is not IAiHuman;
        world.TraceLines(_trace);
        WorldIntersection met = _trace.Intersections[0];
        if (!met.IsHit || met.HitObject?.Handler is not IWeaponHitTarget target)
        {
            return false;
        }

        Vector3d travel = end - origin;
        Deliver(target, met.Position, met.Normal, damage, new Vector3f((float)travel.X, (float)travel.Y, (float)travel.Z), shooter, category);
        return true;
    }
}
