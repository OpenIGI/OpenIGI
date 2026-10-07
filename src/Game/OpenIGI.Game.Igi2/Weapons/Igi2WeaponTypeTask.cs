using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>One weapon, as <c>WEAPONS/weapon.qvm</c> declares it: <c>WeaponType</c> (<c>WeaponType_Open</c>, <c>igi2 0x436C60</c>).</summary>
/// <remarks>
/// <para>
/// The parameters are the script's own declaration, in wire order (<c>Task_DeclareParameters</c> in <c>WEAPONS/weapon.qvm</c>). Its children are the fire types
/// <c>Primary Fire TaskType</c> and <c>Secondary Fire TaskType</c> name, and parameter containers of tagged values.
/// </para>
/// <para>
/// <c>Weapon Type</c> is the family a body borrows clips from (<see cref="Animation.Igi2WeaponAnimFamily"/>), and
/// <c>Weapon Animation Name</c> the name its own clips carry (<see cref="Animation.Igi2WeaponTypeAnims"/>).
/// </para>
/// </remarks>
public sealed partial class Igi2WeaponTypeTask : GameTask
{
    /// <summary>The name scripts spawn this type by.</summary>
    public const string TypeName = "WeaponType";

    /// <summary>Gets or sets the <c>Weapon ID</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "Weapon ID")]
    public string WeaponId { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>Weapon Name</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String256, "Weapon Name")]
    public string WeaponName { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>Weapon Animation Name</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String256, "Weapon Animation Name")]
    public string WeaponAnimationName { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>Weapon Sprite</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String256, "Weapon Sprite")]
    public string WeaponSprite { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>Weapon Type</c> parameter.</summary>
    [ScriptParam(ScriptParamType.EnumInt32, "Weapon Type")]
    public int WeaponType { get; set; }

    /// <summary>Gets or sets the <c>Weapon Category</c> parameter.</summary>
    [ScriptParam(ScriptParamType.EnumInt32, "Weapon Category")]
    public int WeaponCategory { get; set; }

    /// <summary>Gets or sets the <c>Sight Display Type</c> parameter.</summary>
    [ScriptParam(ScriptParamType.EnumInt32, "Sight Display Type")]
    public int SightDisplayType { get; set; }

    /// <summary>Gets or sets the <c>Fixed Model (Used by GunPickup)</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "Fixed Model (Used by GunPickup)")]
    public string FixedModel { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>Available FireModes</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Int32, "Available FireModes")]
    public int AvailableFireModes { get; set; }

    /// <summary>Gets or sets the <c>Firemode Rounds In Succession</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Int32, "Firemode Rounds In Succession")]
    public int FiremodeRoundsInSuccession { get; set; }

    /// <summary>Gets or sets the <c>Firemode Rounds In Succession1</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Int32, "Firemode Rounds In Succession1")]
    public int FiremodeRoundsInSuccession1 { get; set; }

    /// <summary>Gets or sets the <c>Firemode Rounds In Succession2</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Int32, "Firemode Rounds In Succession2")]
    public int FiremodeRoundsInSuccession2 { get; set; }

    /// <summary>Gets or sets the <c>Firemode Name</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "Firemode Name")]
    public string FiremodeName { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>Firemode Name1</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "Firemode Name1")]
    public string FiremodeName1 { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>Firemode Name2</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "Firemode Name2")]
    public string FiremodeName2 { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>Firemode Firesound Stop Time</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "Firemode Firesound Stop Time")]
    public float FiremodeFiresoundStopTime { get; set; }

    /// <summary>Gets or sets the <c>Firemode Firesound Stop Time1</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "Firemode Firesound Stop Time1")]
    public float FiremodeFiresoundStopTime1 { get; set; }

    /// <summary>Gets or sets the <c>Firemode Firesound Stop Time2</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "Firemode Firesound Stop Time2")]
    public float FiremodeFiresoundStopTime2 { get; set; }

    /// <summary>Gets or sets the <c>Standing Precision (Degrees)</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Degrees, "Standing Precision (Degrees)")]
    public float StandingPrecisionDegrees { get; set; }

    /// <summary>Gets or sets the <c>Running Precision (Degrees)</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Degrees, "Running Precision (Degrees)")]
    public float RunningPrecisionDegrees { get; set; }

    /// <summary>Gets or sets the <c>Walking Precision (Degrees)</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Degrees, "Walking Precision (Degrees)")]
    public float WalkingPrecisionDegrees { get; set; }

    /// <summary>Gets or sets the <c>Crouching Precision (Degrees)</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Degrees, "Crouching Precision (Degrees)")]
    public float CrouchingPrecisionDegrees { get; set; }

    /// <summary>Gets or sets the <c>Crouch Walking Precision (Degrees)</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Degrees, "Crouch Walking Precision (Degrees)")]
    public float CrouchWalkingPrecisionDegrees { get; set; }

    /// <summary>Gets or sets the <c>Lying Flat Precision (Degrees)</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Degrees, "Lying Flat Precision (Degrees)")]
    public float LyingFlatPrecisionDegrees { get; set; }

    /// <summary>Gets or sets the <c>Crawling Precision (Degrees)</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Degrees, "Crawling Precision (Degrees)")]
    public float CrawlingPrecisionDegrees { get; set; }

    /// <summary>Gets or sets the <c>Jumping Precision Modifier</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "Jumping Precision Modifier")]
    public float JumpingPrecisionModifier { get; set; }

    /// <summary>Gets or sets the <c>Turning Precision Modifier</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "Turning Precision Modifier")]
    public float TurningPrecisionModifier { get; set; }

    /// <summary>Gets or sets the <c>Movement Speed Modifier</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "Movement Speed Modifier")]
    public float MovementSpeedModifier { get; set; }

    /// <summary>Gets or sets the <c>(De)Activate Time</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "(De)Activate Time")]
    public float DeActivateTime { get; set; }

    /// <summary>Gets or sets the <c>1st Person Stand Anim</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "1st Person Stand Anim")]
    public string FirstPersonStandAnim { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>1st Person Move Anim</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "1st Person Move Anim")]
    public string FirstPersonMoveAnim { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>1st Person Activate Anim</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "1st Person Activate Anim")]
    public string FirstPersonActivateAnim { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>1st Person DeActivate Anim</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "1st Person DeActivate Anim")]
    public string FirstPersonDeActivateAnim { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>3rd Person Stand Anim</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "3rd Person Stand Anim")]
    public string ThirdPersonStandAnim { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>3rd Person Walk Anim</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "3rd Person Walk Anim")]
    public string ThirdPersonWalkAnim { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>3rd Person Run Anim</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "3rd Person Run Anim")]
    public string ThirdPersonRunAnim { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>3rd Person Crouch Stand Anim</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "3rd Person Crouch Stand Anim")]
    public string ThirdPersonCrouchStandAnim { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>3rd Person Crouch Walk Anim</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "3rd Person Crouch Walk Anim")]
    public string ThirdPersonCrouchWalkAnim { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>3rd Person Activate Anim</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "3rd Person Activate Anim")]
    public string ThirdPersonActivateAnim { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>3rd Person DeActivate Anim</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "3rd Person DeActivate Anim")]
    public string ThirdPersonDeActivateAnim { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>Activate Sound</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "Activate Sound")]
    public string ActivateSound { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>DeActivate Sound</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String32, "DeActivate Sound")]
    public string DeActivateSound { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>TracerTexture</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String16, "TracerTexture")]
    public string TracerTexture { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>TracerVelocity(m/s)</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "TracerVelocity(m/s)")]
    public float TracerVelocityMps { get; set; }

    /// <summary>Gets or sets the <c>Primary Fire TaskType</c> parameter.</summary>
    [ScriptParam(ScriptParamType.EnumInt32, "Primary Fire TaskType")]
    public int PrimaryFireTaskType { get; set; }

    /// <summary>Gets or sets the <c>Primary Force Reload</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Primary Force Reload")]
    public bool PrimaryForceReload { get; set; }

    /// <summary>Gets or sets the <c>Primary Reload rounds individiually</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Primary Reload rounds individiually")]
    public bool PrimaryReloadRoundsIndividually { get; set; }

    /// <summary>Gets or sets the <c>Secondary Fire TaskType</c> parameter.</summary>
    [ScriptParam(ScriptParamType.EnumInt32, "Secondary Fire TaskType")]
    public int SecondaryFireTaskType { get; set; }

    /// <summary>Gets or sets the <c>Secondary Force Reload</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Secondary Force Reload")]
    public bool SecondaryForceReload { get; set; }

    /// <summary>Gets or sets the <c>Secondary Reload rounds individiually</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Secondary Reload rounds individiually")]
    public bool SecondaryReloadRoundsIndividually { get; set; }

    /// <summary>Gets or sets the <c>Selectable When Empty</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Selectable When Empty")]
    public bool SelectableWhenEmpty { get; set; }

    /// <summary>Gets or sets the <c>Pickup sprite</c> parameter.</summary>
    [ScriptParam(ScriptParamType.String256, "Pickup sprite")]
    public string PickupSprite { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>Laser Dot</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Laser Dot")]
    public bool LaserDot { get; set; }

    /// <summary>Gets or sets the <c>Laser Dot Always On</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Laser Dot Always On")]
    public bool LaserDotAlwaysOn { get; set; }

    /// <summary>Gets or sets the <c>Netshop price</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Int32, "Netshop price")]
    public int NetshopPrice { get; set; }

    /// <summary>Gets or sets the <c>Bad Guys can buy</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Bad Guys can buy")]
    public bool BadGuysCanBuy { get; set; }

    /// <summary>Gets or sets the <c>Good Guys can buy</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Good Guys can buy")]
    public bool GoodGuysCanBuy { get; set; }

    /// <summary>Gets or sets the <c>Netshop category</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Int32, "Netshop category")]
    public int NetshopCategory { get; set; }

    /// <summary>Gets or sets the <c>Netshop slot position</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Int32, "Netshop slot position")]
    public int NetshopSlotPosition { get; set; }

    /// <summary>Gets or sets the <c>Smoke Particle Size</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Real32, "Smoke Particle Size")]
    public float SmokeParticleSize { get; set; }

    /// <summary>Gets or sets the <c>Can weapon be dropped in 1P game</c> parameter.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Can weapon be dropped in 1P game")]
    public bool CanWeaponBeDroppedInSinglePlayerGame { get; set; }
}
