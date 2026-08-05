using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Physics;

/// <summary>
/// A corpse being simulated: one particle per bone, stepped on the game's own tick.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is presentation, not simulation.</b> The body it belongs to keeps its retail death exactly —
/// the same tick, the same authoritative position, the same consequences for the AI around it. What a
/// ragdoll changes is the pose that is drawn, and nothing reads back out of it into the game.
/// </para>
/// <para>
/// It is stepped on the 30 Hz tick rather than per frame, so it lands on the same grid as everything
/// else the presentation interpolates between; <c>r_smooth</c> and the temporal resolve then get their
/// between-tick blend for free from the mesh's own previous-pose stream.
/// </para>
/// </remarks>
public sealed class RagdollBody
{
    /// <summary>How many constraint iterations each substep runs.</summary>
    /// <remarks>
    /// Two. Position-based dynamics converges with substeps far better than with iterations, so the
    /// budget is better spent on <see cref="Step"/>'s substep count — this only has to clean up what one
    /// pass leaves.
    /// </remarks>
    public const int IterationsPerSubstep = 2;

    /// <summary>What fraction of a particle's speed survives one substep.</summary>
    /// <remarks>
    /// Air drag, and the only thing keeping the solver's own injected energy in check. At 6 substeps a
    /// tick this is about 0.97 per tick, which is heavy enough that a body settles in a second or two.
    /// </remarks>
    public const double DampingPerSubstep = 0.995;

    /// <summary>How far a body may drift in a tick and still count as having stopped, in engine units.</summary>
    public const double RestThresholdInUnits = 8.0;

    /// <summary>How many consecutive still ticks latch a body at rest.</summary>
    public const int RestTicks = 15;

    private readonly RagdollSkeleton _rig;
    private readonly Vector3d[] _position;
    private readonly Vector3d[] _previous;
    private readonly float[] _inverseMass;
    private readonly float[] _selfCollisionFloor;
    private readonly Quaternion32[] _localRotations;
    private readonly Matrix3f[] _worldRotations;
    private readonly Vector3d[] _scratch;
    private int _stillTicks;

    private RagdollBody(
        RagdollSkeleton rig,
        Vector3d origin,
        Vector3d[] position,
        Quaternion32[] localRotations,
        Matrix3f[] worldRotations,
        float[] selfCollisionFloor)
    {
        _rig = rig;
        Origin = origin;
        _position = position;
        _previous = [.. position];
        _scratch = new Vector3d[position.Length];
        _localRotations = localRotations;
        _worldRotations = worldRotations;
        _selfCollisionFloor = selfCollisionFloor;

        _inverseMass = new float[rig.BoneCount];
        for (int bone = 0; bone < rig.BoneCount; bone++)
        {
            _inverseMass[bone] = rig.InverseMassOf(bone);
        }
    }

    /// <summary>Gets the rig this body wears.</summary>
    public RagdollSkeleton Rig => _rig;

    /// <summary>Gets the frozen world position the drawn pose is expressed relative to, in engine units.</summary>
    /// <remarks>
    /// Fixed when the body is seeded and never moved. The pose <see cref="RagdollPose"/> builds is
    /// relative to it with no rotation applied, so the renderer draws the corpse by placing it here with
    /// an identity orientation — the world turn is carried inside the root bone's own rotation.
    /// </remarks>
    public Vector3d Origin { get; }

    /// <summary>Gets whether the body has stopped moving and no longer needs stepping.</summary>
    public bool IsAtRest { get; private set; }

    /// <summary>Gets how many bones — and so how many particles — this body has.</summary>
    public int BoneCount => _rig.BoneCount;

