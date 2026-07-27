using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Menus;
using Xunit;

namespace OpenIGI.Game.Tests.Menus;

public sealed class MenuHitTestTests
{
    private readonly TaskTypeRegistry _types = new();
    private readonly TaskTree _tree;

    public MenuHitTestTests()
    {
        EngineTaskTypes.Register(_types);
        MenuTaskTypes.RegisterAll(_types);
        _tree = new TaskTree(_types);
    }

    [Fact]
    public void AnEntryWithNoActionTakesNoPart()
    {
        // The flag at +76: MenuText_Prepare sets it only when the entry has an On Click Script, and
        // MenuFrame_Update tests it before hit-testing. It is what stops a container swallowing a
        // click meant for one of its children.
        MenuTextTask caption = Item(100, 100, 50, 20);
        caption.OnClickScript = string.Empty;
        Assert.False(caption.IsInteractive);

        MenuPointer pointer = new();
        pointer.Update(125, 110, 640, 480, buttonDown: true);
        Assert.False(caption.HitTest(pointer));
        Assert.False(caption.IsActive);
    }

    [Fact]
    public void AFrameTakesPartOnlyWhenTheScriptMarksItSelectable()
    {
        MenuFrameTask frame = (MenuFrameTask)_tree.New(_tree.Root, _types.Get("MenuFrame"));
        Assert.False(frame.IsInteractive);

        frame.IsSelectable = true;
        Assert.True(frame.IsInteractive);
    }

    [Fact]
    public void ThePointerConvertsOutOfScreenSpaceIntoAuthoredCoordinates()
    {
        // The exact inverse of the centring items are drawn with, so the hit-test always works in
        // 640x480 whatever the back buffer is.
        MenuPointer pointer = new();
        pointer.Update(492, 374, 1024, 768, buttonDown: false);

        Assert.Equal(300, pointer.X);
        Assert.Equal(230, pointer.Y);
    }

    [Fact]
    public void EdgesAreExclusiveOnEverySide()
    {
        MenuTextTask item = Item(100, 100, 50, 20);
        MenuPointer pointer = new();

        Assert.False(Over(item, pointer, 100, 110));  // left edge
        Assert.False(Over(item, pointer, 110, 100));  // top edge
        Assert.False(Over(item, pointer, 150, 110));  // right edge
        Assert.False(Over(item, pointer, 110, 120));  // bottom edge
        Assert.True(Over(item, pointer, 101, 101));
        Assert.True(Over(item, pointer, 149, 119));
    }

    [Fact]
    public void AClickCompletesOnReleaseInsideNotOnPress()
    {
        MenuTextTask item = Item(100, 100, 50, 20);
        MenuPointer pointer = new();

        pointer.Update(125, 110, 640, 480, buttonDown: true);
        Assert.False(item.HitTest(pointer));
        Assert.True(item.IsHeld);

        pointer.Update(125, 110, 640, 480, buttonDown: false);
        Assert.True(item.HitTest(pointer));
        Assert.True(item.WasClicked);
    }

    [Fact]
    public void PressingThenDraggingAwayCancelsTheClick()
    {
        MenuTextTask item = Item(100, 100, 50, 20);
        MenuPointer pointer = new();

        pointer.Update(125, 110, 640, 480, buttonDown: true);
        item.HitTest(pointer);

        // Still held, but released outside: no click.
        pointer.Update(400, 400, 640, 480, buttonDown: false);
        Assert.False(item.HitTest(pointer));
        Assert.False(item.WasClicked);
    }

    [Fact]
    public void DraggingAwayAndBackKeepsTheItemLatched()
    {
        MenuTextTask item = Item(100, 100, 50, 20);
        MenuPointer pointer = new();

        pointer.Update(125, 110, 640, 480, buttonDown: true);
        item.HitTest(pointer);

        pointer.Update(400, 400, 640, 480, buttonDown: true);
        item.HitTest(pointer);
        Assert.True(item.IsHeld);

        pointer.Update(125, 110, 640, 480, buttonDown: false);
        Assert.True(item.HitTest(pointer));
    }

    [Fact]
    public void AnItemIsActiveUnderThePointerWithNoButtonHeld()
    {
        MenuTextTask item = Item(100, 100, 50, 20);
        MenuPointer pointer = new();

        pointer.Update(125, 110, 640, 480, buttonDown: false);
        item.HitTest(pointer);

        Assert.True(item.IsActive);
        Assert.False(item.IsHeld);
    }

    [Fact]
    public void AnItemUnderAButtonHeldFromElsewhereIsNotActive()
    {
        MenuTextTask other = Item(0, 0, 10, 10);
        MenuTextTask item = Item(100, 100, 50, 20);
        MenuPointer pointer = new();

        // Press on the other item, then move over this one without releasing.
        pointer.Update(5, 5, 640, 480, buttonDown: true);
        other.HitTest(pointer);
        item.HitTest(pointer);

        pointer.Update(125, 110, 640, 480, buttonDown: true);
        item.HitTest(pointer);

        Assert.False(item.IsActive);
    }

    [Fact]
    public void HitTestSubtreeReportsTheClickedDescendant()
    {
        MenuFrameTask frame = (MenuFrameTask)_tree.New(_tree.Root, _types.Get("MenuFrame"));
        MenuTextTask a = Item(0, 0, 10, 10, frame);
        MenuTextTask b = Item(100, 100, 50, 20, frame);
        MenuPointer pointer = new();

        pointer.Update(125, 110, 640, 480, buttonDown: true);
        frame.HitTestSubtree(pointer);

        pointer.Update(125, 110, 640, 480, buttonDown: false);
        Assert.Same(b, frame.HitTestSubtree(pointer));
        Assert.False(a.WasClicked);
    }

    private static bool Over(MenuWindowTask item, MenuPointer pointer, int x, int y)
    {
        pointer.Update(x, y, 640, 480, buttonDown: false);
        item.HitTest(pointer);
        return item.IsActive;
    }

    private MenuTextTask Item(int x, int y, int width, int height, MenuWindowTask? parent = null)
    {
        MenuTextTask item = (MenuTextTask)_tree.New(parent ?? _tree.Root, _types.Get("MenuText"));

        // An entry only takes part in interaction when it has an action, so every fixture needs one.
        item.OnClickScript = "MenuManager_PopScreen(FALSE)";
        item.LayoutX = x;
        item.LayoutY = y;
        item.LayoutWidth = width;
        item.LayoutHeight = height;
        return item;
    }
}
