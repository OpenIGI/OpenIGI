using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Animation;

/// <summary>
/// What a blender takes a pose from, <c>AnimControllerBase_t</c>: a controller or another blender, each answering the
/// interpolate event (<c>BoneDynCubeObj_eInterpolateQTaskEvent</c>) into the bones a blender hands it.
/// </summary>
public interface IIgi2PoseSource
{
    /// <summary>Gets the bones it animates, <c>pisAnimated</c>; sources that share one share the array.</summary>
    bool[] Mask { get; }

    /// <summary>Gets a value indicating whether it has no clip, bit <c>0x10</c> of its flags.</summary>
    bool IsNoClip { get; }

    /// <summary>Writes its pose into some bones.</summary>
    /// <param name="bones">The bones, and the root translation it writes.</param>
    /// <param name="mask">The bones to write, in place of its own while a blender narrows it.</param>
    void Interpolate(Igi2BoneBuffer bones, bool[] mask);
}

/// <summary>The state bits of a controller, <c>AnimControllerBase_t.nFlags</c>.</summary>
[Flags]
public enum Igi2AnimControllerBits
{
    /// <summary>No bits.</summary>
    None = 0,

    /// <summary>The clip wraps at its end, set from the clip's own loop flag.</summary>
    Loop = 0x1,

    /// <summary>
    /// The controller steps itself each tick, <c>AnimController_SetAutoStep</c> (<c>igi2 0x554960</c>): controllers are
    /// tasks, and <c>AnimController_RunHandler</c> (<c>0x5540D0</c>) steps one with this bit by its time step. A new
    /// controller has it; a human's active main controller has it cleared, because the human steps that one itself.
    /// </summary>
    AutoStep = 0x2,

    /// <summary>The time has reached or passed an end of the clip.</summary>
    Ended = 0x4,

    /// <summary>Rotations are SQUAD-interpolated through their keys' tangents (<c>AnimController_InterpolateHandler</c>, <c>igi2 0x5541A1</c>).</summary>
    Spline = 0x8,

    /// <summary>No clip is set.</summary>
    NoClip = 0x10,

    /// <summary>The clip's events are not triggered.</summary>
    SuppressTriggers = 0x20,

    /// <summary>
    /// The clip's root translation is held at nought (<c>AnimController_InterpolateHandler</c>, <c>igi2 0x55410E</c>): set on a
    /// dying soldier's main clip by its death entries (<c>HumanSoldier_InitStateDie</c>, <c>0x4E8924</c>).
    /// </summary>
    NoRootTranslation = 0x40,
}

/// <summary>What a controller's clip fires its anim triggers on: the object the clip plays on.</summary>
public interface IIgi2AnimTriggerTarget
{
    /// <summary>A trigger the clip played through, <c>AnimTrigger_Trigger</c> (<c>igi2 0x433D50</c>).</summary>
    /// <param name="trigger">The trigger.</param>
    void Trigger(in Igi2AnimationTrigger trigger);
}

/// <summary>
/// One clip being played: its time, its step a tick and whether it has ended, <c>AnimController_t</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>AnimController_SetAnimationAndTime</c> (<c>igi2 0x554850</c>) sets a clip, takes the loop flag from it, resets
/// the step to one frame's worth of ticks, and moves to the time asked for, wrapped into the clip one tick beyond
/// its end; the ended bit is cleared afterwards, even when that time is the end. <c>AnimController_Step</c>
/// (<c>0x5549A0</c>) moves to a time: one at or beyond the end, or before the start, sets the ended bit and then
/// wraps for a looping clip or stops at that end for any other. The ended bit stays set once a looping clip has
/// wrapped, until a clip is set again.
/// </para>
/// <para>
/// A step fires the clip's anim triggers it plays through (<c>AnimController_TriggerEvents</c>, <c>0x553D50</c>), on its
/// <see cref="Target"/>: forward, those after the old time up to the new; backward, those after the new time up to the old,
/// still in time order. A looping clip that wraps forward first fires the rest of the clip and then the start up to the new
/// time; wrapping backward, the start up to the old time and then the rest after the new. A clip set afresh fires the
/// triggers at the tick it is set to and the one before (<c>AnimController_Seek</c>, <c>0x553F60</c>, fires none and leaves
/// the time one before). A controller with <see cref="Igi2AnimControllerBits.SuppressTriggers"/> fires none.
/// </para>
/// <para>
/// The clip's other events, its attach objects and the interpolated pose are not run here. A trigger placed on a bone fires
/// only for a bone the controller animates; this takes every bone of the clip as animated, which holds for a controller
/// playing the whole body.
/// </para>
/// </remarks>
public sealed class Igi2AnimController : IIgi2PoseSource
{
    /// <summary>The ticks a frame advances a clip by, <c>AnimController_nTicksPerFrame</c>.</summary>
    public const int TicksPerFrame = 160;

