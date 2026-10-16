using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Weapons;

namespace OpenIGI.Game.Igi2.Player;

/// <content>
/// The player's breath: the sway a scope shows once it has been held a second, the kit rattling as the player runs, and the
/// panting that running builds up and that dies away after.
/// </content>
public sealed partial class Igi2HumanPlayer
{
    /// <summary>The sound of the kit rattling while running, <c>equipment_01</c>.</summary>
    public const string RunningSound = "equipment_01";

    /// <summary>The panting a long run brings on, <c>breathe_loop</c>.</summary>
    public const string BreatheLoopSound = "breathe_loop";

    /// <summary>The last breath once the panting stops, <c>breathe_stop</c>.</summary>
    public const string BreatheStopSound = "breathe_stop";

    // The sway's phase, in ticks of a two-second cycle, and how long the scope has been held, up to five seconds.
    private int _breathPhase;
    private int _breathHold;

    private IIgi2SoundInstance? _runningSound;
    private IIgi2SoundInstance? _breatheLoop;
    private IIgi2SoundInstance? _breatheStop;

    /// <summary>
    /// Gets how out of breath the player is, nought to one: running builds it by a six-hundredth a tick, anything else lets it
    /// fall by up to a hundred-and-fiftieth.
    /// </summary>
    public float Fatigue { get; private set; }

    private bool IsRunningState => Locomotion.CurrentState is >= 3 and <= 10;

    /// <summary>
    /// The scope's sway, <c>humanplayer.c HumanPlayer_HandleBreathViewOffset</c>: with a <c>Zoom</c> weapon's scope up, the pitch
    /// rocks on a two-second cosine by the scope's breath offset for the stance; still for the first second it is held, then
    /// growing over the next four. With no scope up the hold winds back two ticks a tick and the phase starts afresh.
    /// </summary>
    private void HandleBreathViewOffset()
    {
        if (Weapon.ActiveScope is not { } scope)
        {
            _breathPhase = 0;
            _breathHold = _breathHold <= 0 ? 0 : _breathHold - 2;
            return;
        }

        Fpu sway = (Fpu)(double)(_breathPhase % 120) * 0.008333333767950535;
        float? degrees = Pose switch
        {
            Igi2HumanPose.Standing => scope.StandBreathOffsetDegrees,
            Igi2HumanPose.Crouching => scope.CrouchBreathOffsetDegrees,
            Igi2HumanPose.LyingFlat => scope.FlatBreathOffsetDegrees,
            _ => null,
        };
        if (degrees is { } d)
        {
            sway = Fpu.Cos(sway * 6.2831854820251465) * ((Fpu)d * 0.01745329238474369);
        }

        _breathHold++;
        Fpu grown;
        if (_breathHold <= 60)
        {
            grown = (Fpu)0.0;
        }
        else
        {
            if (_breathHold >= 300)
            {
                _breathHold = 300;
            }

            grown = (Fpu)(double)(_breathHold - 60) * 0.004166666883975267;
            if (grown.Value <= 0.0)
            {
                grown = (Fpu)0.0;
            }

            _breathPhase++;
        }

        View.BreathPitchOffset = (grown * sway).F;
    }

    /// <summary>
    /// The kit's rattle, <c>humanplayer.c HumanPlayer_MaintainRunningSound</c>: started again whenever it has ended while the player
    /// runs, and stopped once it does not.
    /// </summary>
    private void MaintainRunningSound()
    {
        if (!IsRunningState)
        {
            _runningSound?.Delete();
            _runningSound = null;
        }
        else if (_runningSound is not { IsAlive: true })
        {
            _runningSound = Surroundings.StartSound(RunningSound);
        }
    }

    /// <summary>
    /// The panting, <c>humanplayer.c HumanPlayer_MaintainFatigue</c>: the fatigue moves toward one while running and nought otherwise,
    /// by at most a six-hundredth up and a hundred-and-fiftieth down. Falling, a pant playing stops with a last breath; otherwise past
    /// seven tenths the pant plays, louder to the full at one, and below that it stops.
    /// </summary>
    private void MaintainFatigue()
    {
        Fpu step = IsRunningState ? (Fpu)1.0 - Fatigue : -(Fpu)Fatigue;
        if (step.Value < -0.006666666828095913)
        {
            step = (Fpu)(-0.006666666828095913);
        }
        else if (step.Value > 0.0016666667070239782)
        {
            step = (Fpu)0.0016666667070239782;
        }

        Fatigue = (step + Fatigue).F;
        if (step.Value < 0.0)
        {
            if (_breatheLoop is not null)
            {
                _breatheLoop.Delete();
                _breatheLoop = null;
                _breatheStop = Surroundings.StartSound(BreatheStopSound);
            }

            return;
        }

        float volume = (((Fpu)Fatigue - 0.699999988079071) * 3.3333332538604736).F;
        if (volume < 0f)
        {
            _breatheLoop?.Delete();
            _breatheLoop = null;
            return;
        }

        if (_breatheLoop is not { IsAlive: true })
        {
            _breatheStop?.Delete();
            _breatheStop = null;
            _breatheLoop = Surroundings.StartSound(BreatheLoopSound);
        }

        _breatheLoop.SetVolumes(volume, 1f);
    }
}
