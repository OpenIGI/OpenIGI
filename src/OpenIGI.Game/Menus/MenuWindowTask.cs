using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Menus;

/// <summary>
/// The base every menu type derives from: a rectangle, a line-up rule and the arrange pass that
/// applies it.
/// </summary>
/// <remarks>
/// <para>
/// The original registers this as <c>MenuWindow</c> with an instance size of 84 (<c>0x423FC0</c>), and
/// it owns the layout: <see cref="Arrange"/> is <c>0x4244B0</c>, <see cref="Measure"/> is the slot
/// whose base implementation is <c>0x424400</c>, and <see cref="Place"/> is <c>0x424420</c>.
/// </para>
/// <para>
/// Field offsets in the original, for anyone checking this against the binary: <c>+32</c> X,
/// <c>+36</c> Y, <c>+40</c> width, <c>+44</c> height, <c>+48</c> X-spacing, <c>+52</c> Y-spacing,
/// <c>+56</c> line-up mode, <c>+60</c> centre-horizontally, <c>+64</c> centre-vertically.
/// </para>
/// </remarks>
public abstract class MenuWindowTask : ContainerTask
{
    /// <summary>The spacing a menu window starts with on both axes.</summary>
    /// <remarks><c>MenuWindow</c>'s constructor (<c>0x4241C0</c>) writes 4 to both; everything else it touches starts at zero.</remarks>
    public const int DefaultSpacing = 4;

    /// <summary>Initializes a new instance of the <see cref="MenuWindowTask"/> class.</summary>
    protected MenuWindowTask()
    {
        LayoutSpacingX = DefaultSpacing;
        LayoutSpacingY = DefaultSpacing;
    }

    /// <summary>Gets or sets the left edge in authored pixels.</summary>
    public int LayoutX { get; set; }

    /// <summary>Gets or sets the top edge in authored pixels.</summary>
    public int LayoutY { get; set; }

    /// <summary>Gets or sets the width in authored pixels.</summary>
    public int LayoutWidth { get; set; }

    /// <summary>Gets or sets the height in authored pixels.</summary>
    public int LayoutHeight { get; set; }

    /// <summary>Gets or sets the horizontal gap between children.</summary>
    public int LayoutSpacingX { get; set; }

    /// <summary>Gets or sets the vertical gap between children.</summary>
    public int LayoutSpacingY { get; set; }

