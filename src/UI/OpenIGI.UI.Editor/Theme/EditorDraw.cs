using OpenIGI.Core.Graphics;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Editor.Theme;

/// <summary>The shapes every editor widget is drawn from, so they share one look.</summary>
public static class EditorDraw
{
    /// <summary>Fills a rectangle with rounded corners, or square ones when the theme has no surfaces.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="rect">The rectangle.</param>
    /// <param name="color">The fill.</param>
    /// <param name="radius">The corner radius; <see cref="EditorTheme.Radius"/> when omitted.</param>
    public static void Fill(UiPainter painter, EditorTheme theme, UiRect rect, Bgra32 color, float? radius = null)
    {
        ArgumentNullException.ThrowIfNull(painter);
        ArgumentNullException.ThrowIfNull(theme);
        if (theme.Surfaces is { } surfaces)
        {
            painter.FillRoundedRect(rect, surfaces.Rounded, color, radius ?? theme.Radius);
        }
        else
        {
            painter.FillRect(rect, color);
        }
    }

    /// <summary>Fills a rounded rectangle with a border: a field, a button, a popup.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="rect">The rectangle.</param>
    /// <param name="fill">The fill.</param>
    /// <param name="border">The border colour.</param>
    /// <param name="radius">The corner radius; <see cref="EditorTheme.Radius"/> when omitted.</param>
    public static void Frame(UiPainter painter, EditorTheme theme, UiRect rect, Bgra32 fill, Bgra32 border, float? radius = null)
    {
        ArgumentNullException.ThrowIfNull(theme);
        float r = radius ?? theme.Radius;
        float line = theme.Hairline;

        // The border is the outer shape and the fill an inset copy of it, which keeps the corners concentric.
        Fill(painter, theme, rect, border, r);
        Fill(painter, theme, rect.Deflate(new Thickness(line)), fill, MathF.Max(0f, r - line));
    }

    /// <summary>Draws the soft shadow that lifts a popup off the panels below.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="rect">The rectangle casting it.</param>
    public static void Shadow(UiPainter painter, EditorTheme theme, UiRect rect)
    {
        ArgumentNullException.ThrowIfNull(painter);
        ArgumentNullException.ThrowIfNull(theme);
        if (theme.Surfaces is { } surfaces)
        {
            painter.DrawShadow(rect, surfaces.Shadow, theme.Shadow, theme.Px(10f), new UiVec2(0f, theme.Px(3f)));
        }
    }

    /// <summary>Draws a one-line label vertically centred in a box, clipped with an ellipsis when it does not fit.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="text">The text.</param>
    /// <param name="box">The box; the text starts at its left edge.</param>
    /// <param name="color">The colour.</param>
    /// <param name="strong">Whether to use the strong face.</param>
    /// <returns>The width drawn.</returns>
    public static float Label(UiPainter painter, EditorTheme theme, string text, UiRect box, Bgra32 color, bool strong = false)
    {
        ArgumentNullException.ThrowIfNull(painter);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(text);
        Text.UiFont font = strong ? theme.StrongFont : theme.Font;
        float size = theme.TextSize;
        string shown = Fit(font, text, size, box.Width);
        float y = MathF.Round(box.Y + ((box.Height - font.LineHeight(size)) * 0.5f));
        painter.DrawText(font, shown, new UiVec2(MathF.Round(box.X), y), size, color);
        return font.MeasureWidth(shown, size);
    }

    /// <summary>Draws an icon from the shared vector set, centred in a box.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="icon">The icon.</param>
    /// <param name="box">The box.</param>
    /// <param name="color">The colour; the theme's icon colour when omitted.</param>
    public static void Icon(UiPainter painter, EditorTheme theme, KitIcon icon, UiRect box, Bgra32? color = null)
    {
        ArgumentNullException.ThrowIfNull(theme);
        if (icon != KitIcon.None)
        {
            KitIcons.Draw(painter, icon, box.Center, theme.IconSize, color ?? theme.Icon);
        }
    }

    /// <summary>Shortens text with a trailing ellipsis until it fits a width.</summary>
    /// <param name="font">The font.</param>
    /// <param name="text">The text.</param>
    /// <param name="size">The size in pixels.</param>
    /// <param name="width">The width available.</param>
    /// <returns>The text, or a shortened copy ending in an ellipsis.</returns>
    public static string Fit(Text.UiFont font, string text, float size, float width)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(text);
        // Half a pixel of slack: boxes are sized from the same measurement, and rounding must not ellipsise them.
        width += 0.5f;
        if (font.MeasureWidth(text, size) <= width)
        {
            return text;
        }

        const string ellipsis = "…";
        int low = 0;
        int high = text.Length;
        while (low < high)
        {
            int mid = (low + high + 1) / 2;
            if (font.MeasureWidth(string.Concat(text.AsSpan(0, mid), ellipsis), size) <= width)
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }

        return low == 0 ? string.Empty : string.Concat(text.AsSpan(0, low), ellipsis);
    }
}
