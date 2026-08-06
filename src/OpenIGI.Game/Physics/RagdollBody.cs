using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Physics;

/// <summary>
/// A corpse being simulated: one rigid body per bone, stepped on the game's own tick.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is presentation, not simulation.</b> The body it belongs to keeps its retail death exactly —
/// the same tick, the same authoritative position, the same consequences for the AI around it. What a
/// ragdoll changes is the pose that is drawn, and nothing reads back out of it into the game.
/// </para>
/// <para>
/// Extended position-based dynamics: each tick is divided into substeps, each substep integrates
/// forwards and then projects every constraint once, and the velocities are read back out of how far
/// things actually moved. See <see cref="RagdollSolver"/> for the mathematics and for the reference.
/// </para>
/// <para>
/// It is stepped on the 30 Hz tick rather than per frame, so it lands on the same grid as everything
/// else the presentation interpolates between; <c>r_smooth</c> and the temporal resolve then get their
/// between-tick blend for free from the mesh's own previous-pose stream.
/// </para>
/// </remarks>
public sealed class RagdollBody
{
    /// <summary>How fast a corpse falls, in engine units per tick squared.</summary>
    /// <remarks>
    /// <para>
    /// <b>Not the same gravity the player falls at, and the difference is not invented.</b> The original
    /// pulls a walking human down at <see cref="HumanMotion.GravityPerTick"/> — 18.6 m/s², very nearly
    /// twice life — because a platformer's jump reads better heavy and snappy. A corpse is not jumping;
    /// it is an object, and at that rate it drops like a stone and the fall reads as sped-up film.
    /// </para>
    /// <para>
    /// So a ragdoll uses the <em>other</em> gravity the original already carries:
    /// <see cref="HumanMotion.LadderSlideGravityPerTick"/>, which is exactly 9.8 m/s² and is what the
    /// engine itself uses for the one case where a human is falling rather than moving. No number is
    /// invented here — both are the game's own, and this picks the one that describes a body.
    /// </para>
    /// <para>
    /// <b>Scaling below it is a different kind of decision and is kept separate.</b> Real gravity reads
    /// fast on a corpse — the camera is close, the fall is short, and there is no anticipation to sell
    /// it — so games routinely drop a ragdoll under one <i>g</i> for legibility. That is a look, not a
    /// measurement, so it lives in <c>g_ragdoll_gravity</c> where it can be seen and turned rather than
    /// baked into a constant that claims to be physics.
    /// </para>
    /// </remarks>
    public const double Gravity = HumanMotion.LadderSlideGravityPerTick;

    /// <summary>What fraction of a body's speed survives one tick.</summary>
    /// <remarks>Air drag, and small: what actually stops a corpse is the ground it is lying on.</remarks>
    public const double LinearDamping = 0.99;

    /// <summary>What fraction of a body's spin survives one tick.</summary>
    /// <remarks>
    /// <b>The single most important number for whether a ragdoll reads as a body or as a rag.</b> Limbs
    /// keep spinning long after the torso has stopped moving, and nothing else in the solver takes that
    /// energy out — the joints only say where a limb may be, not how fast it may get there.
    /// </remarks>
    public const double AngularDamping = 0.88;

    /// <summary>How strongly a joint resists its two bones turning relative to one another.</summary>
    /// <remarks>
    /// Joint friction, and it is what makes a corpse limp rather than loose. Distinct from
    /// <see cref="AngularDamping"/>, which slows a limb against the world: this slows it against the
    /// limb it hangs off, so a whole body can tumble freely while its elbows stay quiet.
    /// </remarks>
    public const double JointDamping = 0.25;

    /// <summary>How much of a bone's speed into a surface comes back out of it.</summary>
    /// <remarks>Nothing. A body is not a ball, and restitution on a ragdoll only ever makes it twitch.</remarks>
    public const double Restitution = 0.0;

    /// <summary>How much of a bone's speed along a surface survives a contact.</summary>
    public const double Friction = 0.4;

    /// <summary>How much of a bone's spin is rubbed off each substep it is touching something.</summary>
    /// <remarks>
    /// <para>
    /// <b>Torsional friction, and without it a corpse spins on the ground for a very long time.</b>
    /// Sliding friction acts on how fast a contact point is travelling, which says nothing about a body
    /// turning about the very axis it is resting on: the contact is stationary, the friction sees
    /// nothing to oppose, and only the slow global air damping is left to stop it. A real contact patch
    /// resists twisting as well as sliding, and this is that.
    /// </para>
    /// <para>
    /// Applied to bones that are actually touching, so a body in mid-air still tumbles freely. At eight
    /// substeps a bone in contact loses about three quarters of its spin a tick, which reads as a body
    /// that comes to rest rather than one that winds down.
    /// </para>
    /// </remarks>
    public const double TorsionalFriction = 0.15;

    /// <summary>How far a bone may sink into a surface before it is pushed out, in engine units.</summary>
    /// <remarks>
    /// Two centimetres, and it is a <b>deadband rather than a target</b>. Correcting penetration to
    /// exactly zero makes a resting body flicker between touching and not touching, which reads as a
    /// corpse humming against the floor.
    /// </remarks>
    public const double PenetrationSlop = 82.0;

    /// <summary>The fastest a bone may be pushed out of geometry, in engine units per tick.</summary>
    /// <remarks>
    /// <para>
    /// <b>The single most important number in the contact solve, and leaving it out is what makes a
    /// ragdoll dance.</b> Velocities here are not stored, they are read back out of how far things
    /// moved — so a positional correction of <c>d</c> over a substep of <c>h</c> <em>is</em> a velocity
    /// of <c>d/h</c>. Pushing a bone out of eight centimetres of floor in one sixteenth of a tick
    /// therefore hands it sixteen times that in upward speed, and the corpse launches, cartwheels and
    /// never settles.
    /// </para>
    /// <para>
    /// A quarter of a metre a tick, about 7 m/s. Deep penetration is walked out over several substeps
    /// instead of being fired out in one. From Macklin et al., <i>Small Steps in Physics Simulation</i>,
    /// section 4.3, where it is the fix for exactly this.
    /// </para>
    /// </remarks>
    public const double MaximumDepenetrationSpeed = 205.0;

    /// <summary>How far a body may drift in a tick and still count as having stopped, in engine units.</summary>
    /// <remarks>
    /// Five millimetres a tick, averaged over the bones. It is not zero and cannot be: a settled body is
    /// still pulled into the floor by gravity and pushed back out by the contact every substep, and what
    /// is left over after those cancel is a hum far below anything visible.
    /// </remarks>
    public const double RestThresholdInUnits = 20.0;

