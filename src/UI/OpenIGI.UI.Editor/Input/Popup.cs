using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Editor.Input;

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

/// <summary>
/// An element that floats above everything else: a menu, a dropdown list, a picker.
/// </summary>
/// <remarks>
/// A popup measures itself, is placed against its anchor and flipped or pushed to stay on screen, and closes
/// when the user presses anywhere outside every open popup (light dismiss) or presses Escape.
/// </remarks>
public abstract class Popup : EditorElement
{
    /// <summary>Initializes a new instance of the <see cref="Popup"/> class.</summary>
    /// <param name="theme">The theme.</param>
    protected Popup(EditorTheme theme)
        : base(theme)
    {
    }

    /// <summary>Raised once the popup has closed.</summary>
    public event Action<Popup>? Closed;

    /// <summary>Gets or sets the rectangle the popup belongs to.</summary>
    public UiRect Anchor { get; set; }

    /// <summary>Gets or sets where the popup opens relative to <see cref="Anchor"/>.</summary>
    public PopupPlacement Placement { get; set; } = PopupPlacement.Below;

    /// <summary>Gets or sets the popup that opened this one, which stays open while this one is (a submenu's menu).</summary>
    public Popup? Owner { get; set; }

    /// <summary>Gets the rectangle the popup was placed in.</summary>
    public UiRect Frame { get; private set; }

    /// <summary>Gets whether a press outside the popup is swallowed after closing it, as menus do.</summary>
    public virtual bool SwallowsDismissingPress => true;

    /// <summary>Places the popup against its anchor, keeping it inside the viewport.</summary>
    /// <param name="size">The popup's measured size.</param>
    /// <param name="viewport">The viewport size.</param>
    /// <returns>The frame.</returns>
    internal UiRect Place(UiVec2 size, UiVec2 viewport)
    {
        float width = MathF.Min(size.X, viewport.X);
        float height = MathF.Min(size.Y, viewport.Y);
        (float x, float y) = Placement switch
        {
            PopupPlacement.Right => (Anchor.Right, Anchor.Y),
            PopupPlacement.At => (Anchor.X, Anchor.Y),
            _ => (Anchor.X, Anchor.Bottom),
        };

        // Flip to the other side when the preferred one runs off screen, then clamp what still does not fit.
        if (x + width > viewport.X)
        {
            x = Placement == PopupPlacement.Right ? Anchor.X - width : viewport.X - width;
        }

        if (y + height > viewport.Y)
        {
            y = Placement == PopupPlacement.Below ? Anchor.Y - height : viewport.Y - height;
        }

        Frame = new UiRect(MathF.Round(MathF.Max(0f, x)), MathF.Round(MathF.Max(0f, y)), width, height);
        return Frame;
    }

    /// <summary>Closes the popup.</summary>
    public void Close() => Root?.ClosePopup(this);

    internal void RaiseClosed() => Closed?.Invoke(this);

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        EditorDraw.Shadow(painter, Theme, Bounds);
        EditorDraw.Frame(painter, Theme, Bounds, Theme.Popup, Theme.BorderStrong);
    }
}
