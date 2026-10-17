using OpenIGI.Engine.Tasks;
using Xunit;
using OpenIGI.Game.Igi1.Menus;

namespace OpenIGI.Game.Tests.Menus;

public sealed class MenuLineUpTests
{
    [Fact]
    public void SpacingDefaultsToFourOnBothAxes()
    {
        // MenuWindow's constructor writes 4 to both; everything else it touches starts at zero.
        MenuFrameTask frame = new();

        Assert.Equal(MenuWindowTask.DefaultSpacing, frame.LayoutSpacingX);
        Assert.Equal(4, frame.LayoutSpacingY);
        Assert.Equal(0, frame.LayoutX);
        Assert.Equal(MenuLineUpMode.None, frame.LineUp);
        Assert.False(frame.CentreHorizontally);
    }

    [Fact]
    public void MenuTextStartsCentredAtTheConstructorPosition()
    {
        MenuTextTask text = new();

        Assert.Equal(300, text.LayoutX);
        Assert.Equal(230, text.LayoutY);
        Assert.True(text.CentreHorizontally);
        Assert.True(text.CentreVertically);
    }

    [Fact]
    public void NoneLeavesChildrenWhereTheyAre()
    {
        MenuFrameTask frame = Frame(MenuLineUpMode.None, 100, 50, 400, 300);
        MenuTextTask a = AddChild(frame, 20, 10);
        a.LayoutX = 7;
        a.LayoutY = 9;
        a.CentreHorizontally = false;
        a.CentreVertically = false;

        frame.Arrange();

        Assert.Equal((7, 9), (a.LayoutX, a.LayoutY));
    }

    [Fact]
    public void VerticalTopStacksFromTheTopEdgeOneSpacingIn()
    {
        MenuFrameTask frame = Frame(MenuLineUpMode.VerticalTop, 100, 50, 400, 300, spacingY: 10);
        MenuTextTask a = AddChild(frame, 20, 30);
        MenuTextTask b = AddChild(frame, 20, 40);

        frame.Arrange();

        // cursor = y + step, then advances by step + the child's own height.
        Assert.Equal(60, a.LayoutY);
        Assert.Equal(100, b.LayoutY);
    }

    [Fact]
    public void VerticalCentreSharesTheLeftoverHeightEvenly()
    {
        // step = (height - totalHeight) / (count + 1) = (300 - 100) / 3 = 66
        MenuFrameTask frame = Frame(MenuLineUpMode.VerticalCentre, 0, 0, 400, 300);
        MenuTextTask a = AddChild(frame, 20, 40);
        MenuTextTask b = AddChild(frame, 20, 60);

        frame.Arrange();

        Assert.Equal(66, a.LayoutY);
        Assert.Equal(172, b.LayoutY);
    }

    [Fact]
    public void VerticalBottomEndsAgainstTheBottomEdge()
    {
        // cursor = y + height - count*step - totalHeight = 0 + 300 - 2*10 - 100 = 180
        MenuFrameTask frame = Frame(MenuLineUpMode.VerticalBottom, 0, 0, 400, 300, spacingY: 10);
        MenuTextTask a = AddChild(frame, 20, 40);
        MenuTextTask b = AddChild(frame, 20, 60);

        frame.Arrange();

        Assert.Equal(180, a.LayoutY);
        Assert.Equal(230, b.LayoutY);
        Assert.Equal(300, b.LayoutY + b.LayoutHeight + 10);
    }

    [Fact]
    public void HorizontalLeftLaysOutFromTheLeftEdge()
    {
        MenuFrameTask frame = Frame(MenuLineUpMode.HorizontalLeft, 100, 0, 400, 300, spacingX: 8);
        MenuTextTask a = AddChild(frame, 30, 20);
        MenuTextTask b = AddChild(frame, 50, 20);

        frame.Arrange();

        Assert.Equal(108, a.LayoutX);
        Assert.Equal(146, b.LayoutX);
    }

    [Fact]
    public void HorizontalCentreSharesTheLeftoverWidthEvenly()
    {
        // step = (400 - 80) / 3 = 106
        MenuFrameTask frame = Frame(MenuLineUpMode.HorizontalCentre, 0, 0, 400, 300);
        MenuTextTask a = AddChild(frame, 30, 20);
        MenuTextTask b = AddChild(frame, 50, 20);

        frame.Arrange();

        Assert.Equal(106, a.LayoutX);
        Assert.Equal(242, b.LayoutX);
    }

    [Fact]
    public void HorizontalRightEndsAgainstTheRightEdge()
    {
        // cursor = 0 + 400 - 2*8 - 80 = 304
        MenuFrameTask frame = Frame(MenuLineUpMode.HorizontalRight, 0, 0, 400, 300, spacingX: 8);
        MenuTextTask a = AddChild(frame, 30, 20);
        MenuTextTask b = AddChild(frame, 50, 20);

        frame.Arrange();

        Assert.Equal(304, a.LayoutX);
        Assert.Equal(342, b.LayoutX);
    }

