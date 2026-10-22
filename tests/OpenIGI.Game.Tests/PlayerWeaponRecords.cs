using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The retail weapon and ammunition records the player-weapon tests drive, transcribed as literals so the
/// tests run without game data; <see cref="RetailWeaponFiringTests"/> checks the same numbers against the
/// real scripts.
/// </summary>
internal static class PlayerWeaponRecords
{
    internal static readonly int[] Cycle = [20, 1, 7];

    /// <summary>The knife, as <c>weapons/KNIFE/weapon.qvm</c> declares it.</summary>
    internal static WeaponType Knife => new()
    {
        WeaponId = 20,
        ScriptId = "WEAPON_ID_KNIFE",
        Category = WeaponCategory.Knife,
        CalibreId = -1,
        RoundsPerMinute = 140,
        RoundsPerClip = -1,
        MaximumRoundsPerBurst = 1,
        BulletsPerRound = 1,
        EffectiveRange = 1f,
        StandAnimation = 26,
        FireAnimation1 = 28,
        FireAnimation2 = 29,
        FireAnimation3 = 30,
    };

    /// <summary>The Glock 17 — semi-automatic, because its burst cap is one.</summary>
    internal static WeaponType Glock => new()
    {
        WeaponId = 1,
        ScriptId = "WEAPON_ID_GLOCK",
        Category = WeaponCategory.Pistol,
        CalibreId = 919,
        RoundsPerMinute = 400,
        RoundsPerClip = 17,
        MaximumRoundsPerBurst = 1,
        BulletsPerRound = 1,
        EffectiveRange = 80f,
        MinimumSpread = -1.5f,
        MaximumSpread = 1.5f,
        FixedRecoilX = 1.5f,
        StandAnimation = 14,
        FireAnimation1 = 16,
        FireAnimation2 = -1,
        FireAnimation3 = -1,
    };

    /// <summary>The MP5 SD3 — fully automatic, because its burst cap is the whole magazine.</summary>
    internal static WeaponType Mp5Sd => new()
    {
        WeaponId = 7,
        ScriptId = "WEAPON_ID_MP5SD",
        Category = WeaponCategory.Gun,
        CalibreId = 919,
        RoundsPerMinute = 700,
        RoundsPerClip = 32,
        MaximumRoundsPerBurst = 32,
        BulletsPerRound = 1,
        EffectiveRange = 80f,
        MinimumSpread = -2f,
        MaximumSpread = 2f,
        FixedRecoilX = 0.8f,
        RandomRecoilX = 0.8f,
        StandAnimation = 18,
        FireAnimation1 = 20,
    };

    internal static AmmoType NineMillimetre => new() { AmmoId = 0, ScriptId = "AMMO_ID_919", CalibreId = 919 };

    internal static WeaponType Binoculars => new()
    {
        WeaponId = 30,
        ScriptId = "WEAPON_ID_BINOCULARS",
        Category = WeaponCategory.Binocular,
        Weapon = new WeaponTaskBinding("TASKTYPE_BINOCULAR", 0, [0.5f, 0.1f, 1.0f], string.Empty, 0),
    };

    internal static WeaponType Medipack => new()
    {
        WeaponId = 31,
        ScriptId = "WEAPON_ID_MEDIPACK",
        Category = WeaponCategory.Medipack,
        CalibreId = 10005,
        // The shipped medipack fires its healing handler at 120 RPM: one dose per 15 ticks.
        RoundsPerMinute = 120,
        RoundsPerClip = 1,
        FireAnimation1 = 77,
        FireAnimation2 = 78,
        FireAnimation3 = 79,
        Weapon = new WeaponTaskBinding("TASKTYPE_MEDIPACK", 0, [0.25f], string.Empty, 0),
    };

    internal static AmmoType MedipackAmmo => new() { AmmoId = 12, ScriptId = "AMMO_ID_MEDIPACK", CalibreId = 10005 };

    /// <summary>The frag grenade, as the roster and the physics table declare it.</summary>
    internal static WeaponType Grenade => new()
    {
        WeaponId = 3,
        ScriptId = "WEAPON_ID_GRENADE",
        Category = WeaponCategory.Grenade,
        CalibreId = 10000,
        MuzzleVelocity = 15f,
        RoundsPerMinute = 60,
        RoundsPerClip = 3,
        MaximumRoundsPerBurst = 1,
        BulletsPerRound = 1,
        EffectiveRange = 1f,
        GunModel = "135_01_1",
        StandAnimation = 22,
        FireAnimation1 = 24,
    };

    internal static AmmoType GrenadeAmmo => new() { AmmoId = 8, ScriptId = "AMMO_ID_GRENADE", CalibreId = 10000 };

    internal static WeaponType M16A2 => new()
    {
        WeaponId = 14,
        ScriptId = "WEAPON_ID_M16A2",
        Category = WeaponCategory.Gun,
        CalibreId = 556,
        MuzzleVelocity = 900f,
        RoundsPerMinute = 700,
        RoundsPerClip = 30,
        MaximumRoundsPerBurst = 30,
        BulletsPerRound = 1,
        EffectiveRange = 200f,
        Weapon = new WeaponTaskBinding("TASKTYPE_GUNM16A2", 0, [85f, 3f, 1f], string.Empty, 0),
        Projectile = new WeaponTaskBinding("TASKTYPE_M203", 0, [], "108_01_1", 0),
    };

    internal static WeaponType DualUzi => new()
    {
        WeaponId = 40,
        ScriptId = "WEAPON_ID_UZIX2",
        Category = WeaponCategory.Gun,
        CalibreId = 919,
        RoundsPerMinute = 900,
        RoundsPerClip = 64,
        MaximumRoundsPerBurst = 64,
        BulletsPerRound = 2,
        EffectiveRange = 80f,
        Weapon = new WeaponTaskBinding("TASKTYPE_GUN", 0, [], string.Empty, 0),
    };

    internal static AmmoType M203Ammo => new() { AmmoId = 10, ScriptId = "AMMO_ID_M203", CalibreId = 10002 };
}
