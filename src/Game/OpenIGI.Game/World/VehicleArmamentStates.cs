using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.World;

/// <summary>One vehicle's death zones and how fast it moved on the last tick.</summary>
internal sealed class VehicleDeathZoneState(IReadOnlyList<VehicleDeathZone> zones)
{
    public IReadOnlyList<VehicleDeathZone> Zones { get; } = zones;
    public Vector3d? LastPosition { get; set; }
    public double SpeedUnitsPerTick { get; set; }
}

/// <summary>What one armed vehicle sees, and the guns that aim from it.</summary>
internal sealed class VehicleArmament(WeaponType? legacyWeapon, List<VehicleGunState> guns)
{
    public AiVehicleCombat Sight { get; } = new();
    public WeaponType? LegacyWeapon { get; } = legacyWeapon;
    public List<VehicleGunState> Guns { get; } = guns;

    /// <summary>Gets or sets whether the guns have been seated at least once.</summary>
    public bool Seated { get; set; }
}

/// <summary>One model gun's turret, cadence and ammunition.</summary>
internal sealed class VehicleGunState(VehicleGun gun)
{
    public VehicleGun Gun { get; } = gun;
    public VehicleGunTurret Turret { get; } = new(gun.Mount);
    public int Cooldown { get; set; }
    public int Ammunition { get; set; } = gun.Mount.Ammunition;
    public int RoundsThisPull { get; set; }

    /// <summary>Gets or sets the hold of Gun_Fire, as its last run this tick left it.</summary>
    public bool Hold { get; set; }

    /// <summary>Gets or sets how many rounds the gun fired this tick.</summary>
    public int RoundsFiredThisTick { get; set; }

    /// <summary>Gets or sets a serial that advances once per round fired, for the gun flame's roll.</summary>
    public int ShotSequence { get; set; }

    /// <summary>Gets or sets the mount's position as the last seat left it (<c>+32</c>), which the gun aims from.</summary>
    public Vector3d MountPosition { get; set; }

    /// <summary>Gets or sets the mount's orientation as the last seat left it (<c>+112</c>).</summary>
    public Matrix3f MountOrientation { get; set; } = Matrix3f.Identity;

    /// <summary>Gets or sets the weapon object's position as the last seat left it (<c>+376</c>).</summary>
    public Vector3d WeaponPosition { get; set; }

    /// <summary>Gets or sets the weapon object's orientation as the last seat left it (<c>+400</c>).</summary>
    public Matrix3f WeaponOrientation { get; set; } = Matrix3f.Identity;
}
