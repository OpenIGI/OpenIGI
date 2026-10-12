using OpenIGI.Core.Numerics;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi2.Ai;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Weapons;

namespace OpenIGI.Game.Igi2.Soldiers;

/// <content>Throwing a grenade, a close-combat blow, pressing a switch, a door, a silent death and a stand action.</content>
public sealed partial class Igi2HumanSoldier
{
    /// <summary>The row a throw at a target nearer than 20480 plays, <c>13</c>.</summary>
    public const int NearThrowRow = 13;

    /// <summary>The row a throw at a farther target plays, <c>12</c>.</summary>
    public const int FarThrowRow = 12;

    /// <summary>How near a target is for the near throw, <c>20480</c> (the float <c>0x46A00000</c>).</summary>
    public const float NearThrowRange = 20480f;

    /// <summary>The line a soldier calls as its grenade leaves the hand, <c>GV_75_01</c> (<c>igi2 0x619350</c>).</summary>
    public const string GrenadeShout = "GV_75_01";

    /// <summary>The bone a grenade leaves from, <c>"right hand"</c> (<c>igi2 0x61935C</c>).</summary>
    public const string ThrowHandBone = "right hand";

    /// <summary>The ticks a grenade's fuse runs past its flight, <c>15</c>.</summary>
    public const int GrenadeFuseMargin = 15;

    /// <summary>The main clip's time past which a silently killed body may settle again, <c>9600</c>.</summary>
    public const int SilentKillSettleTime = 9600;

    /// <summary>The bits that end a stand action, <c>0x10003E1</c>: the clip's end, a move, a shot or an aim.</summary>
    private const Igi2SoldierSignal StandActionEnds = Igi2SoldierSignal.MainEnded | Igi2SoldierSignal.StrafeLeft
        | Igi2SoldierSignal.StrafeRight | Igi2SoldierSignal.Forward | Igi2SoldierSignal.Backward | Igi2SoldierSignal.Fire
        | Igi2SoldierSignal.Aim;

    private const string SoldierTriggerTaskType = "HumanSoldier";

    /// <summary>Gets or sets a value indicating whether this throw's grenade has left the hand, <c>isThrowGrenadeTriggered</c>.</summary>
    public bool IsThrowGrenadeTriggered { get; set; }

    /// <summary>Gets or sets a value indicating whether this blow's damage was dealt, <c>isDamageDealt</c>.</summary>
    public bool IsDamageDealt { get; set; }

    /// <summary>Gets or sets the session's anim trigger types, which decide which of the clips' triggers reach the soldier.</summary>
    public Igi2AnimTriggers? AnimTriggers { get; set; }

    /// <summary>
    /// A trigger one of the soldier's clips fired, <c>HumanSoldier_TriggerAnimTriggerHandler</c> (<c>igi2 0x4E9AF0</c>): of the ids
    /// defined for the soldier's type, the grenade's release and the blow each raise their bit in the signal. Any other such id is
    /// fatal, as the original's is.
    /// </summary>
    /// <param name="trigger">The trigger.</param>
    /// <exception cref="EngineFatalException">The id is defined for the soldier's type but not one it knows.</exception>
    public void TriggerFromBody(in Igi2AnimationTrigger trigger)
    {
        if (AnimTriggers is not { } triggers || !triggers.TryGet(trigger.TriggerId, out Igi2AnimTriggerType type)
            || !triggers.IsAnsweredBy(type, SoldierTriggerTaskType))
        {
            TriggerHumanType(trigger, AnimTriggers);
            return;
        }

        Signal |= trigger.TriggerId switch
        {
            Igi2HumanAnimTrigger.ThrowGrenadeRelease => Igi2SoldierSignal.GrenadeReleased,
            Igi2HumanAnimTrigger.CloseCombatDealDamage => Igi2SoldierSignal.BlowLanded,
            _ => throw new EngineFatalException($"Unknown animtrigger for humansoldier: {trigger.TriggerId}"),
        };
    }

