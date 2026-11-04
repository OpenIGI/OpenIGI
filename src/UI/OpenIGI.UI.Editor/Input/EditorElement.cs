using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Editor.Input;

/// <summary>
/// An element that takes desktop input: hover, presses with capture, wheel, keyboard focus and text.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="EditorRoot"/> routes each event to the deepest element under the pointer and bubbles it up
/// the parents until one handles it. An element that handles a press captures the pointer: it receives every
/// move and the release, wherever the pointer goes, until the button comes up. That is what makes splitters,
/// scrubbing number fields and gizmo drags behave.
/// </para>
/// <para>
/// Keys go to the focused element first and bubble the same way; whatever nothing handles is offered to the
/// shortcut table.
/// </para>
/// </remarks>
public abstract class EditorElement : UiElement
{
    /// <summary>Initializes a new instance of the <see cref="EditorElement"/> class.</summary>
    /// <param name="theme">The theme.</param>
    protected EditorElement(EditorTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        Theme = theme;
    }

    /// <summary>Gets the theme.</summary>
    public EditorTheme Theme { get; }

    /// <summary>Gets whether the pointer is over this element (or a child that did not take the hover).</summary>
    public bool IsPointerOver { get; internal set; }

    /// <summary>Gets whether this element has the pointer captured.</summary>
    public bool IsCapturing { get; internal set; }

    /// <summary>Gets whether this element has keyboard focus.</summary>
    public bool IsFocused => Root?.Focused == this;

    /// <summary>Gets whether this element or one of its descendants has keyboard focus.</summary>
    public bool IsFocusWithin => Root?.Focused is { } focused && focused.IsWithin(this);

    /// <summary>Gets or sets the tooltip shown when the pointer rests on the element.</summary>
    public string? Tooltip { get; set; }

    /// <summary>Gets or sets whether the element is enabled; a disabled element takes no input and draws dimmed.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Gets whether a press on the element gives it keyboard focus.</summary>
    public virtual bool Focusable => false;

    /// <summary>
    /// Gets whether a left press gives the element focus before it sees the press. A field that turns a press into
    /// a drag (a scrubbing number field) answers <see langword="false"/> and focuses itself on a plain click.
    /// </summary>
    public virtual bool FocusOnPress => Focusable;

    /// <summary>
    /// Gets whether the element takes typed text while focused, so plain letter keys belong to it and are never
    /// offered to single-key shortcuts.
    /// </summary>
    public virtual bool AcceptsText => false;

    /// <summary>Gets the pointer shape over the element.</summary>
    public virtual EditorCursor Cursor => EditorCursor.Arrow;

    /// <summary>Gets the root the element is attached to, or <see langword="null"/> while detached.</summary>
    protected EditorRoot? Root => this as EditorRoot ?? FindAncestor<EditorRoot>();

    /// <summary>Takes keyboard focus, if the element is attached.</summary>
    public void Focus() => Root?.SetFocus(this);

    /// <summary>Handles a press. Returning <see langword="true"/> claims it and captures the pointer.</summary>
    /// <param name="e">The press.</param>
    /// <returns>Whether the press was handled.</returns>
    protected internal virtual bool OnPointerDown(in PointerEvent e) => false;

    /// <summary>Handles a move: while captured, wherever the pointer is; otherwise while hovered.</summary>
    /// <param name="e">The move.</param>
    protected internal virtual void OnPointerMove(in PointerEvent e)
    {
    }

    /// <summary>Handles the release that ends a capture.</summary>
    /// <param name="e">The release.</param>
    protected internal virtual void OnPointerUp(in PointerEvent e)
    {
    }

    /// <summary>Handles the loss of a capture without a release: the window lost focus, Escape cancelled the drag.</summary>
    protected internal virtual void OnCaptureLost()
    {
    }

    /// <summary>Handles the wheel.</summary>
    /// <param name="position">Where the pointer is.</param>
    /// <param name="notches">Notches turned; positive is away from the user.</param>
    /// <param name="modifiers">The modifiers held.</param>
    /// <returns>Whether the wheel was used; if not, it bubbles to the parent.</returns>
    protected internal virtual bool OnWheel(UiVec2 position, float notches, KeyModifiers modifiers) => false;

    /// <summary>Handles a key press or repeat while this element, or a descendant that passed it on, has focus.</summary>
    /// <param name="e">The key.</param>
    /// <returns>Whether the key was used.</returns>
    protected internal virtual bool OnKeyDown(in KeyEvent e) => false;

    /// <summary>Handles a typed character while this element has focus.</summary>
    /// <param name="character">The character.</param>
    /// <returns>Whether it was used.</returns>
    protected internal virtual bool OnText(char character) => false;

    /// <summary>Called when the pointer arrives or leaves.</summary>
    /// <param name="over">Whether it is now over the element.</param>
    protected internal virtual void OnPointerOverChanged(bool over)
    {
    }

    /// <summary>Called when the element gains or loses keyboard focus.</summary>
    /// <param name="focused">Whether it now has focus.</param>
    protected internal virtual void OnFocusChanged(bool focused)
    {
    }
}
