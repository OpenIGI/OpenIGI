using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>A fire type that shoots rounds, <c>Gun</c>.</summary>
/// <remarks>
/// The parameters are the script's own declaration, in wire order (<c>Task_DeclareParameters</c> in <c>WEAPONS/weapon.qvm</c>). A fire type is a child of the weapon whose fire it carries out.
/// </remarks>
public sealed partial class Igi2GunTask : Igi2Task
{
    /// <summary>The name scripts spawn this type by.</summary>
    public const string TypeName = "Gun";

    /// <summary>Gets or sets the <c>AmmoType ID</c> parameter.</summary>
    [ScriptParam(ScriptParamType.EnumInt32, "AmmoType ID")]
    public int AmmoTypeId { get; set; }

    /// <summary>Gets or sets the <c>Ammo Display Type</c> parameter.</summary>
    [ScriptParam(ScriptParamType.EnumInt32, "Ammo Display Type")]
    public int AmmoDisplayType { get; set; }

    /// <summary>Gets or sets the <c>Bullets Per Round</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Int32, "Bullets Per Round")]
    public int BulletsPerRound { get; set; }

    /// <summary>Gets or sets the <c>Rounds Per Clip</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Int32, "Rounds Per Clip")]
    public int RoundsPerClip { get; set; }

    /// <summary>Gets or sets the <c>Rounds Per Minute</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Int32, "Rounds Per Minute")]
    public int RoundsPerMinute { get; set; }

    /// <summary>Gets or sets the <c>Effective Range (Meters)</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "Effective Range (Meters)")]
    public float EffectiveRangeMeters { get; set; }

    /// <summary>Gets or sets the <c>Reload Time (In Seconds)</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "Reload Time (In Seconds)")]
    public float ReloadTimeSeconds { get; set; }

    /// <summary>Gets or sets the <c>Muzzle Velocity (m/s)</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "Muzzle Velocity (m/s)")]
    public float MuzzleVelocityMps { get; set; }

