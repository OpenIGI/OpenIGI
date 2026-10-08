using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Weapons;

/// <summary>What a shot did when it met a surface.</summary>
public enum GunShotImpact
{
    /// <summary>The shot stopped here.</summary>
    Stopped,

    /// <summary>The shot went through and carries on with its remaining range.</summary>
    Penetrated,

    /// <summary>The shot glanced off and carries on in a reflected direction.</summary>
    Deflected,
}

/// <summary>
/// How a round travels and what stops it, from <c>GunShot</c>'s per-tick (<c>0x47B750</c>).
/// </summary>
/// <remarks>
/// <para>
/// A round is <b>hit-scan and resolved entirely in the tick it is fired</b>. There is no projectile
/// flying over several ticks: the update loops <c>while (remainingRange &gt; 0)</c>, casting a ray from
/// the current position to the end of the remaining range, and each surface it meets either stops it,
/// is penetrated, or deflects it. What persists to later ticks is only the visible tracer and the
/// decals.
/// </para>
/// <para>
/// That is why <see cref="WeaponType.MuzzleVelocity"/> plays no part in any of this — nothing here
/// reads it. It is carried for the HUD and the AI, not for the bullet.
/// </para>
/// </remarks>
public static class GunShotBallistics
{
    /// <summary>How many rounds may be in flight at once.</summary>
    /// <remarks>
    /// Fifteen. A sixteenth is <b>silently dropped</b> — <c>0x47B190</c> takes the early exit that only
    /// bumps the total-fired counter, so the round is counted, consumes its ammunition, and never
    /// exists. With the fastest weapon in the game emptying a magazine in 87 ticks this is not usually
    /// reachable, but a shotgun firing several projectiles per round can get close.
    /// </remarks>
    public const int MaximumLiveShots = 15;

    /// <summary>How many impacts one round may record for decals.</summary>
    /// <remarks>Ten. Impacts past that are resolved but leave no mark.</remarks>
    public const int MaximumRecordedImpacts = 10;

    /// <summary>World units in a metre, used to turn the effective range into a distance.</summary>
    /// <remarks>
    /// <c>0x47B190</c> stores <c>EffectiveRange * 4096.0</c> as the round's starting range, so the
    /// parameter really is in metres.
    /// </remarks>
    public const float UnitsPerMetre = 4096f;

    /// <summary>How far past a penetrated surface the next ray starts.</summary>
    /// <remarks>
    /// <c>20.48</c> units — five millimetres. Without it the next cast would hit the same face again
    /// and the loop would not terminate.
    /// </remarks>
    public const float PenetrationStepOffUnits = 20.48f;

    /// <summary>The chance a round gets through a surface it exactly matches.</summary>
    /// <remarks>Spelled <c>0.60000002</c>.</remarks>
    public const float EqualPenetrationChance = 0.60000002f;

    /// <summary>How many rounds go by between visible tracers.</summary>
    /// <remarks>
    /// Ten. A round that deflects or penetrates draws one regardless of the counter, so the visible
    /// rate is a little above one in ten in a firefight.
    /// </remarks>
    public const int TracerInterval = 10;

    /// <summary>How many ticks a drawn tracer lives.</summary>
    public const int TracerLifetimeTicks = 30;