    /// <summary>
    /// A throw, <c>HumanSoldier_InitStateStandThrowGrenade</c> (<c>igi2 0x4E6470</c>): the grenade not yet thrown, the upper body
    /// cleared, kept off and cleared again; the near or far throw by the mind's grenade target's distance; still.
    /// </summary>
    private void EnterStandThrowGrenade()
    {
        IsThrowGrenadeTriggered = false;
        Animation.ClearUpperBody(0f);
        UpperBodyFlags |= 1;
        Animation.ClearUpperBody(0f);
        Vector3d target = Mind?.GrenadeTargetPosition ?? Position;
        Fpu dx = (Fpu)target.X - Position.X, dy = (Fpu)target.Y - Position.Y, dz = (Fpu)target.Z - Position.Z;
        Fpu distance = Fpu.Sqrt(((dx * dx) + (dy * dy)) + (dz * dz));
        PlayRow(distance.Value < NearThrowRange ? NearThrowRow : FarThrowRow, 0f);
        DeltaTranslation = default;
        IsDisableDeltaTranslation = true;
        Locomotion.Current = (_, _) => RunStandThrowGrenade();
    }

    /// <summary>
    /// A throw's tick, <c>HumanSoldier_RunStateStandThrowGrenade</c> (<c>igi2 0x4E6540</c>): when the clip's release fires, the grenade
    /// leaves the right hand as the mind aims it, its fuse its flight and fifteen ticks more, the soldier shouts, and a grenade that
    /// explodes warns its side. The clip's end stands it again, and so does a fall, a hit or a death, in that order of weight.
    /// </summary>
    /// <remarks>The clip's end outranks a fall, a hit and a death: a soldier killed as the throw ends stands first.</remarks>
    private void RunStandThrowGrenade()
    {
        Igi2SoldierSignal s = Signal;
        int next = -1;
        if ((s & Igi2SoldierSignal.Grounded) == 0)
        {
            next = Igi2SoldierState.Fall;
        }

        if ((s & Igi2SoldierSignal.Hit) != 0)
        {
            next = Igi2SoldierState.Hit;
        }

        if ((s & Igi2SoldierSignal.Dead) != 0)
        {
            next = Igi2SoldierState.Die;
        }

        if ((s & Igi2SoldierSignal.GrenadeReleased) != 0 && !IsThrowGrenadeTriggered)
        {
            Signal &= ~Igi2SoldierSignal.GrenadeReleased;
            ReleaseGrenade();
        }

        if ((s & Igi2SoldierSignal.MainEnded) != 0)
        {
            next = Igi2SoldierState.Stand;
        }
        else if (next == -1)
        {
            return;
        }

        ReleaseUpperBody();
        Go(next);
    }

    /// <summary>The grenade leaving the hand, if the mind finds a throw that reaches its target.</summary>
    private void ReleaseGrenade()
    {
        Vector3d hand = BonePosition(ThrowHandBone);
        if (Mind is not { } mind || !mind.TryAimGrenade(this, hand, out Vector3f speed, out float[] orientation, out int flyTime)
            || mind.GrenadeWeaponId is not { } weaponId || Weapon.Inventory is not { } inventory
            || inventory.Tables.WeaponTypes.FirstOrDefault(w => string.Equals(w.WeaponId, weaponId, StringComparison.Ordinal)) is not { } weaponType)
        {
            return;
        }

        Igi2GrenadeParameters parameters = weaponType.FireType(0) is { } fireType ? Igi2GrenadeParameters.Of(fireType) : default;
        bool explodes = Surroundings.ThrowGrenade(new Igi2GrenadeThrow(hand, orientation, speed, flyTime + GrenadeFuseMargin, weaponType, parameters, this));
        mind.Say(this, GrenadeShout, queueable: true);
        IsThrowGrenadeTriggered = true;
        if (explodes && mind.GrenadeTargetPosition is { } target)
        {
            SendAiEvent(new Igi2AiEvent(Igi2AiEvent.GrenadeShout, Team, 0x522, 40960f, target, default, this));
        }
    }

    /// <summary>Gets what the soldier last activated, <c>ptActivateQTaskRef</c>, or <see langword="null"/>.</summary>
    public Interaction.IIgi2Activatable? ActivateTarget { get; private set; }

    /// <summary>Gets how many times the soldier has activated something, <c>HumanSoldier+0x754</c>.</summary>
    public int ActivationCount { get; private set; }

