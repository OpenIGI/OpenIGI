using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Physics;

/// <summary>
/// A rig prepared for the ragdoll solver: one rigid body per bone, with the capsule, mass, inertia and
/// joint limits the constraint passes need.
/// </summary>
/// <remarks>
/// <para>
/// Everything here is derived from the <see cref="BoneSkeleton"/> itself — no bone is named by index and
/// no per-joint table is hand-authored. That is deliberate: the retail rigs' joint roles have not been
/// measured, and this project does not guess at numbers it has not read. What the skeleton does state,
/// and what turns out to be enough, is the tree and the rest translations.
/// </para>
/// <para>
/// A bone's <b>origin is its joint</b>, at one end of the limb it carries, so the body it stands for is
/// the capsule reaching from there towards its children. That is why a centre of mass has to be carried
/// separately: a rigid body rotates about its centre, and a bone does not sit at its own.
/// </para>
/// <para>Build it once per skeleton and share it between every body wearing it; it holds no live state.</para>
/// </remarks>
public sealed class RagdollSkeleton
{
    /// <summary>What a whole body is taken to weigh, in kilograms.</summary>
    /// <remarks>
    /// The masses are shared out by capsule volume and then scaled to this, so they are real kilograms
    /// and an impulse in newton-seconds means what it says. It also means the mass ratios come from the
    /// rig's own proportions rather than from a table someone wrote.
    /// </remarks>
    public const double BodyMassInKilograms = 80.0;

    /// <summary>How thick a limb is relative to its length, as a fraction.</summary>
    /// <remarks>
    /// A limb's radius is a fraction of its own length rather than a constant, so a forearm is thinner
    /// than a thigh without either being measured. Clamped, because a rig has short bones that are not
    /// short limbs.
    /// </remarks>
    public const float RadiusOfLength = 0.22f;

    /// <summary>The thinnest a bone's capsule may be, in engine units — 4 cm.</summary>
    public const float MinimumRadiusInUnits = 164.0f;

    /// <summary>The thickest, in engine units — 30 cm.</summary>
    /// <remarks>
    /// A torso really is this thick, and a solver that thinks it is not lets an arm fold through a chest.
    /// </remarks>
    public const float MaximumRadiusInUnits = 1229.0f;

    /// <summary>Which of a bone's vertices decides how thick it is, as a fraction from the axis outwards.</summary>
    /// <remarks>
    /// The seventieth percentile rather than the furthest vertex. A limb has outliers — a pocket, a
    /// buckle, the flare of a boot — and sizing the capsule to those makes every bone too fat to touch
    /// anything. Sizing it to the bulk of the mesh is what makes a chest a chest and a forearm a forearm.
    /// </remarks>
    public const double ThicknessPercentile = 0.7;

    /// <summary>The smallest a bone is allowed to be for the purpose of its inertia, in engine units.</summary>
    /// <remarks>
    /// <para>
    /// Ten centimetres. <b>Inertia, not mass, is what makes a ragdoll come apart</b>, and this is the
    /// fix. The generalized mass a constraint divides by is
    /// <c>1/m + (r × n)ᵀ I⁻¹ (r × n)</c>; when <c>I</c> is tiny that term dominates, and the correction
    /// that should have nudged a bone instead spins it — which breaks the joint it was trying to close,
    /// so the next pass corrects harder, and the body shakes itself to pieces on the first thing it
    /// touches.
    /// </para>
    /// <para>
    /// A rig is full of bones with no length at all — a hand, a foot, the tip of the head — and those
    /// have the smallest inertia and the longest arms to their parents, which is the worst possible
    /// combination. Treating nothing as smaller than a fist is the standard remedy, and it matches the
    /// ten-centimetre floor mature engines document for dynamic bodies generally.
    /// </para>
    /// </remarks>
    public const float InertiaFloorInUnits = 409.6f;

    /// <summary>How far apart two bones must be in the rig before they are tested against each other.</summary>
    /// <remarks>
    /// Two joints. One is too near — a bone and its parent are held together by the joint between them,
    /// and testing them as well would only fight it. <b>Three turned out to be too far</b>: it leaves out
    /// a forearm against a chest and a shin against a pelvis, which are exactly the pairs a folded corpse
    /// puts through one another.
    /// </remarks>
    public const int SelfCollisionSeparation = 2;

