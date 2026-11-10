using OpenIGI.UI.Layout;

namespace OpenIGI.Presentation.Rendering;

/// <summary>Where every piece of the remastered computer is this frame, in back-buffer pixels.</summary>
/// <remarks>
/// <para>
/// A strip of tabs along the top, a status line along the bottom, and the map between them with nothing over it; a text
/// page is one panel centred over the map.
/// </para>
/// <para>
/// Measured once and passed to both the hit tests and the draws. Two copies of a layout is the oldest way there is to get
/// a button that highlights somewhere other than where it can be clicked.
/// </para>
/// </remarks>
/// <param name="Top">The tab strip.</param>
/// <param name="Bottom">The status line.</param>
/// <param name="Map">What is left between them: the map, unobstructed.</param>
/// <param name="Page">The panel a text page is set on.</param>
/// <param name="PageVisible">Whether a text page is up.</param>
/// <param name="Unit">How many pixels a design pixel of a 720-line screen is drawn as.</param>
/// <param name="Line">The height of a line of text, in pixels.</param>
internal readonly record struct TacticalComputerChrome(
    UiRect Top,
    UiRect Bottom,
    UiRect Map,
    UiRect Page,
    bool PageVisible,
    float Unit,
    float Line)
{
    /// <summary>The height of the screen the design's lengths are given for.</summary>
    public const float DesignHeight = 720f;

    /// <summary>The widest a text page's panel is, in design pixels.</summary>
    public const float PageWidth = 760f;

    /// <summary>A length in design pixels, on the screen.</summary>
    /// <param name="design">The length.</param>
    /// <returns>It in pixels.</returns>
    public float Px(float design) => design * Unit;

    /// <summary>Gets how far text stands in from an edge of the screen.</summary>
    public float Gutter => Px(16f);

    /// <summary>Determines whether a point is over the interface rather than over the bare map.</summary>
    /// <param name="x">The horizontal position in pixels.</param>
    /// <param name="y">The vertical position in pixels.</param>
    /// <returns><see langword="true"/> when a click there belongs to a control.</returns>
    public bool OverInterface(float x, float y) =>
        Contains(Top, x, y) || Contains(Bottom, x, y) || (PageVisible && Contains(Page, x, y));

    /// <summary>The unit a screen is laid out in.</summary>
    /// <param name="height">The screen's height in pixels.</param>
    /// <returns>How many pixels a design pixel is drawn as.</returns>
    public static float UnitFor(float height) => MathF.Max(0.75f, height / DesignHeight);

    /// <summary>Lays the interface out for a screen.</summary>
    /// <param name="width">The screen's width in pixels.</param>
    /// <param name="height">The screen's height in pixels.</param>
    /// <param name="line">The height of a line of text, in pixels.</param>
    /// <param name="pageVisible">Whether a text page's panel is up over the map.</param>
    /// <param name="tabWidths">The width of each tab's name, in pixels.</param>
    /// <param name="tabs">Receives each tab's rectangle, one for each width.</param>
    /// <returns>Where every piece is this frame.</returns>
    public static TacticalComputerChrome Measure(
        float width,
        float height,
        float line,
        bool pageVisible,
        ReadOnlySpan<float> tabWidths,
        Span<UiRect> tabs)
    {
        float unit = UnitFor(height);
        float topHeight = MathF.Round(line + (16f * unit));
        float bottomHeight = MathF.Round(line + (12f * unit));
        var top = new UiRect(0f, 0f, width, topHeight);
        var bottom = new UiRect(0f, height - bottomHeight, width, bottomHeight);
        var map = new UiRect(0f, top.Bottom, width, MathF.Max(0f, bottom.Y - top.Bottom));

        float x = 16f * unit;
        float inset = 4f * unit;
        for (int i = 0; i < tabs.Length && i < tabWidths.Length; i++)
        {
            float tabWidth = MathF.Round(tabWidths[i] + (20f * unit));
            tabs[i] = new UiRect(MathF.Round(x), inset, tabWidth, topHeight - (inset * 2f));
            x += tabWidth + (6f * unit);
        }

        float margin = 20f * unit;
        float pageWidth = MathF.Min(PageWidth * unit, MathF.Max(0f, width - (margin * 2f)));
        var page = new UiRect(
            MathF.Round((width - pageWidth) * 0.5f),
            map.Y + margin,
            pageWidth,
            MathF.Max(0f, map.Height - (margin * 2f)));

        return new TacticalComputerChrome(top, bottom, map, page, pageVisible, unit, line);
    }

    /// <summary>Determines whether a point lies inside a rectangle, edges included.</summary>
    /// <param name="rect">The rectangle.</param>
    /// <param name="x">The horizontal position in pixels.</param>
    /// <param name="y">The vertical position in pixels.</param>
    /// <returns>Whether it does.</returns>
    public static bool Contains(UiRect rect, float x, float y) =>
        x >= rect.X && x <= rect.Right && y >= rect.Y && y <= rect.Bottom;
}
