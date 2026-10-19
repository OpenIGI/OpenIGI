using OpenIGI.Engine.Tasks;
using Xunit;
using OpenIGI.Game.Menus;

namespace OpenIGI.Game.Tests.Menus;

public sealed class MenuSelectionTests
{
    private readonly TaskTypeRegistry _types = new();
    private readonly TaskTree _tree;

    public MenuSelectionTests()
    {
        EngineTaskTypes.Register(_types);
        MenuTaskTypes.RegisterAll(_types);
        _tree = new TaskTree(_types);
    }

    [Fact]
    public void OnlyInteractiveItemsAreSelectable()
    {
        // A caption has no action, so the arrow keys must never land on it.
        MenuFrameTask frame = Frame();
        MenuTextTask caption = Entry(frame, action: null);
        MenuTextTask a = Entry(frame);
        MenuTextTask b = Entry(frame);

        MenuSelection selection = new();
        selection.Rebuild(frame);

        Assert.Equal([a, b], selection.Items);
        Assert.DoesNotContain(caption, selection.Items);
    }

    [Fact]
    public void ExactlyOneItemCarriesTheHighlight()
    {
        MenuFrameTask frame = Frame();
        MenuTextTask a = Entry(frame);
        MenuTextTask b = Entry(frame);

        MenuSelection selection = new();
        selection.Rebuild(frame);

        Assert.True(a.IsHighlighted);
        Assert.False(b.IsHighlighted);

        selection.MoveNext();
        Assert.False(a.IsHighlighted);
        Assert.True(b.IsHighlighted);
    }

    [Fact]
    public void MovementWrapsAtBothEnds()
    {
        MenuFrameTask frame = Frame();
        MenuTextTask a = Entry(frame);
        MenuTextTask b = Entry(frame);
        MenuTextTask c = Entry(frame);

        MenuSelection selection = new();
        selection.Rebuild(frame);

        selection.MovePrevious();
        Assert.Same(c, selection.Selected);

        selection.MoveNext();
        Assert.Same(a, selection.Selected);

        selection.MoveNext();
        selection.MoveNext();
        selection.MoveNext();
        Assert.Same(a, selection.Selected);
        _ = b;
    }

    [Fact]
    public void TheMouseAndTheKeyboardShareOneSelection()
    {
        // Hovering calls the same setter the arrow keys do, so there is never a keyboard highlight in
        // one place and a mouse highlight in another.
        MenuFrameTask frame = Frame();
        MenuTextTask a = Entry(frame);
        MenuTextTask b = Entry(frame);

        MenuSelection selection = new();
        selection.Rebuild(frame);

        Assert.True(selection.SelectItem(b));
        Assert.Same(b, selection.Selected);
        Assert.True(b.IsHighlighted);
        Assert.False(a.IsHighlighted);
    }

    [Fact]
    public void ReselectingTheSameItemIsSilent()
    {
        // Which is why sliding the pointer around inside one entry does not chatter.
        MenuFrameTask frame = Frame();
        MenuTextTask a = Entry(frame);
        Entry(frame);

        MenuSelection selection = new();
        selection.Rebuild(frame);

        int moves = 0;
        selection.Moved += (_, _) => moves++;

        Assert.False(selection.SelectItem(a));
        Assert.Equal(0, moves);
    }

    [Fact]
    public void MovingRaisesTheEventTheHostAnswersWithASound()
    {
        MenuFrameTask frame = Frame();
        Entry(frame);
        Entry(frame);

        MenuSelection selection = new();
        selection.Rebuild(frame);

        int moves = 0;
        selection.Moved += (_, _) => moves++;

        selection.MoveNext();
        selection.MovePrevious();
        Assert.Equal(2, moves);
    }

