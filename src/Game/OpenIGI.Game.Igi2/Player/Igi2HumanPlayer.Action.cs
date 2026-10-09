using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Weapons;

namespace OpenIGI.Game.Igi2.Player;

/// <summary>What the player is doing with the weapon in hand, <c>HumanPlayerWeaponStatus_e</c>, while it changes.</summary>
public enum Igi2PlayerWeaponStatus
{
    /// <summary>No change under way.</summary>
    None = 0,

    /// <summary>A change has been asked of the weapon.</summary>
    Requested = 1,

    /// <summary>The change may go ahead.</summary>
    Allowed = 2,

    /// <summary>The change was refused (multiplayer only).</summary>
    Denied = 3,

    /// <summary>The new weapon is in hand (<c>Weapon_SingleActivateHandler</c>, <c>igi2 0x51D7A2</c>).</summary>
    Activated = 4,
}

/// <content>
/// The action machine: the player's second state machine, beside locomotion, for what the hands do
/// (<c>HumanPlayer_apfAction</c>, <c>igi2 0x63D794</c>).
/// </content>
/// <remarks>
/// <para>
/// Its signal is packed by <c>HumanPlayer_ActionSignalHandler</c> (<c>0x4B33E0</c>) into the same bits as the locomotion
/// signal, with one difference: the first bit is the first-person model's clip having ended, which is what a weapon
/// change waits on.
/// </para>
/// <para>
/// Ported: locked (0), idle (1), firing (2, 3, 4), reloading (5), the weapon change (6) and firing empty (9), with the
/// upper-body modes the states set (<c>HumanPlayer_SetUpperBody</c>, <c>0x4BD2E0</c>) and the weapon selection the
/// update runs before the machines. Not yet: peeking (7), the computer (8) and throwing (10), and from the idle state the
/// binoculars, activating objects and dropping the weapon. Where idle would go to one of those it stays idle.
/// </para>
/// </remarks>
public sealed partial class Igi2HumanPlayer
{
    /// <summary>How far into its clip a primary shot's first-person clip may start, at most, in seconds (<c>0x3E333333</c>).</summary>
    private const float FireStartSpread = 0.175f;

    private TaskStateMachine? _action;

    /// <summary>Gets the action machine (<c>HumanPlayer+0xBB0</c>), which starts idle.</summary>
    public TaskStateMachine Action => _action ??= CreateAction();

    /// <summary>Gets the state of the player's action machine, <c>tAction.nState</c>.</summary>
    public int ActionState => Action.CurrentState;

    /// <summary>Gets or sets the action machine's signal, <c>tAction.bSignal</c>; it shares the locomotion signal's bits.</summary>
    public Igi2PlayerSignal ActionSignal
    {
        get => (Igi2PlayerSignal)Action.Flags;
        set => Action.Flags = (uint)value;
    }

    /// <summary>Gets or sets a value indicating whether the action machine is held locked, <c>isLockAction</c>.</summary>
    public bool IsLockAction { get; set; }

    /// <summary>Gets or sets the weapon a change is under way to, by number, or −1, <c>nChangeWeaponID</c>.</summary>
    public int ChangeWeaponId { get; set; } = -1;

    /// <summary>Gets or sets where the player is in the weapon cycle, <c>nWeaponCycle</c>.</summary>
    public int WeaponCycleIndex { get; set; }

    /// <summary>Gets or sets the order weapons are cycled in, by number (<c>HumanPlayer_tWeaponCycle</c>).</summary>
    public IReadOnlyList<int> WeaponCycle { get; set; } = [];

    /// <summary>Gets or sets which fire type is in use, <c>nFireType</c>: 0 for the primary, 1 for the secondary.</summary>
    public int FireType { get; set; }

    /// <summary>Gets or sets the progress of a weapon change, <c>eWeaponStatus</c>.</summary>
    public Igi2PlayerWeaponStatus WeaponStatus { get; set; }

    /// <summary>Gets or sets a value indicating whether the weapon in hand is ready to use, <c>Human.isWeaponReady</c>.</summary>
    public bool IsWeaponReady { get; set; }

