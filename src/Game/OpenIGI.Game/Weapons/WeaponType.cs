namespace OpenIGI.Game.Weapons;

/// <summary>
/// One entry of the runtime weapon table, as <c>DefineWeaponType</c> (<c>0x413F90</c>) fills it.
/// </summary>
/// <remarks>
/// <para>
/// The original keeps these in a flat array of 1768-byte records based at <c>0x56E258</c>, indexed by
/// load order and searched linearly — by id at <c>0x413BD0</c>, by <see cref="GunModel"/> at
/// <c>0x413C00</c>. Both searches are order-independent, so nothing depends on which directory the
/// loader happened to read first.
/// </para>
/// <para>
/// Property order is argument order, which is what a reader has to get right.
/// </para>
/// </remarks>
public sealed class WeaponType
{
    /// <summary>Gets the matching runtime entry loaded from <c>weapons/weaponconfig.qvm</c>.</summary>
    /// <remarks>The config supplies the complete shared weapon record and task-specific tuning.</remarks>
    public WeaponConfigTask? RetailConfig { get; internal set; }

    /// <summary>Creates the runtime record represented by the matching weaponconfig.qvm entry.</summary>
    internal WeaponType WithRetailConfig(WeaponConfigTask config) => new()
    {
        RetailConfig = config,
        Index = Index,
        WeaponId = config.WeaponId,
        ScriptId = config.ScriptId,
        DisplayName = config.DisplayName,
        Manufacturer = config.Manufacturer,
        Description = config.Description,
        Category = (WeaponCategory)config.WeaponType,
        SightDisplay = (SightDisplayType)config.SightDisplayType,
        AmmoDisplay = (AmmoDisplayType)config.AmmoDisplayType,
        MassInGrams = config.MassInGrams,
        CalibreId = config.CalibreId,
        DamageFactor = config.DamageFactor,
        PenetrationPower = config.PenetrationPower,
        ReloadTimeSeconds = config.ReloadTimeSeconds,
        MuzzleVelocity = config.MuzzleVelocity,
        BulletsPerRound = config.BulletsPerRound,
        RoundsPerMinute = config.RoundsPerMinute,
        RoundsPerClip = config.RoundsPerClip,
        MaximumRoundsPerBurst = config.MaximumRoundsPerBurst,
        MinimumSpread = config.MinimumSpread,
        MaximumSpread = config.MaximumSpread,
        FixedRecoilX = config.FixedRecoilX,
        FixedRecoilZ = config.FixedRecoilZ,
        RandomRecoilX = config.RandomRecoilX,
        RandomRecoilZ = config.RandomRecoilZ,
        TypeOfWeapon = config.TypeOfWeapon,
        EffectiveRange = config.EffectiveRange,
        WeaponUsers = config.WeaponUsers,
        WeaponLength = config.WeaponLength,
        BarrelLength = config.BarrelLength,
        GunModel = config.GunModel,
        CasingModel = config.CasingModel,
        StandAnimation = config.StandAnimation,
        MoveAnimation = config.MoveAnimation,
        FireAnimation1 = config.FireAnimation1,
        FireAnimation2 = config.FireAnimation2,
        FireAnimation3 = config.FireAnimation3,
        ReloadAnimation = config.ReloadAnimation,
        UpperBodyStandAnimation = config.UpperBodyStandAnimation,
        UpperBodyWalkAnimation = config.UpperBodyWalkAnimation,
        UpperBodyCrouchAnimation = config.UpperBodyCrouchAnimation,
        UpperBodyCrouchRunAnimation = config.UpperBodyCrouchRunAnimation,
        UpperBodyRunAnimation = config.UpperBodyRunAnimation,
        UpperBodyFireAnimation = config.UpperBodyFireAnimation,
        UpperBodyReloadAnimation = config.UpperBodyReloadAnimation,
        FireSound = config.FireSound,
        FireLoopEndSound = config.FireLoopEndSound,
        AiDetectionRange = config.AiDetectionRange,
        Projectile = Projectile,
        Weapon = Weapon,
        SelectableWhenEmpty = config.SelectableWhenEmpty,
    };

