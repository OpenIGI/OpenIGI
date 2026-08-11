using OpenIGI.Core.Numerics;

namespace OpenIGI.Formats.Animation;

/// <summary>
/// A skeleton instance: one node per bone, each able to carry an orientation source, and the pose those
/// sources produce.
/// </summary>
/// <remarks>
/// <para>
/// This is the original's <i>bone object</i> — the thing <c>0x4D2D60</c> builds and every drawable
/// carries inline at its <c>+104</c>. Its palette is <c>132</c> bytes a bone; the fields this reproduces
/// are the source pointer at <c>+128</c>, the propagate flag <c>0x20000</c> in the flag word at
/// <c>+4</c>, and the world position and rotation at <c>+24</c> and <c>+36</c> that
/// <see cref="BonePose"/> fills.
/// </para>
/// <para>
/// <b>A source is inherited.</b> A node with none of its own uses whatever came down from above, and a
/// node's own source only reaches its children when its <c>0x20000</c> flag is set — which
/// <c>0x4D61D0</c> sets on every node it starts a clip on. That is how one clip started on bone 0 drives
/// a whole skeleton without a per-bone binding.
/// </para>
/// <para>
/// <b>What this does not do.</b> The original wraps every clip in a blend node
/// (<c>{ i32 type, void *child }</c> slots, <c>-1</c> empty / <c>0</c> blend / <c>1</c> clip) and can
/// cross-fade two branches with a weight from a callback on the node (<c>0x4D3020</c>, <c>0x4D5EC0</c>,
/// <c>0x4D5FF0</c>). Only the immediate path is built here — the one <c>0x4D61D0</c> takes when the
/// blend time is under a tick — so <see cref="Play"/> replaces a source outright rather than easing into
/// it. Blending is the next piece of this runtime, not a simplification of it.
/// </para>
/// </remarks>
public sealed class BoneObject
{
    private readonly BoneAnimationSet _set;
    private readonly BoneClip?[] _own;
    private readonly bool[] _propagate;
    private readonly BoneClip?[] _resolved;
    private readonly BoneClip?[] _inherited;
    private readonly Quaternion32[] _locals;

    /// <summary>Gets whether the last tick changed any clip's sampled time.</summary>
    public bool ChangedThisTick { get; private set; }

    /// <summary>Initializes a new instance of the <see cref="BoneObject"/> class.</summary>
    /// <param name="set">The animation set whose skeleton this instances and whose clips it can play.</param>
    /// <exception cref="ArgumentNullException"><paramref name="set"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The skeleton cannot be evaluated in one forward pass.</exception>
    public BoneObject(BoneAnimationSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        if (!set.Skeleton.IsTopologicallySorted())
        {
            throw new ArgumentException(
                "The skeleton's parents are not in index order, so it cannot be posed in one pass.",
                nameof(set));
        }

        _set = set;
        int bones = set.Skeleton.BoneCount;
        _own = new BoneClip?[bones];
        _propagate = new bool[bones];
        _resolved = new BoneClip?[bones];
        _inherited = new BoneClip?[bones];
        _locals = new Quaternion32[bones];
    }

    /// <summary>Gets the animation set backing this object.</summary>
    public BoneAnimationSet Set => _set;

    /// <summary>Gets the skeleton being posed.</summary>
    public BoneSkeleton Skeleton => _set.Skeleton;

    /// <summary>Gets the number of bones.</summary>
    public int BoneCount => _own.Length;

    /// <summary>Starts a clip on a bone, replacing whatever that bone was sourced from.</summary>
    /// <param name="bone">The bone to attach the source to; <c>0</c> drives the whole skeleton.</param>
    /// <param name="animationId">The animation id, as the set's sparse table addresses it.</param>
    /// <param name="loops">Whether the clip wraps at its duration.</param>
    /// <param name="backwards">Whether the clip plays from its duration down to zero.</param>
    /// <returns>The clip, or <see langword="null"/> when the set does not define that id.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bone"/> is not a bone.</exception>
    /// <remarks>
    /// Returning <see langword="null"/> rather than throwing matches the original, which leaves undefined
    /// ids as empty slots in a sparse table and reports <c>"Animation #%d not found"</c> without
    /// stopping. Setting the source also raises the node's propagate flag, exactly as <c>0x4D61D0</c>
    /// does with <c>node[1] |= 0x20000</c>.
    /// </remarks>
    public BoneClip? Play(int bone, int animationId, bool loops = false, bool backwards = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bone);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(bone, _own.Length);

        if (!_set.TryGet(animationId, out BoneAnimation animation))
        {
            return null;
        }

