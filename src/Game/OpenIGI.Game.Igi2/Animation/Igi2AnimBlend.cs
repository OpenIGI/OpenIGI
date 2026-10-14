using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Animation;

/// <summary>A blend from one pose source to another, <c>AnimBlender_t</c>: its timing and the pose it mixes.</summary>
/// <remarks>
/// <para>
/// <c>AnimBlender_Blend</c> (<c>igi2 0x5D9DB0</c>) starts it from an old source to a new one over a length in clip ticks,
/// finished at once if that is nought; each run (<c>AnimBlender_RunHandler</c>, <c>0x5D9630</c>) adds a frame's 160 ticks
/// while it is short of its length, and on the run after it reaches it the blend is finished. A blender that drops its old
/// source (bit 1 of its flags) takes that source's clip away once it finishes.
/// </para>
/// <para>
/// Its pose (<c>AnimBlender_InterpolateHandler</c>, <c>0x5D9780</c>): with no clip on the new source, every masked bone is the
/// identity. Finished, the old source writes only the bones the new one does not animate and then the new one writes its own,
/// straight into the bones it was handed. Blending, both write into working buffers of their own and each masked bone is
/// slerped from the old to the new, or taken from whichever animates it alone; the root translation is mixed the same way.
/// </para>
/// </remarks>
public sealed class Igi2AnimBlend : IIgi2PoseSource
{
    /// <summary>The ticks a run adds, <c>AnimBlender_nTicksPerFrame</c>.</summary>
    public const int TicksPerFrame = 160;

    private Igi2BoneBuffer? _old;
    private Igi2BoneBuffer? _new;
    private bool[] _onlyMask = [];
    private bool[] _ownMask = [];

    /// <summary>Gets the ticks the blend has run, <c>nTime</c>.</summary>
    public int Elapsed { get; private set; }

    /// <summary>Gets the blend's length in ticks, <c>nTotalTime</c>.</summary>
    public int Length { get; private set; }

    /// <summary>Gets a value indicating whether the blend has finished, the blender's bit 4.</summary>
    public bool IsFinished { get; private set; } = true;

    /// <summary>Gets the source blended from, <c>ptSource0</c>.</summary>
    public IIgi2PoseSource? Source0 { get; private set; }

    /// <summary>Gets the source blended to, <c>ptSource1</c>.</summary>
    public IIgi2PoseSource? Source1 { get; private set; }

    /// <summary>Gets or sets a value indicating whether a finished blend takes its old source's clip away, bit 1 of its flags.</summary>
    public bool DropsOldSource { get; set; }

    /// <inheritdoc/>
    public bool[] Mask { get; set; } = Igi2AnimController.EveryBone;

    /// <inheritdoc/>
    /// <remarks>Nothing in a body's blend graph ever marks a blender so.</remarks>
    public bool IsNoClip => false;

    /// <summary>Sets the sources a blend is made between when it is made (<c>AnimBlender_CreateHandler</c>, <c>igi2 0x5D96E0</c>): at once.</summary>
    /// <param name="source0">The source blended from.</param>
    /// <param name="source1">The source blended to.</param>
    public void Create(IIgi2PoseSource source0, IIgi2PoseSource source1)
    {
        Source0 = source0 ?? throw new ArgumentNullException(nameof(source0));
        Source1 = source1 ?? throw new ArgumentNullException(nameof(source1));
        Elapsed = 0;
        Length = 0;
        IsFinished = true;
    }

    /// <summary>Starts the blend from one source to another over a length, <c>AnimBlender_Blend</c> (<c>igi2 0x5D9DB0</c>).</summary>
    /// <param name="length">The length in ticks.</param>
    /// <param name="to">The source blended to.</param>
    /// <param name="from">The source blended from.</param>
    public void Blend(int length, IIgi2PoseSource to, IIgi2PoseSource from)
    {
        Source0 = from ?? throw new ArgumentNullException(nameof(from));
        Source1 = to ?? throw new ArgumentNullException(nameof(to));
        Elapsed = 0;
        Length = length;
        IsFinished = length == 0;
        if (IsFinished)
        {
            DropOld();
        }
    }

