namespace OpenIGI.Game.World;

/// <summary>
/// One door's state machine — the <c>Door</c> task's update (<c>0x446D10</c>), transcribed.
/// </summary>
/// <remarks>
/// <para>
/// A door carries an <b>open-command latch</b> (<c>+656</c>, toggled by a use — <c>sub_447DA0</c> is a
/// plain XOR) and two motion channels driven toward the latch's targets every tick: the <b>swing</b>
/// (current angle <c>+396</c> toward <c>Max angle</c> <c>+320</c> or zero, stepping
/// <c>maxAngle / (openTime × 30)</c> degrees a tick) and the <b>slide</b> (fraction <c>+312</c> toward
/// one or zero, stepping <c>1 / (openTime × 30)</c>). When both channels sit at their targets the door
/// is <b>fully closed</b> (<c>+410</c>, targets zero) or <b>fully open</b> (<c>+409</c>); while either
/// moves, both flags are clear.
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
    private readonly float _maxAngleDegrees;
    private readonly float _openTimeSeconds;
    private readonly bool _slides;

    private float _angleDegrees;
    private float _slide;
    private bool _latch;

    /// <summary>Initializes a new instance of the <see cref="DoorState"/> class, fully closed.</summary>
    /// <param name="maxAngleDegrees">The task's <c>Max angle</c> — zero for a purely sliding door.</param>
    /// <param name="openTimeSeconds">The task's <c>Open time</c>, in seconds.</param>
    /// <param name="slides">Whether the task declares a slide (<c>Position stop X/Y</c> non-zero).</param>
    public DoorState(float maxAngleDegrees, float openTimeSeconds, bool slides = false)
    {
        _maxAngleDegrees = maxAngleDegrees;
        _openTimeSeconds = openTimeSeconds;
        _slides = slides;
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

    /// <summary>Gets whether the last tick changed the door's collision/draw transform.</summary>
    public bool TransformChangedThisTick =>
        _angleDegrees != PreviousAngleDegrees || _slide != PreviousSlideFraction;

    /// <summary>Gets whether the door sits fully open — <c>+409</c>.</summary>
    public bool IsFullyOpen { get; private set; }

    /// <summary>Gets whether the door sits fully closed — <c>+410</c>.</summary>
    public bool IsFullyClosed { get; private set; }

    /// <summary>Whether the door was fully open at the end of the previous tick.</summary>
    public bool WasFullyOpen { get; private set; }

    /// <summary>Whether the door was fully closed at the end of the previous tick.</summary>
    public bool WasFullyClosed { get; private set; } = true;

    /// <summary>Gets how many ticks the door has been fully open — <c>+664</c>.</summary>
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

    /// <summary>Advances one 30 Hz tick — the channel-stepping half of <c>0x446D10</c>.</summary>
    public void Tick()
    {
        PreviousAngleDegrees = _angleDegrees;
        PreviousSlideFraction = _slide;
        WasFullyOpen = IsFullyOpen;
        WasFullyClosed = IsFullyClosed;
        float ticks = _openTimeSeconds * 30f;

        // The swing: target = latch ? maxAngle : 0, stepped |maxAngle|/(time x 30) a tick and clamped
        // onto the target. A door with no angle has nothing to swing and sits at its target.
        float angleTarget = _latch ? _maxAngleDegrees : 0f;
        if (_angleDegrees != angleTarget && ticks > 0f && _maxAngleDegrees != 0f)
        {
            float step = Math.Abs(_maxAngleDegrees) / ticks;
            if (_angleDegrees < angleTarget)
            {
                _angleDegrees = Math.Min(_angleDegrees + step, angleTarget);
            }
            else
            {
                _angleDegrees = Math.Max(_angleDegrees - step, angleTarget);
            }
        }
        else
        {
            _angleDegrees = angleTarget;
        }

        // The slide: target = latch ? 1 : 0, stepped 1/(time x 30) a tick. A door that does not slide
        // snaps its fraction straight onto the target, exactly as the original's no-stop branch does.
        float slideTarget = _latch ? 1f : 0f;
        if (_slides && _slide != slideTarget && ticks > 0f)
        {
            float step = 1f / ticks;
            if (_slide < slideTarget)
            {
                _slide = Math.Min(_slide + step, slideTarget);
            }
            else
            {
                _slide = Math.Max(_slide - step, slideTarget);
            }
        }
        else
        {
            _slide = slideTarget;
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

        TicksOpen = IsFullyOpen ? TicksOpen + 1 : 0;
    }
}