    /// <summary>How fast a body may still be moving and count as trying to settle.</summary>
    /// <remarks>
    /// <b>The settling help must not fight the shot.</b> A guard is standing on the floor when he is
    /// killed, so he is already in contact at the instant the round throws him — and a ramp that keys
    /// purely off contact starts scrubbing the throw away immediately, which is why bodies dropped flat
    /// where they stood instead of being knocked back. A body only counts as settling once it is
    /// actually slow; while it is still travelling it is left alone.
    /// </remarks>
    public const double SettleSpeedInUnits = RestThresholdInUnits * 8.0;

    /// <summary>How many consecutive still ticks latch a body at rest.</summary>
    /// <remarks>
    /// Twelve — four tenths of a second. It is a confirmation that the body has stopped, not a waiting
    /// period: a full second of stillness before the latch is a full second of a corpse that has already
    /// finished still being simulated.
    /// </remarks>
    public const int RestTicks = 12;

    /// <summary>Over how many ticks ON THE GROUND the settling help reaches full strength.</summary>
    /// <remarks>
    /// <para>
    /// <b>Counted from first contact, not from death</b>, and that is the whole point. Timing it from the
    /// moment of death means a body that is still falling gets helped to stop — and, worse, a body that
    /// has landed gets no help at all until the clock says so, which is exactly the couple of seconds of
    /// twitching on the ground this exists to remove.
    /// </para>
    /// <para>
    /// It is also the physically honest place for it. A body tumbling through the air really does keep
    /// its energy; a body lying on the ground really is losing it to whatever it is lying on, far faster
    /// than the point friction at a handful of contacts can account for.
    /// </para>
    /// </remarks>
    public const int SettleRampTicks = 18;

    /// <summary>After how many ticks a body is put to rest whatever it is doing.</summary>
    /// <remarks>
    /// <para>
    /// Ten seconds. <b>A guarantee rather than a tuning value.</b> A constraint solver of this shape has
    /// no proof that it converges — the paper it is built from says as much about piles, and a corpse
    /// lying on the ground with its own joints and a dozen contacts fighting each other is a pile. In
    /// practice a body settles in about three seconds; in the cases that do not, the failure a player
    /// sees is a corpse twitching or slowly turning on the spot for the rest of the mission, and it goes
    /// on costing solver time the whole while.
    /// </para>
    /// <para>
    /// Ten seconds after it died, it stops. Nothing that has not settled by then was going to.
    /// </para>
    /// </remarks>
    public const int MaximumActiveTicks = 300;

    /// <summary>How much of a body's spin the settling ramp takes away at full strength.</summary>
    public const double SettleDamping = 0.7;

    /// <summary>How many ways a bone looks for a wall each tick.</summary>
    /// <remarks>
    /// <para>
    /// Two, and which two matters more than how many. <b>One along the way the bone is travelling</b>,
    /// which is what catches a body being thrown into a wall, and <b>one straight out from the body's
    /// own centre</b>, which is what catches a body already resting against one. Casting along the axes
    /// instead would need four and still miss a diagonal.
    /// </para>
    /// <para>
    /// Like the ground, they are collected once a tick and resolved every substep — the surface beside a
    /// bone does not move within a tick even though the bone's distance to it does.
    /// </para>
    /// </remarks>
    public const int WallProbes = 2;

    /// <summary>How many times each substep projects the contacts and then the joints.</summary>
    /// <remarks>
    /// Two. Substepping is the real convergence knob and passes are the expensive one, so this stays
    /// small — but it cannot be one, because the contacts and the joints each undo a little of what the
    /// other did and a single pass leaves whichever ran first entirely unrepaired.
    /// </remarks>
    public const int SolverPasses = 2;

    /// <summary>How much of a joint's excursion past what it allows is taken out each substep.</summary>
    /// <remarks>
    /// Two fifths. See <see cref="SolveDeviation"/> for what this holds and why it is a spring rather
    /// than a stop. It is paired with the substep count — the spring acts once per pass, so halving the
    /// substeps halves how much it removes per tick — and this is the value at which a joint driven to
    /// its limit settles back onto it rather than resting outside it.
    /// </remarks>
    public const double JointStiffness = 0.4;

    /// <summary>How many times each pass projects the joint list.</summary>
    /// <remarks>
    /// One. <see cref="SolverPasses"/> already walks the joint list twice a substep, and a third and
    /// fourth walk measurably cost more than they straighten — the substep count is where convergence
    /// actually comes from.
    /// </remarks>
    public const int JointIterations = 1;

    /// <summary>How far from its origin a body may get before it is abandoned, in engine units.</summary>
    /// <remarks>
    /// <b>A backstop, not a limit.</b> A corpse has no business travelling a hundred metres from where it
    /// died, so a body that has is a body whose solver has gone wrong — and the failure a player sees is
    /// that the corpse <em>vanishes</em>, because it is drawn correctly, half a mile away. Catching it
    /// here means the worst case is falling back to the retail death animation rather than losing the
    /// body altogether.
    /// </remarks>
    public const double RunawayDistanceInUnits = 409_600.0;

    private readonly RagdollSkeleton _rig;
    private readonly int _boneCount;
    private readonly int _carried;

    // The rig's own data, copied flat at construction. Reading it back through RagdollSkeleton every
    // time was the solver's real cost, and none of it is arithmetic: the indexers are interface calls,
    // each one bounds-checks, each returns an eighty-byte struct BY VALUE, and a foreach over an
    // IReadOnlyList allocates an enumerator on the heap -- thirty-two times a tick per corpse, purely
    // to walk an array that never changes. Copying it once turns the inner loops into array reads.
    private readonly RagdollJoint[] _links;
    private readonly RagdollPair[] _pairs;
    private readonly float[] _inverseMass;
    private readonly Vector3f[] _aim;
    private readonly Vector3f[] _centre;
    private readonly float[] _halfLength;
    private readonly float[] _radius;
    private readonly Matrix3f[] _localInverseInertia;
    private readonly Vector3d[] _position;
    private readonly Quaternion32[] _orientation;
    private readonly Vector3d[] _velocity;
    private readonly Vector3d[] _spin;
    private readonly Vector3d[] _priorPosition;
    private readonly Quaternion32[] _priorOrientation;
    private readonly Matrix3f[] _inverseInertia;
    private readonly Vector3d[] _tickStart;
    private readonly Quaternion32[] _local;
    private readonly Vector3d[] _repair;
    private readonly Vector3d[] _repairSpin;
    private readonly Matrix3f[] _rotation;
    private readonly bool[] _rotationStale;
    private readonly double[] _surface;
    private readonly bool[] _grounded;
    private readonly Vector3d[] _wallPoint;
    private readonly Vector3d[] _wallNormal;
    private readonly bool[] _walled;
    private readonly int[] _contactBone;
    private readonly Vector3d[] _contactArm;
    private readonly Vector3d[] _contactNormal;
    private readonly double[] _contactApproach;
    private int _contactCount;
    private int _stillTicks;
    private int _restingTicks;
    private int _age;

