using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>What a <c>Knife</c> does when fired: <c>Knife_WeaponFireHandler</c> (<c>igi2 0x4CF8A0</c>) and <c>Knife_Stab</c> (<c>0x4CF400</c>).</summary>
/// <remarks>
/// <para>
/// The knife's traces are fanned across its trace field of view, centred on the aim, from left to right; each is a
/// two-metre line from the shot's position along the aim turned by its angle, against everything. The first that strikes
/// something stops the sweep: a human sounds a stab for a knife-kind weapon, or one of two punches by a run-stream draw for
/// anything else, and any other surface its material's knife sound; then what was struck takes the knife's damage factor
/// along the line.
/// </para>
/// <para>
/// It fires no rounds, waits its delay in ticks, kicks the view a hundredth of a degree both ways and damps it by a tenth,
/// and plays its fire sound in the fire sound's slot.
/// </para>
/// </remarks>
public sealed class Igi2KnifeFire : IIgi2FireHandler
{
    /// <summary>How far a stab reaches, three metres in world units, <c>12288</c> (<c>Knife_Stab</c>).</summary>
    public const double Reach = 12288.0;

    /// <summary>The kick a stab gives the view both ways, the float <c>0.0099999998</c>.</summary>
    public const float Kick = 0.0099999998f;

    /// <summary>How fast the stab's kick dies away, <c>0.1</c>.</summary>
    public const float KickDamping = 0.1f;

    /// <summary>The sound a knife-kind weapon makes in a human.</summary>
    public const string StabSound = "stab_01";

    private const float DegreesToRadians = 0.017453292f;

    /// <summary>What anything but a knife-kind weapon sounds in a human, one by a draw.</summary>
    private static readonly string[] PunchSounds = ["punched_01", "punched_02"];

    private readonly Igi2LineTrace _trace = new(1);

    /// <inheritdoc/>
    public bool HasRelease => false;

    /// <inheritdoc/>
    public Igi2FireResult Fire(in Igi2FireRequest request)
    {
        Igi2KnifeTask knife = (Igi2KnifeTask)request.FireTypeParameters;
        float step = ((new Fpu(knife.TraceFOV) / (double)knife.Traces) * DegreesToRadians).F;
        float angle = ((new Fpu(knife.TraceFOV) * -0.5f) * DegreesToRadians).F;
        for (int trace = 0; trace < knife.Traces; trace++)
        {
            if (Stab(request, knife, angle))
            {
                break;
            }

            angle = (new Fpu(angle) + step).F;
        }

        int delay = Fpu.ToInt32(new Fpu(knife.DelaySeconds) * 30f);
        request.Weapon.Effects.PlaySound(Igi2WeaponFiring.FireSoundSlot, knife.FireSound, looped: false);
        return new Igi2FireResult(0, delay, KickDamping, 1f, Kick, Kick);
    }

    /// <inheritdoc/>
    public Igi2FireResult Release(in Igi2FireRequest request) => default;

    /// <summary>One stab, at one angle off the aim.</summary>
    /// <returns>Whether it struck something.</returns>
    private bool Stab(in Igi2FireRequest request, Igi2KnifeTask knife, float angle)
    {
        if (request.Weapon.World is not { } world)
        {
            return false;
        }

        float[] turned = Igi2Pose.RotateGamma(request.Orientation, angle);
        Vector3f direction = new(turned[1], turned[4], turned[7]);
        Vector3d from = request.Position;
        _trace.Starts[0] = from;
        _trace.Ends[0] = new Vector3d(
            (new Fpu(from.X) + (new Fpu(direction.X) * Reach)).Value,
            (new Fpu(from.Y) + (new Fpu(direction.Y) * Reach)).Value,
            (new Fpu(from.Z) + (new Fpu(direction.Z) * Reach)).Value);
        _trace.Mesh = 0;
        _trace.TracePortals = false;
        _trace.IgnoreMaterial = Igi2LineTrace.AirMaterial;
        Igi2WeaponCollisionQuery query = new(request.Operator, null, 1f);
        _trace.Includes = owner => owner is not IIgi2WeaponCollisionTarget target || target.EvaluateWeaponCollision(query);
        world.Collision.TraceLines(_trace, uint.MaxValue);

        Igi2Intersection hit = _trace.Intersections[0];
        if (!hit.IsHit || hit.HitObject is not { } struck)
        {
            return false;
        }

        string? sound = null;
        if (world.IsHuman(struck))
        {
            sound = request.WeaponType.WeaponType == Igi2WeaponTypeKind.Knife
                ? StabSound
                : PunchSounds[(int)request.Operator!.Random.NextBounded(2)];
        }
        else if (hit.EnterMaterial is { } row)
        {
            sound = world.Materials[row]?.KnifeHitSound ?? string.Empty;
        }

        if (sound is not null)
        {
            request.Weapon.ImpactEffects.NewSound(sound, hit.Position);
        }

        if ((struck.Handler ?? struck.Owner) is IIgi2WeaponHitTarget target)
        {
            target.WeaponHit(new Igi2WeaponHit(hit, knife.DamageFactor, direction, request.Operator, request.WeaponType, null));
        }

        return true;
    }
}
