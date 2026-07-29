using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Shooting a human: whether the round lands, which zone it lands in, and what that costs them.
/// </summary>
public sealed class HumanHitTests
{
    private static readonly Vector3d Feet = new(0, 0, 0);
    private static readonly Vector3f FacingNorth = new(0f, 1f, 0f);

    [Fact]
    public void ARoundThroughTheChestLands()
    {
        // Fired from in front, at chest height, straight along the facing.
        Vector3d origin = new(0, -10000, 4000);
        Vector3d end = new(0, 10000, 4000);

        Assert.True(HumanHitTest.TryCast(origin, end, Feet, FacingNorth, pose: null, out HumanHit hit));

        // It enters at the near wall of the cylinder, not at the centre.
        Assert.Equal(-HumanHitTest.BodyRadiusInUnits, hit.Position.Y, 1);
        Assert.True(hit.Distance > 0.0);
    }

    [Fact]
    public void ARoundWideOfTheBodyMisses()
    {
        double wide = HumanHitTest.BodyRadiusInUnits * 2.0;
        Vector3d origin = new(wide, -10000, 4000);
        Vector3d end = new(wide, 10000, 4000);

        Assert.False(HumanHitTest.TryCast(origin, end, Feet, FacingNorth, pose: null, out _));
    }

    [Fact]
    public void ARoundOverTheHeadMisses()
    {
        double above = HumanHitTest.BodyHeightInUnits + 1000.0;
        Vector3d origin = new(0, -10000, above);
        Vector3d end = new(0, 10000, above);

        Assert.False(HumanHitTest.TryCast(origin, end, Feet, FacingNorth, pose: null, out _));
    }

    [Fact]
    public void ARoundThatStopsShortMisses()
    {
        // The segment counts, not the infinite ray -- the same rule every other cast in the engine has.
        Vector3d origin = new(0, -10000, 4000);
        Vector3d end = new(0, -9000, 4000);

        Assert.False(HumanHitTest.TryCast(origin, end, Feet, FacingNorth, pose: null, out _));
    }

    [Fact]
    public void AShotFromTheFrontTakesTheFrontZone()
    {
        // The human faces +Y, so a round that hits them in the FACE travels -Y. What the refinement
        // measures is the reversed travel direction -- where the round came FROM.
        Vector3d origin = new(0, 10000, 4000);
        Vector3d end = new(0, -10000, 4000);

        Assert.True(HumanHitTest.TryCast(origin, end, Feet, FacingNorth, pose: null, out HumanHit hit));
        Assert.Equal(HumanDamage.FrontZone, hit.Zone);
    }

    [Fact]
    public void AShotFromBehindTakesTheRearZone()
    {
        // Travelling the same way the human faces means hitting their back.
        Vector3d origin = new(0, -10000, 4000);
        Vector3d end = new(0, 10000, 4000);

        Assert.True(HumanHitTest.TryCast(origin, end, Feet, FacingNorth, pose: null, out HumanHit hit));
        Assert.Equal(HumanDamage.RearZone, hit.Zone);
    }

    [Fact]
    public void AShotFromTheSideKeepsTheDefaultZone()
    {
        // The refinement compares magnitudes, so a round arriving square on the flank is not refined.
        Vector3d origin = new(-10000, 0, 4000);
        Vector3d end = new(10000, 0, 4000);

        Assert.True(HumanHitTest.TryCast(origin, end, Feet, FacingNorth, pose: null, out HumanHit hit));
        Assert.Equal(HumanDamage.DefaultZone, hit.Zone);
    }

    [Fact]
    public void AHitOnTheChestCostsTheZonesMultiplier()
    {
        HumanHealth health = new(HumanHitZoneTable.Soldier);
        float dealt = health.Apply(HumanDamage.FrontZone, damageFactor: 0.25f);

        Assert.Equal(HumanHitZoneTable.Soldier[HumanDamage.FrontZone].Multiplier * 0.25f, dealt, 5);
        Assert.False(health.IsDead);
        Assert.Equal(1, health.Hits);
    }

    [Fact]
    public void AHeadShotKillsWithAnyWeaponAtAll()
    {
        // Zone 15 carries a multiplier of 100, so even the knife's 0.1 damage factor reaches 1.0 in one.
        HumanHealth health = new(HumanHitZoneTable.Soldier);
        health.Apply(HumanDamage.ProximityZone, damageFactor: 0.1f);

        Assert.True(health.IsDead);
        Assert.Equal(0f, health.RemainingFraction);
    }

    [Fact]
    public void EnoughBodyShotsKill()
    {
        HumanHealth health = new(HumanHitZoneTable.Soldier);
        float multiplier = HumanHitZoneTable.Soldier[HumanDamage.FrontZone].Multiplier;
        int expected = HumanHitZoneTable.HitsToKill(0.25f, multiplier);

        for (int i = 0; i < expected - 1; i++)
        {
            health.Apply(HumanDamage.FrontZone, 0.25f);
            Assert.False(health.IsDead, $"dead after {i + 1} of {expected} hits.");
        }

        health.Apply(HumanDamage.FrontZone, 0.25f);
        Assert.True(health.IsDead);
    }

    [Fact]
    public void AZoneOutsideTheTableCostsNothing()
    {
        HumanHealth health = new(HumanHitZoneTable.Soldier);
        Assert.Equal(0f, health.Apply(99, 1f));
        Assert.Equal(0f, health.Damage);
    }
}
