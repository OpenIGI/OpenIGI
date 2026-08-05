using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;

namespace OpenIGI.Game.Physics;

/// <summary>
/// A rig prepared for the ragdoll solver: one particle per bone, with the lengths, masses and limits
/// the constraint passes need.
/// </summary>
/// <remarks>
/// <para>
/// Everything here is derived from the <see cref="BoneSkeleton"/> itself — no bone is named by index and
/// no per-joint table is hand-authored. That is deliberate: the retail rigs' joint roles have not been
/// measured, and this project does not guess at numbers it has not read. What the skeleton does state,
/// and what turns out to be enough, is the tree and the rest translations.
/// </para>
/// <para>
/// Build it once per skeleton and share it between every body wearing that rig; it holds no live state.
/// </para>
/// </remarks>
public sealed class RagdollSkeleton
{
    /// <summary>How wide a bone is treated as being, in engine units.</summary>
    /// <remarks>
    /// 0.09 m. Only the self-collision pass uses it, and there it is a floor on how near two bones that
    /// were already apart may come — so it wants to be about a limb's radius, not a body's.
    /// </remarks>
    public const float BoneRadiusInUnits = 368.64f;

    /// <summary>
    /// The fraction of a two-bone chain's total length that its ends may be pulled apart to.
    /// </summary>
    /// <remarks>
    /// <b>This one constant is what stops a knee bending backwards</b>, and it is worth saying why,
    /// because it replaces a whole hinge-limit apparatus. A joint can only invert by passing through the
    /// configuration where its two bones are in line — that is the only point at which the bend has no
    /// side. Capping the span below the straight length means the solver never reaches that point, so the
    /// sign of the bend is preserved by construction, in every joint, with no axis, no stored frame and
    /// no special case. At 0.999 an equal-limbed joint stops about 5° short of straight, which is also
    /// roughly where a real one stops.
    /// </remarks>
    public const float StraightSpanFactor = 0.999f;

    /// <summary>The fraction of a two-bone chain's total length that its ends may be folded down to.</summary>
    /// <remarks>0.28 lets an equal-limbed joint fold to about 147° from straight — a fully closed elbow.</remarks>
    public const float FoldedSpanFactor = 0.28f;

    /// <summary>How far apart two bones must be in the tree before they are tested against each other.</summary>
    /// <remarks>
    /// Four hops. Anything nearer is already held in place by the length and span constraints, and
    /// testing it would only fight them. Four is what separates an arm from the opposite leg, a hand
    /// from the head, and a foot from the chest — the pairs that actually pass through one another.
    /// </remarks>
    public const int SelfCollisionSeparation = 4;

    private readonly float[] _restLengths;
    private readonly float[] _inverseMasses;
    private readonly int[] _aimChildren;
    private readonly int[,] _hops;
    private readonly RagdollJoint[] _joints;
    private readonly RagdollPair[] _pairs;

    private RagdollSkeleton(
        BoneSkeleton skeleton,
        float[] restLengths,
        float[] inverseMasses,
        int[] aimChildren,
        int[,] hops,
        RagdollJoint[] joints,
        RagdollPair[] pairs)
    {
        Skeleton = skeleton;
        _restLengths = restLengths;
        _inverseMasses = inverseMasses;
        _aimChildren = aimChildren;
        _hops = hops;
        _joints = joints;
        _pairs = pairs;

        double total = 0.0;
        foreach (float inverse in inverseMasses)
        {
            total += 1.0 / inverse;
        }

        TotalMass = total;
    }

    /// <summary>Gets the rig this was built from.</summary>
    public BoneSkeleton Skeleton { get; }

    /// <summary>Gets the number of bones, which is the number of particles.</summary>
    public int BoneCount => _restLengths.Length;

    /// <summary>Gets each bone's distance from its parent, in engine units; zero for a root.</summary>
    public IReadOnlyList<float> RestLengths => _restLengths;