    private RagdollBody(
        RagdollSkeleton rig,
        Vector3d origin,
        Vector3d[] position,
        Quaternion32[] orientation,
        RagdollBone? attachment,
        int host)
    {
        _rig = rig;
        Origin = origin;
        _position = position;
        _orientation = orientation;
        _boneCount = rig.BoneCount;
        _carried = attachment is null ? -1 : rig.BoneCount;

        int count = position.Length;
        _velocity = new Vector3d[count];
        _spin = new Vector3d[count];
        _priorPosition = new Vector3d[count];
        _priorOrientation = new Quaternion32[count];
        _inverseInertia = new Matrix3f[count];
        _tickStart = new Vector3d[count];
        // The SKELETON's share, not every body. Anything the corpse carries is simulated alongside the
        // bones but is no part of the pose, and BonePose.Evaluate rightly refuses a rotation per body
        // when it was given a bone count.
        _local = new Quaternion32[_boneCount];

        // Three probes a bone -- both caps and the middle -- is the most contacts a substep can find.
        _rotation = new Matrix3f[count];
        _rotationStale = new bool[count];

        // The carried body is appended to the joint list rather than special-cased, and because the
        // list is walked backwards -- leaves first, root last -- appending it puts it first, which is
        // exactly where the most leaf-like constraint in the body belongs.
        List<RagdollJoint> links = [.. rig.Joints];
        List<RagdollPair> pairs = [.. rig.SelfCollisionPairs];
        if (attachment is { } carried)
        {
            RagdollBone hand = rig.BoneAt(host);
            links.Add(new RagdollJoint(
                host,
                _carried,
                -hand.Centre,
                -carried.Centre,
                carried.Aim,
                RagdollWeapon.GripAllowance,
                -RagdollWeapon.GripAllowance,
                RagdollWeapon.GripAllowance));

            // Against every bone but the hand itself and whatever that hand hangs from -- the two it is
            // always touching, and which the grip already holds it against.
            for (int bone = 0; bone < rig.BoneCount; bone++)
            {
                if (rig.HopsBetween(host, bone) >= 2)
                {
                    pairs.Add(new RagdollPair(bone, _carried));
                }
            }
        }

        _links = [.. links];
        _pairs = [.. pairs];
        _inverseMass = new float[count];
        _aim = new Vector3f[count];
        _centre = new Vector3f[count];
        _halfLength = new float[count];
        _radius = new float[count];
        _localInverseInertia = new Matrix3f[count];
        for (int bone = 0; bone < count; bone++)
        {
            RagdollBone shape = bone < rig.BoneCount ? rig.BoneAt(bone) : attachment!.Value;
            _inverseMass[bone] = shape.InverseMass;
            _aim[bone] = shape.Aim;
            _centre[bone] = shape.Centre;
            _halfLength[bone] = shape.HalfLength;
            _radius[bone] = shape.Radius;
            _localInverseInertia[bone] = shape.InverseInertia;
        }
        for (int bone = 0; bone < count; bone++)
        {
            _rotation[bone] = BonePose.ToMatrix(orientation[bone]);
        }

        _surface = new double[count * 3];
        _grounded = new bool[count * 3];
        _wallPoint = new Vector3d[count * WallProbes];
        _wallNormal = new Vector3d[count * WallProbes];
        _walled = new bool[count * WallProbes];
        _repair = new Vector3d[count];
        _repairSpin = new Vector3d[count];
        _contactBone = new int[count * (3 + WallProbes)];
        _contactArm = new Vector3d[count * (3 + WallProbes)];
        _contactNormal = new Vector3d[count * (3 + WallProbes)];
        _contactApproach = new double[count * (3 + WallProbes)];
    }

    /// <summary>Gets the rig this body wears.</summary>
    public RagdollSkeleton Rig => _rig;

    /// <summary>Gets the frozen world position the drawn pose is expressed relative to, in engine units.</summary>
    /// <remarks>
    /// Fixed when the body is seeded and never moved. The pose is built relative to it with no rotation
    /// applied, so the renderer draws the corpse by placing it here with an identity orientation — the
    /// world turn is carried inside the root bone's own rotation.
    /// </remarks>
    public Vector3d Origin { get; }

    /// <summary>Gets whether the body has stopped moving and no longer needs stepping.</summary>
    public bool IsAtRest { get; private set; }

    /// <summary>Gets whether the simulation is still producing sensible results.</summary>
    /// <remarks>
    /// Once this goes false the body must be dropped and the retail death animation drawn instead. It is
    /// a backstop against the failure mode a player actually reports, which is not "the physics look
    /// wrong" but <b>"the body vanished"</b> — a corpse drawn perfectly well a mile outside the level, or
    /// at coordinates that are not numbers at all, is invisible either way.
    /// </remarks>
    public bool IsValid { get; private set; } = true;

    /// <summary>Gets how many bones the drawn skeleton has.</summary>
    /// <remarks>
    /// The skeleton's own count, which is not the number of bodies being simulated — anything the corpse
    /// is carrying is a body too, and sits past the end of this.
    /// </remarks>
    public int BoneCount => _boneCount;

    /// <summary>Gets whether this corpse is carrying something that is being simulated with it.</summary>
    public bool HasAttachment => _carried >= 0;

    /// <summary>Gets where the carried body's own origin sits, relative to <see cref="Origin"/>.</summary>
    /// <remarks>
    /// Its <em>origin</em> rather than its centre of mass, because that is the point a weapon's mesh is
    /// modelled about and so the point it has to be drawn at.
    /// </remarks>
    public Vector3f AttachmentPosition
    {
        get
        {
            if (_carried < 0)
            {
                return default;
            }

            Vector3d at = _position[_carried] - Turned(_carried, _centre[_carried]) - Origin;
            return new Vector3f((float)at.X, (float)at.Y, (float)at.Z);
        }
    }

    /// <summary>Gets which way the carried body is facing.</summary>
    public Matrix3f AttachmentRotation => _carried < 0 ? Matrix3f.Identity : Rotation(_carried);

    /// <summary>Gets the body's centre of mass, in engine units.</summary>
    /// <remarks>
    /// What a camera should frame, and it needs no bone to be named to find it — which matters, because
    /// the retail rigs' bone roles have not been measured and "the chest" would be a guess.
    /// </remarks>
    public Vector3d Centre
    {
        get
        {
            Vector3d sum = Vector3d.Zero;
            double mass = 0.0;
            for (int bone = 0; bone < _boneCount; bone++)
            {
                double weight = 1.0 / _inverseMass[bone];
                sum += _position[bone] * weight;
                mass += weight;
            }

            return mass > 0.0 ? sum / mass : Origin;
        }
    }