    /// <summary>
    /// The thing the activation channel names, worked, <c>HumanSoldier_HandleActivation</c> (<c>igi2 0x4E3340</c>): a switch is
    /// pressed; a door opened or shut and the soldier passes it; a mounted gun manned; then the thing is told of a first tick of
    /// activation, and the count goes up.
    /// </summary>
    /// <remarks>An id past the level's 4096 tasks, or one naming nothing, does nothing.</remarks>
    private void HandleActivation()
    {
        if ((uint)ActivateTaskId >= 0x1000 || Surroundings.ActivatableById(ActivateTaskId) is not { } target)
        {
            return;
        }

        ActivateTarget = target;
        switch (target.Kind)
        {
            case Interaction.Igi2ActivateKind.Switch when target is Interaction.IIgi2Pressable button:
                button.Press(0);
                break;
            case Interaction.Igi2ActivateKind.Door when target is Interaction.Igi2DoorTask door:
                door.ToggleOpenBySoldier();
                Go(Igi2SoldierState.Door);
                break;
            case Interaction.Igi2ActivateKind.StationaryGun:
                Go(Igi2SoldierState.StationaryGun);
                break;
        }

        target.Activate(0);
        ActivationCount++;
    }

    /// <summary>
    /// A close-combat blow, <c>HumanSoldier_InitStateStandCloseCombat</c> (<c>igi2 0x4E6750</c>): its damage not yet dealt, its row at
    /// once, still.
    /// </summary>
    private void EnterStandCloseCombat()
    {
        IsDamageDealt = false;
        PlayCurrentState(0f);
        Locomotion.Current = (_, _) => RunStandCloseCombat();
        IsDisableDeltaTranslation = true;
        DeltaTranslation = default;
    }

    /// <summary>
    /// A blow's tick, <c>HumanSoldier_RunStateStandCloseCombat</c> (<c>igi2 0x4E67A0</c>): the clip's blow only marks itself dealt (the
    /// body hands no damage on); the clip's end stands it again.
    /// </summary>
    private void RunStandCloseCombat()
    {
        Igi2SoldierSignal s = Signal;
        if ((s & Igi2SoldierSignal.Grounded) == 0)
        {
            Go(Igi2SoldierState.Fall);
        }
        else if ((s & Igi2SoldierSignal.Hit) != 0)
        {
            Go(Igi2SoldierState.Hit);
        }
        else if ((s & Igi2SoldierSignal.Dead) != 0)
        {
            Go(Igi2SoldierState.Die);
        }
        else
        {
            if ((s & Igi2SoldierSignal.BlowLanded) != 0 && !IsDamageDealt)
            {
                Signal &= ~Igi2SoldierSignal.BlowLanded;
                IsDamageDealt = true;
            }

            if ((s & Igi2SoldierSignal.MainEnded) != 0)
            {
                Go(Igi2SoldierState.Stand);
            }
        }
    }

    /// <summary>
    /// Pressing a switch, <c>HumanSoldier_InitStateStandPressSwitch</c> (<c>igi2 0x4E9550</c>): its row at once, moving by the clip,
    /// the upper body kept off; standing once the clip ends with the upper body free (<c>RunStateStandPressSwitch</c>, <c>0x4E95A0</c>).
    /// </summary>
    private void EnterStandPressSwitch()
    {
        PlayCurrentState(0f);
        IsDisableDeltaTranslation = false;
        UpperBodyFlags |= 1;
        Animation.ClearUpperBody(0f);
        Locomotion.Current = (_, _) =>
        {
            if ((Signal & Igi2SoldierSignal.MainEnded) != 0)
            {
                ReleaseUpperBody();
                Go(Igi2SoldierState.Stand);
            }
        };
    }

    /// <summary>
    /// Through a door, <c>HumanSoldier_InitStateDoor</c> (<c>igi2 0x4E94D0</c>): its row at once, moving by the clip; standing once the
    /// clip ends (<c>RunStateDoor</c>, <c>0x4E9500</c>).
    /// </summary>
    private void EnterDoor()
    {
        PlayCurrentState(0f);
        Locomotion.Current = (_, _) =>
        {
            if ((Signal & Igi2SoldierSignal.MainEnded) != 0)
            {
                Go(Igi2SoldierState.Stand);
            }
        };

        IsDisableDeltaTranslation = false;
    }

    /// <summary>
    /// Killed silently, <c>HumanSoldier_InitStateSilentKill</c> (<c>igi2 0x4E9610</c>): its row at once, damage at the limit, heading
    /// fixed, the upper body unturned, moving by the clip; dead and settled with no blood; the trigger let go and the weapons dropped;
    /// seen by detection; the upper body kept off.
    /// </summary>
    private void EnterSilentKill()
    {
        PlayCurrentState(0f);
        Damage.Damage = Damage.MaxDamage;
        IsLockOrientation = true;
        IsRotateUpperBody = false;
        IsDisableDeltaTranslation = false;
        BoneDeltaMovement = default;
        LifeState = DeadAndStill;
        IsDeathPositionStabilized = true;
        IsBloodSpriteDisabled = true;
        IsDead = true;
        if (Weapon.Inventory is not null)
        {
            StopWeapon(onlyWhenBusy: true);
            CreateGunPickups();
        }

        Surroundings.RegisterDetectable(this);
        UpperBodyFlags |= 1;
        Animation.ClearUpperBody(0f);
        Locomotion.Current = (_, _) => RunSilentKill();
    }

