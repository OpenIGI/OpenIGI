using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Weapons;

namespace OpenIGI.Game.Igi2.Player;

/// <content>The weapon in the player's hands as the first-person view shows it.</content>
public sealed partial class Igi2HumanPlayer
{
    /// <summary>Plays a clip of the weapon in hand on the first-person model (<c>HumanPlayer_SetWeaponTypeAnim</c>, <c>igi2 0x4AC4D0</c>).</summary>
    /// <param name="request">The clip asked of the weapon.</param>
    /// <param name="time">The time into the clip to start from; from its end when it plays backwards.</param>
    /// <param name="reverse">Whether to play it backwards.</param>
    /// <remarks>
    /// <para>
    /// The clip plays at the rate the weapon gives it, negated to reverse, a frame's worth of ticks times that rate a
    /// step. A clip playing backwards starts that far before its end. The action signal's first bit, which the action
    /// states wait on, is cleared while the clip plays.
    /// </para>
    /// <para>
    /// A weapon with no such clip leaves the model's clip ended and raises the bit at once, so whatever waits on it does
    /// not wait. With nothing in hand nothing happens.
    /// </para>
    /// </remarks>
    public void SetWeaponTypeAnim(Igi2WeaponAnimRequest request, int time, bool reverse)
    {
        if (!Weapon.IsHolding)
        {
            return;
        }

        Igi2AnimController arms = View.ObjectAnimation;
        if (Weapon.GetAnim(request, View.ObjectRig, FireType) is { } anim)
        {
            float rate = reverse ? ((Fpu)anim.TimeScale * -1.0).F : anim.TimeScale;
            arms.SetAnimationAndTime(anim.Clip, rate < 0f ? anim.Clip.TotalTime - time : time);
            arms.TimeStep = Fpu.ToInt32(new Fpu(Igi2AnimController.TicksPerFrame) * rate);
            ActionSignal &= ~Igi2PlayerSignal.AnimationEnded;
        }
        else
        {
            arms.Flags |= Igi2AnimControllerBits.Ended;
            ActionSignal |= Igi2PlayerSignal.AnimationEnded;
        }
    }
}
