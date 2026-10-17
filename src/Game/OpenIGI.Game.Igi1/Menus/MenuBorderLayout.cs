
namespace OpenIGI.Game.Igi1.Menus;

/// <summary>
/// Works out where each piece of a frame's eight-piece border goes.
/// </summary>
/// <remarks>
/// <para>
/// Transcribed from <c>MenuFrame_Render</c> (<c>0x4226D0</c>) and its two edge helpers, <c>0x4227A0</c>
/// horizontal and <c>0x4229A0</c> vertical. The arithmetic lives here rather than in a renderer so it
/// can be checked without a graphics device — the retail main menu turns out to use no frame borders
/// at all, so nothing in the shipped data exercises it.
/// </para>
/// <para>
/// The eight sprites form a clockwise ring from the top-left, named <c>&lt;sprite&gt;_1</c> through
/// <c>_8</c>: see <see cref="MenuBorderPiece"/>.
/// </para>
/// <para>
/// <b>Nothing is stretched.</b> The edges tile at their native size an integer number of times, so a
/// frame whose width is not a whole number of tiles leaves a gap before the far corner rather than
/// scaling to fit. The bottom edge and the right edge are pulled back by their own size so they sit
/// inside the rectangle, and the vertical runs span only what is left between the horizontal edges.
/// </para>
/// </remarks>
public static class MenuBorderLayout
{
    /// <summary>Computes where every border piece is drawn.</summary>
    /// <param name="width">The frame width in pixels.</param>
    /// <param name="height">The frame height in pixels.</param>
    /// <param name="sizes">The eight pieces' sizes, indexed by <see cref="MenuBorderPiece"/>. A zero size means the piece is absent.</param>
    /// <returns>The placements, in the order the original draws them.</returns>
    /// <exception cref="ArgumentException"><paramref name="sizes"/> does not hold <see cref="MenuLayout.FramePieceCount"/> entries.</exception>
    public static IReadOnlyList<MenuBorderPlacement> Compute(
        int width,
        int height,
        IReadOnlyList<(int Width, int Height)> sizes)
    {
        ArgumentNullException.ThrowIfNull(sizes);
        if (sizes.Count != MenuLayout.FramePieceCount)
        {
            throw new ArgumentException(
                $"A border needs {MenuLayout.FramePieceCount} piece sizes, got {sizes.Count}.",
                nameof(sizes));
        }

        List<MenuBorderPlacement> placements = [];

        int topHeight = AddHorizontal(
            placements, sizes, 0, 0, width,
            MenuBorderPiece.TopLeft, MenuBorderPiece.Top, MenuBorderPiece.TopRight, alignBottom: false);

        int bottomHeight = AddHorizontal(
            placements, sizes, 0, height, width,
            MenuBorderPiece.BottomLeft, MenuBorderPiece.Bottom, MenuBorderPiece.BottomRight, alignBottom: true);

        int middleY = topHeight;
        int middleHeight = height - (topHeight + bottomHeight);

        AddVertical(placements, sizes, 0, middleY, middleHeight, MenuBorderPiece.Left, alignRight: false);
        AddVertical(placements, sizes, width, middleY, middleHeight, MenuBorderPiece.Right, alignRight: true);

        return placements;
    }

    private static int AddHorizontal(
        List<MenuBorderPlacement> into,
        IReadOnlyList<(int Width, int Height)> sizes,
        int x,
        int y,
        int width,
        MenuBorderPiece first,
        MenuBorderPiece middle,
        MenuBorderPiece last,
        bool alignBottom)
    {
        int firstWidth = sizes[(int)first].Width;
        int middleWidth = sizes[(int)middle].Width;
        int lastWidth = sizes[(int)last].Width;

        int edgeHeight = Math.Max(
            sizes[(int)first].Height,
            Math.Max(sizes[(int)middle].Height, sizes[(int)last].Height));

        if (alignBottom)
        {
            y -= edgeHeight;
        }

        if (firstWidth > 0)
        {
            into.Add(new MenuBorderPlacement(first, x, y));
            x += firstWidth;
            width -= firstWidth;
        }

        if (middleWidth > 0)
        {
            // Integer division: a run that does not divide evenly stops short of the far corner.
            for (int i = 0, n = (width - lastWidth) / middleWidth; i < n; i++)
            {
                into.Add(new MenuBorderPlacement(middle, x, y));
                x += middleWidth;
            }
        }
        else
        {
            x += width - lastWidth;
        }

        if (lastWidth > 0)
        {
            into.Add(new MenuBorderPlacement(last, x, y));
        }

        return edgeHeight;
    }

    private static void AddVertical(
        List<MenuBorderPlacement> into,
        IReadOnlyList<(int Width, int Height)> sizes,
        int x,
        int y,
        int height,
        MenuBorderPiece piece,
        bool alignRight)
    {
        (int pieceWidth, int pieceHeight) = sizes[(int)piece];
        if (pieceHeight <= 0)
        {
            return;
        }

        if (alignRight)
        {
            x -= pieceWidth;
        }

        for (int i = 0, n = height / pieceHeight; i < n; i++)
        {
            into.Add(new MenuBorderPlacement(piece, x, y));
            y += pieceHeight;
        }
    }
}
