using OpenIGI.Core.Graphics;
using OpenIGI.Desktop;
using OpenIGI.Rendering;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// Guards the facts about the storm's drop sheet that the renderer's arithmetic assumes.
/// </summary>
/// <remarks>
/// <para>
/// The sheet is the one part of the storm whose shape is not decided by this repository: it is two
/// strips of painted streaks, and every constant that addresses them — ten cells across, the sharp
/// strip on top, a defocused streak that fills far more of its cell than a sharp one — is read off the
/// images rather than declared by the code. A re-export that changed any of those would not throw and
/// would not look obviously wrong in a still; it would quietly draw ten drops with the wrong tenth of
/// the sheet. These tests are what makes that loud.
/// </para>
/// <para>
/// The sheets ship with the game, so every one of these runs. The renderer still draws streaks it
/// generates itself when a sheet cannot be read, and most of what follows — the colour, the ten cells,
/// the two focus levels — holds for that fallback too; the size and the blank seam can only be true of
/// the painted images, so those two are also what would catch the sheets going missing.
/// </para>
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

/// <summary>
/// Guards the splash sheet's layout, which the renderer's frame arithmetic assumes.
/// </summary>
/// <remarks>
/// The shipped image is eight frames four across and two down, with three different animations packed
/// one per colour channel — that is what the reference's sprite renderer declares and what the image
/// itself measures. Reading it as eight frames across by four strips down, which this port did, slices
/// every frame into quarters and draws a corner of one splash where a whole one belongs.
/// </remarks>
public sealed class StormSplashSheetTests
{
    [Fact]
    public void SplashSheet_IsFourAcrossAndOneStripPerVariantPlusTheBlob()
    {
        Assert.Equal(4, StormRenderer.SplashColumns);
        Assert.Equal(8, StormRenderer.SplashFrames);
        Assert.Equal(2, StormRenderer.SplashFrameRows);
        Assert.Equal(3, StormRenderer.SplashVariants);
        Assert.Equal(7, StormRenderer.SplashRows);
        Assert.Equal(6, StormRenderer.SplashBlobRow);

        (Bgra32[] pixels, int width, int height) = StormRenderer.BuildSplashSheet();
        Assert.Equal(width * height, pixels.Length);

        // Square cells, which is what a 4x2 read of the shipped 512x256 gives and what an 8x4 read
        // does not.
        Assert.Equal(width / StormRenderer.SplashColumns, height / StormRenderer.SplashRows);
    }

    [Fact]
    public void SplashSheet_UnpacksThreeDifferentAnimationsFromTheThreeChannels()
    {
        (Bgra32[] pixels, int width, int height) = StormRenderer.BuildSplashSheet();
        int cell = height / StormRenderer.SplashRows;

        // The mean alpha of each variant's own pair of strips. The three channels of the shipped image
        // carry visibly different amounts of ink, so three unpacked variants must too — if any pair
        // matches another exactly, the same channel has been read three times.
        double[] means = new double[StormRenderer.SplashVariants];
        for (int variant = 0; variant < StormRenderer.SplashVariants; variant++)
        {
            long sum = 0;
            int top = variant * StormRenderer.SplashFrameRows * cell;
            int rows = StormRenderer.SplashFrameRows * cell;
            for (int y = top; y < top + rows; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    sum += pixels[(y * width) + x].A;
                }
            }

            means[variant] = sum / (double)(rows * width);
            Assert.True(means[variant] > 1.0, $"variant {variant} is empty");
        }

        for (int a = 0; a < means.Length; a++)
        {
            for (int b = a + 1; b < means.Length; b++)
            {
                Assert.True(
                    Math.Abs(means[a] - means[b]) > 0.5,
                    $"variants {a} and {b} carry the same ink ({means[a]:0.00} vs {means[b]:0.00}), "
                        + "so one channel has been read for all three");
            }
        }
    }

    [Fact]
    public void SplashSheet_GivesEveryFrameItsOwnCellInReadingOrder()
    {
        // The mapping the builder writes and the renderer reads must be the one inverse of the other:
        // eight frames, four across, filling the first strip before the second.
        for (int frame = 0; frame < StormRenderer.SplashFrames; frame++)
        {
            int column = frame % StormRenderer.SplashColumns;
            int row = frame / StormRenderer.SplashColumns;
            Assert.InRange(column, 0, StormRenderer.SplashColumns - 1);
            Assert.InRange(row, 0, StormRenderer.SplashFrameRows - 1);
            Assert.Equal(frame, (row * StormRenderer.SplashColumns) + column);
        }
    }
}