    [Theory]
    [InlineData(MenuInputEvent.Next)]
    [InlineData(MenuInputEvent.Previous)]
    public void MovementCodesAreHandledByTheManagerAndNotForwarded(MenuInputEvent input)
    {
        MenuFrameTask frame = Frame();
        Entry(frame);
        Entry(frame);

        MenuSelection selection = new();
        selection.Rebuild(frame);

        Assert.Equal(MenuInputEvent.None, selection.Handle(input));
    }

    [Theory]
    [InlineData(MenuInputEvent.Left)]
    [InlineData(MenuInputEvent.Right)]
    [InlineData(MenuInputEvent.Enter)]
    public void TheOtherCodesAreForwardedToTheSelectedItem(MenuInputEvent input)
    {
        // A slider or a toggle takes its input this way.
        MenuFrameTask frame = Frame();
        Entry(frame);

        MenuSelection selection = new();
        selection.Rebuild(frame);

        Assert.Equal(input, selection.Handle(input));
    }

    [Fact]
    public void NothingIsForwardedWhenNothingIsSelected()
    {
        MenuSelection selection = new();
        selection.Rebuild(Frame());

        Assert.Null(selection.Selected);
        Assert.Equal(MenuInputEvent.None, selection.Handle(MenuInputEvent.Left));
        Assert.False(selection.MoveNext());
    }

    [Fact]
    public void SwapKeysExchangesTheTwoDirectionPairs()
    {
        // What DialogWindow's SwapKeys parameter does, so a horizontal OK/Cancel dialog reads the
        // arrow keys the way it is laid out.
        Assert.Equal(MenuInputEvent.Right, MenuSelection.SwapKeys(MenuInputEvent.Next));
        Assert.Equal(MenuInputEvent.Left, MenuSelection.SwapKeys(MenuInputEvent.Previous));
        Assert.Equal(MenuInputEvent.Next, MenuSelection.SwapKeys(MenuInputEvent.Right));
        Assert.Equal(MenuInputEvent.Previous, MenuSelection.SwapKeys(MenuInputEvent.Left));
        Assert.Equal(MenuInputEvent.Enter, MenuSelection.SwapKeys(MenuInputEvent.Enter));
    }

    [Fact]
    public void TheEventCodesAreTheOnesTheOriginalUses()
    {
        // Pinned by InputBox_Update's switch (0x41D450), which is the only place in the binary that
        // names all of them at once. Enter was -8 here and Escape -11; both were wrong, and -7 had
        // been guessed as an "alternate action" it never was.
        Assert.Equal(-1, (int)MenuInputEvent.Left);
        Assert.Equal(-2, (int)MenuInputEvent.Right);
        Assert.Equal(-3, (int)MenuInputEvent.Previous);
        Assert.Equal(-4, (int)MenuInputEvent.Next);
        Assert.Equal(-5, (int)MenuInputEvent.Backspace);
        Assert.Equal(-6, (int)MenuInputEvent.Delete);
        Assert.Equal(-7, (int)MenuInputEvent.Enter);
        Assert.Equal(-8, (int)MenuInputEvent.Escape);
        Assert.Equal(-9, (int)MenuInputEvent.Home);
        Assert.Equal(-10, (int)MenuInputEvent.End);
    }

    [Fact]
    public void RebuildingClearsTheHighlightFromTheOldItems()
    {
        MenuFrameTask frame = Frame();
        MenuTextTask a = Entry(frame);

        MenuSelection selection = new();
        selection.Rebuild(frame);
        Assert.True(a.IsHighlighted);

        selection.Rebuild(Frame());
        Assert.False(a.IsHighlighted);
    }

    private MenuFrameTask Frame() => (MenuFrameTask)_tree.New(_tree.Root, _types.Get("MenuFrame"));

    private MenuTextTask Entry(MenuWindowTask parent, string? action = "MenuManager_PopScreen(FALSE)")
    {
        MenuTextTask entry = (MenuTextTask)_tree.New(parent, _types.Get("MenuText"));
        entry.OnClickScript = action ?? string.Empty;
        return entry;
    }
}
