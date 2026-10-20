using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>
/// One door's state machine — the <c>Door</c> task's update (<c>0x446D10</c>), transcribed.
/// </summary>
/// <remarks>
/// <para>
/// A door carries an <b>open-command latch</b> (<c>+656</c>, toggled by a use — <c>sub_447DA0</c> is a
/// plain XOR) and two motion channels driven toward the latch's targets every tick: the <b>swing</b>
/// (current angle <c>+396</c> toward <c>Max angle</c> <c>+320</c> or zero, stepping
/// <c>|maxAngle| / (openTime × 30)</c> degrees a tick) and the <b>slide</b> (fraction <c>+312</c> toward
/// one or zero, stepping <c>(L / (openTime × 30)) / L</c>, L the slide's length). When both channels sit at
/// their targets the door is <b>fully closed</b> (<c>+410</c>, targets zero) or <b>fully open</b>
/// (<c>+409</c>); while either moves, both flags are clear.
/// </para>
/// <para>
/// The use state the interactions read (<c>sub_447D70</c>): <b>0</b> fully open, <b>1</b> fully
/// closed, <b>2</b> moving with the latch set (opening), <b>3</b> moving with it clear (closing). The
/// AI's walker opens on 1 <i>and</i> 3 — a closing door is re-opened — and waits on 2
/// (<c>"Waiting for door to open"</c>).
/// </para>
/// <para>
/// The registry evaluates the task's authored conditions around this state machine: the open
/// expression only while fully closed, the close expression only while fully open, and the locked
/// expression every tick. The ticks-open counter (<c>+664</c>) is exposed for those conditions.
/// The mid-swing doorway ray that re-opens a closing door off a human standing in it is driven by
/// <see cref="DoorRegistry.SetDoorwayBlockedQuery"/>; the three sounds are recorded in
/// the original.
/// </para>
/// </remarks>
public sealed class DoorState
{
    /// <summary>The ticks per second the steps divide by (<c>flt_5333BC</c>).</summary>
    private const float TicksPerSecond = 30.0f;

    private readonly float _maxAngleDegrees;
    private readonly float _openTimeSeconds;
    private readonly bool _slides;
    private readonly float _slideLength;

    private float _angleDegrees;
    private float _slide;
    private bool _latch;

    /// <summary>Initializes a new instance of the <see cref="DoorState"/> class, fully closed.</summary>
    /// <param name="maxAngleDegrees">The task's <c>Max angle</c> (<c>+320</c>) — zero for a purely sliding door.</param>
    /// <param name="openTimeSeconds">The task's <c>Open time</c> (<c>+324</c>), in seconds.</param>
    /// <param name="slides">Whether the task declares a slide (<c>Position stop X/Y</c> non-zero).</param>
    /// <param name="initialSlide">The task's <c>Position slider</c> (<c>+312</c>): the slide fraction it starts at.</param>
    /// <param name="slideLength">
    /// The slide's length (<c>+404</c>, <c>|√(stopX² + stopY²)|</c>, set by the post-load <c>0x447830</c>), which the
    /// slide step multiplies and divides by.
    /// </param>
    public DoorState(
        float maxAngleDegrees,
        float openTimeSeconds,
        bool slides = false,
        float initialSlide = 0f,
        float slideLength = 1f)
    {
        _maxAngleDegrees = maxAngleDegrees;
        _openTimeSeconds = openTimeSeconds;
        _slides = slides;
        _slide = initialSlide;
        _slideLength = slideLength;
        PreviousSlideFraction = initialSlide;
        IsFullyClosed = true;
    }

    /// <summary>Gets the swing's current angle, in degrees — <c>+396</c>.</summary>
    public float AngleDegrees => _angleDegrees;

    /// <summary>Gets the swing angle at the end of the previous simulation tick.</summary>
    public float PreviousAngleDegrees { get; private set; }

    /// <summary>Gets the slide's current fraction — <c>+312</c>.</summary>
    public float SlideFraction => _slide;

    /// <summary>Gets the slide fraction at the end of the previous simulation tick.</summary>
    public float PreviousSlideFraction { get; private set; }

    /// <summary>Gets whether the last tick stepped the swing: the original rebuilt the orientation then.</summary>
    public bool SwungThisTick { get; private set; }

    /// <summary>Gets whether the last tick stepped the slide: the original re-placed the door then.</summary>
    public bool SlidThisTick { get; private set; }

    /// <summary>Gets whether the last tick changed the door's collision/draw transform.</summary>
    public bool TransformChangedThisTick => SwungThisTick || SlidThisTick;

    /// <summary>Gets whether the door sits fully open — <c>+409</c>.</summary>
    public bool IsFullyOpen { get; private set; }

    /// <summary>Gets whether the door sits fully closed — <c>+410</c>.</summary>
    public bool IsFullyClosed { get; private set; }

    /// <summary>Whether the door was fully open at the end of the previous tick.</summary>
    public bool WasFullyOpen { get; private set; }

    /// <summary>Whether the door was fully closed at the end of the previous tick.</summary>
    public bool WasFullyClosed { get; private set; } = true;

    /// <summary>Gets how many ticks the door has been fully open — <c>+664</c>.</summary>
    /// <remarks>
    /// Counted as the door's update starts, before it moves (<see cref="RunConditions"/>): the update a door ends fully open on
    /// leaves it nought, and the next counts the first tick, so a Close expression reading it sees the ticks it has stood open.
    /// </remarks>
    public int TicksOpen { get; private set; }

