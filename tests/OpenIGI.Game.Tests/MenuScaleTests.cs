using OpenIGI.Core.Graphics;
using Xunit;
using OpenIGI.Game.Menus;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The menu's screen mapping and its inverse, which have to agree exactly or the cursor lands
/// somewhere other than what it points at.
/// </summary>
public sealed class MenuScaleTests
{
    [Fact]
    public void AtNativeScaleTheMappingIsTheOriginalsArithmetic()
    {
        // (width >> 1) + x - 320, (height >> 1) + y - 240, with nothing else in it.
        Assert.Equal((512 + 100 - 320, 384 + 50 - 240), MenuLayout.ToScreen(100, 50, 1024, 768));
        Assert.Equal((100, 50), MenuLayout.ToScreen(100, 50, 640, 480));
    }

    [Fact]
    public void TheAuthoredCentreStaysAtTheScreenCentreWhateverTheScale()
    {
        foreach (int scale in new[] { 1, 2, 3, 4 })
        {
            (int x, int y) = MenuLayout.ToScreen(
                UiScale.AuthoredWidth / 2, UiScale.AuthoredHeight / 2, 1920, 1080, scale);

            Assert.Equal(960, x);
            Assert.Equal(540, y);
        }
    }

    [Fact]
    public void TheOffsetFromTheCentreIsMagnified()
    {
        // A point 100 authored pixels right of centre is 200 real pixels right of it at 2x.
        (int x, _) = MenuLayout.ToScreen((UiScale.AuthoredWidth / 2) + 100, 240, 1920, 1080, 2);
        Assert.Equal(960 + 200, x);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ThePointerInverseUndoesTheMapping(int scale)
    {
        // Round-trip: an authored point mapped to the screen and hit-tested back must come home. If
        // these two ever disagreed, clicking a menu item would activate a different one.
        MenuPointer pointer = new();

        foreach ((int ax, int ay) in new[] { (320, 240), (0, 0), (639, 479), (100, 400) })
        {
            (int sx, int sy) = MenuLayout.ToScreen(ax, ay, 1920, 1080, scale);
            pointer.Update(sx, sy, 1920, 1080, buttonDown: false, scale);

            Assert.Equal(ax, pointer.X);
            Assert.Equal(ay, pointer.Y);
        }
    }
}