    /// <summary>Gets or sets how this window arranges its children.</summary>
    public MenuLineUpMode LineUp { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this item centres itself horizontally in its parent
    /// when the parent's line-up does not place it on that axis.
    /// </summary>
    /// <remarks>
    /// <c>MenuText</c>'s constructor sets both this and <see cref="CentreVertically"/>, which is why a
    /// column of menu entries comes out centred in its frame without anything saying so.
    /// </remarks>
    public bool CentreHorizontally { get; set; }

    /// <summary>Gets or sets a value indicating whether this item centres itself vertically in its parent.</summary>
    public bool CentreVertically { get; set; }

    /// <summary>Reports the size this item occupies in a line-up.</summary>
    /// <param name="width">Receives the width.</param>
    /// <param name="height">Receives the height.</param>
    /// <returns><see langword="true"/> when the item takes part in the line-up.</returns>
    /// <remarks>The base reports the current rectangle, which is <c>0x424400</c> exactly.</remarks>
    public virtual bool Measure(out int width, out int height)
    {
        width = LayoutWidth;
        height = LayoutHeight;
        return true;
    }

    /// <summary>Arranges this window's children, then recurses into them.</summary>
    /// <remarks>
    /// <para>
    /// Transcribed from <c>MenuWindow_LineUp</c> (<c>0x4244B0</c>). Three passes: measure every child
    /// and total their sizes, compute a start cursor and a step from the line-up mode, then walk the
    /// children again placing each and advancing the cursor. Finally recurse, so a frame inside a
    /// frame lays out after its parent has positioned it.
    /// </para>
    /// <para>
    /// All of the arithmetic is integer, and the divisions truncate toward zero, so the leftover pixels
    /// of an uneven split land at the end rather than being distributed.
    /// </para>
    /// </remarks>
    public void Arrange()
    {
        MenuWindowTask[] children = [.. Children.OfType<MenuWindowTask>()];

        bool horizontal = false;
        bool vertical = false;
        int xCursor = 0;
        int yCursor = 0;
        int xStep = 0;
        int yStep = 0;

        if (LineUp != MenuLineUpMode.None)
        {
            int totalWidth = 0;
            int totalHeight = 0;
            int count = 0;

            foreach (MenuWindowTask child in children)
            {
                if (child.Measure(out int w, out int h))
                {
                    totalWidth += w;
                    totalHeight += h;
                    count++;
                }
            }

            switch (LineUp)
            {
                case MenuLineUpMode.VerticalTop:
                    yStep = LayoutSpacingY;
                    yCursor = LayoutY + yStep;
                    vertical = true;
                    break;

                case MenuLineUpMode.VerticalCentre:
                    yStep = (LayoutHeight - totalHeight) / (count + 1);
                    yCursor = LayoutY + yStep;
                    vertical = true;
                    break;

                case MenuLineUpMode.VerticalBottom:
                    yStep = LayoutSpacingY;
                    yCursor = LayoutY + LayoutHeight - (count * yStep) - totalHeight;
                    vertical = true;
                    break;

                case MenuLineUpMode.HorizontalLeft:
                    xStep = LayoutSpacingX;
                    xCursor = LayoutX + xStep;
                    horizontal = true;
                    break;

                case MenuLineUpMode.HorizontalCentre:
                    xStep = (LayoutWidth - totalWidth) / (count + 1);
                    xCursor = LayoutX + xStep;
                    horizontal = true;
                    break;

                case MenuLineUpMode.HorizontalRight:
                    xStep = LayoutSpacingX;
                    xCursor = LayoutX + LayoutWidth - (count * xStep) - totalWidth;
                    horizontal = true;
                    break;

                case MenuLineUpMode.VerticalFillOut:
                    yCursor = LayoutY + LayoutSpacingY;
                    yStep = (LayoutHeight - (2 * LayoutSpacingY) - totalHeight) / Math.Max(1, count - 1);
                    vertical = true;
                    break;

                case MenuLineUpMode.HorizontalFillOut:
                    xCursor = LayoutX + LayoutSpacingX;
                    xStep = (LayoutWidth - (2 * LayoutSpacingX) - totalWidth) / Math.Max(1, count - 1);
                    horizontal = true;
                    break;

                default:
                    break;
            }
        }

        foreach (MenuWindowTask child in children)
        {
            child.Place(this, horizontal, vertical, ref xCursor, ref yCursor, xStep, yStep);
        }

        foreach (MenuWindowTask child in children)
        {
            child.Arrange();
        }
    }

    /// <summary>Places this item against a running cursor, advancing it.</summary>
    /// <param name="parent">The window laying this item out.</param>
    /// <param name="horizontal">Whether the parent is placing along the horizontal axis.</param>
    /// <param name="vertical">Whether the parent is placing along the vertical axis.</param>
    /// <param name="xCursor">The horizontal cursor, advanced when <paramref name="horizontal"/> is set.</param>
    /// <param name="yCursor">The vertical cursor, advanced when <paramref name="vertical"/> is set.</param>
    /// <param name="xStep">The horizontal gap added after this item.</param>
    /// <param name="yStep">The vertical gap added after this item.</param>
    /// <exception cref="ArgumentNullException"><paramref name="parent"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Transcribed from <c>0x424420</c>. On an axis the parent is laying out, the item takes the cursor
    /// and the cursor advances by the step <em>plus the item's own size</em>. On an axis it is not, the
    /// item centres itself in the parent if its centre flag is set, and is otherwise left alone — which
    /// is what makes a vertical column of entries come out horizontally centred.
    /// </remarks>
    public void Place(
        MenuWindowTask parent,
        bool horizontal,
        bool vertical,
        ref int xCursor,
        ref int yCursor,
        int xStep,
        int yStep)
    {
        ArgumentNullException.ThrowIfNull(parent);

        if (horizontal)
        {
            LayoutX = xCursor;
            xCursor += xStep + LayoutWidth;
        }
        else if (CentreHorizontally)
        {
            LayoutX = parent.LayoutX + ((parent.LayoutWidth - LayoutWidth) >> 1);
        }

        if (vertical)
        {
            LayoutY = yCursor;
            yCursor += yStep + LayoutHeight;
        }
        else if (CentreVertically)
        {
            LayoutY = parent.LayoutY + ((parent.LayoutHeight - LayoutHeight) >> 1);
        }
    }
}