    /// <summary>How far a joint may swing off its rest direction by default, in radians.</summary>
    /// <remarks>
    /// <para>
    /// Seventy degrees, and it is uniform because nothing has been measured that would justify anything
    /// else. A knee and a shoulder get the same cone here.
    /// </para>
    /// <para>
    /// <b>This is the honest weak point of the rig, and it is fixable by measurement rather than by
    /// invention</b> — see <see cref="RagdollLimits"/>, which reads the real range out of the animation
    /// corpus when one is available and leaves this as the fallback when it is not.
    /// </para>
    /// </remarks>
    public const float DefaultSwingLimit = 70f * MathF.PI / 180f;

    /// <summary>How far a joint may twist about its own axis by default, in radians.</summary>
    public const float DefaultTwistLimit = 45f * MathF.PI / 180f;

    private readonly RagdollBone[] _bones;
    private readonly RagdollJoint[] _joints;
    private readonly int[] _children;
    private readonly int[] _childOffsets;
    private readonly int[,] _hops;
    private readonly RagdollPair[] _pairs;

    private RagdollSkeleton(
        BoneSkeleton skeleton,
        RagdollBone[] bones,
        RagdollJoint[] joints,
        int[] children,
        int[] childOffsets,
        int[,] hops)
    {
        Skeleton = skeleton;
        _bones = bones;
        _joints = joints;
        _children = children;
        _childOffsets = childOffsets;
        _hops = hops;
        TotalMass = BodyMassInKilograms;

        List<RagdollPair> pairs = [];
        for (int first = 0; first < bones.Length; first++)
        {
            for (int second = first + 1; second < bones.Length; second++)
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

    /// <summary>Gets the number of bones, which is the number of rigid bodies.</summary>
    public int BoneCount => _bones.Length;

    /// <summary>Gets each bone's rigid body.</summary>
    public IReadOnlyList<RagdollBone> Bones => _bones;

    /// <summary>Gets the joints, one per bone that has a parent.</summary>
    public IReadOnlyList<RagdollJoint> Joints => _joints;

    /// <summary>Gets the bone pairs that are kept from passing through one another.</summary>
    /// <remarks>
    /// Without these a corpse folds through itself — an arm lies inside the chest, a leg through the
    /// other leg — and because the limbs are drawn where the solver put them, the body reads as flat and
    /// weightless rather than as something with a volume.
    /// </remarks>
    public IReadOnlyList<RagdollPair> SelfCollisionPairs => _pairs;

    /// <summary>Gets the sum of every bone's mass, in kilograms.</summary>
    public double TotalMass { get; }

    /// <summary>Gets a bone's rigid body.</summary>
    /// <param name="bone">The bone index.</param>
    /// <returns>The body.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bone"/> is not a bone of this rig.</exception>
    public RagdollBone BoneAt(int bone)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bone);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(bone, _bones.Length);
        return _bones[bone];
    }

