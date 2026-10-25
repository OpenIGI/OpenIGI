using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering;

/// <summary>
/// Composes and uploads the storm's sprite sheets: the stacked drop strips, the channel-unpacked
/// splash animations with the droplet blob, and the far sheets' cloud.
/// </summary>
/// <remarks>
/// Each sheet is read from the executable's weather assets and falls back on a generated stand-in
/// at the same layout, so every index the storm addresses still means what it means.
/// </remarks>
internal static class PrecipitationSpriteSheets
{
    private const string LogCategory = "Storm";

    /// <summary>Stacks the two streak strips into the one sheet the drops are drawn from.</summary>
    /// <remarks>
    /// <para>
    /// The two images do not share a width — the defocused streaks are painted at twice the resolution,
    /// because a blurred shape needs the room a sharp one does not — so the narrower strip is expanded
    /// onto the wider one's grid by whole texels. Nearest, deliberately: an integer expansion of a
    /// sharp image is exact, and it is the render's own bilinear sampling that softens it back, whereas
    /// filtering here would blur the sharp strip twice.
    /// </para>
    /// <para>
    /// <b>Nothing bleeds across the seam.</b> Both strips are painted with black margins at top and
    /// bottom — the streaks stop about a twentieth of the image short of each edge — so the rows either
    /// side of the join are empty in both, and a bilinear tap that straddles it picks up nothing. The
    /// mip chain would not cross it either — both strips are the same height, so the join sits at the
    /// halfway row of every level and no box in a halving chain straddles the middle of a power-of-two
    /// axis — but there is no chain, and that was measured rather than assumed.
    /// </para>
    /// <para>
    /// <b>Why the sheet is not mipped, having been.</b> A streak is painted ten texels wide inside a
    /// fifty-texel cell and drawn across a handful of screen pixels, which is exactly the case mips
    /// exist for, and building one all but emptied the sky: a box filter spreads a thin bright line
    /// into its black surround and drops its peak in proportion, the vertex alpha then multiplies what
    /// is left, and the rain past twenty metres stopped being drawn at all. That is the physically
    /// honest answer to a sub-pixel streak and it is the wrong picture — the boost in
    /// <see cref="PrecipitationSettings.WidthBoostPerMetre"/> exists precisely to stop distant rain
    /// falling under a pixel. Level zero it is, at the cost of some sparkle at range, which the
    /// temporal filter takes most of. If this is revisited, it needs a chain that preserves the
    /// streak's peak rather than its integral, not a plain box.
    /// </para>
    /// </remarks>
    /// <param name="logger">Told about a sheet that could not be read; the fallback is drawn instead.</param>
    /// <returns>The sheet, and the v the defocused strip starts at.</returns>
    internal static (Bgra32[] Pixels, int Width, int Height, float SplitV) BuildDropSheet(IOpenIgiLogger? logger = null)
    {
        if (TryLoadMask(StormRenderer.FocusedSheetAsset, logger, out Bgra32[] focused, out int focusedWidth, out int focusedHeight)
            && TryLoadMask(StormRenderer.DefocusedSheetAsset, logger, out Bgra32[] soft, out int softWidth, out int softHeight))
        {
            int width = Math.Max(focusedWidth, softWidth);
            int height = focusedHeight + softHeight;
            Bgra32[] sheet = new Bgra32[width * height];
            Stretch(focused, focusedWidth, focusedHeight, sheet, width, 0);
            Stretch(soft, softWidth, softHeight, sheet, width, focusedHeight);
            return (sheet, width, height, focusedHeight / (float)height);
        }

        return BuildFallbackSheet();
    }

    /// <summary>Reads a greyscale mask and turns it into a white sprite whose alpha is the shape.</summary>
    private static bool TryLoadMask(
        string asset,
        IOpenIgiLogger? logger,
        out Bgra32[] pixels,
        out int width,
        out int height)
    {
        pixels = [];
        width = 0;
        height = 0;

        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "weather", asset);

