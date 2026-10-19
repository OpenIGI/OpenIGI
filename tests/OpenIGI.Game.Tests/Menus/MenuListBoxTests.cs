using Xunit;
using OpenIGI.Game.Menus;

namespace OpenIGI.Game.Tests.Menus;

/// <summary>
/// The horizontal spinner, from <c>ListBox_Construct</c> (<c>0x41E8C0</c>), <c>ListBox_Prepare</c>
/// (<c>0x41EA70</c>), <c>ListBox_Update</c> (<c>0x41EC70</c>) and <c>ListBox_Render</c>
/// (<c>0x41ED70</c>).
/// </summary>
public sealed class MenuListBoxTests
{
    [Fact]
    public void AListBoxCentresItselfBecauseItsFrameOnlyPlacesOneAxis()
    {
        ListBoxTask list = new();

        // The graphics screen's frames are HorizontalFillOut: they assign the horizontal position and
        // leave the vertical one to the child. Without these flags every list box on that screen sits
        // at the top of the display, which is exactly what happened before this was transcribed.
        Assert.True(list.CentreHorizontally);
        Assert.True(list.CentreVertically);
        Assert.Equal(100, list.Width);
    }

    [Fact]
    public void RightStepsForwardAndWrapsThroughZero()
    {
        ListBoxTask list = Filled();

        Step(list, MenuWindowCommand.Right);
        Assert.Equal(1, list.Index);

        Step(list, MenuWindowCommand.Right);
        Step(list, MenuWindowCommand.Right);
        Assert.Equal(0, list.Index);
    }

    [Fact]
    public void LeftStepsBackAndWrapsToTheEnd()
    {
        ListBoxTask list = Filled();

        Step(list, MenuWindowCommand.Left);

        Assert.Equal(2, list.Index);
    }

    [Fact]
    public void TheScriptReadsTheItemsIdRatherThanItsPosition()
    {
        ListBoxTask list = Filled();

        Step(list, MenuWindowCommand.Right);

        // Config_FillScreenResolutionListBox(ListBox_951.nActiveID) is passed the device's id.
        Assert.Equal(1, list.Index);
        Assert.Equal(70, list.ActiveId);
    }

    [Fact]
    public void AListBoxClearsItsPendingCommandWhereASliderDoesNot()
    {
        ListBoxTask list = Filled();

        list.PendingCommand = MenuWindowCommand.Right;
        list.ApplyPendingCommand();
        list.ApplyPendingCommand();

        // One press, one step — the command does not survive to be applied again.
        Assert.Equal(1, list.Index);
        Assert.Equal(MenuWindowCommand.None, list.PendingCommand);
    }

    [Fact]
    public void AnEmptyListIgnoresEveryKey()
    {
        ListBoxTask list = new();

        Step(list, MenuWindowCommand.Right);
        Step(list, MenuWindowCommand.Left);

        // Which is what a retail screen does today: the Config_* natives that fill these do not exist,
        // so the box draws empty rather than showing something invented.
        Assert.Equal(0, list.Index);
        Assert.Empty(list.Items);
    }

    [Fact]
    public void ALabelThatFitsIsDrawnUntouched()
    {
        ListBoxTask list = new() { Width = 100 };

        Assert.Equal("640x480", list.Fit("640x480", label => label.Length * 8));
    }

    [Fact]
    public void ALabelThatDoesNotFitIsChoppedAndMarked()
    {
        ListBoxTask list = new() { Width = 100 };

        // Eight pixels a character: the budget is Width - 12, so eleven characters survive.
        string fitted = list.Fit("A very long display adapter name", label => label.Length * 8);

        Assert.EndsWith(ListBoxTask.Ellipsis, fitted, StringComparison.Ordinal);
        Assert.Equal("A very long" + ListBoxTask.Ellipsis, fitted);
    }

    [Fact]
    public void AWidthTooSmallForAnythingLeavesJustTheEllipsis()
    {
        ListBoxTask list = new() { Width = 8 };

        // The loop stops at the empty string rather than running off the front.
        Assert.Equal(ListBoxTask.Ellipsis, list.Fit("anything", label => label.Length * 8));
    }

    [Fact]
    public void TheControlIsAsWideAsItsArrowsPlusItsFieldPlusSix()
    {
        ListBoxTask list = new() { Width = 150 };
        list.LeftButton.LayoutWidth = 9;
        list.LeftButton.LayoutHeight = 13;
        list.RightButton.LayoutWidth = 9;
        list.RightButton.LayoutHeight = 11;

        list.MeasureFromParts();

        Assert.Equal(9 + 150 + 9 + 6, list.LayoutWidth);

        // Unlike a slider's, this has no carret to guard the branch, so the arrows always count.
        Assert.Equal(13 + 4, list.LayoutHeight);
    }

    [Fact]
    public void BothArrowSpritesComeFromTheOneParameter()
    {
        ListBoxTask list = new() { SpriteResource = "slide" };
        list.PrepareSubtree();

        Assert.Equal("slide_1.spr", list.LeftButton.SpriteResource);
        Assert.Equal("slide_2.spr", list.RightButton.SpriteResource);
    }

    private static void Step(ListBoxTask list, MenuWindowCommand command)
    {
        list.PendingCommand = command;
        list.ApplyPendingCommand();
    }

    private static ListBoxTask Filled()
    {
        ListBoxTask list = new();
        list.SetItems([new MenuListItem(60, "640x480"), new MenuListItem(70, "800x600"), new MenuListItem(80, "1024x768")]);
        return list;
    }
}
