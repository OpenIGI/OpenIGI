using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>The firing half of a human's <c>Weapon</c> task: the trigger, the shot, the delay and the kick.</summary>
/// <remarks>
/// <para>
/// A shot starts in <see cref="Fire"/> (<c>Weapon_Fire</c>, <c>igi2 0x51DE90</c>), which decides whether the trigger may
/// fire at all, asks the holder where it fires from with the holder's inaccuracy, and sends the shot to itself as a
/// message: the position narrowed to whole centimetres and the orientation to three angles. The single-player campaign
/// hands the message straight to <c>Weapon_SingleFireHandler</c> (<c>0x51CD40</c>), which checks the ammunition and the
/// delay, rebuilds the position and orientation, runs the fire type's own handler, and spends what it fired.
/// </para>
/// <para>
/// Held fire is the weapon's: once its first shot has fired an automatic weapon fires again every tick in
/// <see cref="RunDelayed"/> (<c>Weapon_RunDelayedHandler</c>, <c>0x51C480</c>) until its rounds in succession run out
/// or the trigger comes up, and a burst let go early completes itself. The trigger comes up at the end of the holder's
/// tick in <see cref="UpdateStatus"/> (<c>Weapon_UpdateStatus</c>, <c>0x51E370</c>).
/// </para>
/// <para>
/// The status, the delay, the pin, the fuse and the bullets fired are the inventory's (they share <c>Weapon_s</c>);
/// what fires, shows and sounds is the fire type's handler and the <see cref="Effects"/>.
/// </para>
/// </remarks>
public sealed class Igi2WeaponFiring
{
    /// <summary>The fire sound's slot, the one the holder's death stops (<c>aptSoundSlotQTaskRef[0]</c>).</summary>
    public const int FireSoundSlot = 0;

    /// <summary>The grenade fuse's tick sound's slot.</summary>
    public const int FuseSoundSlot = 1;

    /// <summary>The ticks between a pulled grenade's ticks, <c>30</c>.</summary>
    public const int FuseTickInterval = 30;

    /// <summary>The sound a pulled grenade ticks with.</summary>
    public const string FuseTickSound = "grenade_tick";

    private readonly Igi2FireTypeState[] _fireTypes = [new(), new()];
    private readonly bool[] _completingBurst = new bool[Igi2WeaponTypeTask.FireTypeSlots];
    private readonly Igi2Inventory _inventory;
    private readonly Igi2WeaponTables _tables;
    private readonly Igi2FireTaskTypes _taskTypes;
    private readonly Func<object, IIgi2FireHandler?> _handlers;

