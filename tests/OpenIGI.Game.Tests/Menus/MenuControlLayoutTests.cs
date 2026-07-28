using OpenIGI.Game.Menus;
using Xunit;

namespace OpenIGI.Game.Tests.Menus;

/// <summary>
/// How a slider and a switch size and place their parts.
/// </summary>
/// <remarks>
/// From <c>SlideBar_Construct</c> (<c>0x41F5A0</c>), <c>SlideBar_Prepare</c> (<c>0x41F740</c>),
/// <c>SlideBar_Render</c> (<c>0x41FCE0</c>), <c>ToggleBox_Construct</c> (<c>0x41DDD0</c>) and
/// <c>MenuWindow_DrawBox</c> (<c>0x424980</c>). None of it needs a device, which is why it lives on
/// the tasks rather than in the renderer — the update hit-tests the same rectangles the render draws.
/// </remarks>
public sealed class MenuControlLayoutTests
{
    [Fact]
    public void ASliderStartsWhereAMenuEntryDoes()
    {
        SlideBarTask slider = new();

        // The same (300, 230) and centring MenuText's constructor sets, so a slider drops into a
        // column of entries without the script positioning it.
        Assert.Equal(300, slider.LayoutX);
        Assert.Equal(230, slider.LayoutY);
        Assert.True(slider.CentreHorizontally);
        Assert.True(slider.CentreVertically);
        Assert.Equal(SlideBarTask.DefaultSize, slider.Size);
        Assert.True(slider.Horizontal);
        Assert.Equal(SlideBarTask.DefaultStep, slider.Step);
    }

    [Fact]
    public void ASlidersSpriteNamesAllComeFromTheOneParameter()
    {
        SlideBarTask slider = new() { SpriteResource = "slide" };
        slider.PrepareSubtree();

        Assert.Equal("slide_1.spr", slider.LeftButton.SpriteResource);
        Assert.Equal("slide_2.spr", slider.RightButton.SpriteResource);
        Assert.Equal("slide_3.spr", slider.CarretSprite);
    }

    [Fact]
    public void AHorizontalSliderIsAsWideAsItsArrowsPlusItsBarPlusSix()
    {
        SlideBarTask slider = Sized(horizontal: true);
        slider.MeasureFromParts();

        Assert.Equal(9 + 100 + 11 + 6, slider.LayoutWidth);

        // Across the bar: four pixels of padding plus the tallest of the three sprites.
        Assert.Equal(14 + 4, slider.LayoutHeight);
    }

    [Fact]
    public void AVerticalSliderSwapsTheAxes()
    {
        SlideBarTask slider = Sized(horizontal: false);
        slider.MeasureFromParts();

        Assert.Equal(10 + 100 + 12 + 6, slider.LayoutHeight);

        // The widest arrow is 11 and the carret is only 6 wide, so the arrows decide the across size.
        Assert.Equal(11 + 4, slider.LayoutWidth);
    }

    [Fact]
    public void ASliderWithNoCarretIgnoresItsArrowsAndCollapsesToPadding()
    {
        SlideBarTask slider = Sized(horizontal: true);
        slider.CarretWidth = 0;
        slider.CarretHeight = 0;

        slider.MeasureFromParts();

        // A quirk, not a tidy-up: the original only consults the arrows inside the branch guarded by
        // the carret sprite being present, so without one the across-axis size is bare padding.
        Assert.Equal(4, slider.LayoutHeight);
        Assert.Equal(9 + 100 + 11 + 6, slider.LayoutWidth);
    }

    [Fact]
    public void TheCarretTravelsTheBarLessItsOwnWidth()
    {
        SlideBarTask slider = Sized(horizontal: true);
        slider.LayoutX = 100;
        slider.LayoutY = 50;

        slider.Value = 0.0;
        (int trackX, int trackY, int carretX, _) = slider.PlaceParts();

        // The track starts after the left arrow, and both arrows sit two pixels in from the corner.
        Assert.Equal(102, slider.LeftButton.LayoutX);
        Assert.Equal(52, slider.LeftButton.LayoutY);
        Assert.Equal(112, trackX);
        Assert.Equal(52, trackY);
        Assert.Equal(112, carretX);

        // The right arrow sits one pixel past the far end of the bar.
        Assert.Equal(213, slider.RightButton.LayoutX);

        slider.Value = 1.0;
        (_, _, carretX, _) = slider.PlaceParts();
        Assert.Equal(112 + 100 - 6, carretX);
    }

    [Fact]
    public void ASwitchIsAFixedFiftyByFifteenAndStartsOn()
    {
        ToggleBoxTask toggle = new();

        // It never measures its words — the size is written by the constructor.
        Assert.Equal(50, toggle.LayoutWidth);
        Assert.Equal(15, toggle.LayoutHeight);
        Assert.True(toggle.CentreHorizontally);

        // A screen whose Get Data Script is empty therefore shows Yes.
        Assert.True(toggle.IsOn);
    }

    [Fact]
    public void TheBoxOutlineIsInsetOnTheTopAndLeftAndFlushOnTheOtherTwo()
    {
        (int X, int Y, int Width, int Height)[] edges = MenuLayout.GetBoxOutline(10, 20, 100, 40);

        // The loop runs (11, 21) -> (110, 21) -> (110, 60) -> (11, 60), so the horizontal edges span
        // 99 and the vertical ones 39 — one short of the width and height, on both axes.
        Assert.Equal((11, 21, 99, 1), edges[0]);
        Assert.Equal((110, 21, 1, 39), edges[1]);
        Assert.Equal((11, 60, 99, 1), edges[2]);
        Assert.Equal((11, 21, 1, 39), edges[3]);
    }

    [Fact]
    public void ASlidersArrowsAreHitTestedEvenThoughTheyAreNotChildren()
    {
        SlideBarTask slider = Sized(horizontal: true);

        // They are created with no parent and held in the owner's fields, so a child walk misses
        // them — the hit-test has to visit them explicitly.
        Assert.Equal([slider.LeftButton, slider.RightButton], slider.OwnedWindows);
        Assert.True(slider.LeftButton.IsInteractive);
    }

    private static SlideBarTask Sized(bool horizontal)
    {
        SlideBarTask slider = new()
        {
            Horizontal = horizontal,
            Size = 100,
            CarretWidth = 6,
            CarretHeight = 14,
        };

        slider.LeftButton.LayoutWidth = 9;
        slider.LeftButton.LayoutHeight = 10;
        slider.RightButton.LayoutWidth = 11;
        slider.RightButton.LayoutHeight = 12;
        return slider;
    }
}
