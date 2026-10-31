using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi1.Player;

/// <summary>The active IGI1 ladder locomotion state.</summary>
public enum LadderTraversalPhase
{
    /// <summary>The human is using ordinary locomotion.</summary>
    Inactive,

    /// <summary>State 37, animation 168: paused at or moving between authored rung events.</summary>
    Climbing,

    /// <summary>State 38, animation 170 backwards: entering the ladder from its top.</summary>
    GettingOnTop,

    /// <summary>State 39, animation 170 forwards: moving from the top rung onto the roof.</summary>
    GettingOffTop,

    /// <summary>State 40, animation 169: gravity-driven slide until ground contact.</summary>
    SlidingDown,
}

/// <summary>What state 37 resolved from this tick's input.</summary>
public enum LadderStepResult
{
    /// <summary>No transition was requested.</summary>
    Idle,

    /// <summary>Animation 168 began moving forwards toward its next event.</summary>
    SteppingUp,

    /// <summary>Animation 168 began moving backwards toward its previous event.</summary>
    SteppingDown,

    /// <summary>State 39 began; animation 170 must play forwards before detaching.</summary>
    ReachedTop,

    /// <summary>State 37 stepped below rung zero and detached at the foot.</summary>
    Dismounted,

    /// <summary>State 40 began and remains attached until a ground collision.</summary>
    Sliding,
}

/// <summary>IGI1's four-state player ladder machine, driven by authored animation root motion.</summary>
/// <remarks>
/// <c>sub_40D6B0</c> pauses animation 168 whenever authored event 8 changes time, commits
/// <c>nCurrentStep += nStepChange</c>, and only then accepts another up/down rung. Use is different:
/// it can enter the slide state even halfway through a rung. States 38 and 39 keep the human attached
/// while animation 170 supplies the complete top-mount path.
/// </remarks>
public sealed class HumanLadderClimb
{
    private Vector3d _position;
    private double _bottomZ;
    private double _topZ;
    private int _topStep;
    private int _step;
    private int _direction;
    private int _stepSoundCursor;
    private float _lastRungEventTime = UnlatchedRungEventTime;

    /// <summary>What state 37's entry <c>0x40D660</c> writes to the latch: <c>-1.0</c>, a time no event has.</summary>
    private const float UnlatchedRungEventTime = -1f;

    /// <summary>The sound state 40's entry <c>0x40DBA0</c> starts on the human, stopped by its ground signal.</summary>
    public const string SlideSoundName = "wire_slide_1";

    /// <summary>
    /// The order the four rung sounds are used in — a fixed twelve-step cycle, transcribed from the
    /// twelve dwords at <c>0x538C18</c>.
    /// </summary>
    /// <remarks>
    /// Like the footstep cycle it is hand-authored rather than a permutation: in twelve rungs the outer
    /// sounds, <c>walk_ladder_1</c> and <c>_4</c>, are heard twice each and the middle pair four times.
    /// </remarks>
    public static ReadOnlySpan<int> StepSoundCycle => [0, 1, 2, 3, 1, 2, 1, 2, 3, 2, 1, 0];

    /// <summary>Gets the active traversal phase.</summary>
    public LadderTraversalPhase Phase { get; private set; }

    /// <summary>Gets whether ordinary first-person locomotion is currently disabled by a ladder state.</summary>
    public bool IsOnLadder => Phase != LadderTraversalPhase.Inactive;

    /// <summary>Gets the ladder yaw committed by the mount handler.</summary>
    public float Facing { get; private set; }

    /// <summary>Gets whether state 37 is paused at an authored rung event.</summary>
    public bool AtBoundary => Phase == LadderTraversalPhase.Climbing && _direction == 0;

    /// <summary>Gets animation 168's playback direction: zero while paused, +1 up and −1 down.</summary>
    public int Direction => _direction;

    /// <summary>Gets the current whole rung, zero at the foot.</summary>
    public int Step => _step;

    /// <summary>Gets the human task position in world units.</summary>
    public Vector3d Position => _position;

