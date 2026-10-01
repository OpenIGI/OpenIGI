using System.Linq;
using OpenIGI.Core.Graphics;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;
using Xunit;

namespace OpenIGI.UI.Tests;

/// <summary>
/// Exercises the measure/arrange solver — the containers, padding and alignment — with no GL context.
/// The context is built at the reference resolution so one design unit is one pixel and the arithmetic
/// in the assertions is the arithmetic the solver should produce.
/// </summary>
public sealed class LayoutSolverTests
{
    private static LayoutContext Reference() => new(1920f, 1080f);

    private static void Layout(UiElement root, LayoutContext context)
    {
        root.Measure(context, context.SafeRect.Size);
        root.Arrange(context.SafeRect);
    }

    /// <summary>A leaf with a fixed content size in design units, for driving the solver.</summary>
    private sealed class TestBox : UiElement
    {
        public UiVec2 ContentSize { get; init; }

        protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) =>
            new(context.Px(ContentSize.X), context.Px(ContentSize.Y));
    }

    [Fact]
    public void Vertical_stack_places_children_in_order_with_spacing()
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10f };
        var a = new TestBox { ContentSize = new UiVec2(100f, 40f) };
        var b = new TestBox { ContentSize = new UiVec2(100f, 40f) };
        var c = new TestBox { ContentSize = new UiVec2(100f, 40f) };
        stack.AddChild(a);
        stack.AddChild(b);
        stack.AddChild(c);

        Layout(stack, Reference());

        Assert.Equal(0f, a.Bounds.Y, 2);
        Assert.Equal(50f, b.Bounds.Y, 2);
        Assert.Equal(100f, c.Bounds.Y, 2);
    }

    [Fact]
    public void Horizontal_stack_advances_along_x()
    {
        var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8f };
        var a = new TestBox { ContentSize = new UiVec2(60f, 20f) };
        var b = new TestBox { ContentSize = new UiVec2(30f, 20f) };
        stack.AddChild(a);
        stack.AddChild(b);

        Layout(stack, Reference());

        Assert.Equal(0f, a.Bounds.X, 2);
        Assert.Equal(68f, b.Bounds.X, 2);
    }

    [Fact]
    public void Padding_insets_children()
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical, Padding = new Thickness(20f) };
        var a = new TestBox { ContentSize = new UiVec2(100f, 40f) };
        stack.AddChild(a);

        Layout(stack, Reference());

        Assert.Equal(20f, a.Bounds.X, 2);
        Assert.Equal(20f, a.Bounds.Y, 2);
    }

    [Fact]
    public void Grid_auto_column_takes_content_and_star_takes_the_rest()
    {
        var grid = new Grid { Width = 1000f, Height = 100f };
        grid.ColumnDefinitions.Add(GridLength.Auto);
        grid.ColumnDefinitions.Add(GridLength.Star);
        var label = new TestBox { ContentSize = new UiVec2(120f, 30f) };
        var field = new TestBox { ContentSize = new UiVec2(50f, 30f) };
        grid.Add(label, 0, 0);
        grid.Add(field, 0, 1);

        Layout(grid, Reference());

        Assert.Equal(0f, label.Bounds.X, 2);
        Assert.Equal(120f, label.Bounds.Width, 2);
        Assert.Equal(120f, field.Bounds.X, 2);
        Assert.Equal(1000f - 120f, field.Bounds.Width, 2);
    }

    [Fact]
    public void Grid_two_star_columns_split_evenly()
    {
        var grid = new Grid { Width = 800f, Height = 50f };
        grid.ColumnDefinitions.Add(GridLength.Star);
        grid.ColumnDefinitions.Add(GridLength.Star);
        var a = new TestBox { ContentSize = new UiVec2(10f, 10f) };
        var b = new TestBox { ContentSize = new UiVec2(10f, 10f) };
        grid.Add(a, 0, 0);
        grid.Add(b, 0, 1);

        Layout(grid, Reference());

        Assert.Equal(400f, a.Bounds.Width, 2);
        Assert.Equal(400f, b.Bounds.Width, 2);
        Assert.Equal(400f, b.Bounds.X, 2);
    }

    [Fact]
    public void Flex_grow_shares_free_space()
    {
        var flex = new FlexPanel { Orientation = Orientation.Horizontal, Width = 1000f, Height = 50f };
        var a = new TestBox { ContentSize = new UiVec2(100f, 20f) };
        var b = new TestBox { ContentSize = new UiVec2(100f, 20f) };
        flex.Add(a, grow: 1f);
        flex.Add(b, grow: 1f);

        Layout(flex, Reference());

        // 1000 − 200 desired = 800 free, split evenly: each child ends up 100 + 400 = 500 wide.
        Assert.Equal(500f, a.Bounds.Width, 2);
        Assert.Equal(500f, b.Bounds.Width, 2);
        Assert.Equal(500f, b.Bounds.X, 2);
    }

    [Fact]
    public void Flex_space_between_pins_ends_and_spreads_the_gap()
    {
        var flex = new FlexPanel { Orientation = Orientation.Horizontal, Width = 1000f, Height = 50f, Justify = FlexJustify.SpaceBetween };
        var a = new TestBox { ContentSize = new UiVec2(100f, 20f) };
        var b = new TestBox { ContentSize = new UiVec2(100f, 20f) };
        flex.Add(a);
        flex.Add(b);

        Layout(flex, Reference());

        Assert.Equal(0f, a.Bounds.X, 2);
        Assert.Equal(900f, b.Bounds.X, 2);
    }

    [Fact]
    public void Canvas_fill_anchor_stretches_a_child_to_the_canvas()
    {
        var canvas = new Canvas { Width = 600f, Height = 400f };
        var bg = new TestBox();
        canvas.Add(bg, Anchor.Fill);

        Layout(canvas, Reference());

        Assert.Equal(canvas.Bounds.Width, bg.Bounds.Width, 2);
        Assert.Equal(canvas.Bounds.Height, bg.Bounds.Height, 2);
    }

    [Fact]
    public void Canvas_point_anchor_with_centre_alignment_centres_a_sized_child()
    {
        var canvas = new Canvas { Width = 600f, Height = 400f };
        var logo = new TestBox
        {
            ContentSize = new UiVec2(100f, 50f),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        canvas.Add(logo, Anchor.Fill);

        Layout(canvas, Reference());

        Assert.Equal((600f - 100f) / 2f, logo.Bounds.X - canvas.Bounds.X, 2);
        Assert.Equal((400f - 50f) / 2f, logo.Bounds.Y - canvas.Bounds.Y, 2);
    }

    [Fact]
    public void Right_alignment_pins_to_the_right_edge()
    {
        var panel = new Panel { Width = 500f, Height = 100f };
        var box = new TestBox
        {
            ContentSize = new UiVec2(80f, 20f),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
        };
        panel.AddChild(box);

        Layout(panel, Reference());

        Assert.Equal(panel.Bounds.Right - 80f, box.Bounds.X, 2);
        Assert.Equal(panel.Bounds.Y, box.Bounds.Y, 2);
    }

    [Fact]
    public void Collapsed_child_takes_no_space()
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        var a = new TestBox { ContentSize = new UiVec2(100f, 40f) };
        var hidden = new TestBox { ContentSize = new UiVec2(100f, 40f), Visibility = Visibility.Collapsed };
        var c = new TestBox { ContentSize = new UiVec2(100f, 40f) };
        stack.AddChild(a);
        stack.AddChild(hidden);
        stack.AddChild(c);

        Layout(stack, Reference());

        // c follows a directly, as if the collapsed box were not in the tree.
        Assert.Equal(40f, c.Bounds.Y, 2);
    }

    [Fact]
    public void Collapsing_a_subtree_empties_every_bounds_inside_it()
    {
        // The shape of a tab strip: two pages in one panel, one shown at a time. The page that is put away
        // must take its whole subtree with it, because a row's OWN visibility is still Visible -- only its
        // ancestor is collapsed -- so anything reading Bounds still finds it. Focus by point is exactly
        // that: leaving the stale rectangles behind let a hidden tab answer a click meant for the visible
        // one, and dragging a slider on one page moved whatever sat at the same place on the other.
        var pages = new Panel();
        var shown = new Panel();
        var hiddenPage = new Panel();
        var rowOnShownPage = new TestBox { ContentSize = new UiVec2(200f, 40f) };
        var rowOnHiddenPage = new TestBox { ContentSize = new UiVec2(200f, 40f) };
        shown.AddChild(rowOnShownPage);
        hiddenPage.AddChild(rowOnHiddenPage);
        pages.AddChild(shown);
        pages.AddChild(hiddenPage);

        LayoutContext context = Reference();
        Layout(pages, context);
        Assert.False(rowOnHiddenPage.Bounds.IsEmpty); // both pages were up for this pass

        hiddenPage.Visibility = Visibility.Collapsed;
        Layout(pages, context);

        Assert.False(rowOnShownPage.Bounds.IsEmpty);
        Assert.True(hiddenPage.Bounds.IsEmpty);
        Assert.True(rowOnHiddenPage.Bounds.IsEmpty);
    }

    [Fact]
    public void A_grid_arranged_narrower_than_it_was_measured_resizes_its_star_tracks()
    {
        // The shape that caught this: an outer grid measures every child against the whole viewport and
        // only then hands each one its column. A grid inside one of those columns therefore sees the full
        // width at measure and a fraction of it at arrange, and reusing the measured track sizes laid its
        // children out hanging off the side of the column they belong to.
        LayoutContext context = Reference();

        var outer = new Grid();
        outer.ColumnDefinitions.Add(GridLength.Stars(0.25f));
        outer.ColumnDefinitions.Add(GridLength.Stars(0.75f));
        outer.RowDefinitions.Add(GridLength.Star);

        var inner = new Grid();
        inner.ColumnDefinitions.Add(GridLength.Star);
        inner.RowDefinitions.Add(GridLength.Star);
        var child = new TestBox { ContentSize = new UiVec2(50f, 50f) };
        inner.Add(child, 0, 0);

        outer.Add(inner, 0, 0);
        outer.Add(new TestBox { ContentSize = new UiVec2(50f, 50f) }, 0, 1);
        Layout(outer, context);

        Assert.Equal(context.SafeRect.Width * 0.25f, inner.Bounds.Width, 1);
        Assert.Equal(inner.Bounds.Width, child.Bounds.Width, 1);
        Assert.True(
            child.Bounds.Right <= inner.Bounds.Right + 0.5f,
            $"the child ran to {child.Bounds.Right} past its column's {inner.Bounds.Right}");
    }

    [Fact]
    public void A_grid_row_sizes_to_what_a_wrapping_child_needs_at_its_real_width()
    {
        // The same staleness seen through the other axis: a paragraph measured against the whole viewport
        // reports one line, so an auto row sized from that pass is one line tall however narrow the column
        // it actually lands in turns out to be.
        LayoutContext context = Reference();
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "fonts", "Barlow-Regular.ttf");
        UiFont font = UiFont.ForMeasurement(FontAtlasBuilder.Build(File.ReadAllBytes(path), 48f));

        var outer = new Grid();
        outer.ColumnDefinitions.Add(GridLength.Stars(0.2f));
        outer.ColumnDefinitions.Add(GridLength.Stars(0.8f));
        outer.RowDefinitions.Add(GridLength.Star);

        var inner = new Grid();
        inner.ColumnDefinitions.Add(GridLength.Star);
        inner.RowDefinitions.Add(GridLength.Auto);
        inner.RowDefinitions.Add(GridLength.Star);

        var paragraph = new Label(font, string.Join(' ', Enumerable.Repeat("briefing", 40)), 22f, Bgra32.White)
        {
            Wrap = true,
        };
        inner.Add(paragraph, 0, 0);
        outer.Add(inner, 0, 0);
        Layout(outer, context);

        Assert.True(
            paragraph.Bounds.Height > font.LineHeight(context.Px(22f)) * 2f,
            $"the paragraph was given {paragraph.Bounds.Height}px, which is too few lines for its column");
    }
}
