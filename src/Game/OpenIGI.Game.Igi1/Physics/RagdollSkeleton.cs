using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Game.Igi1.Physics;

/// <summary>
/// A rig prepared for the ragdoll solver: the rigid bodies a skeleton is simulated as, with the capsule,
/// mass, inertia and joint each one needs.
/// </summary>
/// <remarks>
/// <para>
/// <b>Bodies are not bones.</b> A <see cref="RagdollProfile"/> picks which bones are simulated; the rest —
/// fingers, toes, the tip of the head, the weapon chain — are carried by the nearest simulated bone above
/// them, holding the turn they had when the body died. Every array here indexed by "body" is in profile
/// order, root first; <see cref="BodyOf"/> maps a bone onto the body that moves it.
/// </para>
/// <para>
/// A body moves in its <b>bone's frame</b>, whose origin is the joint at one end of the limb, so its centre
/// of mass and its capsule are carried separately as an offset and an axis in that frame. The capsule need
/// not run along the bone: a torso's runs across it (<see cref="RagdollShapeAxis.Across"/>).
/// </para>
/// <para>Build it once per skeleton and model and share it between every body wearing it; it holds no live state.</para>
/// </remarks>
public sealed class RagdollSkeleton
{
    /// <summary>What a whole body is taken to weigh, in kilograms.</summary>
    public const double BodyMassInKilograms = 80.0;

    /// <summary>How thick a limb is relative to its length, when there is no mesh to measure.</summary>
    public const float RadiusOfLength = 0.22f;

    /// <summary>The thinnest a capsule may be, in engine units — 4 cm.</summary>
    public const float MinimumRadiusInUnits = 164.0f;

    /// <summary>The thickest, in engine units — 30 cm.</summary>
    public const float MaximumRadiusInUnits = 1229.0f;

    /// <summary>Which of a body's vertices decides how thick it is, as a fraction from the axis outwards.</summary>
    /// <remarks>
    /// <para>
    /// Three quarters. The capsule has to sit on the <em>skin</em> — a corpse whose capsules are thinner
    /// than its mesh lies with its limbs sunk into the floor, which is the "collision is the wrong size" a
    /// player sees. It must also not be sized to the outliers, the flare of a boot or the edge of a
    /// pocket, or every limb is too fat to lie flat.
    /// </para>
    /// <para>
    /// A cylinder's vertices sit at its radius whatever percentile is taken, so on a limb this is close to
    /// the true thickness; on a flattened section such as a torso it falls between the half-depth and the
    /// half-width, which is the radius a capsule laid across that body should have.
    /// </para>
    /// </remarks>
    public const double ThicknessPercentile = 0.75;

    /// <summary>Which of a torso section's vertices decides how deep it is, front to back, as a fraction.</summary>
    /// <remarks>
    /// Nine tenths, higher than <see cref="ThicknessPercentile"/> because it is one axis rather than a
    /// radius: a round limb's vertices all sit near its surface, a torso's are spread between its middle
    /// and its front and back, and only the outer tenth of them is the surface it lies on.
    /// </remarks>
    public const double DepthPercentile = 0.9;

    /// <summary>How much of a body's vertex spread along its axis is ignored at each end, as a fraction.</summary>
    /// <remarks>A twentieth: enough to drop a stray vertex at the far end, not enough to shorten a limb.</remarks>
    public const double LengthTrim = 0.05;

    /// <summary>How few vertices a body may have and still be measured from its mesh.</summary>
    public const int MinimumMeasuredVertices = 8;

    /// <summary>The smallest a body is allowed to be for the purpose of its inertia, in engine units.</summary>
    /// <remarks>
    /// Ten centimetres. <b>Inertia, not mass, is what makes a ragdoll come apart</b>: the generalized mass a
    /// constraint divides by is <c>1/m + (r × n)ᵀ I⁻¹ (r × n)</c>, and when <c>I</c> is tiny that term turns
    /// the correction meant to nudge a body into one that spins it. Treating nothing as smaller than a fist
    /// is the standard remedy, and matches the floor mature engines document for dynamic bodies.
    /// </remarks>
    public const float InertiaFloorInUnits = 409.6f;