    /// <summary>Gets the children of one bone.</summary>
    /// <param name="bone">The bone index.</param>
    /// <returns>Its children, in index order; empty for a bone that ends a limb.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bone"/> is not a bone of this rig.</exception>
    public ReadOnlySpan<int> ChildrenOf(int bone)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bone);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(bone, BoneCount);
        return _children.AsSpan(_childOffsets[bone], _childOffsets[bone + 1] - _childOffsets[bone]);
    }

    /// <summary>Gets how many joints apart two bones are in the rig.</summary>
    /// <param name="first">One bone.</param>
    /// <param name="second">The other.</param>
    /// <returns>The number of hops between them; zero for a bone and itself.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Either index is not a bone of this rig.</exception>
    public int HopsBetween(int first, int second)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(first);
        ArgumentOutOfRangeException.ThrowIfNegative(second);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(first, BoneCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(second, BoneCount);
        return _hops[first, second];
    }

    /// <summary>Prepares a rig for the solver.</summary>
    /// <param name="skeleton">The hierarchy, which must be topologically sorted.</param>
    /// <param name="limits">Measured joint limits, or <see langword="null"/> for the uniform defaults.</param>
    /// <param name="model">
    /// The skinned model this rig wears, so each bone's thickness can be measured from the geometry it
    /// actually carries; <see langword="null"/> falls back to a fraction of the bone's own length.
    /// </param>
    /// <returns>The prepared rig.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="skeleton"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The hierarchy cannot be resolved in one forward pass.</exception>
    public static RagdollSkeleton Build(
        BoneSkeleton skeleton,
        RagdollLimits? limits = null,
        MefModel? model = null)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        if (!skeleton.IsTopologicallySorted())
        {
            throw new ArgumentException(
                "The skeleton's parents are not in index order, so it cannot be prepared in one pass.",
                nameof(skeleton));
        }

        int count = skeleton.BoneCount;
        (int[] children, int[] offsets) = ChildLists(skeleton);

        // A bone's own geometry comes from where its children are: the capsule reaches from its origin
        // to the average of them, which is the limb it actually carries. A bone with none is given a
        // stub, because it still needs a mass to be pulled about by.
        Vector3f[] reach = new Vector3f[count];
        for (int bone = 0; bone < count; bone++)
        {
            ReadOnlySpan<int> kids = children.AsSpan(offsets[bone], offsets[bone + 1] - offsets[bone]);
            Vector3f sum = Vector3f.Zero;
            foreach (int child in kids)
            {
                sum += skeleton.RestTranslations[child];
            }

            reach[bone] = kids.Length > 0 ? sum / kids.Length : Vector3f.Zero;
        }

        double[] volume = new double[count];
        float[] length = new float[count];
        float[] radius = new float[count];
        double totalVolume = 0.0;

        float[]? measured = Thickness(skeleton, reach, model);

        for (int bone = 0; bone < count; bone++)
        {
            length[bone] = reach[bone].Length;
            radius[bone] = Math.Clamp(
                measured is not null && measured[bone] > 0f ? measured[bone] : length[bone] * RadiusOfLength,
                MinimumRadiusInUnits,
                MaximumRadiusInUnits);

            // Cylinder plus two hemispherical caps.
            double r = radius[bone];
            volume[bone] = (Math.PI * r * r * length[bone]) + (4.0 / 3.0 * Math.PI * r * r * r);
            totalVolume += volume[bone];
        }

        double[] mass = new double[count];
        for (int bone = 0; bone < count; bone++)
        {
            mass[bone] = BodyMassInKilograms * volume[bone] / totalVolume;
        }

        Condition(skeleton, mass);

        RagdollBone[] bones = new RagdollBone[count];
        for (int bone = 0; bone < count; bone++)
        {
            bones[bone] = MakeBone(reach[bone], length[bone], radius[bone], mass[bone]);
        }

        return new RagdollSkeleton(
            skeleton,
            bones,
            MakeJoints(skeleton, bones, limits),
            children,
            offsets,
            HopsOf(skeleton, children, offsets));
    }

    /// <summary>The most a bone may outweigh the one it hangs off.</summary>
    /// <remarks>
    /// <b>The single most effective thing that can be done to stop a ragdoll exploding</b>, and it is not
    /// a solver change at all. A chain solved one joint at a time diverges when a light link has to move
    /// a heavy sub-tree, and a rig taken at face value is full of those — a hand weighs a fraction of the
    /// forearm it swings, and the pelvis outweighs the whole leg below it many times over. Squeezing
    /// every neighbouring pair into a narrow band removes the failure mode outright.
    /// </remarks>
    public const double MaximumMassRatio = 1.25;

    // Walked root-first, which the topological sort makes a single pass: each bone's ratio to the root is
    // its parent's, times its own ratio clamped into the band. Then the whole set is rescaled so the body
    // still weighs what it did -- the conditioning changes how the mass is SHARED OUT, never how much of
    // it there is.
    private static void Condition(BoneSkeleton skeleton, double[] mass)
    {
        int count = mass.Length;
        double[] ratio = new double[count];
        double total = 0.0;
        double sum = 0.0;

        for (int bone = 0; bone < count; bone++)
        {
            int parent = skeleton.Parents[bone];
            if (parent == BoneSkeleton.NoParent)
            {
                ratio[bone] = 1.0;
            }
            else
            {
                double own = mass[parent] > 0.0 ? mass[bone] / mass[parent] : 1.0;
                ratio[bone] = ratio[parent] * Math.Clamp(own, 1.0 / MaximumMassRatio, MaximumMassRatio);
            }

            sum += ratio[bone];
            total += mass[bone];
        }

        if (sum <= 0.0)
        {
            return;
        }

        double perUnit = total / sum;
        for (int bone = 0; bone < count; bone++)
        {
            mass[bone] = ratio[bone] * perUnit;
        }
    }

    private static RagdollBone MakeBone(Vector3f reach, float length, float radius, double mass)
    {
        // A bone whose children sit exactly on it has no direction of its own; give it a sphere and an
        // arbitrary but consistent axis rather than a degenerate frame.
        Vector3f aim = length > 0f ? reach / length : Vector3f.UnitZ;
        Vector3f perpendicular = Perpendicular(aim);

        float half = length * 0.5f;

        // A capsule's inertia, taken as the cylinder it mostly is: half m r squared about its own axis,
        // and m (3 r squared + L squared) over twelve across it. Exact enough for something that only
        // has to fall convincingly, and always invertible, which the exact form is not for a sphere.
        //
        // The dimensions are floored FOR THE INERTIA ONLY -- the capsule keeps its real size for
        // collision and its real centre for the joints. See InertiaFloorInUnits: a bone that is
        // physically a point still has to resist being spun by the constraint that holds it on.
        double stout = Math.Max(radius, InertiaFloorInUnits);
        double stretch = Math.Max(length, InertiaFloorInUnits);
        double along = 0.5 * mass * stout * stout;
        double across = mass * ((3.0 * stout * stout) + (stretch * stretch)) / 12.0;

        // The tensor is diagonal in the frame the capsule is aligned to, and the bone's frame is not
        // that one -- so it is turned into the bone's: I = A diag A-transpose, with A the rotation
        // taking the local Z onto the aim. The inverse of a rotated diagonal is the rotation of the
        // inverse, so this is built already inverted.
        Matrix3f axis = FrameAlignedTo(aim, perpendicular);
        Matrix3f inverseDiagonal = new(
            (float)(1.0 / across), 0f, 0f,
            0f, (float)(1.0 / across), 0f,
            0f, 0f, (float)(1.0 / along));

        return new RagdollBone(
            aim * half,
            aim,
            perpendicular,
            half,
            radius,
            (float)(1.0 / mass),
            axis * inverseDiagonal * axis.Transposed());
    }

    private static RagdollJoint[] MakeJoints(
        BoneSkeleton skeleton,
        RagdollBone[] bones,
        RagdollLimits? limits)
    {
        List<RagdollJoint> joints = [];
        for (int bone = 0; bone < skeleton.BoneCount; bone++)
        {
            int parent = skeleton.Parents[bone];
            if (parent == BoneSkeleton.NoParent)
            {
                continue;
            }

            // The rest pose has every local rotation the identity, so a child's axis expressed in its
            // parent's frame is simply its own axis -- unless a measurement says the joint's range is
            // centred somewhere else, which is exactly what a knee's is.
            Vector3f centre = bones[bone].Aim;
            float swing = DefaultSwingLimit;
            float twistLow = -DefaultTwistLimit;
            float twistHigh = DefaultTwistLimit;

            if (limits is not null && limits.TryGet(bone, out RagdollJointRange range))
            {
                centre = range.SwingAxis;
                twistLow = range.TwistLow;
                twistHigh = range.TwistHigh;

                // The allowance is ONE angle, because the solver holds one -- see
                // RagdollBody.SolveDeviation. It is measured as one too: the widest the clips ever turn
                // this bone from its rest, however they turn it. Taking the wider of a separate swing
                // and twist measurement instead let a joint that does 60 of each deviate by 60 in a
                // direction that is both, which is how a spine came out bent double.
                swing = range.SwingLimit;
            }

            joints.Add(new RagdollJoint(
                parent,
                bone,
                skeleton.RestTranslations[bone] - bones[parent].Centre,
                -bones[bone].Centre,
                centre,
                swing,
                twistLow,
                twistHigh));
        }

        return [.. joints];
    }

    /// <summary>Measures how thick each bone is from the mesh it actually carries.</summary>
    /// <param name="skeleton">The hierarchy.</param>
    /// <param name="reach">Each bone's own axis, unnormalized.</param>
    /// <param name="model">The skinned model, or <see langword="null"/>.</param>
    /// <returns>A radius per bone in engine units, or <see langword="null"/> when there is nothing to measure.</returns>
    /// <remarks>
    /// <para>
    /// <b>Guessing this was the single worst assumption in the rig.</b> A bone's thickness was taken as a
    /// fraction of its own length, which makes a torso — short bones carrying a wide body — into a stick,
    /// and a stick is something an arm folds straight through. It is also why a corpse read as having no
    /// volume: the shape the physics saw was nothing like the shape being drawn.
    /// </para>
    /// <para>
    /// The model already knows. A skinned vertex is stored <em>in its own bone's frame</em> — that is why
    /// the skinner can transform it by the bone matrix with no inverse bind pose — so the distance from
    /// each vertex to its bone's axis is the body's real radius there, needing no bone to be named and no
    /// number to be invented.
    /// </para>
    /// </remarks>
    public static float[]? Thickness(BoneSkeleton skeleton, IReadOnlyList<Vector3f> reach, MefModel? model)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        ArgumentNullException.ThrowIfNull(reach);
        if (model is null || !MefSkinner.IsSkinned(model) || model.BaseVertexCount <= 0)
        {
            return null;
        }

        MefModel skinned = model;
        int count = skeleton.BoneCount;
        List<float>[] spread = new List<float>[count];
        for (int bone = 0; bone < count; bone++)
        {
            spread[bone] = [];
        }

        ReadOnlySpan<MefVertex> vertices = skinned.Vertices;
        int shared = Math.Min(skinned.BaseVertexCount, skinned.SkinInfluences.Length);
        for (int i = 0; i < shared && i < vertices.Length; i++)
        {
            int bone = skinned.SkinInfluences[i].BoneIndex;
            if (bone < 0 || bone >= count)
            {
                continue;
            }

            // Perpendicular distance from the bone's own axis. A bone with no axis of its own is a
            // point, so every direction is perpendicular and the plain length is the answer.
            Vector3f local = vertices[i].Position;
            Vector3f axis = reach[bone];
            float along = axis.LengthSquared > 0f ? Vector3f.Dot(local, axis.Normalized()) : 0f;
            Vector3f offset = axis.LengthSquared > 0f ? local - (axis.Normalized() * along) : local;
            spread[bone].Add(offset.Length);
        }

        float[] radius = new float[count];
        for (int bone = 0; bone < count; bone++)
        {
            if (spread[bone].Count == 0)
            {
                continue;
            }

            spread[bone].Sort();
            int at = Math.Min(
                spread[bone].Count - 1, (int)(spread[bone].Count * ThicknessPercentile));
            radius[bone] = spread[bone][at];
        }

        return radius;
    }

    /// <summary>Any unit vector perpendicular to the one given.</summary>
    /// <param name="axis">The axis, unit length.</param>
    /// <returns>A unit vector at right angles to it.</returns>
    /// <remarks>
    /// Which one does not matter, only that the same axis always yields the same answer: the twist limit
    /// measures an angle between two copies of it, so a reference that wandered would read as the joint
    /// spinning.
    /// </remarks>
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

    private static (int[] Children, int[] Offsets) ChildLists(BoneSkeleton skeleton)
    {
        int count = skeleton.BoneCount;
        int[] offsets = new int[count + 1];

        for (int bone = 0; bone < count; bone++)
        {
            int parent = skeleton.Parents[bone];
            if (parent != BoneSkeleton.NoParent && skeleton.RestTranslations[bone].LengthSquared > 0f)
            {
                offsets[parent + 1]++;
            }
        }

        for (int bone = 0; bone < count; bone++)
        {
            offsets[bone + 1] += offsets[bone];
        }

        int[] children = new int[offsets[count]];
        int[] filled = new int[count];
        for (int bone = 0; bone < count; bone++)
        {
            int parent = skeleton.Parents[bone];
            if (parent != BoneSkeleton.NoParent && skeleton.RestTranslations[bone].LengthSquared > 0f)
            {
                children[offsets[parent] + filled[parent]++] = bone;
            }
        }

        return (children, offsets);
    }

    private static int[,] HopsOf(BoneSkeleton skeleton, int[] children, int[] offsets)
    {
        int count = skeleton.BoneCount;
        int[,] hops = new int[count, count];
        Queue<int> frontier = new();

        for (int start = 0; start < count; start++)
        {
            for (int bone = 0; bone < count; bone++)
            {
                hops[start, bone] = bone == start ? 0 : int.MaxValue;
            }

            frontier.Clear();
            frontier.Enqueue(start);
            while (frontier.Count > 0)
            {
                int bone = frontier.Dequeue();
                int parent = skeleton.Parents[bone];
                if (parent != BoneSkeleton.NoParent && hops[start, parent] == int.MaxValue)
                {
                    hops[start, parent] = hops[start, bone] + 1;
                    frontier.Enqueue(parent);
                }

                for (int i = offsets[bone]; i < offsets[bone + 1]; i++)
                {
                    int child = children[i];
                    if (hops[start, child] == int.MaxValue)
                    {
                        hops[start, child] = hops[start, bone] + 1;
                        frontier.Enqueue(child);
                    }
                }
            }

            // A bone nothing reaches -- one with a zero rest translation and no children -- is treated as
            // infinitely far, which keeps it out of the impulse spread rather than crashing it.
            for (int bone = 0; bone < count; bone++)
            {
                if (hops[start, bone] == int.MaxValue)
                {
                    hops[start, bone] = count;
                }
            }
        }

        return hops;
    }
}
