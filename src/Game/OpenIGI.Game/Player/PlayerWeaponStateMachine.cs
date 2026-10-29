using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.Player;

/// <summary>
/// The player's weapon-action machine — the thirteen <see cref="HumanWeaponState"/>s at
/// <c>Human+3320</c>, run by <c>0x4EC1E0</c> over <c>off_538678</c>.
/// </summary>
/// <remarks>
/// <para>
/// The middle of the three pieces <see cref="PlayerWeapons"/> runs each tick: the
/// <see cref="PlayerWeaponSelector"/> has already decided whether a change is pending, this machine
/// decides what the human is doing and raises the trigger byte, and the owner's
/// <see cref="PlayerWeapons.ResolveFire"/> turns that byte into a message to the gun afterwards.
/// </para>
/// <para>
/// It reads the gun in hand, the loose ammunition, the view sway and last tick's round through its
/// owner, and the pending weapon through the selector; everything the states write lives here.
/// </para>
/// </remarks>
internal sealed class PlayerWeaponStateMachine
{
    private readonly PlayerWeapons _owner;
    private readonly PlayerWeaponSelector _selector;
    private bool _wantFire;
    private bool _wantAlternateFire;
    private bool _reloadStarting;
    private int _throwChargeTicks;
    private bool _throwReleased;
    private bool _placingFirePending;
    private bool _actionComplete;
    private bool _medipackUsed;
    private bool _medipackAwaitingRelease;
    private bool _medipackInjectionLoopStarted;
    private bool _medipackClosing;
    private bool _medipackCloseAdvanced;
    private bool _medipackTerminalClipMissing;
    private int _medipackCooldownTicks;
    private WeaponAction _locomotionAction = WeaponAction.Stand;
    private bool _swapped;

    /// <summary>Initializes a new instance of the <see cref="PlayerWeaponStateMachine"/> class.</summary>
    /// <param name="owner">The inventory whose gun, ammunition and sway the states read.</param>
    /// <param name="selector">The selection that holds the pending weapon.</param>
    internal PlayerWeaponStateMachine(PlayerWeapons owner, PlayerWeaponSelector selector)
    {
        _owner = owner;
        _selector = selector;
    }

    /// <summary>Gets the state the machine is in.</summary>
    internal HumanWeaponState State { get; private set; } = HumanWeaponGraph.InitialState;

    /// <summary>Gets the state the machine was in before the current one.</summary>
    internal HumanWeaponState PreviousState { get; private set; } = HumanWeaponGraph.InitialState;

    /// <summary>Gets the flag word the prologue last built.</summary>
    internal HumanWeaponFlagWord Flags { get; private set; }

    /// <summary>Gets the fire-variant counter — the original's <c>Human+1256</c>.</summary>
    internal int FireVariant { get; private set; }

    /// <summary>Gets whether a medipack was consumed during the most recent weapon tick.</summary>
    internal bool MedipackUsedThisTick { get; private set; }

    /// <summary>Gets whether the medipack animation was started during the most recent weapon tick.</summary>
    internal bool MedipackStartedThisTick { get; private set; }

    /// <summary>Gets the weapon action the rig should be playing, or <see langword="null"/>.</summary>
    internal WeaponAction? RequestedAction { get; private set; }

    /// <summary>Gets the fire variant <see cref="RequestedAction"/> was raised with.</summary>
    internal int RequestedFireVariant { get; private set; }

    /// <summary>Gets the action the rig is currently playing — the original's <c>Human+3416</c>.</summary>
    internal WeaponAction CurrentAction { get; private set; } = WeaponAction.None;

    /// <summary>Gets whether the trigger byte is raised this tick.</summary>
    internal bool WantFire => _wantFire;

    /// <summary>Gets whether the M203 callback marker is raised this tick.</summary>
    internal bool WantAlternateFire => _wantAlternateFire;

    /// <summary>Gets how long the current or last throw was charged, in ticks — <c>Human+1248</c>.</summary>
    internal int ThrowChargeTicks => _throwChargeTicks;

    /// <summary>Gets whether the current throw has been released.</summary>
    internal bool ThrowReleased => _throwReleased;

