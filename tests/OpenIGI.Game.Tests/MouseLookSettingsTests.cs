using OpenIGI.Game.Config;
using OpenIGI.Game.Player;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The look rate, held to the properties that decide whether the mouse is aimable: the profile's slider
/// actually reaches it, the ends of that slider are the ends of the range, and the vertical axis runs the
/// way the profile says.
/// </summary>
public sealed class MouseLookSettingsTests
{
    [Fact]
    public void TheSliderSpansTheWholeRangeAndNothingBeyondIt()
    {
        Assert.Equal(
            MouseLookSettings.MinRadiansPerCount,
            MouseLookSettings.FromSensitivity(0f, invertPitch: false).RadiansPerCount,
            6);

        Assert.Equal(
            MouseLookSettings.MaxRadiansPerCount,
            MouseLookSettings.FromSensitivity(1f, invertPitch: false).RadiansPerCount,
            6);

        // Out-of-range values are clamped rather than extrapolated: a corrupt save must not produce a
        // view that spins a full turn per count.
        Assert.Equal(
            MouseLookSettings.MinRadiansPerCount,
            MouseLookSettings.FromSensitivity(-5f, invertPitch: false).RadiansPerCount,
            6);

        Assert.Equal(
            MouseLookSettings.MaxRadiansPerCount,
            MouseLookSettings.FromSensitivity(9f, invertPitch: false).RadiansPerCount,
            6);
    }

    [Fact]
    public void TheTopOfTheSliderIsTheRateTheLookUsedToBeHardcodedTo()
    {
        // The change may only ADD reach below the old value; anyone who liked the previous feel must be
        // able to get it back by pushing the slider to the top.
        Assert.Equal(0.0035f, MouseLookSettings.MaxRadiansPerCount, 6);
    }

    [Fact]
    public void EqualSliderStepsAreEqualRatios()
    {
        // Geometric, not linear: a linear slider spends most of its travel on rates nobody can aim with
        // and leaves no resolution at the slow end, which is where the aiming happens.
        float a = MouseLookSettings.FromSensitivity(0.2f, invertPitch: false).RadiansPerCount;
        float b = MouseLookSettings.FromSensitivity(0.5f, invertPitch: false).RadiansPerCount;
        float c = MouseLookSettings.FromSensitivity(0.8f, invertPitch: false).RadiansPerCount;

        Assert.Equal(b / a, c / b, 4);
    }

    [Fact]
    public void TheRetailDefaultLandsWhereAModernShooterSits()
    {
        float rate = MouseLookSettings.Default.RadiansPerCount;

        // Counts for a full turn, which is the number that decides whether the mouse is aimable. At 800
        // DPI this band is roughly 25 to 45 cm of desk -- where shooters have sat for two decades. The
        // old hardcoded rate was about 1795 counts, or 5.7 cm, which is what made fine aim impossible.
        double countsPerTurn = (2.0 * Math.PI) / rate;
        Assert.InRange(countsPerTurn, 8000.0, 14000.0);
        Assert.InRange(MouseLookSettings.Default.TurnDistanceCentimetres(), 25.0, 45.0);

        // The readout the settings screen shows has to be the same claim, just scaled: twice the
        // resolution is half the desk.
        Assert.Equal(
            MouseLookSettings.Default.TurnDistanceCentimetres(800) / 2.0,
            MouseLookSettings.Default.TurnDistanceCentimetres(1600),
            6);
    }

    [Fact]
    public void ThePitchSignFollowsTheProfileAndTheYawNeverDoes()
    {
        MouseLookSettings upright = MouseLookSettings.FromSensitivity(0.4f, invertPitch: false);
        MouseLookSettings inverted = MouseLookSettings.FromSensitivity(0.4f, invertPitch: true);

        // Pitch is ADDED to the body's own, so mouse up (a negative delta) must ask for a positive pitch.
        Assert.True(upright.PitchDelta(-10f) > 0f);
        Assert.True(inverted.PitchDelta(-10f) < 0f);
        Assert.Equal(upright.PitchDelta(-10f), -inverted.PitchDelta(-10f), 6);

        // Inverting the vertical axis must not touch the horizontal one.
        Assert.Equal(upright.YawDelta(10f), inverted.YawDelta(10f), 6);
        Assert.True(upright.YawDelta(10f) > 0f);
    }

    [Fact]
    public void AProfileReachesTheLookAndAMissingOneFallsBackToTheDefault()
    {
        // The regression this guards: MouseSensitivity and InvertMouse round-tripped through the save
        // file for the whole life of the project while the look read a hardcoded constant instead.
        PlayerProfile profile = new() { MouseSensitivity = 0.9f, InvertMouse = true };
        MouseLookSettings settings = MouseLookSettings.FromProfile(profile);

        Assert.Equal(
            MouseLookSettings.FromSensitivity(0.9f, invertPitch: true).RadiansPerCount,
            settings.RadiansPerCount,
            6);
        Assert.True(settings.InvertPitch);
        Assert.True(settings.RadiansPerCount > MouseLookSettings.Default.RadiansPerCount);

        Assert.Equal(MouseLookSettings.Default, MouseLookSettings.FromProfile(null));

        // A fresh profile is the default profile, so it must look exactly like the no-profile fallback.
        Assert.Equal(MouseLookSettings.Default, MouseLookSettings.FromProfile(new PlayerProfile()));
    }
}
