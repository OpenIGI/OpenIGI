namespace OpenIGI.UI.Layout;

/// <summary>Where a popup opens relative to the rectangle it belongs to.</summary>
public enum PopupPlacement
{
    /// <summary>Below the anchor, left edges aligned: a dropdown or a menu-bar menu.</summary>
    Below,

    /// <summary>To the right of the anchor, top edges aligned: a submenu.</summary>
    Right,

    /// <summary>At the anchor's top-left corner: a context menu at the pointer.</summary>
    At,
}

/// <summary>Places popups (menus, dropdown lists, context menus) against what they belong to, on screen.</summary>
public static class PopupLayout
{
    /// <summary>Places a popup against its anchor, keeping it inside the viewport.</summary>
    /// <param name="anchor">What it belongs to: a field, a menu title, a menu row, or the pointer as an empty rectangle.</param>
    /// <param name="placement">Which side it prefers.</param>
    /// <param name="size">Its size.</param>
    /// <param name="viewport">The viewport's size.</param>
    /// <returns>Its frame, in whole pixels.</returns>
    /// <remarks>
    /// When the preferred side runs off screen it flips to the anchor's other side rather than being pushed back over
    /// the anchor: a submenu opens to the left of its row, a list above its field, and a menu opened near the bottom
    /// right corner opens above and to the left of the pointer, which is what every desktop does and keeps the pointer
    /// beside the menu instead of on it. Whatever still does not fit is clamped to the viewport.
    /// </remarks>
    public static UiRect Place(UiRect anchor, PopupPlacement placement, UiVec2 size, UiVec2 viewport)
    {
        float width = MathF.Min(size.X, viewport.X);
        float height = MathF.Min(size.Y, viewport.Y);
        (float x, float y) = placement switch
        {
            PopupPlacement.Right => (anchor.Right, anchor.Y),
            PopupPlacement.At => (anchor.X, anchor.Y),
            _ => (anchor.X, anchor.Bottom),
        };

        if (x + width > viewport.X)
        {
            // A list below its field keeps its left edge with the field's as far as it can; the others change sides.
            x = placement == PopupPlacement.Below ? viewport.X - width : anchor.X - width;
        }

        if (y + height > viewport.Y)
        {
            y = placement == PopupPlacement.Right ? viewport.Y - height : anchor.Y - height;
        }

        return new UiRect(
            MathF.Round(Math.Clamp(x, 0f, MathF.Max(0f, viewport.X - width))),
            MathF.Round(Math.Clamp(y, 0f, MathF.Max(0f, viewport.Y - height))),
            width,
            height);
    }
}
