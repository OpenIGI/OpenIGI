using OpenIGI.Game.Igi2.Weapons;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary><c>MuzzleFlash_RunHandler</c>: a flash fades only once drawn, a third of its strength a tick, and grows as it fades.</summary>
public sealed class Igi2MuzzleFlashTests
{
    [Fact]
    public void A_flash_waits_to_be_drawn_then_lasts_four_ticks()
    {
        Igi2MuzzleFlashes flashes = new();
        flashes.Add(new Igi2MuzzleFlash(0, "flash_smg.tex", "flash_side2.tex", 0.2f));
        flashes.Run();
        Assert.Equal(1f, Assert.Single(flashes.Active).Intensity);

        var drawn = Assert.Single(flashes.Present(0)!);
        Assert.Equal("LOCAL:common/textures/flash_smg.tex", drawn.FrontTexture);
        Assert.Equal("LOCAL:common/textures/flash_side2.tex", drawn.SideTexture);
        Assert.Null(flashes.Present(1));

        int ticks = 0;
        while (flashes.Active.Count > 0)
        {
            flashes.Run();
            ticks++;
        }

        Assert.Equal(4, ticks);
    }

    [Fact]
    public void A_flash_without_a_front_sprite_draws_nothing()
    {
        Igi2MuzzleFlashes flashes = new();
        flashes.Add(new Igi2MuzzleFlash(0, null, "flash_side.tex", 0.2f));
        Assert.Null(flashes.Present(0));
        Assert.True(Assert.Single(flashes.Active).IsDrawn);
    }
}

/// <summary><c>GunCasing_RunHandler</c>: a casing flies nineteen ticks, falling, then goes.</summary>
public sealed class Igi2GunCasingTests
{
    [Fact]
    public void A_casing_falls_for_nineteen_ticks()
    {
        Igi2GunCasings casings = new(static _ => null);
        casings.Eject(new Igi2CasingEjection(0, "1123_01_1", 0f, true, (0, 0, 0)), "gun", default, Core.Numerics.Matrix3f.Identity, default);
        Igi2GunCasing casing = Assert.Single(casings.Flying);
        Assert.Equal(Igi2GunCasings.EjectionSpeed, casing.Speed.X, 3);
        Assert.Equal(Igi2GunCasings.EjectionSpeed, casing.Speed.Y, 3);

        int ticks = 0;
        while (casings.Flying.Count > 0)
        {
            casings.Run();
            ticks++;
        }

        Assert.Equal(Igi2GunCasing.Lifetime + 1, ticks);
    }
}