    /// <summary>
    /// A silent death's tick, <c>HumanSoldier_RunStateSilentKill</c> (<c>igi2 0x4E96F0</c>): late in the clip the body may settle again,
    /// and at its end it lies dead.
    /// </summary>
    private void RunSilentKill()
    {
        Igi2SoldierSignal s = Signal;
        if (Animation.MainTime > SilentKillSettleTime)
        {
            IsDeathPositionStabilized = false;
        }

        if ((s & Igi2SoldierSignal.MainEnded) != 0)
        {
            Go(Igi2SoldierState.Dead);
        }
    }

    /// <summary>
    /// A stand action, <c>HumanSoldier_InitStateStandAction</c> (<c>igi2 0x4E8CA0</c>): the mind's action clip for the weapon in hand
    /// (the action's name, an underscore and the weapon's animation name), else for its kind (<c>rifle</c>, <c>pistol</c> or
    /// <c>shotgun</c>), else the action alone, blended in over half a second with the upper body off; with no weapon set or no such
    /// clip it stands.
    /// </summary>
    private void EnterStandAction()
    {
        Igi2WeaponTypeTask? held = HeldType;
        string? action = Mind?.StandActionAnimation;
        if (held is null || action is null || Animation.Rig is not { } rig || Weapon.BodyClips(rig) is null)
        {
            Go(Igi2SoldierState.Stand);
            return;
        }

        string kind = held.WeaponType switch
        {
            Igi2WeaponTypeKind.Gun => "rifle",
            Igi2WeaponTypeKind.Pistol => "pistol",
            Igi2WeaponTypeKind.Shotgun => "shotgun",
            _ => string.Empty,
        };
        if ((Animation.FindClip(action + "_" + held.WeaponAnimationName) ?? Animation.FindClip(action + "_" + kind) ?? Animation.FindClip(action))
            is not { } clip)
        {
            Go(Igi2SoldierState.Stand);
            return;
        }

        Animation.SetClip(clip, 0, 0.5f);
        IsRotateUpperBody = false;
        UpperBodyFlags |= 1;
        Animation.ClearUpperBody(0f);
        Locomotion.Current = (_, _) => RunStandAction();
        DeltaTranslation = default;
        IsDisableDeltaTranslation = true;
    }

    /// <summary>
    /// A stand action's tick, <c>HumanSoldier_RunStateStandAction</c> (<c>igi2 0x4E8F90</c>): it ends at the clip's end, a move, a shot
    /// or an aim, the body popped to stand where its root now is; a looping action at its clip's end plays again.
    /// </summary>
    private void RunStandAction()
    {
        Igi2SoldierSignal s = Signal;
        if ((s & Igi2SoldierSignal.Grounded) == 0)
        {
            Go(Igi2SoldierState.Fall);
        }
        else if ((s & Igi2SoldierSignal.Hit) != 0)
        {
            Go(Igi2SoldierState.Hit);
        }
        else if ((s & Igi2SoldierSignal.Dead) != 0)
        {
            Go(Igi2SoldierState.Die);
        }
        else if ((s & StandActionEnds) != 0)
        {
            Vector3d spot = new(Position.X, Position.Y, ((Fpu)RootTranslation.Z + Position.Z).Value);
            IsRotateUpperBody = true;
            ReleaseUpperBody();
            PopPosition(spot);
            Go((s & Igi2SoldierSignal.MainEnded) != 0 && IsLoopStandAnimation ? Igi2SoldierState.StandAction : Igi2SoldierState.Stand);
        }
    }
}

/// <summary>The soldier's clips fire their triggers on it (<c>HumanSoldier_TriggerAnimTriggerHandler</c>, <c>igi2 0x4E9AF0</c>).</summary>
/// <param name="soldier">The soldier.</param>
internal sealed class Igi2SoldierBodyTriggers(Igi2HumanSoldier soldier) : IIgi2AnimTriggerTarget
{
    /// <inheritdoc/>
    public void Trigger(in Igi2AnimationTrigger trigger) => soldier.TriggerFromBody(trigger);
}