    /// <summary>Gets whether a one-shot action finished on the last tick — the original's <c>Human+1232</c>.</summary>
    internal bool ActionComplete => _actionComplete;

    private CarriedWeapon? InHand => _selector.InHand;

    private AmmoPool Ammunition => _owner.Ammunition;

    private HumanViewSway Sway => _owner.Sway;

    private bool FiredThisTick => _owner.FiredThisTick;

    /// <summary>Clears the placement's fire request once the gun has accepted its round.</summary>
    internal void ClearPlacingFirePending() => _placingFirePending = false;

    /// <summary>Rebuilds the flag word, clears the per-tick requests and runs the current state.</summary>
    /// <param name="input">The player's current and previous input frames.</param>
    /// <param name="clipEnded">Whether view bone 0's clip has reached its end.</param>
    /// <param name="onGround">Whether the body is on the ground.</param>
    /// <param name="locomotionAction">What the body's locomotion state asks the torso to play.</param>
    internal void Tick(PlayerInputState input, bool clipEnded, bool onGround, WeaponAction locomotionAction)
    {
        HumanWeaponFlagWord flags = HumanWeaponFlagBuilder.Build(Flags, input.Current, clipEnded, blending: false);
        if (onGround)
        {
            flags |= HumanWeaponFlagWord.OnGround;
        }
        else
        {
            flags &= ~HumanWeaponFlagWord.OnGround;
        }

        Flags = flags;
        RequestedAction = null;
        RequestedFireVariant = 0;
        _wantFire = false;
        _wantAlternateFire = false;
        _actionComplete = false;
        // FUN_0040FBE0 is called once per sustained-handler tick. This latch prevents duplicate
        // calls within one tick (PlayerCamera is the production caller) but must not survive into
        // the next tick, or a held fifty-charge medipack stops after its first dose.
        _medipackUsed = false;
        MedipackUsedThisTick = false;
        MedipackStartedThisTick = false;
        if (!input.IsSet(PlayerInputSlot.Fire))
        {
            _medipackAwaitingRelease = false;
        }
        _locomotionAction = locomotionAction;

        Step(input);
    }

    /// <summary>Executes one cooldown-eligible medipack dose while the syringe loop is held.</summary>
    /// <param name="health">The health accumulator owned by the player.</param>
    /// <returns><see langword="true"/> when one medipack was consumed.</returns>
    internal bool TryUseMedipack(HumanHealth health)
    {
        if (State != HumanWeaponState.Medipack || _medipackUsed || InHand is not { } held
            || held.Weapon.Category != (WeaponCategory)PlayerWeapons.MedipackCategory
            || MedipackStartedThisTick
            || !_medipackInjectionLoopStarted
            || _medipackClosing)
        {
            return false;
        }

        // FUN_0040FBE0 is the sustained part of the state: it runs after the one-shot entry clip and
        // owns every finish decision from here on. Releasing Fire (Human+456 == 0) only *schedules*
        // the terminal clip -- the handler still falls through and applies one final dose on the same
        // tick. Gating the dose on Fire being held (as an earlier port did) is why a quick tap played
        // the syringe animation, put it away, and never healed: the entry clip out-lasts the tap, so
        // by the time the loop begins Fire is already up and the only dose was skipped.
        bool fireReleased = !Flags.HasFlag(HumanWeaponFlagWord.Fire) || _medipackAwaitingRelease;

        // FUN_0040FBE0 tests this before looking up the ammo or touching the damage accumulator.
        // A non-zero countdown spends the whole handler update merely decrementing Human+0xD6C;
        // the visible syringe loop continues, but it neither heals nor consumes a charge. When Fire
        // is already up, the scheduled terminal is all that is left to do.
        if (_medipackCooldownTicks != 0)
        {
            _medipackCooldownTicks--;
            if (fireReleased)
            {
                BeginMedipackFinish();
            }

            return false;
        }

        int ammoId = held.Gun.Ammo?.AmmoId ?? PlayerWeapons.MedipackAmmoId;
        // The native handler resolves AMMO_ID_MEDIPACK and consumes [ammoRecord+4]. A medipack
        // pickup is therefore never a one-round loaded clip; using RoundsInClip here caused the
        // first dose to be detached from the reserve HUD count and made later doses disappear.
        // No charges, no heal fraction, or already at full health are FUN_0040FBE0's else-branch
        // terminal conditions: run FireAnimation3 and dispatch the parent state instead of dosing.
        if (Ammunition.Count(ammoId) <= 0
            || held.Weapon.Weapon.Values.Count == 0
            || health.RemainingFraction >= 1f)
        {
            BeginMedipackFinish();
            return false;
        }

        // FUN_0040FBE0 consumes one AMMO_ID_MEDIPACK charge, applies the clamped task fraction,
        // and starts a fresh native cooldown while the *same looping animation* continues.
        Ammunition.Take(ammoId, 1);

        _medipackUsed = true;
        MedipackUsedThisTick = true;
        health.HealFraction(held.Weapon.Weapon.Values[0]);
        _medipackCooldownTicks = GunTiming.TicksPerRound(held.Weapon.RoundsPerMinute);

        // The dose ran; now honour the terminal the top of the handler scheduled when Fire came up,
        // and the two else-branch stops that a dose can newly satisfy.
        if (fireReleased || health.RemainingFraction >= 1f || Ammunition.Count(ammoId) <= 0)
        {
            BeginMedipackFinish();
        }

        return true;
    }

