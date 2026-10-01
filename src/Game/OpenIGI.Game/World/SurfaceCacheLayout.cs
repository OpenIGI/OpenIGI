using OpenIGI.Core.Diagnostics;

namespace OpenIGI.Game.World;

/// <summary>
/// Where every card's page sits in the surface cache atlas.
/// </summary>
/// <remarks>
/// <para>
/// A card is an orthographic view of part of a model; its page is the rectangle of atlas the capture of
/// that view lands in. Everything downstream addresses surfaces through these pages — a trace that hits
/// a model finds the card facing it, reads its page, and gets the albedo and normal there. Lighting
/// later writes into pages of the same shape.
/// </para>
/// <para>
/// <b>Shelf packing, not a perfect one.</b> Pages are sorted tallest first and laid in rows, each row as
/// tall as its first page. That wastes the difference between a row's tallest page and its shortest —
/// a few per cent here, because card resolutions come from a small set of sizes and sort into rows of
/// near-equal height. A tight packer would recover that and cost a search per page; the atlas is
/// allocated once at load and never repacked, so the saving buys nothing.
/// <para>
/// What did matter was how the atlas GROWS. Doubling a square one quadruples its area, so a packing
/// that only just failed landed near a quarter occupied — measured at 34%. The width is now chosen once
/// and the height grows to suit, which costs nothing because shelves run horizontally.
/// </para>
/// </para>
/// <para>
/// <b>Pages are padded apart.</b> The capture is sampled with a linear filter, so a fetch at a page's
/// edge blends in whatever was packed beside it — a different model's surface entirely. The same
/// hazard the distance field atlas has, and the same answer, except that here the pages are written by
/// a render pass rather than copied, so the padding is a gutter rather than an inset.
/// </para>
/// </remarks>
public sealed class SurfaceCacheLayout
{
    private const string LogCategory = "SurfaceCache";

    /// <summary>How many texels are left between one page and the next.</summary>
    /// <remarks>
    /// One would do for a bilinear fetch, which reaches half a texel. Two leaves room for the capture's
    /// own edge to be slightly wrong — an orthographic render of a card catches geometry right at the
    /// boundary — without spending real space: at the resolutions cards use, this is under a per cent
    /// of the atlas.
    /// </remarks>
    public const int Gutter = 2;

    private SurfaceCacheLayout(int width, int height, Page[] pages)
    {
        Width = width;
        Height = height;
        Pages = pages;
    }

    /// <summary>Gets the atlas width in texels.</summary>
    public int Width { get; }

    /// <summary>Gets the atlas height in texels.</summary>
    public int Height { get; }

    /// <summary>Gets every page, in the order the cards were given.</summary>
    public Page[] Pages { get; }

    /// <summary>Gets what share of the atlas the pages actually occupy.</summary>
    /// <remarks>
    /// The number that says whether the packing is working. Well under a half means the atlas is mostly
    /// gutters and shelf waste and the card resolutions want revisiting; near one means it is about to
    /// stop fitting.
    /// </remarks>
    public float Occupancy
    {
        get
        {
            long used = 0;

            foreach (Page page in Pages)
            {
                used += (long)page.Width * page.Height;
            }

            return Width * Height > 0 ? (float)((double)used / ((double)Width * Height)) : 0f;
        }
    }

