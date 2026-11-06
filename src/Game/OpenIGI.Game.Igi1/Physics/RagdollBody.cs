using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Physics;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi1.Physics;

/// <summary>
/// A corpse being simulated: one rigid body per simulated bone, stepped on the game's own tick.
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
/// <para>
/// Arrays here are indexed by <b>body</b>, in the rig's body order, with anything the corpse carries — a
/// weapon — appended after the last one. Bones the rig does not simulate are carried by their body with
/// the turn they had when it died (<see cref="RagdollSkeleton"/>).
/// </para>
/// </remarks>
public sealed partial class RagdollBody
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
    /// engine itself uses for the one case where a human is falling rather than moving.
    /// </para>
    /// </remarks>
    public const double Gravity = HumanMotion.LadderSlideGravityPerTick;

    /// <summary>What fraction of a body's speed survives one tick.</summary>
    /// <remarks>Air drag, and small: what actually stops a corpse is the ground it is lying on.</remarks>
    public const double LinearDamping = 0.99;

    /// <summary>What fraction of a body's spin survives one tick.</summary>
    /// <remarks>
    /// <para>
    /// Air drag on turning, and like <see cref="LinearDamping"/> small. Limbs keep spinning after the torso
    /// has stopped, and something has to take that out — but it is the joint friction
    /// (<see cref="JointDamping"/>) and the ground that should, not the air.
    /// </para>
    /// <para>
    /// <b>It was 0.88, and that was half of the slow-motion look.</b> Losing an eighth of the spin every
    /// tick in mid-air is not air resistance, it is syrup: a body falling through it turns as though it
    /// were under water.
    /// </para>
    /// </remarks>
    public const double AngularDamping = 0.97;

    /// <summary>How strongly a joint resists its two bodies turning relative to one another.</summary>
    /// <remarks>
    /// Joint friction, and it is what makes a corpse limp rather than loose. Distinct from
    /// <see cref="AngularDamping"/>, which slows a limb against the world: this slows it against the
    /// limb it hangs off, so a whole body can tumble freely while its elbows stay quiet.
    /// </remarks>
    public const double JointDamping = 0.25;

    /// <summary>How much of a bone's speed into a surface comes back out of it.</summary>
    /// <remarks>Nothing. A body is not a ball, and restitution on a ragdoll only ever makes it twitch.</remarks>
    public const double Restitution = 0.0;

    /// <summary>How much of a bone's speed along a surface is taken away by a contact.</summary>
    /// <remarks>
    /// Six tenths — cloth and skin on concrete or earth. A corpse does not skate; at 0.4 a body knocked
    /// down slid along the floor as though it were wet.
    /// </remarks>
    public const double Friction = 0.6;

    /// <summary>How much of a bone's spin is rubbed off each substep it is touching something.</summary>
    /// <remarks>
    /// <b>Torsional friction, and without it a corpse spins on the ground for a very long time.</b>
    /// Sliding friction acts on how fast a contact point is travelling, which says nothing about a body
    /// turning about the very axis it is resting on. A real contact patch resists twisting as well as
    /// sliding, and this is that. Applied only to bones that are touching, so a body in mid-air still
    /// tumbles freely.
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
    /// <b>Leaving it out is what makes a ragdoll dance.</b> Velocities here are not stored, they are read
    /// back out of how far things moved — so a positional correction of <c>d</c> over a substep of
    /// <c>h</c> <em>is</em> a velocity of <c>d/h</c>, and pushing a bone out of eight centimetres of floor
    /// in one substep launches it.
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
    /// Five millimetres a tick, averaged over the bodies. It is not zero and cannot be: a settled body is
    /// still pulled into the floor by gravity and pushed back out by the contact every substep.
    /// </remarks>
    public const double RestThresholdInUnits = 20.0;

    /// <summary>How fast a body may still be moving and count as trying to settle.</summary>
    /// <remarks>
    /// <para>
    /// <b>The settling help must not fight the fall.</b> A guard is standing on the floor when he is
    /// killed, so he is in contact from the first tick, and a body that has crumpled to its knees is in
    /// contact and slow at exactly the moment it starts to tip over. Settling either of them is a corpse
    /// frozen kneeling.
    /// </para>
    /// <para>
    /// Three times the rest threshold — under half a metre a second. It was eight times, well over a
    /// metre a second, which with the old stiff ramp held a kneeling body upright for a second and a half.
    /// </para>
    /// </remarks>
    public const double SettleSpeedInUnits = RestThresholdInUnits * 3.0;

    /// <summary>How many consecutive still ticks latch a body at rest.</summary>
    /// <remarks>Twelve — four tenths of a second. A confirmation that the body has stopped, not a waiting period.</remarks>
    public const int RestTicks = 12;

    /// <summary>Over how many ticks ON THE GROUND the settling help reaches full strength.</summary>
    /// <remarks>
    /// <b>Counted from first contact, not from death</b>, so a body still falling is not helped to stop
    /// and a body that has landed is helped at once. It is also the physically honest place for it: a
    /// body lying on the ground really is losing its energy to whatever it lies on, far faster than the
    /// point friction at a handful of contacts can account for.
    /// </remarks>
    public const int SettleRampTicks = 30;

    /// <summary>After how many ticks a body is put to rest whatever it is doing.</summary>
    /// <remarks>
    /// Ten seconds. <b>A guarantee rather than a tuning value.</b> A constraint solver of this shape has
    /// no proof that it converges, and a corpse lying on the ground with its own joints and a dozen
    /// contacts fighting each other is a pile. Nothing that has not settled by then was going to.
    /// </remarks>
    public const int MaximumActiveTicks = 300;

    /// <summary>How much of a body's motion the settling ramp takes away each tick at full strength.</summary>
    /// <remarks>
    /// A quarter. It was seven tenths — the ramp was a crutch for a solver that jittered on the ground,
    /// and at that strength it also stopped real motion dead: a body slowly tipping over is slow and
    /// touching the floor too. With the joints and the contacts quiet, a light hand is all a corpse needs.
    /// </remarks>
    public const double SettleDamping = 0.25;

    /// <summary>How many ways a bone looks for a wall each tick.</summary>
    /// <remarks>
    /// Two: <b>one along the way the bone is travelling</b>, which catches a body thrown into a wall, and
    /// <b>one straight out from the body's own centre</b>, which catches a body already resting against
    /// one. Collected once a tick and resolved every substep.
    /// </remarks>
    public const int WallProbes = 2;

    /// <summary>How many times each substep projects the contacts and then the joints.</summary>
    /// <remarks>
    /// Two. Substepping is the real convergence knob and passes are the expensive one, so this stays
    /// small — but it cannot be one, because the contacts and the joints each undo a little of what the
    /// other did and a single pass leaves whichever ran first entirely unrepaired.
    /// </remarks>
    public const int SolverPasses = 2;

    /// <summary>How much of a joint's excursion past what it allows is taken out each pass.</summary>
    /// <remarks>
    /// <para>
    /// Nine tenths: a stop, very nearly. Two passes a substep take out ninety-nine percent of an excursion,
    /// so a joint is held where it is allowed to be, not merely leaned back towards it.
    /// </para>
    /// <para>
    /// It used to be a soft 0.4 spring, because the limit then was one angle about the rest pose and a
    /// hard version of that shook a landed body to pieces. That limit could not tell a knee bending from a
    /// knee bending backwards, so it let both through; a correct limit is one a body can lie against, and
    /// is held firmly. What shook it was the bookkeeping, not the stiffness (see <see cref="SolveLimit"/>).
    /// </para>
    /// </remarks>
    public const double LimitStiffness = 0.9;

    /// <summary>How hard the death clip pulls each joint at the instant of death, as a fraction per substep.</summary>
    /// <remarks>
    /// <para>
    /// <b>Muscle tone, and it is what makes a death read as a person collapsing rather than a puppet dropped.</b>
    /// The retail death clip keeps running underneath the corpse. For the first moments the joints chase
    /// it — knees give, the trunk folds the way the animator folded it — while the body as a whole is left
    /// to the physics, so it still falls where the round and the ground put it. The pull fades to nothing
    /// over <c>g_ragdoll_muscle</c> seconds, and from then on the body is limp.
    /// </para>
    /// <para>A fifth of the remaining error each substep; the fade is what lets go.</para>
    /// </remarks>
    public const double MuscleStrength = 0.2;

    /// <summary>How far from its origin a body may get before it is abandoned, in engine units.</summary>
    /// <remarks>
    /// <b>A backstop, not a limit.</b> A corpse has no business travelling a hundred metres from where it
    /// died, so a body that has is a body whose solver has gone wrong — and the failure a player sees is
    /// that the corpse <em>vanishes</em>. Catching it here falls back to the retail death animation.
    /// </remarks>
    public const double RunawayDistanceInUnits = 409_600.0;

    private readonly RagdollSkeleton _rig;
    private readonly int _boneCount;
    private readonly int _bodyCount;
    private readonly int _carried;
    private readonly Quaternion32[] _boneLocal;
    private readonly Quaternion32[] _jointOffset;
    private readonly Quaternion32[] _driveTarget;
    private readonly Quaternion32[] _held;
    private readonly float[] _bend;
    private readonly Quaternion32[] _world;
    private readonly bool[] _worldDone;

    // The rig's own data, copied flat at construction. Reading it back through RagdollSkeleton every
    // time was the solver's real cost, and none of it is arithmetic: the indexers are interface calls,
    // each one bounds-checks, each returns an eighty-byte struct BY VALUE.
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
    private bool _driving;
    private bool _following;
    private int _contactCount;
    private int _stillTicks;
    private int _restingTicks;
    private int _age;

    private RagdollBody(
        RagdollSkeleton rig,
        Vector3d origin,
        Vector3d[] position,
        Quaternion32[] orientation,
        Quaternion32[] boneLocal,
        RagdollJoint[] links,
        Quaternion32[] jointOffset,
        RagdollBone? carried)
    {
        _rig = rig;
        Origin = origin;
        _position = position;
        _orientation = orientation;
        _boneCount = rig.BoneCount;
        _bodyCount = rig.BodyCount;
        _carried = carried is null ? -1 : rig.BodyCount;
        _boneLocal = boneLocal;
        _links = links;
        _jointOffset = jointOffset;
        _driveTarget = new Quaternion32[links.Length];
        Array.Fill(_driveTarget, Quaternion32.Identity);

        // The pose it died in, joint by joint -- what the muscles hold unless they are told to follow the
        // death clip instead -- curled forwards at the spine and neck by each joint's slump. A body losing
        // its tone does not hold itself upright: the head drops and the back rounds, which moves its weight
        // forwards, and that is why people shot or fainting mostly fold forwards rather than going over
        // backwards like a felled tree. A turn about the joint frame's +X by a negative angle carries its
        // up (+Z) towards its forwards (+Y).
        _held = new Quaternion32[rig.Joints.Count];
        for (int i = 0; i < _held.Length; i++)
        {
            Quaternion32 held = boneLocal[rig.BoneOf(links[i].Child)];
            _held[i] = links[i].Slump != 0f
                ? (Quaternion32.FromAxisAngle(Vector3f.UnitX, -links[i].Slump) * held).Normalized()
                : held;
        }

        _bend = new float[position.Length];
        _world = new Quaternion32[_boneCount];
        _worldDone = new bool[_boneCount];

        int count = position.Length;
        _velocity = new Vector3d[count];
        _spin = new Vector3d[count];
        _priorPosition = new Vector3d[count];
        _priorOrientation = new Quaternion32[count];
        _inverseInertia = new Matrix3f[count];
        _tickStart = new Vector3d[count];

        // One per skeleton BONE, not per body: the drawn pose turns every bone, simulated or carried.
        _local = new Quaternion32[_boneCount];
        _rotation = new Matrix3f[count];
        _rotationStale = new bool[count];

        _inverseMass = new float[count];
        _aim = new Vector3f[count];
        _centre = new Vector3f[count];
        _halfLength = new float[count];
        _radius = new float[count];
        _localInverseInertia = new Matrix3f[count];
        for (int body = 0; body < count; body++)
        {
            RagdollBone shape = body < rig.BodyCount ? rig.BodyAt(body) : carried!.Value;
            _inverseMass[body] = shape.InverseMass;
            _aim[body] = shape.Aim;
            _centre[body] = shape.Centre;
            _halfLength[body] = shape.HalfLength;
            _radius[body] = shape.Radius;
            _localInverseInertia[body] = shape.InverseInertia;
            _rotation[body] = BonePose.ToMatrix(orientation[body]);
        }

        // The pairs, less any already touching in the pose the body died in. A rig's capsules overlap
        // where the skin folds -- an arm hanging against the flank, a thigh against the belly -- and
        // pushing those apart on the first substep is a corpse that twitches itself open as it starts to
        // fall. A pair that begins apart is held apart from then on.
        List<RagdollPair> pairs = [];
        foreach (RagdollPair pair in rig.SelfCollisionPairs)
        {
            if (!Overlapping(pair.First, pair.Second))
            {
                pairs.Add(pair);
            }
        }

        if (_carried >= 0)
        {
            // Against every body but the one holding it and whatever that hangs from -- the two it is
            // always touching, and which the grip already holds it against.
            int holder = links[^1].Parent;
            for (int body = 0; body < rig.BodyCount; body++)
            {
                if (rig.HopsBetween(holder, body) >= 2 && !Overlapping(body, _carried))
                {
                    pairs.Add(new RagdollPair(body, _carried));
                }
            }
        }

        _pairs = [.. pairs];

        // Three probes a body -- both caps and the middle -- plus its walls, plus whatever triangles it
        // lies across, is the most contacts a substep keeps.
        _surface = new double[count * 3];
        _grounded = new bool[count * 3];
        _wallPoint = new Vector3d[count * WallProbes];
        _wallNormal = new Vector3d[count * WallProbes];
        _walled = new bool[count * WallProbes];
        _repair = new Vector3d[count];
        _repairSpin = new Vector3d[count];
        int contacts = (count * (3 + WallProbes)) + TriangleContacts;
        _contactBone = new int[contacts];
        _contactArm = new Vector3d[contacts];
        _contactNormal = new Vector3d[contacts];
        _contactApproach = new double[contacts];
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
    /// a backstop against the failure a player actually reports, which is <b>"the body vanished"</b>.
    /// </remarks>
    public bool IsValid { get; private set; } = true;

    /// <summary>Gets how many bones the drawn skeleton has.</summary>
    public int BoneCount => _boneCount;

    /// <summary>Gets how many rigid bodies the skeleton is simulated as, not counting anything carried.</summary>
    public int BodyCount => _bodyCount;

    /// <summary>Gets how many ticks the body has been simulated for.</summary>
    public int Age => _age;

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
    public Vector3d Centre
    {
        get
        {
            Vector3d sum = Vector3d.Zero;
            double mass = 0.0;
            for (int body = 0; body < _bodyCount; body++)
            {
                double weight = 1.0 / _inverseMass[body];
                sum += _position[body] * weight;
                mass += weight;
            }

            return mass > 0.0 ? sum / mass : Origin;
        }
    }

    /// <summary>Pushes the body that moves one bone.</summary>
    /// <param name="bone">The bone to push; a carried bone pushes the body carrying it.</param>
    /// <param name="momentum">The momentum to add, in kilogram engine-units per tick.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bone"/> is not a bone of this rig.</exception>
    /// <remarks>
    /// Divided by the body's own mass on the way in, so the same round moves a hand a long way and the
    /// pelvis barely at all.
    /// </remarks>
    public void AddImpulse(int bone, Vector3d momentum)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bone);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(bone, _boneCount);
        AddBodyImpulse(_rig.BodyOf(bone), momentum, Vector3d.Zero);
    }

    /// <summary>Pushes one body, at a point on it.</summary>
    /// <param name="body">The body index.</param>
    /// <param name="momentum">The momentum to add, in kilogram engine-units per tick.</param>
    /// <param name="arm">Where on the body it lands, from its centre of mass, in world axes and engine units.</param>
    /// <remarks>
    /// Off the centre of mass, a push spins the body as well as moving it — a round through a shoulder
    /// turns the trunk, which is most of what makes a hit read as a hit.
    /// </remarks>
    public void AddBodyImpulse(int body, Vector3d momentum, Vector3d arm)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(body);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(body, _position.Length);

        Matrix3f frame = Rotation(body);
        Matrix3f inertia = frame * _localInverseInertia[body] * frame.Transposed();
        _velocity[body] += momentum * _inverseMass[body];
        _spin[body] += RagdollSolver.Rotate(inertia, Vector3d.Cross(arm, momentum));
        IsAtRest = false;
        _stillTicks = 0;
    }

    /// <summary>Adds a velocity to every body.</summary>
    /// <param name="velocityPerTick">The velocity to add, in engine units per tick.</param>
    /// <remarks>
    /// The body's own motion at the moment it died. A guard shot while running keeps running, which is
    /// the difference between a corpse falling and a corpse being dropped.
    /// </remarks>
    public void AddVelocity(Vector3d velocityPerTick)
    {
        for (int body = 0; body < _velocity.Length; body++)
        {
            _velocity[body] += velocityPerTick;
        }

        IsAtRest = false;
        _stillTicks = 0;
    }

    /// <summary>Advances the body by one game tick.</summary>
    /// <param name="world">The geometry to collide against.</param>
    /// <param name="substeps">How many substeps to take; more is stiffer and more expensive.</param>
    /// <param name="gravityScale">What fraction of <see cref="Gravity"/> to fall at.</param>
    /// <param name="muscle">
    /// A pose for the joints to follow — the death clip, which keeps playing — or <see langword="null"/>
    /// for them to hold the pose the body died in.
    /// </param>
    /// <param name="muscleTicks">
    /// Over how many ticks from death the muscles let go; zero for a body limp from the start. Each joint
    /// lets go over its own share of it (<see cref="RagdollJoint.Tone"/>).
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="world"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="substeps"/> is not positive.</exception>
    /// <remarks>
    /// A body that has latched at rest does nothing here at all. Corpses are never removed in this game,
    /// so without that latch every kill would be a permanent per-tick cost for the rest of the mission.
    /// </remarks>
    public void Step(
        RagdollWorld world,
        int substeps,
        double gravityScale = 1.0,
        BonePose? muscle = null,
        double muscleTicks = 0.0)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentOutOfRangeException.ThrowIfLessThan(substeps, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(gravityScale);

        if (IsAtRest)
        {
            return;
        }

        // How far through the fade the body is: 0 at death, 1 once even the longest-held joint has let go.
        double progress = muscleTicks > 0.0 ? _age / muscleTicks : 1.0;
        _driving = progress < 1.0;
        if (_driving)
        {
            _following = muscle is not null && muscle.BoneCount == _boneCount;
            if (_following)
            {
                Aim(muscle!);
            }
            else
            {
                _held.CopyTo(_driveTarget, 0);
            }
        }

        _position.CopyTo(_tickStart, 0);
        Survey(world);

        double step = 1.0 / substeps;
        double gravity = Gravity * gravityScale * step;
        double stubborn = Math.Clamp(_restingTicks / (double)SettleRampTicks, 0.0, 1.0);
        double settle = 1.0 - (SettleDamping * stubborn);
        double linear = Math.Pow(LinearDamping * settle, step);
        double angular = Math.Pow(AngularDamping * settle, step);
        double joint = Math.Pow(1.0 - JointDamping, step);

        for (int i = 0; i < substeps; i++)
        {
            Integrate(step, gravity);
            if (_driving)
            {
                Drive(progress);
            }

            // Contacts, then joints, and repeated. Whichever runs LAST is the one satisfied: contacts last
            // leaves the joints visibly apart, joints last leaves a little penetration, which is exactly
            // what the slop deadband is there to absorb.
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
        for (int body = 0; body < _position.Length; body++)
        {
            moved += Vector3d.Distance(_tickStart[body], _position[body]);
        }

        // A body is "resting" while anything about it is touching. Losing contact WINDS THAT BACK rather
        // than clearing it: a body that lands hard bounces, and clearing it on the first bounce restarts
        // the settling help from nothing every time it leaves the floor.
        double drift = moved / _position.Length;
        // And never while the muscles are still pulling: that is a body being moved on purpose.
        _restingTicks = _contactCount > 0 && drift < SettleSpeedInUnits && !_driving
            ? Math.Min(_restingTicks + 1, SettleRampTicks)
            : Math.Max(0, _restingTicks - 2);
        _age++;
        Track(drift);
    }

    private void Integrate(double step, double gravity)
    {
        Array.Clear(_worldDone);
        for (int body = 0; body < _position.Length; body++)
        {
            _priorPosition[body] = _position[body];
            _priorOrientation[body] = _orientation[body];
            _repair[body] = Vector3d.Zero;
            _repairSpin[body] = Vector3d.Zero;

            _velocity[body] += new Vector3d(0.0, 0.0, -gravity);
            _position[body] += _velocity[body] * step;
            _orientation[body] = RagdollSolver.Advance(_orientation[body], _spin[body], step);
            _rotationStale[body] = true;

            Matrix3f frame = Rotation(body);
            _inverseInertia[body] = frame * _localInverseInertia[body] * frame.Transposed();
        }
    }

    private void Recover(double step, double linear, double angular)
    {
        for (int body = 0; body < _position.Length; body++)
        {
            // The repair is subtracted before the velocity is read off. In this solver a body's velocity
            // is MEASURED -- how far it moved over the substep -- which is right for motion and wrong for
            // a correction: pushing a bone out of the floor did not mean it was travelling. In a jointed
            // chain the push-out pulls the bone off its neighbour, the joint repairs THAT, the repair is
            // read back as more fabricated speed, and the body feeds itself into a dance. So corrections
            // are accumulated separately and discounted here: the position keeps them, the velocity never
            // sees them. Erin Catto's split impulses (GDC 2007).
            _velocity[body] = (_position[body] - _priorPosition[body] - _repair[body]) / step * linear;
            _spin[body] =
                (RagdollSolver.Recover(_priorOrientation[body], _orientation[body], step)
                    - (_repairSpin[body] / step)) * angular;
        }
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

    private double GeneralizedMass(int body, Vector3d arm, Vector3d normal)
    {
        Vector3d turn = Vector3d.Cross(arm, normal);
        return _inverseMass[body]
            + Vector3d.Dot(turn, RagdollSolver.Rotate(_inverseInertia[body], turn));
    }

    /// <summary>Turns a body, keeping its cached rotation matrix in step with its quaternion.</summary>
    /// <remarks>
    /// <b>The matrix is cached because rebuilding it was the solver's largest single cost.</b> There are
    /// far more rotations of vectors than there are orientation changes.
    /// </remarks>
    private void Turn(int body, Vector3d turn)
    {
        _orientation[body] = RagdollSolver.Advance(_orientation[body], turn, 1.0);
        _rotationStale[body] = true;
    }

    // Rebuilt on demand rather than on every write, because a projection typically turns a body two or
    // three times before anything reads it again.
    private Matrix3f Rotation(int body)
    {
        if (_rotationStale[body])
        {
            _rotation[body] = BonePose.ToMatrix(_orientation[body]);
            _rotationStale[body] = false;
        }

        return _rotation[body];
    }

    private Vector3d Turned(int body, Vector3d local) =>
        RagdollSolver.Rotate(Rotation(body), local);

    private Vector3d Turned(int body, Vector3f local) =>
        Turned(body, new Vector3d(local.X, local.Y, local.Z));

    private Vector3f Direction(int body, Vector3f local) =>
        RagdollSolver.Rotate(Rotation(body), local);

    // Whether two capsules already overlap, used once when the body is built.
    private bool Overlapping(int first, int second)
    {
        CapsuleOf(first, out Vector3d a0, out Vector3d a1, out double ra);
        CapsuleOf(second, out Vector3d b0, out Vector3d b1, out double rb);
        Vector3d onFirst = Vector3d.ClosestOnSegment(a0, a1, (b0 + b1) * 0.5);
        Vector3d onSecond = Vector3d.ClosestOnSegment(b0, b1, onFirst);
        onFirst = Vector3d.ClosestOnSegment(a0, a1, onSecond);
        onSecond = Vector3d.ClosestOnSegment(b0, b1, onFirst);
        return Vector3d.Distance(onFirst, onSecond) < ra + rb;
    }

    // Everything a corpse must still be: made of numbers, and near where it died.
    private bool IsSane()
    {
        for (int body = 0; body < _position.Length; body++)
        {
            Vector3d at = _position[body];
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

        // A body still being held by its muscles is not resting, however still it is: the fade has not
        // finished, and latching now would freeze it mid-collapse.
        if (averageMovement < RestThresholdInUnits && !_driving)
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