    /// <summary>Gets where a bone's joint sits in the world, in engine units.</summary>
    /// <param name="bone">The bone index.</param>
    /// <returns>The joint position — the bone's own origin, not its centre of mass.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bone"/> is not a bone of this rig.</exception>
    public Vector3d PositionOf(int bone)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bone);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(bone, _position.Length);
        return _position[bone] - Turned(bone, _centre[bone]);
    }

    /// <summary>Gets which way a bone is facing, in world space.</summary>
    /// <param name="bone">The bone index.</param>
    /// <returns>The bone's world orientation.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bone"/> is not a bone of this rig.</exception>
    public Quaternion32 OrientationOf(int bone)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bone);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(bone, _orientation.Length);
        return _orientation[bone];
    }

    /// <summary>Starts a ragdoll from the pose a body was drawn in when it died.</summary>
    /// <param name="rig">The prepared rig.</param>
    /// <param name="pose">
    /// The last animated pose, in the body's own model space — what <c>SoldierView.UnrotatedPose()</c>
    /// yields.
    /// </param>
    /// <param name="origin">Where that model space sits in the world, in engine units.</param>
    /// <param name="orientation">The body's heading, applied to the pose to reach world space.</param>
    /// <param name="attachment">
    /// Something the body is carrying — a weapon — as a rigid body of its own, or <see langword="null"/>
    /// for empty hands. It is placed at <paramref name="host"/>'s transform and jointed to it.
    /// </param>
    /// <param name="host">Which bone holds the attachment.</param>
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
        Matrix3f orientation,
        RagdollBone? attachment = null,
        int host = 0)
    {
        ArgumentNullException.ThrowIfNull(rig);
        ArgumentNullException.ThrowIfNull(pose);
        if (pose.BoneCount != rig.BoneCount)
        {
            throw new ArgumentException(
                $"The rig has {rig.BoneCount} bones but the pose has {pose.BoneCount}.",
                nameof(pose));
        }

        if (attachment is not null && (host < 0 || host >= rig.BoneCount))
        {
            attachment = null;
        }

        int count = rig.BoneCount + (attachment is null ? 0 : 1);
        Vector3d[] position = new Vector3d[count];
        Quaternion32[] rotation = new Quaternion32[count];

        for (int bone = 0; bone < rig.BoneCount; bone++)
        {
            Matrix3f world = orientation * pose.Rotations[bone];
            rotation[bone] = RagdollPose.QuaternionOf(world).Normalized();

            Vector3f joint = RagdollSolver.Rotate(orientation, pose.Positions[bone]);
            Vector3f centre = RagdollSolver.Rotate(world, rig.BoneAt(bone).Centre);
            position[bone] = origin + new Vector3d(joint.X + centre.X, joint.Y + centre.Y, joint.Z + centre.Z);
        }

        if (attachment is { } carried)
        {
            // A weapon is drawn AT its hand bone's transform, so that is where its body starts -- offset
            // only by its own centre of mass, which a bone's origin is never at.
            int slot = rig.BoneCount;
            rotation[slot] = rotation[host];

            Vector3f hand = RagdollSolver.Rotate(orientation, pose.Positions[host]);
            Vector3f middle = RagdollSolver.Rotate(BonePose.ToMatrix(rotation[slot]), carried.Centre);
            position[slot] = origin
                + new Vector3d(hand.X + middle.X, hand.Y + middle.Y, hand.Z + middle.Z);
        }

        return new RagdollBody(rig, origin, position, rotation, attachment, host);
    }

    /// <summary>Pushes one bone.</summary>
    /// <param name="bone">The bone to push.</param>
    /// <param name="momentum">The momentum to add, in kilogram engine-units per tick.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bone"/> is not a bone of this rig.</exception>
    /// <remarks>
    /// Divided by the bone's own mass on the way in, so the same round moves a hand a long way and the
    /// pelvis barely at all.
    /// </remarks>
    public void AddImpulse(int bone, Vector3d momentum)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bone);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(bone, _position.Length);

        _velocity[bone] += momentum * _inverseMass[bone];
        IsAtRest = false;
        _stillTicks = 0;
    }

    /// <summary>Adds a velocity to every bone.</summary>
    /// <param name="velocityPerTick">The velocity to add, in engine units per tick.</param>
    /// <remarks>
    /// The body's own motion at the moment it died. A guard shot while running keeps running, which is
    /// the difference between a corpse falling and a corpse being dropped.
    /// </remarks>
    public void AddVelocity(Vector3d velocityPerTick)
    {
        for (int bone = 0; bone < _velocity.Length; bone++)
        {
            _velocity[bone] += velocityPerTick;
        }

        IsAtRest = false;
        _stillTicks = 0;
    }

    /// <summary>Advances the body by one game tick.</summary>
    /// <param name="world">The geometry to collide against.</param>
    /// <param name="substeps">How many substeps to take; more is stiffer and more expensive.</param>
    /// <param name="gravityScale">
    /// What fraction of <see cref="Gravity"/> to fall at. One is real gravity; below it is an authored
    /// slow-down, which a corpse wants and a living body does not.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="world"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="substeps"/> is not positive.</exception>
    /// <remarks>
    /// A body that has latched at rest does nothing here at all. Corpses are never removed in this game,
    /// so without that latch every kill would be a permanent per-tick cost for the rest of the mission.
    /// </remarks>
    public void Step(RagdollWorld world, int substeps, double gravityScale = 1.0)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentOutOfRangeException.ThrowIfLessThan(substeps, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(gravityScale);

        if (IsAtRest)
        {
            return;
        }

        _position.CopyTo(_tickStart, 0);
        Survey(world);

        double step = 1.0 / substeps;
        double gravity = Gravity * gravityScale * step;
        // The longer a body has been ON something, the harder it is damped -- see SettleRampTicks. A
        // body still falling is untouched; one that landed a second ago is being actively quietened.
        double stubborn = Math.Clamp(_restingTicks / (double)SettleRampTicks, 0.0, 1.0);
        double settle = 1.0 - (SettleDamping * stubborn);
        double linear = Math.Pow(LinearDamping * settle, step);
        double angular = Math.Pow(AngularDamping * settle, step);
        double joint = Math.Pow(1.0 - JointDamping, step);

        for (int i = 0; i < substeps; i++)
        {
            Integrate(step, gravity);

            // Contacts, then joints, and repeated. Ordering these matters more than it looks: whichever
            // runs LAST is the one satisfied, and each breaks the other. Contacts last leaves the joints
            // visibly apart, because pushing a bone out of the floor drags it away from the bone it hangs
            // off. Joints last leaves a little penetration, which is exactly what the slop deadband is
            // there to absorb -- and a joint that has come apart is the failure an audience sees.
            _contactCount = 0;
            for (int pass = 0; pass < SolverPasses; pass++)
            {
                SolveContacts(step, record: pass == 0);
                SolveSelfCollision(step);
                SolveJoints();
            }

            Recover(step, linear, angular);
            SolveContactVelocities();
            DampJoints(joint);
        }

        if (!IsSane())
        {
            IsValid = false;
            IsAtRest = true;
            return;
        }

        double moved = 0.0;
        for (int bone = 0; bone < _position.Length; bone++)
        {
            moved += Vector3d.Distance(_tickStart[bone], _position[bone]);
        }

        // A body is "resting" while anything about it is touching. Losing contact WINDS THAT BACK rather
        // than clearing it: a body that lands hard bounces, and clearing it on the first bounce means the
        // settling help restarts from nothing every time it leaves the floor -- which made a body dropped
        // from a height take longer to settle than one dropped from a metre. Winding back at twice the
        // rate still lets a corpse genuinely kicked into the air tumble freely again.
        double drift = moved / _position.Length;
        _restingTicks = _contactCount > 0 && drift < SettleSpeedInUnits
            ? Math.Min(_restingTicks + 1, SettleRampTicks)
            : Math.Max(0, _restingTicks - 2);
        _age++;
        Track(drift);
    }

    /// <summary>Builds the pose the renderer should draw.</summary>
    /// <returns>The pose, relative to <see cref="Origin"/> with no heading applied.</returns>
    /// <remarks>
    /// <b>A direct read, with no fitting and no approximation.</b> Every bone already carries its own
    /// orientation, so the drawn pose is what the solver produced rather than a best guess at it — which
    /// is the largest single reason this is worth being rigid bodies rather than points.
    /// </remarks>
    public BonePose Pose()
    {
        for (int bone = 0; bone < _boneCount; bone++)
        {
            int parent = _rig.Skeleton.Parents[bone];
            Quaternion32 above = parent == BoneSkeleton.NoParent
                ? Quaternion32.Identity
                : _orientation[parent];

            _local[bone] = (above.Conjugate() * _orientation[bone]).Normalized();
        }

        Vector3d root = PositionOf(0) - Origin;
        return BonePose.Evaluate(
            _rig.Skeleton,
            _local,
            new Vector3f((float)root.X, (float)root.Y, (float)root.Z));
    }

    private void Integrate(double step, double gravity)
    {
        for (int bone = 0; bone < _position.Length; bone++)
        {
            _priorPosition[bone] = _position[bone];
            _priorOrientation[bone] = _orientation[bone];
            _repair[bone] = Vector3d.Zero;
            _repairSpin[bone] = Vector3d.Zero;

            _velocity[bone] += new Vector3d(0.0, 0.0, -gravity);
            _position[bone] += _velocity[bone] * step;
            _orientation[bone] = RagdollSolver.Advance(_orientation[bone], _spin[bone], step);
            _rotationStale[bone] = true;

            Matrix3f frame = Rotation(bone);
            _inverseInertia[bone] = frame * _localInverseInertia[bone] * frame.Transposed();
        }
    }

    private void Recover(double step, double linear, double angular)
    {
        for (int bone = 0; bone < _position.Length; bone++)
        {
            // The repair is subtracted before the velocity is read off. THIS is what keeps a corpse from
            // shaking itself apart on the ground, and it is worth being precise about why.
            //
            // In this solver a body's velocity is not stored, it is measured -- how far the body moved
            // over the substep, divided by the substep. That is right for motion. It is wrong for a
            // CORRECTION: pushing a bone out of the floor did not mean the bone was travelling, and
            // reading it back as though it were hands the body speed it never had. On a single bone that
            // is harmless, because the contact pass cancels the normal velocity again immediately. In a
            // jointed chain it is not: the push-out pulls the bone off the one it hangs from, the joint
            // pass repairs THAT, the repair is read back as more fabricated speed, and the body feeds
            // itself. It reads as a corpse that lands and then dances.
            //
            // So the contact corrections are accumulated separately and discounted here: the position
            // keeps them, the velocity never sees them. Erin Catto's split impulses (GDC 2007), which is
            // also what mature solvers do for every position-level correction.
            _velocity[bone] = (_position[bone] - _priorPosition[bone] - _repair[bone]) / step * linear;
            _spin[bone] =
                (RagdollSolver.Recover(_priorOrientation[bone], _orientation[bone], step)
                    - (_repairSpin[bone] / step)) * angular;
        }
    }

    // Joint friction: bleed the RELATIVE spin between a bone and its parent, which is what makes a
    // corpse limp rather than loose. Done at the velocity level after the solve, so it never fights the
    // constraints -- damping inside the projection would show up as a joint that drifts apart.
    private void DampJoints(double survives)
    {
        for (int i = 0; i < _links.Length; i++)
        {
            RagdollJoint link = _links[i];
            Vector3d relative = _spin[link.Child] - _spin[link.Parent];
            Vector3d bleed = relative * (1.0 - survives) * 0.5;
            _spin[link.Child] -= bleed;
            _spin[link.Parent] += bleed;
        }
    }

    // Leaves first, root LAST. In a Gauss-Seidel sweep the constraint solved last is the one satisfied
    // most accurately, and a hip that has come apart is far worse than a wrist that has -- so the joints
    // near the root get the final word. The skeleton being topologically sorted means walking the list
    // backwards is already leaf-first.
    private void SolveJoints()
    {
        for (int pass = 0; pass < JointIterations; pass++)
        {
            for (int i = _links.Length - 1; i >= 0; i--)
            {
                RagdollJoint link = _links[i];
                SolveDeviation(link);
                SolveAttachment(link);
            }
        }
    }

    // The joint itself: the two bones' anchor points are the same point, and they must stay so. Solved
    // AFTER the angular limits, because it is the one violation an audience can actually see.
    private void SolveAttachment(RagdollJoint link)
    {
        Vector3d parentArm = Turned(link.Parent, link.ParentAnchor);
        Vector3d childArm = Turned(link.Child, link.ChildAnchor);
        Vector3d gap = (_position[link.Parent] + parentArm) - (_position[link.Child] + childArm);

        double distance = gap.Length;
        if (distance <= 0.0)
        {
            return;
        }

        ApplyPositional(link.Parent, link.Child, parentArm, childArm, gap / distance, distance);
    }

    /// <summary>
    /// Eases a bone back toward its parent whenever it has turned further from it than the joint allows —
    /// in any direction at all, roll included.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One constraint on the whole relative rotation, rather than a swing limit and a twist limit
    /// solved separately, and that is a correction rather than a simplification.</b> Splitting a rotation
    /// into a swing and a twist needs an axis to split about, and on a joint that is already heavily
    /// swung — every joint of a corpse on the ground — the split moves part of the swing into the twist
    /// and part of the twist into the swing. Two constraints then each measure a quantity the other is
    /// changing, and neither converges: measured one way a joint would read 45°, measured the other 83°,
    /// on the same body at the same instant. The total angle between two orientations needs no axis and
    /// has no such ambiguity.
    /// </para>
    /// <para>
    /// It is also what fixes the limbs going thin. Nothing else in the solver constrains a bone's roll
    /// about its own length — the attachment says where a joint is and nothing about which way the bone
    /// is turned around it — so bones rolled freely, and a skinned mesh whose bones roll relative to
    /// their neighbours pinches: the vertices wind up like a sweet wrapper and the limb narrows to a
    /// point. That reads as a broken model rather than as broken physics, which is exactly how it looked.
    /// </para>
    /// <para>
    /// A <em>soft</em> spring rather than a hard stop, deliberately. A limit is a one-sided inequality
    /// that does nothing until it snaps, and snapping is what made the hard version shake a landed body
    /// to pieces. Taking a fixed fraction of the excess every substep acts continuously, acts hardest
    /// where the violation is worst, and cannot snap.
    /// </para>
    /// </remarks>
    private void SolveDeviation(RagdollJoint link)
    {
        // The child's orientation expressed in its parent's frame. At rest this is the identity, because
        // a rig's rest pose is every local rotation being nothing.
        Quaternion32 relative =
            (_orientation[link.Parent].Conjugate() * _orientation[link.Child]).Normalized();

        // A quaternion and its negation are the same orientation but opposite halves of a turn; taking
        // the near half is what makes the angle below the SHORT way round.
        if (relative.W < 0f)
        {
            relative = new Quaternion32(-relative.X, -relative.Y, -relative.Z, -relative.W);
        }

        Vector3f vector = relative.Vector;
        float sine = vector.Length;
        if (sine <= RagdollSolver.ParallelEpsilon)
        {
            return;
        }

        float angle = 2f * MathF.Atan2(sine, relative.W);
        float over = angle - link.SwingLimit;
        if (over <= 0f)
        {
            return;
        }

        // The axis is in the parent's frame, so it has to be carried into the world before it can turn
        // anything -- the constraint acts on world orientations.
        Vector3f axis = Direction(link.Parent, vector / sine);
        ApplyAngular(link.Parent, link.Child, axis, over * JointStiffness, repair: true);
    }

    /// <summary>Finds the ground under every probe, once for the whole tick.</summary>
    /// <remarks>
    /// <para>
    /// <b>The one thing that decides whether ragdolls are affordable.</b> Asking the world where the
    /// floor is costs a ray cast against every placed collision mesh plus a terrain lookup — easily the
    /// most expensive call in the solver, and thousands of times more expensive than the arithmetic
    /// around it. Doing it inside the projection meant <b>1349 of them per corpse per tick</b>: three
    /// probes a bone, sixteen substeps, two passes. Ten corpses were costing more than the frame.
    /// </para>
    /// <para>
    /// It is also unnecessary. A bone moves a few units in a substep, so the <em>surface</em> under it
    /// does not change within a tick even though its distance to that surface does — and only the
    /// distance is wanted per projection. So the plane is found once and the penetration recomputed
    /// against it every time, which is the same split the XPBD paper makes between collecting collision
    /// pairs once a frame and recomputing each contact every substep.
    /// </para>
    /// <para>
    /// What it costs is a tick of lag on the surface itself: a bone that slides over a step edge uses the
    /// old floor until the next tick. At thirty ticks a second, on a corpse, that is not observable.
    /// </para>
    /// </remarks>
    private void Survey(RagdollWorld world)
    {
        Vector3d middle = Vector3d.Zero;
        for (int bone = 0; bone < _position.Length; bone++)
        {
            middle += _position[bone];
        }

        middle /= _position.Length;

        for (int bone = 0; bone < _position.Length; bone++)
        {
            // Whatever is beside this bone: one probe along its travel, one straight out from the body.
            //
            // The reach INCLUDES THE TICK'S TRAVEL, and it has to. The surfaces are collected once a tick
            // and resolved every substep, which is what makes them affordable — but a bone moving at ten
            // metres a second covers a metre and a half between two surveys, so a reach of only its own
            // size looks at empty air, finds nothing, and the bone is through the wall before anything is
            // asked again. Padding the look-ahead by the distance about to be travelled is the same thing
            // a broad phase does when it expands its bounds by velocity times the step.
            double reach =
                _radius[bone] + _halfLength[bone] + PenetrationSlop + _velocity[bone].Length;
            Vector3d outward = _position[bone] - middle;
            for (int probe = 0; probe < WallProbes; probe++)
            {
                int slot = (bone * WallProbes) + probe;
                Vector3d towards = probe == 0 ? _velocity[bone] : outward;
                _walled[slot] = world.TrySurface(
                    _position[bone], towards, reach, out _wallPoint[slot], out _wallNormal[slot]);
            }

            Vector3d along = Turned(bone, _aim[bone]) * _halfLength[bone];

            // A bone shorter than it is wide is a sphere, and its three probes are the same probe -- so
            // it is asked once and the answer shared. A human rig is full of them (a hand, a foot, the
            // head's tip, every short spine segment), and each one skipped is two fewer ray casts
            // against the world per corpse per tick.
            if (_halfLength[bone] <= _radius[bone])
            {
                int only = bone * 3;
                _grounded[only] = world.TryGround(_position[bone], out _surface[only]);
                _grounded[only + 1] = _grounded[only];
                _grounded[only + 2] = _grounded[only];
                _surface[only + 1] = _surface[only];
                _surface[only + 2] = _surface[only];
                continue;
            }

            for (int end = -1; end <= 1; end++)
            {
                int probe = (bone * 3) + end + 1;
                _grounded[probe] =
                    world.TryGround(_position[bone] + (along * end), out _surface[probe]);
            }
        }
    }

    // Every bone is a capsule, and it is tested along its whole length rather than at its centre -- which
    // is the difference between a corpse lying ON a floor and a corpse buried to its waist in one.
    private void SolveContacts(double step, bool record)
    {
        double budget = MaximumDepenetrationSpeed * step;

        for (int bone = 0; bone < _position.Length; bone++)
        {
            Vector3d along = Turned(bone, _aim[bone]) * _halfLength[bone];

            // Both caps and the middle. Three probes is what a capsule against a heightfield needs to
            // stop it standing on one end.
            for (int end = -1; end <= 1; end++)
            {
                int index = (bone * 3) + end + 1;
                if (!_grounded[index])
                {
                    continue;
                }

                Vector3d probe = _position[bone] + (along * end);
                double depth = _surface[index] + _radius[bone] - probe.Z - PenetrationSlop;
                if (depth <= 0.0)
                {
                    continue;
                }

                // Recorded on the FIRST pass, and that is not a detail. By the last pass the earlier ones
                // have already pushed the bone clear, so the contact is no longer found -- and a contact
                // that is not recorded never has its push-out velocity cancelled, which is precisely the
                // energy that makes a corpse bounce higher each time it lands.
                Vector3d arm = probe - _position[bone];

                // Only ever a substep's worth of it -- see MaximumDepenetrationSpeed for why that cap
                // is what stops a corpse launching off the floor rather than landing on it.
                Remember(record, bone, arm, Vector3d.UnitZ);
                ApplyPositional(
                    -1, bone, Vector3d.Zero, arm, Vector3d.UnitZ, Math.Min(depth, budget), repair: true);
            }

            // And the walls. Pushed at the bone's centre rather than at the contact point: a wall shove
            // that also spins the bone reads as a body crawling up the wall it was thrown against.
            for (int probe = 0; probe < WallProbes; probe++)
            {
                int slot = (bone * WallProbes) + probe;
                if (!_walled[slot])
                {
                    continue;
                }

                double clear = Vector3d.Dot(_position[bone] - _wallPoint[slot], _wallNormal[slot]);
                double into = _radius[bone] - clear - PenetrationSlop;
                if (into <= 0.0)
                {
                    continue;
                }

                Remember(record, bone, Vector3d.Zero, _wallNormal[slot]);
                ApplyPositional(
                    -1,
                    bone,
                    Vector3d.Zero,
                    Vector3d.Zero,
                    _wallNormal[slot],
                    Math.Min(into, budget),
                    repair: true);
            }
        }
    }

    // One contact, kept for the velocity pass -- including the speed the bone was already carrying into
    // the surface, which is the one thing that cannot be recovered afterwards.
    private void Remember(bool record, int bone, Vector3d arm, Vector3d normal)
    {
        if (!record || _contactCount >= _contactBone.Length)
        {
            return;
        }

        _contactBone[_contactCount] = bone;
        _contactArm[_contactCount] = arm;
        _contactNormal[_contactCount] = normal;
        _contactApproach[_contactCount] =
            Vector3d.Dot(_velocity[bone] + Vector3d.Cross(_spin[bone], arm), normal);
        _contactCount++;
    }

    // Bone against bone, so a corpse has a volume instead of folding flat through itself. Capsule
    // against capsule: the nearest points on the two straight sections, pushed apart if they are closer
    // than the two radii together.
    private void SolveSelfCollision(double step)
    {
        double budget = MaximumDepenetrationSpeed * step;

        for (int i = 0; i < _pairs.Length; i++)
        {
            RagdollPair pair = _pairs[i];

            // The cheapest possible rejection first: two capsules cannot touch if their centres are
            // further apart than their two half-lengths and two radii together. Most pairs on a body
            // fail this every substep, and failing it costs one subtraction and a dot product instead of
            // four closest-point solves.
            double reach = _halfLength[pair.First] + _halfLength[pair.Second]
                + _radius[pair.First] + _radius[pair.Second];
            if (Vector3d.DistanceSquared(_position[pair.First], _position[pair.Second]) > reach * reach)
            {
                continue;
            }

            Vector3d firstAlong = Turned(pair.First, _aim[pair.First]) * _halfLength[pair.First];
            Vector3d secondAlong = Turned(pair.Second, _aim[pair.Second]) * _halfLength[pair.Second];

            Vector3d a0 = _position[pair.First] - firstAlong;
            Vector3d a1 = _position[pair.First] + firstAlong;
            Vector3d b0 = _position[pair.Second] - secondAlong;
            Vector3d b1 = _position[pair.Second] + secondAlong;

            // Two rounds of alternating projection finds the nearest pair of points on two segments
            // closely enough for a push-apart; an exact solve buys nothing a corpse can show.
            Vector3d onFirst = RagdollSolver.ClosestOnSegment(a0, a1, _position[pair.Second]);
            Vector3d onSecond = RagdollSolver.ClosestOnSegment(b0, b1, onFirst);
            onFirst = RagdollSolver.ClosestOnSegment(a0, a1, onSecond);
            onSecond = RagdollSolver.ClosestOnSegment(b0, b1, onFirst);

            Vector3d apart = onFirst - onSecond;
            double distance = apart.Length;
            double wanted = _radius[pair.First] + _radius[pair.Second];
            if (distance >= wanted || distance <= 0.0)
            {
                continue;
            }

            ApplyPositional(
                pair.First,
                pair.Second,
                onFirst - _position[pair.First],
                onSecond - _position[pair.Second],
                apart / distance,
                -Math.Min(wanted - distance, budget),
                repair: true);
        }
    }

    // The velocity half of a contact, run after the velocities have been read back out of the motion.
    // Without it the body keeps whatever speed the push-out gave it, which is the difference between a
    // corpse landing and a corpse bouncing for ever.
    private void SolveContactVelocities()
    {
        for (int i = 0; i < _contactCount; i++)
        {
            int bone = _contactBone[i];
            Vector3d arm = _contactArm[i];
            Vector3d normal = _contactNormal[i];
            Vector3d point = _velocity[bone] + Vector3d.Cross(_spin[bone], arm);

            // The speed INTO the surface is replaced outright by what the bone was doing before the
            // solve, mirrored by the restitution -- which is zero, so a body meeting something simply
            // stops. Deriving it from the post-solve motion instead reads the push-out as an impact and
            // bounces on it. Along the surface's own normal, so a wall works exactly as a floor does.
            double wanted = Math.Min(-Restitution * _contactApproach[i], 0.0);
            ApplyVelocity(bone, arm, normal * (wanted - Vector3d.Dot(point, normal)));

            // And friction across the surface, bounded by the tangential speed itself so that it can slow
            // the bone but never reverse it.
            point = _velocity[bone] + Vector3d.Cross(_spin[bone], arm);
            Vector3d tangent = point - (normal * Vector3d.Dot(point, normal));
            if (tangent.LengthSquared > 0.0)
            {
                ApplyVelocity(bone, arm, tangent * (-Math.Min(Friction, 1.0)));
            }

            // And the twisting the sliding friction above cannot see -- a bone turning about the surface
            // it rests on has a contact point that is barely moving, so there is nothing for a velocity
            // at that point to oppose.
            _spin[bone] *= 1.0 - TorsionalFriction;
        }
    }

    // A velocity change applied at a point on one body, shared between its linear and angular parts the
    // same way a positional correction is.
    private void ApplyVelocity(int bone, Vector3d arm, Vector3d change)
    {
        double size = change.Length;
        if (size <= 0.0)
        {
            return;
        }

        double weight = GeneralizedMass(bone, arm, change / size);
        if (weight <= 0.0)
        {
            return;
        }

        Vector3d impulse = change / weight;
        _velocity[bone] += impulse * _inverseMass[bone];
        _spin[bone] += RagdollSolver.Rotate(_inverseInertia[bone], Vector3d.Cross(arm, impulse));
    }

    /// <summary>
    /// The XPBD positional projection: move two bodies so a violation along one direction is closed.
    /// </summary>
    /// <remarks>
    /// The generalized inverse mass of each body includes the <c>(r × n)ᵀ I⁻¹ (r × n)</c> term, which is
    /// what makes a push at the end of a limb rotate it rather than slide it. A body index of −1 is the
    /// world: infinitely heavy, and it takes none of the correction.
    /// </remarks>
    private void ApplyPositional(
        int a,
        int b,
        Vector3d armA,
        Vector3d armB,
        Vector3d normal,
        double violation,
        bool repair = false)
    {
        double weightA = a < 0 ? 0.0 : GeneralizedMass(a, armA, normal);
        double weightB = b < 0 ? 0.0 : GeneralizedMass(b, armB, normal);
        double total = weightA + weightB;
        if (total <= 0.0)
        {
            return;
        }

        Vector3d impulse = normal * (-violation / total);

        if (a >= 0)
        {
            Vector3d shifted = impulse * _inverseMass[a];
            Vector3d spun = RagdollSolver.Rotate(_inverseInertia[a], Vector3d.Cross(armA, impulse));

            _position[a] += shifted;
            Turn(a, spun);

            if (repair)
            {
                _repair[a] += shifted;
                _repairSpin[a] += spun;
            }
        }

        if (b >= 0)
        {
            Vector3d moved = impulse * _inverseMass[b];
            Vector3d turned = RagdollSolver.Rotate(_inverseInertia[b], -Vector3d.Cross(armB, impulse));

            _position[b] -= moved;
            Turn(b, turned);

            if (repair)
            {
                _repair[b] -= moved;
                _repairSpin[b] += turned;
            }
        }
    }

    /// <summary>The XPBD angular projection: turn two bodies so a rotation violation is closed.</summary>
    /// <remarks>
    /// <b>A joint limit is applied as a repair, and that is what makes a corpse settle.</b> The reasoning
    /// is the contacts' exactly: the velocities here are measured from how far things turned, so a limb
    /// shoved back inside its cone reads afterwards as a limb that was <em>spinning</em> that fast. On
    /// the ground the contacts push bones past their limits every substep, the limits push back, and each
    /// push-back is handed to the body as angular speed it never had — a corpse that lands and then
    /// twitches, rolls and dances, which is precisely what this looked like before the correction was
    /// excluded from the velocity.
    /// </remarks>
    private void ApplyAngular(int a, int b, Vector3f axis, double violation, bool repair = false)
    {
        Vector3d n = new(axis.X, axis.Y, axis.Z);
        double weightA = Vector3d.Dot(n, RagdollSolver.Rotate(_inverseInertia[a], n));
        double weightB = Vector3d.Dot(n, RagdollSolver.Rotate(_inverseInertia[b], n));
        double total = weightA + weightB;
        if (total <= 0.0)
        {
            return;
        }

        Vector3d impulse = n * (violation / total);
        Vector3d turnA = RagdollSolver.Rotate(_inverseInertia[a], impulse);
        Vector3d turnB = RagdollSolver.Rotate(_inverseInertia[b], -impulse);

        Turn(a, turnA);
        Turn(b, turnB);

        if (repair)
        {
            _repairSpin[a] += turnA;
            _repairSpin[b] += turnB;
        }
    }

    private double GeneralizedMass(int bone, Vector3d arm, Vector3d normal)
    {
        Vector3d turn = Vector3d.Cross(arm, normal);
        return _inverseMass[bone]
            + Vector3d.Dot(turn, RagdollSolver.Rotate(_inverseInertia[bone], turn));
    }

    /// <summary>Turns a bone, keeping its cached rotation matrix in step with its quaternion.</summary>
    /// <remarks>
    /// <b>The matrix is cached because rebuilding it was the solver's largest single cost.</b> Every
    /// vector this thing rotates — three contact probes a bone, two anchors a joint, two axes a
    /// self-collision pair, every substep, every pass — went through a quaternion-to-matrix conversion
    /// first, and there are far more rotations than there are orientation changes. Keeping the matrix
    /// beside the quaternion turns each of those into a plain multiply.
    /// </remarks>
    private void Turn(int bone, Vector3d turn)
    {
        _orientation[bone] = RagdollSolver.Advance(_orientation[bone], turn, 1.0);
        _rotationStale[bone] = true;
    }

    // Rebuilt on demand rather than on every write, because a projection typically turns a bone two or
    // three times before anything reads it again -- rebuilding eagerly cost more than it saved.
    private Matrix3f Rotation(int bone)
    {
        if (_rotationStale[bone])
        {
            _rotation[bone] = BonePose.ToMatrix(_orientation[bone]);
            _rotationStale[bone] = false;
        }

        return _rotation[bone];
    }

    private Vector3d Turned(int bone, Vector3d local) =>
        RagdollSolver.Rotate(Rotation(bone), local);

    private Vector3d Turned(int bone, Vector3f local) =>
        Turned(bone, new Vector3d(local.X, local.Y, local.Z));

    private Vector3f Direction(int bone, Vector3f local) =>
        RagdollSolver.Rotate(Rotation(bone), local);

    private static Vector3f Perpendicular(Vector3f value, Vector3f axis) =>
        value - (axis * Vector3f.Dot(value, axis));

    // Everything a corpse must still be: made of numbers, and near where it died.
    private bool IsSane()
    {
        for (int bone = 0; bone < _position.Length; bone++)
        {
            Vector3d at = _position[bone];
            if (!double.IsFinite(at.X) || !double.IsFinite(at.Y) || !double.IsFinite(at.Z))
            {
                return false;
            }

            if (Vector3d.DistanceSquared(at, Origin) > RunawayDistanceInUnits * RunawayDistanceInUnits)
            {
                return false;
            }
        }

        return true;
    }

    private void Track(double averageMovement)
    {
        if (_age >= MaximumActiveTicks)
        {
            IsAtRest = true;
            return;
        }

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
