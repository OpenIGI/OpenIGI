using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using Xunit;

namespace OpenIGI.UI.Tests;

/// <summary>
/// Covers atlas packing and text measurement without a GL context. The atlas is built from the exact
/// Barlow faces the game ships, so a font that fails to parse or pack in the build fails here too.
/// </summary>
public sealed class TextTests
{
    private static string FontPath(string file) =>
        Path.Combine(AppContext.BaseDirectory, "Assets", "fonts", file);

    private static byte[] Barlow() => File.ReadAllBytes(FontPath("Barlow-Regular.ttf"));

    private static byte[] Mono() => File.ReadAllBytes(FontPath("JetBrainsMono-Regular.ttf"));

    // ---- The developer tools' monospaced face ----

    [Fact]
    public void Mono_face_packs_at_the_size_the_developer_tools_bake_it()
    {
        FontAtlasData atlas = FontAtlasBuilder.Build(Mono(), 32f, FontAtlasKind.Coverage, FontAtlasBuilder.Extended);

        Assert.NotEmpty(atlas.Glyphs);
        Assert.True(atlas.Width > 0 && atlas.Height > 0);
    }

    /// <summary>
    /// The developer console places every column on an integer multiple of one character advance,
    /// measured once per size rather than per row. That is only correct while the face really is
    /// fixed-pitch, so assert the invariant rather than trusting the file name: swapping in a
    /// proportional face would otherwise misalign every table with nothing failing.
    /// </summary>
    [Fact]
    public void Mono_face_advances_every_printable_ascii_glyph_equally()
    {
        var font = UiFont.ForMeasurement(FontAtlasBuilder.Build(Mono(), 32f, FontAtlasKind.Coverage, FontAtlasBuilder.Extended));

        float expected = font.GetGlyph('M').Advance;
        Assert.True(expected > 0f);

        for (char c = ' '; c <= '~'; c++)
        {
            Assert.Equal(expected, font.GetGlyph(c).Advance, 3);
        }
    }

    [Fact]
    public void Mono_face_measures_a_run_as_its_character_count()
    {
        var font = UiFont.ForMeasurement(FontAtlasBuilder.Build(Mono(), 32f, FontAtlasKind.Coverage, FontAtlasBuilder.Extended));

        float cell = font.MeasureWidth("0", 14f);
        Assert.Equal(cell * 8f, font.MeasureWidth("r_vsync ", 14f), 2);
    }

    // ---- Packing (pure) ----

    [Fact]
    public void Packer_places_every_rectangle_inside_the_atlas_without_overlap()
    {
        var sizes = new (int, int)[]
        {
            (30, 40), (10, 12), (50, 20), (8, 60), (25, 25), (40, 10), (12, 12), (33, 44),
        };

        ShelfPacker.Result result = ShelfPacker.Pack(sizes, atlasWidth: 128, padding: 1);

        for (int i = 0; i < sizes.Length; i++)
        {
            ShelfPacker.Placement a = result.Placements[i];
            Assert.True(a.X + sizes[i].Item1 <= 128, "rectangle ran off the right edge");
            Assert.True(a.Y + sizes[i].Item2 <= result.Height, "rectangle ran below the atlas");

            for (int j = i + 1; j < sizes.Length; j++)
            {
                ShelfPacker.Placement b = result.Placements[j];
                bool disjoint =
                    a.X + sizes[i].Item1 <= b.X || b.X + sizes[j].Item1 <= a.X ||
                    a.Y + sizes[i].Item2 <= b.Y || b.Y + sizes[j].Item2 <= a.Y;
                Assert.True(disjoint, $"rectangles {i} and {j} overlap");
            }
        }
    }

    [Fact]
    public void Packer_is_deterministic()
    {
        var sizes = new (int, int)[] { (20, 20), (30, 10), (15, 40), (10, 10) };
        ShelfPacker.Result first = ShelfPacker.Pack(sizes, 64, 1);
        ShelfPacker.Result second = ShelfPacker.Pack(sizes, 64, 1);
        Assert.Equal(first.Height, second.Height);
        Assert.Equal(first.Placements, second.Placements);
    }

    [Fact]
    public void Packer_rejects_a_rectangle_wider_than_the_atlas()
    {
        Assert.Throws<ArgumentException>(() => ShelfPacker.Pack([(200, 10)], atlasWidth: 128, padding: 1));
    }

    // ---- Atlas build + measurement (uses the real font, no GL) ----

