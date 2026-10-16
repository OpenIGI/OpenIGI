using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Weapons;

namespace OpenIGI.Game.Igi2.Player;

/// <summary>The player's weapon as its body and its input handling see it: the weapon in hand, out of the inventory.</summary>
/// <remarks>
/// <para>
/// Answers what the locomotion and the signal packing ask of the held weapon from the item the inventory has in hand
/// (<c>Weapon+0x2C</c>): its category, its precision per posture (<c>WeaponType+0x348</c>…<c>+0x368</c>), whether its
/// secondary fire type is a zoom (<c>+0x528</c>), whether it is a gun firing missiles, how much the carried weapons slow
/// the player, and the clips it gives a body.
/// </para>
/// <para>
/// A weapon type keeps one clip table per rig it was built for (<c>WeaponType+0x790</c>/<c>+0x794</c>,
/// <c>WeaponType_GetAnimHeaderFromWeaponTypeAnim</c>, <c>igi2 0x4383B0</c>); this builds a table the first time a rig asks
/// for one and keeps it.
/// </para>
/// <para>
/// Firing is the weapon's own (<see cref="Igi2WeaponFiring"/>), its fire types' handlers chosen by
/// <see cref="Igi2FireHandlers"/>. Not yet: zooming and the overlay, and the sound slots; until then those requests do
/// nothing, as they do with nothing held.
/// </para>
/// </remarks>
public sealed class Igi2PlayerWeapons : IIgi2PlayerWeaponControl
{
    private readonly Igi2Inventory _inventory;
    private readonly Igi2FireTaskTypes _taskTypes;
    private readonly Igi2AnimationLibrary? _clips;
    private readonly Dictionary<(Igi2WeaponTypeTask, Igi2BoneHierarchy), Igi2AnimationClip?[]> _bodyClips = [];
    private readonly Igi2WeaponFiring _firing;
    private readonly Igi2KnifeClipCycle _knifeClips = new();