    /// <summary>The mask a controller animates by unless given one, <c>AnimController_aisAnimated</c>: every bone (<c>AnimController_Open</c>, <c>igi2 0x5546E0</c>).</summary>
    public static bool[] EveryBone { get; } = [.. Enumerable.Repeat(true, 256)];

    /// <inheritdoc/>
    public bool[] Mask { get; set; } = EveryBone;

    /// <inheritdoc/>
    public bool IsNoClip => (Flags & Igi2AnimControllerBits.NoClip) != 0;

    /// <summary>
    /// Takes the clip away as a blend that finished from this controller does (<c>AnimBlender_Blend</c>, <c>igi2 0x5D9DE6</c>, and
    /// <c>AnimBlender_RunHandler</c>, <c>0x5D9653</c>): no clip, and the bit that says so.
    /// </summary>
    public void DropClip()
    {
        Flags |= Igi2AnimControllerBits.NoClip;
        Clip = null;
    }

    /// <summary>
    /// The clip's pose at the controller's time, <c>AnimController_InterpolateHandler</c> (<c>igi2 0x5540F0</c>): the root translation
    /// linear between its keys (nought while <see cref="Igi2AnimControllerBits.NoRootTranslation"/> is set), and each masked bone's
    /// orientation slerped from its active key toward the next, or through the tangents when the controller is a spline one.
    /// </summary>
    /// <param name="bones">The bones to write.</param>
    /// <param name="mask">The bones to write.</param>
    /// <remarks>
    /// A key with no next is copied, its operation count with it (nought in every clip read); a slerp leaves the count as the
    /// bones held it. The parameter is the time since the key over the segment, divided at 24 bits and stored.
    /// </remarks>
    public void Interpolate(Igi2BoneBuffer bones, bool[] mask)
    {
        ArgumentNullException.ThrowIfNull(bones);
        ArgumentNullException.ThrowIfNull(mask);
        if (Clip is not { } clip)
        {
            return;
        }

        if ((Flags & Igi2AnimControllerBits.NoRootTranslation) != 0)
        {
            bones.Root = default;
        }
        else if (Active(clip.Translation, static k => k.Time) is int key)
        {
            Igi2TranslationKey from = clip.Translation[key];
            if (key + 1 < clip.Translation.Count)
            {
                Igi2TranslationKey to = clip.Translation[key + 1];
                Fpu t = (Fpu)(double)(Time - from.Time) / (to.Time - from.Time);
                bones.Root = new Vector3f(
                    ((((Fpu)to.Translation.X - from.Translation.X) * t) + from.Translation.X).F,
                    ((((Fpu)to.Translation.Y - from.Translation.Y) * t) + from.Translation.Y).F,
                    ((((Fpu)to.Translation.Z - from.Translation.Z) * t) + from.Translation.Z).F);
            }
            else
            {
                bones.Root = from.Translation;
            }
        }

        int count = Math.Min(bones.BoneCount, clip.BoneCount);
        bool spline = (Flags & Igi2AnimControllerBits.Spline) != 0;
        for (int bone = 0; bone < count && bone < mask.Length; bone++)
        {
            if (!mask[bone])
            {
                continue;
            }

            IReadOnlyList<Igi2RotationKey> track = clip.Rotations(bone);
            if (Active(track, static k => k.Time) is not int key)
            {
                continue;
            }

            Igi2RotationKey from = track[key];
            if (key + 1 >= track.Count)
            {
                bones.Orientations[bone] = from.Rotation;
                bones.Ops[bone] = 0;
                continue;
            }

            Igi2RotationKey to = track[key + 1];
            float t = ((Fpu)(double)(Time - from.Time) / (to.Time - from.Time)).F;
            Quaternion32 rotation = Igi2Pose.Slerp(from.Rotation, to.Rotation, t);
            if (spline)
            {
                Quaternion32 tangent = Igi2Pose.Slerp(from.OutgoingTangent, to.IncomingTangent, t);
                Fpu share = ((Fpu)1f - t) * t;
                rotation = Igi2Pose.Slerp(rotation, tangent, (share + share).F);
            }

            bones.Orientations[bone] = rotation;
        }
    }

    /// <summary>
    /// A bone's active rotation key as it stands, unblended, <c>pptActiveRotationKeys[bone]</c>: the last the controller has reached.
    /// </summary>
    /// <param name="bone">The bone.</param>
    /// <returns>The key's orientation, or <see langword="null"/> with no clip or key.</returns>
    public Quaternion32? ActiveRotation(int bone) => ActiveRotationKey(bone)?.Rotation;