    private void BeginMedipackFinish()
    {
        if (_medipackClosing)
        {
            return;
        }

        // 0x40FBE0 does not jump straight to Ready when Fire is released, health is full, or stock
        // is empty. It starts Weapon+0x68C (FireAnimation3), then 0x40FD70 waits for that one-shot
        // clip to end before dispatching the parent state. Keeping that terminal clip separate from
        // the sustained loop prevents the medipack from looking or sounding like a firearm reload.
        _medipackUsed = true;
        _medipackAwaitingRelease = true;
        _medipackClosing = true;
        _medipackCloseAdvanced = false;
        // The retail medipack record currently shipped with IGI.EXE has no FireAnimation3 (-1).
        // 0x40FBE0 still dispatches that id, but the native animation dispatcher leaves the view
        // untouched and the callback returns to Ready. Do the same; requesting an unavailable clip
        // would leave the managed loop waiting forever for a ClipEnded flag that can never arrive.
        _medipackTerminalClipMissing = InHand is not { } held
            || WeaponAnimationTable.Select(held.Weapon, WeaponAction.Fire, fireVariant: 2).FullBody < 0;
        if (!_medipackTerminalClipMissing)
        {
            Request(WeaponAction.Fire, fireVariant: 2);
        }
    }

    /// <summary>Mirrors <c>HumanPlayer</c>'s state-11 half of the map computer.</summary>
    /// <param name="open">Whether the map computer is open.</param>
    internal void SetMapComputerOpen(bool open)
    {
        if (open)
        {
            if (State == HumanWeaponState.Ready)
            {
                PreviousState = State;
                State = HumanWeaponState.MapComputer;
                Enter(State);
            }

            return;
        }

        if (State == HumanWeaponState.MapComputer)
        {
            PreviousState = State;
            State = HumanWeaponState.Ready;
            Enter(State);
        }
    }

    /// <summary>Suspends the machine and drops every outgoing request because the player has died.</summary>
    internal void StopForDeath()
    {
        PreviousState = State;
        State = HumanWeaponState.Suspended;
        Flags = (Flags & ~(HumanWeaponFlagWord.Fire | HumanWeaponFlagWord.AlternateFire | HumanWeaponFlagWord.Reload))
            | HumanWeaponFlagWord.Dead
            | HumanWeaponFlagWord.Dying;
        _wantFire = false;
        _wantAlternateFire = false;
        _throwReleased = false;
        _actionComplete = false;
        Request(WeaponAction.None);
    }

    /// <summary>Whether the grenade in hand has nothing left to throw — clip and reserve both empty.</summary>
    private bool OutOfThrowables() =>
        InHand is not { } held || (held.Gun.RoundsInClip <= 0 && held.Gun.Reserve(Ammunition) <= 0);

