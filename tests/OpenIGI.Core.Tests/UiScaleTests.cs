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
}
