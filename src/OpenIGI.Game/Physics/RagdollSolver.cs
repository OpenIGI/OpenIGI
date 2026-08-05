using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Physics;

/// <summary>
/// The constraint passes a ragdoll is solved with — position-based dynamics over one particle per bone.
/// </summary>
/// <remarks>
/// <para>
/// Every method here is pure in the sense that matters: it takes the particle arrays and edits them in
/// place, holds no state of its own, and can be driven from a test with three particles and a plane. The
/// scheduling — how many substeps, in what order, with what damping — belongs to
/// <see cref="RagdollBody"/>.
/// </para>
/// <para>
/// Positions are <see cref="Vector3d"/> in engine units, because that is what world positions are
/// throughout this port; a corpse a kilometre from the origin must not judder.
/// </para>
/// </remarks>
public static class RagdollSolver
{
    /// <summary>How far a particle is pushed clear of a surface it met, in engine units.</summary>
    /// <remarks>
    /// The same 2.5 mm skin <see cref="Player.HumanWallProbe.SkinInUnits"/> keeps the player off a wall.
    /// Landing exactly on a surface makes the next cast start on it, which reads as a hit every step.
    /// </remarks>
    public const double SkinInUnits = 10.24;

    /// <summary>How much of the speed along a surface survives a contact.</summary>
    /// <remarks>
    /// A corpse does not slide far. This is deliberately heavy: 0.4 stops a body dead in about three
    /// contacts, which is what a limp shape hitting rough ground does.
    /// </remarks>
    public const double SurfaceFriction = 0.4;

    /// <summary>How much of the speed into a surface is returned on the way out.</summary>
    /// <remarks>Almost nothing. A body is not a ball; the bounce here only stops it settling into a jitter.</remarks>
    public const double SurfaceRestitution = 0.05;

    /// <summary>Moves every particle by its own velocity and by gravity.</summary>
    /// <param name="position">The particles, edited in place.</param>
    /// <param name="previous">Where each particle was a substep ago, edited in place.</param>
    /// <param name="inverseMass">One inverse mass per particle; zero pins a particle.</param>
    /// <param name="gravityPerStepSquared">Gravity's contribution to this substep, in engine units.</param>
    /// <param name="damping">What fraction of the implied velocity survives the substep.</param>
    /// <exception cref="ArgumentException">The spans differ in length.</exception>
    public static void Integrate(
        Span<Vector3d> position,
        Span<Vector3d> previous,
        ReadOnlySpan<float> inverseMass,
        double gravityPerStepSquared,
        double damping)
    {
        RequireSameLength(position, previous, inverseMass);

        for (int i = 0; i < position.Length; i++)
        {
            if (inverseMass[i] <= 0f)
            {
                previous[i] = position[i];
                continue;
            }

            Vector3d velocity = (position[i] - previous[i]) * damping;
            previous[i] = position[i];
            position[i] = position[i] + velocity + new Vector3d(0.0, 0.0, -gravityPerStepSquared);
        }
    }

    /// <summary>Holds every bone at its own rest length from its parent.</summary>
    /// <param name="rig">The prepared rig.</param>
    /// <param name="position">The particles, edited in place.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rig"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="position"/> is the wrong length.</exception>
    /// <remarks>
    /// This is the pass that makes the cloud of points a body. It runs at zero compliance — a bone does
    /// not stretch — and the corrections are split between the two ends by inverse mass, so pulling on a
    /// hand moves the hand and barely moves the chest.
    /// </remarks>
    public static void ProjectBoneLengths(RagdollSkeleton rig, Span<Vector3d> position)
    {
        ArgumentNullException.ThrowIfNull(rig);
        RequireLength(position, rig.BoneCount);

        for (int bone = 0; bone < rig.BoneCount; bone++)
        {
            int parent = rig.Skeleton.Parents[bone];
            if (parent == Formats.Animation.BoneSkeleton.NoParent)
            {
                continue;
            }

            SolveDistance(
                position,
                rig.InverseMassOf(parent),
                rig.InverseMassOf(bone),
                parent,
                bone,
                rig.RestLengths[bone],
                rig.RestLengths[bone]);
        }
    }

    /// <summary>Keeps every joint inside its bend range.</summary>
    /// <param name="rig">The prepared rig.</param>
    /// <param name="position">The particles, edited in place.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rig"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="position"/> is the wrong length.</exception>
    /// <remarks>
    /// The upper bound is also what stops a knee inverting — see
    /// <see cref="RagdollSkeleton.StraightSpanFactor"/> for why that is enough on its own.
    /// </remarks>
    public static void ProjectJointSpans(RagdollSkeleton rig, Span<Vector3d> position)
    {
        ArgumentNullException.ThrowIfNull(rig);
        RequireLength(position, rig.BoneCount);

        foreach (RagdollJoint joint in rig.Joints)
        {
            SolveDistance(
                position,
                rig.InverseMassOf(joint.Grandparent),
                rig.InverseMassOf(joint.Bone),
                joint.Grandparent,
                joint.Bone,
                joint.MinimumSpan,
                joint.MaximumSpan);
        }
    }