    /// <summary>Gets or sets a value indicating whether the change has asked the weapon to activate, <c>isWeaponActivateRequested</c>.</summary>
    public bool IsWeaponActivateRequested { get; set; }

    /// <summary>Gets or sets a value indicating whether the network needs telling the upper body changed, <c>isForceNetUpdate</c>.</summary>
    public bool IsForceNetUpdate { get; set; }

    /// <summary>Gets or sets a value indicating whether the player never runs out of ammunition, <c>isUnlimitedAmmo</c> (a cheat).</summary>
    public bool IsUnlimitedAmmo { get; set; }

    /// <summary>Gets or sets a value indicating whether the player stands in a bomb area, <c>isInsideBombArea</c>.</summary>
    public bool IsInsideBombArea { get; set; }

    private Igi2WeaponItem? HeldItem => Weapon.Inventory?.Current;

    private TaskStateMachine CreateAction()
    {
        var states = new TaskStateFunction?[Igi2PlayerActionState.Count];
        states[Igi2PlayerActionState.Lock] = (_, _) => EnterLock();
        states[Igi2PlayerActionState.Idle] = (_, _) => EnterIdle();
        states[Igi2PlayerActionState.InitFire] = (_, _) => EnterInitFire();
        states[Igi2PlayerActionState.Fire] = (_, _) => EnterFire();
        states[Igi2PlayerActionState.FireRelease] = (_, _) => EnterFireRelease();
        states[Igi2PlayerActionState.Reload] = (_, _) => EnterReload();
        states[Igi2PlayerActionState.ChangeWeapon] = (_, _) => EnterChangeWeapon();
        states[Igi2PlayerActionState.FireEmpty] = (_, _) => EnterFireEmpty();
        states[Igi2PlayerActionState.ThrowGrenade] = (_, _) => EnterThrowGrenade();
        return new TaskStateMachine((_, _) => PackActionSignal(), states, Igi2PlayerActionState.Idle, exits: new TaskStateFunction?[Igi2PlayerActionState.Count]);
    }

    private void GoAction(int state) => Action.Enter(state, this);

    /// <summary>Packs the action signal from the channels (<c>HumanPlayer_ActionSignalHandler</c>, <c>igi2 0x4B33E0</c>).</summary>
    /// <remarks>
    /// The carried bits are the locomotion's and the turning bit. The first bit is the first-person clip having ended.
    /// The alternate fire key does nothing with a laser designator in hand, and otherwise resets the perfect-shot timer.
    /// </remarks>
    public void PackActionSignal()
    {
        Igi2HumanChannels channels = Channels;
        Igi2PlayerSignal signal = ActionSignal & (Igi2PlayerSignal.Carried | Igi2PlayerSignal.Turning);
        if (View.ObjectAnimation.IsEnded)
        {
            signal |= Igi2PlayerSignal.AnimationEnded;
        }

        signal |= HeldBit(Igi2HumanChannel.MoveLeft, Igi2PlayerSignal.Left);
        signal |= HeldBit(Igi2HumanChannel.MoveRight, Igi2PlayerSignal.Right);
        signal |= HeldBit(Igi2HumanChannel.MoveForwards, Igi2PlayerSignal.Forward);
        signal |= HeldBit(Igi2HumanChannel.MoveBackwards, Igi2PlayerSignal.Backward);
        signal |= HeldBit(Igi2HumanChannel.Fire, Igi2PlayerSignal.Fire);
        if (channels.IsHeld(Igi2HumanChannel.AlternateFire))
        {
            if (HeldItem?.WeaponType.WeaponCategory == LaserDesignatorCategory)
            {
                channels[Igi2HumanChannel.AlternateFire] = 0f;
            }
            else
            {
                signal |= Igi2PlayerSignal.AlternateFire;
                PerfectShotTimer = 0;
            }
        }

        signal |= HeldBit(Igi2HumanChannel.Reload, Igi2PlayerSignal.Reload);
        signal |= HeldBit(Igi2HumanChannel.Activate, Igi2PlayerSignal.Activate);
        if (channels.WasPressed(Igi2HumanChannel.RaiseStance))
        {
            signal |= Igi2PlayerSignal.RaiseStance;
        }

        if (channels.WasPressed(Igi2HumanChannel.LowerStance))
        {
            signal |= Igi2PlayerSignal.LowerStance;
        }

        if (channels.WasPressed(Igi2HumanChannel.ToggleStandLie))
        {
            signal |= Pose == Igi2HumanPose.LyingFlat ? Igi2PlayerSignal.StandUp : Igi2PlayerSignal.LieDown;
        }

        signal |= HeldBit(Igi2HumanChannel.ModifyWalkRun, Igi2PlayerSignal.ModifyWalkRun);
        signal |= HeldBit(Igi2HumanChannel.Crouch, Igi2PlayerSignal.Crouch);
        signal |= HeldBit(Igi2HumanChannel.Jump, Igi2PlayerSignal.Jump);
        signal |= HeldBit(Igi2HumanChannel.Peek, Igi2PlayerSignal.Peek);
        signal |= HeldBit(Igi2HumanChannel.NextWeapon, Igi2PlayerSignal.NextWeapon);
        ActionSignal = signal;
    }

