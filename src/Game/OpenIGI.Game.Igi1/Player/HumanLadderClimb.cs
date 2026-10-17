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
        if (!moves)
        {
            return;
        }

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
            Phase = LadderTraversalPhase.Climbing;
            _direction = 0;
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
}