    /// <summary>Keeps distant bones from passing through one another.</summary>
    /// <param name="rig">The prepared rig.</param>
    /// <param name="position">The particles, edited in place.</param>
    /// <param name="floor">One minimum distance per pair, in engine units.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rig"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A span is the wrong length.</exception>
    /// <remarks>
    /// The floors are measured when the body is seeded rather than assumed, so a pair that was already
    /// touching stays as it was and only pairs that try to close further are pushed apart. Assuming a
    /// fixed radius instead makes a corpse shrug itself apart on the first step.
    /// </remarks>
    public static void ProjectSelfCollision(
        RagdollSkeleton rig,
        Span<Vector3d> position,
        ReadOnlySpan<float> floor)
    {
        ArgumentNullException.ThrowIfNull(rig);
        RequireLength(position, rig.BoneCount);
        RequireLength(floor, rig.SelfCollisionPairs.Count);

        for (int i = 0; i < floor.Length; i++)
        {
            RagdollPair pair = rig.SelfCollisionPairs[i];
            SolveDistance(
                position,
                rig.InverseMassOf(pair.First),
                rig.InverseMassOf(pair.Second),
                pair.First,
                pair.Second,
                floor[i],
                double.PositiveInfinity);
        }
    }

    /// <summary>Resolves one particle against the world it just moved through.</summary>
    /// <param name="world">The geometry to test against.</param>
    /// <param name="from">Where the particle was.</param>
    /// <param name="position">Where it wants to be; receives where it may be.</param>
    /// <param name="previous">Its previous position, adjusted to encode the post-contact velocity.</param>
    /// <returns><see langword="true"/> when the particle met something.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="world"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Two tests, because the port's geometry is in two places. The ray reaches placed collision meshes —
    /// walls, crates, stairs — and the ground query reaches the terrain, which no ray in the object index
    /// can see. A particle that meets neither is left where it asked to be.
    /// </para>
    /// <para>
    /// Velocity is not stored: in a Verlet integrator the previous position <em>is</em> the velocity, so a
    /// contact is applied by moving that previous position rather than the current one. Splitting the
    /// implied velocity into its normal and tangential parts is what lets friction and restitution be
    /// separate numbers.
    /// </para>
    /// </remarks>
    public static bool ResolveContact(
        RagdollWorld world,
        Vector3d from,
        ref Vector3d position,
        ref Vector3d previous)
    {
        ArgumentNullException.ThrowIfNull(world);

        Vector3d contact;
        Vector3d normal;

        if (world.Cast is { } cast && cast(from, position, out WorldRayHit hit))
        {
            contact = hit.Position;
            normal = new Vector3d(hit.Normal.X, hit.Normal.Y, hit.Normal.Z).Normalized();
        }
        else if (world.TryGround(position, SkinInUnits, out double groundZ) && position.Z < groundZ)
        {
            contact = new Vector3d(position.X, position.Y, groundZ);
            normal = Vector3d.UnitZ;
        }
        else
        {
            return false;
        }

        if (normal.LengthSquared <= 0.0)
        {
            normal = Vector3d.UnitZ;
        }

        Vector3d velocity = position - previous;
        double into = Vector3d.Dot(velocity, normal);
        Vector3d alongNormal = normal * into;
        Vector3d alongSurface = velocity - alongNormal;

        // Only a velocity heading INTO the surface is reflected. A particle already leaving one -- which
        // happens the step after a landing, and every step while a body rests on the floor -- keeps its
        // motion, or resting would be indistinguishable from being pushed.
        Vector3d resolved = into < 0.0
            ? (alongSurface * (1.0 - SurfaceFriction)) - (alongNormal * SurfaceRestitution)
            : velocity;

        position = contact + (normal * SkinInUnits);
        previous = position - resolved;
        return true;
    }

    /// <summary>Solves one distance constraint between two particles.</summary>
    /// <param name="position">The particles, edited in place.</param>
    /// <param name="inverseMassA">The first particle's inverse mass.</param>
    /// <param name="inverseMassB">The second particle's inverse mass.</param>
    /// <param name="a">The first particle's index.</param>
    /// <param name="b">The second particle's index.</param>
    /// <param name="minimum">The nearest the two may be.</param>
    /// <param name="maximum">The furthest the two may be.</param>
    /// <remarks>
    /// A range rather than a single length, because every constraint in this solver is one: a bone is a
    /// range of zero width, a joint span is a genuine range, and a self-collision floor is a range with
    /// no top. One routine for all three keeps the corrections consistent.
    /// </remarks>
    public static void SolveDistance(
        Span<Vector3d> position,
        float inverseMassA,
        float inverseMassB,
        int a,
        int b,
        double minimum,
        double maximum)
    {
        double total = inverseMassA + inverseMassB;
        if (total <= 0.0)
        {
            return;
        }

        Vector3d delta = position[b] - position[a];
        double length = delta.Length;
        if (length <= 0.0)
        {
            // Coincident particles have no direction to be separated along. Nudging along Z is arbitrary
            // and that is the point: any axis breaks the tie, and the next step's constraints decide.
            if (minimum > 0.0)
            {
                position[b] = position[b] + new Vector3d(0.0, 0.0, minimum);
            }

            return;
        }

        double target = Math.Clamp(length, minimum, maximum);
        if (target == length)
        {
            return;
        }

        Vector3d correction = delta * ((length - target) / (length * total));
        position[a] = position[a] + (correction * inverseMassA);
        position[b] = position[b] - (correction * inverseMassB);
    }

    private static void RequireLength<T>(ReadOnlySpan<T> span, int length)
    {
        if (span.Length != length)
        {
            throw new ArgumentException($"Expected {length} entries but was given {span.Length}.", nameof(span));
        }
    }

    private static void RequireLength<T>(Span<T> span, int length) =>
        RequireLength((ReadOnlySpan<T>)span, length);

    private static void RequireSameLength(
        Span<Vector3d> position,
        Span<Vector3d> previous,
        ReadOnlySpan<float> inverseMass)
    {
        if (position.Length != previous.Length || position.Length != inverseMass.Length)
        {
            throw new ArgumentException(
                "The position, previous-position and inverse-mass spans must be the same length.",
                nameof(position));
        }
    }
}
