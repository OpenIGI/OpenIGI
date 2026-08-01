using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Player;

/// <summary>What deciding the next rung resolves to at a rung boundary.</summary>
public enum LadderStepResult
{
    /// <summary>No input — hold at the current rung.</summary>
    Idle,

    /// <summary>A step up has begun; its clip runs until the cycle completes.</summary>
    SteppingUp,

    /// <summary>A step down has begun.</summary>
    SteppingDown,

    /// <summary>At the top — hand off to getting off the head, then to standing.</summary>
    ReachedTop,

    /// <summary>Stepped off the foot — dismount and return to standing.</summary>
    Dismounted,

    /// <summary>The player asked to slide down — drop to the foot.</summary>
    Sliding,
}

/// <summary>
/// Drives a human up and down a ladder — the running half of locomotion state 37 (<c>sub_40D6B0</c>),
/// <b>moved by the climb animation's own root motion</b>, which is how the retail game does it.
/// </summary>
/// <remarks>
/// <para>
/// Read out of the running game with <c>a probe of the running original</c> (the <c>Ladder_ClimbTick</c> hook): the body
/// is <b>not</b> slid along a synthetic vertical line — every tick it is moved by the <c>walk_ladder</c>
/// clip's root motion, so it sways side to side (the logged X swings ±50 units a rung) while it climbs,
/// exactly as a walking guard is moved by its walk clip. A rung is one clip cycle (~10 ticks, ~1228 units
/// of height), the rung counter (<c>human+3456</c>) ticks up when the cycle completes, and <b>input is
/// taken only at a rung boundary</b> (<c>human+3460</c> direction is zero there), so a rung once begun
/// cannot be cancelled.
/// </para>
/// <para>
/// The position is owned here but <i>advanced from the outside</i>: the host samples the body's clip root
/// motion each tick and calls <see cref="Move"/> with it. This keeps the movement the animation's and the
/// discrete rung logic this class's.
/// </para>
/// </remarks>
public sealed class HumanLadderClimb
{
    /// <summary>The height of one rung, in world units — the ~1228.8 the retail rung counter ticks by.</summary>
    /// <remarks>
    /// Animation 168 is a two-rung stride, so a rung is measured by height climbed, not by the clip's own
    /// loop: the probe shows the rung counter ticking every ~1228 units, twice per clip cycle.
    /// </remarks>
    public const double RungHeightUnits = LadderClimbLine.StepLengthUnits;

    private Vector3d _position;
    private double _bottomZ;
    private double _topZ;
    private double _rungStartZ;
    private int _topStep;

    private int _step;
    private int _direction; // 0 at a rung boundary, +1 up, -1 down (human+3460)

    /// <summary>Gets whether the human is currently on a ladder.</summary>
    public bool IsOnLadder { get; private set; }

    /// <summary>Gets the ladder yaw the human faces while climbing, in radians.</summary>
    public float Facing { get; private set; }

    /// <summary>Gets whether the climb is at a rung boundary, ready to take input.</summary>
    public bool AtBoundary => _direction == 0;

    /// <summary>Gets the direction the current rung is going: 0 at a boundary, +1 up, −1 down.</summary>
    public int Direction => _direction;

    /// <summary>Gets the current whole rung, 0 at the foot.</summary>
    public int Step => _step;

    /// <summary>Gets the human's world position on the ladder, in engine units.</summary>
    public Vector3d Position => _position;