    /// <summary>Plays an upper-body mode: its body clips and its first-person clip (<c>HumanPlayer_SetUpperBody</c>, <c>igi2 0x4BD2E0</c>).</summary>
    /// <param name="mode">The mode, <c>eCurrentUpperBody</c>: 0 none, 1 stand, 2 and 3 on the move, 4 and 5 crouched, 6 and 7 firing,
    /// 8 to 10 the reload's start, loop and end, 11 putting the weapon away, 12 drawing it.</param>
    /// <remarks>
    /// <para>
    /// Mode 0 clears the upper body and the request with it. Any other notes the network. Standing or crouched, the upper
    /// body plays the stand state's clip through the weapon for modes 1 to 5, the fire state's for a primary shot, and the
    /// weapon's own clip for a secondary shot, a reload step, a put-away or a draw — a draw from its end backwards. Lying
    /// flat there is no upper body: the whole body plays the lying state's clip, or the weapon's lying reload, put-away
    /// or draw.
    /// </para>
    /// <para>
    /// The first-person model then plays the matching clip: stand for 1, 4 and 5, move for 2 and 3, fire for 6 and 7 — a
    /// primary shot starting up to 0.175 s in, drawn from the run stream — the reload steps for 8 to 10, and the draw for
    /// 11 forwards and 12 backwards.
    /// </para>
    /// </remarks>
    public void SetUpperBody(int mode)
    {
        CurrentUpperBody = mode;
        if (mode == 0)
        {
            RequestedUpperBody = 0;
            Animation.ClearUpperBody(0f);
            return;
        }

        IsForceNetUpdate = true;
        bool lying = Pose == Igi2HumanPose.LyingFlat;
        int state = lying ? 75 : 0;
        Igi2WeaponAnimRequest firstPerson = Igi2WeaponAnimRequest.FirstStand;
        int firstPersonTime = 0;
        bool reverse = false;
        bool secondaryFire = false;
        switch (mode)
        {
            case 2:
            case 3:
                firstPerson = Igi2WeaponAnimRequest.FirstMove;
                break;
            case 6:
                firstPersonTime = Fpu.ToInt32(((Fpu)Random.NextDouble() * FireStartSpread) * Igi2HumanAnimation.BlendTicksPerSecond);
                goto case 7;
            case 7:
                secondaryFire = FireType != 0;
                firstPerson = Igi2WeaponAnimRequest.FirstFire;
                state = lying ? 76 : 83;
                break;
            case 8:
                firstPerson = Igi2WeaponAnimRequest.FirstReloadStart;
                state = lying ? 77 : 84;
                break;
            case 9:
                firstPerson = Igi2WeaponAnimRequest.FirstReloadLoop;
                state = lying ? 78 : 85;
                break;
            case 10:
                firstPerson = Igi2WeaponAnimRequest.FirstReloadEnd;
                state = lying ? 79 : 86;
                break;
            case 11:
                state = lying ? 91 : 88;
                firstPerson = Igi2WeaponAnimRequest.FirstActivate;
                break;
            case 12:
                reverse = true;
                state = lying ? 90 : 87;
                firstPerson = Igi2WeaponAnimRequest.FirstActivate;
                break;
        }

        if (lying)
        {
            SetLyingUpperBody(mode, state);
        }
        else
        {
            SetStandingUpperBody(mode, state, secondaryFire);
        }

        SetWeaponTypeAnim(firstPerson, firstPersonTime, reverse);
    }

