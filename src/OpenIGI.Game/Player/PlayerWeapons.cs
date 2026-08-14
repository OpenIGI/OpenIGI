using OpenIGI.Core.Numerics;
using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.Player;

/// <summary>One carried weapon: its record, its gun, and the ammunition type it draws from.</summary>
/// <param name="Weapon">The roster record.</param>
/// <param name="Gun">The magazine and the clock.</param>
public readonly record struct CarriedWeapon(WeaponType Weapon, HeldGun Gun);

/// <summary>What acquiring a world weapon changed in the player's inventory.</summary>
/// <param name="AddedWeapon">Whether the pickup created a new carried gun.</param>
/// <param name="AddedReserveRounds">The rounds added to an already-owned weapon's reserve.</param>
public readonly record struct WeaponPickupResult(bool AddedWeapon, int AddedReserveRounds);

/// <summary>
/// The player's weapons: the state machine at <c>Human+3320</c>, the gun it talks to, and the cycle it
/// switches through.
/// </summary>
/// <remarks>
/// <para>
/// Three pieces of the original meet here, in the order <c>HumanPlayer_Update</c> runs them:
/// <c>sub_411000</c> and <c>sub_40F760</c> decide whether a weapon change is pending, the state machine
/// (<c>0x4EC1E0</c> over <c>off_538678</c>) decides what the human is doing, and <c>0x462E50</c> turns
/// the byte it raised into a message to the gun.
/// </para>
/// <para>
/// <b>The split matters.</b> Nothing in the human counts rounds or reads <c>Rounds/Minute</c>: the
/// firing state asks for a round on every tick the trigger is down and <see cref="HeldGun"/> answers on
/// the ticks its cooldown allows. That is what makes a weapon's cadence its own.
/// </para>
/// <para>
/// The bridge runs <b>after</b> the view has been committed for the tick, because the muzzle is the
/// eye — call <see cref="Tick"/>, sync the rig, then <see cref="ResolveFire"/>.
/// </para>
/// <para>See the original and the original.</para>
/// </remarks>
public sealed class PlayerWeapons
{
    /// <summary>The category whose weapons enter <see cref="HumanWeaponState.Striking"/>.</summary>
    /// <remarks>The knife. <c>0x40E9C0</c>'s switch names the value, not the enum.</remarks>
    public const int StrikingCategory = 3;

    /// <summary>The category whose weapons are thrown.</summary>
    public const int ThrownCategory = 4;

    /// <summary>The category that heals.</summary>
    public const int MedipackCategory = 6;

    /// <summary>The native ammo id published by <c>AMMO_ID_MEDIPACK</c>.</summary>
    /// <remarks>
    /// The shipped <c>AMMO/MEDIPACK/AMMO.QVM</c> passes 12 as DefineAmmoType's first argument.
    /// This is distinct from the medipack weapon category (6); using the category here makes a
    /// collected medipack appear as zero reserve and prevents the injection from consuming it.
    /// </remarks>
    public const int MedipackAmmoId = 12;

    /// <summary>The category that is placed rather than fired.</summary>
    public const int PlacedCategory = 7;

    /// <summary>How high the fire-variant counter may go before the state stops asking.</summary>
    /// <remarks>
    /// Three, which is exactly the number of firing animations a weapon record carries. The dispatch
    /// plays <b>nothing</b> at three and above rather than wrapping, so the cap and the table agree.
    /// </remarks>
    public const int FireVariantCount = 3;

    /// <summary>How much of the accumulated view recoil survives a tick.</summary>
    /// <remarks><c>0.89999998</c>, applied by <c>0x462E50</c> to both angles after adding the kick.</remarks>
    public const float RecoilDamping = 0.89999998f;

    /// <summary>What <c>Human+3400</c> holds before the first update.</summary>
    /// <remarks>
    /// <c>-1</c>, and that is what puts the first entry of the weapon cycle in your hands: the cycler
    /// reads the sentinel as "advance", and <c>-1 + 1</c> is index 0.
    /// </remarks>
    public const int NoCycleIndex = -1;

    private readonly List<CarriedWeapon> _carried = [];
    private readonly List<GunProjectile> _shots = [];
    private readonly IgiRandom _random;
    private readonly List<int> _cycle = [];
    private readonly List<HumanPlayerWeaponCategory> _categories = [];
    private int _categoryIndex = -1;
    private int _memberIndex;

