using OpenIGI.Game.Menus;
using Xunit;

namespace OpenIGI.Game.Tests.Menus;

/// <summary>
/// The slider's and the switch's value models, transcribed from <c>SlideBar_Update</c>
/// (<c>0x41FA10</c>) and <c>ToggleBox_Update</c> (<c>0x41E020</c>).
/// </summary>
/// <remarks>
/// These need no retail data: they exercise arithmetic and state that belongs to the control itself,
/// so the numbers below are the original's formulas rather than observations.
/// </remarks>
public sealed class MenuControlTests
{
    [Fact]
    public void AContinuousSliderStepsByTwentiethsAndStopsAtBothEnds()
    {
        SlideBarTask slider = new() { Value = 0.0 };

        // The constructor's 0x3D4CCCCD is 0.05f, so twenty presses cross the bar.
        for (int i = 0; i < 25; i++)
        {
            slider.PendingCommand = MenuWindowCommand.Right;
            slider.ApplyPendingCommand();
        }

        Assert.Equal(1.0, slider.Value);

        for (int i = 0; i < 25; i++)
        {
            slider.PendingCommand = MenuWindowCommand.Left;
            slider.ApplyPendingCommand();
        }

        Assert.Equal(0.0, slider.Value);
    }

    [Fact]
    public void ADiscreteSliderStepsBetweenItsStopsAndSnapsToThem()
    {
        SlideBarTask slider = new() { NumOptions = 5, Value = 0.0 };

        slider.PendingCommand = MenuWindowCommand.Right;
        slider.ApplyPendingCommand();

        // Five stops means a step of 1/(5-1), and the snap leaves the value exactly on one.
        Assert.Equal(0.25, slider.Value, 10);
        Assert.Equal(1, slider.Position);
    }

    [Fact]
    public void ASlidersIndexAndItsPositionAreNotExactInverses()
    {
        SlideBarTask slider = new() { NumOptions = 4 };

        // SetIndex divides by n and GetIndex multiplies by n - 1. The asymmetry is the original's,
        // and it survives the round trip only because both round to the nearest stop.
        slider.SetIndex(2);
        Assert.Equal(0.625, slider.Value, 10);
        Assert.Equal(2, slider.GetIndex());

        slider.SetIndex(0);
        Assert.Equal(0.125, slider.Value, 10);
        Assert.Equal(0, slider.GetIndex());
    }

    [Fact]
    public void ASliderIgnoresACommandThatWouldPushItPastAnEnd()
    {
        SlideBarTask slider = new() { Value = 1.0 };

        // The command is never cleared by the control, so this is what stops a still-posted right
        // press from doing anything on later ticks.
        slider.PendingCommand = MenuWindowCommand.Right;
        slider.ApplyPendingCommand();
        slider.ApplyPendingCommand();

        Assert.Equal(1.0, slider.Value);
    }

    [Fact]
    public void LeftTurnsASwitchOnAndRightTurnsItOff()
    {
        ToggleBoxTask toggle = new() { IsOn = false };

        // Left is the "on" half — the control picks between two labels rather than flipping.
        toggle.PendingCommand = MenuWindowCommand.Left;
        toggle.ApplyPendingCommand();
        Assert.True(toggle.IsOn);

        toggle.PendingCommand = MenuWindowCommand.Left;
        toggle.ApplyPendingCommand();
        Assert.True(toggle.IsOn);

        toggle.PendingCommand = MenuWindowCommand.Right;
        toggle.ApplyPendingCommand();
        Assert.False(toggle.IsOn);
    }

    [Fact]
    public void ASwitchSplitsItsWidthWithTheOddPixelGoingRight()
    {
        ToggleBoxTask toggle = new() { LayoutX = 100, LayoutWidth = 51 };

        Assert.Equal(25, toggle.OnHalfWidth);
        Assert.Equal(26, toggle.OffHalfWidth);
        Assert.Equal(125, toggle.OffHalfX);
    }
}
