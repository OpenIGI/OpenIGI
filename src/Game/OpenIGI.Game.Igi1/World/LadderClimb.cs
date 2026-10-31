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
    private bool _topOffEnded;

    /// <summary>Gets a value indicating whether a climb is under way.</summary>
    public bool IsActive => _active;

    /// <summary>Forgets the climb, as the player leaves the ladder or the level goes.</summary>
    public void Reset()
    {
        _active = false;
        _slideArmed = false;
        _topOffEnded = false;
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

        // Mount and use share E. The release gate is a shell detail that prevents the mounting press
        // from becoming state 40 on the immediately following tick.
        if (!_active)
        {
            _active = true;
            _slideArmed = false;
            if (player.LadderPhase == LadderTraversalPhase.GettingOnTop)
            {
                body?.StartLadderClip(PlayerCamera.LadderTopAnimation, loops: false, backwards: true);
            }
            else
            {
                StartClimbClip(player, body, rung);
            }
        }

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

        if (!player.IsOnLadder || body is null)
        {
            return;
        }

        switch (player.LadderPhase)
        {
            case LadderTraversalPhase.Climbing when player.LadderDirection != 0:
                body.AdvanceLadderClip(player.LadderFacing, out Vector3d climbDelta, out float? rungEventTime, out _);
                player.MoveLadder(climbDelta);
                if (TakeRung(player, rungEventTime, rung))
                {
                    player.CompleteLadderStep();
                    body.SetLadderPlaybackDirection(0);
                }

                break;

            case LadderTraversalPhase.GettingOffTop when _topOffEnded:
                // State 39 (0x40DA80) reads the clip-done bit the previous tick's advance set, in the state machine
                // ahead of this tick's advance: it lets go of the ladder a tick after animation 170 ends, the clip
                // held on its last frame meanwhile.
                _topOffEnded = false;
                player.CompleteLadderTopTransition();
                break;

            case LadderTraversalPhase.GettingOnTop:
            case LadderTraversalPhase.GettingOffTop:
                body.AdvanceLadderClip(player.LadderFacing, out Vector3d topDelta, out float? topEventTime, out bool topEnded);
                player.MoveLadder(topDelta);
                TakeRung(player, topEventTime, rung);

                if (topEnded && player.LadderPhase == LadderTraversalPhase.GettingOffTop)
                {
                    _topOffEnded = true;
                }
                else if (topEnded)
                {
                    player.CompleteLadderTopTransition();
                    if (player.LadderPhase == LadderTraversalPhase.Climbing)
                    {
                        StartClimbClip(player, body, rung);
                    }
                }

                break;

            case LadderTraversalPhase.SlidingDown:
                body.AdvanceLadderClip(player.LadderFacing, out Vector3d slideDelta, out _, out _);

                // State 40's entry only installs animation 169 and the sound. Its damped
                // velocity function first runs on the following tick.
                if (decision != LadderStepResult.Sliding)
                {
                    player.TickLadderSlide(slideDelta, ground);
                }

                break;
        }

        // All four states stay in third person until their actual deinitialization signal.
        if (player.IsOnLadder)
        {
            body.PlaceClimbing(player.LadderFeet, player.LadderFacing);
        }
    }

    /// <summary>
    /// Starts the climb clip and takes its mount frame: the root step to the first rung and the rung event of getting on,
    /// all in one go, so the pause is in place before any input can start a rung.
    /// </summary>
    private static void StartClimbClip(PlayerCamera player, ILadderBody? body, Action<PlayerCamera> rung)
    {
        if (body is null)
        {
            return;
        }

        body.StartLadderClip(PlayerCamera.LadderClimbAnimation, loops: true);
        body.AdvanceLadderClip(player.LadderFacing, out Vector3d entryDelta, out float? mountEventTime, out _);
        player.MoveLadderEntryFrame(entryDelta);
        TakeRung(player, mountEventTime, rung);
        body.SetLadderPlaybackDirection(0);
    }

    // Sounds a ladder state's crossing of event 8 when the latch lets it count; true when state 37 should commit its rung.
    private static bool TakeRung(PlayerCamera player, float? eventTime, Action<PlayerCamera> rung)
    {
        if (eventTime is not { } time || !player.AcceptLadderRungEvent(time))
        {
            return false;
        }

        rung(player);
        return true;
    }
}
