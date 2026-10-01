using OpenIGI.Core.Graphics;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Widgets;

/// <summary>
/// The shared look of a settings row: a caption on the left, its control on the right, and a faint wash
/// under the whole row while the pointer or focus is on it.
/// </summary>
/// <remarks>
/// Every row-shaped control — toggle, slider, select, key binding — draws its frame through here, so a
/// column of mixed rows lines up to the pixel and lights the same way whichever kind is under the cursor.
/// The design defines only a hover state; focus from the keyboard or a pad looks exactly like it, which is
/// what lets a player who switches between mouse and pad never lose track of where they are.
/// </remarks>
public static class UiRowStyle
{
    /// <summary>A row's height, in design units.</summary>
    public const float Height = 46f;

    /// <summary>The inset of a row's caption and control from its edges, in design units.</summary>
    public const float Inset = 10f;

    /// <summary>How lit a row is: 1 under the pointer or focus, easing in and out with focus.</summary>
    /// <param name="control">The row.</param>
    /// <returns>The highlight, 0 to 1.</returns>
    public static float Highlight(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return MathF.Max(control.Visual.Focus, control.IsPointerOver ? 1f : 0f);
    }

    /// <summary>Measures a row: the available width, and the theme's row height or the caption's, whichever is taller.</summary>
    /// <param name="control">The row.</param>
    /// <param name="context">The layout context.</param>
    /// <param name="available">The space offered.</param>
    /// <returns>The row's size.</returns>
    public static UiVec2 Measure(Control control, LayoutContext context, UiVec2 available)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(context);
        UiTheme theme = control.Theme;
        float caption = theme.Body.LineHeight(context.Px(theme.BodyTextSize)) + context.Px(12f);
        if (control.Hint.Length > 0)
        {
            caption += context.Px(3f) + theme.Body.LineHeight(context.Px(HintSize(theme)));
        }

        return new UiVec2(available.X, MathF.Max(context.Px(theme.RowHeight), caption));
    }

    /// <summary>Draws the row's hover wash and its caption.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="control">The row.</param>
    /// <param name="caption">The caption, set on the left.</param>
    /// <param name="highlight">How lit the row is, from <see cref="Highlight"/>.</param>
    public static void DrawFrame(UiPainter painter, Control control, string caption, float highlight)
    {
        ArgumentNullException.ThrowIfNull(painter);
        ArgumentNullException.ThrowIfNull(control);
        UiTheme theme = control.Theme;
        float appear = control.Visual.Appear;

        if (highlight > 0.001f)
        {
            painter.FillRect(control.Bounds, UiPalette.Fade(theme.FocusFill, highlight * appear));
        }

        UiRect bounds = control.Bounds;
        if (theme.RowDividers)
        {
            float t = painter.Context.Px(1f);
            painter.FillRect(new UiRect(bounds.X, bounds.Bottom - t, bounds.Width, t), UiPalette.White(0.05f * appear));
        }

        if (caption.Length == 0)
        {
            return;
        }

        float sizePx = painter.Context.Px(theme.BodyTextSize);
        Bgra32 color = !control.IsEnabled ? theme.TextDisabled : Interpolate.Lerp(theme.TextLabel, theme.TextBright, highlight);
        float x = bounds.X + painter.Context.Px(Inset);
        if (control.Hint.Length == 0)
        {
            float y = bounds.Y + ((bounds.Height - theme.Body.LineHeight(sizePx)) * 0.5f);
            painter.DrawText(theme.Body, caption, new UiVec2(x, y), sizePx, UiPalette.Fade(color, appear));
            return;
        }

        // A caption with a quieter line of explanation under it, the pair centred in the row.
        float hintPx = painter.Context.Px(HintSize(theme));
        float gap = painter.Context.Px(3f);
        float block = theme.Body.LineHeight(sizePx) + gap + theme.Body.LineHeight(hintPx);
        float top = bounds.Y + ((bounds.Height - block) * 0.5f);
        painter.DrawText(theme.Body, caption, new UiVec2(x, top), sizePx, UiPalette.Fade(color, appear));
        painter.DrawText(theme.Body, control.Hint, new UiVec2(x, top + theme.Body.LineHeight(sizePx) + gap), hintPx, UiPalette.Fade(theme.TextMuted, appear));
    }

    private static float HintSize(UiTheme theme) => theme.BodyTextSize * 0.89f;

    /// <summary>Gets the y of text centred vertically in a box.</summary>
    /// <param name="font">The font.</param>
    /// <param name="sizePx">The text size in pixels.</param>
    /// <param name="box">The box.</param>
    /// <returns>The top of the line.</returns>
    public static float CenterY(Text.UiFont font, float sizePx, UiRect box)
    {
        ArgumentNullException.ThrowIfNull(font);
        return box.Y + ((box.Height - font.LineHeight(sizePx)) * 0.5f);
    }

    /// <summary>
    /// Gets the y of uppercase text whose capitals sit centred in a box — what CSS's line box does for a
    /// label in a flex row, and what keeps a caption from riding high on its descender space.
    /// </summary>
    /// <param name="font">The font.</param>
    /// <param name="sizePx">The text size in pixels.</param>
    /// <param name="box">The box.</param>
    /// <returns>The top of the line.</returns>
    public static float CapsCenterY(Text.UiFont font, float sizePx, UiRect box)
    {
        ArgumentNullException.ThrowIfNull(font);

        // Capitals stand about 0.7 of the size above the baseline in the faces the design uses.
        float capHeight = sizePx * 0.7f;
        float baseline = box.Y + ((box.Height + capHeight) * 0.5f);
        return baseline - font.Ascent(sizePx);
    }
}