    /// <summary>Mounts the human using <c>sub_4113E0</c>'s exact authored offsets.</summary>
    /// <param name="ladder">The resolved ladder task.</param>
    /// <param name="atTop">Whether activation resolved through magic vertex 1.</param>
    public void Mount(LadderPlacement ladder, bool atTop)
    {
        ArgumentNullException.ThrowIfNull(ladder);

        _bottomZ = ladder.Bottom.Z;
        _topZ = ladder.Top.Z;
        _topStep = ladder.ClimbLine.TopStep;
        Facing = ladder.Facing;
        _position = atTop ? ladder.TopMount : ladder.BottomMount;
        _step = atTop ? _topStep : 0;
        _direction = 0;
        Phase = atTop ? LadderTraversalPhase.GettingOnTop : LadderTraversalPhase.Climbing;

        // Only state 37's entry clears the latch; state 38's (0x40D8E0) leaves whatever the last ladder
        // left in it.
        if (!atTop)
        {
            _lastRungEventTime = UnlatchedRungEventTime;
        }
    }

    /// <summary>Takes a crossing of rung event 8, unless it is the event time the climb last took.</summary>
    /// <param name="time">The crossed event's authored time.</param>
    /// <returns>
    /// <see langword="true"/> when the crossing counts: state 37 then pauses and commits the rung, and
    /// all three states sound it and post the AI event.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <c>0x40D6B0</c>, <c>0x40D920</c> and <c>0x40DA80</c> all open with
    /// <c>if (Human+1336 == 8 &amp;&amp; Human+1344 != Human+3464)</c> and then store the time. Three
    /// behaviours follow from that one comparison:
    /// </para>
    /// <list type="bullet">
    /// <item>The <b>mount</b> is heard. State 37's entry sets the latch to <c>-1.0</c> and starts animation
    /// 168 afresh, whose first tick reports its event at time zero, so climbing on — from the foot, or
    /// at the end of the top mount — plays a rung sound with nothing committed.</item>
    /// <item>A rung can be <b>reversed</b>. Paused just past the event at 1737, the first tick back
    /// crosses 1737 again; being the latched time it is ignored, and the clip runs back to zero.</item>
    /// <item>Climbing off the top can start <b>silently</b>. Animation 170's first event is at zero; if the
    /// last rung climbed was also the one at zero, that first crossing is the latched time.</item>
    /// </list>
    /// </remarks>
    public bool AcceptRungEvent(float time)
    {
        if (time == _lastRungEventTime)
        {
            return false;
        }

        _lastRungEventTime = time;
        return true;
    }

    /// <summary>Resolves state 37 input without advancing its animation.</summary>
    /// <param name="up">Whether MoveUp is held.</param>
    /// <param name="down">Whether MoveDown is held.</param>
    /// <param name="slide">Whether Activate is held after the mount press was released.</param>
    /// <returns>The transition or playback change selected this tick.</returns>
    public LadderStepResult Decide(bool up, bool down, bool slide)
    {
        if (Phase != LadderTraversalPhase.Climbing)
        {
            return LadderStepResult.Idle;
        }

        // sub_40D6B0 checks Activate after the rung-direction block, but does not require direction zero:
        // unlike changing direction, a slide is allowed to interrupt a rung in progress.
        if (slide)
        {
            _direction = 0;
            Phase = LadderTraversalPhase.SlidingDown;
            return LadderStepResult.Sliding;
        }

        if (_direction != 0)
        {
            return LadderStepResult.Idle;
        }

        if (up)
        {
            if (_step == _topStep)
            {
                Phase = LadderTraversalPhase.GettingOffTop;
                return LadderStepResult.ReachedTop;
            }

            _direction = 1;
            return LadderStepResult.SteppingUp;
        }

        if (down)
        {
            if (_step == 0)
            {
                Phase = LadderTraversalPhase.Inactive;
                return LadderStepResult.Dismounted;
            }

            _direction = -1;
            return LadderStepResult.SteppingDown;
        }

        return LadderStepResult.Idle;
    }

    /// <summary>Applies this tick's already-rotated animation root motion.</summary>
    /// <param name="delta">The authored delta in world units.</param>
    /// <remarks>
    /// Ladder mounting sets <c>isDisableDeltaTranslationScale</c>, so this delta is deliberately not
    /// multiplied by the ordinary player's 1.75 locomotion scale.
    /// </remarks>
    public void Move(Vector3d delta)
    {
        bool moves = (Phase == LadderTraversalPhase.Climbing && _direction != 0)
            || Phase is LadderTraversalPhase.GettingOnTop or LadderTraversalPhase.GettingOffTop;
        if (moves)
        {
            Apply(delta);
        }
    }