    /// <summary>Initializes a new instance of the <see cref="Igi2PlayerWeapons"/> class.</summary>
    /// <param name="inventory">What the player carries.</param>
    /// <param name="taskTypes">The task-type ids a held weapon is tested against.</param>
    /// <param name="clips">The level's clips, or <see langword="null"/> for none.</param>
    public Igi2PlayerWeapons(Igi2Inventory inventory, Igi2FireTaskTypes taskTypes, Igi2AnimationLibrary? clips)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        _inventory = inventory;
        _taskTypes = taskTypes;
        _clips = clips;
        _firing = new Igi2WeaponFiring(inventory, inventory.Tables, taskTypes, Igi2FireHandlers.Create(inventory.Tables));
    }

    /// <summary>Gets what the player carries.</summary>
    public Igi2Inventory Inventory => _inventory;

    /// <inheritdoc/>
    public Igi2WeaponFiring Firing => _firing;

    /// <summary>Gets or sets what firing shows and sounds.</summary>
    public IIgi2WeaponEffects Effects
    {
        get => _firing.Effects;
        set => _firing.Effects = value;
    }

    /// <summary>Gets or sets what the weapon's bullets and stabs show and sound where they strike.</summary>
    public IIgi2GunShotEffects ImpactEffects
    {
        get => _firing.ImpactEffects;
        set => _firing.ImpactEffects = value;
    }

    /// <summary>Gets or sets the level the weapon's bullets and stabs strike.</summary>
    public IIgi2GunShotWorld? World
    {
        get => _firing.World;
        set => _firing.World = value;
    }

    /// <summary>
    /// Traces the bullets the weapon fired last tick (<c>GunShot_RunHandler</c>, <c>igi2 0x4C76E0</c>).
    /// </summary>
    /// <remarks>
    /// The shot is the weapon's child and runs in the tick's run pass, where the player's and the weapon's own run
    /// handlers only queue them for the delayed pass (<c>HumanPlayer_RunHandler</c>, <c>0x4B8A30</c>;
    /// <c>Weapon_RunHandler</c>, <c>0x51C460</c>). So a bullet is fired in one tick's delayed pass and traced at the start
    /// of the next, before the player moves again.
    /// </remarks>
    public void RunGunShot() => _firing.RunGunShot();

    /// <inheritdoc/>
    public int? ActiveCategory => Held?.WeaponCategory;

    /// <inheritdoc/>
    public bool IsHolding => Held is not null;

    /// <inheritdoc/>
    public bool ActiveIsZoom => Held is { } held && held.FireTaskType(1) == _taskTypes.Zoom;

    /// <inheritdoc/>
    /// <remarks>
    /// <c>HumanPlayer_Update</c> (<c>igi2 0x4A20C0</c>): the primary fire type is declared a <c>Gun</c> and the gun's
    /// <c>Projectile TaskType</c> is a <c>Missile</c>.
    /// </remarks>
    public bool ActiveIsMissileLauncher =>
        Held is { } held && held.FireTaskType(0) == _taskTypes.Gun
        && held.FireType(0) is Igi2GunTask gun && gun.ProjectileTaskType == _taskTypes.Missile;

    /// <inheritdoc/>
    public float SummedMovementSlowDown => _inventory.SummedMovementSlowDown;

    /// <inheritdoc/>
    public Igi2WeaponPrecision? ActivePrecision => Held is { } held
        ? new Igi2WeaponPrecision(
            held.StandingPrecisionDegrees,
            held.RunningPrecisionDegrees,
            held.WalkingPrecisionDegrees,
            held.CrouchingPrecisionDegrees,
            held.CrouchWalkingPrecisionDegrees,
            held.LyingFlatPrecisionDegrees,
            held.CrawlingPrecisionDegrees,
            held.JumpingPrecisionModifier,
            held.TurningPrecisionModifier)
        : null;

    /// <inheritdoc/>
    /// <remarks>
    /// <c>Human_IsZoomFireModeActive</c> (<c>igi2 0x506450</c>): the weapon's zoom is up and the weapon in hand's second
    /// slot is a <c>Zoom</c>.
    /// </remarks>
    public bool IsZoomFireModeActive =>
        _firing.OverlayStatus != Igi2WeaponOverlayStatus.None && Held is { } held && held.FireTaskType(1) == _taskTypes.Zoom;

    private Igi2WeaponTypeTask? Held => _inventory.Current?.WeaponType;

    /// <inheritdoc/>
    public IReadOnlyList<Igi2AnimationClip?>? BodyClips(Igi2BoneHierarchy? rig)
    {
        if (Held is not { } held || rig is null || _clips is null)
        {
            return null;
        }

        if (!_bodyClips.TryGetValue((held, rig), out Igi2AnimationClip?[]? table))
        {
            table = Igi2WeaponTypeAnims.BuildTable(_clips, rig, held.WeaponAnimationName, (Igi2WeaponAnimFamily)held.WeaponType);
            _bodyClips[(held, rig)] = table;
        }

        return table;
    }

    /// <inheritdoc/>
    public Igi2WeaponAnim? GetAnim(Igi2WeaponAnimRequest request, Igi2BoneHierarchy? rig, int fireType) =>
        Held is { } held
            ? Igi2WeaponTypeAnim.Get(held, request, rig, _clips, BodyClips(rig), fireType, _knifeClips, RoundsCarried(fireType))
            : null;

    /// <summary>Gets or sets where the grenades the weapon throws go.</summary>
    public IIgi2GrenadeWorld Grenades
    {
        get => _firing.Grenades;
        set => _firing.Grenades = value;
    }

    /// <summary>The rounds carried for a fire type of the weapon in hand, <c>Weapon_GetFireTypeAmmoInfoFromWeaponTypeItem</c>.</summary>
    private int RoundsCarried(int fireType) =>
        (uint)fireType < Igi2WeaponTypeTask.FireTypeSlots && _inventory.Current is { } item ? _inventory.FireTypeRounds(item, fireType).Available : 0;

    /// <inheritdoc/>
    public void ClearAllWeapons() => _inventory.ClearAll();

    /// <inheritdoc/>
    public void Fire(int fireType) => _firing.Fire(fireType);

    /// <inheritdoc/>
    public void ClearDelay() => _inventory.Delay = 0;

    /// <inheritdoc/>
    public void Bind(IIgi2WeaponOwner owner) => _firing.Owner = owner;

    /// <inheritdoc/>
    public void UpdateStatus() => _firing.UpdateStatus();

    /// <inheritdoc/>
    public void RunDelayed() => _firing.RunDelayed();

    /// <inheritdoc/>
    public void Zoom(bool zoomIn) => _firing.Zoom(zoomIn);

    /// <inheritdoc/>
    public bool Activate(Igi2WeaponItem item) => _firing.Activate(item);

    /// <inheritdoc/>
    /// <remarks>
    /// The single-player half of <c>HumanPlayer_LeaveZoomAlternateFireMode</c> (<c>igi2 0x4BEC80</c>): a zoom that is up
    /// on a weapon whose second slot is a <c>Zoom</c> comes down.
    /// </remarks>
    public void LeaveZoomAlternateFireMode()
    {
        if (IsZoomFireModeActive)
        {
            _firing.SetOverlayStatus(Igi2WeaponOverlayStatus.None);
        }
    }

    /// <inheritdoc/>
    public void StopSoundSlot()
    {
    }

    /// <inheritdoc/>
    public void ClearOverlay() => _firing.SetOverlayStatus(Igi2WeaponOverlayStatus.None);

    /// <inheritdoc/>
    public void ReleaseTriggers() => _firing.ReleaseTriggers();
}
