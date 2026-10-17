using OpenIGI.Core.Numerics;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.Physics;

namespace OpenIGI.Game.Igi1.Physics;

/// <summary>
/// What a round does to a body it kills: how hard, in which direction, and where it lands.
/// </summary>
/// <remarks>
/// <para>
/// Three of the four things here are genuinely physical and come out of the shot itself — the
/// <b>direction</b> is the round's own line, the <b>point of application</b> is the bone it went through,
/// and the <b>relative magnitudes</b> between weapons are their real momenta. That is what makes a
/// shotgun at close range throw a body and a pistol round merely drop it, without any of it being an
/// authored table of falls.
/// </para>
/// <para>
/// <b>The fourth is not physical and must not pretend to be.</b> A 7.62 round carries about 6.8 N·s. Given
/// to an 80 kg body that is 0.085 m/s — a barely perceptible nudge, and the reason a real person shot
/// dead crumples rather than flies. Bodies thrown backwards by gunfire is cinema. <see cref="CinematicScale"/>
/// is therefore a deliberate, authored exaggeration, recorded here as the divergence it is: it buys the
/// reaction the shot visibly earned, at the cost of being wrong by a factor of thirty. Everything else
/// scales off it, so the whole feel of the feature is one number, and <c>g_ragdoll_force</c> is that
/// number in the player's hands.
/// </para>
/// </remarks>
public static class RagdollImpulse
{
    /// <summary>Engine units in a metre.</summary>
    public const double UnitsPerMetre = 4096.0;

    /// <summary>Simulation ticks in a second.</summary>
    public const double TicksPerSecond = 30.0;


    /// <summary>How much harder a round hits here than it does in the world.</summary>
    /// <remarks>See the class remarks: this is the one number that is not measured, and it is a choice.</remarks>
    public const double CinematicScale = 30.0;

    /// <summary>How much of the impulse survives each hop away from the bone that was struck.</summary>
    /// <remarks>
    /// The struck bone takes the most of it and its neighbours progressively less, so a body reacts as a
    /// body. Giving the whole impulse to one bone flails that limb about on the end of the torso;
    /// spreading it evenly makes every hit read the same wherever it landed.
    /// </remarks>
    public const double SpreadPerHop = 0.55;

    /// <summary>Beyond how many hops a bone is left out of it entirely.</summary>
    public const int SpreadReach = 6;

    /// <summary>How much of the momentum moves the whole body rather than the part that was hit.</summary>
    /// <remarks>
    /// <para>
    /// Seven tenths. A round genuinely does two things to a body — it moves all of it, and it snaps the
    /// bit it went through — and the split matters because the two behave completely differently under
    /// the rig's masses. The body-wide share is applied as a <em>velocity</em>, so every particle gets
    /// the same one and the corpse travels as a piece; the local share is applied as a
    /// <em>momentum</em>, so a light limb takes far more of it than the trunk does.
    /// </para>
    /// <para>
    /// Giving the whole thing to the local term is what makes a ragdoll look like a rag: a rifle round
    /// carrying enough momentum to visibly throw eighty kilograms will throw a two-kilogram forearm at a
    /// speed nothing about the shot justifies.
    /// </para>
    /// </remarks>
    public const double BodyShare = 0.7;

    /// <summary>The momentum a weapon's projectile carries, in newton-seconds.</summary>
    /// <param name="category">The weapon type that fired.</param>
    /// <returns>The momentum.</returns>
    /// <remarks>
    /// Ordinary exterior ballistics for the classes of weapon this game ships, not values read from the
    /// game's own data — the original has no such quantity, because nothing in it is ever pushed by a
    /// round. A 9 mm side-arm is about 2.9, a rifle round about 6.8, a shotgun's whole charge about 12.8,
    /// and a grenade at the body an order more than any of them.
    /// </remarks>
    public static double NewtonSecondsOf(WeaponCategory category) => category switch
    {
        WeaponCategory.Pistol => 2.9,
        WeaponCategory.Gun => 6.8,
        WeaponCategory.Shotgun => 12.8,
        WeaponCategory.Knife => 0.5,
        WeaponCategory.Grenade => 40.0,
        WeaponCategory.ProximityMine => 40.0,
        _ => 3.0,
    };

    /// <summary>The momentum a shot delivers, in the solver's own units.</summary>
    /// <param name="rig">The rig being hit, whose masses the momentum is measured against.</param>
    /// <param name="category">The weapon that fired.</param>
    /// <param name="scale">The player's own multiplier — <c>g_ragdoll_force</c>.</param>
    /// <returns>The momentum, in kilogram engine-units per tick.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rig"/> is <see langword="null"/>.</exception>
    public static double MomentumOf(RagdollSkeleton rig, WeaponCategory category, double scale)
    {
        ArgumentNullException.ThrowIfNull(rig);

        // Newton-seconds into a speed for a body of this weight, exaggerated, then into engine units per
        // tick, then back into a momentum against the rig's own total mass.
        double metresPerSecond =
            NewtonSecondsOf(category) / RagdollSkeleton.BodyMassInKilograms * CinematicScale * scale;
        return metresPerSecond * UnitsPerMetre / TicksPerSecond * rig.TotalMass;
    }

    /// <summary>Pushes a body with the round that killed it.</summary>
    /// <param name="body">The body to push.</param>
    /// <param name="bone">The bone the round went through; <c>-1</c> pushes the root instead.</param>
    /// <param name="travel">The round's own line, from the muzzle towards where it ended.</param>
    /// <param name="momentum">What <see cref="MomentumOf"/> returned.</param>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// The direction is the round's, unchanged and un-flattened: a shot from above drives a body down
    /// into the floor and a shot from below lifts it, which is the whole difference between this and a
    /// canned fall that only ever knows front from back.
    /// </para>
    /// <para>
    /// The local shares are <b>normalized</b>, so the momentum the body actually receives is the
    /// momentum that was asked for however many bones the rig happens to have. Without that a
    /// thirty-three-bone soldier is thrown three times as hard as a sixteen-bone one by the same round.
    /// </para>
    /// </remarks>
    public static void Apply(RagdollBody body, int bone, Vector3d travel, double momentum)
    {
        ArgumentNullException.ThrowIfNull(body);

        Vector3d direction = travel.Normalized();
        if (direction.LengthSquared <= 0.0 || momentum == 0.0)
        {
            return;
        }

        // The throw: the same velocity for every particle, so the corpse travels as one piece.
        body.AddVelocity(direction * (momentum * BodyShare / body.Rig.TotalMass));

        int struck = bone >= 0 && bone < body.BoneCount ? bone : 0;
        double total = 0.0;
        for (int other = 0; other < body.BoneCount; other++)
        {
            int hops = body.Rig.HopsBetween(struck, other);
            if (hops <= SpreadReach)
            {
                total += Math.Pow(SpreadPerHop, hops);
            }
        }

        if (total <= 0.0)
        {
            return;
        }

        // And the snap, around the bone it went through.
        double local = momentum * (1.0 - BodyShare) / total;
        for (int other = 0; other < body.BoneCount; other++)
        {
            int hops = body.Rig.HopsBetween(struck, other);
            if (hops <= SpreadReach)
            {
                body.AddImpulse(other, direction * (local * Math.Pow(SpreadPerHop, hops)));
            }
        }
    }
}
