using OpenIGI.UI;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using Xunit;

namespace OpenIGI.UI.Tests;

/// <summary>Pins how a <see cref="ReorderList"/> is put in order: by dragging, by the keys, and abandoned.</summary>
public sealed class ReorderListTests
{
    [Theory]
    [InlineData(0f, 0)]
    [InlineData(19f, 0)]
    [InlineData(21f, 1)]
    [InlineData(70f, 1)]
    [InlineData(71f, 2)]
    [InlineData(500f, 2)]
    public void A_carried_item_passes_a_neighbour_at_its_middle(float centre, int slot) =>
        Assert.Equal(slot, ReorderList.SlotFor(centre, [40f, 40f], 10f));

    [Fact]
    public void Dragging_an_item_past_its_neighbour_moves_it_and_reports_the_move()
    {
        (ReorderList list, Box a, Box b, Box c) = Build();
        List<(int From, int To)> moves = [];
        list.Moved += (from, to) => moves.Add((from, to));

        Assert.True(list.BeginDrag(b, new UiVec2(10f, b.Bounds.Center.Y)));
        list.DragTo(new UiVec2(10f, c.Bounds.Bottom + 5f));
        list.Update(0.016f);
        list.Drop(cancel: false);

        Assert.Equal([a, c, b], list.Items);
        Assert.Equal([(1, 2)], moves);
    }

    [Fact]
    public void An_abandoned_drag_puts_the_item_back_and_reports_nothing()
    {
        (ReorderList list, Box a, Box b, Box c) = Build();
        bool moved = false;
        list.Moved += (_, _) => moved = true;

        list.BeginDrag(a, new UiVec2(10f, a.Bounds.Center.Y));
        list.DragTo(new UiVec2(10f, c.Bounds.Bottom + 50f));
        list.Drop(cancel: true);

        Assert.Equal([a, b, c], list.Items);
        Assert.False(moved);
    }

    [Fact]
    public void The_keys_move_a_picked_up_item_one_place_at_a_time()
    {
        (ReorderList list, Box a, Box b, Box c) = Build();
        List<(int From, int To)> moves = [];
        list.Moved += (from, to) => moves.Add((from, to));

        Assert.True(list.BeginKeyboardMove(c));
        Assert.True(list.IsCarriedByKeys);
        Assert.True(list.Step(-1));
        Assert.True(list.Step(-1));
        Assert.False(list.Step(-1));
        list.Drop(cancel: false);

        Assert.Equal([c, a, b], list.Items);
        Assert.Equal([(2, 0)], moves);
        Assert.Null(list.Carried);
    }

    private static (ReorderList List, Box A, Box B, Box C) Build()
    {
        var list = new ReorderList { Spacing = 10f, SettleSeconds = 0f };
        Box a = new(), b = new(), c = new();
        list.Add(a);
        list.Add(b);
        list.Add(c);

        LayoutContext context = new(1920f, 1080f);
        list.Measure(context, context.SafeRect.Size);
        list.Arrange(context.SafeRect);
        return (list, a, b, c);
    }

    private sealed class Box : UiElement
    {
        protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(context.Px(200f), context.Px(40f));
    }
}
