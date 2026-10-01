namespace OpenIGI.UI.Text;

/// <summary>
/// Packs a set of rectangles into a fixed-width atlas using the shelf-next-fit heuristic.
/// </summary>
/// <remarks>
/// <para>
/// A font atlas is a friendly case for packing: a few hundred glyphs, all roughly one line tall, packed
/// once at startup. The shelf heuristic — sort tallest-first, lay rectangles left to right until the row
/// is full, then start a new row at the height of the tallest so far — wastes a little vertical space but
/// is trivial, deterministic and fast, which matters more here than the last few percent of density a
/// skyline packer would recover.
/// </para>
/// <para>
/// It is a pure function of its inputs with no graphics dependency, so the packing is unit-tested
/// directly: the same glyph set always produces the same layout and the same atlas height.
/// </para>
/// </remarks>
public static class ShelfPacker
{
    /// <summary>The placement chosen for one input rectangle.</summary>
    /// <param name="X">The left edge in the atlas, in texels.</param>
    /// <param name="Y">The top edge in the atlas, in texels.</param>
    public readonly record struct Placement(int X, int Y);

    /// <summary>The result of a packing run.</summary>
    /// <param name="Placements">One placement per input rectangle, in the input's original order.</param>
    /// <param name="Height">The atlas height the placements require, in texels.</param>
    public readonly record struct Result(IReadOnlyList<Placement> Placements, int Height);

    /// <summary>Packs rectangles into an atlas of a fixed width.</summary>
    /// <param name="sizes">The rectangle sizes, width then height, in texels.</param>
    /// <param name="atlasWidth">The atlas width in texels.</param>
    /// <param name="padding">The gap left between neighbouring rectangles and at the edges, in texels.</param>
    /// <returns>The placement of every rectangle and the resulting atlas height.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="atlasWidth"/> is not positive, or <paramref name="padding"/> is negative.</exception>
    /// <exception cref="ArgumentException">A rectangle is wider than the atlas can ever hold.</exception>
    public static Result Pack(IReadOnlyList<(int Width, int Height)> sizes, int atlasWidth, int padding = 1)
    {
        ArgumentNullException.ThrowIfNull(sizes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(atlasWidth);
        ArgumentOutOfRangeException.ThrowIfNegative(padding);

        var placements = new Placement[sizes.Count];

        // Tallest first so short glyphs never force a shelf taller than they need. The index carried
        // alongside restores the caller's order, and it breaks ties so the run is fully deterministic.
        int[] order = new int[sizes.Count];
        for (int i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, (a, b) =>
        {
            int byHeight = sizes[b].Height.CompareTo(sizes[a].Height);
            return byHeight != 0 ? byHeight : a.CompareTo(b);
        });

        int cursorX = padding;
        int shelfTop = padding;
        int shelfHeight = 0;

        foreach (int index in order)
        {
            (int w, int h) = sizes[index];
            if (w == 0 || h == 0)
            {
                // A whitespace glyph occupies no atlas space; it still needs a placement so callers can
                // index by rectangle, but it never contributes to the layout.
                placements[index] = new Placement(padding, shelfTop);
                continue;
            }

            if (w + (padding * 2) > atlasWidth)
            {
                throw new ArgumentException(
                    $"Rectangle {index} is {w} texels wide, wider than the {atlasWidth}-texel atlas can hold.",
                    nameof(sizes));
            }

            if (cursorX + w + padding > atlasWidth)
            {
                // Start a new shelf below the tallest rectangle placed on the current one.
                shelfTop += shelfHeight + padding;
                shelfHeight = 0;
                cursorX = padding;
            }

            placements[index] = new Placement(cursorX, shelfTop);
            cursorX += w + padding;
            shelfHeight = Math.Max(shelfHeight, h);
        }

        int height = shelfTop + shelfHeight + padding;
        return new Result(placements, height);
    }
}
