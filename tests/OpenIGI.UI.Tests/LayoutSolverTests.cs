using OpenIGI.UI;
using OpenIGI.UI.Layout;
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
}