    private void SetLyingUpperBody(int mode, int state)
    {
        Animation.ClearUpperBody(0f);
        if (state is 77 or 78 or 79)
        {
            Igi2WeaponAnimRequest request = mode switch
            {
                8 => Igi2WeaponAnimRequest.ThirdReloadStartFlat,
                9 => Igi2WeaponAnimRequest.ThirdReloadLoopFlat,
                _ => Igi2WeaponAnimRequest.ThirdReloadEndFlat,
            };
            if (Weapon.GetAnim(request, Animation.Rig, FireType) is { } anim)
            {
                Animation.PlayClip(anim.Clip, 0, 0f, state);
                Animation.MainTimeStep = anim.TimeStep;
            }

            return;
        }

        if (state is not (90 or 91))
        {
            Animation.PlayState(state, 0, 0f);
            return;
        }

        Animation.LastUpperBodyState = state;
        Igi2WeaponAnimRequest drawRequest = mode == 11 ? Igi2WeaponAnimRequest.ThirdDeactivateFlat : Igi2WeaponAnimRequest.ThirdActivateFlat;
        PlayWeaponUpperBody(Weapon.GetAnim(drawRequest, Animation.Rig, FireType), backwards: mode == 12);
    }

    private void SetStandingUpperBody(int mode, int state, bool secondaryFire)
    {
        if (secondaryFire)
        {
            if (Weapon.GetAnim(Igi2WeaponAnimRequest.ThirdFire, Animation.Rig, FireType) is { } fire)
            {
                Animation.LastUpperBodyState = 83;
                Animation.SetUpperBody(fire.Clip, 0, 0f);
            }
            else
            {
                Animation.ClearUpperBody(0f);
            }

            return;
        }

        Igi2WeaponAnimRequest? request = mode switch
        {
            8 => Igi2WeaponAnimRequest.ThirdReloadStart,
            9 => Igi2WeaponAnimRequest.ThirdReloadLoop,
            10 => Igi2WeaponAnimRequest.ThirdReloadEnd,
            11 => Igi2WeaponAnimRequest.ThirdDeactivate,
            12 => Igi2WeaponAnimRequest.ThirdActivate,
            _ => null,
        };
        if (request is not { } asked)
        {
            Animation.PlayUpperBodyState(state, 0, 0f);
            return;
        }

        Animation.LastUpperBodyState = state;
        PlayWeaponUpperBody(Weapon.GetAnim(asked, Animation.Rig, FireType), backwards: mode == 12);
    }

    /// <summary>Plays a weapon's clip on the upper body at its rate, a draw from its end backwards; none clears the upper body.</summary>
    private void PlayWeaponUpperBody(Igi2WeaponAnim? anim, bool backwards)
    {
        if (anim is not { } clip)
        {
            Animation.ClearUpperBody(0f);
            return;
        }

        if (backwards)
        {
            Animation.SetUpperBody(clip.Clip, clip.Clip.TotalTime, 0f);
            Animation.UpperBodyTimeStep = Fpu.ToInt32((Fpu)(-clip.TimeScale) * Igi2AnimController.TicksPerFrame);
        }
        else
        {
            Animation.SetUpperBody(clip.Clip, 0, 0f);
            Animation.UpperBodyTimeStep = clip.TimeStep;
        }
    }

    /// <summary>Re-aims the precision for the posture already selected (<c>HumanPlayer_SetWeaponPrecision</c>, <c>igi2 0x49F5F0</c>).</summary>
    public void SetWeaponPrecision() => AimPrecision(PrecisionType);

    /// <summary>Locked: the upper body is let go, unless planting a charge (<c>HumanPlayer_InitActionLock</c>, <c>igi2 0x4B3630</c>).</summary>
    private void EnterLock()
    {
        if (Locomotion.CurrentState != Igi2PlayerState.ActivateC4BombTba)
        {
            CurrentUpperBody = 0;
            RequestedUpperBody = 0;
            Animation.ClearUpperBody(0f);
        }

        Action.Current = (_, _) => RunLock();
    }

