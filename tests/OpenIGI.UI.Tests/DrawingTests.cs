using OpenIGI.Core.Graphics;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Rhi;
using OpenIGI.UI.Skia;
using Xunit;

namespace OpenIGI.UI.Tests;

/// <summary>
/// The backend-neutral drawing layer: what the painter reduces shapes to, so the game's sprite batch and the
/// launcher's Skia canvas draw the same pixels from the same calls.
/// </summary>
public sealed class DrawingTests
{
    private static (UiPainter Painter, RecordingUiRenderer Renderer) Painter()
    {
        var renderer = new RecordingUiRenderer();
        return (new UiPainter(renderer, new LayoutContext(1920f, 1080f)), renderer);
    }

    [Fact]
    public void A_rectangle_inside_the_clip_is_one_untrimmed_quad()
    {
        (UiPainter painter, RecordingUiRenderer renderer) = Painter();
        painter.FillRect(new UiRect(10f, 20f, 100f, 50f), Bgra32.White);

        RecordingUiRenderer.RecordedQuad quad = Assert.Single(renderer.Quads);
        Assert.Equal(10f, quad.Corners[0].X);
        Assert.Equal(20f, quad.Corners[0].Y);
        Assert.Equal(110f, quad.Corners[2].X);
        Assert.Equal(70f, quad.Corners[2].Y);
    }

    [Fact]
    public void A_rectangle_is_trimmed_to_the_clip_with_its_texture_window_in_proportion()
    {
        (UiPainter painter, RecordingUiRenderer renderer) = Painter();
        painter.PushClip(new UiRect(0f, 0f, 50f, 100f));
        painter.DrawSprite(new UiImage(7), new UiRect(0f, 0f, 100f, 100f), UiRect.Unit, Bgra32.White);
        painter.PopClip();

        RecordingUiRenderer.RecordedQuad quad = Assert.Single(renderer.Quads);
        Assert.Equal(50f, quad.Corners[1].X);
        Assert.Equal(0.5f, quad.Corners[1].U, 4);
    }

    [Fact]
    public void A_transform_scales_and_moves_what_is_drawn_under_it()
    {
        (UiPainter painter, RecordingUiRenderer renderer) = Painter();
        painter.PushTransform(UiTransform.ScaleAbout(2f, new UiVec2(100f, 100f)));
        painter.FillRect(new UiRect(100f, 100f, 10f, 10f), Bgra32.White);
        painter.PopTransform();

        RecordingUiRenderer.RecordedQuad quad = Assert.Single(renderer.Quads);
        Assert.Equal(100f, quad.Corners[0].X);
        Assert.Equal(120f, quad.Corners[2].X);
    }

    [Fact]
    public void Only_the_slanted_edges_of_a_cut_corner_are_feathered()
    {
        (UiPainter painter, RecordingUiRenderer renderer) = Painter();
        painter.FillCutRect(new UiRect(0f, 0f, 200f, 100f), 20f, UiCorners.BottomRight, Bgra32.White);

        // Five corners fan into two quads; the one slanted edge adds one transparent fringe quad.
        Assert.Equal(3, renderer.Quads.Count);
        RecordingUiRenderer.RecordedQuad fringe = renderer.Quads[^1];
        Assert.Contains(fringe.Corners, corner => corner.Color.A == 0);
    }

    [Fact]
    public void A_polygon_crossing_the_clip_is_cut_to_it()
    {
        Span<UiVertex> triangle =
        [
            new(0f, 0f, 0f, 0f, Bgra32.White),
            new(100f, 0f, 0f, 0f, Bgra32.White),
            new(0f, 100f, 0f, 0f, Bgra32.White),
        ];
        Span<UiVertex> output = stackalloc UiVertex[UiGeometry.MaxClippedVertices];
        int count = UiGeometry.ClipPolygon(triangle, new UiRect(0f, 0f, 50f, 50f), output);

        Assert.True(count >= 4);
        foreach (UiVertex v in output[..count])
        {
            Assert.InRange(v.X, 0f, 50f);
            Assert.InRange(v.Y, 0f, 50f);
        }
    }

    [Fact]
    public void A_gradient_samples_between_its_stops()
    {
        ReadOnlySpan<UiGradientStop> stops = [new(0f, Bgra32.FromRgba(0, 0, 0, 0)), new(1f, Bgra32.FromRgba(200, 100, 0, 255))];
        Bgra32 middle = UiGeometry.Sample(stops, 0.5f);

        Assert.Equal(100, middle.R);
        Assert.Equal(50, middle.G);
        Assert.Equal(128, middle.A);
    }