    // 0x4EC1E0: the state's per-tick runs once a tick, and a transition calls only the target's ENTRY, which installs the
    // target's per-tick and returns (0x40E9B0, 0x40EF60, 0x40F170 and every sibling). So the new state's per-tick first runs on
    // the next tick, against that tick's input and clip flags -- never against the outgoing clip's. Measured against retail on
    // mission 1: a one-tick tap of the MP5 enters the fire state and fires nothing; a reload under a held trigger finishes
    // into the ready hub, not straight into the fire state; the reload clip starts the tick after the press.
    private void Step(PlayerInputState input)
    {
        HumanWeaponState next = Advance(input);
        if (next == State)
        {
            return;
        }

        PreviousState = State;
        State = next;
        Enter(next);
    }

    private HumanWeaponState Advance(PlayerInputState input) => State switch
    {
        HumanWeaponState.Suspended => HumanWeaponState.Ready,
        HumanWeaponState.Ready => Ready(input),
        HumanWeaponState.Firing => Firing(input),
        HumanWeaponState.Striking => Striking(input),
        HumanWeaponState.DryFire => DryFire(input),
        HumanWeaponState.Reloading => Reloading(),
        HumanWeaponState.ChangingWeapon => ChangingWeapon(),
        HumanWeaponState.Peeking => Peeking(input),
        HumanWeaponState.Throwing => Throwing(input),
        HumanWeaponState.Placing => Placing(input),
        HumanWeaponState.Medipack => Medipack(),
        HumanWeaponState.AlternateFire => AlternateFireState(),
        _ => Flags.HasFlag(HumanWeaponFlagWord.ClipEnded) ? HumanWeaponState.Ready : State,
    };

    // 0x40E9C0. The order of these tests is the specification.
    private HumanWeaponState Ready(PlayerInputState input)
    {
        // The first thing 0x40E9C0 does, before anything else: put back whatever the LOCOMOTION side
        // asked for. Without it a weapon holds the last frame of its firing clip for ever, because
        // nothing else ever returns it to its stand pose.
        if (CurrentAction != _locomotionAction)
        {
            Request(_locomotionAction);
        }

        if (InHand is { } held)
        {
            int clip = held.Gun.RoundsInClip;
            int reserve = held.Gun.Reserve(Ammunition);

            if (Flags.HasFlag(HumanWeaponFlagWord.AlternateFire)
                && input.IsRisingEdge(PlayerInputSlot.AlternateFire)
                && PlayerWeapons.IsM16A2(held.Weapon)
                && _owner.HasM203Round())
            {
                return HumanWeaponState.AlternateFire;
            }

            // Level-triggered, deliberately: there is no edge test here in the original, which is what
            // lets an automatic weapon keep firing while the button is down.
            if (Flags.HasFlag(HumanWeaponFlagWord.Fire))
            {
                switch (held.Weapon.Category)
                {
                    case (WeaponCategory)PlayerWeapons.StrikingCategory:
                        return HumanWeaponState.Striking;

                    case (WeaponCategory)PlayerWeapons.ThrownCategory when clip != 0:
                        return HumanWeaponState.Throwing;

                    case (WeaponCategory)PlayerWeapons.PlacedCategory when clip != 0:
                        return HumanWeaponState.Placing;

                    case (WeaponCategory)PlayerWeapons.MedipackCategory:
                        if (_medipackAwaitingRelease)
                        {
                            break;
                        }

                        return HumanWeaponState.Medipack;

                    case WeaponCategory.Binocular:
                        // Binoculars have no firearm action. Fire, reload and peek are ignored while
                        // the overlay is held; otherwise the generic default would request a gun
                        // animation behind the device frame and could emit a phantom shot.
                        break;

                    case (WeaponCategory)PlayerWeapons.ThrownCategory:
                    case (WeaponCategory)PlayerWeapons.PlacedCategory:
                        break;

                    default:
                        return clip != 0 ? HumanWeaponState.Firing : HumanWeaponState.DryFire;
                }
            }

            // The manual reload and the automatic one are the same branch -- the `clip == 0` disjunct
            // is why an empty magazine tops itself up without being asked.
            // The medipack's one-slot count is a use charge, not a firearm magazine. It
            // has an authored injection action and must never enter the generic reload
            // animation or transfer a charge from the reserve pool.
            bool reloadable = held.Weapon.Category is not WeaponCategory.Binocular
                and not (WeaponCategory)PlayerWeapons.MedipackCategory;
            bool asked = Flags.HasFlag(HumanWeaponFlagWord.Reload) || clip == 0;
            if (reloadable && asked && reserve != 0 && clip < held.Weapon.RoundsPerClip)
            {
                return HumanWeaponState.Reloading;
            }

            if (held.Weapon.Category != WeaponCategory.Binocular
                && Flags.HasFlag(HumanWeaponFlagWord.Peek)
                && Flags.HasFlag(HumanWeaponFlagWord.OnGround))
            {
                return HumanWeaponState.Peeking;
            }
        }

        if (_selector.PendingWeaponId != -1)
        {
            _selector.SetBinocularOpenPending(_selector.PendingWeaponId);
            return HumanWeaponState.ChangingWeapon;
        }

        if (input.IsRisingEdge(PlayerInputSlot.MapComputer) && Flags.HasFlag(HumanWeaponFlagWord.OnGround))
        {
            return HumanWeaponState.MapComputer;
        }

        return HumanWeaponState.Ready;
    }