    /// <summary>Waits for the lock to lift, then goes idle (<c>HumanPlayer_RunActionLock</c>, <c>igi2 0x4B3680</c>).</summary>
    /// <remarks>Not yet: activating an object while swimming, which the lock lets through.</remarks>
    private void RunLock()
    {
        if (!IsLockAction)
        {
            GoAction(Igi2PlayerActionState.Idle);
        }
    }

    /// <summary>Idle: the stand mode on the upper body (<c>HumanPlayer_InitActionIdle</c>, <c>igi2 0x4B3760</c>).</summary>
    private void EnterIdle()
    {
        SetUpperBody(1);
        Action.Current = (_, _) => RunIdle();
    }

    /// <summary>Idle, ready for the next action (<c>HumanPlayer_RunActionIdle</c>, <c>igi2 0x4B3E40</c>).</summary>
    /// <remarks>
    /// <para>
    /// The upper body follows the mode the locomotion asks for, except that the two crouched modes swap without a new clip.
    /// The precision is re-aimed. A lock takes the zoom off and locks.
    /// </para>
    /// <para>
    /// A spent throwable in hand — a proximity mine or the RPG-18 with nothing left to fire, or a grenade that went off —
    /// forces the next weapon in the cycle. A change asked for starts with the zoom taken off.
    /// </para>
    /// <para>
    /// With a weapon in hand the fire keys fire (<see cref="TryFireFromIdle"/>) and the reload key reloads.
    /// </para>
    /// <para>
    /// With nothing to change to, the activate key activates what the view has found (<see cref="TryActivateFromIdle"/>).
    /// </para>
    /// <para>
    /// Not yet: the binoculars, peeking, the computer and activating anything but a switch; throwing, whose state is not ported, so
    /// where idle would throw it stays idle; and dropping the weapon.
    /// </para>
    /// </remarks>
    private void RunIdle()
    {
        if (CurrentUpperBody != RequestedUpperBody)
        {
            if ((CurrentUpperBody == 4 && RequestedUpperBody == 5) || (CurrentUpperBody == 5 && RequestedUpperBody == 4))
            {
                CurrentUpperBody = RequestedUpperBody;
            }
            else
            {
                SetUpperBody(RequestedUpperBody);
            }
        }

        Igi2WeaponItem? held = HeldItem;
        SetWeaponPrecision();
        if (IsLockAction)
        {
            View.DisableZoomMode();
            GoAction(Igi2PlayerActionState.Lock);
            return;
        }

        if (held is not null && Weapon.Inventory is { } inventory
            && (held.WeaponType.WeaponType == Igi2WeaponTypeKind.ProximityMine || Igi2Inventory.IsRpg18(held.WeaponType)))
        {
            bool spent = inventory.FireTypeRounds(held, 0).Available == 0
                && (!Igi2Inventory.IsRpg18(held.WeaponType) || held.Loaded(0) == 0);
            if (spent || inventory.Status == Igi2WeaponStatus.ExplodedInHand)
            {
                SelectWeapon(forceCycle: true);
            }
        }

        CheckBinoculars(forceChangeWeapon: false);
        if (held is not null && TryFireFromIdle(held))
        {
            return;
        }

        if (ChangeWeaponId == -1)
        {
            TryActivateFromIdle();
            IsInsideBombArea = false;
            return;
        }

        View.DisableZoomMode();
        GoAction(Igi2PlayerActionState.ChangeWeapon);
    }