    /// <summary>Runs the blend one tick, <c>AnimBlender_RunHandler</c> (<c>igi2 0x5D9630</c>).</summary>
    public void Run()
    {
        if (Elapsed < Length)
        {
            Elapsed += TicksPerFrame;
        }
        else
        {
            IsFinished = true;
            DropOld();
        }
    }

    /// <inheritdoc/>
    public void Interpolate(Igi2BoneBuffer bones, bool[] mask)
    {
        ArgumentNullException.ThrowIfNull(bones);
        ArgumentNullException.ThrowIfNull(mask);
        if (Source0 is not { } from || Source1 is not { } to)
        {
            return;
        }

        int count = Math.Min(bones.BoneCount, mask.Length);
        if (to.IsNoClip)
        {
            for (int bone = 0; bone < count; bone++)
            {
                if (mask[bone])
                {
                    bones.Orientations[bone] = Quaternion32.Identity;
                    bones.Ops[bone] = 0;
                }
            }

            return;
        }

        Vector3f root = bones.Root;
        if (Elapsed >= Length || from.IsNoClip)
        {
            Vector3f fromRoot = root, toRoot = root;
            if (!ReferenceEquals(from.Mask, to.Mask) && !from.IsNoClip)
            {
                if (_onlyMask.Length != count) { _onlyMask = new bool[count]; }
                bool[] only = _onlyMask;
                bool any = false;
                for (int bone = 0; bone < count; bone++)
                {
                    only[bone] = At(from.Mask, bone) && !At(to.Mask, bone) && mask[bone];
                    any |= only[bone];
                }

                if (any)
                {
                    bones.Root = fromRoot;
                    from.Interpolate(bones, only);
                    fromRoot = bones.Root;
                }
            }

            if (_ownMask.Length != count) { _ownMask = new bool[count]; }
            bool[] own = _ownMask;
            for (int bone = 0; bone < count; bone++)
            {
                own[bone] = At(to.Mask, bone) && mask[bone];
            }

            bones.Root = toRoot;
            to.Interpolate(bones, own);
            toRoot = bones.Root;
            bones.Root = count > 0 && mask[0] ? (At(to.Mask, 0) ? toRoot : fromRoot) : root;
            return;
        }

        Fpu ratio = (Fpu)(double)Elapsed / Length;
        float t = ratio.Value < 0.0 ? 0f : ratio.Value > 1.0 ? 1f : ratio.F;
        _old = Buffer(_old, bones.BoneCount);
        _new = Buffer(_new, bones.BoneCount);
        _old.Root = root;
        _new.Root = root;
        from.Interpolate(_old, from.Mask);
        to.Interpolate(_new, to.Mask);
        if (count > 0 && mask[0])
        {
            Vector3f r0 = _old.Root, r1 = _new.Root;
            bones.Root = !At(from.Mask, 0) ? r1
                : !At(to.Mask, 0) ? r0
                : new Vector3f(
                    ((((Fpu)r1.X - r0.X) * t) + r0.X).F,
                    ((((Fpu)r1.Y - r0.Y) * t) + r0.Y).F,
                    ((((Fpu)r1.Z - r0.Z) * t) + r0.Z).F);
        }

        for (int bone = 0; bone < count; bone++)
        {
            if (!mask[bone])
            {
                continue;
            }

            if (!At(to.Mask, bone))
            {
                bones.Orientations[bone] = _old.Orientations[bone];
                bones.Ops[bone] = _old.Ops[bone];
            }
            else if (!At(from.Mask, bone))
            {
                bones.Orientations[bone] = _new.Orientations[bone];
                bones.Ops[bone] = _new.Ops[bone];
            }
            else
            {
                bones.Orientations[bone] = Igi2Pose.Slerp(_old.Orientations[bone], _new.Orientations[bone], t);
            }
        }
    }

    private static bool At(bool[] mask, int bone) => (uint)bone < (uint)mask.Length && mask[bone];

    private static Igi2BoneBuffer Buffer(Igi2BoneBuffer? buffer, int count) =>
        buffer is not null && buffer.BoneCount == count ? buffer : new Igi2BoneBuffer(count);

    /// <summary>The old source's clip taken away, if the blender drops it and it is a controller.</summary>
    private void DropOld()
    {
        if (DropsOldSource && Source0 is Igi2AnimController controller)
        {
            controller.DropClip();
        }
    }
}