    /// <summary>Initializes a new instance of the <see cref="Igi2WeaponFiring"/> class.</summary>
    /// <param name="inventory">What the human carries.</param>
    /// <param name="tables">The session's weapons and ammunition.</param>
    /// <param name="taskTypes">The task-type ids a fire type is tested against.</param>
    /// <param name="handlers">The handler for a fire type's parameters task, or <see langword="null"/> for one with none.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public Igi2WeaponFiring(Igi2Inventory inventory, Igi2WeaponTables tables, Igi2FireTaskTypes taskTypes, Func<object, IIgi2FireHandler?> handlers)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(handlers);
        _inventory = inventory;
        _tables = tables;
        _taskTypes = taskTypes;
        _handlers = handlers;
    }

    /// <summary>Gets what the human carries.</summary>
    public Igi2Inventory Inventory => _inventory;

    /// <summary>Gets or sets the human holding the weapon, the weapon task's parent; with none the weapon does nothing.</summary>
    public IIgi2WeaponOwner? Owner { get; set; }

    /// <summary>Gets or sets what firing shows and sounds.</summary>
    public IIgi2WeaponEffects Effects { get; set; } = Igi2NoWeaponEffects.Instance;

    /// <summary>Gets the weapon's bullets, the <c>GunShot</c> <c>Weapon_CreateHandler</c> (<c>igi2 0x51C320</c>) makes under it.</summary>
    public Igi2GunShot GunShot { get; } = new();

    /// <summary>Gets the flashes the weapon's shots made, its <c>MuzzleFlash</c> children.</summary>
    public Igi2MuzzleFlashes Flashes { get; } = new();

    /// <summary>Gets or sets where the grenades the weapon throws go (<c>Grenade_CreateProjectileHandler</c>, <c>igi2 0x484BE0</c>).</summary>
    public IIgi2GrenadeWorld Grenades { get; set; } = Igi2NoGrenades.Instance;

    /// <summary>Gets or sets the level the weapon's bullets and stabs strike, or <see langword="null"/> for none: then nothing is struck.</summary>
    public IIgi2GunShotWorld? World { get; set; }

    /// <summary>Gets or sets the level a proximity mine is laid in, or <see langword="null"/>: then none is laid.</summary>
    public IIgi2MineWorld? Mines { get; set; }

    /// <summary>Gets or sets the laser designator's module state a designator's fire marks its target in, or <see langword="null"/>.</summary>
    public Igi2LaserDesignatorState? LaserDesignator { get; set; }

    /// <summary>Gets or sets what the weapon's bullets and stabs show and sound where they strike.</summary>
    public IIgi2GunShotEffects ImpactEffects { get; set; } = Igi2NoGunShotEffects.Instance;

    /// <summary>Traces the bullets fired last tick (<c>GunShot_RunHandler</c>, <c>igi2 0x4C76E0</c>); with no level they are forgotten.</summary>
    /// <remarks>The weapon's flashes run after it, as its later children.</remarks>
    public void RunGunShot()
    {
        if (World is { } world)
        {
            GunShot.Run(Owner, HitProbability, world, ImpactEffects);
        }

        Flashes.Run();
    }

    /// <summary>
    /// Gets or sets how likely the weapon's bullets are to strike the player, <c>Weapon+0x64</c>: one, as
    /// <c>Weapon_CreateHandler</c> sets it, unless the holder's AI lowers it.
    /// </summary>
    public float HitProbability { get; set; } = 1f;

    /// <summary>Gets or sets the fire type last fired or let go, <c>nFireType</c> (<c>Weapon+0x34</c>).</summary>
    public int FireType { get; set; }

    /// <summary>Gets or sets a value indicating whether reloading takes nothing from what is carried, <c>isUnlimitedAmmo</c>.</summary>
    public bool IsUnlimitedAmmo { get; set; }

    /// <summary>Gets or sets a value indicating whether the weapon fires on by itself, <c>isAutoFire</c>.</summary>
    public bool IsAutoFire { get; set; }

    /// <summary>Gets the ticks the weapon has fired on by itself, <c>nAutoTimer</c>.</summary>
    public int AutoTimer { get; private set; }

    /// <summary>Gets or sets a value indicating whether the last shot kicked and the holder is yet to take it, <c>isKickback</c>.</summary>
    public bool IsKickback { get; set; }

    /// <summary>Gets the last shot's upward kick, in degrees, <c>vAlphaKickBack</c>.</summary>
    public float AlphaKickback { get; private set; }

    /// <summary>Gets the last shot's sideways kick, in degrees, <c>vGammaKickBack</c>.</summary>
    public float GammaKickback { get; private set; }

    /// <summary>Gets how fast the last shot's kick dies away on the view, <c>vKickBackDamping</c>.</summary>
    public float KickbackDamping { get; private set; }

    /// <summary>Gets how fast the holder's precision settles after the last shot, <c>vCrossHairDamping</c>.</summary>
    public float CrossHairDamping { get; private set; }

    /// <summary>
    /// Launches a fire type's projectile, <c>Weapon_eCreateProjectileQTaskEvent</c> sent to the projectile's type: a gun's
    /// bullets go to the weapon's <see cref="GunShot"/> with the gun's bullet parameters; any other projectile is the
    /// <see cref="Effects"/>'.
    /// </summary>
    /// <param name="launch">The launch.</param>
    /// <param name="fireTypeParameters">The fire type whose tagged values carry the projectile's parameters.</param>
    public void CreateProjectile(in Igi2ProjectileLaunch launch, GameTask fireTypeParameters)
    {
        ArgumentNullException.ThrowIfNull(fireTypeParameters);
        if (launch.ProjectileTaskType == _taskTypes.GunShot)
        {
            GunShot.CreateProjectile(launch, Igi2GunShotParameters.Of(fireTypeParameters));
        }
        else if (launch.ProjectileTaskType == _taskTypes.Grenade)
        {
            Grenades.Launch(Igi2GrenadeThrow.Of(launch, Igi2GrenadeParameters.Of(fireTypeParameters)));
        }
        else if (launch.ProjectileTaskType == _taskTypes.ImpactGrenade)
        {
            Grenades.LaunchImpact(launch, fireTypeParameters);
        }
        else if (launch.ProjectileTaskType == _taskTypes.Missile)
        {
            Grenades.LaunchMissile(launch, fireTypeParameters);
        }
        else
        {
            Effects.CreateProjectile(launch);
        }
    }

    /// <summary>Gets whether the weapon's zoom is up, and with an overlay, <c>Weapon+0xA0</c>.</summary>
    public Igi2WeaponOverlayStatus OverlayStatus { get; private set; }

    /// <summary>
    /// Sets the weapon's overlay status, <c>Weapon_SetOverlayStatus</c> (<c>igi2 0x51F1C0</c>): taking it down takes the
    /// holder's zoom off and the weapon's overlay with it.
    /// </summary>
    /// <param name="status">The status.</param>
    public void SetOverlayStatus(Igi2WeaponOverlayStatus status)
    {
        if (status == Igi2WeaponOverlayStatus.None)
        {
            Owner?.ZoomView?.DisableZoomMode();
            Effects.RemoveOverlay();
        }

        OverlayStatus = status;
    }

    /// <summary>
    /// Puts a carried weapon in hand, <c>Weapon_Activate</c> (<c>igi2 0x51E220</c>) as the single-player game sends it to
    /// <c>Weapon_SingleActivateHandler</c> (<c>0x51D600</c>): the weapon put away takes its zoom down first.
    /// </summary>
    /// <param name="item">The weapon.</param>
    /// <returns><see langword="true"/> when it went in hand; a weapon not carried does not.</returns>
    /// <remarks>
    /// Of the fire types' own handlers single-player ships only <c>ThermoSight_WeaponDeactivateHandler</c>: infrared off and the overlay
    /// down, which the overlay's coming down here and the player's tick-end infrared check (<c>HumanPlayer_RunDelayedHandler</c>) do.
    /// A spent throwable put away is dropped by the inventory (<see cref="Igi2Inventory.Activate"/>).
    /// </remarks>
    public bool Activate(Igi2WeaponItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!item.IsActive)
        {
            return false;
        }

        if (_inventory.Current is not null)
        {
            SetOverlayStatus(Igi2WeaponOverlayStatus.None);
        }

        return _inventory.Activate(item);
    }

    /// <summary>
    /// Marks the weapon's zoom up without touching the view or the overlay, as <c>Zoom_WeaponFireHandler</c> writes it
    /// (<c>igi2 0x525C23</c>, <c>0x525C32</c>).
    /// </summary>
    /// <param name="status">The status.</param>
    internal void MarkOverlayStatus(Igi2WeaponOverlayStatus status) => OverlayStatus = status;

    /// <summary>
    /// Steps the zoom of the weapon in hand, <c>Weapon_Zoom</c> (<c>igi2 0x51E140</c>) as the single-player game sends it
    /// to <c>Weapon_SingleZoomHandler</c> (<c>0x51D560</c>): each fire type, primary first, that answers the zoom keys.
    /// </summary>
    /// <param name="zoomIn">Whether the view narrows.</param>
    public void Zoom(bool zoomIn)
    {
        if (_inventory.Current is not { } item)
        {
            return;
        }

        for (int slot = 0; slot < Igi2WeaponTypeTask.FireTypeSlots; slot++)
        {
            if (item.WeaponType.FireTaskType(slot) != Igi2WeaponConstants.NoTaskType
                && item.WeaponType.FireType(slot) is { } parameters
                && _handlers(parameters) is IIgi2ZoomHandler handler)
            {
                handler.Zoom(new Igi2ZoomRequest(this, Owner, zoomIn, parameters));
            }
        }
    }

    /// <summary>Gets one fire type's trigger, <c>atFireTypeData[slot]</c>.</summary>
    /// <param name="slot">0 for the primary fire type, 1 for the secondary.</param>
    /// <returns>The trigger.</returns>
    public Igi2FireTypeState FireTypeState(int slot) => _fireTypes[slot];

    /// <summary>Gets whether a fire type is finishing a burst its trigger let go early, <c>aisCompletingBurst[slot]</c>.</summary>
    /// <param name="slot">0 for the primary fire type, 1 for the secondary.</param>
    /// <returns>The flag.</returns>
    public bool IsCompletingBurst(int slot) => _completingBurst[slot];

    /// <summary>Pulls a fire type's trigger, <c>Weapon_Fire</c> (<c>igi2 0x51DE90</c>).</summary>
    /// <param name="fireType">0 for the primary fire type, 1 for the secondary.</param>
    /// <remarks>
    /// <para>
    /// Nothing fires with nothing in hand, a holder that cannot say where it fires from, or no fire type in the slot.
    /// Nothing loaded leaves the weapon empty. A fire mode that fires a set number of rounds a pull waits out its delay
    /// (a zoom does not), fires only once a pull when that number is one or for the secondary fire type, and locks once
    /// the pull has fired them all.
    /// </para>
    /// <para>
    /// The pull's first shot marks the trigger down at this tick; a grenade held by the player backdates it by the time
    /// held, and the mark is then the primary's whatever was pulled. The trigger's held time is the difference of the two
    /// ticks' low bytes. The holder's frame is taken with its inaccuracy.
    /// </para>
    /// </remarks>
    public void Fire(int fireType)
    {
        if (_inventory.Current is not { } item || Owner is not { HasFireFrame: true } owner)
        {
            return;
        }

        Igi2WeaponTypeTask weaponType = item.WeaponType;
        if (weaponType.FireTaskType(fireType) == Igi2WeaponConstants.NoTaskType)
        {
            return;
        }

        if (item.Loaded(fireType) == 0)
        {
            _inventory.Status = Igi2WeaponStatus.Empty;
            return;
        }

        Igi2FireTypeState trigger = _fireTypes[fireType];
        int inSuccession = weaponType.RoundsInSuccession(item.ActiveFireMode);
        if (inSuccession != 0)
        {
            if ((_inventory.Delay != 0 && weaponType.FireTaskType(fireType) != _taskTypes.Zoom)
                || ((inSuccession == 1 || fireType == 1) && trigger.IsFirstFire))
            {
                return;
            }

            if (trigger.RoundsFired >= inSuccession)
            {
                trigger.IsFirstFire = false;
                _inventory.Status = Igi2WeaponStatus.Locked;
                return;
            }
        }

        if (!trigger.IsFireStatus)
        {
            if (weaponType.WeaponType == Igi2WeaponTypeKind.Grenade && owner.IsPlayer)
            {
                _fireTypes[0].FireStatusTick = owner.Ticks - owner.GrenadeHeldTime;
            }
            else
            {
                trigger.FireStatusTick = owner.Ticks;
            }

            trigger.IsFirstFire = true;
            trigger.IsFireStatus = true;
        }

        byte heldTime = (byte)(owner.Ticks - trigger.FireStatusTick);
        SingleFire(Message(item.Number, fireType, heldTime, owner.GetFireFrame(addInaccuracy: true)));
    }

    /// <summary>
    /// The weapon's end of the tick, after its holder's: <c>Weapon_RunDelayedHandler</c> (<c>igi2 0x51C480</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The delay counts down. A pulled grenade's fuse burns: at nothing it goes off in hand, firing the primary and
    /// letting its trigger go, and every thirtieth tick before that it ticks.
    /// </para>
    /// <para>
    /// A weapon firing on by itself fires again, and so does one finishing a burst; the shot is the primary's, with the
    /// held time of the fire type last fired and the holder's inaccuracy. Once the fire type last fired has nothing
    /// loaded or has fired its rounds in succession it stops: the trigger is let go and its count starts again.
    /// </para>
    /// <para>The sound sequence plays on, and a delay run out leaves the weapon ready.</para>
    /// </remarks>
    public void RunDelayed()
    {
        if (_inventory.Current is null)
        {
            return;
        }

        if (_inventory.Delay != 0)
        {
            _inventory.Delay--;
        }

        if (_inventory.IsPinPulled && _inventory.Timer > 0)
        {
            _inventory.Timer--;
            if (_inventory.Timer == 0)
            {
                Fire(0);
                _fireTypes[0].IsReleaseFire = true;
                _inventory.Status = Igi2WeaponStatus.ExplodedInHand;
            }
            else if (_inventory.Timer % FuseTickInterval == 0)
            {
                Effects.PlaySound(FuseSoundSlot, FuseTickSound, looped: false);
            }
        }

        bool fire;
        if (IsAutoFire)
        {
            AutoTimer++;
            fire = true;
        }
        else
        {
            AutoTimer = 0;
            fire = _completingBurst[FireType];
        }

        if (fire && _inventory.Current is { } item && Owner is { } owner)
        {
            byte heldTime = (byte)(owner.Ticks - _fireTypes[FireType].FireStatusTick);
            SingleFire(Message(item.Number, 0, heldTime, owner.GetFireFrame(addInaccuracy: true)));

            Igi2WeaponItem active = _inventory.Current!;
            int slot = FireType;
            if (active.Loaded(slot) == 0 || _fireTypes[slot].RoundsFired >= active.WeaponType.RoundsInSuccession(active.ActiveFireMode))
            {
                IsAutoFire = false;
                _completingBurst[slot] = false;
                _fireTypes[slot].IsReleaseFire = true;
                _fireTypes[slot].RoundsFired = 0;
            }
        }

        Effects.MaintainSoundSequence();
        if (_inventory.Status == Igi2WeaponStatus.Delay && _inventory.Delay == 0)
        {
            _inventory.Status = Igi2WeaponStatus.Ready;
        }
    }

    /// <summary>Lets go the triggers the tick let go of, at the end of the holder's tick: <c>Weapon_UpdateStatus</c> (<c>igi2 0x51E370</c>).</summary>
    /// <remarks>
    /// A primary trigger let go part way into a burst — more than one round a pull, some but not all fired, and fewer
    /// than the clip holds — is kept down to finish the burst. Any other is let go: the release goes out with how long the
    /// trigger was down and the holder's frame, taken with its inaccuracy, and the trigger and its count start again.
    /// </remarks>
    public void UpdateStatus()
    {
        if (_inventory.Current is null || Owner is not { HasFireFrame: true } owner)
        {
            return;
        }

        for (int slot = 0; slot < Igi2WeaponTypeTask.FireTypeSlots; slot++)
        {
            Igi2FireTypeState trigger = _fireTypes[slot];
            if (!trigger.IsReleaseFire)
            {
                continue;
            }

            Igi2WeaponItem active = _inventory.Current;
            int inSuccession = active.WeaponType.RoundsInSuccession(active.ActiveFireMode);
            Igi2FireTypeRounds rounds = _inventory.FireTypeRounds(active, slot);
            if (slot == 0 && inSuccession > 1 && trigger.RoundsFired > 0 && trigger.RoundsFired < inSuccession && inSuccession < rounds.RoundsPerClip)
            {
                _completingBurst[0] = true;
                trigger.IsReleaseFire = false;
                continue;
            }

            _completingBurst[slot] = false;
            int heldTime = Math.Abs(owner.Ticks - trigger.FireStatusTick);
            Igi2FireFrame frame = owner.GetFireFrame(addInaccuracy: true);
            SingleRelease(new Igi2ReleaseMessage(active.Number, slot, heldTime, frame.Position, Igi2Pose.Angles(frame.Orientation)));
            trigger.IsFireStatus = false;
            trigger.IsReleaseFire = false;
            trigger.RoundsFired = 0;
        }
    }

    /// <summary>Reloads the weapon in hand, <c>Weapon_Reload</c> (<c>igi2 0x51E120</c>) as <c>Weapon_SingleReloadHandler</c> (<c>0x51D440</c>) does it.</summary>
    /// <remarks>
    /// The delay is cleared. Each fire type that uses ammunition starts its count of rounds fired again and, unless it is
    /// loaded by hand, takes what its clip lacks — a single round when it is reloaded a round at a time — from what is
    /// carried, no more than there is, or all of it with ammunition that never runs out. An empty weapon is then ready.
    /// </remarks>
    public void Reload()
    {
        if (_inventory.Current is not { } item)
        {
            return;
        }

        _inventory.Delay = 0;
        Igi2WeaponTypeTask weaponType = item.WeaponType;
        for (int slot = 0; slot < Igi2WeaponTypeTask.FireTypeSlots; slot++)
        {
            Igi2FireTypeAmmo ammo = weaponType.AmmoInfo(slot, _tables);
            if (ammo.AmmoType is not { } ammoType)
            {
                continue;
            }

            Igi2AmmoItem? carried = _inventory.FindAmmo(ammoType.AmmoId);
            int lacking = ammo.RoundsPerClip - item.Loaded(slot);
            _fireTypes[slot].RoundsFired = 0;
            if (weaponType.IsForceReload(slot) || lacking == 0)
            {
                continue;
            }

            if (weaponType.IsReloadRoundsIndividually(slot))
            {
                lacking = 1;
            }

            if (carried is null)
            {
                continue;
            }

            if (!IsUnlimitedAmmo)
            {
                if (lacking > carried.Count)
                {
                    lacking = carried.Count;
                }

                carried.Spend(lacking);
            }

            if (lacking != 0)
            {
                item.SetLoaded(slot, item.Loaded(slot) + lacking);
            }
        }

        if (_inventory.Status == Igi2WeaponStatus.Empty)
        {
            _inventory.Status = Igi2WeaponStatus.Ready;
        }
    }

    /// <summary>Lets go of both triggers and clears the status, as death and water do.</summary>
    public void ReleaseTriggers()
    {
        _fireTypes[0].IsReleaseFire = true;
        _fireTypes[1].IsReleaseFire = true;
        _inventory.Status = Igi2WeaponStatus.Ready;
    }

    /// <summary>Clears the kick once the holder has taken it.</summary>
    public void TakeKickback() => IsKickback = false;

    /// <summary>The shot itself, <c>Weapon_SingleFireHandler</c> (<c>igi2 0x51CD40</c>).</summary>
    /// <remarks>
    /// A fire type that must be loaded by hand fires from what is carried and needs as many rounds carried as it takes; any
    /// other needs them loaded. A delay not run out holds the shot. The position comes back from centimetres and the
    /// orientation from its angles, and the fire type's handler fires; whatever it fired or delayed is spent and noted,
    /// the weapon has fired, and the kick is kept for the holder.
    /// </remarks>
    private void SingleFire(in Igi2FireMessage message)
    {
        int fireType = message.FireType;
        Igi2FireTypeState trigger = _fireTypes[fireType];
        trigger.IsFirstFire = false;
        FireType = fireType;
        if (_inventory.Current is { } current && fireType == 0 && current.WeaponType.RoundsInSuccession(current.ActiveFireMode) > 1)
        {
            IsAutoFire = true;
        }

        if (_inventory.WeaponByNumber(message.WeaponId) is not { } item)
        {
            return;
        }

        Igi2WeaponTypeTask weaponType = item.WeaponType;
        if (weaponType.FireTaskType(fireType) == Igi2WeaponConstants.NoTaskType
            || weaponType.FireType(fireType) is not { } parameters || _handlers(parameters) is not { } handler)
        {
            return;
        }

        trigger.LastStatusTick = Owner?.Ticks ?? 0;
        Igi2FireTypeAmmo ammo = weaponType.AmmoInfo(fireType, _tables);
        bool forceReload = weaponType.IsForceReload(fireType);
        Igi2AmmoItem? carried = ammo.AmmoType is { } ammoType ? _inventory.FindAmmo(ammoType.AmmoId) : null;
        bool enough = forceReload
            ? ammo.AmmoType is not null ? ammo.RoundsToFire <= (carried?.Count ?? 0) : ammo.RoundsToFire == 0
            : ammo.RoundsToFire <= item.Loaded(fireType);
        if (!enough)
        {
            _inventory.Status = Igi2WeaponStatus.Empty;
            return;
        }

        if (_inventory.Delay != 0 && weaponType.FireTaskType(fireType) != _taskTypes.Zoom)
        {
            _inventory.Status = Igi2WeaponStatus.Delay;
            return;
        }

        Igi2FireRequest request = Request(item, fireType, message.HeldTime, Position(message.Position), message.Direction, parameters);
        Igi2FireResult result = handler.Fire(request);
        int fired = result.BulletsFired;
        _inventory.TotalBulletsFired += fired;
        if (fired != 0 || result.Delay != 0)
        {
            _inventory.Delay = result.Delay;
            if (forceReload)
            {
                if (carried is not null)
                {
                    carried.Spend(fired);
                    CountShots(fireType, fired);
                }
            }
            else
            {
                int left = item.Loaded(fireType) - fired;
                item.SetLoaded(fireType, left <= 0 ? 0 : left);
                trigger.RoundsFired += fired;
                CountShots(fireType, fired);
            }
        }

        _inventory.Status = Igi2WeaponStatus.Fire;
        SetKickback(result);
    }

    /// <summary>A trigger let go, <c>Weapon_SingleReleaseHandler</c> (<c>igi2 0x51D1D0</c>).</summary>
    /// <remarks>
    /// Letting go the primary stops the weapon firing on by itself. A fire type with a release handler runs it, and the
    /// weapon is ready after. A release that fires spends it as a shot does, except that a weapon loaded by hand loses
    /// twice the rounds it fired, floored at none (the original's arithmetic).
    /// </remarks>
    private void SingleRelease(in Igi2ReleaseMessage message)
    {
        int fireType = message.FireType;
        if (fireType == 0)
        {
            IsAutoFire = false;
        }

        FireType = fireType;
        if (_inventory.WeaponByNumber(message.WeaponId) is not { } item)
        {
            return;
        }

        Igi2WeaponTypeTask weaponType = item.WeaponType;
        if (weaponType.FireTaskType(fireType) == Igi2WeaponConstants.NoTaskType
            || weaponType.FireType(fireType) is not { } parameters || _handlers(parameters) is not { HasRelease: true } handler
            || _inventory.Current is null)
        {
            return;
        }

        Igi2FireResult result = handler.Release(Request(item, fireType, message.HeldTime, message.Position, message.Direction, parameters));
        int fired = result.BulletsFired;
        if (fired != 0)
        {
            _inventory.Delay = result.Delay;
            if (weaponType.IsForceReload(fireType))
            {
                Igi2FireTypeRounds rounds = _inventory.FireTypeRounds(_inventory.Current, fireType);
                if (rounds.AmmoType is { } ammoType)
                {
                    _inventory.FindAmmo(ammoType.AmmoId)?.Spend(fired);
                }
            }
            else
            {
                int left = item.Loaded(fireType) - fired - fired;
                item.SetLoaded(fireType, left <= 0 ? 0 : left);
                _fireTypes[fireType].RoundsFired += fired;
            }
        }

        _inventory.Status = Igi2WeaponStatus.Ready;
    }

    /// <summary>Keeps a shot's kick for the holder, <c>Weapon_SetKickBack</c> (<c>igi2 0x51DD70</c>).</summary>
    private void SetKickback(in Igi2FireResult result)
    {
        AlphaKickback = result.AlphaKickback;
        KickbackDamping = result.KickbackDamping;
        GammaKickback = result.GammaKickback;
        CrossHairDamping = result.CrossHairDamping;
        IsKickback = result.AlphaKickback != 0f || result.GammaKickback != 0f;
    }

    /// <summary>
    /// Counts shots towards the holder's accuracy, <c>Weapon_UpdateOwnersMissionScoreShots</c> (<c>igi2 0x51F830</c>): when
    /// the weapon in hand's ammunition for the fire type is used for the accuracy score and it is a gun, pistol or shotgun.
    /// </summary>
    private void CountShots(int fireType, int shots)
    {
        if (Owner is { } owner && _inventory.Current is { } active
            && _inventory.FireTypeRounds(active, fireType).AmmoType is { UsedForAccuracyScore: true }
            && (uint)active.WeaponType.WeaponType <= Igi2WeaponTypeKind.Shotgun)
        {
            owner.CountShots(shots);
        }
    }

    private Igi2FireRequest Request(Igi2WeaponItem item, int fireType, int heldTime, Vector3d position, (float Alpha, float Beta, float Gamma) direction, object parameters) =>
        new(
            this,
            Owner,
            position,
            Igi2Pose.Rotation(direction.Alpha, direction.Beta, direction.Gamma),
            Owner?.AccumulatedSpeed ?? default,
            heldTime,
            parameters,
            item.WeaponType,
            _fireTypes[fireType].RoundsFired);

    /// <summary>
    /// A shot as it is sent: the position in metres at the FPU's precision, then whole centimetres, truncated
    /// (<c>World_SetInt32CentimeterFromReal64Meter</c>, <c>igi2 0x552530</c>), and the orientation's angles
    /// (<c>Real32x9_GetAngles</c>, <c>0x562D60</c>).
    /// </summary>
    private static Igi2FireMessage Message(int weaponId, int fireType, byte heldTime, in Igi2FireFrame frame)
    {
        Vector3d p = frame.Position;
        return new Igi2FireMessage(
            (byte)weaponId,
            (byte)fireType,
            heldTime,
            (Centimetres(p.X), Centimetres(p.Y), Centimetres(p.Z)),
            Igi2Pose.Angles(frame.Orientation));

        static int Centimetres(double units) => Fpu.ToInt32(((Fpu)units * 0.000244140625) * 100.0);
    }

    /// <summary>
    /// A sent position back in world units: centimetres to metres (<c>World_SetReal64MeterFromInt32Centimeter</c>,
    /// <c>igi2 0x5525A0</c>) and metres to units, each product at the FPU's precision.
    /// </summary>
    private static Vector3d Position((int X, int Y, int Z) centimetres)
    {
        return new Vector3d(Units(centimetres.X), Units(centimetres.Y), Units(centimetres.Z));

        static double Units(int cm) => (((Fpu)(double)cm * 0.01) * 4096.0).Value;
    }
}