    /// <summary>The idle state's fire branch, <c>HumanPlayer_RunActionIdle</c> (<c>igi2 0x4B4067</c>…<c>0x4B4347</c>).</summary>
    /// <param name="held">The weapon in hand.</param>
    /// <returns><see langword="true"/> when the idle state goes no further this tick.</returns>
    /// <remarks>
    /// <para>
    /// The activate key in a bomb area with the C4 in hand fires it. With neither fire key held the reload is checked.
    /// Otherwise the alternate key picks the secondary fire type; a fire type that uses ammunition with nothing loaded
    /// reloads when there is more carried, or fires empty — unless the weapon waits on the server. A slot with no fire
    /// type, the binoculars and the thermal goggles check the reload instead. A grenade with no delay is thrown when fire
    /// is held or its pin is still in. Anything else starts firing.
    /// </para>
    /// <para>
    /// The reload check is the primary's: the reload key with less than a load in and more carried, or ammunition that
    /// never runs out, reloads.
    /// </para>
    /// </remarks>
    private bool TryFireFromIdle(Igi2WeaponItem held)
    {
        Igi2WeaponTypeTask weaponType = held.WeaponType;
        if (string.Equals(weaponType.WeaponId, C4BombWeaponId, StringComparison.Ordinal) && IsInsideBombArea
            && Channels[Igi2HumanChannel.Activate] > 0f)
        {
            Channels[Igi2HumanChannel.Fire] = 1f;
        }

        if (Channels[Igi2HumanChannel.Fire] == 0f && Channels[Igi2HumanChannel.AlternateFire] == 0f)
        {
            return WantsReload(held);
        }

        FireType = Channels[Igi2HumanChannel.AlternateFire] != 0f ? 1 : 0;
        Igi2Inventory inventory = Weapon.Inventory!;
        Igi2FireTypeRounds rounds = inventory.FireTypeRounds(held, FireType);
        if (rounds.AmmoType is not null && rounds.Loaded == 0 && inventory.Status != Igi2WeaponStatus.Waiting)
        {
            GoAction(rounds.Available != 0 || IsUnlimitedAmmo ? Igi2PlayerActionState.Reload : Igi2PlayerActionState.FireEmpty);
            return true;
        }

        if (weaponType.FireTaskType(FireType) == Igi2WeaponConstants.NoTaskType)
        {
            return WantsReload(held);
        }

        if (weaponType.WeaponType == Igi2WeaponTypeKind.Grenade)
        {
            if (inventory.Delay == 0 && (Channels[Igi2HumanChannel.Fire] != 0f || !inventory.IsPinPulled))
            {
                GoAction(Igi2PlayerActionState.ThrowGrenade);
                return true;
            }

            return WantsReload(held);
        }

        if (weaponType.WeaponCategory is Igi2WeaponCategory.Binoculars or Igi2WeaponCategory.ThermoChemical)
        {
            return WantsReload(held);
        }

        GoAction(Igi2PlayerActionState.InitFire);
        return true;
    }

    /// <summary>The idle state's reload check, on the primary fire type: the reload key with less than a load in and more to hand.</summary>
    /// <returns><see langword="true"/> when it reloads.</returns>
    private bool WantsReload(Igi2WeaponItem held)
    {
        if ((ActionSignal & Igi2PlayerSignal.Reload) == 0)
        {
            return false;
        }

        Igi2FireTypeRounds rounds = Weapon.Inventory!.FireTypeRounds(held, 0);
        if (rounds.Loaded == rounds.RoundsPerClip || (rounds.Available == 0 && !IsUnlimitedAmmo))
        {
            return false;
        }

        GoAction(Igi2PlayerActionState.Reload);
        return true;
    }

    /// <summary>
    /// Starts a change: a change sound, the put-away on the upper body and the first-person model
    /// (<c>HumanPlayer_InitActionChangeWeapon</c>, <c>igi2 0x4B7180</c>).
    /// </summary>
    /// <remarks>
    /// One draw from the run stream picks one of the four <c>changeweapon_0N</c> sounds; it is drawn whatever is in hand,
    /// and the sound and the put-away play only for a weapon still carried. The weapon stops being ready. Not yet: the AI
    /// hearing the weapon handled (<c>HumanPlayer_SendHandleWeaponAIEvent</c>, a range of 20480).
    /// </remarks>
    private void EnterChangeWeapon()
    {
        uint draw = Random.NextRaw();
        if (HeldItem is { IsActive: true })
        {
            Surroundings.PlaySound($"changeweapon_0{(draw & 3) + 1}");
            SetUpperBody(11);
        }

        IsWeaponReady = false;
        WeaponStatus = Igi2PlayerWeaponStatus.None;
        Action.Current = (_, _) => RunChangeWeapon();
    }

