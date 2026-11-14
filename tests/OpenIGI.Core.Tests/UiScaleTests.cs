using OpenIGI.Core.Graphics;
using Xunit;

namespace OpenIGI.Core.Tests;

/// <summary>
/// The one deliberate divergence in the interface: how much the 640×480-authored art is magnified.
/// </summary>
public sealed class UiScaleTests
{
    [Theory]
    [InlineData(640, 480, 1)]
    [InlineData(800, 600, 1)]
    [InlineData(1024, 768, 1)]
    [InlineData(1280, 960, 2)]
    [InlineData(1920, 1080, 2)]
    [InlineData(2560, 1440, 3)]
    [InlineData(3840, 2160, 4)]
    public void TheScaleIsTheSmallerWholeRatio(int width, int height, int expected) =>
        Assert.Equal(expected, UiScale.For(width, height));

    [Fact]
    public void TheAuthoredSizeItselfIsNeverMagnified()
    {
        // At the size the art was drawn for, the divergence must vanish entirely.
        Assert.Equal(UiScale.Native, UiScale.For(UiScale.AuthoredWidth, UiScale.AuthoredHeight));
    }

    [Fact]
    public void AWindowSmallerThanTheAuthoredSizeStaysAtOne()
    {
        // Never below one: a small window has to behave exactly as the original did rather than
        // shrinking the art into mush.
        Assert.Equal(UiScale.Native, UiScale.For(320, 240));
        Assert.Equal(UiScale.Native, UiScale.For(0, 0));
        Assert.Equal(UiScale.Native, UiScale.For(-1, -1));
    }

    [Fact]
    public void AWideDisplayScalesByItsHeight()
    {
        // 3440x1440 allows 5 horizontally and 3 vertically. Taking the larger would run the menu off
        // the top and bottom, so the smaller wins.
        Assert.Equal(3, UiScale.For(3440, 1440));
    }

    [Fact]
    public void AnExplicitScaleIsHonouredAndCapped()
    {
        Assert.Equal(1, UiScale.Resolve(1, 2560, 1440));
        Assert.Equal(2, UiScale.Resolve(2, 640, 480));
        Assert.Equal(UiScale.Maximum, UiScale.Resolve(99, 640, 480));
    }

    [Fact]
    public void AutomaticDerivesFromTheBackBuffer()
    {
        Assert.Equal(UiScale.For(1920, 1080), UiScale.Resolve(UiScale.Automatic, 1920, 1080));
        Assert.Equal(UiScale.For(1920, 1080), UiScale.Resolve(-5, 1920, 1080));
    }

    /// <summary>
    /// A desktop tool draws one-to-one up to 1440 rows -- where the game's menus are tripled -- grows by quarters toward 4K,
    /// and a chosen scale is used as chosen.
    /// </summary>
    [Theory]
    [InlineData(0f, 900f, 1f)]
    [InlineData(0f, 1440f, 1f)]
    [InlineData(0f, 1800f, 1.25f)]
    [InlineData(0f, 2160f, 1.5f)]
    [InlineData(0f, 4320f, 2f)]
    [InlineData(1.25f, 1440f, 1.25f)]
    [InlineData(9f, 1440f, 4f)]
    public void A_desktop_tool_is_sized_for_the_screen(float requested, float height, float expected)
    {
        Assert.Equal(expected, UiScale.Desktop(requested, height));
        Assert.Equal(3, UiScale.Resolve(UiScale.Automatic, 2560, 1440));
    }

    /// <summary>
    /// The menus are drawn at no less than 0.85 of their authored size on a short wide screen, are left alone from 918
    /// lines up, and are never made narrower than the 1440 units a 4:3 screen already gives them.
    /// </summary>
    [Theory]
    [InlineData(1280f, 720f, 0.85f)]
    [InlineData(1366f, 768f, 0.85f)]
    [InlineData(1280f, 800f, 0.85f)]
    [InlineData(1600f, 900f, 0.85f)]
    [InlineData(1920f, 1080f, 1f)]
    [InlineData(2560f, 1440f, 1.3333f)]
    [InlineData(1024f, 768f, 0.7111f)]
    [InlineData(1152f, 720f, 0.8f)]
    public void The_menus_stay_readable_on_a_small_screen(float width, float height, float effective)
    {
        float boost = UiScale.MenuBoost(width, height);

        Assert.True(boost >= 1f);
        Assert.Equal(effective, boost * height / 1080f, 3);
        Assert.True(width / (boost * height / 1080f) >= UiScale.MenuNarrowestWidth - 0.5f || boost == 1f);
    }

    /// <summary>A screen is as small as its owner sees it: a phone's three pixels to one do not make it a large display.</summary>
    [Theory]
    [InlineData(1920f, 1080f, 1.25f, 1.0625f)] // a laptop at 125 percent
    [InlineData(3840f, 2160f, 1.5f, 2f)]     // a large display is left alone
    public void The_menus_are_sized_for_the_screen_as_it_is_seen(float width, float height, float density, float effective)
    {
        float boost = UiScale.MenuBoost(width, height, density);

        Assert.True(boost >= 1f);
        Assert.Equal(effective, boost * height / 1080f, 3);
        Assert.True(width / (boost * height / 1080f) >= UiScale.MenuNarrowestWidth - 0.5f || boost == 1f);
    }

    /// <summary>A phone and a small window take the compact pages, authored in the pixels the person sees.</summary>
    [Theory]
    [InlineData(2532f, 1170f, 3f)]  // a phone on its side, 844 by 390 as seen
    [InlineData(844f, 390f, 1f)]    // the same screen at one pixel to one
    [InlineData(800f, 600f, 1f)]    // a small window
    [InlineData(2001f, 1125f, 3f)]  // a smaller phone, 667 by 375
    public void A_small_screen_is_laid_out_in_the_pixels_it_is_seen_in(float width, float height, float density)
    {
        Assert.True(UiScale.MenuIsCompact(width, height, density));
        Assert.Equal(density, UiScale.MenuBoost(width, height, density) * height / 1080f, 3);
    }

    [Theory]
    [InlineData(1024f, 768f, 1f)]
    [InlineData(1366f, 768f, 1f)]
    [InlineData(2048f, 1536f, 2f)]  // a tablet
    public void A_laptop_and_a_tablet_keep_the_full_pages(float width, float height, float density) =>
        Assert.False(UiScale.MenuIsCompact(width, height, density));

    [Fact]
    public void A_window_with_no_size_asks_for_no_enlargement()
    {
        Assert.Equal(1f, UiScale.MenuBoost(0f, 0f));
        Assert.Equal(1f, UiScale.MenuBoost(1280f, 0f));
    }
}
