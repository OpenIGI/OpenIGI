using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Humans;

namespace OpenIGI.Game.Igi2.Player;

/// <content>
/// The tilt of the player's spine with its view: the bone callbacks <c>HumanPlayer_UpdateInternalDataHandler</c>
/// (<c>igi2 0x4B9850</c>) sets on the lower back, upper back and shoulders.
/// </content>
/// <remarks>
/// The lower back runs first. With an upper-body clip playing it swaps its parent's turn for the clip's root key, as a soldier's
/// does. Then it eases the view's shoulder pitch a fifth of the way toward the view's pitch (toward level unless the player
/// stands) and tilts by a share of it; the upper back and the shoulders tilt by their own shares of that pitch. A dead player's
/// spine is left as its clips pose it.
/// </remarks>
public sealed partial class Igi2HumanPlayer : IIgi2BoneCallbacks
{
    /// <summary>The empty slot of the player's state table its lower back also treats as standing, <c>83</c>.</summary>
    private const int UnusedStandingState = 83;

    /// <summary>The share of the way the shoulder pitch eases each update, the float <c>0x3E4CCCCD</c>.</summary>
    private const float ShoulderPitchEase = 0.2f;

    /// <inheritdoc/>
    protected override IIgi2BoneCallbacks? BoneCallbacks => this;

    /// <inheritdoc/>
    public void Run(Igi2BoneBuffer bones, int bone, int parent)
    {
        ArgumentNullException.ThrowIfNull(bones);
        if (Model is not { } model || IsDead)
        {
            return;
        }

        if (bone == model.ShouldersBone)
        {
            Tilt(bones, bone, 0.24705882f, 0.17777778f);
        }
        else if (bone == model.UpperBodyBone)
        {
            Tilt(bones, bone, 0.33529413f, 0.31111112f);
        }
        else if (bone == model.LowerBodyBone)
        {
            TurnLowerBack(bones, bone, parent);
        }
    }

    /// <summary><c>HumanPlayer_LowerBackCallback</c> (<c>igi2 0x4B93F0</c>).</summary>
    private void TurnLowerBack(Igi2BoneBuffer bones, int bone, int parent)
    {
        if (Animation is Igi2HumanAnimation { UpperBody: { } upper } && upper.ActiveRotationKey(0) is { } key)
        {
            Igi2BoneMath.SwapParentTurn(bones, bone, parent, key.Rotation, key.Ops);
        }

        int state = Locomotion.CurrentState;
        float target = state is UnusedStandingState or Igi2PlayerState.Stand ? View.Pitch : 0f;
        View.ShouldersPitch = ((((Fpu)target - View.ShouldersPitch) * ShoulderPitchEase) + View.ShouldersPitch).F;
        Tilt(bones, bone, 0.32352942f, 0.5f);
    }

    /// <summary>
    /// <c>HumanPlayer_ShouldersCallback</c> (<c>igi2 0x4B9110</c>) and <c>UpperBackCallback</c> (<c>0x4B9280</c>), and the lower back's
    /// last step: the bone tilted by one share of the shoulder pitch upward, another downward.
    /// </summary>
    private void Tilt(Igi2BoneBuffer bones, int bone, float upShare, float downShare)
    {
        float pitch = View.ShouldersPitch;
        Igi2BoneMath.TiltAboutX(bones, bone, (Fpu)pitch * (pitch >= 0f ? upShare : downShare));
    }
}
