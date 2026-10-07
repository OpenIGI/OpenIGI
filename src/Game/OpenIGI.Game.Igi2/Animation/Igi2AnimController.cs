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

    /// <summary>The controller is being handed over from, while a new clip is set on its partner.</summary>
    Handover = 0x2,

    /// <summary>The time has reached or passed an end of the clip.</summary>
    Ended = 0x4,

    /// <summary>No clip is set.</summary>
    NoClip = 0x10,

    /// <summary>The clip's events are not triggered.</summary>
    SuppressTriggers = 0x20,
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
/// The clip's events, attach objects and the interpolated pose are not run here; this is the time and the bits the
/// states read.
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
        if (time >= 0)
        {
            if (time >= total)
            {
                Flags |= Igi2AnimControllerBits.Ended;
                time = loops ? time % total : total;
            }
        }
        else
        {
            Flags |= Igi2AnimControllerBits.Ended;
            time = loops ? total + (time % total) : 0;
        }

        Time = time;
    }
}
