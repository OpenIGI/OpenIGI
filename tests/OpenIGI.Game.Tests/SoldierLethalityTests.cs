using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// How many rounds a guard actually survives — the arithmetic behind "guards die to one shot".
/// </summary>
/// <remarks>
/// <para>
/// Damage is a plain product of the struck zone's multiplier and the weapon's <c>Damage Factor</c>
/// (<see cref="HumanDamage.DamageOfHit"/>), and death is <c>&gt;=</c> the settings row's
/// <c>Damage scale</c>, which is maximum health — 0.3 / 0.5 / 0.8 for a guard across the three
/// difficulties.
/// </para>
/// <para>
/// <b>So a one-shot kill to the chest is correct for several weapons on the easiest difficulty</b>, and
/// the trunk zones carry a multiplier of 1.0 while the legs carry 0.5 to 1.0. A leg shot killing as
/// fast as a chest shot is therefore <i>not</i> explained by the tables: it means the zone resolved to
/// the trunk, which is what happens when the hit test is given no pose.
/// </para>
/// </remarks>
public sealed class SoldierLethalityTests
{
    private const float EasyGuardHealth = 0.3f;
    private const float HardGuardHealth = 0.8f;

    /// <summary>An AK-47 chest shot kills an easy guard outright, and that is the data, not a bug.</summary>
    [RequiresGameDataFact]
    public void AnAk47ChestShotKillsAnEasyGuardOutright()
    {
        WeaponType ak = Weapon("WEAPON_ID_AK47");
        var health = new HumanHealth(HumanHitZoneTable.Soldier, EasyGuardHealth);

        health.Apply(HumanDamage.DefaultZone, ak.DamageFactor);

        Assert.Equal(0.3f, ak.DamageFactor, 5);
        Assert.True(health.IsDead, "0.3 damage against 0.3 maximum kills, because the test is >=");
    }

    /// <summary>The same round takes three on the hardest, so the difficulty really is the difference.</summary>
    [RequiresGameDataFact]
    public void TheSameRoundTakesThreeOnTheHardest()
    {
        WeaponType ak = Weapon("WEAPON_ID_AK47");
        var health = new HumanHealth(HumanHitZoneTable.Soldier, HardGuardHealth);

        int shots = 0;
        while (!health.IsDead && shots < 10)
        {
            health.Apply(HumanDamage.DefaultZone, ak.DamageFactor);
            shots++;
        }

        Assert.Equal(3, shots);
    }

    /// <summary>
    /// A head shot one-shots by default, but <c>AIFunction_SetInstantDeath(FALSE)</c> — the
    /// <c>isInstantDeathDisabled</c> flag at <c>HumanAI+15037</c> — takes it off the ×100 multiplier so the
    /// hit does only the weapon's plain damage factor. This is the branch <c>0x45D3C0</c> takes on its
    /// <c>v13</c>.
    /// </summary>
    [RequiresGameDataFact]
    public void DisablingInstantDeathTakesAHeadShotOffItsLethalMultiplier()
    {
        WeaponType ak = Weapon("WEAPON_ID_AK47");

        // Default: a head shot (zone 15) one-shots even the toughest guard, because zone 15 carries ×100.
        var normal = new HumanHealth(HumanHitZoneTable.Soldier, HardGuardHealth);
        normal.Apply(HumanDamage.ProximityZone, ak.DamageFactor);
        Assert.True(normal.IsDead, "a head shot one-shots by default");

        // Disabled: the same head shot, and a neck shot, deal only the weapon's damage factor.
        foreach (int zone in new[] { HumanDamage.ProximityZone, HumanDamage.NeckZone })
        {
            var survivor = new HumanHealth(HumanHitZoneTable.Soldier, HardGuardHealth);
            float dealt = survivor.Apply(zone, ak.DamageFactor, instantDeathDisabled: true);

            Assert.Equal(ak.DamageFactor, dealt, 5);
            Assert.False(survivor.IsDead, "with instant death disabled the head/neck no longer one-shots");
        }
    }

    /// <summary>
    /// A leg is worth less than the trunk, so it must take strictly more rounds. If the game shows
    /// otherwise, the zone is resolving to the trunk and the pose is the thing to look at.
    /// </summary>
    [RequiresGameDataFact]
    public void ALegTakesMoreRoundsThanTheTrunk()
    {
        WeaponType ak = Weapon("WEAPON_ID_AK47");

        Assert.True(
            ShotsToKill(ak, 0, EasyGuardHealth)
                > ShotsToKill(ak, HumanDamage.DefaultZone, EasyGuardHealth),
            "a foot shot must not be as lethal as a chest shot");
    }

    private static int ShotsToKill(WeaponType weapon, int zone, float maximum)
    {
        var health = new HumanHealth(HumanHitZoneTable.Soldier, maximum);
        int shots = 0;
        while (!health.IsDead && shots < 99)
        {
            health.Apply(zone, weapon.DamageFactor);
            shots++;
        }

        return shots;
    }

    private static WeaponType Weapon(string scriptId)
    {
        foreach (WeaponType weapon in RetailWeaponRoster.Value.Weapons)
        {
            if (weapon.ScriptId == scriptId)
            {
                return weapon;
            }
        }

        throw new InvalidOperationException($"{scriptId} is not in the retail roster");
    }
}
