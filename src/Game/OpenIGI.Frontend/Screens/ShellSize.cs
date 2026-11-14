using OpenIGI.UI.Layout;

namespace OpenIGI.Frontend.Screens;

/// <summary>Which of its three arrangements a shell page takes on a screen.</summary>
public enum ShellSize
{
    /// <summary>A desktop monitor: the full page, its columns side by side.</summary>
    Wide,

    /// <summary>A small laptop, a 4:3 screen or a tablet: the same page with less room, so fewer columns and tighter margins.</summary>
    Medium,

    /// <summary>A phone on its side or a small window: a rail of sections down the left and one scrolling column.</summary>
    Compact,
}

/// <summary>Reads a page's arrangement, and the measures that follow from it, off the canvas it is laid out on.</summary>
/// <remarks>
/// The canvas is the screen in design units, which <see cref="OpenIGI.Core.Graphics.UiScale.MenuBoost(float, float, float)"/>
/// has already sized for the display: 1920 by 1080 on a monitor, about 1600 by 900 on a laptop, and the pixels the
/// person sees on a phone. So the canvas alone says which page fits, and a window dragged across a boundary is
/// rebuilt as the other.
/// </remarks>
public static class ShellSizes
{
    /// <summary>The canvas width under which a page is compact, in design units.</summary>
    public const float CompactBelowWidth = 1100f;

    /// <summary>The canvas height under which a page is compact, in design units.</summary>
    public const float CompactBelowHeight = 620f;

    /// <summary>The canvas width from which a page is wide, in design units.</summary>
    public const float WideFromWidth = 1800f;

    /// <summary>Gets the arrangement for a layout.</summary>
    /// <param name="context">The layout.</param>
    /// <returns>The arrangement.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    public static ShellSize Of(LayoutContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        UiVec2 canvas = Canvas(context);
        if (canvas.X < CompactBelowWidth || canvas.Y < CompactBelowHeight)
        {
            return ShellSize.Compact;
        }

        return canvas.X >= WideFromWidth ? ShellSize.Wide : ShellSize.Medium;
    }

    /// <summary>Gets the canvas a layout gives its pages, in design units.</summary>
    /// <param name="context">The layout.</param>
    /// <returns>The canvas's width and height.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    public static UiVec2 Canvas(LayoutContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new UiVec2(context.SafeRect.Width / context.Scale, context.SafeRect.Height / context.Scale);
    }

    /// <summary>Gives the shell's theme the measures of an arrangement.</summary>
    /// <param name="theme">The shell's theme.</param>
    /// <param name="size">The arrangement.</param>
    /// <remarks>
    /// The shell's rows take the large look everywhere. On a monitor a row is 64 units with 20-unit text, and its
    /// explanation is shown beside the list; on a compact page, authored in seen pixels, it is 56 with 16-unit text --
    /// still a thumb tall -- and the row in focus carries its own explanation under its caption.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="theme"/> is <see langword="null"/>.</exception>
    public static void Apply(OpenIGI.UI.Widgets.UiTheme theme, ShellSize size)
    {
        ArgumentNullException.ThrowIfNull(theme);
        bool compact = size == ShellSize.Compact;
        theme.Large = true;
        theme.RowDividers = true;
        theme.BodyTextSize = compact ? 16f : 20f;
        theme.RowHeight = compact ? 56f : 64f;
        theme.RowInset = compact ? 16f : 20f;
        theme.RowHints = OpenIGI.UI.Widgets.UiRowHints.Always;
    }

    /// <summary>Gets a value indicating whether a theme carries a compact page's measures.</summary>
    /// <param name="theme">The shell's theme, as <see cref="Apply"/> left it.</param>
    /// <returns><see langword="true"/> on a compact page.</returns>
    /// <remarks>For what is built from the theme alone, with no page to ask.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="theme"/> is <see langword="null"/>.</exception>
    public static bool IsCompact(OpenIGI.UI.Widgets.UiTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return theme.BodyTextSize < 18f;
    }

    /// <summary>Gets the margin either side of a page, in design units.</summary>
    /// <param name="size">The arrangement.</param>
    /// <returns>The margin.</returns>
    public static float SideMargin(ShellSize size) => size switch
    {
        ShellSize.Wide => 80f,
        ShellSize.Medium => 56f,
        _ => 16f,
    };
}