    // 0x40FD70, the wait 0x40F4A0 leaves the state on: back to the ready hub when the launch clip has ended, and nothing else
    // -- no second launch, no stand pose over the clip.
    private HumanWeaponState AlternateFireState() =>
        Flags.HasFlag(HumanWeaponFlagWord.ClipEnded) ? HumanWeaponState.Ready : HumanWeaponState.AlternateFire;

    // 0x40EF70. It does not fire; it asks, on every tick the trigger is down.
    private HumanWeaponState Firing(PlayerInputState input)
    {
        CarriedWeapon held = InHand!.Value;
        int clip = held.Gun.RoundsInClip;
        int reserve = held.Gun.Reserve(Ammunition);
        bool triggerDown = input.IsSet(PlayerInputSlot.Fire);
        bool ended = Flags.HasFlag(HumanWeaponFlagWord.ClipEnded);

        if (!triggerDown)
        {
            bool reloadable = InHand?.Weapon.Category is not WeaponCategory.Binocular
                and not (WeaponCategory)PlayerWeapons.MedipackCategory;
            bool asked = input.IsSet(PlayerInputSlot.Reload) || clip == 0;
            if (reloadable && asked && reserve != 0 && clip < held.Weapon.RoundsPerClip)
            {
                return HumanWeaponState.Reloading;
            }
        }
        else if (clip == 0 && ended)
        {
            return HumanWeaponState.DryFire;
        }
        else
        {
            _wantFire = true;
        }

        // The animation is played for the round that went off LAST tick, which is what keeps a shot's
        // report and its animation on the same frame as each other rather than one ahead.
        if (FiredThisTick)
        {
            Request(WeaponAction.FireTimed);
        }

        return ended && !triggerDown ? HumanWeaponState.Ready : HumanWeaponState.Firing;
    }

    // 0x40FA80, the knife. The counter advances on rounds, not on ticks.
    private HumanWeaponState Striking(PlayerInputState input)
    {
        if (input.IsSet(PlayerInputSlot.Fire) && FireVariant < PlayerWeapons.FireVariantCount)
        {
            _wantFire = true;
        }

        if (FiredThisTick)
        {
            // Dispatch first, THEN increment: the original reads Human+1256 inside 0x4137E0 and
            // advances it afterwards, so the first strike of a press is FireAnimation1.
            Request(WeaponAction.Fire, FireVariant);
            FireVariant++;
        }

        return Flags.HasFlag(HumanWeaponFlagWord.ClipEnded) ? HumanWeaponState.Ready : HumanWeaponState.Striking;
    }

