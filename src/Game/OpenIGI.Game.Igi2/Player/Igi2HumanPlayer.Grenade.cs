using OpenIGI.Core.Diagnostics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Weapons;

namespace OpenIGI.Game.Igi2.Player;

/// <content>Throwing a grenade, and the anim triggers its clips and the body's fire on the player.</content>
public sealed partial class Igi2HumanPlayer
{
    /// <summary>The tick of the throw the pin is pulled on, after <c>nGrenadeTickTime</c> has counted to it.</summary>
    public const int PullPinTick = 7;

    /// <summary>How far the first-person throw has to have played before the last grenade's throw changes weapon.</summary>
    public const int ThrowChangeTime = 6400;

    /// <summary>The sound the hold point plays as the throw first holds, <c>grenade_handle</c> (<c>igi2 0x61CCDC</c>).</summary>
    public const string GrenadeHandleSound = "grenade_handle";

    /// <summary>The sound the throw plays as the fire key comes up at the hold point, <c>grenade_throw</c> (<c>igi2 0x61CCCC</c>).</summary>
    public const string GrenadeThrowSound = "grenade_throw";

    /// <summary>The name of the task type the player's anim triggers are defined for.</summary>
    private const string TriggerTaskType = "HumanPlayer";

    /// <summary>Gets or sets the ticks the throw has run, <c>nGrenadeTickTime</c>.</summary>
    public int GrenadeTickTime { get; set; }

    /// <summary>Gets or sets the session's anim trigger types, or <see langword="null"/> for none: then no trigger does anything.</summary>
    public Igi2AnimTriggers? AnimTriggers { get; set; }

    /// <summary>
    /// A trigger one of the body's clips fired, <c>HumanPlayer_TriggerAnimTriggerHandler</c> (<c>igi2 0x4B7F10</c>) with the
    /// player as the object the clip plays on.
    /// </summary>
    /// <param name="trigger">The trigger.</param>
    /// <remarks>
    /// Only an id defined for the player's own type does anything: opening and closing a drawer, and the hold point, each
    /// set their bit in the locomotion's signal; the throw does nothing on the body. Any other id defined for the type is
    /// fatal, as the original's is.
    /// </remarks>
    /// <exception cref="EngineFatalException">The id is defined for the player's type but not one it knows.</exception>
    public void TriggerFromBody(in Igi2AnimationTrigger trigger)
    {
        if (!IsPlayerTrigger(trigger.TriggerId))
        {
            return;
        }

        switch (trigger.TriggerId)
        {
            case Igi2HumanAnimTrigger.OpenDrawer:
                Signal |= Igi2PlayerSignal.OpenDrawer;
                break;
            case Igi2HumanAnimTrigger.CloseDrawer:
                Signal |= Igi2PlayerSignal.CloseDrawer;
                break;
            case Igi2HumanAnimTrigger.ThrowGrenade:
                break;
            case Igi2HumanAnimTrigger.StopAnimation:
                Signal |= Igi2PlayerSignal.StopAnimation;
                break;
            default:
                throw new EngineFatalException($"Unknown animtrigger for humanplayer: {trigger.TriggerId}");
        }
    }

    /// <summary>
    /// A trigger the first-person clip fired, <c>HumanPlayer_TriggerAnimTriggerHandler</c> (<c>igi2 0x4B7F10</c>) with the
    /// view as the object the clip plays on and the player as its parent.
    /// </summary>
    /// <param name="trigger">The trigger.</param>
    /// <remarks>
    /// The throw fires the weapon at the end of this tick's update; the hold point sets its bit in the action's signal. No
    /// other id does anything on the view.
    /// </remarks>
    public void TriggerFromView(in Igi2AnimationTrigger trigger)
    {
        if (!IsPlayerTrigger(trigger.TriggerId))
        {
            return;
        }

        if (trigger.TriggerId == Igi2HumanAnimTrigger.ThrowGrenade)
        {
            IsFire = true;
        }
        else if (trigger.TriggerId == Igi2HumanAnimTrigger.StopAnimation)
        {
            ActionSignal |= Igi2PlayerSignal.StopAnimation;
        }
    }

    /// <summary>Whether a trigger's id is defined for the player's type, which is what sends it to this handler.</summary>
    private bool IsPlayerTrigger(int id) =>
        AnimTriggers is { } triggers && triggers.TryGet(id, out Igi2AnimTriggerType type) && triggers.IsAnsweredBy(type, TriggerTaskType);

    /// <summary>
    /// Starts a throw, <c>HumanPlayer_InitActionThrowGrenade</c> (<c>igi2 0x4B4D60</c>): not lying flat, where the player goes
    /// back to idle. The hold and the tick count start again, the primary fire type is the one thrown, and the upper body and
    /// the first-person model play the fire (<see cref="SetUpperBody"/> mode 7).
    /// </summary>
    private void EnterThrowGrenade()
    {
        if (Pose == Igi2HumanPose.LyingFlat)
        {
            GoAction(Igi2PlayerActionState.Idle);
            return;
        }

        GrenadeHeld = 0;
        GrenadeTickTime = 0;
        FireType = 0;
        SetUpperBody(7);
        Action.Current = (_, _) => RunThrowGrenade();
    }

