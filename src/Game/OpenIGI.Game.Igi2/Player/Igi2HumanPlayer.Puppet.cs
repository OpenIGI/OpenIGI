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
    /// <param name="animation">The state the server's copy's main clip was last set from, or 0 when not known.</param>
    /// <remarks>
    /// <para>
    /// The locomotion states are never ticked here, as a soldier's puppet's are not: a state's tick reads the controls, and a copy
    /// has none. Ticked with empty controls, every gait let go of its keys and went to standing, the next snapshot put it back in
    /// the gait, and the gait's clip started over from its first frame -- the body slid over the ground in one frozen frame.
    /// Nor are its action states: the upper body is the server's copy's (<see cref="PoseUpperBodyAsPuppet"/>).
    /// </para>
    /// <para>A run's clip steps at the rate its tick would set (<see cref="Humans.Igi2Human.ControlGait"/>); every other state keeps what its entry set.</para>
    /// </remarks>
    public void PoseAsPuppet(int state, bool rearmed, int upperBody, int fireType, byte upperBodySequence, Vector3d position, Vector3f speed, float gamma, float pitch, int animation = 0)
    {
        bool entered = rearmed || Locomotion.CurrentState != state;
        if (entered)
        {
            // A copy is never hit itself, so it has no zone for the flinch's entry to pick its clip by, and the entry read
            // its table at no zone and threw: the zone is read back from the flinch the server's copy plays.
            if (state == Igi2PlayerState.Hit)
            {
                Damage.LastPart = FlinchPart(animation);
            }

            Locomotion.Enter(state, this);
        }

        PlayWorkAsPuppet(state, animation, entered, position);

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
    /// <param name="state">The state the copy is in.</param>
    /// <param name="animation">The state the server's copy's main clip was last set from, or 0 when not known.</param>
    /// <param name="entered">Whether the state was entered this tick.</param>
    /// <param name="position">Where the copy stands this tick.</param>
    /// <remarks>
    /// The clip is the one the server's copy plays, by the state it was set from, whenever that is known: a timed action names
    /// its own clip (a gate's lock picked, a code pad hacked), which no state's entry here could tell, and a copy at one stood
    /// still; and work that goes from one clip to the next (a cabinet opened, searched and shut) goes with it.
    /// </remarks>
    private void PlayWorkAsPuppet(int state, int animation, bool entered, Vector3d position)
    {
        if (state is not (Igi2PlayerState.ActivateDoor or Igi2PlayerState.ActivateTerminal or Igi2PlayerState.ActivateGenericTba
            or Igi2PlayerState.ActivateCabinet or Igi2PlayerState.ActivateStationaryGun or Igi2PlayerState.SilentKill))
        {
            _puppetWork = 0;
            return;
        }

        bool known = animation > 0 && animation != Humans.Igi2HumanAnimation.NamedStateFallback;
        if (!entered && (!known || animation == _puppetWork))
        {
            return;
        }

        _puppetWork = known ? animation : 0;
        Animation.ClearUpperBody(0f);

        // A timed action's clip is its own, by name, and a name no state plays cannot be told by a state: it is read from the
        // action the copy stands at.
        if (state == Igi2PlayerState.ActivateGenericTba && NearestTimedAction(position) is { ActivateAnimation.Length: > 0 } action)
        {
            if (entered)
            {
                Animation.PlayNamed(action.ActivateAnimation, 0, StateBlendSeconds);
            }

            return;
        }

        if (known)
        {
            Animation.PlayState(animation, 0, StateBlendSeconds);
            if (animation == SearchLoopClip)
            {
                Animation.LoopMain();
            }

            return;
        }

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

    // The timed action nearest the copy among those within its reach, or null.
    private Interaction.IIgi2TimedAction? NearestTimedAction(Vector3d position)
    {
        Interaction.IIgi2TimedAction? nearest = null;
        double least = double.MaxValue;
        foreach (Interaction.IIgi2Activatable candidate in Surroundings.FindActivatables(position, ActivateRadius))
        {
            if (candidate is Interaction.IIgi2TimedAction action)
            {
                Vector3d off = action.Centre - position;
                double distance = (off.X * off.X) + (off.Y * off.Y) + (off.Z * off.Z);
                if (distance < least)
                {
                    least = distance;
                    nearest = action;
                }
            }
        }

        return nearest;
    }

    // The server's copy's clip a copy at work last played.
    private int _puppetWork;

    private static readonly float[] NoChannels = new float[Humans.Igi2HumanChannels.Count];
}
