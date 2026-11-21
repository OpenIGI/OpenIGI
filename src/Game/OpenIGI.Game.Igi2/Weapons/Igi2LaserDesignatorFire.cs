using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Physics;
using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>
/// The laser designator's fire, <c>LaserDesignator_Fire</c> (laserdesignator.c): while the trigger is held, a line from the holder's eyes
/// along its view for the designator's range, past the holder itself. What it strikes is taken up to the task that placed it
/// (<c>LaserDesignator_ResolveHitQTask</c>), and the level tick it first came to rest there is noted; striking nothing forgets it. The
/// weapon's dot is on while it is held, and letting go forgets the target and turns the dot off (<c>LaserDesignator_WeaponReleaseHandler</c>).
/// </summary>
internal sealed class Igi2LaserDesignatorFire : IIgi2FireHandler
{
    private readonly WorldLineTrace _trace = new(1);

    /// <inheritdoc/>
    public bool HasRelease => true;

    /// <inheritdoc/>
    public Igi2FireResult Fire(in Igi2FireRequest request)
    {
        if (request.Weapon.Inventory.Current is { } item)
        {
            item.IsLaserDotOn = true;
        }

        if (request.Weapon.LaserDesignator is not { } state || request.Weapon.World is not { } world)
        {
            return Igi2FireResult.Nothing;
        }

        Igi2LaserDesignatorTask designator = (Igi2LaserDesignatorTask)request.FireTypeParameters;
        float[] m = request.Orientation;
        Fpu range = (Fpu)designator.Range * 4096.0;
        Vector3d from = request.Position;
        Vector3d to = new(
            (((Fpu)m[1] * range) + from.X).Value,
            ((range * m[4]) + from.Y).Value,
            ((range * m[7]) + from.Z).Value);
        Designate(state, world, _trace, from, to, request.Operator);
        return Igi2FireResult.Nothing;
    }

    /// <summary>
    /// Rests the designator on whatever a line meets first: the task that placed it is the target, and the level tick the line
    /// first came to rest there is noted; a line that meets nothing forgets the target.
    /// </summary>
    /// <param name="state">The designator's target, which the level's scripts read.</param>
    /// <param name="world">The level's world.</param>
    /// <param name="trace">A one-line trace to cast with.</param>
    /// <param name="from">Where the line starts: the holder's eyes.</param>
    /// <param name="to">Where it ends: the designator's range along the view.</param>
    /// <param name="holder">Who holds it, which the line passes.</param>
    internal static void Designate(
        Igi2LaserDesignatorState state, IIgi2GunShotWorld world, WorldLineTrace trace, Vector3d from, Vector3d to, object? holder)
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

        WorldIntersection hit = trace.Intersections[0];
        if (!hit.IsHit || hit.HitObject is not { } struck)
        {
            state.LoseLock();
            return;
        }

        object target = struck.Owner is Igi2CollisionOwner owner ? state.TaskById(owner.LevelId) ?? owner : struck.Owner;
        if (!ReferenceEquals(state.HitTask, target))
        {
            state.HitTask = target;
            state.TickFirstHit = state.LevelTick();
        }
    }

    /// <inheritdoc/>
    public Igi2FireResult Release(in Igi2FireRequest request)
    {
        request.Weapon.LaserDesignator?.LoseLock();
        if (request.Weapon.Inventory.Current is { } item)
        {
            item.IsLaserDotOn = false;
        }

        return Igi2FireResult.Nothing;
    }
}
