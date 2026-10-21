using OpenIGI.UI.Layout;

namespace OpenIGI.Presentation.Igi1.World;

/// <summary>Where every piece of the interface is this frame, in back-buffer pixels.</summary>
/// <remarks>
/// Measured once and passed to both the hit tests and the draws. Two copies of a layout is the oldest
/// way there is to get a button that highlights somewhere other than where it can be clicked.
/// </remarks>
internal readonly record struct TacticalComputerChrome(
    UiRect Header,
    UiRect Footer,
    UiRect Rail,
    UiRect Status,
    UiRect Page,
    bool PageVisible,
    float RowHeight,
    float RailPad,
    float Gutter)
{
    /// <summary>Gets the row a page's rail entry occupies.</summary>
    /// <param name="index">The page's index.</param>
    /// <returns>The row rectangle.</returns>
    public UiRect PageRow(int index) => new(
        Rail.X + RailPad,
        Rail.Y + RailPad + (RowHeight * index),
        Rail.Width - (RailPad * 2f),
        RowHeight);

    /// <summary>Determines whether a point is over the interface rather than over the bare map.</summary>
    /// <param name="x">The horizontal position in pixels.</param>
    /// <param name="y">The vertical position in pixels.</param>
    /// <returns><see langword="true"/> when a click there belongs to a control.</returns>
    public bool OverInterface(float x, float y) =>
        Contains(Header, x, y)
        || Contains(Footer, x, y)
        || Contains(Rail, x, y)
        || Contains(Status, x, y)
        || (PageVisible && Contains(Page, x, y));

    /// <summary>Lays the interface out for a back buffer.</summary>
    /// <param name="context">The back buffer's layout context.</param>
    /// <param name="pageVisible">Whether a text page's card is up over the map.</param>
    /// <param name="pageCount">How many pages the rail lists.</param>
    /// <returns>Where every piece is this frame.</returns>
    public static TacticalComputerChrome Measure(LayoutContext context, bool pageVisible, int pageCount)
    {
        float gutter = context.Px(28f);
        float width = context.Viewport.X;
        float height = context.Viewport.Y;
        float headerHeight = context.Px(72f);
        float footerHeight = context.Px(54f);
        float railWidth = context.Px(268f);
        float rowHeight = context.Px(44f);
        float railPad = context.Px(12f);

        var rail = new UiRect(
            gutter,
            headerHeight + gutter,
            railWidth,
            (rowHeight * pageCount) + (railPad * 2f));

        var status = new UiRect(gutter, rail.Bottom + context.Px(14f), railWidth, context.Px(178f));

        float pageLeft = rail.Right + gutter;
        var page = new UiRect(
            pageLeft,
            headerHeight + gutter,
            MathF.Max(context.Px(200f), width - pageLeft - gutter),
            MathF.Max(context.Px(200f), height - footerHeight - gutter - headerHeight - gutter));

        return new TacticalComputerChrome(
            new UiRect(0f, 0f, width, headerHeight),
            new UiRect(0f, height - footerHeight, width, footerHeight),
            rail,
            status,
            page,
            pageVisible,
            rowHeight,
            railPad,
            gutter);
    }

    /// <summary>Determines whether a point lies inside a rectangle, edges included.</summary>
    public static bool Contains(UiRect rect, float x, float y) =>
        x >= rect.X && x <= rect.Right && y >= rect.Y && y <= rect.Bottom;
}