    /// <summary>Gets a particle's world position, in engine units.</summary>
    /// <param name="bone">The bone index.</param>
    /// <returns>The world position.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bone"/> is not a bone of this rig.</exception>
    public Vector3d PositionOf(int bone)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bone);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(bone, _position.Length);
        return _position[bone];
    }

    /// <summary>Starts a ragdoll from the pose a body was drawn in when it died.</summary>
    /// <param name="rig">The prepared rig.</param>
    /// <param name="pose">
    /// The last animated pose, in the body's own model space — what
    /// <c>SoldierView.UnrotatedPose()</c> yields.
    /// </param>
    /// <param name="origin">Where that model space sits in the world, in engine units.</param>
    /// <param name="orientation">The body's heading, applied to the pose to reach world space.</param>
    /// <returns>The seeded body, at rest and with no velocity.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The pose does not match the rig.</exception>
    /// <remarks>
    /// Seeding from the drawn pose rather than from a bind pose is what makes the takeover invisible: the
    /// first simulated frame is the frame that was already on screen, so there is no snap between the
    /// animation ending and the physics starting.
    /// </remarks>
    public static RagdollBody Seed(
        RagdollSkeleton rig,
        BonePose pose,
        Vector3d origin,
        Matrix3f orientation)
    {
        ArgumentNullException.ThrowIfNull(rig);
        ArgumentNullException.ThrowIfNull(pose);
        if (pose.BoneCount != rig.BoneCount)
        {
            throw new ArgumentException(
                $"The rig has {rig.BoneCount} bones but the pose has {pose.BoneCount}.",
                nameof(pose));
        }

        int count = rig.BoneCount;
        Vector3d[] position = new Vector3d[count];
        Matrix3f[] world = new Matrix3f[count];
        Quaternion32[] local = new Quaternion32[count];

        for (int bone = 0; bone < count; bone++)
        {
            Vector3f turned = Rotate(orientation, pose.Positions[bone]);
            position[bone] = origin + new Vector3d(turned.X, turned.Y, turned.Z);

            // The pose was built with an identity root, so turning it by the body's heading is what
            // takes each bone's orientation into the world -- the same left-multiplication BonePose
            // applies above the root.
            world[bone] = orientation * pose.Rotations[bone];
        }

        for (int bone = 0; bone < count; bone++)
        {
            int parent = rig.Skeleton.Parents[bone];
            Matrix3f above = parent == BoneSkeleton.NoParent ? Matrix3f.Identity : world[parent];
            local[bone] = RagdollPose.QuaternionOf(above.Transposed() * world[bone]);
        }

        float[] floor = new float[rig.SelfCollisionPairs.Count];
        for (int i = 0; i < floor.Length; i++)
        {
            RagdollPair pair = rig.SelfCollisionPairs[i];
            double apart = Vector3d.Distance(position[pair.First], position[pair.Second]);

            // A pair already nearer than two bone radii keeps the gap it had, less a little. Demanding
            // the full radius from a pose that never had it is what makes a corpse explode on contact
            // with its own arm.
            floor[i] = (float)Math.Min(RagdollSkeleton.BoneRadiusInUnits * 2.0, apart * 0.9);
        }

        return new RagdollBody(rig, origin, position, local, world, floor);
    }

    /// <summary>Pushes one particle.</summary>
    /// <param name="bone">The bone to push.</param>
    /// <param name="momentum">The momentum to add, in mass-units times engine-units per tick.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bone"/> is not a bone of this rig.</exception>
    /// <remarks>
    /// A momentum rather than a velocity, divided by the particle's own mass on the way in — so the same
    /// round moves a hand a long way and the pelvis barely at all, which is the whole reason the masses
    /// come off the tree.
    /// </remarks>
    public void AddImpulse(int bone, Vector3d momentum)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bone);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(bone, _position.Length);

        _previous[bone] = _previous[bone] - (momentum * _inverseMass[bone]);
        IsAtRest = false;
        _stillTicks = 0;
    }

    /// <summary>Adds a velocity to every particle.</summary>
    /// <param name="velocityPerTick">The velocity to add, in engine units per tick.</param>
    /// <remarks>
    /// The body's own motion at the moment it died. A guard shot while running keeps running, which is
    /// the difference between a corpse falling and a corpse being dropped.
    /// </remarks>
    public void AddVelocity(Vector3d velocityPerTick)
    {
        for (int bone = 0; bone < _previous.Length; bone++)
        {
            _previous[bone] = _previous[bone] - velocityPerTick;
        }

        IsAtRest = false;
        _stillTicks = 0;
    }

    /// <summary>Advances the body by one game tick.</summary>
    /// <param name="world">The geometry to collide against.</param>
    /// <param name="substeps">How many substeps to take; more is stiffer and more expensive.</param>
    /// <exception cref="ArgumentNullException"><paramref name="world"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="substeps"/> is not positive.</exception>
    /// <remarks>
    /// A body that has latched at rest does nothing here at all. Corpses are never removed in this game,
    /// so without that latch every kill would be a permanent per-tick cost for the rest of the mission.
    /// </remarks>
    public void Step(RagdollWorld world, int substeps)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentOutOfRangeException.ThrowIfLessThan(substeps, 1);

        if (IsAtRest)
        {
            return;
        }

        Span<Vector3d> position = _position;
        Span<Vector3d> previous = _previous;
        Span<float> inverseMass = _inverseMass;

        // Gravity is an acceleration in units per tick squared, so a substep of 1/n of a tick moves a
        // particle by 1/n^2 of it. Getting that exponent wrong is the classic substepping bug: the body
        // falls n times too fast and reads as a solver that has gone unstable.
        double fraction = 1.0 / substeps;
        double gravity = HumanMotion.GravityPerTick * fraction * fraction;

        double moved = 0.0;
        Span<Vector3d> start = _scratch;
        for (int step = 0; step < substeps; step++)
        {
            position.CopyTo(start);

            RagdollSolver.Integrate(position, previous, inverseMass, gravity, DampingPerSubstep);

            for (int iteration = 0; iteration < IterationsPerSubstep; iteration++)
            {
                RagdollSolver.ProjectBoneLengths(_rig, position);
                RagdollSolver.ProjectJointSpans(_rig, position);
                RagdollSolver.ProjectSelfCollision(_rig, position, _selfCollisionFloor);
            }

            for (int bone = 0; bone < position.Length; bone++)
            {
                Vector3d candidate = position[bone];
                Vector3d back = previous[bone];
                RagdollSolver.ResolveContact(world, start[bone], ref candidate, ref back);
                position[bone] = candidate;
                previous[bone] = back;
                moved += Vector3d.Distance(start[bone], candidate);
            }
        }

        Track(moved / position.Length);
    }

    /// <summary>Builds the pose the renderer should draw.</summary>
    /// <returns>The pose, relative to <see cref="Origin"/> with no heading applied.</returns>
    /// <remarks>
    /// The positions come back out of <see cref="BonePose.Evaluate"/> rather than straight off the
    /// particles, so the drawn skeleton is rigid by construction and no amount of solver drift can
    /// stretch a limb. See <see cref="RagdollPose"/>.
    /// </remarks>
    public BonePose Pose()
    {
        RagdollPose.Resolve(_rig, _position, _worldRotations, _localRotations);

        Vector3d root = _position[0] - Origin;
        return BonePose.Evaluate(
            _rig.Skeleton,
            _localRotations,
            new Vector3f((float)root.X, (float)root.Y, (float)root.Z));
    }

    private static Vector3f Rotate(in Matrix3f m, in Vector3f v) => new(
        (m.M11 * v.X) + (m.M12 * v.Y) + (m.M13 * v.Z),
        (m.M21 * v.X) + (m.M22 * v.Y) + (m.M23 * v.Z),
        (m.M31 * v.X) + (m.M32 * v.Y) + (m.M33 * v.Z));

    private void Track(double averageMovement)
    {
        if (averageMovement < RestThresholdInUnits)
        {
            _stillTicks++;
            if (_stillTicks >= RestTicks)
            {
                IsAtRest = true;
            }
        }
        else
        {
            _stillTicks = 0;
        }
    }
}
