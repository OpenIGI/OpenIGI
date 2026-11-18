using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi2.Player;

/// <content>A co-op client's copy of another player: posed by the server's, its own controls never run.</content>
public sealed partial class Igi2HumanPlayer
{
    /// <summary>One tick of a player another game runs, its upper body as the server's copy has it.</summary>
    /// <param name="state">The server's copy's locomotion state.</param>
    /// <param name="rearmed">Whether the state's entry must run even if the state is unchanged.</param>
    /// <param name="upperBody">The upper-body mode, 0 to 12.</param>
    /// <param name="fireType">The fire type it fires with.</param>
    /// <param name="upperBodySequence">The count of upper-body clips set on the server's copy.</param>
    /// <param name="position">Where its feet stand, in world units.</param>
    /// <param name="speed">Its speed.</param>
    /// <param name="gamma">Its heading.</param>
    /// <param name="pitch">Its view's pitch.</param>
    /// <remarks>
    /// <para>
    /// The locomotion states are never ticked here, as a soldier's puppet's are not: a state's tick reads the controls, and a copy
    /// has none. Ticked with empty controls, every gait let go of its keys and went to standing, the next snapshot put it back in
    /// the gait, and the gait's clip started over from its first frame -- the body slid over the ground in one frozen frame.
    /// Nor are its action states: the upper body is the server's copy's (<see cref="PoseUpperBodyAsPuppet"/>).
    /// </para>
    /// <para>A run's clip steps at the rate its tick would set (<see cref="Humans.Igi2Human.ControlGait"/>); every other state keeps what its entry set.</para>
    /// </remarks>
    public void PoseAsPuppet(int state, bool rearmed, int upperBody, int fireType, byte upperBodySequence, Vector3d position, Vector3f speed, float gamma, float pitch)
    {
        bool entered = rearmed || Locomotion.CurrentState != state;
        if (entered)
        {
            Locomotion.Enter(state, this);
            PlayWorkAsPuppet(state);
        }

        if (Array.Find(Gaits, g => g.State == state) is { TimeScale: float timeScale })
        {
            ControlGait(timeScale);
        }

        Channels.Accept(NoChannels);
        PoseUpperBodyAsPuppet(upperBody, fireType, upperBodySequence, again: entered);
        Position = position;
        Speed = speed;
        Gamma = gamma;
        IdealGamma = gamma;
        View.Pitch = pitch;
        UpdateBody();
        Animation.StepMain();
        TouchPose();
        UpdateViewBody(updateAnimation: false);
        Weapon.UpdateStatus();
        RunPuppetBody();
    }

    /// <summary>
    /// The clip a copy plays at a door, a terminal, a cabinet, a mounted gun, a timed action or a silent kill: each of those
    /// states' entries needs what is worked, which a copy has none of, and played nothing -- a partner picking a lock or manning
    /// a gun stood there with its gun up. The original's remote human plays the state's own clip
    /// (<c>HumanAnim_SetAnimationAndTimeFromState</c> from <c>HumanRemote_UpdateAnim</c>), the upper body let go.
    /// </summary>
    /// <param name="state">The state entered.</param>
    private void PlayWorkAsPuppet(int state)
    {
        if (state is not (Igi2PlayerState.ActivateDoor or Igi2PlayerState.ActivateTerminal or Igi2PlayerState.ActivateGenericTba
            or Igi2PlayerState.ActivateCabinet or Igi2PlayerState.ActivateStationaryGun or Igi2PlayerState.SilentKill))
        {
            return;
        }

        Animation.ClearUpperBody(0f);
        switch (state)
        {
            case Igi2PlayerState.ActivateDoor:
                Animation.PlayState(PickLockAnimation, 0, StateBlendSeconds);
                break;
            case Igi2PlayerState.ActivateTerminal:
                Animation.PlayState(HackAnimation, 0, StateBlendSeconds);
                break;
            case Igi2PlayerState.ActivateCabinet:
                Animation.PlayState(SearchLoopClip, 0, StateBlendSeconds);
                Animation.LoopMain();
                break;
            case Igi2PlayerState.ActivateStationaryGun:
                Animation.PlayState(StationaryGunClip, 0, StateBlendSeconds);
                break;
            case Igi2PlayerState.SilentKill:
                Animation.PlayNamed(SilentKillAnimation, 0, StateBlendSeconds);
                break;
            default:
                Animation.PlayState(state, 0, StateBlendSeconds);
                break;
        }
    }

    /// <summary>
    /// The body's own run, as a soldier's puppet's runs in its delayed half: the human's part (<c>Human_RunHandler</c>), then the
    /// controllers that step themselves and the blends between clips.
    /// </summary>
    /// <remarks>
    /// Left out, every blend a state's entry started stood at its first tick and drew the clip it blends from, frozen: a
    /// partner that set off slid along in its standing pose, one that stopped held its stride, and one that fell stayed upright.
    /// Retail's remote human runs <c>Human_RunHandler</c> every tick (<c>HumanRemote_RunDelayedHandler</c>).
    /// </remarks>
    public void RunPuppetBody()
    {
        RunHuman();
        Animation.AutoStep();
        TouchPose();
    }

    private static readonly float[] NoChannels = new float[Humans.Igi2HumanChannels.Count];
}