    /// <summary>Gets the numeric id the rest of the game addresses this weapon by.</summary>
    /// <remarks>Argument 0. Ids are unique — the original hangs outright on a repeat.</remarks>
    public int WeaponId { get; init; }

    /// <summary>Gets the script symbol this weapon publishes its id under.</summary>
    /// <remarks>
    /// Argument 1. The loader registers this name with <see cref="WeaponId"/> as it reads it, which is
    /// what gives the pattern-registered <c>WEAPON_ID_*</c> symbols their values.
    /// </remarks>
    public string ScriptId { get; init; } = string.Empty;

    /// <summary>Gets the display name.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Gets the manufacturer, as a language key.</summary>
    public string Manufacturer { get; init; } = string.Empty;

    /// <summary>Gets the description, as a language key.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Gets the weapon category.</summary>
    public WeaponCategory Category { get; init; }

    /// <summary>Gets which sight the HUD draws.</summary>
    public SightDisplayType SightDisplay { get; init; }

    /// <summary>Gets how the HUD counts the ammunition.</summary>
    public AmmoDisplayType AmmoDisplay { get; init; }

    /// <summary>Gets the mass in grams.</summary>
    public int MassInGrams { get; init; }

    /// <summary>Gets the ammunition this weapon takes.</summary>
    /// <remarks>
    /// A real calibre names a numerically-titled directory under <c>ammo/</c>. The special types are
    /// numbered from 10000 and their directories are named instead, so the id does not give the path.
    /// The knife takes <c>-1</c> because it consumes nothing.
    /// </remarks>
    public int CalibreId { get; init; }

    /// <summary>Gets the damage multiplier.</summary>
    public float DamageFactor { get; init; }

    /// <summary>Gets how much cover the round defeats.</summary>
    public float PenetrationPower { get; init; }

    /// <summary>Gets the reload time in seconds.</summary>
    public float ReloadTimeSeconds { get; init; }

    /// <summary>Gets the muzzle velocity in metres per second.</summary>
    public float MuzzleVelocity { get; init; }

    /// <summary>Gets how many projectiles one round produces — above one for shot.</summary>
    public int BulletsPerRound { get; init; }

    /// <summary>Gets the cyclic rate in rounds per minute.</summary>
    public int RoundsPerMinute { get; init; }

    /// <summary>Gets the magazine capacity.</summary>
    public int RoundsPerClip { get; init; }

    /// <summary>Gets the longest burst one trigger pull fires.</summary>
    /// <remarks>
    /// Either 1 or <see cref="RoundsPerClip"/> across the whole retail roster, never anything between,
    /// so it reads as semi-automatic against fully automatic rather than as a tunable.
    /// </remarks>
    public int MaximumRoundsPerBurst { get; init; }

    /// <summary>Gets the smallest random spread.</summary>
    public float MinimumSpread { get; init; }

    /// <summary>Gets the largest random spread.</summary>
    public float MaximumSpread { get; init; }

    /// <summary>Gets the fixed recoil about the X axis, in degrees.</summary>
    public float FixedRecoilX { get; init; }

    /// <summary>Gets the fixed recoil about the Z axis, in degrees.</summary>
    public float FixedRecoilZ { get; init; }

    /// <summary>Gets the random recoil about the X axis, in degrees.</summary>
    public float RandomRecoilX { get; init; }

    /// <summary>Gets the random recoil about the Z axis, in degrees.</summary>
    public float RandomRecoilZ { get; init; }

    /// <summary>Gets the weapon's kind, as a language key.</summary>
    public string TypeOfWeapon { get; init; } = string.Empty;

    /// <summary>Gets the effective range in metres.</summary>
    public float EffectiveRange { get; init; }

    /// <summary>Gets who uses it, as a language key.</summary>
    public string WeaponUsers { get; init; } = string.Empty;

