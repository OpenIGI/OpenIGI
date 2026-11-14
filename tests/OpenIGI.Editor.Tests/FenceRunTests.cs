using OpenIGI.Editor.Viewport;
using Xunit;

namespace OpenIGI.Editor.Tests;

/// <summary>A fence carried on toward a point: panels a length apart along the line, each turned so its long side follows it.</summary>
public sealed class FenceRunTests
{
    /// <summary>A 4 m panel long on its X run 10 m north-east: three panels, 4 m apart, turned to the heading.</summary>
    [Fact]
    public void Panels_follow_the_line_a_length_apart()
    {
        double d = 10 / Math.Sqrt(2);
        IReadOnlyList<FencePanel> panels = FenceRun.Layout(0, 0, 2, 0.1, d, d);
        Assert.Equal(3, panels.Count);
        Assert.All(panels, static p => Assert.Equal(Math.PI / 4, p.Gamma, 9));
        Assert.Equal(4.0, Math.Sqrt((panels[1].X * panels[1].X) + (panels[1].Y * panels[1].Y)), 9);
        Assert.Equal(8.0, Math.Sqrt((panels[2].X * panels[2].X) + (panels[2].Y * panels[2].Y)), 9);
    }

    /// <summary>A panel long on its Y is turned a quarter less, so its Y, not its X, follows the line.</summary>
    [Fact]
    public void A_panel_long_on_its_y_turns_its_y_along_the_line()
    {
        IReadOnlyList<FencePanel> panels = FenceRun.Layout(0, 0, 0.1, 2, 10, 0);
        Assert.Equal(-Math.PI / 2, panels[0].Gamma, 9);

        // Its Y after the turn, (-sin, cos), lies along east.
        Assert.Equal(1.0, -Math.Sin(panels[0].Gamma), 9);
        Assert.Equal(0.0, Math.Cos(panels[0].Gamma), 9);
    }
}