    /// <summary>One tick of a throw, <c>HumanPlayer_RunActionThrowGrenade</c> (<c>igi2 0x4B4880</c>).</summary>
    /// <remarks>
    /// <para>
    /// On its eighth tick the pin is pulled (the secondary fire type), its trigger let go and the weapon left ready, with the
    /// primary the fire type again. At the clip's hold point, while the fire key is down, the first-person clip and the upper
    /// body stand still and the hold counts up, the first tick of it to the sound of the grenade handled; let go, they play on
    /// to the sound of the throw and the hold point is passed. The clip's throw trigger then fires the primary, which throws.
    /// </para>
    /// <para>
    /// Held at the hold point until it went off in hand, or played past its throw with nothing more of it carried, the throw
    /// turns into a change: the next weapon of the cycle goes in hand and is drawn from its end backwards, the hold point
    /// forgotten. Once the first-person clip ends the primary's trigger is let go, the weapon is ready and the player idle.
    /// </para>
    /// </remarks>
    private void RunThrowGrenade()
    {
        Igi2PlayerSignal signal = ActionSignal;
        bool forceChange = false;
        Igi2AnimController firstPerson = View.ObjectAnimation;
        if (GrenadeTickTime++ == PullPinTick)
        {
            Weapon.Fire(1);
            if (Weapon.Firing is { } firing)
            {
                firing.FireTypeState(1).IsReleaseFire = true;
            }

            if (Weapon.Inventory is { } inventory)
            {
                inventory.Status = Igi2WeaponStatus.Ready;
            }

            FireType = 0;
        }

        if ((signal & Igi2PlayerSignal.StopAnimation) != 0)
        {
            if (GrenadeHeld == 0)
            {
                Surroundings.PlaySound(GrenadeHandleSound);
            }

            if (Channels[Igi2HumanChannel.Fire] == 0f)
            {
                Surroundings.PlaySound(GrenadeThrowSound);
                firstPerson.TimeStep = Igi2AnimController.TicksPerFrame;
                Animation.UpperBodyTimeStep = Igi2AnimController.TicksPerFrame;
                ActionSignal &= ~Igi2PlayerSignal.StopAnimation;
            }
            else
            {
                firstPerson.TimeStep = 0;
                Animation.UpperBodyTimeStep = 0;
                GrenadeHeld++;
                if (Weapon.Inventory?.Status == Igi2WeaponStatus.ExplodedInHand)
                {
                    forceChange = true;
                }
            }
        }

        if (forceChange
            || (firstPerson.Time > ThrowChangeTime && Weapon.Inventory is { Current: { } held } carried && carried.FireTypeRounds(held, 0).Available == 0))
        {
            ActionSignal &= ~Igi2PlayerSignal.StopAnimation;
            SelectWeapon(forceCycle: true);
            Igi2WeaponItem? next = Weapon.Inventory?.WeaponByNumber(ChangeWeaponId);
            ChangeWeaponId = -1;
            if (next is not null)
            {
                Weapon.Activate(next);
            }

            SetUpperBody(12);
            SetWeaponPrecision();
            Action.Current = (_, _) => RunChangeWeaponDrawn();
        }

        if ((signal & Igi2PlayerSignal.AnimationEnded) != 0)
        {
            if (Weapon.Firing is { } firing)
            {
                firing.FireTypeState(0).IsReleaseFire = true;
            }

            if (Weapon.Inventory is { } inventory)
            {
                inventory.Status = Igi2WeaponStatus.Ready;
            }

            GoAction(Igi2PlayerActionState.Idle);
        }
    }
}

/// <summary>The body's clips fire their triggers on the player (<c>HumanPlayer_TriggerAnimTriggerHandler</c>, <c>igi2 0x4B7F10</c>).</summary>
/// <param name="player">The player.</param>
internal sealed class Igi2PlayerBodyTriggers(Igi2HumanPlayer player) : IIgi2AnimTriggerTarget
{
    /// <inheritdoc/>
    public void Trigger(in Igi2AnimationTrigger trigger) => player.TriggerFromBody(trigger);
}

/// <summary>The first-person clip fires its triggers on the view, whose parent is the player.</summary>
/// <param name="player">The player.</param>
internal sealed class Igi2PlayerViewTriggers(Igi2HumanPlayer player) : IIgi2AnimTriggerTarget
{
    /// <inheritdoc/>
    public void Trigger(in Igi2AnimationTrigger trigger) => player.TriggerFromView(trigger);
}
