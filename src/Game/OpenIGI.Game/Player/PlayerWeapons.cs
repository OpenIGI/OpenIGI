using OpenIGI.Core.Numerics;
using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.Player;

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

    /// <summary>The retail weapon id used by the M16A2/M203 alternate-fire callback.</summary>
    public const string M16A2WeaponScriptId = "WEAPON_ID_M16A2";

    /// <summary>The roster id for the retail two-Uzi weapon whose model owns the dual-hand child task.</summary>
    public const string DualUziWeaponScriptId = "WEAPON_ID_UZIX2";

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
    private readonly List<M203Projectile> _m203Shots = [];
    private readonly IgiRandom _random;
    private readonly PlayerWeaponSelector _selector;
    private readonly PlayerWeaponStateMachine _machine;

    private AmmoType? _m203Ammunition;
    private bool _firedThisTick;
    private int _shotSequence;
    private HeldGun? _mineGun;
    private bool _mineTriggerDown;
    private PlayerCheatOptions _cheats = PlayerCheatOptions.Retail;

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
        _selector = new PlayerWeaponSelector(_carried, weaponCycle);
        _random = random ?? new IgiRandom();
        _machine = new PlayerWeaponStateMachine(this, _selector);
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
        _selector.UseCategories(categories);
    }

    /// <summary>Gets the state the machine is in.</summary>
    public HumanWeaponState State => _machine.State;

    /// <summary>Gets the state the machine was in before the current one.</summary>
    public HumanWeaponState PreviousState => _machine.PreviousState;

    /// <summary>Gets the flag word the prologue last built.</summary>
    public HumanWeaponFlagWord Flags => _machine.Flags;

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

    /// <summary>Gets the cheats in force on this inventory.</summary>
    public PlayerCheatOptions Cheats => _cheats;

    /// <summary>Gets whether ammunition is unlimited (<c>byte_56E214</c>), which the one-shot states test before the pouch.</summary>
    internal bool InfiniteAmmo => _cheats.InfiniteAmmo;

    /// <summary>
    /// Gets the mine the weapon asked to lay this tick (<c>0x4755A0</c>): the eye and the view's direction, on the tick the
    /// trigger byte rises; <see langword="null"/> otherwise. The owner traces it and reports back with
    /// <see cref="MineLaid"/>.
    /// </summary>
    public MinePlacementRequest? MinePlacement { get; private set; }

    /// <summary>The mine the weapon asked for went down: a round leaves the magazine and the shot is counted (<c>0x4755A0</c>).</summary>
    public void MineLaid()
    {
        if (InHand is { } held && held.Gun.DrawsFromMagazine && held.Gun.State.RoundsInClip > 0)
        {
            held.Gun.State.RoundsInClip--;
        }

        _firedThisTick = true;
        unchecked
        {
            _shotSequence++;
        }
    }

    /// <summary>
    /// Consumes a binocular toggle after the requested weapon has reached the hand. The request is
    /// retained across the equip transition, matching the original's one-tick action hand-off.
    /// </summary>
    public bool ConsumeBinocularToggle() => _selector.ConsumeBinocularToggle();

    /// <summary>Gets the weapon in hand, or <see langword="null"/> before one has been selected.</summary>
    /// <remarks>Slot 0 of the original's three-pointer array at <c>Human+1220</c>.</remarks>
    public CarriedWeapon? InHand => _selector.InHand;

    /// <summary>
    /// Gets the fire-variant counter — the original's <c>Human+1256</c>.
    /// </summary>
    /// <remarks>
    /// Counts <b>rounds that went off</b>, not ticks. <see cref="HumanWeaponState.Striking"/> zeroes it
    /// on entry and stops asking for rounds once it reaches <see cref="FireVariantCount"/>.
    /// </remarks>
    public int FireVariant => _machine.FireVariant;

    /// <summary>Gets the accumulated view kick in pitch, in radians — <c>HumanView+296</c>.</summary>
    public float RecoilPitch { get; private set; }

    /// <summary>Gets whether the gun's trigger stood held this tick, after its burst and magazine gates (<c>0x478BA0</c>).</summary>
    /// <remarks>What an automatic's fire loop follows: it runs while this holds and ends, with its tail, the tick it does not.</remarks>
    public bool TriggerHeldThisTick { get; private set; }

    /// <summary>Gets the accumulated view kick in yaw, in radians — <c>HumanView+308</c>.</summary>
    public float RecoilYaw { get; private set; }

    /// <summary>Gets whether a round went off on the last <see cref="ResolveFire"/>.</summary>
    public bool FiredThisTick => _firedThisTick;

    /// <summary>Forgets the last round, when something else takes the trigger — a mounted gun.</summary>
    /// <remarks>
    /// The machine does not tick while the player is on a stand, so without this the round fired on
    /// the tick before mounting would read as fired for the whole of the stay, and again on the way off.
    /// </remarks>
    public void ClearFiredThisTick() => _firedThisTick = false;

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

    /// <summary>Gets the M203 projectiles produced by the last alternate-fire resolution.</summary>
    public IReadOnlyList<M203Projectile> M203Shots => _m203Shots;

    /// <summary>Gets whether a medipack was consumed during the most recent weapon tick.</summary>
    public bool MedipackUsedThisTick => _machine.MedipackUsedThisTick;

    /// <summary>Gets whether the medipack animation was started during the most recent weapon tick.</summary>
    /// <remarks>
    /// Healing is deliberately not allowed on this tick. The retail task raises the syringe action
    /// first and only consumes the medipack once that action has advanced; otherwise the health bar,
    /// reserve count and sound lead the visible left-arm injection by one animation.
    /// </remarks>
    public bool MedipackStartedThisTick => _machine.MedipackStartedThisTick;

    /// <summary>Gets the weapon action the rig should be playing, or <see langword="null"/>.</summary>
    /// <remarks>
    /// The original writes this to <c>Human+3416</c> inside <c>0x4137E0</c> and the animation follows;
    /// here the caller drives the rig from it, so it is exposed rather than applied.
    /// </remarks>
    public WeaponAction? RequestedAction => _machine.RequestedAction;

    /// <summary>Gets whether the retail first-person weapon is hidden for the current reload action.</summary>
    /// <remarks>
    /// <c>TASKTYPE_GUNX2</c> reloads are the special first-person hand presentation: the two Uzi models
    /// leave the view while the reload sound/action runs. The native M16A2 path still keeps its gun
    /// attachment while <c>0x4137E0</c> dispatches view clip 5, so hiding every weapon here made its
    /// retail reload appear missing.
    /// </remarks>
    public bool HideFirstPersonWeapon =>
        (State == HumanWeaponState.Reloading || CurrentAction == WeaponAction.Reload)
        && string.Equals(InHand?.Weapon.ScriptId, DualUziWeaponScriptId, StringComparison.Ordinal);

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
    public int RequestedFireVariant => _machine.RequestedFireVariant;

    /// <summary>Gets the action the rig is currently playing — the original's <c>Human+3416</c>.</summary>
    /// <remarks>
    /// <c>0x4137E0</c> writes it unconditionally at the top of the dispatch, and the Ready state
    /// compares it against what the locomotion side has asked for.
    /// </remarks>
    public WeaponAction CurrentAction => _machine.CurrentAction;

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
        ApplyCheatsTo(gun);
        return gun;
    }

    /// <summary>Gets whether a weapon is carried.</summary>
    /// <param name="weaponId">The weapon's <see cref="WeaponType.WeaponId"/>.</param>
    /// <returns><see langword="true"/> when it is carried.</returns>
    public bool HasWeapon(int weaponId) => _selector.IndexOf(weaponId) >= 0;

    /// <summary>Switches to a weapon on the next tick, as the remaster's weapon wheel picks it (<see cref="WeaponWheel"/>).</summary>
    /// <param name="weaponId">The weapon's <see cref="WeaponType.WeaponId"/>; one not carried, or the one in hand, changes nothing.</param>
    public void RequestWeapon(int weaponId) => _selector.Pick(weaponId);

    /// <summary>Appends a weapon to the switch cycle unless it is already in it.</summary>
    /// <param name="weaponId">The weapon's <see cref="WeaponType.WeaponId"/>.</param>
    /// <remarks>
    /// For weapons the level's <c>DefineHumanPlayerWeaponCycle</c> does not list -- the all-weapons
    /// cheat's -- which would otherwise be carried and never reachable with the cycle keys.
    /// </remarks>
    public void EnsureCycleContains(int weaponId) => _selector.EnsureCycleContains(weaponId);

    /// <summary>Puts a set of cheats in force on everything carried, and on anything carried later.</summary>
    /// <param name="cheats">The cheats, or <see cref="PlayerCheatOptions.Retail"/> for none.</param>
    /// <remarks>Cheap to call every tick: an unchanged set does nothing.</remarks>
    public void ApplyCheats(PlayerCheatOptions cheats)
    {
        if (cheats == _cheats)
        {
            return;
        }

        _cheats = cheats;
        foreach (CarriedWeapon carried in _carried)
        {
            ApplyCheatsTo(carried.Gun);
        }

        if (cheats.NoRecoil)
        {
            RecoilPitch = 0f;
            RecoilYaw = 0f;
        }
    }

    /// <summary>Associates the separately declared M203 ammunition with the carried M16A2.</summary>
    /// <remarks>
    /// The retail loadout declares M16A2's primary calibre on the gun and M203 rounds as a separate
    /// <c>AddAmmo(AMMO_ID_M203)</c> child. It is therefore not correct to replace the gun's cached
    /// primary ammunition record with this one.
    /// </remarks>
    public void SetM203Ammunition(AmmoType ammunition)
    {
        ArgumentNullException.ThrowIfNull(ammunition);
        _m203Ammunition = ammunition;
    }

    /// <summary>Applies a <c>GunPickup</c> to this inventory.</summary>
    /// <param name="weapon">The roster weapon the world pickup names.</param>
    /// <param name="ammo">Its resolved ammunition record, or <see langword="null"/> for weapons that consume none.</param>
    /// <param name="rounds">The pickup's magazine payload.</param>
    /// <returns>Whether a gun was added or reserve ammunition was added.</returns>
    /// <remarks>
    /// <para>This is <c>Human::GunPickup</c> at <c>0x45FFC0</c>. A firearm the human does not yet own is
    /// added without selecting it, with the payload in its magazine. Medipack is the native exception:
    /// its payload is an <c>AMMO_ID_MEDIPACK</c> reserve count, never a magazine charge. When the weapon
    /// already exists, retail adds the payload to the matching loose-ammunition entry and never
    /// duplicates the gun.</para>
    /// <para><c>GunPickup::Update</c> supplies the weapon record's clip size as <paramref name="rounds"/>,
    /// so authored pickups are one full magazine. Keeping the argument explicit preserves the native
    /// message boundary and lets scripted pickups provide a different payload later.</para>
    /// </remarks>
    public WeaponPickupResult PickUp(WeaponType weapon, AmmoType? ammo, int rounds)
    {
        ArgumentNullException.ThrowIfNull(weapon);

        // FUN_0040FBE0 reads and decrements the AMMO_ID_MEDIPACK record directly at +4. It never
        // reads Gun+RoundsInClip, and HeldGun.Tick deliberately does not fill that field for this
        // category. Put both first-time and duplicate medipack pickups in that same native reserve
        // slot so the HUD and the injection handler see the authored 50/100 charge amount.
        if (weapon.Category == (WeaponCategory)MedipackCategory)
        {
            int ammoId = ammo?.AmmoId ?? MedipackAmmoId;
            if (!_carried.Any(carried => carried.Weapon.WeaponId == weapon.WeaponId))
            {
                Carry(weapon, ammo);
                Ammunition.Add(ammoId, rounds);
                return new WeaponPickupResult(AddedWeapon: true, AddedReserveRounds: rounds);
            }

            Ammunition.Add(ammoId, rounds);
            return new WeaponPickupResult(AddedWeapon: false, AddedReserveRounds: rounds);
        }

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
    /// <c>HumanLocomotionWeaponActions</c> — the original's <c>Human+3420</c>. The Ready state
    /// resyncs to it, which is how a weapon goes back to its stand pose after a shot without either
    /// machine referring to the other.
    /// </param>
    /// <param name="crossedEvent">Whether the view's clip crossed an event on its last tick, which the throw reads.</param>
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
        WeaponAction locomotionAction = WeaponAction.Stand,
        Func<int, bool>? crossedEvent = null)
    {
        ArgumentNullException.ThrowIfNull(input);

        // Before the flag word is built, so the empty-magazine auto-reload below never sees an empty
        // magazine while there is a reserve to refill it from.
        if (_cheats.NoReload && !_cheats.InfiniteAmmo)
        {
            InHand?.Gun.Reload(Ammunition);
        }

        _selector.SelectBinoculars(input);
        _selector.SelectCategory(input);
        _selector.CycleWeapons(input);

        _machine.Tick(input, clipEnded, onGround, locomotionAction, crossedEvent);
    }

    /// <summary>Executes one cooldown-eligible medipack dose while the syringe loop is held.</summary>
    /// <param name="health">The health accumulator owned by the player.</param>
    /// <returns><see langword="true"/> when one medipack was consumed.</returns>
    /// <param name="animationProgress">Retained for callers compiled against the former API; retail does not gate a dose on this value.</param>
    public bool TryUseMedipack(HumanHealth health, float animationProgress = 1f)
    {
        ArgumentNullException.ThrowIfNull(health);
        return _machine.TryUseMedipack(health);
    }

    /// <summary>
    /// Synchronizes this weapon machine with the map-computer owner. The human state transition itself
    /// is performed by <c>HumanPlayer</c>; this mirrors its state-11 half so no fire/reload state survives
    /// behind the computer.
    /// </summary>
    public void SetMapComputerOpen(bool open) => _machine.SetMapComputerOpen(open);

    /// <summary>Immediately stops weapon activity because the owning player has died.</summary>
    /// <remarks>
    /// The death transition does not run another normal player tick. Clearing the outgoing firing
    /// request here therefore matters: otherwise its last <see cref="FiredThisTick"/> value and firing
    /// state remain latched for the presentation layer after the player is dead.
    /// </remarks>
    /// <summary>Takes the weapons out of death, as a co-op player is brought back.</summary>
    public void Revive() => _machine.Revive();

    /// <summary>Stops the weapons because the player has died.</summary>
    public void StopForDeath()
    {
        _machine.StopForDeath();
        _firedThisTick = false;
        _shots.Clear();
        _m203Shots.Clear();
    }

    /// <summary>Refunds a projectile whose simulation could not accept the round.</summary>
    /// <returns><see langword="true"/> when one fired round was returned to its source.</returns>
    /// <remarks>
    /// The launcher simulation has a finite live-object pool. A rejected spawn is not an impact and
    /// must not consume ammunition or leave the presentation layer reporting a shot that never entered
    /// the world. This is called immediately after <see cref="ResolveFire"/> for a rejected launcher
    /// or M203 round, before the next weapon tick clears the scratch projectile list.
    /// </remarks>
    public bool RefundRejectedSimulationShot()
    {
        if (_m203Shots.Count > 0)
        {
            if (_m203Ammunition is null)
            {
                return false;
            }

            // Under infinite ammunition the launch took nothing, so there is nothing to give back.
            if (!_cheats.InfiniteAmmo)
            {
                Ammunition.Add(_m203Ammunition.AmmoId, _m203Shots.Count);
            }

            _m203Shots.Clear();
            _firedThisTick = false;
            if (_shotSequence > 0)
            {
                _shotSequence--;
            }

            return true;
        }

        if (_shots.Count == 0
            || InHand is not { } held
            || !held.Gun.DrawsFromMagazine
            || held.Gun.State.RoundsInClip < 0)
        {
            return false;
        }

        int bulletsPerRound = Math.Max(1, held.Weapon.BulletsPerRound);
        int rounds = _shots.Count / bulletsPerRound;
        if (rounds <= 0)
        {
            return false;
        }

        held.Gun.State.RoundsInClip += rounds;
        _shots.Clear();
        _firedThisTick = false;
        if (_shotSequence > 0)
        {
            _shotSequence--;
        }

        return true;
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
        _m203Shots.Clear();
        _firedThisTick = false;
        TriggerHeldThisTick = false;
        MinePlacement = null;

        if (InHand is not { } held)
        {
            return false;
        }

        // The mine weapon's use handler (0x4755A0) lays one only on the tick its trigger rises -- the gun keeps last tick's
        // byte (+0x24C) -- and takes a round only if it did; nothing else of a gun's firing applies.
        if (held.Weapon.Category == (WeaponCategory)PlacedCategory)
        {
            if (!ReferenceEquals(_mineGun, held.Gun))
            {
                _mineGun = held.Gun;
                _mineTriggerDown = false;
            }

            if (_machine.WantFire && !_mineTriggerDown)
            {
                MinePlacement = new MinePlacementRequest(eye, forward, _machine.WantRemoteMine);
            }

            _mineTriggerDown = _machine.WantFire;
            return false;
        }

        // Native 0x40F4A0 raises Human+0x4D2 and 0x473510 consumes that marker in GunM16A2's
        // alternate callback. It never calls the ordinary magazine path, so an M203 launch must not
        // consume a 5.56 round or sample the rifle's bullet spread/recoil.
        if (_machine.WantAlternateFire && IsM16A2(held.Weapon))
        {
            if (_m203Ammunition is not { } m203 || !HasM203Round())
            {
                return false;
            }

            if (!_cheats.InfiniteAmmo)
            {
                Ammunition.Take(m203.AmmoId, 1);
            }

            WeaponConfigTask? config = held.Weapon.RetailConfig;
            float muzzleVelocity = M203Physics.DefaultMuzzleVelocityKilometresPerHour;
            float explosionRadius = M203Physics.DefaultExplosionRadiusMetres;
            float activateRadius = M203Physics.DefaultActivateRadiusMetres;
            if (config is not null)
            {
                if (WeaponConfigRegistry.TryGetRealTag(config, "M203 - Muzzle velocity (km/h)", out float configured))
                {
                    muzzleVelocity = configured;
                }
                if (WeaponConfigRegistry.TryGetRealTag(config, "M203 - Explosion radius (meters)", out configured))
                {
                    explosionRadius = configured;
                }
                if (WeaponConfigRegistry.TryGetRealTag(config, "M203 - Activate radius (meters)", out configured))
                {
                    activateRadius = configured;
                }
            }
            string model = config is not null
                && WeaponConfigRegistry.TryGetStringTag(config, "M203 - Model", out string configuredModel)
                && configuredModel.Length > 0
                ? configuredModel
                : "108_05_1";
            _m203Shots.Add(new M203Projectile(
                eye,
                forward,
                muzzleVelocity,
                explosionRadius,
                activateRadius,
                model,
                M203Physics.LaunchOrientation(forward)));
            _firedThisTick = true;
            unchecked
            {
                _shotSequence++;
            }

            return true;
        }

        held.Gun.Tick(_machine.WantFire);

        GunFireResult result = held.Gun.Fire(_machine.WantFire, eye, forward, right, up, _random);
        _firedThisTick = result.Fired;

        // 0x478BA0 clears its trigger for a finished burst and an empty magazine, but not for the cooldown between rounds.
        TriggerHeldThisTick = _machine.WantFire
            && result.Block is not (GunFireBlock.BurstComplete or GunFireBlock.Empty or GunFireBlock.TriggerReleased);

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
        if (_cheats.NoRecoil)
        {
            recoilScale = 0f;
        }

        RecoilPitch = ((result.RecoilX * recoilScale) + RecoilPitch) * RecoilDamping;
        RecoilYaw = ((result.RecoilZ * recoilScale) + RecoilYaw) * RecoilDamping;
        return result.Fired;
    }

    /// <summary>Gets how long the current or last throw was charged, in ticks.</summary>
    /// <remarks>
    /// The original's <c>Human+1248</c>: the ticks the fire button was held in the throw state,
    /// which <c>0x462E50</c> clamps to 3–60 and feeds the power-law strength curve.
    /// </remarks>
    public int ThrowChargeTicks => _machine.ThrowChargeTicks;

    /// <summary>Gets whether a grenade throw is being charged — the hand cooking it.</summary>
    /// <remarks>
    /// The retail throw handler starts <c>grenade_tick_1</c> as it enters the throw state
    /// (<c>0x40F590</c>) and stops it on release (<c>0x40F640</c>). This is that window: in the throw
    /// state with the fire button still held.
    /// </remarks>
    public bool IsChargingThrow => State == HumanWeaponState.Throwing && _machine.ThrowCooking;

    /// <summary>Turns a throw's cook into the grenade's speed, in units a tick -- <c>0x462E50</c>'s grenade case.</summary>
    /// <param name="throwSpeed">The weapon row's thirteenth argument (record <c>+1632</c>), the muzzle velocity: 15 for both grenades.</param>
    /// <param name="chargeTicks">The cook, <c>Human+1248</c>.</param>
    /// <returns>The speed the grenade leaves at, along the view's forward axis, in units a tick.</returns>
    /// <remarks>
    /// <c>t = clamp(cook, 3, 60); k = (pow(t * 50, 0.7) + (float)t * 0.1f) * throwSpeed</c>, which replaces the thrower's own
    /// velocity in the fire message: about 3.7 m/s for the shortest cook and 30.5 m/s for a two-second one.
    /// </remarks>
    public static double ThrowSpeedUnitsPerTick(float throwSpeed, int chargeTicks)
    {
        double t = chargeTicks;
        if (3.0 > t)
        {
            t = 3.0;
        }
        else if (60.0 < t)
        {
            t = 60.0;
        }

        float tf = (float)t;
        return (Math.Pow(t * 50.0, 0.7) + (tf * (double)0.1f)) * throwSpeed;
    }

    /// <summary>Gets whether a one-shot action finished on the last tick — the original's <c>Human+1232</c>.</summary>
    public bool ActionCompleted => _machine.ActionComplete;

    /// <summary>Gets whether an M203 round is available — declared, and in the pouch or under infinite ammunition.</summary>
    internal bool HasM203Round() =>
        _m203Ammunition is not null
        && (_cheats.InfiniteAmmo || Ammunition.Count(_m203Ammunition.AmmoId) > 0);

    // Infinite ammunition fills the magazine once and then keeps every fired round in it; the reserve is
    // never touched, so the HUD goes on showing what the player actually picked up.
    private void ApplyCheatsTo(HeldGun gun)
    {
        gun.PreserveMagazine = _cheats.InfiniteAmmo;
        if (gun.PreserveMagazine && gun.ConsumesAmmunition)
        {
            gun.State.RoundsInClip = gun.Weapon.RoundsPerClip;
        }
    }

    /// <summary>Gets whether a weapon is the retail M16A2, whose alternate fire launches an M203 round.</summary>
    internal static bool IsM16A2(WeaponType weapon) =>
        string.Equals(weapon.ScriptId, M16A2WeaponScriptId, StringComparison.Ordinal);
}