    // 0x40F100.
    private static HumanWeaponState DryFire(PlayerInputState input) =>
        input.IsSet(PlayerInputSlot.Fire) ? HumanWeaponState.DryFire : HumanWeaponState.Ready;

    // 0x40F590 into 0x40F640, the grenade's charge-and-release. Holding fire charges (Human+1248
    // counts the ticks); letting go releases -- the trigger byte is raised for exactly one tick,
    // the gun lets the round out, and the throw animation plays over the round that went off. The
    // state runs home once that clip ends.
    private HumanWeaponState Throwing(PlayerInputState input)
    {
        if (!_throwReleased)
        {
            if (input.IsSet(PlayerInputSlot.Fire))
            {
                _throwChargeTicks++;
                return HumanWeaponState.Throwing;
            }

            _throwReleased = true;
            _wantFire = true;
            return HumanWeaponState.Throwing;
        }

        if (FiredThisTick)
        {
            // The throw itself is the SECOND fire animation where the record carries one -- the
            // first is the wind-up the entry already played and holding it again would read as the
            // hand pocketing the grenade rather than releasing it.
            Request(WeaponAction.Fire, InHand?.Weapon.FireAnimation2 >= 0 ? 1 : 0);

            // 0x40F640 snaps the rig out of the frame the instant the round leaves the hand -- the
            // throw's own follow-through, the same lowering a weapon change and a peek use.
            Sway.SnapTo(HumanViewSway.LoweredPitch, HumanViewSway.LoweredYaw);

            // Out of grenades, 0x40F640's else branch (sub_40F760) selects the next carried weapon rather
            // than staying on the spent one; the rig is already lowered for the change. With more in the
            // pouch it stays and eases the rig back up, ready to throw again.
            if (OutOfThrowables() && _selector.SelectNextCarried())
            {
                BeginChange();
                return HumanWeaponState.ChangingWeapon;
            }

            Sway.Raise();
        }

        return Flags.HasFlag(HumanWeaponFlagWord.ClipEnded)
            ? HumanWeaponState.Ready
            : HumanWeaponState.Throwing;
    }

    // 0x40F930/0x40F950. A mine is a one-shot placed task: entering the state lowers the rig and
    // requests one gun round, then the state returns as soon as that round has been accepted. The
    // old port entered Placing but never set the fire request, which made every mine a silent no-op.
    private HumanWeaponState Placing(PlayerInputState input)
    {
        if (_placingFirePending)
        {
            _wantFire = true;
            return HumanWeaponState.Placing;
        }

        // A held trigger must not place a mine again when the state returns through Ready. Wait for
        // the release edge, just as the grenade throw waits for its release before accepting another
        // round.
        if (input.IsSet(PlayerInputSlot.Fire))
        {
            return HumanWeaponState.Placing;
        }

        return OutOfThrowables() && _selector.SelectNextCarried()
            ? HumanWeaponState.ChangingWeapon
            : HumanWeaponState.Ready;
    }

    // 0x479990, the gun's own reload handler, which 0x40F180 drives. It is a two-phase machine: play
    // the reload animation, and move the rounds only when that animation has finished. So the reload's
    // DURATION is the clip's length -- the roster's `Reload Time` (+1596) is not read on this path at
    // all, which is a good example of a field that exists for the HUD.
    private HumanWeaponState Reloading()
    {
        // 0x40F180's first call: the gun's handler starts the reload clip -- the SPAS-12's opens its tube -- and the clip's end
        // is waited for from the next tick on.
        if (_reloadStarting)
        {
            _reloadStarting = false;
            if (InHand is { } first && LoadsByTheShell(first.Weapon))
            {
                return LoadingShells(first.Gun);
            }

            Request(WeaponAction.Reload);
            return HumanWeaponState.Reloading;
        }

        if (InHand is { } shotgun && LoadsByTheShell(shotgun.Weapon))
        {
            return LoadingShells(shotgun.Gun);
        }

        if (!Flags.HasFlag(HumanWeaponFlagWord.ClipEnded))
        {
            return HumanWeaponState.Reloading;
        }

        InHand?.Gun.Reload(Ammunition);
        _actionComplete = true;
        return HumanWeaponState.Ready;
    }

