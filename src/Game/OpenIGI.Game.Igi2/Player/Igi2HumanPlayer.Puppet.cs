using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi2.Player;

/// <content>A co-op client's copy of another player: posed by the server's, its own controls never run.</content>
public sealed partial class Igi2HumanPlayer
{
    /// <summary>
    /// One tick of a player another game runs: put in the locomotion state the server's copy is in, its clip played on, and
    /// stood where the server's stands, facing and looking as it does.
    /// </summary>
    /// <param name="state">The server's copy's locomotion state.</param>
    /// <param name="position">Where its feet stand, in world units.</param>
    /// <param name="speed">Its speed.</param>
    /// <param name="gamma">Its heading.</param>
    /// <param name="pitch">Its view's pitch.</param>
    /// <remarks>
    /// <para>
    /// The locomotion states are never ticked here, as a soldier's puppet's are not (<see cref="Soldiers.Igi2HumanSoldier"/>): a
    /// state's tick reads the controls, and a copy has none. Ticked with empty controls, every gait let go of its keys and went
    /// to standing, the next snapshot put it back in the gait, and the gait's clip started over from its first frame -- the
    /// body slid over the ground in one frozen frame.
    /// </para>
    /// <para>
    /// The action states still run, on empty controls, so the upper body takes up the clip the state asks for. A run's clip
    /// steps at the rate its tick would set (<see cref="Humans.Igi2Human.ControlGait"/>); every other state keeps what its entry set.
    /// </para>
    /// </remarks>
    public void PoseAsPuppet(int state, Vector3d position, Vector3f speed, float gamma, float pitch)
    {
        if (Locomotion.CurrentState != state)
        {
            Locomotion.Enter(state, this);
        }

        if (Array.Find(Gaits, g => g.State == state) is { TimeScale: float timeScale })
        {
            ControlGait(timeScale);
        }

        Channels.Accept(NoChannels);
        Action.Tick(this);
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
    }

    private static readonly float[] NoChannels = new float[Humans.Igi2HumanChannels.Count];
}
