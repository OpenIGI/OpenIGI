using OpenIGI.Game.Weapons;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// What a flashbang does, and to whom.
/// </summary>
public sealed class FlashbangTests
{
    [Fact]
    public void TheFlashUsesTheRetailHundredThousandUnitRange()
    {
        Assert.Equal(100000f, Flashbang.VisibleRangeUnits);

        Assert.True(Flashbang.IsVisible(0f));
        Assert.True(Flashbang.IsVisible(Flashbang.VisibleRangeUnits - 1f));

        // Strictly less than, so exactly twenty-five metres is unaffected.
        Assert.False(Flashbang.IsVisible(Flashbang.VisibleRangeUnits));
        Assert.False(Flashbang.IsVisible(Flashbang.VisibleRangeUnits + 1f));
    }

    [Fact]
    public void TheVisibleRangeIsIndependentOfTheGrenadesRadius()
    {
        // 100000 units is hardcoded and has nothing to do with the weaponconfig blast radius.
        float grenadeRadius = ExplosionDamage.ToUnits(ExplosionDamage.DefaultFlashbangRadiusMetres);

        Assert.Equal(40960f, grenadeRadius);
        Assert.True(Flashbang.VisibleRangeUnits > grenadeRadius * 2f);
    }

    [Fact]
    public void TheTwoEffectsAreIndependentAndBothCanApply()
    {
        float radius = ExplosionDamage.ToUnits(ExplosionDamage.DefaultFlashbangRadiusMetres);

        // Right on top of it: both.
        (float visible, float close) = Flashbang.Effect(0f, radius);
        Assert.Equal(Flashbang.VisibleStrength, visible);
        Assert.Equal(Flashbang.CloseStrength, close);

        // Between the radius and 100000 units: the first only. That band is most of the flash's reach.
        (visible, close) = Flashbang.Effect(radius + 1f, radius);
        Assert.Equal(Flashbang.VisibleStrength, visible);
        Assert.Equal(0f, close);

        // At/past 100000 units: neither.
        (visible, close) = Flashbang.Effect(Flashbang.VisibleRangeUnits, radius);
        Assert.Equal(0f, visible);
        Assert.Equal(0f, close);
    }

    [Fact]
    public void OutsideTheNativeFlashRangeThePlayerGetsNoWhiteoutWhileWorldSmokeStillBelongsToTheEvent()
    {
        float radius = ExplosionDamage.ToUnits(ExplosionDamage.DefaultFlashbangRadiusMetres);
        FlashbangScreenFlash whiteout = new();

        (float visible, float close) = Flashbang.Effect(Flashbang.VisibleRangeUnits + 1f, radius);
        whiteout.Apply(visible, close);

        // DesktopGame resolves the world smoke before this player-only callback. The native callback
        // writes neither whiteout field outside its two distance tests; smoke is not gated on either.
        Assert.False(whiteout.IsActive);
    }

    [Fact]
    public void TheWhiteoutKeepsTheTwoNativeFieldsAndFadesThemIndependently()
    {
        FlashbangScreenFlash flash = new();
        flash.Apply(Flashbang.VisibleStrength, Flashbang.CloseStrength);

        Assert.Equal(Flashbang.VisibleStrength, flash.VisibleStrength);
        Assert.Equal(Flashbang.CloseStrength, flash.CloseStrength);
        Assert.Equal(Flashbang.CloseStrength, flash.Strength);

        for (int tick = 0; tick < 45; tick++)
        {
            flash.Tick();
        }

        Assert.Equal(
            Flashbang.VisibleStrength - (45 * FlashbangScreenFlash.VisibleFadePerTick),
            flash.VisibleStrength,
            5);
        Assert.Equal(
            Flashbang.CloseStrength - (45 * FlashbangScreenFlash.CloseFadeAboveOnePerTick),
            flash.CloseStrength,
            5);
        Assert.Equal(flash.CloseStrength, flash.Strength, 5);

        // A weaker event must not shorten a stronger field that is already on screen.
        flash.Apply(0.5f, 0f);
        Assert.Equal(Flashbang.VisibleStrength - (45 * FlashbangScreenFlash.VisibleFadePerTick), flash.VisibleStrength, 5);
        Assert.Equal(
            Flashbang.CloseStrength - (45 * FlashbangScreenFlash.CloseFadeAboveOnePerTick),
            flash.CloseStrength,
            5);

        // The close field crosses one after sixty ticks above one, then uses the slower native
        // 0.011111112 subtraction. Both fields are fully gone after 150 ticks from strength 1/2.
        for (int tick = 0; tick < 105; tick++)
        {
            flash.Tick();
        }

        Assert.False(flash.IsActive);
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