    private int _cycleIndex = NoCycleIndex;
    private int _pendingWeaponId = -1;
    private int _heldIndex = -1;
    private bool _wantFire;
    private bool _firedThisTick;
    private int _throwChargeTicks;
    private bool _throwReleased;
    private int _shotSequence;
    private bool _actionComplete;
    private bool _medipackUsed;
    private bool _medipackAdvanced;
    private bool _medipackAwaitingRelease;
    private bool _medipackEffectStarted;
    private bool _medipackClipCompletionPending;
    private float _medipackAppliedProgress;
    private WeaponAction _locomotionAction = WeaponAction.Stand;
    private int _categoryPick = -1;
    private bool _swapped;
    private int _lastNonBinocularWeaponId = -1;
    private bool _binocularTogglePending;
    private bool _binocularOpenPending;

    /// <summary>Initializes a new instance of the <see cref="PlayerWeapons"/> class.</summary>
    /// <param name="weaponCycle">
    /// <c>DefineHumanPlayerWeaponCycle</c>'s list of weapon ids, in order. The shipped one starts
    /// <c>KNIFE, GLOCK, COLT, DESERTEAGLE, UZI, UZIX2, MP5SD, …</c>.
    /// </param>
    /// <param name="random">The stream spread and recoil are sampled from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="weaponCycle"/> is <see langword="null"/>.</exception>
    public PlayerWeapons(IReadOnlyList<int> weaponCycle, IgiRandom? random = null)
    {
        ArgumentNullException.ThrowIfNull(weaponCycle);
        _cycle.AddRange(weaponCycle);
        _random = random ?? new IgiRandom();
    }

    /// <summary>Gives the machine the nine weapon categories the number keys select.</summary>
    /// <param name="categories"><c>DefineHumanPlayerWeaponCategory</c>'s rows, in script order.</param>
    /// <exception cref="ArgumentNullException"><paramref name="categories"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Optional: with none given the number keys do nothing and the cycle still works, which is the
    /// honest failure when <c>humanplayer.qvm</c> could not be read.
    /// </remarks>
    public void UseCategories(IReadOnlyList<HumanPlayerWeaponCategory> categories)
    {
        ArgumentNullException.ThrowIfNull(categories);
        _categories.Clear();
        _categories.AddRange(categories);
    }

    /// <summary>Gets the state the machine is in.</summary>
    public HumanWeaponState State { get; private set; } = HumanWeaponGraph.InitialState;

    /// <summary>Gets the state the machine was in before the current one.</summary>
    public HumanWeaponState PreviousState { get; private set; } = HumanWeaponGraph.InitialState;

    /// <summary>Gets the flag word the prologue last built.</summary>
    public HumanWeaponFlagWord Flags { get; private set; }

    /// <summary>Gets the two angles the rig is turned by while a weapon is being put away.</summary>
    /// <remarks>
    /// The weapon-change animation lives here rather than in a clip — see <see cref="HumanViewSway"/>.
    /// The caller composes it into the rig's orientation and calls <see cref="HumanViewSway.Advance"/>
    /// where <c>0x464BD0</c> would.
    /// </remarks>
    public HumanViewSway Sway { get; } = new();

    /// <summary>Gets the loose ammunition the player carries.</summary>
    public AmmoPool Ammunition { get; } = new();

    /// <summary>Gets everything the player carries, in the order it was given.</summary>
    public IReadOnlyList<CarriedWeapon> Carried => _carried;

    /// <summary>
    /// Consumes a binocular toggle after the requested weapon has reached the hand. The request is
    /// retained across the equip transition, matching the original's one-tick action hand-off.
    /// </summary>
    public bool ConsumeBinocularToggle()
    {
        if ((!_binocularTogglePending && !_binocularOpenPending)
            || InHand?.Weapon.Category != WeaponCategory.Binocular)
        {
            return false;
        }

        _binocularTogglePending = false;
        _binocularOpenPending = false;
        return true;
    }

    /// <summary>Gets the weapon in hand, or <see langword="null"/> before one has been selected.</summary>
    /// <remarks>Slot 0 of the original's three-pointer array at <c>Human+1220</c>.</remarks>
    public CarriedWeapon? InHand => _heldIndex < 0 ? null : _carried[_heldIndex];

    /// <summary>
    /// Gets the fire-variant counter — the original's <c>Human+1256</c>.
    /// </summary>
    /// <remarks>
    /// Counts <b>rounds that went off</b>, not ticks. <see cref="HumanWeaponState.Striking"/> zeroes it
    /// on entry and stops asking for rounds once it reaches <see cref="FireVariantCount"/>.
    /// </remarks>
    public int FireVariant { get; private set; }

    /// <summary>Gets the accumulated view kick in pitch, in radians — <c>HumanView+296</c>.</summary>
    public float RecoilPitch { get; private set; }

    /// <summary>Gets the accumulated view kick in yaw, in radians — <c>HumanView+308</c>.</summary>
    public float RecoilYaw { get; private set; }

