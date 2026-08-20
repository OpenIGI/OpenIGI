using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Weapons;

/// <summary>
/// One weapon's configuration, as <c>weapons/weaponconfig.qsc</c> declares it.
/// </summary>
/// <remarks>
/// <para>
/// The config is not a bespoke format: it is an ordinary task tree, a <c>WeaponConfigContainer</c> of
/// <c>WeaponConfig</c> children, declared with <c>Task_DeclareParameters</c> and built with
/// <c>Task_New</c>. The M3 task system reads it unmodified.
/// </para>
/// <para>
/// <b>Parameter order is wire order</b>, transcribed from the script's own declaration. See
/// the original.
/// </para>
/// </remarks>
public partial class WeaponConfigTask : GameTask
{
    /// <summary>Gets or sets the numeric id the rest of the game addresses this weapon by.</summary>
    [ScriptParam(ScriptParamType.Int32, "Weapon ID")]
    public int WeaponId { get; set; }

    /// <summary>Gets or sets the script symbol this weapon publishes its id under.</summary>
    /// <remarks>
    /// The <c>WEAPON_ID_*</c> name, carried as a plain string. Pairing it with
    /// <see cref="WeaponId"/> is what gives those pattern-registered symbols their values.
    /// </remarks>
    [ScriptParam(ScriptParamType.String256, "Script ID")]
    public string ScriptId { get; set; } = string.Empty;

    /// <summary>Gets or sets the weapon's display name — <c>GameTask.Name</c> is the task's own.</summary>
    [ScriptParam(ScriptParamType.String256, "Name")]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Gets or sets the manufacturer, as a language key.</summary>
    [ScriptParam(ScriptParamType.String256, "Manufacturer")]
    public string Manufacturer { get; set; } = string.Empty;

