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
/// <b>Blending.</b> The original wraps a started clip in a blend node
/// (<c>{ i32 type, void *child }</c> slots, <c>-1</c> empty / <c>0</c> blend / <c>1</c> clip) and
/// cross-fades two branches with a weight from a callback on the node (<c>0x4D5EC0</c>, <c>0x4D5FF0</c>).
/// <c>0x4D61D0</c> takes the immediate path — replacing the source outright — only when the blend time is
/// under a tick; otherwise it eases from the old source to the new over that time. <see cref="Play"/>
/// reproduces both: with no blend time (the default) it replaces, and with one it ramps a weight
/// <c>0 → 1</c> over <c>blendSeconds × 4800 / 160</c> ticks and slerps the outgoing and incoming poses at
/// that weight. The blend node's full recursive tree is flattened here to one outgoing branch per bone,
/// which is all a whole-rig cross-fade (a clip on bone 0 driving every bone) needs.
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

    // The cross-fade half of 0x4D61D0. When a clip is started with a blend time of a tick or more, the
    // node it replaced becomes the outgoing branch of a blend node whose weight ramps 0 -> 1 over that
    // time; the pose is the slerp of the two branches at the ramp's current value. These arrays are the
    // flattened form of that blend node: the outgoing clip, how far the ramp has run, and its length in
    // ticks (zero when no blend is live). The *_Resolved/_Inherited pairs carry the outgoing branch and
    // weight down the skeleton exactly as _resolved/_inherited carry the live source.
    private readonly BoneClip?[] _blendFrom;
    private readonly float[] _blendElapsed;
    private readonly float[] _blendDuration;
    private readonly BoneClip?[] _blendFromResolved;
    private readonly BoneClip?[] _blendFromInherited;
    private readonly float[] _blendWeightResolved;
    private readonly float[] _blendWeightInherited;

    // The orientation source a bone's animated rotation is composed under. IGI1 has exactly one of
    // these -- sub_4D6130 builds a node combining a clip with an orientation, and its only caller is the
    // human's upper body (sub_489ED0, bone 1, type 5) -- so the mechanism is retail's even where the
    // value driven through it is not. Null everywhere until something sets one, which is the common case
    // and costs one null test per bone.
    private Quaternion32?[]? _bends;

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
        _blendFrom = new BoneClip?[bones];
        _blendElapsed = new float[bones];
        _blendDuration = new float[bones];
        _blendFromResolved = new BoneClip?[bones];
        _blendFromInherited = new BoneClip?[bones];
        _blendWeightResolved = new float[bones];
        _blendWeightInherited = new float[bones];
    }

    /// <summary>Gets the animation set backing this object.</summary>
    public BoneAnimationSet Set => _set;

    /// <summary>Gets the skeleton being posed.</summary>
    public BoneSkeleton Skeleton => _set.Skeleton;

    /// <summary>Gets the number of bones.</summary>
    public int BoneCount => _own.Length;

    /// <summary>Composes an extra rotation above a bone's own animated orientation.</summary>
    /// <param name="bone">The bone to bend.</param>
    /// <param name="rotation">
    /// The rotation, expressed in the <b>parent's</b> frame, or <see langword="null"/> to remove one.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bone"/> is not a bone.</exception>
    /// <remarks>
    /// <para>
    /// The engine's own orientation source, generalised. <c>sub_4D6130</c> attaches a bone to a node
    /// that combines a clip with a stored orientation, and <c>sub_4D5820</c>'s type 5 resolves that node
    /// as <c>stored ⊗ sampled</c> — the stored rotation on the left, which is what puts it in the
    /// parent's frame rather than the bone's own. This composes on the same side for the same reason:
    /// bending a spine means turning it where it joins the pelvis, not where it joins its own child.
    /// </para>
    /// <para>
    /// A bend moves the bone's <b>descendants</b> and not the bone itself, because a bone is placed by
    /// its parent's rotation (<see cref="BonePose.Evaluate"/>) — which is exactly what a joint does.
    /// </para>
    /// </remarks>
    public void Bend(int bone, Quaternion32? rotation)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bone);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(bone, _own.Length);

        if (_bends is null)
        {
            if (rotation is null)
            {
                return;
            }

            _bends = new Quaternion32?[_own.Length];
        }

        _bends[bone] = rotation;
    }

    /// <summary>Gets the rotation composed above a bone's animated orientation, if any.</summary>
    /// <param name="bone">The bone.</param>
    /// <returns>The bend, or <see langword="null"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bone"/> is not a bone.</exception>
    public Quaternion32? BendOf(int bone)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bone);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(bone, _own.Length);
        return _bends?[bone];
    }

    /// <summary>Starts a clip on a bone, replacing whatever that bone was sourced from.</summary>
    /// <param name="bone">The bone to attach the source to; <c>0</c> drives the whole skeleton.</param>
    /// <param name="animationId">The animation id, as the set's sparse table addresses it.</param>
    /// <param name="loops">Whether the clip wraps at its duration.</param>
    /// <param name="backwards">Whether the clip plays from its duration down to zero.</param>
    /// <param name="blendSeconds">
    /// How long to cross-fade from whatever was playing on the bone into the new clip. Zero (the default)
    /// replaces the source outright; a value that works out to a tick or more eases into it. This is
    /// <c>0x4D61D0</c>'s <c>a5</c>, converted to ticks with the same <c>× 4800 / 160</c> the original uses.
    /// </param>
    /// <returns>The clip, or <see langword="null"/> when the set does not define that id.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bone"/> is not a bone.</exception>
    /// <remarks>
    /// Returning <see langword="null"/> rather than throwing matches the original, which leaves undefined
    /// ids as empty slots in a sparse table and reports <c>"Animation #%d not found"</c> without
    /// stopping. Setting the source also raises the node's propagate flag, exactly as <c>0x4D61D0</c>
    /// does with <c>node[1] |= 0x20000</c>.
    /// </remarks>
    public BoneClip? Play(int bone, int animationId, bool loops = false, bool backwards = false, float blendSeconds = 0f)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bone);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(bone, _own.Length);

        if (!_set.TryGet(animationId, out BoneAnimation animation))
        {
            return null;
        }

        BoneClip clip = new(animation, bone, loops, backwards);

        // 0x4D61D0: v12 = a5 * 4800 / flt_A54658 (160). A blend under one tick is the immediate path,
        // and a bone with nothing already playing has no branch to fade from.
        float blendTicks = blendSeconds * (4800f / BoneAnimation.FrameStep);
        if (_own[bone] is { } outgoing && blendTicks >= 1f)
        {
            _blendFrom[bone] = outgoing;
            _blendElapsed[bone] = 0f;
            _blendDuration[bone] = blendTicks;
        }
        else
        {
            _blendFrom[bone] = null;
            _blendElapsed[bone] = 0f;
            _blendDuration[bone] = 0f;
        }

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
        _blendFrom[bone] = null;
        _blendElapsed[bone] = 0f;
        _blendDuration[bone] = 0f;
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
    public Vector3f SampleRootMotion()
    {
        if (_own[0] is not { } to)
        {
            return default;
        }

        Vector3f incoming = to.SampleRoot();
        if (_blendDuration[0] <= 0f || _blendFrom[0] is not { } from)
        {
            return incoming;
        }

        // Blend the hold offset alongside the pose so the weapon does not jump to its new resting spot
        // on the tick the source swaps; the position eases over the same weight as the rotations.
        float weight = _blendElapsed[0] / _blendDuration[0];
        Vector3f outgoing = from.SampleRoot();
        return new Vector3f(
            outgoing.X + ((incoming.X - outgoing.X) * weight),
            outgoing.Y + ((incoming.Y - outgoing.Y) * weight),
            outgoing.Z + ((incoming.Z - outgoing.Z) * weight));
    }

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
            if (clip is not null)
            {
                float before = clip.Time;
                clip.Tick();
                ChangedThisTick |= before != clip.Time;
            }

            if (_blendDuration[bone] <= 0f)
            {
                continue;
            }

            // The blend node advances both children; the outgoing branch keeps sampling live, which
            // matters when it is still moving (a walk fading into a stand). A cross-fade in progress
            // changes the pose every tick even if neither clip's clock moved, so it must re-pose.
            _blendFrom[bone]?.Tick();
            _blendElapsed[bone] += 1f;
            ChangedThisTick = true;
            if (_blendElapsed[bone] >= _blendDuration[bone])
            {
                _blendFrom[bone] = null;
                _blendElapsed[bone] = 0f;
                _blendDuration[bone] = 0f;
            }
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
            Quaternion32 incoming = clip is null || bone >= clip.Animation.BoneCount
                ? new Quaternion32(0f, 0f, 0f, 1f)
                : clip.SampleRotation(bone);

            // While a cross-fade is live on the source reaching this bone, the local orientation is the
            // slerp of the outgoing branch and the incoming clip at the ramp's current weight (0 = fully
            // outgoing, 1 = fully incoming). 0x4D5EC0's blend node, flattened to one branch per bone.
            BoneClip? from = _blendFromResolved[bone];
            float weight = _blendWeightResolved[bone];
            Quaternion32 local = from is not null && weight < 1f && bone < from.Animation.BoneCount
                ? BoneRotationSampler.Slerp(from.SampleRotation(bone), incoming, weight)
                : incoming;

            // The bend goes on the LEFT, after the cross-fade rather than inside it: it is not a pose
            // being blended between but a frame the blended pose is expressed in.
            _locals[bone] = _bends?[bone] is { } bend ? bend * local : local;
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
            if (parent == BoneSkeleton.NoParent)
            {
                _inherited[bone] = null;
                _blendFromInherited[bone] = null;
                _blendWeightInherited[bone] = 1f;
            }
            else if (_propagate[parent])
            {
                _inherited[bone] = _resolved[parent];
                _blendFromInherited[bone] = _blendFromResolved[parent];
                _blendWeightInherited[bone] = _blendWeightResolved[parent];
            }
            else
            {
                _inherited[bone] = _inherited[parent];
                _blendFromInherited[bone] = _blendFromInherited[parent];
                _blendWeightInherited[bone] = _blendWeightInherited[parent];
            }

            if (_own[bone] is not null)
            {
                _resolved[bone] = _own[bone];

                // A blend lives on the same bone as the source it replaced, so it rides the propagate
                // flag down the skeleton exactly as the source does.
                bool blending = _blendDuration[bone] > 0f;
                _blendFromResolved[bone] = blending ? _blendFrom[bone] : null;
                _blendWeightResolved[bone] = blending ? _blendElapsed[bone] / _blendDuration[bone] : 1f;
            }
            else
            {
                _resolved[bone] = _inherited[bone];
                _blendFromResolved[bone] = _blendFromInherited[bone];
                _blendWeightResolved[bone] = _blendWeightInherited[bone];
            }
        }
    }
}