    /// <summary>
    /// Once the put-away has played, or the weapon is no longer carried, puts the new weapon in hand and draws it
    /// (<c>HumanPlayer_RunActionChangeWeapon</c>, <c>igi2 0x4B75C0</c>).
    /// </summary>
    /// <remarks>Not yet: dropping the weapon put away as a pickup, which a drop key asks for.</remarks>
    private void RunChangeWeapon()
    {
        Igi2WeaponItem? held = HeldItem;
        if ((ActionSignal & Igi2PlayerSignal.AnimationEnded) == 0 && (held is null || held.IsActive))
        {
            return;
        }

        Igi2Inventory? inventory = Weapon.Inventory;
        Igi2WeaponItem? next = inventory?.WeaponByNumber(ChangeWeaponId);
        if (next is not null && WeaponStatus == Igi2PlayerWeaponStatus.None)
        {
            WeaponStatus = Igi2PlayerWeaponStatus.Allowed;
        }

        if (WeaponStatus == Igi2PlayerWeaponStatus.Allowed && !IsWeaponActivateRequested)
        {
            if (next is not null && Weapon.Activate(next))
            {
                WeaponStatus = Igi2PlayerWeaponStatus.Activated;
            }

            IsWeaponActivateRequested = true;
        }

        if (WeaponStatus is not (Igi2PlayerWeaponStatus.Activated or Igi2PlayerWeaponStatus.Denied))
        {
            return;
        }

        IsWeaponActivateRequested = false;
        WeaponStatus = Igi2PlayerWeaponStatus.None;
        ChangeWeaponId = -1;
        SetUpperBody(12);
        SetWeaponPrecision();
        Action.Current = (_, _) => RunChangeWeaponDrawn();
    }

    /// <summary>Waits for the draw to play, then the weapon is ready and the player idle (<c>HumanPlayer_RunActionChangeWeapon2</c>, <c>igi2 0x4B7BE0</c>).</summary>
    private void RunChangeWeaponDrawn()
    {
        if ((ActionSignal & Igi2PlayerSignal.AnimationEnded) != 0)
        {
            IsWeaponReady = true;
            GoAction(Igi2PlayerActionState.Idle);
        }
    }

    /// <summary>Turns a weapon-category key into a weapon to change to (<c>HumanPlayer_SelectWeaponFromCategory</c>, <c>igi2 0x4A0140</c>).</summary>
    /// <remarks>
    /// <para>
    /// The keys pressed this tick pick a category, the last in key order winning: 1, 2, 3, 4, 9, 5, 10, then 8. The grenade key
    /// steps from the category of the weapon shown through grenades, flashbangs and smoke grenades, three tries, to the first
    /// with one carried.
    /// </para>
    /// <para>
    /// The weapon is the next carried one of that category after the weapon shown, when that one is of the category, and
    /// otherwise the first; it goes into <see cref="Igi2HumanChannel.SelectWeapon"/> as its number.
    /// </para>
    /// </remarks>
    public void SelectWeaponFromCategory()
    {
        int category = 0;
        if (Channels.WasPressed(Igi2HumanChannel.WeaponCategory1))
        {
            category = 1;
        }

        category = CategoryKey(Igi2HumanChannel.WeaponCategory1 + 1, 2, category);
        category = CategoryKey(Igi2HumanChannel.WeaponCategory1 + 2, 3, category);
        category = CategoryKey(Igi2HumanChannel.WeaponCategory1 + 3, 4, category);
        category = CategoryKey(Igi2HumanChannel.WeaponCategory1 + 4, 9, category);
        category = CategoryKey(Igi2HumanChannel.WeaponCategory1 + 5, 5, category);
        category = CategoryKey(Igi2HumanChannel.WeaponCategory1 + 6, 10, category);
        category = CategoryKey(Igi2HumanChannel.WeaponCategory9, 8, category);
        if (category == 0 || Weapon.Inventory is not { } inventory)
        {
            return;
        }

        Igi2WeaponTypeTask? shown = inventory.StatusDisplay?.WeaponType;
        if (shown is not null)
        {
            if (category == 4)
            {
                int step = shown.WeaponCategory;
                for (int tries = 0; tries < 3; tries++)
                {
                    step = step switch
                    {
                        4 => 6,
                        6 => 7,
                        _ => 4,
                    };
                    if (inventory.NextInCategory(step, null) is not null)
                    {
                        category = step;
                        break;
                    }
                }
            }

            if (shown.WeaponCategory != category)
            {
                shown = null;
            }
        }

        if ((inventory.NextInCategory(category, shown) ?? inventory.NextInCategory(category, null)) is { } item)
        {
            Channels[Igi2HumanChannel.SelectWeapon] = item.Number;
        }
    }

