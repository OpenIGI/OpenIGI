using OpenIGI.Presentation.Hud.Remastered;
using OpenIGI.Presentation.Hud.Remastered.Widgets;
using OpenIGI.Presentation.Igi2.Hud;
using OpenIGI.UI;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Tests;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The bright band that now and then sweeps across the remastered I.G.I. 2 HUD's panels.</summary>
public sealed class Igi2HudPanelShimmerTests
{
    /// <summary>
    /// Wherever the band is in its sweep -- coming in at the left, crossing the middle, leaning out past the right -- nothing of
    /// it is drawn outside the panel: it used to spill over the screen beside the health and weapon panels.
    /// </summary>
    /// <param name="seconds">The time, which places the band in its sweep: 0.05 enters, 0.45 crosses, 0.85 leaves.</param>
    [Theory]
    [InlineData(0.05f)]
    [InlineData(0.45f)]
    [InlineData(0.85f)]
    public void The_band_stays_inside_the_panel(float seconds)
    {
        RecordingUiRenderer renderer = new();
        UiPainter painter = new(renderer, new LayoutContext(1920f, 1080f));
        UiRect panel = new(0f, 300f, 220f, 70f);

        HudPanel.Draw(painter, Igi2HudTheme.Theme, scale: 1f, seconds, panel);

        Assert.NotEmpty(renderer.Quads);
        foreach (RecordingUiRenderer.RecordedQuad quad in renderer.Quads)
        {
            foreach (UiVertex corner in quad.Corners)
            {
                // A pixel's give for the feathered edges of the panel's own rim.
                Assert.InRange(corner.X, panel.X - 1f, panel.Right + 1f);
                Assert.InRange(corner.Y, panel.Y - 1f, panel.Bottom + 1f);
            }
        }
    }
}