    [Fact]
    public void Glass_hands_the_renderer_the_clipped_polygon_and_the_blur()
    {
        (UiPainter painter, RecordingUiRenderer renderer) = Painter();
        painter.PushOpacity(0.5f);
        painter.DrawBackdrop(new UiRect(0f, 0f, 100f, 100f), 18f, Bgra32.FromRgba(10, 12, 13, 168));
        painter.PopOpacity();

        RecordingUiRenderer.RecordedBackdrop glass = Assert.Single(renderer.Backdrops);
        Assert.Equal(18f, glass.SigmaPx);
        Assert.Equal(0.5f, glass.Opacity);
        Assert.Equal(84, glass.Tint.A);
        Assert.Equal(4, glass.Polygon.Length);
    }

    [Theory]
    [InlineData(UiCorners.BottomRight, 5)]
    [InlineData(UiCorners.TopLeft | UiCorners.BottomRight, 6)]
    [InlineData(UiCorners.None, 4)]
    [InlineData(UiCorners.All, 8)]
    public void A_cut_corner_box_has_one_extra_point_per_cut(UiCorners corners, int points)
    {
        Span<UiVec2> path = stackalloc UiVec2[UiPath.MaxPoints];
        Assert.Equal(points, UiPath.CutCornerRect(new UiRect(0f, 0f, 380f, 96f), 22f, corners, path));
    }

    [Fact]
    public void The_cta_cut_matches_the_mock_ups_clip_path()
    {
        // clip-path: polygon(0 0, 100% 0, 100% calc(100% - 22px), calc(100% - 22px) 100%, 0 100%)
        Span<UiVec2> path = stackalloc UiVec2[UiPath.MaxPoints];
        int count = UiPath.CutCornerRect(new UiRect(0f, 0f, 380f, 96f), 22f, UiCorners.BottomRight, path);

        Assert.Equal(
            [new UiVec2(0f, 0f), new UiVec2(380f, 0f), new UiVec2(380f, 74f), new UiVec2(358f, 96f), new UiVec2(0f, 96f)],
            path[..count].ToArray());
    }

    [Fact]
    public void The_design_curves_start_at_zero_end_at_one_and_never_go_back()
    {
        foreach (Ease ease in (Ease[])[Ease.Emphasized, Ease.EmphasizedAccelerate, Ease.Snappy, Ease.Sweep])
        {
            Assert.Equal(0f, Easing.Evaluate(ease, 0f), 4);
            Assert.Equal(1f, Easing.Evaluate(ease, 1f), 4);

            float previous = 0f;
            for (int i = 1; i <= 100; i++)
            {
                float value = Easing.Evaluate(ease, i / 100f);
                Assert.True(value >= previous - 1e-4f, $"{ease} turned back at {i}%");
                previous = value;
            }
        }

        // cubic-bezier(.16,1,.3,1) is most of the way there by half time: the design's "fast start".
        Assert.True(Easing.Evaluate(Ease.Emphasized, 0.5f) > 0.9f);
    }

    [Fact]
    public void A_title_change_swaps_once_the_old_title_has_gone_and_settles_back_to_rest()
    {
        var reveal = new TitleReveal();
        int swaps = 0;
        reveal.Start(1, () => swaps++);

        reveal.Update(0.1f);
        Assert.Equal(0, swaps);
        Assert.True(reveal.TitleOpacity < 1f);

        reveal.Update(0.2f);
        Assert.Equal(1, swaps);
        Assert.Equal(1.1f, reveal.BackgroundScale, 2);

        for (int i = 0; i < 100; i++)
        {
            reveal.Update(1f / 60f);
        }

        Assert.False(reveal.IsRunning);
        Assert.Equal(1f, reveal.TitleOpacity);
        Assert.Equal(1f, reveal.BackgroundScale);
        Assert.Equal(0.01f, reveal.LetterSpacing, 4);
    }

    [Theory]
    [InlineData(3f, 0, 0, 0f)]
    [InlineData(4f, 0, 0, 0f)]
    [InlineData(8f, 0, 1, 1f)]
    [InlineData(64f, 3, 4, 1f)]
    [InlineData(100f, 4, 4, 0f)]
    public void Glass_picks_the_two_pyramid_levels_either_side_of_its_blur(float sigma, int lower, int upper, float blend)
    {
        RhiBackdropLevel[] levels = [.. Enumerable.Range(0, 5).Select(i => new RhiBackdropLevel(new Rendering.TextureHandle((uint)(i + 1)), MathF.Pow(2f, i + 2)))];
        var backdrop = new RhiBackdrop(levels, 1920f, 1080f);

        Assert.True(backdrop.TryPick(sigma, out RhiBackdropLevel low, out RhiBackdropLevel high, out float mix));
        Assert.Equal(levels[lower], low);
        Assert.Equal(levels[upper], high);
        Assert.Equal(blend, mix, 3);
    }

    [Fact]
    public void Skia_sizes_a_face_by_the_same_span_stb_truetype_measures()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "fonts", IgiTypography.BodyFile);
        float ratio = SkiaFontCache.EmPerPixelHeight(File.ReadAllBytes(path));

        // Barlow's em is a little shorter than its ascender-to-descender span, as in most text faces.
        Assert.InRange(ratio, 0.6f, 1f);
    }
}
