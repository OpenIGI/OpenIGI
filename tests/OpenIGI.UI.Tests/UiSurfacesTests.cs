using OpenIGI.Core.Graphics;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using OpenIGI.UI.Layout;
using Xunit;

namespace OpenIGI.UI.Tests;

/// <summary>
/// Covers the generated corner atlas and the rounded/shadow drawing built on it.
/// </summary>
public sealed class UiSurfacesTests
{
    [Fact]
    public void The_atlas_is_created_once_and_yields_two_valid_slices()
    {
        var device = new RecordingGraphicsDevice();
        using var surfaces = UiSurfaces.Create(device);

        Assert.True(surfaces.Texture.IsValid);
        Assert.True(surfaces.Rounded.IsValid);
        Assert.True(surfaces.Shadow.IsValid);
    }

    /// <summary>
    /// Mirroring is expressed by negating the source extent, which is what lets one authored quadrant
    /// serve all four corners and guarantees they are identical rather than nearly so.
    /// </summary>
    [Fact]
    public void The_corners_are_the_same_texels_mirrored()
    {
        var device = new RecordingGraphicsDevice();
        using var surfaces = UiSurfaces.Create(device);

        UiRect topLeft = surfaces.Rounded.Corner(false, false);
        UiRect topRight = surfaces.Rounded.Corner(true, false);
        UiRect bottomRight = surfaces.Rounded.Corner(true, true);

        Assert.True(topLeft.Width > 0f);
        Assert.True(topRight.Width < 0f);
        Assert.True(bottomRight.Width < 0f && bottomRight.Height < 0f);
        Assert.Equal(MathF.Abs(topLeft.Width), MathF.Abs(topRight.Width), 5);
    }

    [Fact]
    public void A_rounded_rectangle_is_nine_quads()
    {
        var device = new RecordingGraphicsDevice();
        using var surfaces = UiSurfaces.Create(device);

        var context = new LayoutContext(1000f, 600f, reference: new UiVec2(1000f, 600f));
        var batch = new SpriteBatch(device);
        var painter = UiPainter.Create(batch, context);

        batch.Begin();
        painter.FillRoundedRect(new UiRect(10f, 10f, 400f, 300f), surfaces.Rounded, Bgra32.White, 12f);
        batch.End();

        Assert.Equal(9, batch.QuadCount);
    }

    /// <summary>
    /// A caller must never have to check whether the atlas was built, so a missing slice degrades to a
    /// plain fill rather than drawing nothing.
    /// </summary>
    [Fact]
    public void Without_a_slice_it_falls_back_to_one_plain_fill()
    {
        var device = new RecordingGraphicsDevice();
        var context = new LayoutContext(1000f, 600f, reference: new UiVec2(1000f, 600f));
        var batch = new SpriteBatch(device);
        var painter = UiPainter.Create(batch, context);

        batch.Begin();
        painter.FillRoundedRect(new UiRect(10f, 10f, 400f, 300f), default, Bgra32.White, 12f);
        batch.End();

        Assert.Equal(1, batch.QuadCount);
    }

    [Fact]
    public void A_radius_can_never_exceed_half_the_rectangle()
    {
        var device = new RecordingGraphicsDevice();
        using var surfaces = UiSurfaces.Create(device);

        var context = new LayoutContext(1000f, 600f, reference: new UiVec2(1000f, 600f));
        var batch = new SpriteBatch(device);
        var painter = UiPainter.Create(batch, context);

        // A 40-pixel radius on a 20-pixel-tall bar would otherwise produce inverted edge rectangles.
        batch.Begin();
        painter.FillRoundedRect(new UiRect(0f, 0f, 200f, 20f), surfaces.Rounded, Bgra32.White, 40f);
        batch.End();

        Assert.True(batch.QuadCount is > 0 and <= 9);
    }

    [Fact]
    public void A_shadow_with_no_spread_draws_nothing()
    {
        var device = new RecordingGraphicsDevice();
        using var surfaces = UiSurfaces.Create(device);

        var context = new LayoutContext(1000f, 600f, reference: new UiVec2(1000f, 600f));
        var batch = new SpriteBatch(device);
        var painter = UiPainter.Create(batch, context);

        batch.Begin();
        painter.DrawShadow(new UiRect(50f, 50f, 200f, 100f), surfaces.Shadow, Bgra32.Black, spread: 0f);
        batch.End();

        Assert.Equal(0, batch.QuadCount);
    }

    [Fact]
    public void Tracking_widens_a_measured_run_by_one_step_per_glyph()
    {
        var device = new RecordingGraphicsDevice();
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "fonts", "JetBrainsMono-Regular.ttf");
        OpenIGI.UI.Text.UiFont font = OpenIGI.UI.Text.UiFont.Upload(
            device,
            OpenIGI.UI.Text.FontAtlasBuilder.Build(File.ReadAllBytes(path), 32f));

        float plain = font.MeasureWidth("ABCD", 14f);
        float tracked = UiPainter.MeasureTracked(font, "ABCD", 14f, 2f);

        Assert.Equal(plain + 8f, tracked, 3);
    }
}