    /// <summary>Gets whether a round went off on the last <see cref="ResolveFire"/>.</summary>
    public bool FiredThisTick => _firedThisTick;

    /// <summary>Gets a serial that advances once for every round that actually fires.</summary>
    /// <remarks>
    /// Presentation uses this to choose one muzzle-flash roll per shot rather than once per uncapped
    /// render. It does not consume the gameplay random stream.
    /// </remarks>
    public int ShotSequence => _shotSequence;

    /// <summary>Gets the projectiles the last <see cref="ResolveFire"/> produced.</summary>
    /// <remarks>
    /// One per <see cref="WeaponType.BulletsPerRound"/>. Empty on every tick no round went off, which
    /// is most of them.
    /// </remarks>
    public IReadOnlyList<GunProjectile> Shots => _shots;

    /// <summary>Gets whether a medipack was consumed during the most recent weapon tick.</summary>
    public bool MedipackUsedThisTick { get; private set; }

    /// <summary>Gets whether the medipack animation was started during the most recent weapon tick.</summary>
    /// <remarks>
    /// Healing is deliberately not allowed on this tick. The retail task raises the syringe action
    /// first and only consumes the medipack once that action has advanced; otherwise the health bar,
    /// reserve count and sound lead the visible left-arm injection by one animation.
    /// </remarks>
    public bool MedipackStartedThisTick { get; private set; }

    /// <summary>Gets the weapon action the rig should be playing, or <see langword="null"/>.</summary>
    /// <remarks>
    /// The original writes this to <c>Human+3416</c> inside <c>0x4137E0</c> and the animation follows;
    /// here the caller drives the rig from it, so it is exposed rather than applied.
    /// </remarks>
    public WeaponAction? RequestedAction { get; private set; }

    /// <summary>
    /// Gets the fire variant <see cref="RequestedAction"/> was raised with.
    /// </summary>
    /// <remarks>
    /// <b>Not the same as <see cref="FireVariant"/>.</b> The striking state dispatches the animation and
    /// <i>then</i> increments the counter — <c>sub_4137E0(human, 8)</c> reads <c>Human+1256</c> before
    /// <c>++</c> touches it — so the first strike of a press plays <c>FireAnimation1</c> and the counter
    /// is 1 by the time anything else looks at it. Reading the counter after the increment plays the
    /// second clip for the first swing, which is what this exists to stop.
    /// </remarks>
    public int RequestedFireVariant { get; private set; }

    /// <summary>Gets the action the rig is currently playing — the original's <c>Human+3416</c>.</summary>
    /// <remarks>
    /// <c>0x4137E0</c> writes it unconditionally at the top of the dispatch, and the Ready state
    /// compares it against what the locomotion side has asked for.
    /// </remarks>
    public WeaponAction CurrentAction { get; private set; } = WeaponAction.None;

    /// <summary>Adds a weapon to what the player carries.</summary>
    /// <param name="weapon">The roster record.</param>
    /// <param name="ammo">Its ammunition record, or <see langword="null"/> when it names no calibre.</param>
    /// <returns>The gun that was created for it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="weapon"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The original spawns a task per declared weapon as a child of the level's own <c>HumanPlayer</c>;
    /// which of them ends up in hand is not this call's business but the cycle's.
    /// </remarks>
    public HeldGun Carry(WeaponType weapon, AmmoType? ammo = null)
    {
        ArgumentNullException.ThrowIfNull(weapon);

        HeldGun gun = new(weapon, ammo);
        _carried.Add(new CarriedWeapon(weapon, gun));
        return gun;
    }

    /// <summary>Applies a <c>GunPickup</c> to this inventory.</summary>
    /// <param name="weapon">The roster weapon the world pickup names.</param>
    /// <param name="ammo">Its resolved ammunition record, or <see langword="null"/> for weapons that consume none.</param>
    /// <param name="rounds">The pickup's magazine payload.</param>
    /// <returns>Whether a gun was added or reserve ammunition was added.</returns>
    /// <remarks>
    /// <para>This is <c>Human::GunPickup</c> at <c>0x45FFC0</c>. A weapon the human does not yet own is
    /// added without selecting it, with the payload in its magazine. When the weapon already exists,
    /// retail instead adds that payload to the matching loose-ammunition entry and never duplicates the
    /// gun.</para>
    /// <para><c>GunPickup::Update</c> supplies the weapon record's clip size as <paramref name="rounds"/>,
    /// so authored pickups are one full magazine. Keeping the argument explicit preserves the native
    /// message boundary and lets scripted pickups provide a different payload later.</para>
    /// </remarks>
    public WeaponPickupResult PickUp(WeaponType weapon, AmmoType? ammo, int rounds)
    {
        ArgumentNullException.ThrowIfNull(weapon);

        if (_carried.Any(carried => carried.Weapon.WeaponId == weapon.WeaponId))
        {
            if (ammo is null)
            {
                return default;
            }

            Ammunition.Add(ammo.AmmoId, rounds);
            return new WeaponPickupResult(AddedWeapon: false, AddedReserveRounds: rounds);
        }

        HeldGun gun = Carry(weapon, ammo);
        gun.State.RoundsInClip = rounds;
        return new WeaponPickupResult(AddedWeapon: true, AddedReserveRounds: 0);
    }