    [Fact]
    public void Coverage_atlas_covers_printable_ascii()
    {
        FontAtlasData atlas = FontAtlasBuilder.Build(Barlow(), pixelHeight: 48f);
        Assert.Equal(FontAtlasKind.Coverage, atlas.Kind);
        Assert.Equal(FontAtlasBuilder.AsciiPrintable.Count, atlas.Glyphs.Count);
        Assert.True(atlas.Height > 0);
        Assert.Equal(atlas.Width * atlas.Height, atlas.Pixels.Length);
        Assert.True(atlas.Ascent > 0f);
    }

    [Fact]
    public void Sdf_atlas_carries_its_distance_parameters()
    {
        FontAtlasData atlas = FontAtlasBuilder.Build(Barlow(), 48f, FontAtlasKind.SignedDistanceField);
        Assert.Equal(FontAtlasKind.SignedDistanceField, atlas.Kind);
        Assert.True(atlas.SdfPadding > 0);
        Assert.True(atlas.SdfPixelDistScale > 0f);
    }

    [Fact]
    public void Letter_glyphs_have_ink_and_space_does_not()
    {
        var font = UiFont.ForMeasurement(FontAtlasBuilder.Build(Barlow(), 48f));
        Assert.True(font.GetGlyph('A').HasBitmap);
        Assert.False(font.GetGlyph(' ').HasBitmap);
        Assert.True(font.GetGlyph(' ').Advance > 0f);
    }

    [Fact]
    public void Measurement_grows_with_length_and_scales_with_size()
    {
        var font = UiFont.ForMeasurement(FontAtlasBuilder.Build(Barlow(), 48f));

        float shortWidth = font.MeasureWidth("AB", 32f);
        float longWidth = font.MeasureWidth("ABCDEF", 32f);
        Assert.True(longWidth > shortWidth);

        // Twice the pixel size measures twice as wide, to within rounding.
        float small = font.MeasureWidth("Hello", 24f);
        float big = font.MeasureWidth("Hello", 48f);
        Assert.Equal(2f, big / small, 2);
    }

    [Fact]
    public void Measurement_counts_lines_for_height()
    {
        var font = UiFont.ForMeasurement(FontAtlasBuilder.Build(Barlow(), 48f));

        // Identical lines so the widest line is unambiguous and the width is expected to match.
        UiVec2 oneLine = font.Measure("abc", 32f);
        UiVec2 twoLines = font.Measure("abc\nabc", 32f);
        Assert.Equal(2f * oneLine.Y, twoLines.Y, 2);
        Assert.Equal(oneLine.X, twoLines.X, 1);
    }

    [Fact]
    public void Unknown_codepoint_falls_back_rather_than_throwing()
    {
        var font = UiFont.ForMeasurement(FontAtlasBuilder.Build(Barlow(), 48f));
        GlyphInfo fallback = font.GetGlyph('☃'); // snowman, not in the ASCII atlas
        Assert.Equal('?', fallback.Codepoint);
    }

    // ---- Disk cache ----

    [Fact]
    public void Cache_round_trips_an_atlas()
    {
        string dir = Path.Combine(Path.GetTempPath(), "openigi-ui-tests", Guid.NewGuid().ToString("N"));
        try
        {
            FontAtlasData built = FontAtlasCache.LoadOrBuild(dir, Barlow(), 48f);
            string key = FontAtlasCache.ComputeKey(Barlow(), 48f, FontAtlasKind.Coverage, FontAtlasBuilder.AsciiPrintable, 512, 4, 128);
            Assert.True(File.Exists(Path.Combine(dir, $"font-{key}.atlas")));

            FontAtlasData reloaded = FontAtlasCache.LoadOrBuild(dir, Barlow(), 48f);
            Assert.Equal(built.Width, reloaded.Width);
            Assert.Equal(built.Height, reloaded.Height);
            Assert.Equal(built.Glyphs.Count, reloaded.Glyphs.Count);
            Assert.Equal(built.Pixels, reloaded.Pixels);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void Cache_key_changes_with_pixel_height()
    {
        string a = FontAtlasCache.ComputeKey(Barlow(), 32f, FontAtlasKind.Coverage, FontAtlasBuilder.AsciiPrintable, 512, 4, 128);
        string b = FontAtlasCache.ComputeKey(Barlow(), 48f, FontAtlasKind.Coverage, FontAtlasBuilder.AsciiPrintable, 512, 4, 128);
        Assert.NotEqual(a, b);
    }
}