        try
        {
            (Bgra32[] read, int w, int h) = PngReader.Read(path);
            if (w <= 0 || h <= 0 || read.Length < w * h)
            {
                return false;
            }

            for (int i = 0; i < read.Length; i++)
            {
                // The mask is painted in luminance, and the blue channel is as good a read of a grey
                // pixel as any. White times the vertex tint is what the fragment then multiplies.
                read[i] = new Bgra32(255, 255, 255, read[i].B);
            }

            pixels = read;
            width = w;
            height = h;
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            logger?.Warning(
                LogCategory,
                FormattableString.Invariant($"Storm sheet '{asset}' could not be read ({ex.Message}); drawing generated streaks."));
            return false;
        }
    }

    /// <summary>Copies one strip into the sheet, expanding it by whole texels to the sheet's width.</summary>
    private static void Stretch(
        ReadOnlySpan<Bgra32> source,
        int sourceWidth,
        int sourceHeight,
        Span<Bgra32> destination,
        int destinationWidth,
        int destinationTop)
    {
        for (int y = 0; y < sourceHeight; y++)
        {
            int sourceRow = y * sourceWidth;
            int destinationRow = (destinationTop + y) * destinationWidth;
            for (int x = 0; x < destinationWidth; x++)
            {
                int sx = Math.Min(x * sourceWidth / destinationWidth, sourceWidth - 1);
                destination[destinationRow + x] = source[sourceRow + sx];
            }
        }
    }

    /// <summary>Generated streaks, for when the sheets are missing.</summary>
    /// <remarks>
    /// <para>
    /// Kept because the alternative failure is silent and expensive: an asset that did not reach the output
    /// directory would otherwise mean no rain at all, which reads as a bug in the storm rather than as a
    /// missing file.
    /// </para>
    /// <para>
    /// Drawn as what a falling drop looks like through a shutter, not as a line of paint. A drop is a lens: it
    /// is almost clear through the middle, and bright only along its two edges, where it bends the sky round
    /// into the eye and reflects it at a grazing angle -- so a streak is a thin tube with two bright rims and a
    /// faint body. The drop itself sits at the leading end as a small bead; behind it the smear thins out along
    /// the path it swept. Out of focus, near the lens, a drop spreads into a faint ring, which the defocused
    /// strip carries as a soft, hollow streak. Every cell varies the tube's weight and its rims, so one storm is
    /// not one repeated sprite.
    /// </para>
    /// </remarks>
    private static (Bgra32[] Pixels, int Width, int Height, float SplitV) BuildFallbackSheet()
    {
        const int Cell = 32;
        const int Strip = 256;

        int width = Cell * StormRenderer.SheetCells;
        int height = Strip * 2;
        Bgra32[] pixels = new Bgra32[width * height];

        for (int cell = 0; cell < StormRenderer.SheetCells; cell++)
        {
            // Per cell: how wide the tube is in its cell, where its rims sit, and how bright they are.
            float variation = cell / (float)Math.Max(StormRenderer.SheetCells - 1, 1);
            float halfWidth = 0.30f + (0.14f * variation);
            float rimAt = 0.62f + (0.18f * ((cell * 7) % StormRenderer.SheetCells) / StormRenderer.SheetCells);
            float rimLevel = 0.75f + (0.25f * ((cell * 3) % StormRenderer.SheetCells) / StormRenderer.SheetCells);

            for (int strip = 0; strip < 2; strip++)
            {
                bool defocused = strip == 1;
                for (int y = 0; y < Strip; y++)
                {
                    // v runs from the leading end (-1, the top row) to the tail (+1).
                    float v = (((y + 0.5f) / Strip) * 2f) - 1f;
                    float along01 = (v + 1f) * 0.5f;

                    // Rounded ends, and a smear that thins out behind the drop.
                    float ends = MathF.Sqrt(Math.Clamp(1f - (v * v * v * v), 0f, 1f));
                    float trail = defocused ? 1f : 0.35f + (0.65f * MathF.Pow(1f - along01, 0.7f));

                    // The drop itself, a bead at the leading end of the sharp streak.
                    float beadOffset = (v + 0.82f) / 0.07f;
                    float bead = defocused ? 0f : 0.55f * MathF.Exp(-0.5f * beadOffset * beadOffset);

                    for (int x = 0; x < Cell; x++)
                    {
                        float u = ((x + 0.5f) / Cell * 2f) - 1f;
                        float r = MathF.Abs(u) / (defocused ? 0.95f : halfWidth);

                        float value;
                        if (defocused)
                        {
                            // A soft ring: bright towards its edge, all but clear inside.
                            float ring = (r - 0.78f) / 0.16f;
                            value = r > 1.15f ? 0f : (0.30f * MathF.Exp(-0.5f * ring * ring)) + (0.04f * (1f - Math.Min(r, 1f)));
                        }
                        else
                        {
                            float rim = (r - rimAt) / 0.14f;
                            float rims = rimLevel * MathF.Exp(-0.5f * rim * rim);
                            float body = r < 1f ? 0.14f * (1f - (r * r)) : 0f;
                            value = r > 1.2f ? 0f : rims + body;
                            value += bead * MathF.Exp(-2.5f * r * r);
                        }

                        value *= ends * trail;
                        byte alpha = (byte)(Math.Clamp(value, 0f, 1f) * 255f);
                        pixels[(((strip * Strip) + y) * width) + (cell * Cell) + x] =
                            new Bgra32(255, 255, 255, alpha);
                    }
                }
            }
        }

        return (pixels, width, height, 0.5f);
    }

    /// <summary>Uploads a sheet with as much of a mip chain as its cells can take.</summary>
    /// <param name="pixels">Level zero.</param>
    /// <param name="width">Its width.</param>
    /// <param name="height">Its height.</param>
    /// <param name="cellWidth">The narrowest cell across the sheet, in texels.</param>
    /// <returns>The texture.</returns>
    /// <remarks>
    /// A streak is a few texels of a painted line, and drawn a few pixels across without a chain it is sampled
    /// at scattered texels -- which broke every drop into a row of hard white dashes, rain as a dotted line of
    /// paint rather than water. The chain lets a small drop read as the soft, faint thing it is. It stops while a
    /// cell is still four texels wide, so the averaging never mixes one cell's streak into its neighbour's.
    /// </remarks>
    /// <param name="device">The device to upload to.</param>
    internal static TextureHandle UploadSheet(IGraphicsDevice device, Bgra32[] pixels, int width, int height, int cellWidth)
    {
        int levels = 1;
        for (int cell = cellWidth; cell >= 8 && levels < LinearMipChain.LevelCount(width, height); cell >>= 1)
        {
            levels++;
        }

        if (levels == 1)
        {
            return device.CreateTexture(SheetTexture(width, height), pixels);
        }

        (Bgra32[] chain, _) = LinearMipChain.Build(pixels, width, height);
        int texels = 0;
        for (int level = 0; level < levels; level++)
        {
            texels += Math.Max(1, width >> level) * Math.Max(1, height >> level);
        }

        return device.CreateTexture(
            SheetTexture(width, height) with { MipLevels = levels },
            chain.AsSpan(0, texels));
    }

    /// <summary>How every one of the storm's sprite sheets is described to the device.</summary>
    /// <param name="width">The sheet's width in texels.</param>
    /// <param name="height">Its height in texels.</param>
    /// <returns>The description.</returns>
    /// <remarks>
    /// <para>
    /// <b>One mip level, and asking for none is not a way to ask for a chain.</b>
    /// <see cref="TextureDescription"/> counts levels and requires at least one; zero makes level zero
    /// itself out of range, and the upload throws an <see cref="ArgumentOutOfRangeException"/> before a
    /// texel is written. There is no mip generation behind this interface — a chain is uploaded level by
    /// level or it does not exist — so a sheet that wants one has to build it, not request it.
    /// </para>
    /// <para>
    /// Asking for zero here took the WHOLE storm off the screen, not just the sheet that asked: the
    /// sprites are resolved together at the top of the draw, inside one try, and the splash sheet is
    /// resolved before the drops. One bad description and the curtains, the splashes and the rain all
    /// stopped being drawn at once.
    /// </para>
    /// </remarks>
    internal static TextureDescription SheetTexture(int width, int height) =>
        new(width, height, MipLevels: 1, TextureFilter.Linear, TextureAddressMode.Clamp);

    /// <summary>Where one splash cell sits across the sheet, mirrored when asked.</summary>
    /// <param name="column">The cell's column.</param>
    /// <param name="flip">Whether to read the cell right to left.</param>
    /// <returns>The left and right texture coordinates, swapped when flipped.</returns>
    /// <remarks>
    /// Every splash has an even chance of being mirrored. Three animations over eight frames is not
    /// many, and without the mirror a heavy shower is visibly the same handful of stamps repeating.
    /// </remarks>
    internal static (float U0, float U1) SplashColumnUv(int column, bool flip)
    {
        float left = column / (float)StormRenderer.SplashColumns;
        float right = (column + 1) / (float)StormRenderer.SplashColumns;
        return flip ? (right, left) : (left, right);
    }

    /// <summary>Unpacks the three channel-packed splash animations and adds the small splashes' blob.</summary>
    /// <param name="logger">Told about a sheet that could not be read; the stand-in is drawn instead.</param>
    /// <returns>The sheet, four cells across by <see cref="StormRenderer.SplashRows"/> down.</returns>
    internal static (Bgra32[] Pixels, int Width, int Height) BuildSplashSheet(IOpenIgiLogger? logger = null)
    {
        if (TryLoadSheetPixels(StormRenderer.SplashSheetAsset, logger, out Bgra32[] source, out int sourceWidth, out int sourceHeight)
            && sourceWidth % StormRenderer.SplashColumns == 0
            && sourceHeight % StormRenderer.SplashFrameRows == 0)
        {
            int cell = sourceHeight / StormRenderer.SplashFrameRows;
            int width = sourceWidth;
            int height = cell * StormRenderer.SplashRows;
            Bgra32[] sheet = new Bgra32[width * height];

            for (int variant = 0; variant < StormRenderer.SplashVariants; variant++)
            {
                int top = variant * StormRenderer.SplashFrameRows * cell;
                for (int y = 0; y < sourceHeight; y++)
                {
                    for (int x = 0; x < sourceWidth; x++)
                    {
                        Bgra32 texel = source[(y * sourceWidth) + x];
                        byte mask = variant switch
                        {
                            0 => texel.R,
                            1 => texel.G,
                            _ => texel.B,
                        };

                        // White with the mask in alpha, which is the shape every sheet here carries.
                        sheet[(((top + y) * width) + x)] = new Bgra32(255, 255, 255, mask);
                    }
                }
            }

            FillBlobStrip(sheet, width, cell, logger);
            return (sheet, width, height);
        }

        // No image: the stand-in, drawn at the same layout so every index still addresses what it means.
        return LoadSheet(
            string.Empty,
            logger,
            128,
            StormRenderer.SplashColumns,
            StormRenderer.SplashRows,
            static (u, v, column, row) =>
            {
                if (row >= StormRenderer.SplashBlobRow)
                {
                    float square = MathF.Max(MathF.Abs(u), MathF.Abs(v));
                    return Math.Clamp(1f - square, 0f, 1f) * Math.Clamp(1f - square, 0f, 1f);
                }

                int frame = ((row % StormRenderer.SplashFrameRows) * StormRenderer.SplashColumns) + column;
                float life = (frame + 0.5f) / StormRenderer.SplashFrames;
                float radius = MathF.Sqrt((u * u) + ((v + 1f) * (v + 1f)));
                float ring = Math.Clamp(1f - (MathF.Abs(radius - (0.3f + (life * 1.1f))) / 0.22f), 0f, 1f);
                return ring * ring * (1f - life) * Math.Clamp(-v, 0f, 1f);
            });
    }

    /// <summary>Writes the small splashes' round blob across the sheet's last strip.</summary>
    /// <remarks>
    /// Repeated across all four columns so that the blob strip is addressed by the same column
    /// arithmetic as every other, and a droplet that picks a column other than zero still lands on it.
    /// </remarks>
    private static void FillBlobStrip(Bgra32[] sheet, int width, int cell, IOpenIgiLogger? logger)
    {
        int top = StormRenderer.SplashBlobRow * cell;

        if (!TryLoadMask(StormRenderer.DropletSheetAsset, logger, out Bgra32[] blob, out int blobWidth, out int blobHeight)
            || blobWidth <= 0
            || blobHeight <= 0)
        {
            // A soft round blob, standing in when the image is missing.
            for (int y = 0; y < cell; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float u = ((((x % cell) + 0.5f) / cell) * 2f) - 1f;
                    float v = (((y + 0.5f) / cell) * 2f) - 1f;
                    float fade = Math.Clamp(1f - MathF.Sqrt((u * u) + (v * v)), 0f, 1f);
                    sheet[(((top + y) * width) + x)] = new Bgra32(255, 255, 255, (byte)(fade * fade * 255f));
                }
            }

            return;
        }

        for (int y = 0; y < cell; y++)
        {
            int sourceY = Math.Min(blobHeight - 1, y * blobHeight / cell);
            for (int x = 0; x < width; x++)
            {
                int sourceX = Math.Min(blobWidth - 1, (x % cell) * blobWidth / cell);
                sheet[(((top + y) * width) + x)] = blob[(sourceY * blobWidth) + sourceX];
            }
        }
    }

    /// <summary>Reads a sheet's pixels as they are, colour channels intact.</summary>
    /// <remarks>
    /// <see cref="TryLoadMask"/> whitens the colour and moves blue into alpha, which is right for every
    /// sheet whose shape is one greyscale mask and wrong for the splash sheet, whose three channels are
    /// three different animations.
    /// </remarks>
    private static bool TryLoadSheetPixels(
        string asset,
        IOpenIgiLogger? logger,
        out Bgra32[] pixels,
        out int width,
        out int height)
    {
        pixels = [];
        width = 0;
        height = 0;

        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "weather", asset);

        try
        {
            (Bgra32[] read, int w, int h) = PngReader.Read(path);
            if (w <= 0 || h <= 0 || read.Length < w * h)
            {
                return false;
            }

            pixels = read;
            width = w;
            height = h;
            return true;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            logger?.Warning(
                LogCategory,
                FormattableString.Invariant($"Storm sheet '{asset}' could not be read ({error.Message}); drawing generated splashes."));
            return false;
        }
    }

    /// <summary>Reads the far sheets' cloud, or draws its stand-in.</summary>
    /// <param name="logger">Told when the file could not be read.</param>
    /// <returns>The sheet.</returns>
    internal static (Bgra32[] Pixels, int Width, int Height) BuildCurtainSheet(IOpenIgiLogger? logger) =>
        LoadSheet(
            StormRenderer.CurtainSheetAsset,
            logger,
            256,
            1,
            1,
            static (u, v, _, _) =>
            {
                float radius = MathF.Sqrt((u * u) + (v * v));
                float body = Math.Clamp(1f - radius, 0f, 1f);
                return body * body * body * (0.75f + (0.25f * MathF.Sin((u * 19f) + (v * 3f))));
            });

    /// <summary>Reads a mask sheet, or draws the given stand-in at the given size when it is missing.</summary>
    /// <param name="asset">The file, under the executable's weather assets.</param>
    /// <param name="logger">Told when the file could not be read.</param>
    /// <param name="cell">The stand-in's cell edge in texels, used only when the file is missing.</param>
    /// <param name="columns">How many cells across the stand-in is.</param>
    /// <param name="rows">How many cells down it is.</param>
    /// <param name="shape">
    /// The stand-in's alpha, given the position within the current cell from -1 to 1 on both axes and
    /// the cell's column and row.
    /// </param>
    private static (Bgra32[] Pixels, int Width, int Height) LoadSheet(
        string asset,
        IOpenIgiLogger? logger,
        int cell,
        int columns,
        int rows,
        Func<float, float, int, int, float> shape)
    {
        // An empty name is a caller that has already decided to draw the stand-in.
        if (asset.Length > 0
            && TryLoadMask(asset, logger, out Bgra32[] loaded, out int loadedWidth, out int loadedHeight))
        {
            return (loaded, loadedWidth, loadedHeight);
        }

        int width = cell * columns;
        int height = cell * rows;
        Bgra32[] pixels = new Bgra32[width * height];
        for (int y = 0; y < height; y++)
        {
            int row = y / cell;
            float v = ((((y - (row * cell)) + 0.5f) / cell) * 2f) - 1f;
            for (int x = 0; x < width; x++)
            {
                int column = x / cell;
                float u = ((((x - (column * cell)) + 0.5f) / cell) * 2f) - 1f;
                byte alpha = (byte)(Math.Clamp(shape(u, v, column, row), 0f, 1f) * 255f);
                pixels[(y * width) + x] = new Bgra32(255, 255, 255, alpha);
            }
        }

        return (pixels, width, height);
    }
}