    /// <summary>Gets the state a use reads — <c>sub_447D70</c>.</summary>
    /// <remarks>0 open, 1 closed, 2 opening, 3 closing.</remarks>
    public int UseState
    {
        get
        {
            if (IsFullyOpen)
            {
                return 0;
            }

            if (IsFullyClosed)
            {
                return 1;
            }

            return _latch ? 2 : 3;
        }
    }

    /// <summary>Toggles the open command — <c>sub_447DA0</c>'s XOR.</summary>
    public void Toggle() => _latch ^= true;

    /// <summary>Commands the door open, however it stands — what the AI's walker wants of it.</summary>
    /// <remarks>
    /// The walker calls the toggle only in states 1 and 3, where the latch is guaranteed clear, so
    /// toggling and setting are the same thing there; this is the same operation without the caller
    /// having to know the latch.
    /// </remarks>
    public void CommandOpen() => _latch = true;

    /// <summary>Commands the door closed, however it stands.</summary>
    public void CommandClosed() => _latch = false;

    /// <summary>Gets a value indicating whether the door has updated at least once.</summary>
    /// <remarks>
    /// Its open and closed flags (<c>+409</c>/<c>+410</c>) are written only at the end of its update (<c>0x446D10</c>);
    /// the constructor (<c>0x4468F0</c>) leaves them as the allocation found them, and the lock flag (<c>+408</c>) at
    /// nought. A door in a section the level has not streamed in yet reports none of them.
    /// </remarks>
    public bool HasTicked { get; private set; }

    /// <summary>
    /// The door's expressions, between its children and its motion in <c>0x446D10</c>: a door that stood fully closed opens
    /// when its Open expression holds (<c>0x446DF0</c>); one that stood fully open counts the tick, then shuts when its Close
    /// expression holds (<c>0x446E38</c>); any other's count is nought.
    /// </summary>
    /// <param name="open">The Open expression, or <see langword="null"/> for none.</param>
    /// <param name="close">The Close expression, or <see langword="null"/> for none.</param>
    /// <remarks>
    /// Open is asked only while closed and Close only while open, as they stood after the last update. Asking Close as the door
    /// opens would cancel a button-driven gate on its first tick of travel.
    /// </remarks>
    public void RunConditions(Func<bool>? open, Func<bool>? close)
    {
        if (IsFullyClosed && open?.Invoke() == true)
        {
            _latch = true;
        }

        if (IsFullyOpen)
        {
            TicksOpen++;
            if (close?.Invoke() == true)
            {
                _latch = false;
            }
        }
        else
        {
            TicksOpen = 0;
        }
    }

    /// <summary>Advances one 30 Hz tick — the channel-stepping half of <c>0x446D10</c>, with its x87 arithmetic.</summary>
    public void Tick()
    {
        HasTicked = true;
        PreviousAngleDegrees = _angleDegrees;
        PreviousSlideFraction = _slide;
        WasFullyOpen = IsFullyOpen;
        WasFullyClosed = IsFullyClosed;
        SwungThisTick = false;
        SlidThisTick = false;

        // The swing (+396) toward latch ? Max angle : 0. The stored float is the new angle; the clamp compares the
        // register's value, which fst leaves unrounded.
        float angleTarget = _latch ? _maxAngleDegrees : 0f;
        if (angleTarget != _angleDegrees)
        {
            X87 step = X87.Abs(_maxAngleDegrees) / ((X87)_openTimeSeconds * TicksPerSecond);
            if (_angleDegrees < angleTarget)
            {
                X87 next = step + _angleDegrees;
                _angleDegrees = next.F;
                if (next.Value > angleTarget)
                {
                    _angleDegrees = angleTarget;
                }
            }
            else if (_angleDegrees > angleTarget)
            {
                X87 next = (X87)_angleDegrees - step;
                _angleDegrees = next.F;
                if (next.Value < angleTarget)
                {
                    _angleDegrees = angleTarget;
                }
            }

            SwungThisTick = true;
        }

        // The slide (+312) toward latch ? 1 : 0. A door with no stop, or already at its target, takes the target
        // without moving.
        float slideTarget = _latch ? 1f : 0f;
        if (!_slides || _slide == slideTarget)
        {
            _slide = slideTarget;
        }
        else
        {
            X87 step = (X87)_slideLength / ((X87)_openTimeSeconds * TicksPerSecond) / _slideLength;
            if (_slide < slideTarget)
            {
                X87 next = step + _slide;
                _slide = next.F;
                if (next.Value > slideTarget)
                {
                    _slide = slideTarget;
                }
            }
            else if (_slide > slideTarget)
            {
                X87 next = (X87)_slide - step;
                _slide = next.F;
                if (next.Value < slideTarget)
                {
                    _slide = slideTarget;
                }
            }

            SlidThisTick = true;
        }

        // Fully open / fully closed are recomputed from "both channels at target": closed when the
        // targets are zero, open otherwise, and neither while anything still moves.
        bool atTargets = _angleDegrees == angleTarget && _slide == slideTarget;
        if (atTargets)
        {
            bool closed = angleTarget == 0f && slideTarget == 0f;
            IsFullyClosed = closed;
            IsFullyOpen = !closed;
        }
        else
        {
            IsFullyClosed = false;
            IsFullyOpen = false;
        }
    }
}
