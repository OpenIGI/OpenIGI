using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
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
    private readonly Igi2LineTrace _trace = new(1);

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
        _trace.Starts[0] = from;
        _trace.Ends[0] = new Vector3d(
            (((Fpu)m[1] * range) + from.X).Value,
            ((range * m[4]) + from.Y).Value,
            ((range * m[7]) + from.Z).Value);
        _trace.Mesh = 0;
        _trace.TracePortals = false;
        _trace.IgnoreMaterial = Igi2LineTrace.AirMaterial;
        WeaponCollisionQuery query = new(request.Operator, null, 1f);
        _trace.Includes = owner => owner is not IWeaponCollisionTarget target || target.EvaluateWeaponCollision(query);
        world.Collision.TraceLines(_trace, uint.MaxValue);

        Igi2Intersection hit = _trace.Intersections[0];
        if (!hit.IsHit || hit.HitObject is not { } struck)
        {
            state.LoseLock();
            return Igi2FireResult.Nothing;
        }

        object target = struck.Owner is Igi2CollisionOwner owner ? state.TaskById(owner.LevelId) ?? owner : struck.Owner;
        if (!ReferenceEquals(state.HitTask, target))
        {
            state.HitTask = target;
            state.TickFirstHit = state.LevelTick();
        }

        return Igi2FireResult.Nothing;
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
