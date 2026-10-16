using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Soldiers;

namespace OpenIGI.Game.Igi2.Player;

/// <content>
/// The silent kill: activating a soldier from behind plays the player's <c>tba_silent_kill_jones</c> with the soldier taken into its
/// own silent death beside it, seen from the movement camera, until the clip ends.
/// </content>
public sealed partial class Igi2HumanPlayer
{
    /// <summary>The player's clip for it.</summary>
    public const string SilentKillAnimation = "tba_silent_kill_jones";

    /// <summary>A clip's time units a tick, as the timed action's length is taken from the clip's.</summary>
    private const int ClipUnitsPerTick = 160;

    /// <summary>
    /// <c>HumanPlayer_InitStateSilentKill</c>: standing, the upper body cleared, the standing precision, the clip from its start; the
    /// soldier killed beside the player, counted as its kill; the action locked, a progress bar the clip's length, the movement
    /// camera, the view straight ahead and locked, the player stopped where it stands and its heading locked.
    /// </summary>
    private void EnterSilentKill()
    {
        if (ActivatingTarget is not Igi2HumanSoldier soldier)
        {
            return;
        }

        PreviousStance = Pose;
        Pose = Igi2HumanPose.Standing;
        CurrentUpperBody = 0;
        RequestedUpperBody = 0;
        Animation.ClearUpperBody(0.4f);
        AimPrecision(Igi2PrecisionType.Standing);
        Animation.PlayNamed(SilentKillAnimation, 0, 0f);
        soldier.KillSilently(Gamma, Position);
        KilledByYou(soldier);
        IsLockAction = true;
        TimedActionTimer = 0;
        CameraMode = 3;
        IsProgressBar = true;
        IsDisableDeltaTranslationScale = true;
        TimedActionTime = Animation.MainTotalTime / ClipUnitsPerTick;
        View.LookAhead();
        View.IsLockOrientation = true;
        MoveOnGround(0f, 1781.48681640625f, 0f, 1781.48681640625f);
        IsLockOrientation = true;
        Locomotion.Current = (_, _) => RunSilentKill();
    }

    /// <summary>
    /// <c>HumanPlayer_RunStateSilentKill</c>: the action menu stays hidden and the bar fills; when the clip ends the activation is let go
    /// and the player returns to its stance, or flinches if hit meanwhile.
    /// </summary>
    private void RunSilentKill()
    {
        ActionMenu.SetNoDrawTimeout(ActivationMenuHide);
        if (TimedActionTimer < TimedActionTime)
        {
            TimedActionTimer++;
        }

        if (!Has(Igi2PlayerSignal.AnimationEnded))
        {
            return;
        }

        IsActivateLocked = false;
        EndTimedAction();
    }
}