    /// <summary>Gets or sets the <c>Muzzle flash front sprite</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "Muzzle flash front sprite")]
    public string MuzzleFlashFrontSprite { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>Muzzle flash side sprite</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "Muzzle flash side sprite")]
    public string MuzzleFlashSideSprite { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>Muzzle flash size</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "Muzzle flash size")]
    public float MuzzleFlashSize { get; set; }

    /// <summary>Gets or sets the <c>Min Alpha Kickback (In Degress)</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "Min Alpha Kickback (In Degress)")]
    public float MinAlphaKickbackDegrees { get; set; }

    /// <summary>Gets or sets the <c>Max Alpha Kickback (In Degress)</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "Max Alpha Kickback (In Degress)")]
    public float MaxAlphaKickbackDegrees { get; set; }

    /// <summary>Gets or sets the <c>Min Gamma Kickback (In Degress)</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "Min Gamma Kickback (In Degress)")]
    public float MinGammaKickbackDegrees { get; set; }

    /// <summary>Gets or sets the <c>Max Gamma Kickback (In Degress)</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "Max Gamma Kickback (In Degress)")]
    public float MaxGammaKickbackDegrees { get; set; }

    /// <summary>Gets or sets the <c>Kickback factor floor</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "Kickback factor floor")]
    public float KickbackFactorFloor { get; set; }

    /// <summary>Gets or sets the <c>Kickback factor attack time</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "Kickback factor attack time")]
    public float KickbackFactorAttackTime { get; set; }

    /// <summary>Gets or sets the <c>Kickback factor acceleration</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "Kickback factor acceleration")]
    public float KickbackFactorAcceleration { get; set; }

    /// <summary>Gets or sets the <c>Kickback damping factor</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "Kickback damping factor")]
    public float KickbackDampingFactor { get; set; }

    /// <summary>Gets or sets the <c>Crosshair damping factor</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "Crosshair damping factor")]
    public float CrosshairDampingFactor { get; set; }

    /// <summary>Gets or sets the <c>Projectile TaskType</c> parameter.</summary>
    [ScriptParam(ScriptParamType.EnumInt32, "Projectile TaskType")]
    public int ProjectileTaskType { get; set; }

    /// <summary>Gets or sets the <c>Bullet casing model</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "Bullet casing model")]
    public string BulletCasingModel { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>1st Person Fire Anim</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "1st Person Fire Anim")]
    public string FirstPersonFireAnim { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>1st Person Fire Hold Anim</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "1st Person Fire Hold Anim")]
    public string FirstPersonFireHoldAnim { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>1st Person Fire Release Anim</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "1st Person Fire Release Anim")]
    public string FirstPersonFireReleaseAnim { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>1st Person Reload Start Anim</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "1st Person Reload Start Anim")]
    public string FirstPersonReloadStartAnim { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>1st Person Reload Hold Anim</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "1st Person Reload Hold Anim")]
    public string FirstPersonReloadHoldAnim { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>1st Person Reload End Anim</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "1st Person Reload End Anim")]
    public string FirstPersonReloadEndAnim { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>3rd Person Fire Anim</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "3rd Person Fire Anim")]
    public string ThirdPersonFireAnim { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>3rd Person Fire Hold Anim</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "3rd Person Fire Hold Anim")]
    public string ThirdPersonFireHoldAnim { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>3rd Person Fire Release Anim</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "3rd Person Fire Release Anim")]
    public string ThirdPersonFireReleaseAnim { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>3rd Person Reload Start Anim</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "3rd Person Reload Start Anim")]
    public string ThirdPersonReloadStartAnim { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>3rd Person Reload Hold Anim</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "3rd Person Reload Hold Anim")]
    public string ThirdPersonReloadHoldAnim { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>3rd Person Reload End Anim</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "3rd Person Reload End Anim")]
    public string ThirdPersonReloadEndAnim { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>Fire Sound 1</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "Fire Sound 1")]
    public string FireSound1 { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>Fire Sound 11</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "Fire Sound 11")]
    public string FireSound11 { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>Fire Sound 12</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "Fire Sound 12")]
    public string FireSound12 { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>Play Fire Sound Looped</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Play Fire Sound Looped")]
    public bool PlayFireSoundLooped { get; set; }

    /// <summary>Gets or sets the <c>Play Fire Sound Looped1</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Play Fire Sound Looped1")]
    public bool PlayFireSoundLooped1 { get; set; }

    /// <summary>Gets or sets the <c>Play Fire Sound Looped2</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Play Fire Sound Looped2")]
    public bool PlayFireSoundLooped2 { get; set; }

    /// <summary>Gets or sets the <c>Fire Sound 2</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "Fire Sound 2")]
    public string FireSound2 { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>AI Detection Range</c> parameter.</summary>
    [ScriptParam(ScriptParamType.EnumReal32, "AI Detection Range")]
    public float AIdetectionRange { get; set; }

    /// <summary>Gets or sets the <c>Count bullets per round as rounds</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Count bullets per round as rounds")]
    public bool CountBulletsPerRoundAsRounds { get; set; }

    /// <summary>Gets or sets the <c>Bullets per round Spread (In Degrees)</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "Bullets per round Spread (In Degrees)")]
    public float BulletsPerRoundSpreadDegrees { get; set; }
}

/// <summary>A fire type that narrows the view, <c>Zoom</c>.</summary>
/// <remarks>
/// The parameters are the script's own declaration, in wire order (<c>Task_DeclareParameters</c> in <c>WEAPONS/weapon.qvm</c>). A fire type is a child of the weapon whose fire it carries out.
/// </remarks>
public sealed partial class Igi2ZoomTask : Igi2Task
{
    /// <summary>
    /// Gets the scale of the narrowest view the zoom reaches, <c>Zoom+0x28</c>: the tangent of half its
    /// <c>Minimum FOV</c> (<c>Zoom_UpdateInternalDataHandler</c>, <c>igi2 0x525A40</c>).
    /// </summary>
    public float MinimumFieldOfViewScale => HalfTangent(MinimumFOV);

    /// <summary>Gets the scale of the widest view the zoom reaches, the one it starts at, <c>Zoom+0x2C</c>: the tangent of half its <c>Maximum FOV</c>.</summary>
    public float MaximumFieldOfViewScale => HalfTangent(MaximumFOV);

    /// <summary>The tangent of half an angle in degrees, at the FPU's precision: <c>(a × 0.5) × 0.017453292</c> through <c>fptan</c>.</summary>
    private static float HalfTangent(float degrees) => new Fpu(Math.Tan(((new Fpu(degrees) * 0.5f) * 0.017453292f).Value)).F;

    /// <summary>The name scripts spawn this type by.</summary>
    public const string TypeName = "Zoom";

    /// <summary>Gets or sets the <c>Minimum FOV</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Degrees, "Minimum FOV")]
    public float MinimumFOV { get; set; }

    /// <summary>Gets or sets the <c>Maximum FOV</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Degrees, "Maximum FOV")]
    public float MaximumFOV { get; set; }

    /// <summary>Gets or sets the <c>Zoom Step Factor</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "Zoom Step Factor")]
    public float ZoomStepFactor { get; set; }

    /// <summary>Gets or sets the <c>Overlay QTaskType</c> parameter.</summary>
    [ScriptParam(ScriptParamType.EnumInt32, "Overlay QTaskType")]
    public int OverlayQTaskType { get; set; }

    /// <summary>Gets or sets the <c>Loop Sound</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Loop Sound")]
    public bool LoopSound { get; set; }

    /// <summary>Gets or sets the <c>Sound</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "Sound")]
    public string Sound { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>Stand Breath Offset (degrees)</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "Stand Breath Offset (degrees)")]
    public float StandBreathOffsetDegrees { get; set; }

    /// <summary>Gets or sets the <c>Crouch Breath Offset (degrees)</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "Crouch Breath Offset (degrees)")]
    public float CrouchBreathOffsetDegrees { get; set; }

    /// <summary>Gets or sets the <c>Flat Breath Offset (degrees)</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "Flat Breath Offset (degrees)")]
    public float FlatBreathOffsetDegrees { get; set; }
}

