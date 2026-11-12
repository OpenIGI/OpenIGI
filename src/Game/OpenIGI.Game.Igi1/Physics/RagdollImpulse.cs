using OpenIGI.Core.Numerics;
using OpenIGI.Game.Weapons;

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
/// is therefore a deliberate, authored exaggeration, recorded here as the divergence it is — and a small
/// one: enough that a rifle round visibly rocks the part it hits, not so much that it throws the man.
/// Everything else scales off it, and <c>g_ragdoll_force</c> is that number in the player's hands.
/// </para>
/// <para>
/// What brings a body down is not the round at all but gravity, once the legs stop holding it — which
/// is the muscle fade in <see cref="RagdollBody"/>, not anything here.
/// </para>
/// </remarks>
public static class RagdollImpulse
{
    /// <summary>Engine units in a metre.</summary>
    public const double UnitsPerMetre = WorldUnits.PerMetre;

    /// <summary>Simulation ticks in a second.</summary>
    public const double TicksPerSecond = 30.0;


    /// <summary>How much harder a round hits here than it does in the world.</summary>
    /// <remarks>
    /// <para>See the class remarks: this is the one number that is not measured, and it is a choice.</para>
    /// <para>
    /// Four. It was thirty, which with the old default force of 1.5 was forty-five times a real round:
    /// every kill threw an eighty-kilogram man backwards at close to three metres a second, which is the
    /// cartoon a player saw. At four a rifle round shoves the struck part about as hard as a solid punch,
    /// a shotgun at close range staggers the body, and a grenade still throws it.
    /// </para>
    /// </remarks>
    public const double CinematicScale = 4.0;

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
    /// Three tenths, down from seven: a round moves the part it goes through first, and the rest of the
    /// body only through the joints. A large uniform share made every hit a shove of the whole man.
    /// </para>
    /// <para> A round genuinely does two things to a body — it moves all of it, and it snaps the
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
    public const double BodyShare = 0.3;

    /// <summary>How much an explosion lifts a body, as a fraction of its push.</summary>
    /// <remarks>
    /// A blast at the ground pushes up as well as out — the ground it goes off against reflects it — so a
    /// body caught by one leaves its feet rather than sliding along the floor.
    /// </remarks>
    public const double BlastLift = 0.5;

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
    /// <param name="blast">Whether it was an explosion, which lifts as well as pushes.</param>
    /// <param name="at">
    /// Where the round met the body, in world units, or <see langword="null"/> to push the struck bone's
    /// body at its centre. Given a point, the body nearest it takes the round there, off its centre, so a
    /// round high in the back pitches the trunk forwards rather than shoving it along.
    /// </param>
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
    public static void Apply(
        RagdollBody body, int bone, Vector3d travel, double momentum, bool blast = false, Vector3d? at = null)
    {
        ArgumentNullException.ThrowIfNull(body);

        Vector3d direction = travel.Normalized();
        if (direction.LengthSquared <= 0.0 || momentum == 0.0)
        {
            return;
        }

        if (blast)
        {
            direction = (direction + new Vector3d(0.0, 0.0, BlastLift)).Normalized();
        }

        // The throw: the same velocity for every particle, so the corpse travels as one piece.
        body.AddVelocity(direction * (momentum * BodyShare / body.Rig.TotalMass));

        int struck = bone >= 0 && bone < body.BoneCount ? body.Rig.BodyOf(bone) : 0;
        Vector3d arm = Vector3d.Zero;
        if (at is { } point)
        {
            // Only the HEIGHT of the hit above the struck part's centre is kept as a lever. A round high in
            // the back tips the trunk over in the round's own direction, which is the reaction that reads as
            // being hit; a round a hand's width to one side would also spin the body about the vertical,
            // and with the momentum exaggerated as it is here that is a guard pirouetting before he falls.
            struck = body.NearestBody(point, out Vector3d centre);
            arm = new Vector3d(0.0, 0.0, point.Z - centre.Z);
        }
        double total = 0.0;
        for (int other = 0; other < body.BodyCount; other++)
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
        for (int other = 0; other < body.BodyCount; other++)
        {
            int hops = body.Rig.HopsBetween(struck, other);
            if (hops <= SpreadReach)
            {
                body.AddBodyImpulse(
                    other, direction * (local * Math.Pow(SpreadPerHop, hops)), other == struck ? arm : Vector3d.Zero);
            }
        }
    }
}
