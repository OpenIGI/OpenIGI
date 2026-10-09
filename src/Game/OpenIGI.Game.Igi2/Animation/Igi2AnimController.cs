using OpenIGI.Formats.Animation;

namespace OpenIGI.Game.Igi2.Animation;

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

    /// <summary>No clip is set.</summary>
    NoClip = 0x10,

    /// <summary>The clip's events are not triggered.</summary>
    SuppressTriggers = 0x20,

    /// <summary>Set on a dying soldier's main clip by its death entries (<c>HumanSoldier_InitStateDie</c>, <c>igi2 0x4E8924</c>).</summary>
    Dying = 0x40,
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
public sealed class Igi2AnimController
{
    /// <summary>The ticks a frame advances a clip by, <c>AnimController_nTicksPerFrame</c>.</summary>
    public const int TicksPerFrame = 160;

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
