using OpenIGI.Input;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Editor.Input;

/// <summary>The modifier keys held during an event.</summary>
[Flags]
public enum KeyModifiers
{
    /// <summary>No modifier.</summary>
    None = 0,

    /// <summary>Either Shift key.</summary>
    Shift = 1,

    /// <summary>Either Control key.</summary>
    Control = 2,

    /// <summary>Either Alt key.</summary>
    Alt = 4,
}

/// <summary>A pointer press, move or release.</summary>
/// <param name="Position">Where the pointer is, in pixels.</param>
/// <param name="Button">The button pressed or released; for a move, the button the drag holds, if any.</param>
/// <param name="ClickCount">1 for a single press, 2 for a double click, 3 for a triple.</param>
/// <param name="Modifiers">The modifiers held.</param>
public readonly record struct PointerEvent(UiVec2 Position, MouseButton Button, int ClickCount, KeyModifiers Modifiers)
{
    /// <summary>Gets whether Shift is held.</summary>
    public bool Shift => (Modifiers & KeyModifiers.Shift) != 0;

    /// <summary>Gets whether Control is held.</summary>
    public bool Control => (Modifiers & KeyModifiers.Control) != 0;

    /// <summary>Gets whether Alt is held.</summary>
    public bool Alt => (Modifiers & KeyModifiers.Alt) != 0;
}

/// <summary>A key press, or an auto-repeat of a held one.</summary>
/// <param name="Key">The key.</param>
/// <param name="Modifiers">The modifiers held.</param>
/// <param name="IsRepeat">Whether this is an auto-repeat rather than the press itself.</param>
public readonly record struct KeyEvent(KeyCode Key, KeyModifiers Modifiers, bool IsRepeat = false)
{
    /// <summary>Gets whether Shift is held.</summary>
    public bool Shift => (Modifiers & KeyModifiers.Shift) != 0;

    /// <summary>Gets whether Control is held.</summary>
    public bool Control => (Modifiers & KeyModifiers.Control) != 0;

    /// <summary>Gets whether Alt is held.</summary>
    public bool Alt => (Modifiers & KeyModifiers.Alt) != 0;
}

/// <summary>The shape the pointer should take over an element.</summary>
public enum EditorCursor
{
    /// <summary>The ordinary arrow.</summary>
    Arrow,

    /// <summary>A text caret: over a text field.</summary>
    Text,

    /// <summary>A hand: over a link or something to grab.</summary>
    Hand,

    /// <summary>Horizontal resize: a vertical splitter.</summary>
    ResizeHorizontal,

    /// <summary>Vertical resize: a horizontal splitter.</summary>
    ResizeVertical,

    /// <summary>Move in any direction.</summary>
    Move,
}