    /// <summary>Puts the human on a ladder.</summary>
    /// <param name="line">The ladder's climb geometry.</param>
    /// <param name="facing">The yaw the human faces — into the ladder.</param>
    /// <param name="atTop">
    /// <see langword="true"/> to mount at the head (approached from above); <see langword="false"/> at the foot.
    /// </param>
    public void Mount(LadderClimbLine line, float facing, bool atTop)
    {
        _bottomZ = line.Bottom.Z;
        _topZ = line.Top.Z;
        _topStep = line.TopStep;
        Facing = facing;
        IsOnLadder = true;

        // The retail mount stands the body OFF the magic vertex (sub_4113E0): about 1515.52 units back
        // from the ladder -- along the body's own backward direction, since it faces the ladder as
        // (-sin f, cos f) so back is (sin f, -cos f) -- so its hands reach forward onto the rungs, and
        // about 3915.776 units up it, so a foot mount starts a couple of rungs off the ground. Confirmed
        // against the running game: it climbs ~1515 units in front of the ladder model, not on it.
        Vector3d at = atTop ? line.Top : line.Bottom;
        _position = new Vector3d(
            at.X + (Math.Sin(facing) * HorizontalStandoffUnits),
            at.Y - (Math.Cos(facing) * HorizontalStandoffUnits),
            at.Z + (atTop ? 0.0 : FootMountLiftUnits));
        _rungStartZ = _position.Z;
        _step = atTop ? _topStep : 0;
        _direction = 0;
    }

    /// <summary>How far back from the ladder the body stands, in world units — the retail mount's horizontal offset.</summary>
    /// <remarks>The <c>col116 · 1515.52</c> term of <c>sub_4113E0</c>'s stand-off; the body reaches its hands forward onto the rungs.</remarks>
    public const double HorizontalStandoffUnits = 1515.52;

    /// <summary>How far up the ladder a foot mount starts, in world units — the ~0.96 m the retail mount lifts by.</summary>
    /// <remarks>From the probe: the mount snaps the body about 3915 units (<c>3915.776</c>) up the ladder.</remarks>
    public const double FootMountLiftUnits = 3915.776;

    /// <summary>Decides what to do at a rung boundary — begin a rung, hand off, or hold.</summary>
    /// <param name="up">Whether up (forward) is held.</param>
    /// <param name="down">Whether down (back) is held.</param>
    /// <param name="slide">Whether slide (use) is held.</param>
    /// <returns>The decision; a stepping result begins a rung.</returns>
    /// <remarks>Does nothing unless <see cref="AtBoundary"/> — a rung in progress cannot be interrupted.</remarks>
    public LadderStepResult Decide(bool up, bool down, bool slide)
    {
        if (!IsOnLadder || !AtBoundary)
        {
            return LadderStepResult.Idle;
        }

        if (slide)
        {
            IsOnLadder = false;
            return LadderStepResult.Sliding;
        }

        if (up)
        {
            if (_step >= _topStep)
            {
                IsOnLadder = false;
                return LadderStepResult.ReachedTop;
            }

            _direction = 1;
            _rungStartZ = _position.Z;
            return LadderStepResult.SteppingUp;
        }

        if (down)
        {
            if (_step <= 0)
            {
                IsOnLadder = false;
                return LadderStepResult.Dismounted;
            }

            _direction = -1;
            _rungStartZ = _position.Z;
            return LadderStepResult.SteppingDown;
        }

        return LadderStepResult.Idle;
    }

    /// <summary>Moves the body by the climb clip's root motion for this tick.</summary>
    /// <param name="delta">
    /// The clip's forward root-motion delta, already rotated into the facing frame (world units); the
    /// climb direction is applied here, so pass the same delta whether going up or down.
    /// </param>
    /// <returns><see langword="true"/> when this move completed the rung (climbed a whole rung's height).</returns>
    /// <remarks>Only moves while a rung is in progress; the height is clamped to the ladder's extent.</remarks>
    public bool Move(Vector3d delta)
    {
        if (_direction == 0)
        {
            return false;
        }

        _position = new Vector3d(
            _position.X + (delta.X * _direction),
            _position.Y + (delta.Y * _direction),
            Math.Clamp(_position.Z + (delta.Z * _direction), _bottomZ, _topZ));

        // A rung is one rung's worth of height climbed, twice per two-rung clip cycle.
        return Math.Abs(_position.Z - _rungStartZ) >= RungHeightUnits;
    }

    /// <summary>Commits the rung in progress — the clip cycle has completed — returning to a boundary.</summary>
    /// <returns><see langword="true"/> when a rung was in progress and is now committed.</returns>
    public bool CompleteStep()
    {
        if (_direction == 0)
        {
            return false;
        }

        _step += _direction;
        _direction = 0;
        return true;
    }
}