    /// <summary>
    /// Asks for a change to the weapon a category key picked, or steps the weapon cycle
    /// (<c>HumanPlayer_SelectWeapon</c>, <c>igi2 0x4A0350</c>).
    /// </summary>
    /// <param name="forceCycle">Whether to step on even without the cycle keys.</param>
    /// <remarks>
    /// <para>
    /// A weapon picked by number is taken if it may be selected, and the cycle moves to its place. Otherwise the cycle
    /// steps forwards on the next-weapon key, when forced or when it has no place, and back on the previous-weapon key;
    /// from the weapon shown, it wraps and takes the first weapon that may be selected, trying each entry once.
    /// </para>
    /// <para>
    /// A weapon other than the one shown becomes the change asked for. Not yet: going back to the weapon held before the
    /// binoculars, and taking the overlay off.
    /// </para>
    /// </remarks>
    public void SelectWeapon(bool forceCycle)
    {
        if (Weapon.Inventory is not { } inventory)
        {
            return;
        }

        int shownId = inventory.StatusDisplay?.Number ?? -1;
        int pick = Fpu.ToInt32(Channels[Igi2HumanChannel.SelectWeapon]);
        if (inventory.WeaponByNumber(pick) is { } picked)
        {
            if (inventory.IsSelectable(picked))
            {
                for (int i = 0; i < WeaponCycle.Count; i++)
                {
                    if (WeaponCycle[i] == pick)
                    {
                        WeaponCycleIndex = i;
                    }
                }
            }
            else
            {
                pick = -1;
            }
        }

        if (pick == -1)
        {
            pick = StepCycle(inventory, forceCycle);
        }

        if (pick != shownId && inventory.WeaponByNumber(pick) is not null)
        {
            ChangeWeaponId = pick;
        }
    }

    private int StepCycle(Igi2Inventory inventory, bool forceCycle)
    {
        int step = WeaponCycleIndex == -1 || forceCycle || Channels.WasPressed(Igi2HumanChannel.NextWeapon) ? 1 : 0;
        if (Channels.WasPressed(Igi2HumanChannel.PrevWeapon))
        {
            step--;
        }

        if (step == 0)
        {
            return -1;
        }

        if (inventory.StatusDisplay is { } shown)
        {
            WeaponCycleIndex = 0;
            for (int i = 0; i < WeaponCycle.Count && WeaponCycle[WeaponCycleIndex] != shown.Number; i++)
            {
                WeaponCycleIndex++;
            }
        }

        for (int left = WeaponCycle.Count; left > 0; left--)
        {
            WeaponCycleIndex += step;
            if (WeaponCycleIndex >= WeaponCycle.Count)
            {
                WeaponCycleIndex = 0;
            }
            else if (WeaponCycleIndex < 0)
            {
                WeaponCycleIndex = WeaponCycle.Count - 1;
            }

            int candidate = WeaponCycle[WeaponCycleIndex];
            if (inventory.WeaponByNumber(candidate) is { } item && inventory.IsSelectable(item))
            {
                return candidate;
            }
        }

        return -1;
    }

    private int CategoryKey(Igi2HumanChannel channel, int category, int current) =>
        Channels.WasPressed(channel) ? category : current;

    private Igi2PlayerSignal HeldBit(Igi2HumanChannel channel, Igi2PlayerSignal bit) =>
        Channels.IsHeld(channel) ? bit : Igi2PlayerSignal.None;
}
