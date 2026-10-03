using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using Xunit;

namespace OpenIGI.UI.Tests;

/// <summary>Pins the two pieces that let a page fit a narrow window: shortening text, and stacking columns.</summary>
public sealed class ResponsiveLayoutTests
{
    private static UiFont Barlow() =>
        UiFont.ForMeasurement(FontAtlasBuilder.Build(
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "fonts", "Barlow-Regular.ttf")),
            32f,
            FontAtlasKind.Coverage,
            FontAtlasBuilder.Extended));

    [Fact]
    public void ToWidth_leaves_text_that_fits_alone()
    {
        UiFont font = Barlow();
        Assert.Equal("Library folder", TextFit.ToWidth(font, "Library folder", 18f, 1000f));
    }

    [Fact]
    public void ToWidth_cuts_text_that_does_not_fit_and_ends_it_with_an_ellipsis()
    {
        UiFont font = Barlow();
        const string hint = "A desktop notification when a release comes out while the launcher is in the background";
        float width = UiPainter.MeasureLabel(font, hint, 16f, 0f) / 2f;

        string fitted = TextFit.ToWidth(font, hint, 16f, width);

        Assert.EndsWith("…", fitted, StringComparison.Ordinal);
        Assert.True(UiPainter.MeasureLabel(font, fitted, 16f, 0f) <= width);
        Assert.StartsWith(fitted[..^1], hint, StringComparison.Ordinal);

        // As long as it can be: one more character would not fit.
        string longer = string.Concat(hint.AsSpan(0, fitted.Length), "…");
        Assert.True(UiPainter.MeasureLabel(font, longer, 16f, 0f) > width);
    }

    [Fact]
    public void ToWidth_with_no_room_draws_nothing()
    {
        Assert.Equal(string.Empty, TextFit.ToWidth(Barlow(), "Library folder", 18f, 0f));
    }

    [Theory]
    [InlineData("short", 10, "short")]
    [InlineData("a long variable name", 8, "a long …")]
    [InlineData("x", 1, "x")]
    public void ToColumns_cuts_to_the_column_count(string text, int columns, string expected) =>
        Assert.Equal(expected, TextFit.ToColumns(text, columns));

    [Fact]
    public void Columns_sit_side_by_side_while_each_has_its_minimum_width()
    {
        AdaptiveColumns columns = Columns(out Panel left, out Panel right);
        Layout(columns, 1400f);

        Assert.False(columns.IsStacked);
        Assert.Equal(left.Bounds.Y, right.Bounds.Y);
        Assert.True(right.Bounds.X > left.Bounds.Right);
        Assert.Equal(left.Bounds.Width, right.Bounds.Width, 3);
    }

    [Fact]
    public void Columns_stack_when_the_window_is_too_narrow_for_both()
    {
        AdaptiveColumns columns = Columns(out Panel left, out Panel right);
        Layout(columns, 1000f);

        Assert.True(columns.IsStacked);
        Assert.Equal(left.Bounds.X, right.Bounds.X);
        Assert.True(right.Bounds.Y >= left.Bounds.Bottom);
        Assert.Equal(columns.Bounds.Width, left.Bounds.Width, 3);
    }

    private static AdaptiveColumns Columns(out Panel left, out Panel right)
    {
        left = new Panel { Height = 200f };
        right = new Panel { Height = 120f };
        AdaptiveColumns columns = new() { MinColumnWidth = 600f, Spacing = 20f };
        columns.Add(left);
        columns.Add(right);
        return columns;
    }

    private static void Layout(UiElement root, float width)
    {
        // At the design's own scale, so a design unit is a pixel.
        var context = new LayoutContext(width, 900f, new UiVec2(width, 900f));
        root.Measure(context, new UiVec2(width, 900f));
        root.Arrange(new UiRect(0f, 0f, width, 900f));
    }
}