    /// <summary>Turns a weapon's effective range into the distance a round may travel.</summary>
    /// <param name="weapon">The weapon fired.</param>
    /// <returns>The range in world units.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="weapon"/> is <see langword="null"/>.</exception>
    public static float RangeInUnits(WeaponType weapon)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        return weapon.EffectiveRange * UnitsPerMetre;
    }

    /// <summary>Decides whether a round gets through what it hit.</summary>
    /// <param name="penetrationPower">The weapon's <see cref="WeaponType.PenetrationPower"/>.</param>
    /// <param name="surfaceResistance">What the surface or target resists with.</param>
    /// <param name="uniformSample">A uniform sample on 0–1, drawn only when the two are equal.</param>
    /// <returns><see langword="true"/> when the round carries on.</returns>
    /// <remarks>
    /// <para>
    /// The rule is not a threshold, and it is not monotonic. A margin of <b>one or more</b> always gets
    /// through; a margin of <b>exactly zero</b> gets through six times in ten; and <i>anything else</i>
    /// stops — including a margin strictly between zero and one, which is a narrower gap than the equal
    /// case that succeeds. That looks like an oversight in the original, but it is what it does, so it
    /// is what this does.
    /// </para>
    /// <para>
    /// The original also requires the ray to have named an object before it will let a round through, so
    /// a hit on nothing in particular stops it whatever the margin.
    /// </para>
    /// </remarks>
    public static bool Penetrates(float penetrationPower, float surfaceResistance, float uniformSample)
    {
        float margin = penetrationPower - surfaceResistance;

        if (margin == 0f)
        {
            return uniformSample < EqualPenetrationChance;
        }

        return margin >= 1f;
    }

    /// <summary>The chance a round glances off rather than biting.</summary>
    /// <param name="grazeLimitDegrees">
    /// The surface's own limit, in degrees. Zero means the surface never deflects anything.
    /// </param>
    /// <param name="incidenceRadians">
    /// The angle between the reversed travel direction and the surface normal, as
    /// <c>0x4B2B30</c> returns it.
    /// </param>
    /// <returns>The probability, which the caller compares a uniform sample against.</returns>
    /// <remarks>
    /// <para>
    /// The expression is <c>(limit − (π/2 − incidence)) / limit</c> with the limit in radians. A round
    /// arriving square on the surface has an incidence of zero, giving a large negative number and no
    /// chance of deflecting; a round arriving almost parallel has an incidence near π/2, giving a value
    /// near one. So it is a <b>grazing-angle ricochet</b>, and the surface's limit sets how oblique the
    /// approach has to be.
    /// </para>
    /// <para>
    /// The result is deliberately <b>not clamped</b> — the caller only ever compares it against a
    /// uniform sample on 0–1, for which any negative value is as good as zero.
    /// </para>
    /// </remarks>
    public static float DeflectionChance(float grazeLimitDegrees, float incidenceRadians)
    {
        if (grazeLimitDegrees == 0f)
        {
            return 0f;
        }

        float limit = grazeLimitDegrees * GunFiring.DegreesToRadians;
        return (limit - (MathF.PI / 2f - incidenceRadians)) / limit;
    }

    /// <summary>Whether a round may still deflect.</summary>
    /// <param name="hasAlreadyDeflected">Whether this round has deflected before.</param>
    /// <returns><see langword="true"/> when a deflection is still allowed.</returns>
    /// <remarks>
    /// <b>Once only.</b> The round carries a flag set on its first deflection and the test is skipped
    /// from then on, so a round cannot ricochet twice however many grazing surfaces it meets.
    /// </remarks>
    public static bool CanDeflect(bool hasAlreadyDeflected) => !hasAlreadyDeflected;

    /// <summary>What a round does at a surface it enters: first the graze, then the bite.</summary>
    /// <param name="penetrationPower">The weapon's <see cref="WeaponType.PenetrationPower"/>.</param>
    /// <param name="material">The struck face's game material.</param>
    /// <param name="namedObject">Whether the trace named the object it struck.</param>
    /// <param name="incidenceRadians">The arrival angle off the normal, as <see cref="IncidenceRadians"/> gives it.</param>
    /// <param name="alreadyDeflected">Whether this round has glanced before.</param>
    /// <param name="uniformSample">Draws a uniform sample on 0–1, called only where the original draws one.</param>
    /// <returns>Whether the round stops, goes through, or glances off.</returns>
    /// <remarks>
    /// <para>
    /// One rule for every round, because <c>0x47B750</c> is one routine for every round: the player's, a
    /// guard's and a mounted gun's alike. A glance is tried while the round may still deflect, against
    /// the surface's own ricochet limit; failing that, the round goes through only when the trace named an
    /// object and <see cref="Penetrates"/> lets it.
    /// </para>
    /// <para>
    /// A guard firing from behind a fence is the case that needs it. The water tower's railing on mission 1
    /// is <c>Fence</c> (resistance 0) and a sniper's Dragunov carries power 3, so the probe watched every one
    /// of the tower guard's rounds strike that railing 4.3 m out and carry on to the player.
    /// </para>
    /// </remarks>
    public static GunShotImpact ResolveEntry(
        float penetrationPower,
        GameMaterial material,
        bool namedObject,
        float incidenceRadians,
        bool alreadyDeflected,
        Func<float> uniformSample)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(uniformSample);

        if (CanDeflect(alreadyDeflected)
            && uniformSample() < DeflectionChance(material.RicochetAngleDegrees, incidenceRadians))
        {
            return GunShotImpact.Deflected;
        }

        return namedObject && Penetrates(penetrationPower, material.BulletResistance, uniformSample())
            ? GunShotImpact.Penetrated
            : GunShotImpact.Stopped;
    }

    /// <summary>The angle a round arrives at, measured off the surface normal.</summary>
    /// <param name="direction">The round's travel.</param>
    /// <param name="normal">The face's normal, turned to face the ray.</param>
    /// <returns>Zero arriving square on, near a right angle arriving along the surface (<c>0x4B2B30</c>).</returns>
    public static float IncidenceRadians(Vector3d direction, Vector3f normal)
    {
        double length = Math.Sqrt(
            (direction.X * direction.X) + (direction.Y * direction.Y) + (direction.Z * direction.Z));
        if (length <= 0.0)
        {
            return 0f;
        }

        double dot = ((-direction.X / length) * normal.X)
            + ((-direction.Y / length) * normal.Y)
            + ((-direction.Z / length) * normal.Z);
        return (float)Math.Acos(Math.Clamp(dot, -1.0, 1.0));
    }

    /// <summary>How much range is left after travelling a distance.</summary>
    /// <param name="remainingRange">The range before the step.</param>
    /// <param name="distanceTravelled">What the ray reported.</param>
    /// <returns>The range left, which the loop tests against zero.</returns>
    public static float ConsumeRange(float remainingRange, float distanceTravelled) =>
        remainingRange - distanceTravelled;
}
