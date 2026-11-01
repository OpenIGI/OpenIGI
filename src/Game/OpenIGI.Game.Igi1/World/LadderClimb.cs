using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>One player's ladder: the climb, the top and the slide, a tick at a time, on the body it climbs with.</summary>
/// <remarks>
/// The complete IGI1 ladder graph: 37 pauses animation 168 at authored event 8, 38/39 play animation 170 backwards/forwards
/// at the top, and 40 loops 169 while its damped velocity falls to a ground collision. Every advance happens here, on the
/// fixed 30 Hz tick. The mission's own player and every co-op partner climb through this one machine.
/// </remarks>
public sealed class LadderClimb
{
    private bool _active;
    private bool _slideArmed;
    private bool _topEnded;

    // The rung event the last advance crossed, which the next tick's state update takes.
    private float? _pendingEvent;

    /// <summary>Gets a value indicating whether a climb is under way.</summary>
    public bool IsActive => _active;

    /// <summary>Forgets the climb, as the player leaves the ladder or the level goes.</summary>
    public void Reset()
    {
        _active = false;
        _slideArmed = false;
        _topEnded = false;
        _pendingEvent = null;
    }

    /// <summary>Runs the climb a tick.</summary>
    /// <param name="player">The climber, on a ladder.</param>
    /// <param name="body">The body it climbs with, or <see langword="null"/> when there is none to pose.</param>
    /// <param name="up">Whether up is held.</param>
    /// <param name="down">Whether down is held.</param>
    /// <param name="use">Whether use is held.</param>
    /// <param name="ground">The ground a slide lands on.</param>
    /// <param name="rung">Sounds a rung the climber crossed.</param>
    public void Tick(PlayerCamera player, ILadderBody? body, bool up, bool down, bool use, IGroundHeightField ground, Action<PlayerCamera> rung)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(rung);

        // The mount tick: 0x4113E0 sets state 37 or 38 after HumanPlayer_Update's state machine has run (0x4109A0), so no
        // state update reads this tick's keys; only the update's clip advance and move follow -- 168's first frame at the
        // foot, 170's first frame back from the top. Climbing starts on the next tick.
        if (!_active)
        {
            _active = true;
            _slideArmed = false;
            _topEnded = false;
            _pendingEvent = null;
            if (player.LadderPhase == LadderTraversalPhase.GettingOnTop)
            {
                body?.StartLadderClip(PlayerCamera.LadderTopAnimation, loops: false, backwards: true);
                Advance(player, body, LadderStepResult.Idle, ground);
            }
            else
            {
                StartClimbClip(player, body);
            }

            Place(player, body);
            return;
        }

        // Each state's update opens on the event the last tick's advance crossed (0x40D6B0, 0x40D920, 0x40DA80): event 8 at a
        // time other than the latched one sounds a rung, and in state 37 pauses the clip and commits the step it ends.
        if (_pendingEvent is { } time)
        {
            _pendingEvent = null;
            if (player.AcceptLadderRungEvent(time))
            {
                rung(player);
                if (player.LadderPhase == LadderTraversalPhase.Climbing)
                {
                    player.CompleteLadderStep();
                    body?.SetLadderPlaybackDirection(0);
                }
            }
        }

        // States 38 and 39 read the clip-done bit the last tick's advance set, the clip held on its last frame meanwhile.
        // 38 then enters 37, whose update first runs next tick: this tick only takes 168's first frame. 39 lets go.
        if (_topEnded && player.LadderPhase is LadderTraversalPhase.GettingOnTop or LadderTraversalPhase.GettingOffTop)
        {
            _topEnded = false;
            player.CompleteLadderTopTransition();
            if (player.LadderPhase == LadderTraversalPhase.Climbing)
            {
                StartClimbClip(player, body);
            }

            Place(player, body);
            return;
        }

        // The use key counts only once it has been let go since the mount: the press that took hold of the ladder is no
        // fresh press (0x40D6B0 wants input 9 down this tick and up the last).
        if (!use)
        {
            _slideArmed = true;
        }

        bool slide = use && _slideArmed;
        LadderStepResult decision = player.DecideLadderStep(up, down, slide);
        switch (decision)
        {
            case LadderStepResult.SteppingUp:
            case LadderStepResult.SteppingDown:
                body?.SetLadderPlaybackDirection(player.LadderDirection);
                break;

            case LadderStepResult.ReachedTop:
                body?.StartLadderClip(PlayerCamera.LadderTopAnimation, loops: false);
                break;

            case LadderStepResult.Sliding:
                body?.StartLadderClip(PlayerCamera.LadderSlideAnimation, loops: true);
                break;
        }

        Advance(player, body, decision, ground);
        Place(player, body);
    }

    // HumanPlayer_Update's clip advance and move, after the state update: the root step moves the origin, and the event it
    // crossed waits for the next tick's update.
    private void Advance(PlayerCamera player, ILadderBody? body, LadderStepResult decision, IGroundHeightField ground)
    {
        if (!player.IsOnLadder || body is null)
        {
            return;
        }

        switch (player.LadderPhase)
        {
            case LadderTraversalPhase.Climbing when player.LadderDirection != 0:
                body.AdvanceLadderClip(player.LadderFacing, out Vector3d climbDelta, out float? climbEvent, out _);
                player.MoveLadder(climbDelta);
                _pendingEvent = climbEvent;
                break;

            case LadderTraversalPhase.GettingOnTop:
            case LadderTraversalPhase.GettingOffTop:
                body.AdvanceLadderClip(player.LadderFacing, out Vector3d topDelta, out float? topEvent, out bool topEnded);
                player.MoveLadder(topDelta);
                _pendingEvent = topEvent;
                _topEnded = topEnded;
                break;

            case LadderTraversalPhase.SlidingDown:
                body.AdvanceLadderClip(player.LadderFacing, out Vector3d slideDelta, out _, out _);
                player.TickLadderSlide(slideDelta, ground, entering: decision == LadderStepResult.Sliding);
                break;
        }
    }

    // All four states stay in third person until their actual deinitialization signal.
    private static void Place(PlayerCamera player, ILadderBody? body)
    {
        if (player.IsOnLadder)
        {
            body?.PlaceClimbing(player.LadderFeet, player.LadderFacing);
        }
    }

    /// <summary>
    /// Enters state 37 (<c>0x40D660</c>): animation 168 from its start, and the frame the same update advances it, whose rung
    /// event at zero the next tick's update takes -- the sound of getting on, nothing committed.
    /// </summary>
    private void StartClimbClip(PlayerCamera player, ILadderBody? body)
    {
        if (body is null)
        {
            return;
        }

        body.StartLadderClip(PlayerCamera.LadderClimbAnimation, loops: true);
        body.AdvanceLadderClip(player.LadderFacing, out Vector3d entryDelta, out float? mountEvent, out _);
        player.MoveLadderEntryFrame(entryDelta);
        _pendingEvent = mountEvent;
        body.SetLadderPlaybackDirection(0);
    }
}