    /// <summary>Gets each bone's inverse mass.</summary>
    /// <remarks>
    /// Mass is <c>1 + descendants</c>: a bone carries the weight of everything hanging off it, so the
    /// pelvis and the spine are heavy and the hands and feet are light. It falls straight out of the
    /// tree, which is the only thing the skeleton actually tells us about a bone's importance.
    /// </remarks>
    public IReadOnlyList<float> InverseMasses => _inverseMasses;

    /// <summary>Gets the child each bone is aimed at, or <c>-1</c> for a bone with none.</summary>
    /// <remarks>
    /// <para>
    /// A particle cloud fixes where each bone <em>is</em>, not which way it faces. What recovers the
    /// facing is that a bone's rest translation says where its child sits in its own frame, so the
    /// direction from a bone's particle to its child's is exactly the direction that rest translation
    /// must end up pointing — two of the three degrees of freedom, straight out of the geometry.
    /// </para>
    /// <para>
    /// The lowest-indexed child with a non-zero rest translation is taken. A bone that forks — the
    /// pelvis, the chest — is aimed by one branch and the others hang off the result; that is a choice,
    /// and it is the same choice every frame, which is what matters for stability.
    /// </para>
    /// </remarks>
    public IReadOnlyList<int> AimChildren => _aimChildren;

    /// <summary>Gets the three-bone chains whose span is limited.</summary>
    public IReadOnlyList<RagdollJoint> Joints => _joints;

    /// <summary>Gets the bone pairs that are kept from passing through one another.</summary>
    public IReadOnlyList<RagdollPair> SelfCollisionPairs => _pairs;

    /// <summary>Gets the sum of every bone's mass, in the solver's own arbitrary mass unit.</summary>
    /// <remarks>
    /// What an impulse has to be divided by to reach a speed. It is the bridge between a weapon's
    /// momentum, which is a real quantity, and this rig's masses, which are a count of descendants.
    /// </remarks>
    public double TotalMass { get; }

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
    /// <returns>The prepared rig.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="skeleton"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The hierarchy cannot be resolved in one forward pass.</exception>
    public static RagdollSkeleton Build(BoneSkeleton skeleton)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        if (!skeleton.IsTopologicallySorted())
        {
            throw new ArgumentException(
                "The skeleton's parents are not in index order, so it cannot be prepared in one pass.",
                nameof(skeleton));
        }

        int count = skeleton.BoneCount;
        float[] lengths = new float[count];
        for (int bone = 0; bone < count; bone++)
        {
            lengths[bone] = skeleton.Parents[bone] == BoneSkeleton.NoParent
                ? 0f
                : skeleton.RestTranslations[bone].Length;
        }

