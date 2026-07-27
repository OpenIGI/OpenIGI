using OpenIGI.Core.Graphics;
using OpenIGI.Game.Menus;
using Xunit;

namespace OpenIGI.Game.Tests.Menus;

public sealed class MenuLayoutTests
{
    [Theory]
    [InlineData(640, 480, 300, 230, 300, 230)]
    [InlineData(1024, 768, 300, 230, 492, 374)]
    [InlineData(800, 600, 320, 240, 400, 300)]
    public void MenusAreCentredAtTheirAuthoredSizeRatherThanScaled(
        int screenWidth,
        int screenHeight,
        int x,
        int y,
        int expectedX,
        int expectedY)
    {
        // (screenWidth >> 1) + x - 320. At a higher resolution the menu stays 640x480-sized in the
        // middle of the screen; scaling it to fill would be a visible divergence.
        Assert.Equal((expectedX, expectedY), MenuLayout.ToScreen(x, y, screenWidth, screenHeight));
    }

    [Fact]
    public void AuthoredCoordinatesAreUnchangedAtTheAuthoredResolution()
    {
        for (int x = 0; x <= MenuLayout.AuthoredWidth; x += 64)
        {
            Assert.Equal(
                (x, 17),
                MenuLayout.ToScreen(x, 17, MenuLayout.AuthoredWidth, MenuLayout.AuthoredHeight));
        }
    }

    [Theory]
    [InlineData(0, 0f, 0f, 0f)]
    [InlineData(1, 128f, 128f, 128f)]
    [InlineData(2, 0f, 128f, 0f)]
    [InlineData(3, 128f, 95f, 0f)]
    public void TheColourTableIsTheOneTheBinaryHolds(int index, float r, float g, float b) =>
        Assert.Equal((r, g, b), MenuLayout.GetColour(index, selected: false));

    [Fact]
    public void OnlyTheLowFourBitsOfTheColourIndexAreUsed()
    {
        // The original masks with 0xF; the upper bits carry a separate half-size flag.
        Assert.Equal(MenuLayout.GetColour(1, false), MenuLayout.GetColour(0x11, false));
        Assert.Equal(MenuLayout.GetColour(3, false), MenuLayout.GetColour(0xF3 & 0xF, false));
    }

    [Fact]
    public void SelectionBrightensTowardTheMiddleRatherThanScalingUp()
    {
        // c * 0.5 + 64. White is already at the midpoint and does not move; orange's blue rises from
        // 0 to 64 while its red stays put.
        Assert.Equal((128f, 128f, 128f), MenuLayout.GetColour(1, selected: true));
        Assert.Equal((128f, 111.5f, 64f), MenuLayout.GetColour(3, selected: true));
        Assert.Equal((64f, 64f, 64f), MenuLayout.GetColour(0, selected: true));
    }

    [Fact]
    public void FullIntensityIsOneHundredAndTwentyEightNotTwoHundredAndFiftyFive()
    {
        Assert.Equal(Bgra32.White, MenuLayout.ToPixel((128f, 128f, 128f)));
        Assert.Equal(Bgra32.FromRgb(0, 255, 0), MenuLayout.ToPixel((0f, 128f, 0f)));
        Assert.Equal(Bgra32.Black, MenuLayout.ToPixel((0f, 0f, 0f)));
    }

    [Fact]
    public void ColoursAboveFullIntensitySaturate()
    {
        Assert.Equal(Bgra32.White, MenuLayout.ToPixel((200f, 200f, 200f)));
    }

    [Fact]
    public void FramePieceNamesMatchTheArchive()
    {
        // menusystem.res holds win1_1.spr .. win1_8.spr; the frame loads exactly eight.
        Assert.Equal(8, MenuLayout.FramePieceCount);
        Assert.Equal("win1_1.spr", MenuLayout.GetFramePieceName("win1", 1));
        Assert.Equal("win2_8.spr", MenuLayout.GetFramePieceName("win2", 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => MenuLayout.GetFramePieceName("win1", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => MenuLayout.GetFramePieceName("win1", 9));
    }
}