    // 0x472AE0, the SPAS-12's own reload handler in 0x479990's place: open, then a shell a clip until the tube is full or
    // the pocket is empty, then close. A gun with nothing to load and no reload under way is done at once.
    private HumanWeaponState LoadingShells(HeldGun gun)
    {
        GunState state = gun.State;
        bool clipDone = Flags.HasFlag(HumanWeaponFlagWord.ClipEnded);
        if (gun.Reserve(Ammunition) <= 0 && state.ReloadStage == 0)
        {
            _actionComplete = true;
            return HumanWeaponState.Ready;
        }

        switch (state.ReloadStage)
        {
            case 0:
                Request(WeaponAction.ShellReloadOpen);
                state.ReloadStage = 1;
                break;
            case 1 when clipDone:
                Request(WeaponAction.ShellReloadInsert);
                state.ReloadStage = 2;
                break;
            case 2 when clipDone:
                bool pocket = gun.Reserve(Ammunition) > 0;
                int room = pocket ? gun.LoadShell(Ammunition) : 0;
                if (room > 1)
                {
                    Request(WeaponAction.ShellReloadInsert);
                    break;
                }

                Request(WeaponAction.ShellReloadClose);
                state.ReloadStage = 3;
                break;
            case 3 when clipDone:
                state.ReloadStage = 0;
                _actionComplete = true;
                return HumanWeaponState.Ready;
        }

        return HumanWeaponState.Reloading;
    }

    // The one gun whose class loads a shell at a time.
    private static bool LoadsByTheShell(WeaponType weapon) =>
        string.Equals(weapon.Weapon.TypeName, "TASKTYPE_GUNSPAS12", StringComparison.Ordinal);

    private HumanWeaponState Medipack()
    {
        // 0x40FB90 waits for the entry clip to end, then starts FireAnimation2 with the native loop
        // flag. The loop is kept in this state; a clip wrap must not create another medipack action.
        if (!_medipackInjectionLoopStarted)
        {
            if (Flags.HasFlag(HumanWeaponFlagWord.ClipEnded))
            {
                _medipackInjectionLoopStarted = true;
                Request(WeaponAction.MedipackLoop);
            }

            return HumanWeaponState.Medipack;
        }

        if (_medipackClosing)
        {
            if (_medipackTerminalClipMissing)
            {
                return HumanWeaponState.Ready;
            }

            // The current ClipEnded flag belongs to the outgoing syringe loop on the tick that
            // starts FireAnimation3. Do not let it complete the newly requested terminal clip.
            if (!_medipackCloseAdvanced)
            {
                _medipackCloseAdvanced = true;
                return HumanWeaponState.Medipack;
            }

            return Flags.HasFlag(HumanWeaponFlagWord.ClipEnded)
                ? HumanWeaponState.Ready
                : HumanWeaponState.Medipack;
        }

        if (!Flags.HasFlag(HumanWeaponFlagWord.Fire) || _medipackAwaitingRelease)
        {
            BeginMedipackFinish();
        }

        return HumanWeaponState.Medipack;
    }

    // 0x40F280 into 0x40F300, and it is two phases rather than one.
    //
    // Phase one waits for the rig to finish tipping out of the frame -- the entry sent it there -- and
    // only THEN exchanges the model, which is what hides the swap. Phase two sends the rig back up.
    private HumanWeaponState ChangingWeapon()
    {
        if (!_swapped)
        {
            // 0x40F280.
            if (!Sway.Settled)
            {
                return HumanWeaponState.ChangingWeapon;
            }

            _selector.SwapToPending();
            _swapped = true;

            // Action 10 is the stand pose again, snapped rather than eased -- the model in your hands
            // has just changed, so there is nothing to ease from.
            Request(WeaponAction.StandImmediate);
            Sway.Raise();
            return HumanWeaponState.ChangingWeapon;
        }

        // 0x40F300, and it tests the pending id BEFORE it tests the rig: asking for another weapon
        // while the last one is still coming up abandons the raise and starts over, which is what lets
        // NextWeapon be held down and stepped through.
        if (_selector.PendingWeaponId != -1)
        {
            BeginChange();
            return HumanWeaponState.ChangingWeapon;
        }

        if (!Sway.Settled)
        {
            return HumanWeaponState.ChangingWeapon;
        }

        _actionComplete = true;
        return HumanWeaponState.Ready;
    }