    /// <summary>Gets the overall length in millimetres.</summary>
    /// <remarks>
    /// Read as an integer here, though the parallel <see cref="WeaponConfigTask"/> declaration types it
    /// as a real. Every shipped value is whole, so the two agree.
    /// </remarks>
    public int WeaponLength { get; init; }

    /// <summary>Gets the barrel length in millimetres.</summary>
    /// <remarks>An integer here, a real in <see cref="WeaponConfigTask"/>; see <see cref="WeaponLength"/>.</remarks>
    public int BarrelLength { get; init; }

    /// <summary>Gets the model the weapon is drawn with.</summary>
    /// <remarks>Also the key <c>0x413C00</c> looks a weapon up by.</remarks>
    public string GunModel { get; init; } = string.Empty;

    /// <summary>Gets the model an ejected casing uses.</summary>
    public string CasingModel { get; init; } = string.Empty;

    /// <summary>Gets the full-body standing animation.</summary>
    public int StandAnimation { get; init; }

    /// <summary>Gets the full-body moving animation.</summary>
    public int MoveAnimation { get; init; }

    /// <summary>Gets the first firing animation.</summary>
    public int FireAnimation1 { get; init; }

    /// <summary>Gets the second firing animation, or <c>-1</c>.</summary>
    public int FireAnimation2 { get; init; }

    /// <summary>Gets the third firing animation, or <c>-1</c>.</summary>
    public int FireAnimation3 { get; init; }

    /// <summary>Gets the full-body reloading animation.</summary>
    public int ReloadAnimation { get; init; }

    /// <summary>Gets the upper-body standing animation.</summary>
    public int UpperBodyStandAnimation { get; init; }

    /// <summary>Gets the upper-body walking animation.</summary>
    public int UpperBodyWalkAnimation { get; init; }

    /// <summary>Gets the upper-body crouching animation.</summary>
    public int UpperBodyCrouchAnimation { get; init; }

    /// <summary>Gets the upper-body crouch-running animation.</summary>
    public int UpperBodyCrouchRunAnimation { get; init; }

    /// <summary>Gets the upper-body running animation.</summary>
    public int UpperBodyRunAnimation { get; init; }

    /// <summary>Gets the upper-body firing animation.</summary>
    public int UpperBodyFireAnimation { get; init; }

    /// <summary>Gets the upper-body reloading animation.</summary>
    public int UpperBodyReloadAnimation { get; init; }

    /// <summary>Gets the firing sound, or the loop for an automatic weapon.</summary>
    public string FireSound { get; init; } = string.Empty;

    /// <summary>Gets the sound that ends a firing loop.</summary>
    public string FireLoopEndSound { get; init; } = string.Empty;

    /// <summary>Gets how far the shot is heard by the AI.</summary>
    public float AiDetectionRange { get; init; }

    /// <summary>Gets the class spawned for each projectile, and what it read from the same call.</summary>
    public WeaponTaskBinding Projectile { get; init; } = EmptyBinding;

    /// <summary>Gets the class the weapon itself is, and what it read from the same call.</summary>
    public WeaponTaskBinding Weapon { get; init; } = EmptyBinding;

    /// <summary>Gets whether the weapon can still be selected with no ammunition.</summary>
    /// <remarks>
    /// Always <see langword="true"/> on this path: <c>0x413F90</c> stores a literal 1 at <c>+1756</c>
    /// and reads no argument for it. The field is only ever varied by the parallel
    /// <see cref="WeaponConfigTask"/> declaration, which nothing at runtime reads.
    /// </remarks>
    public bool SelectableWhenEmpty { get; init; } = true;

    /// <summary>Gets the position in load order, which is the original's array index.</summary>
    public int Index { get; init; }

    private static WeaponTaskBinding EmptyBinding { get; } = new(string.Empty, 0, [], string.Empty, 0);

    /// <inheritdoc/>
    public override string ToString() => $"{ScriptId} (#{WeaponId}, {DisplayName})";
}