    /// <summary>Advances the weapon machine by one tick.</summary>
    /// <param name="input">The player's current and previous input frames.</param>
    /// <param name="clipEnded">Whether view bone 0's clip has reached its end.</param>
    /// <param name="onGround">Whether the body is on the ground.</param>
    /// <param name="locomotionAction">
    /// What the body's current locomotion state asks the torso to play, from
    /// <see cref="HumanLocomotionWeaponActions"/> — the original's <c>Human+3420</c>. The Ready state
    /// resyncs to it, which is how a weapon goes back to its stand pose after a shot without either
    /// machine referring to the other.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="input"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The original's order, kept: the weapon cycler runs first (it is what sets the pending id the
    /// Ready state reacts to), then the prologue rebuilds the flag word, then the current state's
    /// per-tick runs — and a transition runs its target's entry <b>within the same tick</b>, as every
    /// machine in this engine does.
    /// </remarks>
    public void Tick(
        PlayerInputState input,
        bool clipEnded,
        bool onGround,
        WeaponAction locomotionAction = WeaponAction.Stand)
    {
        ArgumentNullException.ThrowIfNull(input);

        SelectBinoculars(input);
        SelectCategory(input);
        CycleWeapons(input);

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
        _actionComplete = false;
        MedipackUsedThisTick = false;
        MedipackStartedThisTick = false;
        if (!input.IsSet(PlayerInputSlot.Fire))
        {
            _medipackAwaitingRelease = false;
        }
        _locomotionAction = locomotionAction;

        Step(input);
    }

    /// <summary>Advances the medipack injection and applies its authored healing over the animation.</summary>
    /// <param name="health">The health accumulator owned by the player.</param>
    /// <returns><see langword="true"/> when one medipack was consumed.</returns>
    /// <param name="animationProgress">The first-person injection clip progress, from zero to one.</param>
    public bool TryUseMedipack(HumanHealth health, float animationProgress = 1f)
    {
        ArgumentNullException.ThrowIfNull(health);
        if (State != HumanWeaponState.Medipack || _medipackUsed || InHand is not { } held
            || held.Weapon.Category != (WeaponCategory)MedipackCategory
            || MedipackStartedThisTick)
        {
            return false;
        }

        // The native action is a no-op as soon as the health accumulator reaches its ceiling; it
        // does not keep the syringe pose armed until the authored clip's last frame. This also
        // handles a player who was already at 100% when the animation advanced for the first time.
        if (health.RemainingFraction >= 1f)
        {
            CompleteMedipackAttempt();
            return false;
        }

        animationProgress = Math.Clamp(animationProgress, 0f, 1f);
        if (animationProgress <= _medipackAppliedProgress)
        {
            if (Flags.HasFlag(HumanWeaponFlagWord.ClipEnded))
            {
                CompleteMedipackAttempt();
            }

            return false;
        }

        int ammoId = held.Gun.Ammo?.AmmoId ?? MedipackAmmoId;
        // The sentinel means the carried gun has not reached its first per-tick load yet. A real
        // pickup writes its payload into RoundsInClip, so only a concrete clip count is spendable
        // here; this keeps a merely equipped, uninitialised medipack from inventing a charge.
        int clip = held.Gun.RoundsInClip == GunTiming.UnloadedClipSentinel
            ? 0
            : held.Gun.RoundsInClip;
        bool hasClipPack = clip > 0;
        bool hasReservePack = Ammunition.Count(ammoId) > 0;
        if ((!_medipackEffectStarted && !hasClipPack && !hasReservePack)
            || held.Weapon.Weapon.Values.Count == 0)
        {
            CompleteMedipackAttempt();
            return false;
        }

        float progressDelta = animationProgress - _medipackAppliedProgress;
        float healed = health.HealFraction(held.Weapon.Weapon.Values[0] * progressDelta);
        if (healed <= 0f)
        {
            _medipackAppliedProgress = animationProgress;
            if (Flags.HasFlag(HumanWeaponFlagWord.ClipEnded))
            {
                CompleteMedipackAttempt();
            }

            return false;
        }

        if (!_medipackEffectStarted)
        {
            // A pickup starts in the medipack's one-slot magazine. Only subsequent pickups enter the
            // loose pool, so the first pickup must be usable without a second reserve pickup.
            if (hasClipPack)
            {
                held.Gun.State.RoundsInClip = clip - 1;
            }
            else
            {
                Ammunition.Take(ammoId, 1);
            }
            _medipackEffectStarted = true;
            MedipackUsedThisTick = true;
        }

        _medipackAppliedProgress = animationProgress;
        if (health.RemainingFraction >= 1f)
        {
            _medipackClipCompletionPending = true;
            CompleteMedipackAttempt();
        }
        if (Flags.HasFlag(HumanWeaponFlagWord.ClipEnded))
        {
            CompleteMedipackAttempt();
        }

        return true;
    }