    /// <summary>Moves the climber by a correction from outside the climb: a co-op server's word on where it is.</summary>
    /// <param name="delta">The correction, in world units.</param>
    /// <remarks>The climb's place is the body's on the ladder; corrected on the body alone, the next step put it back.</remarks>
    public void Shift(Vector3d delta) => _position += delta;

    /// <summary>Applies the one frame animation 168 runs on entering state 37, before its event pauses it.</summary>
    /// <param name="delta">That frame's already-rotated root motion, in world units.</param>
    /// <remarks>
    /// A new clip node starts at a full frame step (<c>0x4D4F60</c>), and <c>HumanPlayer_Update</c>
    /// (<c>0x4109A0</c>) ticks it (<c>0x4D2FB0</c>) and moves the human by it (<c>0x4614B0</c>) in the
    /// same update that entered the state. Only the next update's state 37 sees event 8 at zero and sets
    /// the rate to zero, so the climber always settles one frame into the clip, no rung committed.
    /// </remarks>
    public void MoveEntryFrame(Vector3d delta)
    {
        if (Phase == LadderTraversalPhase.Climbing)
        {
            Apply(delta);
        }
    }

    private void Apply(Vector3d delta)
    {
        double z = _position.Z + delta.Z;
        if (Phase == LadderTraversalPhase.Climbing)
        {
            z = Math.Clamp(z, _bottomZ, _topZ);
        }

        _position = new Vector3d(_position.X + delta.X, _position.Y + delta.Y, z);
    }

    /// <summary>Commits the rung when animation 168 crosses authored event 8.</summary>
    /// <returns><see langword="true"/> when a moving rung was committed.</returns>
    public bool CompleteStep()
    {
        if (Phase != LadderTraversalPhase.Climbing || _direction == 0)
        {
            return false;
        }

        _step += _direction;
        _direction = 0;
        return true;
    }

    /// <summary>Finishes animation 170 and advances state 38 or 39.</summary>
    /// <returns><see langword="true"/> when a top transition was active.</returns>
    public bool CompleteTopTransition()
    {
        if (Phase == LadderTraversalPhase.GettingOnTop)
        {
            // This is state 37's entry, so it clears the latch just as a mount at the foot does.
            Phase = LadderTraversalPhase.Climbing;
            _direction = 0;
            _lastRungEventTime = UnlatchedRungEventTime;
            return true;
        }

        if (Phase == LadderTraversalPhase.GettingOffTop)
        {
            Phase = LadderTraversalPhase.Inactive;
            _direction = 0;
            return true;
        }

        return false;
    }

    /// <summary>Takes the sound of the rung event 8 just marked, and advances the cycle.</summary>
    /// <returns>The definition name, <c>walk_ladder_1</c> through <c>walk_ladder_4</c>.</returns>
    /// <remarks>
    /// <para>
    /// States 37, 38 and 39 (<c>0x40D6B0</c>, <c>0x40D920</c>, <c>0x40DA80</c>) share the block: when the
    /// last crossed event is 8 at a new time they <c>sprintf("walk_ladder_%d", table[cursor] + 1)</c>,
    /// advance <c>Human+3468</c> to <c>(cursor + 1) % 12</c>, play the sound at the human's position and
    /// post the LADDER AI event. So the top transitions are heard rung by rung as well, not only the
    /// climb.
    /// </para>
    /// <para>
    /// No entry or mount resets the cursor, so it carries from one ladder to the next.
    /// </para>
    /// </remarks>
    public string NextStepSound()
    {
        int slot = StepSoundCycle[_stepSoundCursor];
        _stepSoundCursor = (_stepSoundCursor + 1) % StepSoundCycle.Length;
        return $"walk_ladder_{slot + 1}";
    }

    /// <summary>Copies the collision-resolved slide position back into the traversal state.</summary>
    /// <param name="position">The body position after its slide movement and collision pass.</param>
    public void UpdateSlidePosition(Vector3d position)
    {
        if (Phase == LadderTraversalPhase.SlidingDown)
        {
            _position = position;
        }
    }

    /// <summary>Detaches state 40 after its ground-collision signal.</summary>
    /// <returns><see langword="true"/> when a slide was active.</returns>
    public bool CompleteSlide()
    {
        if (Phase != LadderTraversalPhase.SlidingDown)
        {
            return false;
        }

        Phase = LadderTraversalPhase.Inactive;
        return true;
    }

    /// <summary>Lets go of the ladder wherever the climb is, as a co-op server that has the climber off it says.</summary>
    public void Release() => Phase = LadderTraversalPhase.Inactive;
}
