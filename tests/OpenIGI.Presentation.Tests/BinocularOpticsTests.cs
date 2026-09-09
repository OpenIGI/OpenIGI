using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;
using Xunit;

namespace OpenIGI.Presentation.Tests;

/// <summary>
/// The binocular glass fade (<see cref="BinocularOptics"/>) must rise on the render clock when the
/// device is raised, fall back faster when it is lowered, and clamp -- it drives a resolve-time display
/// transform, so an out-of-range amount would black or wash the whole frame.
/// </summary>
public sealed class BinocularOpticsTests
{
    [Fact]
    public void StartsClosed()
    {
        BinocularOptics optics = new();
        Assert.Equal(0f, optics.Amount);
        Assert.Equal(0f, optics.Seconds);
        Assert.False(optics.Display.IsActive);
    }

    [Fact]
    public void RaisesToFullOverTheOpenWindow()
    {
        BinocularOptics optics = new();

        // Roughly the open window in small steps: it should be fully raised, and never past 1.
        for (int i = 0; i < 8; i++)
        {
            optics.Update(open: true, elapsedSeconds: BinocularOptics.OpenSeconds / 8f);
        }

        Assert.Equal(1f, optics.Amount, 3);
        Assert.True(optics.Display.IsActive);
    }

    [Fact]
    public void AmountNeverExceedsOne()
    {
        BinocularOptics optics = new();
        for (int i = 0; i < 20; i++)
        {
            optics.Update(open: true, elapsedSeconds: 0.1f);
        }

        Assert.Equal(1f, optics.Amount, 3);
    }

    [Fact]
    public void FallsBackToClosedAndFasterThanItRose()
    {
        BinocularOptics openOptics = new();
        BinocularOptics closeOptics = new();

        // One equal step: the close moves further than the open, since the fall is quicker.
        openOptics.Update(open: true, elapsedSeconds: 0.05f);
        float raised = openOptics.Amount;

        closeOptics.Update(open: true, elapsedSeconds: 0.2f); // partway up
        float before = closeOptics.Amount;
        closeOptics.Update(open: false, elapsedSeconds: 0.05f);
        float dropped = before - closeOptics.Amount;

        Assert.True(dropped > raised, "the close should fall faster than the open rises.");

        // Held closed it settles at exactly zero rather than going negative.
        for (int i = 0; i < 20; i++)
        {
            closeOptics.Update(open: false, elapsedSeconds: 0.1f);
        }

        Assert.Equal(0f, closeOptics.Amount, 3);
        Assert.False(closeOptics.Display.IsActive);
    }

    [Fact]
    public void SecondsRiseMonotonicallyAndClampEachStep()
    {
        BinocularOptics optics = new();

        // A hitch is folded in at the per-step cap rather than jumping the clock forward five seconds.
        optics.Update(open: false, elapsedSeconds: 5f);
        Assert.Equal(0.1f, optics.Seconds, 4);

        float previous = optics.Seconds;
        optics.Update(open: true, elapsedSeconds: 0.03f);
        Assert.True(optics.Seconds > previous);
        Assert.Equal(0.13f, optics.Seconds, 4);
    }

    [Fact]
    public void ResetReturnsToClosed()
    {
        BinocularOptics optics = new();
        optics.Update(open: true, elapsedSeconds: 0.2f);
        optics.Reset();

        Assert.Equal(0f, optics.Amount);
        Assert.Equal(0f, optics.Seconds);
    }
}
