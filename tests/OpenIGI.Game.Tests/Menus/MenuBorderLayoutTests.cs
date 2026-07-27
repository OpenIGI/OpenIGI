using OpenIGI.Game.Menus;
using Xunit;

namespace OpenIGI.Game.Tests.Menus;

public sealed class MenuBorderLayoutTests
{
    [Fact]
    public void PieceNumbersRunClockwiseFromTheTopLeft()
    {
        // The enum value is one less than the number in the file name.
        Assert.Equal("win1_1.spr", MenuLayout.GetFramePieceName("win1", (int)MenuBorderPiece.TopLeft + 1));
        Assert.Equal("win1_4.spr", MenuLayout.GetFramePieceName("win1", (int)MenuBorderPiece.Right + 1));
        Assert.Equal("win1_8.spr", MenuLayout.GetFramePieceName("win1", (int)MenuBorderPiece.Left + 1));
    }

    [Fact]
    public void CornersSitAtTheFourCornersAndEdgesTileBetweenThem()
    {
        // Corners 10x10, edges 10 wide/tall, in a 50x50 frame.
        var placements = MenuBorderLayout.Compute(50, 50, Uniform(10));

        Assert.Equal(new MenuBorderPlacement(MenuBorderPiece.TopLeft, 0, 0), placements[0]);
        Assert.Contains(new MenuBorderPlacement(MenuBorderPiece.TopRight, 40, 0), placements);

        // The bottom edge is pulled back by its own height so it sits inside the rectangle.
        Assert.Contains(new MenuBorderPlacement(MenuBorderPiece.BottomLeft, 0, 40), placements);
        Assert.Contains(new MenuBorderPlacement(MenuBorderPiece.BottomRight, 40, 40), placements);

        // Three tiles across the top between the two corners.
        Assert.Equal(3, placements.Count(p => p.Piece == MenuBorderPiece.Top));
        Assert.Equal(3, placements.Count(p => p.Piece == MenuBorderPiece.Bottom));
    }

    [Fact]
    public void TheRightEdgeIsPulledBackInsideTheRectangle()
    {
        var placements = MenuBorderLayout.Compute(50, 50, Uniform(10));

        Assert.All(
            placements.Where(p => p.Piece == MenuBorderPiece.Right),
            p => Assert.Equal(40, p.X));

        Assert.All(
            placements.Where(p => p.Piece == MenuBorderPiece.Left),
            p => Assert.Equal(0, p.X));
    }

    [Fact]
    public void VerticalRunsSpanOnlyTheGapBetweenTheHorizontalEdges()
    {
        // 50 tall, 10-tall edges top and bottom, leaves 30 = three tiles.
        var placements = MenuBorderLayout.Compute(50, 50, Uniform(10));

        Assert.Equal(3, placements.Count(p => p.Piece == MenuBorderPiece.Left));
        Assert.Equal([10, 20, 30], placements.Where(p => p.Piece == MenuBorderPiece.Left).Select(p => p.Y));
    }

    [Fact]
    public void EdgesTileRatherThanStretchAndLeaveAGapWhenTheyDoNotDivide()
    {
        // 55 wide with 10-wide pieces: corners take 20, leaving 35, of which three tiles cover 30.
        var placements = MenuBorderLayout.Compute(55, 50, Uniform(10));

        Assert.Equal(3, placements.Count(p => p.Piece == MenuBorderPiece.Top));

        // The far corner still lands where the run left off, not at the frame's right edge — the gap
        // is real and is what the original draws.
        MenuBorderPlacement topRight = placements.First(p => p.Piece == MenuBorderPiece.TopRight);
        Assert.Equal(40, topRight.X);
    }

    [Fact]
    public void AnAbsentEdgeSkipsItsRunButStillPlacesTheFarCorner()
    {
        (int, int)[] sizes = Uniform(10);
        sizes[(int)MenuBorderPiece.Top] = (0, 0);

        var placements = MenuBorderLayout.Compute(50, 50, sizes);

        Assert.DoesNotContain(placements, p => p.Piece == MenuBorderPiece.Top);

        // Without a run to advance the cursor the original jumps straight to the far corner.
        Assert.Equal(40, placements.First(p => p.Piece == MenuBorderPiece.TopRight).X);
    }

    [Fact]
    public void AnAbsentPieceIsSimplyNotDrawn()
    {
        (int, int)[] sizes = Uniform(10);
        sizes[(int)MenuBorderPiece.TopLeft] = (0, 0);
        sizes[(int)MenuBorderPiece.Left] = (0, 0);

        var placements = MenuBorderLayout.Compute(50, 50, sizes);

        Assert.DoesNotContain(placements, p => p.Piece == MenuBorderPiece.TopLeft);
        Assert.DoesNotContain(placements, p => p.Piece == MenuBorderPiece.Left);
        Assert.NotEmpty(placements);
    }

    [Fact]
    public void TheEdgeHeightIsTheTallestOfItsThreePieces()
    {
        // A taller corner pushes the vertical runs further down.
        (int, int)[] sizes = Uniform(10);
        sizes[(int)MenuBorderPiece.TopLeft] = (10, 16);

        var placements = MenuBorderLayout.Compute(50, 50, sizes);

        Assert.Equal(16, placements.Where(p => p.Piece == MenuBorderPiece.Left).Min(p => p.Y));
    }

    [Fact]
    public void AFrameTooSmallForItsBorderProducesNoVerticalRun()
    {
        var placements = MenuBorderLayout.Compute(50, 20, Uniform(10));

        Assert.DoesNotContain(placements, p => p.Piece == MenuBorderPiece.Left);
        Assert.DoesNotContain(placements, p => p.Piece == MenuBorderPiece.Right);
        Assert.Contains(placements, p => p.Piece == MenuBorderPiece.TopLeft);
    }

    [Fact]
    public void TheWrongNumberOfPieceSizesIsRejected() =>
        Assert.Throws<ArgumentException>(() => MenuBorderLayout.Compute(50, 50, Uniform(10)[..4]));

    private static (int Width, int Height)[] Uniform(int size)
    {
        (int, int)[] sizes = new (int, int)[MenuLayout.FramePieceCount];
        Array.Fill(sizes, (size, size));
        return sizes;
    }
}
