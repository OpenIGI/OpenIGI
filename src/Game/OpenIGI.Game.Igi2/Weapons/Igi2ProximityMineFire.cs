using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>Laying a proximity mine: <c>proximitymine.c ProximityMine_WeaponFireHandler</c>.</summary>
/// <remarks>
/// <para>
/// A line goes out along the aim for the fire type's effective range, against everything a weapon may strike. A surface it
/// meets takes the mine, unless it is a human, glass, a door or a vehicle: the mine lies where the line struck, its Y axis the
/// surface's normal (<see cref="Igi2Pose.FromDirection"/>), the weapon's fixed model, arming after its time and set off by a human
/// within its radius in front of it (<see cref="Igi2Mine"/>). Laying it uses a round, waits a round's time and plays the fire
/// sound in the fire sound's slot.
/// </para>
/// <para>Laid or not, the view kicks a hundredth of a degree each way, damped by a tenth.</para>
/// </remarks>
internal sealed class Igi2ProximityMineFire : IIgi2FireHandler
{
    /// <summary>The kick each way, <c>0x3C23D70A</c>.</summary>
    internal const float Kick = 0.01f;

    /// <summary>The kick's damping, <c>0x3DCCCCCD</c>.</summary>
    internal const float KickDamping = 0.1f;

    /// <summary>The kinds of vehicle a mine is not laid on: <c>Vehicle</c> and the types derived from it.</summary>
    private static readonly HashSet<string> Vehicles = new(StringComparer.Ordinal) { "Vehicle", "Car", "Plane", "Train", "Heli", "Missile" };

    private readonly Igi2LineTrace _trace = new(1);

    /// <inheritdoc/>
    public bool HasRelease => false;

    /// <inheritdoc/>
    public Igi2FireResult Fire(in Igi2FireRequest request)
    {
        Igi2ProximityMineTask mine = (Igi2ProximityMineTask)request.FireTypeParameters;
        int laid = 0;
        int delay = 0;
        if (request.Weapon.World is { } world && request.Weapon.Mines is { } mines)
        {
            float reach = ((Fpu)mine.EffectiveRangeMeters * 4096.0).F;
            float[] o = request.Orientation;
            Vector3d from = request.Position;
            _trace.Starts[0] = from;
            _trace.Ends[0] = new Vector3d(
                (((Fpu)reach * o[1]) + from.X).Value,
                (((Fpu)reach * o[4]) + from.Y).Value,
                (((Fpu)reach * o[7]) + from.Z).Value);
            _trace.Count = 1;
            _trace.Mesh = 0;
            _trace.TracePortals = false;
            _trace.IgnoreMaterial = Igi2LineTrace.AirMaterial;
            _trace.Includes = Igi2GunShot.WeaponCollisionIncludes(new Igi2WeaponCollisionQuery(request.Operator, null, 1f));
            world.Collision.TraceLines(_trace, uint.MaxValue);
            Igi2Intersection hit = _trace.Intersections[0];
            if (hit.IsHit && !Refuses(hit.HitObject, world))
            {
                Vector3f n = hit.Normal;
                if (n.X != 0f || n.Y != 0f || n.Z != 0f)
                {
                    Fpu inverse = (Fpu)1.0 / Fpu.Sqrt((((Fpu)n.X * n.X) + ((Fpu)n.Y * n.Y)) + ((Fpu)n.Z * n.Z));
                    n = new Vector3f(((Fpu)n.X * inverse).F, ((Fpu)n.Y * inverse).F, ((Fpu)n.Z * inverse).F);
                }

                int armTicks = Fpu.ToInt32((Fpu)mine.TimeBeforeArmingSeconds * 30.0);
                mines.Lay(new Igi2Mine(
                    mines,
                    hit.Position,
                    Igi2Pose.FromDirection(n),
                    request.WeaponType.FixedModel,
                    armTicks,
                    mine.CountdownTimeSeconds,
                    mine.ActivateRadiusMeters,
                    mine.ExplosionRadiusMeters,
                    isRemote: false));
                laid = 1;
                delay = Fpu.ToInt32(((Fpu)60.0 / (double)mine.RoundsPerMinute) * 30.0);
                request.Weapon.Effects.PlaySound(Igi2WeaponFiring.FireSoundSlot, mine.FireSound, looped: false);
            }
        }

        return new Igi2FireResult(laid, delay, KickDamping, 1f, Kick, Kick);
    }

    /// <inheritdoc/>
    public Igi2FireResult Release(in Igi2FireRequest request) => Igi2FireResult.Nothing;

    // A human, glass, a door or a vehicle takes no mine.
    private static bool Refuses(IIgi2Collider? struck, IIgi2GunShotWorld world) =>
        struck is not null
        && (world.IsHuman(struck)
            || struck.Owner is Igi2CollisionOwner { GlassKey: not null }
            || struck.Owner is Igi2CollisionOwner { TypeName: "Door" }
            || struck.Owner is Igi2CollisionOwner owner && Vehicles.Contains(owner.TypeName));
}
