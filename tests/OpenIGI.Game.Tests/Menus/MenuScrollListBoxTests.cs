using OpenIGI.Game.Menus;
using Xunit;

namespace OpenIGI.Game.Tests.Menus;

/// <summary>
/// The vertical list, from <c>ScrollListBox_Update</c> (<c>0x41BDD0</c>) and
/// <c>ScrollListBox_Render</c> (<c>0x41C080</c>).
/// </summary>
/// <remarks>
/// The behaviour worth pinning is what the mission screen depends on: a click lands on a <em>row</em>
/// and selects it, and only a second click on the row already selected runs its action. Before this was
/// transcribed the control had no pointer handling at all, so clicking a mission did nothing.
/// </remarks>
public sealed class MenuScrollListBoxTests
{
    private const int RowHeight = 10;

    [Fact]
    public void ClickingARowSelectsIt()
    {
        ScrollListBoxTask list = Filled();

        Click(list, Row(list, 2));

        Assert.Equal(2, list.Index);
        Assert.Equal(202, list.ActiveId);
    }

    [Fact]
    public void ClickingAnotherRowMovesTheSelectionAgain()
    {
        ScrollListBoxTask list = Filled();

        Click(list, Row(list, 3));
        Click(list, Row(list, 1));

        Assert.Equal(1, list.Index);
        Assert.Equal(201, list.ActiveId);
    }

    [Fact]
    public void ClickingBelowTheLastRowLeavesTheSelectionAlone()
    {
        ScrollListBoxTask list = Filled();
        Click(list, Row(list, 1));

        // The rows run out well before the box does; a click in the empty part is not row zero.
        Click(list, list.LayoutY + (RowHeight * 20));

        Assert.Equal(1, list.Index);
    }

    [Fact]
    public void ClickingTheSelectedRowDoesNotMoveTheSelection()
    {
        ScrollListBoxTask list = Filled();
        Click(list, Row(list, 2));

        // The original treats this as the first half of a double click rather than a re-selection, so
        // nothing moves and nothing is re-run. Whether the second click fires On Select Item needs a
        // script host, so it is covered by the menu-level tests rather than here.
        Click(list, Row(list, 2));

        Assert.Equal(2, list.Index);
        Assert.Equal(202, list.ActiveId);
    }

    [Fact]
    public void TheSelectionScrollsIntoViewWhenItLeavesTheWindow()
    {
        ScrollListBoxTask list = Filled(count: 20);

        // The box is four rows tall, so selecting the tenth has to scroll.
        Assert.True(list.SelectId(210));
        Assert.Equal(10, list.Index);
        Assert.InRange(10 - list.ScrollOffset, 0, list.VisibleRows - 1);
    }

    [Fact]
    public void AClickIsResolvedAgainstTheScrolledView()
    {
        ScrollListBoxTask list = Filled(count: 20);
        list.ScrollOffset = 8;

        // The second visible row is item 9, not item 1.
        Click(list, Row(list, 1));

        Assert.Equal(9, list.Index);
        Assert.Equal(209, list.ActiveId);
    }

    private static int Row(ScrollListBoxTask list, int visibleRow) =>
        list.LayoutY + (visibleRow * RowHeight) + 1;

    private static void Click(ScrollListBoxTask list, int y)
    {
        MenuPointer pointer = new();

        // A press latches, and the hit-test is what hands the control the pointer.
        pointer.Set(list.LayoutX + 4, y, buttonDown: true);
        list.HitTest(pointer);

        pointer.Set(list.LayoutX + 4, y, buttonDown: false);
        list.HitTest(pointer);
    }

    private static ScrollListBoxTask Filled(int count = 4)
    {
        ScrollListBoxTask list = new()
        {
            LayoutX = 100,
            LayoutY = 50,
            LayoutWidth = 120,
            LayoutHeight = (RowHeight * 4) + 4,
            RowHeight = RowHeight,
        };

        list.SetItems(Enumerable.Range(0, count).Select(i => new MenuListItem(200 + i, $"Item {i}")));
        return list;
    }
}