    [Fact]
    public void VerticalFillOutSpreadsBetweenTheSpacingMargins()
    {
        // cursor = y + spacing = 10; step = (300 - 20 - 100) / (3 - 1) = 90
        MenuFrameTask frame = Frame(MenuLineUpMode.VerticalFillOut, 0, 0, 400, 300, spacingY: 10);
        MenuTextTask a = AddChild(frame, 20, 30);
        MenuTextTask b = AddChild(frame, 20, 30);
        MenuTextTask c = AddChild(frame, 20, 40);

        frame.Arrange();

        Assert.Equal(10, a.LayoutY);
        Assert.Equal(130, b.LayoutY);
        Assert.Equal(250, c.LayoutY);
    }

    [Fact]
    public void FillOutWithASingleChildDoesNotDivideByZero()
    {
        // The original clamps the divisor to at least one rather than guarding the count.
        MenuFrameTask frame = Frame(MenuLineUpMode.VerticalFillOut, 0, 0, 400, 300, spacingY: 10);
        MenuTextTask only = AddChild(frame, 20, 30);

        frame.Arrange();

        Assert.Equal(10, only.LayoutY);
    }

    [Fact]
    public void AnItemCentresOnTheAxisItsParentDoesNotLayOut()
    {
        // This is what makes a vertical column of entries come out horizontally centred: MenuText sets
        // both centre flags, and the frame only places it on Y.
        MenuFrameTask frame = Frame(MenuLineUpMode.VerticalTop, 100, 50, 400, 300, spacingY: 10);
        MenuTextTask a = AddChild(frame, 60, 30);

        frame.Arrange();

        Assert.Equal(100 + ((400 - 60) / 2), a.LayoutX);
        Assert.Equal(60, a.LayoutY);
    }

    [Fact]
    public void AnItemWithoutTheCentreFlagIsLeftAloneOnThatAxis()
    {
        MenuFrameTask frame = Frame(MenuLineUpMode.VerticalTop, 100, 50, 400, 300);
        MenuTextTask a = AddChild(frame, 60, 30);
        a.CentreHorizontally = false;
        a.LayoutX = 12;

        frame.Arrange();

        Assert.Equal(12, a.LayoutX);
    }

    [Fact]
    public void ArrangeRecursesSoNestedFramesLayOutAfterBeingPlaced()
    {
        MenuFrameTask outer = Frame(MenuLineUpMode.VerticalTop, 0, 0, 400, 300, spacingY: 10);
        MenuFrameTask inner = NestedFrame(outer, MenuLineUpMode.HorizontalLeft, 200, 100, spacingX: 5);
        MenuTextTask leaf = AddChild(inner, 20, 20);

        outer.Arrange();

        // The inner frame is placed first, and its own line-up then runs against its new position.
        Assert.Equal(10, inner.LayoutY);
        Assert.Equal(inner.LayoutX + 5, leaf.LayoutX);
    }

    [Fact]
    public void DivisionsTruncateSoLeftoverPixelsLandAtTheEnd()
    {
        // (300 - 0) / 4 = 75 exactly; (301 - 0) / 4 = 75 too, with the odd pixel simply unused.
        MenuFrameTask frame = Frame(MenuLineUpMode.VerticalCentre, 0, 0, 400, 301);
        MenuTextTask a = AddChild(frame, 10, 0);
        MenuTextTask b = AddChild(frame, 10, 0);
        MenuTextTask c = AddChild(frame, 10, 0);

        frame.Arrange();

        Assert.Equal(75, a.LayoutY);
        Assert.Equal(150, b.LayoutY);
        Assert.Equal(225, c.LayoutY);
    }

    // Built through the real TaskTree rather than by hand, so the tests exercise the same attachment
    // path the menu script does.
    private readonly TaskTypeRegistry _types = new();
    private readonly TaskTree _tree;

    public MenuLineUpTests()
    {
        EngineTaskTypes.Register(_types);
        MenuTaskTypes.RegisterAll(_types);
        _tree = new TaskTree(_types);
    }

    private MenuFrameTask Frame(
        MenuLineUpMode mode,
        int x,
        int y,
        int width,
        int height,
        int spacingX = MenuWindowTask.DefaultSpacing,
        int spacingY = MenuWindowTask.DefaultSpacing)
    {
        MenuFrameTask frame = (MenuFrameTask)_tree.New(_tree.Root, _types.Get("MenuFrame"));
        frame.LineUp = mode;
        frame.LayoutX = x;
        frame.LayoutY = y;
        frame.LayoutWidth = width;
        frame.LayoutHeight = height;
        frame.LayoutSpacingX = spacingX;
        frame.LayoutSpacingY = spacingY;
        return frame;
    }

    private MenuFrameTask NestedFrame(MenuWindowTask parent, MenuLineUpMode mode, int width, int height, int spacingX)
    {
        MenuFrameTask frame = (MenuFrameTask)_tree.New(parent, _types.Get("MenuFrame"));
        frame.LineUp = mode;
        frame.LayoutWidth = width;
        frame.LayoutHeight = height;
        frame.LayoutSpacingX = spacingX;
        return frame;
    }

    private MenuTextTask AddChild(MenuWindowTask parent, int width, int height)
    {
        MenuTextTask child = (MenuTextTask)_tree.New(parent, _types.Get("MenuText"));
        child.LayoutWidth = width;
        child.LayoutHeight = height;
        return child;
    }
}