    /// <summary>How far apart two bodies must be in the rig before they are tested against each other.</summary>
    /// <remarks>
    /// Two joints. A body and its parent are held together by the joint between them and testing them as
    /// well would only fight it; two apart is a forearm against a chest, which a folded corpse puts
    /// through one another.
    /// </remarks>
    public const int SelfCollisionSeparation = 2;

    /// <summary>How far a joint may swing off its centre when nothing better is known, in radians.</summary>
    public const float DefaultSwingLimit = 70f * MathF.PI / 180f;

    /// <summary>How far a joint may twist about its own axis when nothing better is known, in radians.</summary>
    public const float DefaultTwistLimit = 45f * MathF.PI / 180f;

    /// <summary>The most a body may outweigh the one it hangs off, for a rig shared out by volume.</summary>
    /// <remarks>
    /// <para>
    /// Only ever applied to a rig without a measured table. A chain solved one joint at a time diverges
    /// when a light link has to move a heavy sub-tree, and a tree shared out purely by volume can produce
    /// those; three to one is comfortably inside what the substepped solver holds.
    /// </para>
    /// <para>
    /// <b>It used to be 1.25, applied to every rig, and that was the weightless look.</b> Squeezing every
    /// neighbouring pair to within a quarter of each other gave a finger the mass of a pelvis.
    /// </para>
    /// </remarks>
    public const double MaximumMassRatio = 3.0;

    private readonly RagdollBone[] _bodies;
    private readonly RagdollJoint[] _joints;
    private readonly int[] _bodyBone;
    private readonly int[] _bodyOf;
    private readonly int[] _parentBody;
    private readonly int[,] _hops;
    private readonly RagdollPair[] _pairs;

    private RagdollSkeleton(
        BoneSkeleton skeleton,
        RagdollProfile profile,
        RagdollBone[] bodies,
        RagdollJoint[] joints,
        int[] bodyBone,
        int[] bodyOf,
        int[] parentBody,
        int[,] hops)
    {
        Skeleton = skeleton;
        Profile = profile;
        _bodies = bodies;
        _joints = joints;
        _bodyBone = bodyBone;
        _bodyOf = bodyOf;
        _parentBody = parentBody;
        _hops = hops;
        TotalMass = BodyMassInKilograms;

        List<RagdollPair> pairs = [];
        for (int first = 0; first < bodies.Length; first++)
        {
            for (int second = first + 1; second < bodies.Length; second++)
            {
                if (hops[first, second] >= SelfCollisionSeparation)
                {
                    pairs.Add(new RagdollPair(first, second));
                }
            }
        }

        _pairs = [.. pairs];
    }

    /// <summary>Gets the rig this was built from.</summary>
    public BoneSkeleton Skeleton { get; }

    /// <summary>Gets which bones are simulated and how.</summary>
    public RagdollProfile Profile { get; }

    /// <summary>Gets the number of skeleton bones, which is what a drawn pose has.</summary>
    public int BoneCount => _bodyOf.Length;

    /// <summary>Gets the number of rigid bodies the skeleton is simulated as.</summary>
    public int BodyCount => _bodies.Length;

    /// <summary>Gets each body's shape and mass.</summary>
    public IReadOnlyList<RagdollBone> Bodies => _bodies;

    /// <summary>Gets the joints, one per body that has a body above it, in body order.</summary>
    public IReadOnlyList<RagdollJoint> Joints => _joints;

    /// <summary>Gets the body pairs that are kept from passing through one another.</summary>
    public IReadOnlyList<RagdollPair> SelfCollisionPairs => _pairs;

    /// <summary>Gets the sum of every body's mass, in kilograms.</summary>
    public double TotalMass { get; }

