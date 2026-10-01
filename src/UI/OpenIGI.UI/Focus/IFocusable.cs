using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Focus;

/// <summary>
/// A widget that can hold focus and be reached by directional traversal.
/// </summary>
/// <remarks>
/// The focus model is deliberately narrow: the manager needs only where a widget is (to pick the
/// nearest neighbour in a pressed direction), whether it currently wants focus, and a way to tell it
/// that focus arrived or left. Everything else — how it looks focused, what activating it does — is the
/// widget's own business.
/// </remarks>
public interface IFocusable
{
    /// <summary>Gets the rectangle used to place the widget for directional traversal, in pixels.</summary>
    UiRect FocusBounds { get; }

    /// <summary>Gets the region a pointer can hit, in pixels.</summary>
    /// <remarks>
    /// The same rectangle as <see cref="FocusBounds"/> for a widget nothing clips, and the clipped part of
    /// it for one inside a scrolling viewport. The two are separate on purpose: a row scrolled out of sight
    /// must not answer a click where it would be, but pressing down must still be able to reach it.
    /// </remarks>
    UiRect PointerBounds => FocusBounds;

    /// <summary>Gets a value indicating whether the widget can take focus right now.</summary>
    /// <remarks><see langword="false"/> for a disabled, hidden or collapsed widget.</remarks>
    bool CanFocus { get; }

    /// <summary>Called when focus arrives or leaves.</summary>
    /// <param name="focused">Whether the widget now holds focus.</param>
    void SetFocused(bool focused);
}