        int[,] hops = HopsOf(skeleton);
        return new RagdollSkeleton(
            skeleton,
            lengths,
            InverseMassesOf(skeleton),
            AimChildrenOf(skeleton, lengths),
            hops,
            JointsOf(skeleton, lengths),
            PairsOf(skeleton, hops));
    }

    /// <summary>Gets a bone's inverse mass.</summary>
    /// <param name="bone">The bone index.</param>
    /// <returns>The inverse mass.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bone"/> is not a bone of this rig.</exception>
    public float InverseMassOf(int bone)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bone);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(bone, _inverseMasses.Length);
        return _inverseMasses[bone];
    }

    private static float[] InverseMassesOf(BoneSkeleton skeleton)
    {
        int count = skeleton.BoneCount;
        int[] descendants = new int[count];

        // Backwards, so a bone's own descendants are all counted before it reports to its parent. The
        // parents being in index order is what makes one pass enough.
        for (int bone = count - 1; bone >= 1; bone--)
        {
            int parent = skeleton.Parents[bone];
            if (parent != BoneSkeleton.NoParent)
            {
                descendants[parent] += descendants[bone] + 1;
            }
        }

        float[] inverse = new float[count];
        for (int bone = 0; bone < count; bone++)
        {
            inverse[bone] = 1f / (1f + descendants[bone]);
        }

        return inverse;
    }

    private static int[] AimChildrenOf(BoneSkeleton skeleton, float[] lengths)
    {
        int count = skeleton.BoneCount;
        int[] aim = new int[count];
        Array.Fill(aim, -1);

        // Backwards, so the LOWEST-indexed qualifying child is the one that survives.
        for (int bone = count - 1; bone >= 1; bone--)
        {
            int parent = skeleton.Parents[bone];
            if (parent != BoneSkeleton.NoParent && lengths[bone] > 0f)
            {
                aim[parent] = bone;
            }
        }

        return aim;
    }

    private static RagdollJoint[] JointsOf(BoneSkeleton skeleton, float[] lengths)
    {
        List<RagdollJoint> joints = [];
        for (int bone = 0; bone < skeleton.BoneCount; bone++)
        {
            int parent = skeleton.Parents[bone];
            if (parent == BoneSkeleton.NoParent)
            {
                continue;
            }

            int grandparent = skeleton.Parents[parent];
            if (grandparent == BoneSkeleton.NoParent)
            {
                continue;
            }

            float total = lengths[parent] + lengths[bone];
            if (total <= 0f)
            {
                continue;
            }

            // The floor can never be tighter than the two bones physically allow: with the ends that
            // close the length constraints and the span constraint would pull against each other for
            // ever. The 1.05 keeps a margin above that degenerate point.
            float reachable = Math.Abs(lengths[parent] - lengths[bone]) * 1.05f;
            float minimum = Math.Max(total * FoldedSpanFactor, reachable);
            float maximum = total * StraightSpanFactor;

            if (minimum < maximum)
            {
                joints.Add(new RagdollJoint(bone, parent, grandparent, minimum, maximum));
            }
        }

        return [.. joints];
    }

    private static RagdollPair[] PairsOf(BoneSkeleton skeleton, int[,] hops)
    {
        int count = skeleton.BoneCount;
        List<RagdollPair> pairs = [];

        for (int first = 0; first < count; first++)
        {
            for (int second = first + 1; second < count; second++)
            {
                if (hops[first, second] >= SelfCollisionSeparation)
                {
                    pairs.Add(new RagdollPair(first, second));
                }
            }
        }

        return [.. pairs];
    }

    private static int[,] HopsOf(BoneSkeleton skeleton)
    {
        int count = skeleton.BoneCount;
        int[,] hops = new int[count, count];
        List<int>[] neighbours = new List<int>[count];
        for (int bone = 0; bone < count; bone++)
        {
            neighbours[bone] = [];
        }

        for (int bone = 0; bone < count; bone++)
        {
            int parent = skeleton.Parents[bone];
            if (parent != BoneSkeleton.NoParent)
            {
                neighbours[bone].Add(parent);
                neighbours[parent].Add(bone);
            }
        }

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
                foreach (int next in neighbours[bone])
                {
                    if (hops[start, next] == int.MaxValue)
                    {
                        hops[start, next] = hops[start, bone] + 1;
                        frontier.Enqueue(next);
                    }
                }
            }
        }

        return hops;
    }
}

/// <summary>A three-bone chain whose two ends are held within a bend range.</summary>
/// <param name="Bone">The far bone.</param>
/// <param name="Parent">The bone between, which is the joint that bends.</param>
/// <param name="Grandparent">The near bone.</param>
/// <param name="MinimumSpan">How near the two ends may come, in engine units.</param>
/// <param name="MaximumSpan">How far apart they may be pulled, in engine units.</param>
public readonly record struct RagdollJoint(
    int Bone,
    int Parent,
    int Grandparent,
    float MinimumSpan,
    float MaximumSpan);

/// <summary>Two bones far enough apart in the rig that they are kept from passing through each other.</summary>
/// <param name="First">The lower bone index.</param>
/// <param name="Second">The higher bone index.</param>
public readonly record struct RagdollPair(int First, int Second);