/// <summary>A fire type that throws or launches a projectile, <c>ProjectileLauncher</c>.</summary>
/// <remarks>
/// The parameters are the script's own declaration, in wire order (<c>Task_DeclareParameters</c> in <c>WEAPONS/weapon.qvm</c>). A fire type is a child of the weapon whose fire it carries out.
/// </remarks>
public sealed partial class Igi2ProjectileLauncherTask : Igi2Task
{
    /// <summary>The name scripts spawn this type by.</summary>
    public const string TypeName = "ProjectileLauncher";

    /// <summary>Gets or sets the <c>Create On Release</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Create On Release")]
    public bool CreateOnRelease { get; set; }

    /// <summary>Gets or sets the <c>Ammo Type</c> parameter.</summary>
    [ScriptParam(ScriptParamType.EnumInt32, "Ammo Type")]
    public int AmmoType { get; set; }

    /// <summary>Gets or sets the <c>Rounds Per Minute</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Int32, "Rounds Per Minute")]
    public int RoundsPerMinute { get; set; }

    /// <summary>Gets or sets the <c>Speed (m/s)</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "Speed (m/s)")]
    public float SpeedMps { get; set; }

    /// <summary>Gets or sets the <c>Effective Range (meters)</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "Effective Range (meters)")]
    public float EffectiveRangeMeters { get; set; }

    /// <summary>Gets or sets the <c>Max Hold Time (seconds)</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "Max Hold Time (seconds)")]
    public float MaxHoldTimeSeconds { get; set; }

    /// <summary>Gets or sets the <c>Explode Time (seconds)</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "Explode Time (seconds)")]
    public float ExplodeTimeSeconds { get; set; }

    /// <summary>Gets or sets the <c>1st Fire Anim</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "1st Fire Anim")]
    public string FirstFireAnim { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>1st Fire Last Anim</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "1st Fire Last Anim")]
    public string FirstFireLastAnim { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>3rd Fire Anim</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "3rd Fire Anim")]
    public string ThirdFireAnim { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>3rd Fire Last Anim</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "3rd Fire Last Anim")]
    public string ThirdFireLastAnim { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>Fire Sound</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "Fire Sound")]
    public string FireSound { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>Fire Hold Sound</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "Fire Hold Sound")]
    public string FireHoldSound { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>Fire Release Sound</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "Fire Release Sound")]
    public string FireReleaseSound { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>Projectile QTaskType</c> parameter.</summary>
    [ScriptParam(ScriptParamType.EnumInt32, "Projectile QTaskType")]
    public int ProjectileQTaskType { get; set; }
}