/// <summary>
/// Holds the splash to the size and the variety the reference gives it.
/// </summary>
/// <remarks>
/// A splash sprite there is two to four CENTIMETRES — <c>Sprite Size Min (2,2)</c> to
/// <c>Sprite Size Max (2.5,4)</c> in its own units, which are centimetres — and it is flipped
/// horizontally at random, which is what stops a shower of them reading as one stamp repeated. This
/// port drew them at a fifth of a metre, five to ten times over, which is most of why they were both
/// too big and too bright: a splash covering twenty-five times the screen area emits twenty-five times
/// the light, and a 128-texel cell magnified that far has nothing left to be smooth with.
/// </remarks>
public sealed class StormSplashSizeTests
{
    [Fact]
    public void SplashSprite_IsTheReferencesTwoToFourCentimetres()
    {
        PrecipitationSettings settings = new();

        // The renderer draws the quad at +/- radius, over the 0.7..1 band the spawn gives it.
        float smallest = settings.SplashRadiusMetres * 0.7f * 2f;
        float largest = settings.SplashRadiusMetres * 2f;

        Assert.InRange(smallest, 0.018f, 0.032f);
        Assert.InRange(largest, 0.030f, 0.045f);
    }

    [Fact]
    public void SplashColumn_FlipsHorizontallyWhenAsked()
    {
        (float u0, float u1) = StormRenderer.SplashColumnUv(1, flip: false);
        (float f0, float f1) = StormRenderer.SplashColumnUv(1, flip: true);

        // The same cell, read the other way round: a mirrored splash, not a different one.
        Assert.Equal(u0, f1, 6);
        Assert.Equal(u1, f0, 6);
        Assert.True(u1 > u0, "an unflipped cell must run left to right");
        Assert.True(f1 < f0, "a flipped cell must run right to left");
    }

    [Fact]
    public void SplashColumn_CoversItsOwnCellAndNoOther()
    {
        for (int column = 0; column < StormRenderer.SplashColumns; column++)
        {
            (float u0, float u1) = StormRenderer.SplashColumnUv(column, flip: false);
            Assert.Equal(column / (float)StormRenderer.SplashColumns, u0, 6);
            Assert.Equal((column + 1) / (float)StormRenderer.SplashColumns, u1, 6);
        }
    }
}

/// <summary>
/// Holds the storm's sprite sheets to a description the device can actually upload.
/// </summary>
/// <remarks>
/// Written after asking for zero mip levels took the whole storm off the screen. Zero is not a way to
/// ask for a generated chain — <see cref="TextureDescription"/> counts levels, requires at least one,
/// and makes level zero out of range when there are none, so the upload throws before a texel is
/// written. The three sheets are resolved together inside one try at the top of the draw, so the rain,
/// the splashes and the curtains all vanished together with the one that threw.
/// </remarks>
public sealed class StormSheetTextureTests
{
    [Fact]
    public void SheetTexture_AsksForALevelThatCanBeUploaded()
    {
        TextureDescription description = StormRenderer.SheetTexture(512, 1024);

        Assert.True(description.MipLevels >= 1, "a sheet must have at least one mip level");

        // The exact call the upload makes for every level it is told exists. With no levels at all this
        // throws for level zero, which is what took the storm off the screen.
        for (int level = 0; level < description.MipLevels; level++)
        {
            _ = description.GetMipSize(level);
        }
    }

    [Theory]
    [InlineData(512, 1024)]
    [InlineData(512, 896)]
    [InlineData(512, 512)]
    public void SheetTexture_DescribesTheSheetItWasGiven(int width, int height)
    {
        TextureDescription description = StormRenderer.SheetTexture(width, height);

        Assert.Equal(width, description.Width);
        Assert.Equal(height, description.Height);
        Assert.Equal(TextureAddressMode.Clamp, description.AddressMode);
    }
}
