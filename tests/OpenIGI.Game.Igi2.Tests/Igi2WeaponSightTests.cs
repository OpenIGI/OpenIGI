using OpenIGI.Game.Igi2.Config;
using OpenIGI.Game.Igi2.Hud;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The weapon's precision and profile settings produce visible, centred sight geometry.</summary>
public sealed class Igi2WeaponSightTests
{
    /// <summary>A new profile must have a visible crosshair before a configuration file is loaded.</summary>
    [Fact]
    public void Default_profile_has_the_retail_orange_sight()
    {
        Igi2PlayerProfile profile = new();
        Assert.Equal(1f, profile.CrossHairAlpha);
        Assert.Equal((1f, 0.25f, 0f), profile.CrossHairColour);
    }

    /// <summary>Every authored sight type produces finite geometry without a frame allocation.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void Weapon_sights_have_visible_segments(int sight)
    {
        Span<Igi2SightLine> lines = stackalloc Igi2SightLine[Igi2WeaponSight.MaximumLines];
        int count = Igi2WeaponSight.Write(lines, sight, 0.5f, 1f, 1280, 720, thick: false);
        Assert.InRange(count, 4, lines.Length);
        foreach (Igi2SightLine line in lines[..count])
        {
            Assert.True(float.IsFinite(line.X1) && float.IsFinite(line.Y1) && float.IsFinite(line.X2) && float.IsFinite(line.Y2));
            Assert.True(line.X1 != line.X2 || line.Y1 != line.Y2);
        }
    }

    /// <summary>A less accurate shot spreads the sight; zooming magnifies the same angular spread.</summary>
    [Fact]
    public void Precision_and_zoom_change_the_sights_radius()
    {
        Span<Igi2SightLine> lines = stackalloc Igi2SightLine[Igi2WeaponSight.MaximumLines];
        Igi2WeaponSight.Write(lines, 4, 0.5f, 1f, 1280, 720, thick: false);
        float radius = lines[0].X1 - 641f;
        Igi2WeaponSight.Write(lines, 4, 1f, 1f, 1280, 720, thick: false);
        Assert.Equal(radius * 2f, lines[0].X1 - 641f, 3);
        Igi2WeaponSight.Write(lines, 4, 0.5f, 0.5f, 1280, 720, thick: false);
        Assert.Equal(radius * 2f, lines[0].X1 - 641f, 3);
    }
}
