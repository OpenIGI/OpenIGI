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
/// Not yet: firing, zooming and the overlay, the sound slots and the trigger state, which arrive with the weapon's own
/// update. Until then those requests do nothing, as they do with nothing held.
/// </para>
/// </remarks>
public sealed class Igi2PlayerWeapons : IIgi2PlayerWeaponControl
{
    private readonly Igi2Inventory _inventory;
    private readonly Igi2FireTaskTypes _taskTypes;
    private readonly Igi2AnimationLibrary? _clips;
    private readonly Dictionary<(Igi2WeaponTypeTask, Igi2BoneHierarchy), Igi2AnimationClip?[]> _bodyClips = [];

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
    }

    /// <summary>Gets what the player carries.</summary>
    public Igi2Inventory Inventory => _inventory;

    /// <inheritdoc/>
    public int? ActiveCategory => Held?.WeaponCategory;

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
    public bool IsZoomFireModeActive => false;

    /// <inheritdoc/>
    public bool IsZoomedModeAllowed => false;

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
    public void ClearAllWeapons() => _inventory.ClearAll();

    /// <inheritdoc/>
    public void Fire()
    {
    }

    /// <inheritdoc/>
    public void ClearTrigger()
    {
    }

    /// <inheritdoc/>
    public void Zoom(bool zoomIn)
    {
    }

    /// <inheritdoc/>
    public void LeaveZoomAlternateFireMode()
    {
    }

    /// <inheritdoc/>
    public void StepActionMenu(int direction)
    {
    }

    /// <inheritdoc/>
    public void StopSoundSlot()
    {
    }

    /// <inheritdoc/>
    public void ClearOverlay()
    {
    }

    /// <inheritdoc/>
    public void ReleaseTriggers()
    {
    }
}
