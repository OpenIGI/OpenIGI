using OpenIGI.Core.Numerics;

namespace OpenIGI.Formats.Animation;

/// <summary>
/// Turns an I.G.I. 2 clip into an I.G.I. 1 animation for a human skeleton of the first game, so a body of the first game can
/// play a move only the sequel has (its bomb's plant and defuse).
/// </summary>
/// <remarks>
/// <para>
/// The two games' human rigs are one rig: every limb's rest offset agrees to the millimetre. They differ in the order the bones
/// are kept -- the sequel lists them a generation at a time, the first game a limb at a time -- in three zero-length bones the
/// sequel puts between the chest and the neck and shoulders, and in bones the first game has and the sequel's clips do not
/// (the head's end, the weapon's chain). So the bones are matched by shape, not by number: a sequel bone is the first game's
/// child, of the bone its nearest matched ancestor is, whose rest offset from it is the sequel's summed offset to within
/// <see cref="MatchUnits"/>.
/// </para>
/// <para>
/// The clip is sampled every <see cref="BoneAnimation.FrameStep"/> -- the two games' clocks run alike, 160 a tick -- each bone's
/// world orientation built down the sequel's tree, and the first game's local orientations taken back out of them down its
/// own: a bone the sequel lacks keeps a local orientation from <c>fallback</c> (the rig's stand), and a sequel bone the first
/// game lacks is folded into its children. The root's translation is the sequel's, in the same frame the first game's is.
/// </para>
/// </remarks>
public static class Igi2ClipRetarget
{
    /// <summary>How near, in world units, two rest offsets must be to be the same bone: 5 mm.</summary>
    public const float MatchUnits = 20.48f;

    /// <summary>Matches a sequel clip's bones to a skeleton of the first game.</summary>
    /// <param name="clip">The clip.</param>
    /// <param name="target">The skeleton.</param>
    /// <returns>For each of the skeleton's bones, the clip's bone it is, or -1 for none.</returns>
    public static int[] Match(Igi2AnimationClip clip, BoneSkeleton target)
    {
        ArgumentNullException.ThrowIfNull(clip);
        ArgumentNullException.ThrowIfNull(target);
        IReadOnlyList<int> from = clip.Hierarchy.Parents;
        IReadOnlyList<Vector3f> fromRest = clip.Hierarchy.Translations;
        ReadOnlySpan<int> to = target.Parents;
        ReadOnlySpan<Vector3f> toRest = target.RestTranslations;

        int[] ofTarget = new int[target.BoneCount];
        Array.Fill(ofTarget, -1);
        int[] ofClip = new int[clip.BoneCount];
        Array.Fill(ofClip, -1);
        for (int bone = 0; bone < clip.BoneCount; bone++)
        {
            if (from[bone] < 0)
            {
                // The roots: each rig's one bone with no parent.
                int root = to.IndexOf(BoneSkeleton.NoParent);
                if (root >= 0 && ofTarget[root] < 0)
                {
                    ofClip[bone] = root;
                    ofTarget[root] = bone;
                }

                continue;
            }

            // Up to the nearest matched ancestor, summing the offsets on the way.
            Vector3f offset = fromRest[bone];
            int ancestor = from[bone];
            while (ancestor >= 0 && ofClip[ancestor] < 0)
            {
                offset += fromRest[ancestor];
                ancestor = from[ancestor];
            }

            if (ancestor < 0)
            {
                continue;
            }

            int parent = ofClip[ancestor];
            for (int child = 0; child < target.BoneCount; child++)
            {
                if (to[child] == parent && ofTarget[child] < 0 && Vector3f.Distance(toRest[child], offset) <= MatchUnits)
                {
                    ofClip[bone] = child;
                    ofTarget[child] = bone;
                    break;
                }
            }
        }

        return ofTarget;
    }