    /// <summary>Assigns every card a page.</summary>
    /// <param name="cards">The cards to place, each already carrying the resolution it wants.</param>
    /// <param name="maximumSize">
    /// The largest atlas edge to allow, on either axis. The height grows to hold the shelves and the
    /// width doubles only if even a full-height atlas cannot take them; a driver's own texture limit is
    /// the real bound and the caller is the one that knows it.
    /// </param>
    /// <param name="logger">Where to report what was packed and anything that would not fit.</param>
    /// <returns>The layout. Cards that did not fit are given an empty page rather than dropped.</returns>
    /// <remarks>
    /// <b>A card that does not fit keeps its entry.</b> The pages are addressed by index — a trace finds
    /// card three of model seven — so dropping one would renumber every page after it and silently
    /// point every later trace at the wrong surface. An empty page is a card that reads as nothing,
    /// which is wrong in one place instead of everywhere.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="cards"/> is <see langword="null"/>.</exception>
    public static SurfaceCacheLayout Pack(
        IReadOnlyList<MeshCard> cards, int maximumSize = 8192, IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(cards);

        IIgiLogger log = logger ?? NullLogger.Instance;

        if (cards.Count == 0)
        {
            return new SurfaceCacheLayout(0, 0, []);
        }

        // Tallest first. A shelf is as tall as the first page laid on it, so putting the tall ones down
        // while the atlas is empty is what keeps later rows from being mostly air.
        int[] order = [.. Enumerable.Range(0, cards.Count)];
        Array.Sort(order, (a, b) => cards[b].ResolutionV.CompareTo(cards[a].ResolutionV));

        long wanted = 0;

        foreach (MeshCard card in cards)
        {
            wanted += (long)(card.ResolutionU + Gutter) * (card.ResolutionV + Gutter);
        }

        // The width is chosen once and the height grows to suit, which is the whole reason this packs
        // tightly. Doubling a SQUARE atlas quadruples its area, so a packing that only just fails jumps
        // to four times the space and lands near a quarter occupied -- measured at 34% before this,
        // which is most of the atlas being nothing.
        //
        // Shelves run horizontally, so height is the axis that costs nothing to extend: a taller atlas
        // is more shelves, and the last one is as full as it happened to get.
        int width = 64;

        while ((long)width * width < wanted && width < maximumSize)
        {
            width *= 2;
        }

        while (true)
        {
            if (TryPack(cards, order, width, maximumSize, out Page[] packed, out int usedHeight, out int unplaced)
                && unplaced == 0)
            {
                // Rounded up to a power of two. Not required by any version of GL this targets, but it
                // keeps the atlas to sizes a driver allocates without repacking, and the rounding costs
                // less than the doubling it replaced.
                int height = 64;

                while (height < usedHeight)
                {
                    height *= 2;
                }

                SurfaceCacheLayout layout = new(width, Math.Min(height, maximumSize), packed);

                log.Information(
                    LogCategory,
                    $"Surface cache: {cards.Count} page(s) in a {layout.Width}x{layout.Height} atlas, "
                    + $"{layout.Occupancy * 100f:0.#}% occupied.");

                return layout;
            }

            if (width >= maximumSize)
            {
                SurfaceCacheLayout layout = new(width, maximumSize, packed);

                log.Warning(
                    LogCategory,
                    $"Surface cache: {unplaced} of {cards.Count} page(s) did not fit a "
                    + $"{width}x{maximumSize} atlas and will read as nothing. Lower the card resolution.");

                return layout;
            }

            width *= 2;
        }
    }

    /// <summary>Lays the pages onto shelves, reporting whether every one fitted.</summary>
    private static bool TryPack(
        IReadOnlyList<MeshCard> cards,
        int[] order,
        int width,
        int maximumHeight,
        out Page[] pages,
        out int usedHeight,
        out int unplaced)
    {
        pages = new Page[cards.Count];
        unplaced = 0;

        int shelfY = 0;
        int shelfHeight = 0;
        int cursorX = 0;

        foreach (int index in order)
        {
            MeshCard card = cards[index];
            int pageWidth = card.ResolutionU + Gutter;
            int pageHeight = card.ResolutionV + Gutter;

            if (pageWidth > width || pageHeight > maximumHeight)
            {
                unplaced++;
                continue;
            }

            // Past the right edge, so start a new shelf below the tallest page on this one.
            if (cursorX + pageWidth > width)
            {
                shelfY += shelfHeight;
                shelfHeight = 0;
                cursorX = 0;
            }

            if (shelfY + pageHeight > maximumHeight)
            {
                unplaced++;
                continue;
            }

            // The gutter is spent on the page's own right and bottom, so the rectangle recorded is the
            // usable area and nothing downstream has to know the padding exists.
            pages[index] = new Page(cursorX, shelfY, card.ResolutionU, card.ResolutionV);

            cursorX += pageWidth;
            shelfHeight = Math.Max(shelfHeight, pageHeight);
        }

        usedHeight = shelfY + shelfHeight;
        return unplaced == 0;
    }

    /// <summary>Where one card's capture lives in the atlas.</summary>
    /// <param name="X">The page's left edge in texels.</param>
    /// <param name="Y">The page's top edge in texels.</param>
    /// <param name="Width">The page's width in texels.</param>
    /// <param name="Height">The page's height in texels.</param>
    public readonly record struct Page(int X, int Y, int Width, int Height)
    {
        /// <summary>Gets a value indicating whether this page was given any atlas space.</summary>
        public bool IsAllocated => Width > 0 && Height > 0;

        /// <summary>Gets the page's right edge, exclusive.</summary>
        public int Right => X + Width;

        /// <summary>Gets the page's bottom edge, exclusive.</summary>
        public int Bottom => Y + Height;

        /// <summary>Determines whether this page overlaps another.</summary>
        /// <param name="other">The page to test against.</param>
        /// <returns><see langword="true"/> when the two share any texel.</returns>
        public bool Overlaps(Page other) =>
            IsAllocated
            && other.IsAllocated
            && X < other.Right
            && other.X < Right
            && Y < other.Bottom
            && other.Y < Bottom;
    }
}