    /// <summary>Gets or sets the description, as a language key.</summary>
    [ScriptParam(ScriptParamType.String256, "Description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>Gets or sets the weapon category.</summary>
    [ScriptParam(ScriptParamType.EnumInt32, "Weapon Type")]
    public int WeaponType { get; set; }

    /// <summary>Gets or sets which sight the HUD draws.</summary>
    [ScriptParam(ScriptParamType.EnumInt32, "Sight Display Type")]
    public int SightDisplayType { get; set; }

    /// <summary>Gets or sets how the HUD counts the ammunition.</summary>
    [ScriptParam(ScriptParamType.EnumInt32, "Ammo Display Type")]
    public int AmmoDisplayType { get; set; }

    /// <summary>Gets or sets the mass in grams.</summary>
    [ScriptParam(ScriptParamType.Int32, "Mass of Weapon (grams)")]
    public int MassInGrams { get; set; }

    /// <summary>Gets or sets the ammunition this weapon takes.</summary>
    /// <remarks>
    /// Matches a directory under <c>ammo/</c>. Real calibres use their own numbers (<c>919</c>,
    /// <c>556</c>, <c>762</c>, <c>12</c>, <c>357</c>, <c>127</c>); the thrown and special types are
    /// numbered from 10000 upwards, and the knife takes <c>-1</c> because it consumes nothing.
    /// </remarks>
    [ScriptParam(ScriptParamType.Int32, "Calibre ID")]
    public int CalibreId { get; set; }

    /// <summary>Gets or sets the damage multiplier.</summary>
    [ScriptParam(ScriptParamType.Real32, "Damage Factor")]
    public float DamageFactor { get; set; }

    /// <summary>Gets or sets how much cover the round defeats.</summary>
    [ScriptParam(ScriptParamType.Real32, "Penetration Power")]
    public float PenetrationPower { get; set; }

    /// <summary>Gets or sets the reload time in seconds.</summary>
    [ScriptParam(ScriptParamType.Real32, "Reload Time")]
    public float ReloadTimeSeconds { get; set; }

    /// <summary>Gets or sets the muzzle velocity in metres per second.</summary>
    [ScriptParam(ScriptParamType.Real32, "Muzzle Velocity")]
    public float MuzzleVelocity { get; set; }

    /// <summary>Gets or sets how many projectiles one round produces — above one for shot.</summary>
    [ScriptParam(ScriptParamType.Int32, "Bullets/Round")]
    public int BulletsPerRound { get; set; }

    /// <summary>Gets or sets the cyclic rate in rounds per minute.</summary>
    [ScriptParam(ScriptParamType.Int32, "Rounds/Minute")]
    public int RoundsPerMinute { get; set; }

    /// <summary>Gets or sets the magazine capacity.</summary>
    [ScriptParam(ScriptParamType.Int32, "Rounds/Clip")]
    public int RoundsPerClip { get; set; }

    /// <summary>Gets or sets the longest burst one trigger pull fires.</summary>
    /// <remarks>Equal to <see cref="RoundsPerClip"/> on a fully automatic weapon, and 1 on a semi.</remarks>
    [ScriptParam(ScriptParamType.Int32, "Maximum Rounds/Burst")]
    public int MaximumRoundsPerBurst { get; set; }

    /// <summary>Gets or sets the smallest random spread.</summary>
    [ScriptParam(ScriptParamType.Real32, "Minimum random spread")]
    public float MinimumSpread { get; set; }

    /// <summary>Gets or sets the largest random spread.</summary>
    [ScriptParam(ScriptParamType.Real32, "Maximum random spread")]
    public float MaximumSpread { get; set; }

    /// <summary>Gets or sets the fixed recoil about the X axis, in degrees.</summary>
    [ScriptParam(ScriptParamType.Real32, "Fixed view change around X axis (degrees)")]
    public float FixedRecoilX { get; set; }

    /// <summary>Gets or sets the fixed recoil about the Z axis, in degrees.</summary>
    [ScriptParam(ScriptParamType.Real32, "Fixed view change around Z axis (degrees)")]
    public float FixedRecoilZ { get; set; }

    /// <summary>Gets or sets the random recoil about the X axis, in degrees.</summary>
    [ScriptParam(ScriptParamType.Real32, "Random view change around X axis (degrees)")]
    public float RandomRecoilX { get; set; }

    /// <summary>Gets or sets the random recoil about the Z axis, in degrees.</summary>
    [ScriptParam(ScriptParamType.Real32, "Random view change around Z axis (degrees)")]
    public float RandomRecoilZ { get; set; }

    /// <summary>Gets or sets the weapon's kind, as a language key.</summary>
    [ScriptParam(ScriptParamType.String256, "Type of weapon")]
    public string TypeOfWeapon { get; set; } = string.Empty;

    /// <summary>Gets or sets the effective range in metres.</summary>
    [ScriptParam(ScriptParamType.Real32, "Effective Range of Weapon")]
    public float EffectiveRange { get; set; }

    /// <summary>Gets or sets who uses it, as a language key.</summary>
    [ScriptParam(ScriptParamType.String256, "Weapon Users")]
    public string WeaponUsers { get; set; } = string.Empty;

    /// <summary>Gets or sets the overall length in millimetres.</summary>
    [ScriptParam(ScriptParamType.Int32, "Weapon Length")]
    public int WeaponLength { get; set; }

    /// <summary>Gets or sets the barrel length in millimetres.</summary>
    [ScriptParam(ScriptParamType.Int32, "Barrel Length")]
    public int BarrelLength { get; set; }

    /// <summary>Gets or sets the model the weapon is drawn with.</summary>
    [ScriptParam(ScriptParamType.String16, "Gun Model")]
    public string GunModel { get; set; } = string.Empty;

    /// <summary>Gets or sets the model an ejected casing uses.</summary>
    [ScriptParam(ScriptParamType.String16, "Casing Model")]
    public string CasingModel { get; set; } = string.Empty;

    /// <summary>Gets or sets the full-body standing animation.</summary>
    [ScriptParam(ScriptParamType.Int32, "StandAnim")]
    public int StandAnimation { get; set; }

    /// <summary>Gets or sets the full-body moving animation.</summary>
    [ScriptParam(ScriptParamType.Int32, "MoveAnim")]
    public int MoveAnimation { get; set; }

    /// <summary>Gets or sets the first firing animation.</summary>
    [ScriptParam(ScriptParamType.Int32, "FireAnim1")]
    public int FireAnimation1 { get; set; }

    /// <summary>Gets or sets the second firing animation, or <c>-1</c>.</summary>
    [ScriptParam(ScriptParamType.Int32, "FireAnim2")]
    public int FireAnimation2 { get; set; }

    /// <summary>Gets or sets the third firing animation, or <c>-1</c>.</summary>
    [ScriptParam(ScriptParamType.Int32, "FireAnim3")]
    public int FireAnimation3 { get; set; }

    /// <summary>Gets or sets the reload animation.</summary>
    [ScriptParam(ScriptParamType.Int32, "ReloadAnim")]
    public int ReloadAnimation { get; set; }

    /// <summary>Gets or sets the upper-body standing animation.</summary>
    [ScriptParam(ScriptParamType.Int32, "UpperBodyStandAnim")]
    public int UpperBodyStandAnimation { get; set; }

    /// <summary>Gets or sets the upper-body walking animation.</summary>
    [ScriptParam(ScriptParamType.Int32, "UpperBodyWalkAnim")]
    public int UpperBodyWalkAnimation { get; set; }

    /// <summary>Gets or sets the upper-body crouching animation.</summary>
    [ScriptParam(ScriptParamType.Int32, "UpperBodyCrouchAnim")]
    public int UpperBodyCrouchAnimation { get; set; }

    /// <summary>Gets or sets the upper-body crouch-running animation.</summary>
    [ScriptParam(ScriptParamType.Int32, "UpperBodyCrouchRunAnim")]
    public int UpperBodyCrouchRunAnimation { get; set; }

    /// <summary>Gets or sets the upper-body running animation.</summary>
    [ScriptParam(ScriptParamType.Int32, "UpperBodyRunAnim")]
    public int UpperBodyRunAnimation { get; set; }

    /// <summary>Gets or sets the upper-body firing animation.</summary>
    [ScriptParam(ScriptParamType.Int32, "UpperBodyFireAnim")]
    public int UpperBodyFireAnimation { get; set; }

    /// <summary>Gets or sets the upper-body reloading animation.</summary>
    [ScriptParam(ScriptParamType.Int32, "UpperBodyReloadAnim")]
    public int UpperBodyReloadAnimation { get; set; }

    /// <summary>Gets or sets the firing sound, or the loop for an automatic weapon.</summary>
    [ScriptParam(ScriptParamType.String16, "Fire Sound Loop/Single Sample")]
    public string FireSound { get; set; } = string.Empty;

    /// <summary>Gets or sets the sound that ends a firing loop.</summary>
    [ScriptParam(ScriptParamType.String16, "Fire Sound Loop End Sample")]
    public string FireLoopEndSound { get; set; } = string.Empty;

    /// <summary>Gets or sets how far the shot is heard by the AI.</summary>
    [ScriptParam(ScriptParamType.EnumReal32, "AI Detection Event Range")]
    public float AiDetectionRange { get; set; }

    /// <summary>Gets or sets the task type spawned for each projectile.</summary>
    [ScriptParam(ScriptParamType.EnumInt32, "Projectile Task Type")]
    public int ProjectileTaskType { get; set; }

    /// <summary>Gets or sets the task type the weapon itself is.</summary>
    [ScriptParam(ScriptParamType.EnumInt32, "Weapon Task Type")]
    public int WeaponTaskType { get; set; }

    /// <summary>Gets or sets whether the weapon can still be selected with no ammunition.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Is weapon selectable when empty")]
    public bool SelectableWhenEmpty { get; set; }
}

/// <summary>The container the weapon configurations hang off.</summary>
public sealed class WeaponConfigContainerTask : ContainerTask;
