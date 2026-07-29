using OpenIGI.Game.Weapons;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// How a round's impacts become decals.
/// </summary>
public sealed class BulletImpactTests
{
    [Fact]
    public void EveryImpactButTheLastIsAnEntry()
    {
        // The round demonstrably carried on past them, so there is nothing to decide.
        for (int i = 0; i < 4; i++)
        {
            Assert.True(BulletImpact.ClassifyEntry(i, impactCount: 5, hasDeflected: false, normalDotTravel: 1f));
        }
    }

    [Fact]
    public void TheLastImpactIsDecidedByTheSurfaceNormal()
    {
        // A normal facing the same way as the travel means the round was on its way out.
        Assert.False(BulletImpact.ClassifyEntry(4, impactCount: 5, hasDeflected: false, normalDotTravel: 1f));
        Assert.True(BulletImpact.ClassifyEntry(4, impactCount: 5, hasDeflected: false, normalDotTravel: -1f));

        // The test is "> 0", so exactly perpendicular counts as an entry.
        Assert.True(BulletImpact.ClassifyEntry(4, impactCount: 5, hasDeflected: false, normalDotTravel: 0f));
    }

    [Fact]
    public void ADeflectedRoundNeverLeavesAnExitMark()
    {
        // Checked first, so it overrides both of the other cases however many surfaces followed.
        Assert.True(BulletImpact.ClassifyEntry(4, impactCount: 5, hasDeflected: true, normalDotTravel: 1f));
        Assert.True(BulletImpact.ClassifyEntry(0, impactCount: 1, hasDeflected: true, normalDotTravel: 1f));
    }

    [Fact]
    public void ASingleImpactIsJustTheNormalTest()
    {
        // With one impact, index 0 is also the last, so it falls straight through to the sign test.
        Assert.False(BulletImpact.ClassifyEntry(0, impactCount: 1, hasDeflected: false, normalDotTravel: 0.5f));
        Assert.True(BulletImpact.ClassifyEntry(0, impactCount: 1, hasDeflected: false, normalDotTravel: -0.5f));
    }

    [Fact]
    public void AnImpactCarriesTheMaterialRatherThanTheMark()
    {
        // The bullet does not choose what the mark looks like: the material's own task type does, which
        // is why a round through glass and into brick leaves two different marks without the shot
        // knowing about either.
        BulletImpact glass = new(MaterialTypeId: 7, Surface: 100, IsEntry: true);
        BulletImpact brick = new(MaterialTypeId: 12, Surface: 200, IsEntry: false);

        Assert.NotEqual(glass.MaterialTypeId, brick.MaterialTypeId);
        Assert.True(glass.IsEntry);
        Assert.False(brick.IsEntry);
    }

    [Fact]
    public void NoMoreThanTenImpactsAreEverRecorded()
    {
        // Impacts past the tenth are resolved - they still stop or penetrate the round - but leave no
        // mark, so a round through a lot of thin cover stops marking before it stops travelling.
        Assert.Equal(10, GunShotBallistics.MaximumRecordedImpacts);
    }
}