    private void CompleteMedipackAttempt()
    {
        // The native task exits after a no-op attempt (empty reserve or full health); it does not
        // leave the fire edge armed for the next simulation tick.
        _medipackUsed = true;
        _medipackAwaitingRelease = true;
        _actionComplete = true;
    }

    /// <summary>
    /// Synchronizes this weapon machine with the map-computer owner. The human state transition itself
    /// is performed by <c>HumanPlayer</c>; this mirrors its state-11 half so no fire/reload state survives
    /// behind the computer.
    /// </summary>
    public void SetMapComputerOpen(bool open)
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

    /// <summary>Immediately stops weapon activity because the owning player has died.</summary>
    /// <remarks>
    /// The death transition does not run another normal player tick. Clearing the outgoing firing
    /// request here therefore matters: otherwise its last <see cref="FiredThisTick"/> value and firing
    /// state remain latched for the presentation layer after the player is dead.
    /// </remarks>
    public void StopForDeath()
    {
        PreviousState = State;
        State = HumanWeaponState.Suspended;
        Flags = (Flags & ~(HumanWeaponFlagWord.Fire | HumanWeaponFlagWord.AlternateFire | HumanWeaponFlagWord.Reload))
            | HumanWeaponFlagWord.Dead
            | HumanWeaponFlagWord.Dying;
        _wantFire = false;
        _firedThisTick = false;
        _shots.Clear();
        _throwReleased = false;
        _actionComplete = false;
        Request(WeaponAction.None);
    }

    /// <summary>
    /// Turns this tick's raised byte into a message to the gun — the original's <c>0x462E50</c>.
    /// </summary>
    /// <param name="eye">The muzzle, in engine units: the view's committed eye position.</param>
    /// <param name="forward">The view's <c>+Y</c> axis, which is where the barrel points.</param>
    /// <param name="right">The view's <c>+X</c> axis.</param>
    /// <param name="up">The view's <c>+Z</c> axis.</param>
    /// <param name="recoilScale">
    /// What the round's sampled kick is multiplied by before it accumulates — the Dragunov's scoped
    /// 0.05, or one.
    /// </param>
    /// <returns><see langword="true"/> when a round went off.</returns>
    /// <remarks>
    /// <para>
    /// Called <b>after</b> the rig has been placed for the tick, because the original calls it after
    /// <c>0x464BD0</c> — which is why the muzzle is this tick's eye and not last tick's.
    /// </para>
    /// <para>
    /// The recoil the gun reports is added to a pair of view angles that lose a tenth every tick. That
    /// is the whole of the model: no clamp, no separate recovery curve.
    /// </para>
    /// </remarks>
    public bool ResolveFire(Vector3d eye, Vector3f forward, Vector3f right, Vector3f up, float recoilScale = 1f)
    {
        _shots.Clear();
        _firedThisTick = false;

        if (InHand is not { } held)
        {
            return false;
        }

        held.Gun.Tick(_wantFire);

        GunFireResult result = held.Gun.Fire(_wantFire, eye, forward, right, up, _random);
        _firedThisTick = result.Fired;

        if (result.Fired)
        {
            unchecked
            {
                _shotSequence++;
            }

            _shots.AddRange(result.Projectiles);
        }

        // The scale is the Dragunov's scoped 0.05, or one. It multiplies the ROUND'S OWN kick and
        // not the accumulated angles, exactly where 0x472E50 applies it -- damping what is already
        // accumulated would make opening the scope mid-burst yank the view back down.
        RecoilPitch = ((result.RecoilX * recoilScale) + RecoilPitch) * RecoilDamping;
        RecoilYaw = ((result.RecoilZ * recoilScale) + RecoilYaw) * RecoilDamping;
        return result.Fired;
    }