/// <summary>A fire type that places a charge, <c>C4Bomb</c>.</summary>
/// <remarks>
/// The parameters are the script's own declaration, in wire order (<c>Task_DeclareParameters</c> in <c>WEAPONS/weapon.qvm</c>). A fire type is a child of the weapon whose fire it carries out.
/// </remarks>
public sealed partial class Igi2C4BombTask : Igi2Task
{
    /// <summary>The name scripts spawn this type by.</summary>
    public const string TypeName = "C4Bomb";

    /// <summary>Gets or sets the <c>AmmoType</c> parameter.</summary>
    [ScriptParam(ScriptParamType.EnumInt32, "AmmoType")]
    public int AmmoType { get; set; }

    /// <summary>Gets or sets the <c>Rounds Per Minute</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Int32, "Rounds Per Minute")]
    public int RoundsPerMinute { get; set; }

    /// <summary>Gets or sets the <c>Effective Range (meters)</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "Effective Range (meters)")]
    public float EffectiveRangeMeters { get; set; }

    /// <summary>Gets or sets the <c>Time before arming (seconds)</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "Time before arming (seconds)")]
    public float TimeBeforeArmingSeconds { get; set; }

    /// <summary>Gets or sets the <c>Explosion radius (meters)</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "Explosion radius (meters)")]
    public float ExplosionRadiusMeters { get; set; }

    /// <summary>Gets or sets the <c>1st Fire Anim</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "1st Fire Anim")]
    public string FirstFireAnim { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>3rd Fire Anim</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "3rd Fire Anim")]
    public string ThirdFireAnim { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>Fire Sound</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "Fire Sound")]
    public string FireSound { get; set; } = string.Empty;
}

/// <summary>A fire type that lays a proximity mine, <c>ProximityMine</c>.</summary>
/// <remarks>
/// The parameters are the script's own declaration, in wire order (<c>Task_DeclareParameters</c> in <c>WEAPONS/weapon.qvm</c>). A fire type is a child of the weapon whose fire it carries out.
/// </remarks>
public sealed partial class Igi2ProximityMineTask : Igi2Task
{
    /// <summary>The name scripts spawn this type by.</summary>
    public const string TypeName = "ProximityMine";

    /// <summary>Gets or sets the <c>AmmoType</c> parameter.</summary>
    [ScriptParam(ScriptParamType.EnumInt32, "AmmoType")]
    public int AmmoType { get; set; }

    /// <summary>Gets or sets the <c>Rounds Per Minute</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Int32, "Rounds Per Minute")]
    public int RoundsPerMinute { get; set; }

    /// <summary>Gets or sets the <c>Effective Range (meters)</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "Effective Range (meters)")]
    public float EffectiveRangeMeters { get; set; }

    /// <summary>Gets or sets the <c>Time before arming (seconds)</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "Time before arming (seconds)")]
    public float TimeBeforeArmingSeconds { get; set; }

    /// <summary>Gets or sets the <c>Countdown time (seconds)</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "Countdown time (seconds)")]
    public float CountdownTimeSeconds { get; set; }

    /// <summary>Gets or sets the <c>Activate radius (meters)</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "Activate radius (meters)")]
    public float ActivateRadiusMeters { get; set; }

    /// <summary>Gets or sets the <c>Explosion radius (meters)</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "Explosion radius (meters)")]
    public float ExplosionRadiusMeters { get; set; }

    /// <summary>Gets or sets the <c>1st Fire Anim</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "1st Fire Anim")]
    public string FirstFireAnim { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>3rd Fire Anim</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "3rd Fire Anim")]
    public string ThirdFireAnim { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>Fire Sound</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "Fire Sound")]
    public string FireSound { get; set; } = string.Empty;
}

