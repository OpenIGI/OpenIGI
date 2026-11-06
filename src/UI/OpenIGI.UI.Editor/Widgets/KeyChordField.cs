using OpenIGI.Input;
using OpenIGI.UI.Editor.Actions;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Editor.Widgets;

/// <summary>
/// A shortcut, shown as its keys: click it and press the new keys to rebind, Escape to keep the old ones, Backspace to
/// leave it unbound.
/// </summary>
public sealed class KeyChordField : EditorElement
{
    private bool _listening;

    /// <summary>Initializes a new instance of the <see cref="KeyChordField"/> class.</summary>
    /// <param name="theme">The theme.</param>
    public KeyChordField(EditorTheme theme)
        : base(theme)
    {
    }

    /// <summary>Raised when a new shortcut is chosen, or <see langword="null"/> for none.</summary>
    public event Action<KeyChord?>? Changed;

    /// <summary>Gets or sets the shortcut shown.</summary>
    public KeyChord? Chord { get; set; }

    /// <summary>Gets or sets whether the shortcut differs from its default, which marks it.</summary>
    public bool IsModified { get; set; }

    /// <inheritdoc/>
    public override bool Focusable => true;

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) =>
        new(float.IsFinite(available.X) ? available.X : Theme.Px(140f), Theme.ControlHeight);

    /// <inheritdoc/>
    protected internal override bool OnPointerDown(in PointerEvent e)
    {
        if (e.Button != MouseButton.Left)
        {
            return false;
        }

        _listening = true;
        Focus();
        return true;
    }

    /// <inheritdoc/>
    protected internal override void OnFocusChanged(bool focused)
    {
        if (!focused)
        {
            _listening = false;
        }
    }

    /// <inheritdoc/>
    protected internal override bool OnKeyDown(in KeyEvent e)
    {
        if (!_listening)
        {
            return false;
        }

        switch (e.Key)
        {
            case KeyCode.LeftShift or KeyCode.RightShift or KeyCode.LeftControl or KeyCode.RightControl or KeyCode.LeftAlt or KeyCode.RightAlt:
                // A modifier alone is the start of a chord, not one.
                return true;
            case KeyCode.Escape:
                _listening = false;
                return true;
            case KeyCode.Backspace when e.Modifiers == KeyModifiers.None:
                _listening = false;
                Chord = null;
                Changed?.Invoke(null);
                return true;
        }

        _listening = false;
        Chord = new KeyChord(e.Key, e.Modifiers);
        Changed?.Invoke(Chord);
        return true;
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        EditorDraw.Frame(painter, Theme, Bounds, Theme.Input, _listening ? Theme.Accent : IsPointerOver ? Theme.BorderStrong : Theme.Border);
        string text = _listening ? "Press keys…  (Esc keeps, Backspace clears)" : Chord?.ToString() ?? "—";
        EditorDraw.Label(
            painter,
            Theme,
            text,
            Bounds.Deflate(new Thickness(Theme.Px(8f), 0f)),
            _listening ? Theme.Accent : Chord is null ? Theme.TextDisabled : IsModified ? Theme.Text : Theme.TextMuted,
            strong: IsModified && !_listening);
    }
}