    // 0x411000, the number keys. It runs immediately before the cycler in HumanPlayer_Update and
    // hands its answer over through input frame slot 13 -- a one-tick scratch register that carries no
    // binding of its own. The port passes it directly instead of
    // writing into the frame, which is the one thing here that is not literal.
    private void SelectCategory(PlayerInputState input)
    {
        _categoryPick = -1;

        if (_categories.Count == 0)
        {
            return;
        }

        // The original tests all nine in order and takes the LAST that fired, not the first.
        int wanted = -1;
        for (int i = 0; i < HumanPlayerWeaponCategory.CategoryCount; i++)
        {
            PlayerInputSlot slot = PlayerInputSlot.WeaponCategory1 + i;
            if (input.IsRisingEdge(slot))
            {
                wanted = i;
            }
        }

        if (wanted < 0 || wanted >= _categories.Count)
        {
            return;
        }

        // A different category restarts at its first member; the same one steps on, which is what makes
        // pressing 2 twice walk from the Glock to the Colt.
        if (wanted != _categoryIndex)
        {
            _memberIndex = 0;
        }

        HumanPlayerWeaponCategory category = _categories[wanted];
        if (category.Count == 0)
        {
            _categoryIndex = wanted;
            return;
        }

        for (int tried = 0; tried < category.Count; tried++)
        {
            int id = category.WeaponIds[_memberIndex];
            _memberIndex++;
            if (_memberIndex >= category.Count)
            {
                _memberIndex = 0;
            }

            if (IndexOf(id) >= 0)
            {
                _categoryPick = id;
                break;
            }
        }

        _categoryIndex = wanted;
    }

    private void SelectBinoculars(PlayerInputState input)
    {
        if (!input.IsRisingEdge(PlayerInputSlot.Binoculars))
        {
            return;
        }

        int binocularIndex = _carried.FindIndex(
            carried => carried.Weapon.Category == WeaponCategory.Binocular);
        if (binocularIndex < 0)
        {
            return;
        }

        if (_heldIndex == binocularIndex)
        {
            _binocularTogglePending = true;
            _binocularOpenPending = false;
            _pendingWeaponId = IndexOf(_lastNonBinocularWeaponId);
            if (_pendingWeaponId < 0)
            {
                _pendingWeaponId = _carried.FindIndex(
                    carried => carried.Weapon.Category != WeaponCategory.Binocular);
            }

            if (_pendingWeaponId >= 0)
            {
                _pendingWeaponId = _carried[_pendingWeaponId].Weapon.WeaponId;
            }
        }
        else
        {
            _binocularTogglePending = false;
            _binocularOpenPending = true;
            _pendingWeaponId = _carried[binocularIndex].Weapon.WeaponId;
        }
    }

    // 0x40F760, reduced to the edges that reach it in practice.
    private void CycleWeapons(PlayerInputState input)
    {
        int step = 0;
        if (_cycleIndex == NoCycleIndex || input.IsRisingEdge(PlayerInputSlot.NextWeapon))
        {
            step = 1;
        }

        if (input.IsRisingEdge(PlayerInputSlot.PreviousWeapon))
        {
            step = -1;
        }

        int heldId = InHand is { } held ? held.Weapon.WeaponId : -1;

        // The cycler falls through to the category's answer when neither edge fired -- the original
        // reads it out of the scratch slot at exactly this point.
        if (step == 0 || _cycle.Count == 0)
        {
            if (_categoryPick != -1 && _categoryPick != heldId && IndexOf(_categoryPick) >= 0)
            {
                _pendingWeaponId = _categoryPick;
            }

            return;
        }

        for (int n = _cycle.Count; n > 0; n--)
        {
            _cycleIndex += step;
            if (_cycleIndex >= _cycle.Count)
            {
                _cycleIndex = 0;
            }
            else if (_cycleIndex < 0)
            {
                _cycleIndex = _cycle.Count - 1;
            }

            int candidate = _cycle[_cycleIndex];

            // 0x408720: carried, and selectable -- which in retail every carried weapon is, because
            // the runtime loader always stores 1 in the record's +1756.
            if (IndexOf(candidate) >= 0)
            {
                if (candidate != heldId)
                {
                    _pendingWeaponId = candidate;
                    SetBinocularOpenPending(candidate);
                }

                return;
            }
        }
    }

