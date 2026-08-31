using OpenIGI.Core.Graphics;
using OpenIGI.Desktop;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// Guards the facts about the storm's drop sheet that the renderer's arithmetic assumes.
/// </summary>
/// <remarks>
/// The sheet is the one part of the storm whose shape is not decided by this repository: it is two
/// strips of painted streaks, and every constant that addresses them — ten cells across, the sharp
/// strip on top, a defocused streak that fills far more of its cell than a sharp one — is read off the
/// images rather than declared by the code. A re-export that changed any of those would not throw and
/// would not look obviously wrong in a still; it would quietly draw ten drops with the wrong tenth of
/// the sheet. These tests are what makes that loud.
/// </remarks>
public sealed class StormDropSheetTests
{
    [Fact]
    public void DropSheet_StacksTheTwoStripsIntoOneImage()
    {
        (Bgra32[] pixels, int width, int height, float splitV) = StormRenderer.BuildDropSheet();

        Assert.Equal(width * height, pixels.Length);

        // The shipped images, not the generated stand-in — which is a different size, so this is also
        // what tells the rest of these tests they are reading the real sheets. A build that failed to
        // copy them still runs, still rains, and would otherwise pass every assertion below.
        Assert.Equal(512, width);
        Assert.Equal(1024, height);

        // Both strips are square and the same height, so the join lands exactly halfway — which is what
        // keeps the mip chain from ever boxing across it.
        Assert.Equal(0.5f, splitV, 3);
    }

    [Fact]
    public void DropSheet_CarriesTheShapeInAlphaAndLeavesTheColourWhite()
    {
        (Bgra32[] pixels, _, _, _) = StormRenderer.BuildDropSheet();

        // A quad whose colour is not white tints the drops before the vertex colour ever gets to, and a
        // quad whose RGB is not opaque white stamps its rectangle into the scene's alpha channel.
        Assert.All(pixels, pixel =>
        {
            Assert.Equal(255, pixel.R);
            Assert.Equal(255, pixel.G);
            Assert.Equal(255, pixel.B);
        });

        Assert.Contains(pixels, pixel => pixel.A > 200);
        Assert.Contains(pixels, pixel => pixel.A == 0);
    }

    [Fact]
    public void DropSheet_HoldsTenStreaksInEachStrip()
    {
        (Bgra32[] pixels, int width, int height, float splitV) = StormRenderer.BuildDropSheet();
        int split = (int)MathF.Round(splitV * height);

        for (int cell = 0; cell < StormRenderer.SheetCells; cell++)
        {
            Assert.True(
                Peak(pixels, width, Left(width, cell), Right(width, cell), 0, split) > 100,
                $"the sharp strip has no streak in cell {cell}");
            Assert.True(
                Peak(pixels, width, Left(width, cell), Right(width, cell), split, height) > 100,
                $"the defocused strip has no streak in cell {cell}");
        }
    }

    [Fact]
    public void DropSheet_KeepsTheSharpStreaksClearOfTheirCellEdges()
    {
        // What makes the ten addressable: a sharp streak is painted well inside its cell, so sampling
        // one cell never picks up the streak next door however the filter spreads.
        (Bgra32[] pixels, int width, int height, float splitV) = StormRenderer.BuildDropSheet();
        int split = (int)MathF.Round(splitV * height);

        for (int cell = 0; cell < StormRenderer.SheetCells; cell++)
        {
            int left = Left(width, cell);
            int right = Right(width, cell);

            Assert.Equal(0, Peak(pixels, width, left, left + 1, 0, split));
            Assert.Equal(0, Peak(pixels, width, right - 1, right, 0, split));
        }
    }

    [Fact]
    public void DropSheet_PutsTheSharpStripFirstAndTheFatOneSecond()
    {
        // The two strips are told apart by nothing but their order, and getting them the wrong way round
        // draws blurred blobs at distance and hairlines against the lens — which reads as a tuning
        // problem for as long as it takes to suspect the sheet.
        (Bgra32[] pixels, int width, int height, float splitV) = StormRenderer.BuildDropSheet();
        int split = (int)MathF.Round(splitV * height);

        float sharp = 0f;
        float soft = 0f;
        for (int cell = 0; cell < StormRenderer.SheetCells; cell++)
        {
            sharp += Fill(pixels, width, Left(width, cell), Right(width, cell), 0, split);
            soft += Fill(pixels, width, Left(width, cell), Right(width, cell), split, height);
        }

        sharp /= StormRenderer.SheetCells;
        soft /= StormRenderer.SheetCells;

        Assert.True(sharp < 0.6f, $"the top strip fills {sharp:P0} of its cells, which is not a sharp streak");
        Assert.True(soft > 0.7f, $"the bottom strip fills {soft:P0} of its cells, which is not a defocused one");
    }

    [Fact]
    public void DropSheet_LeavesTheSeamBetweenTheStripsEmpty()
    {
        // Nothing stops a bilinear tap straddling the join; what stops it mattering is that both strips
        // are painted short of their edges, so the rows either side of it are empty.
        (Bgra32[] pixels, int width, int height, float splitV) = StormRenderer.BuildDropSheet();
        int split = (int)MathF.Round(splitV * height);

        Assert.Equal(0, Peak(pixels, width, 0, width, split - 1, split));
        Assert.Equal(0, Peak(pixels, width, 0, width, split, split + 1));
    }

    private static int Left(int width, int cell) => (int)((long)cell * width / StormRenderer.SheetCells);

    private static int Right(int width, int cell) => (int)((long)(cell + 1) * width / StormRenderer.SheetCells);

    private static int Peak(Bgra32[] pixels, int width, int left, int right, int top, int bottom)
    {
        int peak = 0;
        for (int y = top; y < bottom; y++)
        {
            for (int x = left; x < right; x++)
            {
                peak = Math.Max(peak, pixels[(y * width) + x].A);
            }
        }

        return peak;
    }

    /// <summary>How much of a cell's width the streak in it occupies, from 0 to 1.</summary>
    private static float Fill(Bgra32[] pixels, int width, int left, int right, int top, int bottom)
    {
        int used = 0;
        for (int x = left; x < right; x++)
        {
            if (Peak(pixels, width, x, x + 1, top, bottom) > 8)
            {
                used++;
            }
        }

        return used / (float)Math.Max(right - left, 1);
    }
}