    /// <summary>A bone's active rotation key whole, with its stored operation count.</summary>
    /// <param name="bone">The bone.</param>
    /// <returns>The key, or <see langword="null"/> with no clip or key.</returns>
    public Igi2RotationKey? ActiveRotationKey(int bone)
    {
        if (Clip is not { } clip || (uint)bone >= (uint)clip.BoneCount)
        {
            return null;
        }

        IReadOnlyList<Igi2RotationKey> track = clip.Rotations(bone);
        return Active(track, static k => k.Time) is int key ? track[key] : null;
    }

    /// <summary>The active key: the last whose time the controller has reached.</summary>
    private int? Active<TKey>(IReadOnlyList<TKey> track, Func<TKey, int> timeOf)
    {
        int? key = null;
        for (int i = 0; i < track.Count && timeOf(track[i]) <= Time; i++)
        {
            key = i;
        }

        return key;
    }

    /// <summary>Gets the clip, or <see langword="null"/>.</summary>
    public Igi2AnimationClip? Clip { get; private set; }

    /// <summary>Gets the controller's time, <c>nTime</c>, in clip ticks.</summary>
    public int Time { get; private set; }

    /// <summary>Gets or sets how far a step advances the time, <c>nTimeStep</c>.</summary>
    public int TimeStep { get; set; }

    /// <summary>Gets or sets the controller's bits.</summary>
    public Igi2AnimControllerBits Flags { get; set; }

    /// <summary>Gets or sets what the clip's anim triggers fire on, the object the clip plays on; <see langword="null"/> fires none.</summary>
    public IIgi2AnimTriggerTarget? Target { get; set; }

    /// <summary>Steps the controller by its time step when it steps itself (<c>AnimController_RunHandler</c>, <c>igi2 0x5540D0</c>).</summary>
    public void AutoStep()
    {
        if ((Flags & Igi2AnimControllerBits.AutoStep) != 0)
        {
            Step(Time + TimeStep);
        }
    }

    /// <summary>Gets a value indicating whether the time has reached an end of the clip.</summary>
    public bool IsEnded => (Flags & Igi2AnimControllerBits.Ended) != 0;

    /// <summary>Sets a clip and the time it plays from.</summary>
    /// <param name="clip">The clip; <see langword="null"/> clears it and leaves the time as it was.</param>
    /// <param name="time">The time, wrapped into the clip one tick beyond its end.</param>
    public void SetAnimationAndTime(Igi2AnimationClip? clip, int time)
    {
        Flags |= Igi2AnimControllerBits.NoClip;
        Clip = null;
        if (clip is null)
        {
            return;
        }

        Clip = clip;
        Flags = clip.Loops ? Flags | Igi2AnimControllerBits.Loop : Flags & ~Igi2AnimControllerBits.Loop;
        Flags &= ~Igi2AnimControllerBits.NoClip;
        TimeStep = TicksPerFrame;

        int start = time % (clip.TotalTime + 1);

        // AnimController_Seek (igi2 0x553F60) leaves the time one before the tick it seeks to.
        Time = start - 2;
        Step(start);
        Flags &= ~Igi2AnimControllerBits.Ended;
    }

    /// <summary>Moves the clip to a time, setting the ended bit at or past either end and wrapping a looping clip.</summary>
    /// <param name="time">The time.</param>
    public void Step(int time)
    {
        if (Clip is not { } clip)
        {
            return;
        }

        int total = clip.TotalTime;
        bool loops = (Flags & Igi2AnimControllerBits.Loop) != 0;
        int old = Time;
        (int After, int UpTo)? wrapped = null;
        if (time >= 0)
        {
            if (time >= total)
            {
                Flags |= Igi2AnimControllerBits.Ended;
                if (loops)
                {
                    wrapped = (old, int.MaxValue);
                    old = -1;
                    time %= total;
                }
                else
                {
                    time = total;
                }
            }
        }
        else
        {
            Flags |= Igi2AnimControllerBits.Ended;
            if (loops)
            {
                wrapped = (int.MinValue, old);
                old = total + 1;
                time = total + (time % total);
            }
            else
            {
                time = 0;
            }
        }

        if (time != old && (Flags & Igi2AnimControllerBits.SuppressTriggers) == 0 && Target is { } target)
        {
            if (wrapped is { } first)
            {
                Fire(clip, first.After, first.UpTo, target);
            }

            Fire(clip, time > old ? old : time, time > old ? time : old, target);
        }

        Time = time;
    }

    /// <summary>Fires the clip's triggers after one time and up to another, in time order.</summary>
    private static void Fire(Igi2AnimationClip clip, int after, int upTo, IIgi2AnimTriggerTarget target)
    {
        foreach (Igi2AnimationTrigger trigger in clip.Triggers)
        {
            if (trigger.Time > after && trigger.Time <= upTo)
            {
                target.Trigger(trigger);
            }
        }
    }
}
