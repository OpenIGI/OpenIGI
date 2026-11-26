using OpenIGI.Core.Graphics;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;
using Xunit;

namespace OpenIGI.Presentation.Tests;

/// <summary>
/// The remastered thermal sensor's presentation: its raise and calibration on the render clock (<see cref="ThermalOptics"/>), a
/// dead body's cooling (<see cref="BodyCooling"/>), the palettes the eyepiece draws (<see cref="ThermalPaletteRamp"/>) and when the
/// sensor is used at all.
/// </summary>
public sealed class ThermalOpticsTests
{
    [Fact]
    public void TheSensorStartsDownAndCalibrated()
    {
        ThermalOptics optics = new();

        Assert.Equal(0f, optics.Amount);
        Assert.Equal(0f, optics.Calibration);
        Assert.False(optics.Display(ThermalPalette.WhiteHot, 1f).IsActive);
    }

    [Fact]
    public void RaisingItCalibratesAndTheCalibrationSettles()
    {
        ThermalOptics optics = new();

        optics.Update(open: true, 0.016f);
        Assert.True(optics.Calibration > 0.9f);
        Assert.True(optics.Amount > 0f);

        for (int frame = 0; frame < 60; frame++)
        {
            optics.Update(open: true, 0.016f);
        }

        Assert.Equal(1f, optics.Amount);
        Assert.Equal(0f, optics.Calibration);
    }

    [Fact]
    public void RaisingItAgainCalibratesAgain()
    {
        ThermalOptics optics = new();
        for (int frame = 0; frame < 60; frame++)
        {
            optics.Update(open: true, 0.016f);
        }

        optics.Update(open: false, 0.016f);
        optics.Update(open: true, 0.016f);

        Assert.True(optics.Calibration > 0.9f);
    }

    [Fact]
    public void APaletteChangeShowsAndFades()
    {
        ThermalOptics optics = new();
        optics.Update(open: true, 0.016f);
        Assert.Equal(0f, optics.PaletteNotice);

        optics.NotePaletteChanged();
        Assert.Equal(1f, optics.PaletteNotice);

        for (int frame = 0; frame < 30; frame++)
        {
            optics.Update(open: true, 0.1f);
        }

        Assert.Equal(0f, optics.PaletteNotice);
    }

    [Fact]
    public void TheLivingAreHotAndTheDeadCoolOverAMinute()
    {
        BodyCooling cooling = new();
        object body = new();

        Assert.Equal(BodyCooling.LivingHeat, cooling.HeatOf(body, dead: false, 10.0));
        Assert.Equal(BodyCooling.LivingHeat, cooling.HeatOf(body, dead: true, 10.0));

        float tenSeconds = cooling.HeatOf(body, dead: true, 20.0);
        float aMinute = cooling.HeatOf(body, dead: true, 10.0 + BodyCooling.CoolingSeconds);

        Assert.InRange(tenSeconds, aMinute, BodyCooling.LivingHeat);
        Assert.InRange(aMinute, BodyCooling.SettledHeat, BodyCooling.SettledHeat + 0.05f);
        Assert.True(cooling.HeatOf(body, dead: true, 10000.0) >= BodyCooling.SettledHeat);
    }

    [Fact]
    public void ABodySeenAliveAgainIsForgotten()
    {
        BodyCooling cooling = new();
        object body = new();
        cooling.HeatOf(body, dead: true, 0.0);
        cooling.HeatOf(body, dead: false, 30.0);

        Assert.Equal(BodyCooling.LivingHeat, cooling.HeatOf(body, dead: true, 30.0));
    }

    [Fact]
    public void WhiteHotRunsFromNearBlackToNearWhiteAndBlackHotTheOtherWay()
    {
        Bgra32 coldWhite = ThermalPaletteRamp.At(ThermalPalette.WhiteHot, 0f);
        Bgra32 hotWhite = ThermalPaletteRamp.At(ThermalPalette.WhiteHot, 1f);
        Bgra32 coldBlack = ThermalPaletteRamp.At(ThermalPalette.BlackHot, 0f);
        Bgra32 hotBlack = ThermalPaletteRamp.At(ThermalPalette.BlackHot, 1f);

        Assert.True(coldWhite.R < 10 && hotWhite.R > 245);
        Assert.True(coldBlack.R > 240 && hotBlack.R < 10);
    }

    [Fact]
    public void TheKeyStepsThroughEveryPaletteAndBack()
    {
        ThermalPalette palette = ThermalPalette.WhiteHot;
        HashSet<ThermalPalette> seen = [];
        foreach (ThermalPalette _ in Enum.GetValues<ThermalPalette>())
        {
            seen.Add(palette);
            palette = ThermalPaletteRamp.Next(palette);
        }

        Assert.Equal(ThermalPalette.WhiteHot, palette);
        Assert.Equal(Enum.GetValues<ThermalPalette>().Length, seen.Count);
    }

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, false)]
    public void TheSensorNeedsTheSettingADeviceThatCapturesTheSceneAndItsFaces(bool chosen, bool sceneCapture, bool faces, bool used) =>
        Assert.Equal(used, RemasteredHudPresentation.UsesRemasteredThermal(chosen, sceneCapture, faces));
}