    private void Step(PlayerInputState input)
    {
        // A transition runs its target's entry immediately, so the loop is bounded rather than
        // recursive; the graph is a star and cannot cycle more than a couple of hops in one tick.
        for (int guard = 0; guard <= HumanWeaponGraph.StateCount; guard++)
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
        HumanWeaponState.Medipack => Medipack(),
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

            // Level-triggered, deliberately: there is no edge test here in the original, which is what
            // lets an automatic weapon keep firing while the button is down.
            if (Flags.HasFlag(HumanWeaponFlagWord.Fire))
            {
                switch (held.Weapon.Category)
                {
                    case (WeaponCategory)StrikingCategory:
                        return HumanWeaponState.Striking;

                    case (WeaponCategory)ThrownCategory when clip != 0:
                        return HumanWeaponState.Throwing;

                    case (WeaponCategory)PlacedCategory when clip != 0:
                        return HumanWeaponState.Placing;

                    case (WeaponCategory)MedipackCategory:
                        if (_medipackAwaitingRelease)
                        {
                            break;
                        }

                        return HumanWeaponState.Medipack;

                    case (WeaponCategory)ThrownCategory:
                    case (WeaponCategory)PlacedCategory:
                        break;

                    default:
                        return clip != 0 ? HumanWeaponState.Firing : HumanWeaponState.DryFire;
                }
            }

            // The manual reload and the automatic one are the same branch -- the `clip == 0` disjunct
            // is why an empty magazine tops itself up without being asked.
            bool asked = Flags.HasFlag(HumanWeaponFlagWord.Reload) || clip == 0;
            if (asked && reserve != 0 && clip < held.Weapon.RoundsPerClip)
            {
                return HumanWeaponState.Reloading;
            }

            if (Flags.HasFlag(HumanWeaponFlagWord.Peek) && Flags.HasFlag(HumanWeaponFlagWord.OnGround))
            {
                return HumanWeaponState.Peeking;
            }
        }

        if (_pendingWeaponId != -1)
        {
            SetBinocularOpenPending(_pendingWeaponId);
            return HumanWeaponState.ChangingWeapon;
        }

        if (input.IsRisingEdge(PlayerInputSlot.MapComputer) && Flags.HasFlag(HumanWeaponFlagWord.OnGround))
        {
            return HumanWeaponState.MapComputer;
        }