        BoneClip clip = new(animation, bone, loops, backwards);
        _own[bone] = clip;
        _propagate[bone] = true;
        return clip;
    }

    /// <summary>Removes a bone's own source, so it inherits again.</summary>
    /// <param name="bone">The bone.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bone"/> is not a bone.</exception>
    public void Stop(int bone)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bone);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(bone, _own.Length);

        _own[bone] = null;
        _propagate[bone] = false;
    }

    /// <summary>Gets the clip attached to a bone itself, ignoring inheritance.</summary>
    /// <param name="bone">The bone.</param>
    /// <returns>The clip, or <see langword="null"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bone"/> is not a bone.</exception>
    public BoneClip? SourceOf(int bone)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bone);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(bone, _own.Length);
        return _own[bone];
    }

    /// <summary>Gets the clip a bone is actually driven by, after inheritance.</summary>
    /// <param name="bone">The bone.</param>
    /// <returns>The clip, or <see langword="null"/> when nothing reaches that bone.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bone"/> is not a bone.</exception>
    public BoneClip? EffectiveSourceOf(int bone)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bone);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(bone, _own.Length);

        Resolve();
        return _resolved[bone];
    }

    /// <summary>Samples the root-motion track of the clip attached to bone 0.</summary>
    /// <returns>The root's position at the current time, or zero when bone 0 has no source.</returns>
    /// <remarks>
    /// <para>
    /// <c>0x4D2FB0</c>, reached from every consumer as <c>(out position, out delta, boneObject)</c>. It
    /// looks at the <b>root node's own source</b> — <c>boneObject[2] + 128</c>, not an inherited one —
    /// and asks it for both, through the clip's advance callback <c>0x4D4840</c>.
    /// </para>
    /// <para>
    /// <b>This is not only locomotion.</b> On the first-person rig it is the weapon's <i>hold offset</i>:
    /// every weapon's standing clip carries a translation track whose value is where that weapon sits
    /// relative to the eye, and <c>0x464BD0</c> commits the view object's position as
    /// <c>eye + orientation · thisValue</c>. Dropping it puts every weapon in the middle of the screen.
    /// </para>
    /// </remarks>
    public Vector3f SampleRootMotion() => _own[0]?.SampleRoot() ?? default;

    /// <summary>How far the root clip moved the body on the last tick — the step that actually moves it.</summary>
    /// <returns>The per-tick root-motion delta, in engine units.</returns>
    public Vector3f RootMotionDelta() => _own[0]?.RootDelta ?? default;

    /// <summary>Advances every distinct clip on the object by one tick.</summary>
    /// <remarks>
    /// Each clip is ticked once however many bones inherit it — a clip is one clock, and ticking it per
    /// bone would run a 33-bone skeleton thirty-three times too fast.
    /// </remarks>
    public void Tick()
    {
        ChangedThisTick = false;
        for (int bone = 0; bone < _own.Length; bone++)
        {
            BoneClip? clip = _own[bone];
            if (clip is null)
            {
                continue;
            }

            float before = clip.Time;
            clip.Tick();
            ChangedThisTick |= before != clip.Time;
        }
    }

    /// <summary>Poses the skeleton.</summary>
    /// <param name="rootRotation">
    /// The orientation applied above the root — the drawable's own 3×3, which <c>0x4D38C0</c> passes in.
    /// </param>
    /// <param name="rootPosition">
    /// Where the root sits. Defaults to the origin, which is what <c>0x4D38C0</c> passes: the palette is
    /// built in the object's own space and the object's world position is added at use.
    /// </param>
    /// <returns>The posed skeleton.</returns>
    public BonePose Pose(Matrix3f? rootRotation = null, Vector3f rootPosition = default)
    {
        Resolve();

        for (int bone = 0; bone < _locals.Length; bone++)
        {
            BoneClip? clip = _resolved[bone];
            _locals[bone] = clip is null || bone >= clip.Animation.BoneCount
                ? new Quaternion32(0f, 0f, 0f, 1f)
                : clip.SampleRotation(bone);
        }

        return BonePose.Evaluate(_set.Skeleton, _locals, rootPosition, rootRotation);
    }

    // Walks the bones in index order resolving each node's source, which is the original's recursion
    // written flat: it hands a child either its parent's resolved source or the one the parent was
    // itself handed, and the propagate flag picks between them. Valid in one pass for the same reason
    // BonePose is -- every retail skeleton lists a parent before its children.
    private void Resolve()
    {
        IReadOnlyList<int> parents = _set.Skeleton.Parents;

        for (int bone = 0; bone < _resolved.Length; bone++)
        {
            int parent = parents[bone];
            _inherited[bone] = parent == BoneSkeleton.NoParent
                ? null
                : _propagate[parent] ? _resolved[parent] : _inherited[parent];

            _resolved[bone] = _own[bone] ?? _inherited[bone];
        }
    }
}
