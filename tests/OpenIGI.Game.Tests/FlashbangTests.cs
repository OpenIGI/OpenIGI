using OpenIGI.Game.Weapons;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// What a flashbang does, and to whom.
/// </summary>
public sealed class FlashbangTests
{
    [Fact]
    public void TheFlashIsSeenTwentyFiveMetresOut()
    {
        Assert.Equal(25f, Flashbang.VisibleRangeUnits / 4096f);

        Assert.True(Flashbang.IsVisible(0f));
        Assert.True(Flashbang.IsVisible(Flashbang.VisibleRangeUnits - 1f));

        // Strictly less than, so exactly twenty-five metres is unaffected.
        Assert.False(Flashbang.IsVisible(Flashbang.VisibleRangeUnits));
        Assert.False(Flashbang.IsVisible(Flashbang.VisibleRangeUnits + 1f));
    }

    [Fact]
    public void TheVisibleRangeIsIndependentOfTheGrenadesRadius()
    {
        // 25 m is hardcoded and has nothing to do with the explosion radius, so at the default tuning
        // the flash reaches five times further than the blast does.
        float grenadeRadius = ExplosionDamage.ToUnits(ExplosionDamage.DefaultGrenadeRadiusMetres);

        Assert.Equal(20480f, grenadeRadius);
        Assert.True(Flashbang.VisibleRangeUnits > grenadeRadius * 4f);
    }

    [Fact]
    public void TheTwoEffectsAreIndependentAndBothCanApply()
    {
        float radius = ExplosionDamage.ToUnits(ExplosionDamage.DefaultGrenadeRadiusMetres);

        // Right on top of it: both.
        (float visible, float close) = Flashbang.Effect(0f, radius);
        Assert.Equal(Flashbang.VisibleStrength, visible);
        Assert.Equal(Flashbang.CloseStrength, close);

        // Between the radius and 25 m: the first only. That band is most of the flash's reach.
        (visible, close) = Flashbang.Effect(radius + 1f, radius);
        Assert.Equal(Flashbang.VisibleStrength, visible);
        Assert.Equal(0f, close);

        // Past 25 m: neither.
        (visible, close) = Flashbang.Effect(Flashbang.VisibleRangeUnits, radius);
        Assert.Equal(0f, visible);
        Assert.Equal(0f, close);
    }

    [Fact]
    public void TheCloseTestIsWrittenAsASubtractionButAgreesWithAComparison()
    {
        // The original spells it "distance - radius < 0". Worth pinning that the readable form is
        // equivalent for every input that can reach it.
        foreach (float distance in new[] { 0f, 1f, 20479f, 20480f, 20481f, 1e6f })
        {
            Assert.Equal(distance < 20480f, Flashbang.IsClose(distance, 20480f));
        }
    }

    [Fact]
    public void TheMissileTypesAreNotInTheOrderTheStringsSit()
    {
        // The helicopter takes 1, between the two missile kinds - so reading the binary's string order
        // and numbering from zero would get four of the six wrong.
        Assert.Equal(0, (int)MissileType.MissileDirect);
        Assert.Equal(1, (int)MissileType.HindHelicopter);
        Assert.Equal(2, (int)MissileType.MissileBallistic);
        Assert.Equal(3, (int)MissileType.MissileAntiTank);
        Assert.Equal(4, (int)MissileType.MissileKeyboard);
        Assert.Equal(5, (int)MissileType.Bomb);

        // Six members, no gaps.
        Assert.Equal(
            Enumerable.Range(0, 6),
            Enum.GetValues<MissileType>().Select(v => (int)v).Order());
    }

    [Fact]
    public void TheMissileSmokeConstantsAreZeroAndOne()
    {
        Assert.Equal(0, (int)MissileSmoke.None);
        Assert.Equal(1, (int)MissileSmoke.Normal);
    }
}