    /// <summary>Gets one body's shape and mass.</summary>
    /// <param name="body">The body index.</param>
    /// <returns>The body.</returns>
    public RagdollBone BodyAt(int body)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(body);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(body, _bodies.Length);
        return _bodies[body];
    }

    /// <summary>Gets the body that moves a bone.</summary>
    /// <param name="bone">The bone index.</param>
    /// <returns>The body: its own when it is simulated, otherwise its nearest simulated ancestor's.</returns>
    public int BodyOf(int bone)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bone);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(bone, _bodyOf.Length);
        return _bodyOf[bone];
    }

    /// <summary>Gets the bone a body moves in the frame of.</summary>
    /// <param name="body">The body index.</param>
    /// <returns>The bone.</returns>
    public int BoneOf(int body)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(body);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(body, _bodyBone.Length);
        return _bodyBone[body];
    }

    /// <summary>Gets the body above one body, or <c>-1</c> for the root.</summary>
    /// <param name="body">The body index.</param>
    /// <returns>The parent body.</returns>
    public int ParentOf(int body)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(body);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(body, _parentBody.Length);
        return _parentBody[body];
    }

    /// <summary>Gets whether a bone is simulated as a body of its own.</summary>
    /// <param name="bone">The bone index.</param>
    /// <returns><see langword="true"/> when it is; <see langword="false"/> when another body carries it.</returns>
    public bool IsSimulated(int bone) => _bodyBone[BodyOf(bone)] == bone;

    /// <summary>Gets how many joints apart two bodies are.</summary>
    /// <param name="first">One body.</param>
    /// <param name="second">The other.</param>
    /// <returns>The number of joints between them; zero for a body and itself.</returns>
    public int HopsBetween(int first, int second)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(first);
        ArgumentOutOfRangeException.ThrowIfNegative(second);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(first, BodyCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(second, BodyCount);
        return _hops[first, second];
    }

    /// <summary>Prepares a rig for the solver.</summary>
    /// <param name="skeleton">The hierarchy, which must be topologically sorted.</param>
    /// <param name="limits">Ranges measured from the rig's animations, or <see langword="null"/>.</param>
    /// <param name="model">
    /// The skinned model this rig wears, so each body's shape can be measured from the geometry it actually
    /// carries; <see langword="null"/> falls back to a fraction of each bone's own length.
    /// </param>
    /// <param name="profile">Which bones to simulate and how; <see langword="null"/> picks <see cref="RagdollProfile.For"/>.</param>
    /// <returns>The prepared rig.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="skeleton"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The hierarchy cannot be resolved in one forward pass, or the profile does not fit it.</exception>
    public static RagdollSkeleton Build(
        BoneSkeleton skeleton,
        RagdollLimits? limits = null,
        MefModel? model = null,
        RagdollProfile? profile = null) => Measure(skeleton, limits, model, profile, measured: null);

    /// <summary>
    /// Prepares a rig for a character that is not a <see cref="MefModel"/>, its shapes measured from points of its own skin. <b>Not retail.</b>
    /// </summary>
    /// <param name="skeleton">The skeleton.</param>
    /// <param name="measured">Each point of its skin with the bone that carries it, in the rig's rest pose, in engine units.</param>
    /// <param name="limits">The rig's limits; <see langword="null"/> for the defaults.</param>
    /// <param name="profile">Which bones to simulate and how; <see langword="null"/> picks <see cref="RagdollProfile.For"/>.</param>
    /// <returns>The prepared rig.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="skeleton"/> or <paramref name="measured"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The hierarchy cannot be resolved in one forward pass, or the profile does not fit it.</exception>
    public static RagdollSkeleton BuildMeasured(
        BoneSkeleton skeleton,
        IReadOnlyList<(int Bone, Vector3f At)> measured,
        RagdollLimits? limits = null,
        RagdollProfile? profile = null)
    {
        ArgumentNullException.ThrowIfNull(measured);
        return Measure(skeleton, limits, model: null, profile, measured);
    }

    private static RagdollSkeleton Measure(
        BoneSkeleton skeleton,
        RagdollLimits? limits,
        MefModel? model,
        RagdollProfile? profile,
        IReadOnlyList<(int Bone, Vector3f At)>? measured)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        if (!skeleton.IsTopologicallySorted())
        {
            throw new ArgumentException(
                "The skeleton's parents are not in index order, so it cannot be prepared in one pass.",
                nameof(skeleton));
        }

        profile ??= RagdollProfile.For(skeleton);
        int count = skeleton.BoneCount;
        int bodies = profile.Bodies.Count;

        int[] bodyBone = new int[bodies];
        int[] bodyOfBone = new int[count];
        Array.Fill(bodyOfBone, -1);
        for (int body = 0; body < bodies; body++)
        {
            int bone = profile.Bodies[body].Bone;
            if (bone < 0 || bone >= count || bodyOfBone[bone] >= 0)
            {
                throw new ArgumentException($"Profile body {body} names bone {bone}, which is not a free bone of the rig.");
            }

            bodyBone[body] = bone;
            bodyOfBone[bone] = body;
        }

        if (bodyOfBone[0] != 0 || skeleton.Parents[bodyBone[0]] != BoneSkeleton.NoParent)
        {
            throw new ArgumentException("A profile's first body must be the rig's root.");
        }

        // Every bone the profile does not simulate rides on the nearest one above it that it does. The sort
        // makes that one pass: a bone's parent is always resolved first.
        for (int bone = 0; bone < count; bone++)
        {
            if (bodyOfBone[bone] < 0)
            {
                int parent = skeleton.Parents[bone];
                bodyOfBone[bone] = parent == BoneSkeleton.NoParent ? 0 : bodyOfBone[parent];
            }
        }

        int[] parentBody = new int[bodies];
        for (int body = 0; body < bodies; body++)
        {
            int parent = skeleton.Parents[bodyBone[body]];
            parentBody[body] = parent == BoneSkeleton.NoParent ? -1 : bodyOfBone[parent];
            if (parentBody[body] >= body)
            {
                throw new ArgumentException($"Profile body {body} is listed before the body it hangs off.");
            }
        }

        Vector3f[] restAt = RestPositions(skeleton);
        RagdollBone[] shapes = Shapes(skeleton, profile, bodyBone, bodyOfBone, restAt, model, measured);

        return new RagdollSkeleton(
            skeleton,
            profile,
            shapes,
            MakeJoints(skeleton, profile, shapes, bodyBone, bodyOfBone, parentBody, restAt, limits),
            bodyBone,
            bodyOfBone,
            parentBody,
            HopsOf(parentBody));
    }

    /// <summary>Any unit vector perpendicular to the one given.</summary>
    /// <param name="axis">The axis, unit length.</param>
    /// <returns>A unit vector at right angles to it; the same one for the same axis every time.</returns>
    public static Vector3f Perpendicular(Vector3f axis)
    {
        Vector3f other = MathF.Abs(axis.Z) < 0.9f ? Vector3f.UnitZ : Vector3f.UnitX;
        Vector3f perpendicular = Vector3f.Cross(axis, other);
        return perpendicular.LengthSquared > 0f ? perpendicular.Normalized() : Vector3f.UnitX;
    }

    /// <summary>The rotation whose third column is the axis given.</summary>
    /// <param name="axis">The axis the capsule runs along, unit length.</param>
    /// <param name="perpendicular">A unit vector at right angles to it.</param>
    /// <returns>The frame, in the <c>M · v</c> convention.</returns>
    public static Matrix3f FrameAlignedTo(Vector3f axis, Vector3f perpendicular)
    {
        Vector3f third = Vector3f.Cross(axis, perpendicular);
        return new Matrix3f(
            perpendicular.X, third.X, axis.X,
            perpendicular.Y, third.Y, axis.Y,
            perpendicular.Z, third.Z, axis.Z);
    }

    /// <summary>A capsule's rigid body: its shape, its mass, and the inertia that goes with both.</summary>
    /// <param name="centre">The centre of mass in the bone's frame, which is also the capsule's middle.</param>
    /// <param name="aim">The capsule's axis in the bone's frame, unit length.</param>
    /// <param name="halfLength">Half the straight section.</param>
    /// <param name="radius">The radius.</param>
    /// <param name="mass">The mass, in kilograms.</param>
    /// <returns>The body.</returns>
    public static RagdollBone MakeBone(Vector3f centre, Vector3f aim, float halfLength, float radius, double mass)
    {
        Vector3f perpendicular = Perpendicular(aim);

        // Taken as the cylinder a capsule mostly is: half m r squared about its own axis, and
        // m (3 r squared + L squared) over twelve across it. The dimensions are floored FOR THE INERTIA ONLY
        // -- see InertiaFloorInUnits -- so the capsule keeps its real size for collision.
        double stout = Math.Max(radius, InertiaFloorInUnits);
        double stretch = Math.Max((halfLength * 2.0) + (radius * 2.0), InertiaFloorInUnits);
        double along = 0.5 * mass * stout * stout;
        double across = mass * ((3.0 * stout * stout) + (stretch * stretch)) / 12.0;

        // Diagonal in the capsule's own frame, which is not the bone's, so it is turned into the bone's:
        // I = A diag A-transpose, and the inverse of a turned diagonal is the turn of the inverse.
        Matrix3f axis = FrameAlignedTo(aim, perpendicular);
        Matrix3f inverseDiagonal = new(
            (float)(1.0 / across), 0f, 0f,
            0f, (float)(1.0 / across), 0f,
            0f, 0f, (float)(1.0 / along));

        return new RagdollBone(
            centre,
            aim,
            perpendicular,
            halfLength,
            radius,
            (float)(1.0 / mass),
            axis * inverseDiagonal * axis.Transposed());
    }

    // Every bone's origin in the rest pose, in model space. The rest pose has every local rotation the
    // identity, so this is just the translations summed down the tree.
    private static Vector3f[] RestPositions(BoneSkeleton skeleton)
    {
        Vector3f[] at = new Vector3f[skeleton.BoneCount];
        for (int bone = 0; bone < skeleton.BoneCount; bone++)
        {
            int parent = skeleton.Parents[bone];
            at[bone] = parent == BoneSkeleton.NoParent
                ? skeleton.RestTranslations[bone]
                : at[parent] + skeleton.RestTranslations[bone];
        }

        return at;
    }

    // The direction a body's capsule runs, in its bone's frame.
    private static Vector3f AxisFor(BoneSkeleton skeleton, RagdollShapeAxis kind, int bone) => kind switch
    {
        RagdollShapeAxis.Across => Vector3f.UnitX,
        RagdollShapeAxis.Upright => Vector3f.UnitZ,
        _ => RagdollLimits.AxisOf(skeleton, bone),
    };

    /// <summary>Measures every body's capsule from the vertices it carries, and gives each its mass.</summary>
    /// <remarks>
    /// <para>
    /// <b>The shape the physics sees has to be the shape that is drawn</b>, or a corpse lies sunk into the
    /// floor, floats above it, or folds a forearm through a chest it thinks is a stick. A skinned vertex is
    /// stored <em>in its own bone's frame</em> — the skinner transforms it by the bone matrix with no
    /// inverse bind pose — so every vertex a body carries, its own bone's and its carried bones', can be put
    /// in the body's frame by the rest offsets alone.
    /// </para>
    /// <para>
    /// The capsule is then fitted to that cloud: through its centroid, along the profile's axis, as long as
    /// the cloud reaches along it (less a twentieth at each end) and as thick as three quarters of it. The
    /// old fit measured each vertex against an infinite line through the joint, using only one bone's
    /// vertices, which made a ribcage — short bones carrying a wide body — into a 4 cm stick.
    /// </para>
    /// </remarks>
    private static RagdollBone[] Shapes(
        BoneSkeleton skeleton,
        RagdollProfile profile,
        int[] bodyBone,
        int[] bodyOfBone,
        Vector3f[] restAt,
        MefModel? model,
        IReadOnlyList<(int Bone, Vector3f At)>? measured)
    {
        int bodies = bodyBone.Length;
        List<Vector3f>[] clouds = new List<Vector3f>[bodies];
        for (int body = 0; body < bodies; body++)
        {
            clouds[body] = [];
        }

        // A character of another format: the points of its skin it was measured by (Build's measured overload).
        if (measured is not null)
        {
            foreach ((int bone, Vector3f at) in measured)
            {
                int body = bodyOfBone[bone];
                clouds[body].Add(at - restAt[bodyBone[body]]);
            }
        }

        if (model is not null && MefSkinner.IsSkinned(model) && model.BaseVertexCount > 0)
        {
            ReadOnlySpan<MefVertex> vertices = model.Vertices;
            int shared = Math.Min(Math.Min(model.BaseVertexCount, model.SkinInfluences.Length), vertices.Length);
            for (int i = 0; i < shared; i++)
            {
                int bone = model.SkinInfluences[i].BoneIndex;
                if (bone < 0 || bone >= skeleton.BoneCount)
                {
                    continue;
                }

                int body = bodyOfBone[bone];
                clouds[body].Add(vertices[i].Position + restAt[bone] - restAt[bodyBone[body]]);
            }
        }

        Vector3f[] centre = new Vector3f[bodies];
        Vector3f[] aim = new Vector3f[bodies];
        float[] half = new float[bodies];
        float[] radius = new float[bodies];
        double[] volume = new double[bodies];
        for (int body = 0; body < bodies; body++)
        {
            RagdollBodySpec spec = profile.Bodies[body];
            aim[body] = AxisFor(skeleton, spec.Axis, bodyBone[body]);
            if (clouds[body].Count >= MinimumMeasuredVertices)
            {
                (centre[body], half[body], radius[body]) = Fit(
                    clouds[body], aim[body], spec.Axis == RagdollShapeAxis.Across ? Vector3f.UnitY : null);
            }
            else
            {
                (centre[body], half[body], radius[body]) = Guess(skeleton, bodyBone[body], spec.Axis);
            }

            double r = radius[body];
            volume[body] = (Math.PI * r * r * half[body] * 2.0) + (4.0 / 3.0 * Math.PI * r * r * r);
        }

        double[] mass = Masses(skeleton, profile, bodyBone, volume);

        RagdollBone[] shapes = new RagdollBone[bodies];
        for (int body = 0; body < bodies; body++)
        {
            shapes[body] = MakeBone(centre[body], aim[body], half[body], radius[body], mass[body]);
        }

        return shapes;
    }

    // A capsule laid across a torso takes its radius from the body's DEPTH alone, front to back. Measured
    // over the whole cross-section, the height of the section counts too, and a belly that is forty
    // centimetres tall became a forty-centimetre ball -- round in exactly the direction the body lies on.
    // The sections above and below it are other capsules' business.
    private static (Vector3f Centre, float Half, float Radius) Fit(List<Vector3f> cloud, Vector3f aim, Vector3f? depth)
    {
        Vector3f middle = Vector3f.Zero;
        foreach (Vector3f point in cloud)
        {
            middle += point;
        }

        middle /= cloud.Count;

        float[] along = new float[cloud.Count];
        float[] away = new float[cloud.Count];
        for (int i = 0; i < cloud.Count; i++)
        {
            Vector3f offset = cloud[i] - middle;
            along[i] = Vector3f.Dot(offset, aim);
            away[i] = depth is { } across
                ? MathF.Abs(Vector3f.Dot(offset, across))
                : (offset - (aim * along[i])).Length;
        }

        Array.Sort(along);
        Array.Sort(away);
        float low = along[(int)(LengthTrim * (along.Length - 1))];
        float high = along[(int)((1.0 - LengthTrim) * (along.Length - 1))];
        double percentile = depth is null ? ThicknessPercentile : DepthPercentile;
        float radius = Math.Clamp(
            away[(int)(percentile * (away.Length - 1))], MinimumRadiusInUnits, MaximumRadiusInUnits);
        float half = MathF.Max(0f, ((high - low) * 0.5f) - radius);
        return (middle + (aim * ((low + high) * 0.5f)), half, radius);
    }

    // No mesh to measure: a limb is a capsule from its joint to where its children sit, as thick as a
    // fraction of that.
    private static (Vector3f Centre, float Half, float Radius) Guess(
        BoneSkeleton skeleton, int bone, RagdollShapeAxis kind)
    {
        Vector3f reach = Vector3f.Zero;
        int kids = 0;
        for (int child = 0; child < skeleton.BoneCount; child++)
        {
            if (skeleton.Parents[child] == bone && skeleton.RestTranslations[child].LengthSquared > 0f)
            {
                reach += skeleton.RestTranslations[child];
                kids++;
            }
        }

        if (kids > 0)
        {
            reach /= kids;
        }

        float length = reach.Length;
        float radius = Math.Clamp(length * RadiusOfLength, MinimumRadiusInUnits, MaximumRadiusInUnits);
        float half = kind == RagdollShapeAxis.AlongBone ? length * 0.5f : radius;
        return (reach * 0.5f, half, radius);
    }

    private static double[] Masses(BoneSkeleton skeleton, RagdollProfile profile, int[] bodyBone, double[] volume)
    {
        int bodies = bodyBone.Length;
        double[] mass = new double[bodies];
        bool measured = true;
        for (int body = 0; body < bodies; body++)
        {
            measured &= !double.IsNaN(profile.Bodies[body].MassFraction);
        }

        if (measured)
        {
            double sum = 0.0;
            for (int body = 0; body < bodies; body++)
            {
                sum += profile.Bodies[body].MassFraction;
            }

            for (int body = 0; body < bodies; body++)
            {
                mass[body] = BodyMassInKilograms * profile.Bodies[body].MassFraction / sum;
            }

            return mass;
        }

        double total = 0.0;
        for (int body = 0; body < bodies; body++)
        {
            total += volume[body];
        }

        for (int body = 0; body < bodies; body++)
        {
            mass[body] = BodyMassInKilograms * volume[body] / total;
        }

        Condition(skeleton, bodyBone, mass);
        return mass;
    }

    // Walked root-first: each body's ratio to the root is its parent's, times its own ratio clamped into
    // the band. Then the whole set is rescaled so the body still weighs what it did -- this changes how
    // the mass is SHARED OUT, never how much of it there is.
    private static void Condition(BoneSkeleton skeleton, int[] bodyBone, double[] mass)
    {
        int bodies = mass.Length;
        int[] parentBody = new int[bodies];
        Dictionary<int, int> bodyOfBone = [];
        for (int body = 0; body < bodies; body++)
        {
            bodyOfBone[bodyBone[body]] = body;
        }

        for (int body = 0; body < bodies; body++)
        {
            parentBody[body] = -1;
            for (int bone = skeleton.Parents[bodyBone[body]]; bone != BoneSkeleton.NoParent; bone = skeleton.Parents[bone])
            {
                if (bodyOfBone.TryGetValue(bone, out int found))
                {
                    parentBody[body] = found;
                    break;
                }
            }
        }

        double[] ratio = new double[bodies];
        double total = 0.0;
        double sum = 0.0;
        for (int body = 0; body < bodies; body++)
        {
            int parent = parentBody[body];
            if (parent < 0)
            {
                ratio[body] = 1.0;
            }
            else
            {
                double own = mass[parent] > 0.0 ? mass[body] / mass[parent] : 1.0;
                ratio[body] = ratio[parent] * Math.Clamp(own, 1.0 / MaximumMassRatio, MaximumMassRatio);
            }

            sum += ratio[body];
            total += mass[body];
        }

        if (sum <= 0.0)
        {
            return;
        }

        double perUnit = total / sum;
        for (int body = 0; body < bodies; body++)
        {
            mass[body] = ratio[body] * perUnit;
        }
    }

    /// <summary>Builds each body's joint to the body above it.</summary>
    /// <remarks>
    /// <para>
    /// The anchors here are the <b>rest pose's</b>. A body that carries a bone between itself and its parent
    /// body — the chest carries nothing, but the abdomen carries the upper spine below the chest's joint —
    /// has its real anchor decided by how that carried bone was turned when the body died, so
    /// <see cref="RagdollBody.Seed"/> recomputes them from the pose it is given.
    /// </para>
    /// <para>
    /// A hinge's axis is taken from the animations when they turned the bone at all, and its sign is
    /// settled by which way the profile says the joint bends; with nothing measured it is worked out from
    /// that direction alone.
    /// </para>
    /// </remarks>
    private static RagdollJoint[] MakeJoints(
        BoneSkeleton skeleton,
        RagdollProfile profile,
        RagdollBone[] shapes,
        int[] bodyBone,
        int[] bodyOfBone,
        int[] parentBody,
        Vector3f[] restAt,
        RagdollLimits? limits)
    {
        List<RagdollJoint> joints = [];
        for (int body = 1; body < bodyBone.Length; body++)
        {
            int parent = parentBody[body];
            int bone = bodyBone[body];
            RagdollJointSpec spec = profile.Joints[body];
            Vector3f twistAxis = RagdollLimits.AxisOf(skeleton, bone);

            bool known = limits is not null && limits.TryGet(bone, out _);
            RagdollJointRange range = default;
            if (known)
            {
                limits!.TryGet(bone, out range);
            }

            Vector3f centre = known ? range.SwingAxis : twistAxis;
            float swing = spec.Swing;
            float twistLow = spec.TwistLow;
            float twistHigh = spec.TwistHigh;

            // A rig with no table of its own takes what its clips demonstrate.
            if (!profile.IsHuman && known)
            {
                swing = range.SwingLimit;
                twistLow = range.TwistLow;
                twistHigh = range.TwistHigh;
            }

            Vector3f bendAxis = Vector3f.Zero;
            if (spec.Kind == RagdollJointKind.Hinge)
            {
                bendAxis = HingeAxis(twistAxis, known ? range.BendAxis : Vector3f.Zero, spec.BendToward);
            }

            joints.Add(new RagdollJoint(
                parent,
                body,
                restAt[bone] - restAt[bodyBone[parent]] - shapes[parent].Centre,
                -shapes[body].Centre,
                centre,
                swing,
                twistLow,
                twistHigh,
                spec.Kind,
                twistAxis,
                bendAxis,
                spec.BendLow,
                spec.BendHigh,
                spec.Tone,
                spec.CoupledKnee >= 0 && spec.CoupledKnee < bodyOfBone.Length ? bodyOfBone[spec.CoupledKnee] : -1,
                spec.FlexStraight,
                spec.FlexBent,
                spec.SwingSide,
                spec.Slump));
        }

        return [.. joints];
    }

    /// <summary>A hinge's axis: the measured one when there is one, signed so positive bends the way it should.</summary>
    /// <param name="twist">The child's own length, unit.</param>
    /// <param name="measured">The axis the animations turned it about, or zero.</param>
    /// <param name="toward">The way the joint bends, in the parent's frame.</param>
    /// <returns>A unit axis square to <paramref name="twist"/>.</returns>
    /// <remarks>
    /// Turning <paramref name="twist"/> a little about axis <c>h</c> moves it along <c>h × twist</c>, so the
    /// axis is the right way round exactly when that points the way the joint bends.
    /// </remarks>
    public static Vector3f HingeAxis(Vector3f twist, Vector3f measured, Vector3f toward)
    {
        Vector3f axis = measured - (twist * Vector3f.Dot(measured, twist));
        if (axis.LengthSquared < 1e-4f)
        {
            axis = Vector3f.Cross(twist, toward);
        }

        if (axis.LengthSquared < 1e-8f)
        {
            axis = Perpendicular(twist);
        }

        axis = axis.Normalized();
        if (toward.LengthSquared > 0f && Vector3f.Dot(Vector3f.Cross(axis, twist), toward) < 0f)
        {
            axis = -axis;
        }

        return axis;
    }

    private static int[,] HopsOf(int[] parentBody)
    {
        int bodies = parentBody.Length;
        List<int>[] links = new List<int>[bodies];
        for (int body = 0; body < bodies; body++)
        {
            links[body] = [];
        }

        for (int body = 0; body < bodies; body++)
        {
            if (parentBody[body] >= 0)
            {
                links[body].Add(parentBody[body]);
                links[parentBody[body]].Add(body);
            }
        }

        int[,] hops = new int[bodies, bodies];
        Queue<int> frontier = new();
        for (int start = 0; start < bodies; start++)
        {
            for (int body = 0; body < bodies; body++)
            {
                hops[start, body] = body == start ? 0 : bodies;
            }

            frontier.Clear();
            frontier.Enqueue(start);
            while (frontier.Count > 0)
            {
                int body = frontier.Dequeue();
                foreach (int next in links[body])
                {
                    if (hops[start, next] == bodies && next != start)
                    {
                        hops[start, next] = hops[start, body] + 1;
                        frontier.Enqueue(next);
                    }
                }
            }
        }

        return hops;
    }
}