    /// <summary>Makes the animation.</summary>
    /// <param name="clip">The sequel's clip.</param>
    /// <param name="target">The first game's skeleton it is played on.</param>
    /// <param name="id">The animation's id in its set.</param>
    /// <param name="fallback">A local orientation for each of the skeleton's bones the clip does not move, or empty for none.</param>
    /// <param name="match">The bones matched, as <see cref="Match"/> gives them, or <see langword="null"/> to match them here.</param>
    /// <returns>The animation, cyclic when the clip loops.</returns>
    /// <exception cref="ArgumentException">The clip moves fewer than half the skeleton's bones: it is another rig's.</exception>
    public static BoneAnimation ToBoneAnimation(
        Igi2AnimationClip clip, BoneSkeleton target, int id, ReadOnlySpan<Quaternion32> fallback = default, int[]? match = null)
    {
        ArgumentNullException.ThrowIfNull(clip);
        ArgumentNullException.ThrowIfNull(target);
        match ??= Match(clip, target);
        if (match.Count(b => b >= 0) * 2 < target.BoneCount)
        {
            throw new ArgumentException($"Clip '{clip.Name}' fits {match.Count(b => b >= 0)} of the skeleton's {target.BoneCount} bones.", nameof(clip));
        }

        int bones = target.BoneCount;
        int steps = Math.Max(1, (int)Math.Ceiling(clip.TotalTime / BoneAnimation.FrameStep));
        List<BoneRotationKey>[] rotations = new List<BoneRotationKey>[bones];
        for (int bone = 0; bone < bones; bone++)
        {
            rotations[bone] = new List<BoneRotationKey>(steps + 1);
        }

        List<(Vector3f Position, float Time)> roots = new(steps + 1);
        Span<Quaternion32> sampled = new Quaternion32[clip.BoneCount];
        Quaternion32[] world = new Quaternion32[clip.BoneCount];
        Quaternion32[] targetWorld = new Quaternion32[bones];
        IReadOnlyList<int> from = clip.Hierarchy.Parents;
        ReadOnlySpan<int> to = target.Parents;
        for (int step = 0; step <= steps; step++)
        {
            float time = Math.Min(step * BoneAnimation.FrameStep, clip.TotalTime);

            // A looping clip's last key is its first, so the loop closes where the clip's own does.
            int at = clip.Loops && step == steps ? 0 : (int)time;
            Vector3f root = Igi2ClipSampler.Sample(clip, at, squad: true, sampled);
            for (int bone = 0; bone < clip.BoneCount; bone++)
            {
                world[bone] = from[bone] < 0 ? sampled[bone] : (world[from[bone]] * sampled[bone]).Normalized();
            }

            for (int bone = 0; bone < bones; bone++)
            {
                int parent = to[bone];
                Quaternion32 above = parent < 0 ? Quaternion32.Identity : targetWorld[parent];
                targetWorld[bone] = match[bone] >= 0
                    ? world[match[bone]]
                    : (above * (bone < fallback.Length ? fallback[bone] : Quaternion32.Identity)).Normalized();
                Quaternion32 local = (above.Conjugate() * targetWorld[bone]).Normalized();
                rotations[bone].Add(new BoneRotationKey(local, time, local, local));
            }

            roots.Add((root, time));
        }

        return BoneAnimation.Create(
            id,
            clip.TotalTime,
            [.. rotations.Select(static r => (IReadOnlyList<BoneRotationKey>)r)],
            RootKeys(roots),
            flags: clip.Loops ? BoneAnimation.CyclicFlag : 0u);
    }

    /// <summary>A pose of the first game's skeleton, as the sequel's clip numbers its bones: for reading the clip's attach objects
    /// (<see cref="Igi2AttachedParts"/>) off a body of the first game.</summary>
    /// <param name="pose">The first game's posed skeleton.</param>
    /// <param name="match">The bones matched, as <see cref="Match"/> gives them.</param>
    /// <param name="clip">The clip.</param>
    /// <returns>The pose, one bone for each of the clip's: a matched one where the first game's is, one the first game lacks
    /// hanging rigidly off its parent.</returns>
    public static BonePose ToClipPose(BonePose pose, int[] match, Igi2AnimationClip clip)
    {
        ArgumentNullException.ThrowIfNull(pose);
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(clip);
        int count = clip.BoneCount;
        Matrix3f[] rotations = new Matrix3f[count];
        Vector3f[] positions = new Vector3f[count];
        int[] parents = [.. clip.Hierarchy.Parents];
        for (int bone = 0; bone < count; bone++)
        {
            int target = Array.IndexOf(match, bone);
            if (target >= 0 && target < pose.BoneCount)
            {
                rotations[bone] = pose.Rotations[target];
                positions[bone] = pose.Positions[target];
                continue;
            }

            int parent = parents[bone];
            Matrix3f above = parent < 0 ? Matrix3f.Identity : rotations[parent];
            Vector3f at = parent < 0 ? Vector3f.Zero : positions[parent];

            // The palette's M . v, as BonePose places a bone on its parent.
            rotations[bone] = above;
            positions[bone] = at + above.Transposed().Transform(clip.Hierarchy.Translations[bone]);
        }

        return BonePose.FromBones(rotations, positions, parents);
    }

    /// <summary>Gets whether two clips are of one rig: the same bones under the same parents, whatever rest pose each keeps.</summary>
    /// <param name="a">One clip.</param>
    /// <param name="b">The other.</param>
    /// <returns><see langword="true"/> when their bone trees are the same.</returns>
    /// <remarks>A clip may keep a limb's rest a few centimetres off its rig's -- the sequel's carry clip does one forearm -- which
    /// loses the limb to <see cref="Match"/>; a clip of the same tree can take another's match instead.</remarks>
    public static bool SameTree(Igi2AnimationClip a, Igi2AnimationClip b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return a.BoneCount == b.BoneCount && a.Hierarchy.Parents.SequenceEqual(b.Hierarchy.Parents);
    }

    // The root's keys, each with the slope of the curve through its neighbours as both tangents. The sampler (BoneTrackSampler)
    // reads a translation key's tangents as slopes per unit of clip time and scales them by the segment's length -- unlike a
    // rotation key's, which are quaternions of the curve -- so a position put there throws the root far off between keys.
    private static List<BoneTranslationKey> RootKeys(List<(Vector3f Position, float Time)> roots)
    {
        List<BoneTranslationKey> keys = new(roots.Count);
        for (int i = 0; i < roots.Count; i++)
        {
            (Vector3f before, float from) = roots[Math.Max(0, i - 1)];
            (Vector3f after, float to) = roots[Math.Min(roots.Count - 1, i + 1)];
            float span = to - from;
            Vector3f slope = span > 0f ? (after - before) * (1f / span) : Vector3f.Zero;
            keys.Add(new BoneTranslationKey(roots[i].Position, roots[i].Time, slope, slope));
        }

        return keys;
    }
}