        return HumanWeaponState.Ready;
    }

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
            bool asked = input.IsSet(PlayerInputSlot.Reload) || clip == 0;
            if (asked && reserve != 0 && clip < held.Weapon.RoundsPerClip)
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
        if (_firedThisTick)
        {
            Request(WeaponAction.FireTimed);
        }

        return ended && !triggerDown ? HumanWeaponState.Ready : HumanWeaponState.Firing;
    }

    // 0x40FA80, the knife. The counter advances on rounds, not on ticks.
    private HumanWeaponState Striking(PlayerInputState input)
    {
        if (input.IsSet(PlayerInputSlot.Fire) && FireVariant < FireVariantCount)
        {
            _wantFire = true;
        }

        if (_firedThisTick)
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

    /// <summary>Gets how long the current or last throw was charged, in ticks.</summary>
    /// <remarks>
    /// The original's <c>Human+1248</c>: the ticks the fire button was held in the throw state,
    /// which <c>0x462E50</c> clamps to 3–60 and feeds the power-law strength curve.
    /// </remarks>
    public int ThrowChargeTicks => _throwChargeTicks;

    /// <summary>Turns a throw's charge into its release speed, in metres a second.</summary>
    /// <param name="muzzleVelocity">The weapon's own muzzle velocity — fifteen for both grenades.</param>
    /// <param name="chargeTicks">How many ticks the button was held.</param>
    /// <returns>The speed the grenade leaves at.</returns>
    /// <remarks>
    /// <para>
    /// The curve is retail's, from <c>0x462E50</c>'s category-4 special case:
    /// <c>t = clamp(held, 3, 60); s = pow(t·50, 0.7) + t·0.1</c>, times a per-weapon scale — so a
    /// tenth-of-a-second tap lobs and a two-second wind-up hurls, on a 0.7 power law rather than a
    /// line.
    /// </para>
    /// <para>
    /// <b>The scale's field (<c>record+1632</c>) is unresolved</b>, so the calibration is chosen and
    /// measured in play: a tap throws at <see cref="MinimumThrowShare"/> of the muzzle velocity —
    /// a deliberate short toss that still clears a crate rather than a drop at the feet — and a
    /// full two-second charge at <see cref="MaximumThrowShare"/>, the long hurl. The retail curve
    /// shapes everything between.
    /// </para>
    /// </remarks>
    public static float ChargedThrowSpeed(float muzzleVelocity, int chargeTicks)
    {
        float held = Math.Clamp(chargeTicks, 3, 60);
        float curve = MathF.Pow(held * 50f, 0.7f) + (held * 0.1f);
        float tap = MathF.Pow(3f * 50f, 0.7f) + 0.3f;
        float fullCharge = MathF.Pow(60f * 50f, 0.7f) + 6f;

        float share = (curve - tap) / (fullCharge - tap);
        return muzzleVelocity * (MinimumThrowShare + ((MaximumThrowShare - MinimumThrowShare) * share));
    }

    /// <summary>What fraction of the muzzle velocity a minimal tap throws at.</summary>
    public const float MinimumThrowShare = 0.45f;

    /// <summary>What fraction of the muzzle velocity a full charge throws at.</summary>
    /// <remarks>
    /// Above one on purpose: the table's fifteen metres a second reads as the ordinary throw, and a
    /// two-second wind-up should visibly out-range it.
    /// </remarks>
    public const float MaximumThrowShare = 1.8f;

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

        if (_firedThisTick)
        {
            // The throw itself is the SECOND fire animation where the record carries one -- the
            // first is the wind-up the entry already played and holding it again would read as the
            // hand pocketing the grenade rather than releasing it.
            Request(WeaponAction.Fire, InHand?.Weapon.FireAnimation2 >= 0 ? 1 : 0);
        }

        return Flags.HasFlag(HumanWeaponFlagWord.ClipEnded)
            ? HumanWeaponState.Ready
            : HumanWeaponState.Throwing;
    }

    // 0x479990, the gun's own reload handler, which 0x40F180 drives. It is a two-phase machine: play
    // the reload animation, and move the rounds only when that animation has finished. So the reload's
    // DURATION is the clip's length -- the roster's `Reload Time` (+1596) is not read on this path at
    // all, which is a good example of a field that exists for the HUD.
    private HumanWeaponState Reloading()
    {
        if (!Flags.HasFlag(HumanWeaponFlagWord.ClipEnded))
        {
            return HumanWeaponState.Reloading;
        }

        InHand?.Gun.Reload(Ammunition);
        _actionComplete = true;
        return HumanWeaponState.Ready;
    }

    private HumanWeaponState Medipack()
    {
        // ClipEnded belongs to the outgoing animation on the tick that enters this state. Let the
        // medipack advance once before accepting that flag, otherwise a completed outgoing clip
        // prevents the newly requested healing animation from being used.
        if (!_medipackAdvanced)
        {
            _medipackAdvanced = true;
            return HumanWeaponState.Medipack;
        }

        // Keep the state alive until the authored injection clip ends. The consume latch only blocks
        // a second use; it must not shorten the animation or the healing clip is cut off next tick.
        if (_medipackUsed && !_medipackEffectStarted)
        {
            return HumanWeaponState.Ready;
        }

        // Defer the transition by one machine tick. PlayerCamera samples the clip and applies the
        // final healing fraction after PlayerWeapons.Tick has observed ClipEnded; returning Ready
        // immediately would hide that completion tick.
        if (_medipackClipCompletionPending)
        {
            return HumanWeaponState.Ready;
        }

        if (Flags.HasFlag(HumanWeaponFlagWord.ClipEnded) && _medipackEffectStarted)
        {
            _medipackClipCompletionPending = true;
        }

        return HumanWeaponState.Medipack;
    }

    private void SetBinocularOpenPending(int weaponId)
    {
        if (IndexOf(weaponId) >= 0 && _carried[IndexOf(weaponId)].Weapon.Category == WeaponCategory.Binocular)
        {
            _binocularOpenPending = true;
        }
        else
        {
            _binocularOpenPending = false;
        }
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

            int index = IndexOf(_pendingWeaponId);
            if (index >= 0)
            {
                if (InHand is { Weapon.Category: not WeaponCategory.Binocular } previous)
                {
                    _lastNonBinocularWeaponId = previous.Weapon.WeaponId;
                }

                _heldIndex = index;
            }

            _pendingWeaponId = -1;
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
        if (_pendingWeaponId != -1)
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

            // 0x40F4F0. Zeroed here: a transition runs its target's step in the same tick, and that
            // step counts this first held tick to one. The entry also plays the weapon's first fire
            // animation -- the pin-and-wind-up the hand holds while the throw charges -- exactly as
            // the retail entry plays record +1668.
            case HumanWeaponState.Throwing:
                _throwChargeTicks = 0;
                _throwReleased = false;
                Request(WeaponAction.Fire);
                break;

            case HumanWeaponState.Medipack:
                _medipackUsed = false;
                _medipackAdvanced = false;
                _medipackEffectStarted = false;
                _medipackClipCompletionPending = false;
                _medipackAppliedProgress = 0f;
                MedipackStartedThisTick = true;
                Request(WeaponAction.Fire);
                break;

            case HumanWeaponState.Reloading:
                Request(WeaponAction.Reload);
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

    private int IndexOf(int weaponId)
    {
        for (int i = 0; i < _carried.Count; i++)
        {
            if (_carried[i].Weapon.WeaponId == weaponId)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Gets whether a one-shot action finished on the last tick — the original's <c>Human+1232</c>.</summary>
    public bool ActionCompleted => _actionComplete;
}