    // 0x40F3F0: hold the lean until Peek is let go, then raise the rig and go back.
    private HumanWeaponState Peeking(PlayerInputState input)
    {
        if (input.IsSet(PlayerInputSlot.Peek))
        {
            return HumanWeaponState.Peeking;
        }

        Sway.Raise();
        return HumanWeaponState.Ready;
    }

    // 0x40F230, the change state's entry -- reached both from Ready and from its own per-tick.
    private void BeginChange()
    {
        _swapped = false;
        Request(WeaponAction.StandAlternate);
        Sway.Lower();
    }

    private void Enter(HumanWeaponState state)
    {
        switch (state)
        {
            case HumanWeaponState.Suspended:
                Request(WeaponAction.None);
                break;

            // 0x40F370 lowers the rig too, on exactly the same rates -- leaning out of cover puts the
            // weapon away the same way changing it does.
            case HumanWeaponState.Peeking:
                Request(WeaponAction.Stand);
                Sway.Lower();
                break;

            // 0x40FA60 zeroes the variant counter, which is what makes each press start at
            // FireAnimation1 rather than continuing the previous swing's cycle.
            case HumanWeaponState.Striking:
                FireVariant = 0;
                break;

            // 0x40F4F0. Zeroed here; the per-tick counts the held ticks from the next one on. The entry also plays the
            // weapon's first fire animation -- the pin-and-wind-up the hand holds while the throw charges -- exactly as the
            // retail entry plays record +1668.
            case HumanWeaponState.Throwing:
                _throwChargeTicks = 0;
                _throwReleased = false;
                Request(WeaponAction.Fire);
                break;

            // 0x40F930 raises the fire request itself, so the mine goes down on the entry tick; the per-tick only waits on it.
            case HumanWeaponState.Placing:
                _placingFirePending = true;
                _wantFire = true;
                Request(WeaponAction.Fire);
                Sway.SnapTo(HumanViewSway.LoweredPitch, HumanViewSway.LoweredYaw);
                break;

            // 0x40F4A0: raises the launch marker once and plays the weapon's FireAnimation2 (0x4137E0 action 8, variant 1) --
            // clip 39 in WEAPONS/M16A2/WEAPON.QVM; variant 0 is the ordinary rifle shot (clip 4). The M203 object is created
            // after the view has been committed, in ResolveFire, exactly like the 0x462E50 bridge.
            case HumanWeaponState.AlternateFire:
                _wantAlternateFire = true;
                Request(WeaponAction.Fire, fireVariant: 1);
                break;

            case HumanWeaponState.Medipack:
                _medipackUsed = false;
                _medipackInjectionLoopStarted = false;
                _medipackClosing = false;
                _medipackCloseAdvanced = false;
                _medipackTerminalClipMissing = false;
                // 0x40FB6D explicitly clears Human+0xD6C on entry. The first dose therefore occurs
                // when the entry clip hands off to the loop; only later doses wait for the cadence.
                _medipackCooldownTicks = 0;
                MedipackStartedThisTick = true;
                Request(WeaponAction.Fire);
                break;

            // 0x40F170 only installs 0x40F180; the reload starts on its first call (Reloading).
            case HumanWeaponState.Reloading:
                _reloadStarting = true;
                break;

            // Play the stand pose and start tipping the rig out of the frame. Nothing is exchanged
            // yet -- the per-tick does that once the rig has arrived.
            case HumanWeaponState.ChangingWeapon:
                BeginChange();
                break;

            default:
                break;
        }
    }

    // 0x4137E0's two inputs, kept together so nothing can raise an action and read a variant that has
    // already moved on.
    private void Request(WeaponAction action, int fireVariant = 0)
    {
        CurrentAction = action;
        RequestedAction = action;
        RequestedFireVariant = fireVariant;
    }
}