/// <summary>A fire type that lays a remote mine, <c>RemoteMine</c>.</summary>
/// <remarks>
/// The parameters are the script's own declaration, in wire order (<c>Task_DeclareParameters</c> in <c>WEAPONS/weapon.qvm</c>). A fire type is a child of the weapon whose fire it carries out.
/// </remarks>
public sealed partial class Igi2RemoteMineTask : Igi2Task
{
    /// <summary>The name scripts spawn this type by.</summary>
    public const string TypeName = "RemoteMine";

    /// <summary>Gets or sets the <c>AmmoType</c> parameter.</summary>
    [ScriptParam(ScriptParamType.EnumInt32, "AmmoType")]
    public int AmmoType { get; set; }

    /// <summary>Gets or sets the <c>Rounds Per Minute</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Int32, "Rounds Per Minute")]
    public int RoundsPerMinute { get; set; }

    /// <summary>Gets or sets the <c>Effective Range (meters)</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "Effective Range (meters)")]
    public float EffectiveRangeMeters { get; set; }

    /// <summary>Gets or sets the <c>Time before arming (seconds)</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "Time before arming (seconds)")]
    public float TimeBeforeArmingSeconds { get; set; }

    /// <summary>Gets or sets the <c>Countdown time (seconds)</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "Countdown time (seconds)")]
    public float CountdownTimeSeconds { get; set; }

    /// <summary>Gets or sets the <c>Activate radius (meters)</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "Activate radius (meters)")]
    public float ActivateRadiusMeters { get; set; }

    /// <summary>Gets or sets the <c>Explosion radius (meters)</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "Explosion radius (meters)")]
    public float ExplosionRadiusMeters { get; set; }

    /// <summary>Gets or sets the <c>1st Fire Anim</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "1st Fire Anim")]
    public string FirstFireAnim { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>3rd Fire Anim</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "3rd Fire Anim")]
    public string ThirdFireAnim { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>Fire Sound</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "Fire Sound")]
    public string FireSound { get; set; } = string.Empty;
}

/// <summary>A fire type that heals its user, <c>Medipack</c>.</summary>
/// <remarks>
/// The parameters are the script's own declaration, in wire order (<c>Task_DeclareParameters</c> in <c>WEAPONS/weapon.qvm</c>). A fire type is a child of the weapon whose fire it carries out.
/// </remarks>
public sealed partial class Igi2MedipackTask : Igi2Task
{
    /// <summary>The name scripts spawn this type by.</summary>
    public const string TypeName = "Medipack";

    /// <summary>Gets or sets the <c>Factor of human max damage to add for each round</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "Factor of human max damage to add for each round")]
    public float FactorOfHumanMaxDamageToAddForEachRound { get; set; }

    /// <summary>Gets or sets the <c>Rounds Per minute</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Int32, "Rounds Per minute")]
    public int RoundsPerMinute { get; set; }

    /// <summary>Gets or sets the <c>AmmoType</c> parameter.</summary>
    [ScriptParam(ScriptParamType.EnumInt32, "AmmoType")]
    public int AmmoType { get; set; }

    /// <summary>Gets or sets the <c>1st Fire Anim</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "1st Fire Anim")]
    public string FirstFireAnim { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>1st Fire Hold Anim</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "1st Fire Hold Anim")]
    public string FirstFireHoldAnim { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>1st Fire Release Anim</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "1st Fire Release Anim")]
    public string FirstFireReleaseAnim { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>3rd Fire Anim</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "3rd Fire Anim")]
    public string ThirdFireAnim { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>3rd Fire Hold Anim</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "3rd Fire Hold Anim")]
    public string ThirdFireHoldAnim { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>3rd Fire Release Anim</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "3rd Fire Release Anim")]
    public string ThirdFireReleaseAnim { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>Fire Sound</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "Fire Sound")]
    public string FireSound { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>Hold Sound</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "Hold Sound")]
    public string HoldSound { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>Release Sound</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "Release Sound")]
    public string ReleaseSound { get; set; } = string.Empty;
}

/// <summary>A fire type that stabs, <c>Knife</c>.</summary>
/// <remarks>
/// The parameters are the script's own declaration, in wire order (<c>Task_DeclareParameters</c> in <c>WEAPONS/weapon.qvm</c>). A fire type is a child of the weapon whose fire it carries out.
/// </remarks>
public sealed partial class Igi2KnifeTask : Igi2Task
{
    /// <summary>The name scripts spawn this type by.</summary>
    public const string TypeName = "Knife";

    /// <summary>Gets or sets the <c>Traces</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Int32, "Traces")]
    public int Traces { get; set; }

    /// <summary>Gets or sets the <c>Trace FOV</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Degrees, "Trace FOV")]
    public float TraceFOV { get; set; }

    /// <summary>Gets or sets the <c>Delay (seconds)</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "Delay (seconds)")]
    public float DelaySeconds { get; set; }

    /// <summary>Gets or sets the <c>Damage Factor</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Degrees, "Damage Factor")]
    public float DamageFactor { get; set; }

    /// <summary>Gets or sets the <c>1st Fire Anim</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "1st Fire Anim")]
    public string FirstFireAnim { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>1st Fire Anim1</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "1st Fire Anim1")]
    public string FirstFireAnim1 { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>1st Fire Anim2</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "1st Fire Anim2")]
    public string FirstFireAnim2 { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>3rd Fire Anim</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "3rd Fire Anim")]
    public string ThirdFireAnim { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>Fire Sound</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "Fire Sound")]
    public string FireSound { get; set; } = string.Empty;
}

/// <summary>A fire type that marks a target for an air strike, <c>LaserDesignator</c>.</summary>
/// <remarks>
/// The parameters are the script's own declaration, in wire order (<c>Task_DeclareParameters</c> in <c>WEAPONS/weapon.qvm</c>). A fire type is a child of the weapon whose fire it carries out.
/// </remarks>
public sealed partial class Igi2LaserDesignatorTask : Igi2Task
{
    /// <summary>The name scripts spawn this type by.</summary>
    public const string TypeName = "LaserDesignator";

    /// <summary>Gets or sets the <c>Range</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "Range")]
    public float Range { get; set; }

    /// <summary>Gets or sets the <c>1st person fire anim</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "1st person fire anim")]
    public string FirstPersonFireAnim { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>1st person fire hold anim</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "1st person fire hold anim")]
    public string FirstPersonFireHoldAnim { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>1st person fire release anim</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "1st person fire release anim")]
    public string FirstPersonFireReleaseAnim { get; set; } = string.Empty;
}

/// <summary>A fire type that pulls a grenade's pin, <c>PullPin</c>.</summary>
/// <remarks>
/// The parameters are the script's own declaration, in wire order (<c>Task_DeclareParameters</c> in <c>WEAPONS/weapon.qvm</c>). A fire type is a child of the weapon whose fire it carries out.
/// </remarks>
public sealed partial class Igi2PullPinTask : Igi2Task
{
    /// <summary>The name scripts spawn this type by.</summary>
    public const string TypeName = "PullPin";

    /// <summary>Gets or sets the <c>1st Fire Anim</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "1st Fire Anim")]
    public string FirstFireAnim { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>3rd Fire Anim</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "3rd Fire Anim")]
    public string ThirdFireAnim { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>Explode Time (seconds)</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "Explode Time (seconds)")]
    public float ExplodeTimeSeconds { get; set; }
}

/// <summary>A fire type that steps a gun through its fire modes, <c>FireMode</c>; it has no parameters.</summary>
/// <remarks>
/// The parameters are the script's own declaration, in wire order (<c>Task_DeclareParameters</c> in <c>WEAPONS/weapon.qvm</c>). A fire type is a child of the weapon whose fire it carries out.
/// </remarks>
public sealed partial class Igi2FireModeTask : Igi2Task
{
    /// <summary>The name scripts spawn this type by.</summary>
    public const string TypeName = "FireMode";
}

/// <summary>A fire type that turns a weapon's laser dot on and off, <c>LaserDotSwitch</c>; it has no parameters.</summary>
/// <remarks>
/// The parameters are the script's own declaration, in wire order (<c>Task_DeclareParameters</c> in <c>WEAPONS/weapon.qvm</c>). A fire type is a child of the weapon whose fire it carries out.
/// </remarks>
public sealed partial class Igi2LaserDotSwitchTask : Igi2Task
{
    /// <summary>The name scripts spawn this type by.</summary>
    public const string TypeName = "LaserDotSwitch";
}
