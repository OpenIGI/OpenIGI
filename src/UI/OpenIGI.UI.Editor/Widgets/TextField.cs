using OpenIGI.Core.Configuration;
using OpenIGI.Core.Graphics;
using OpenIGI.Input;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;

namespace OpenIGI.UI.Editor.Widgets;

/// <summary>
/// A single-line text field: click to place the caret, drag to select, double-click for a word, triple for
/// everything; the shared editing keys; Enter or leaving the field commits, Escape reverts.
/// </summary>
public class TextField : EditorElement
{
    private const float BlinkPeriod = 0.53f;

    private readonly TextEditState _edit = new();
    private string _committed = string.Empty;
    private float _scroll;
    private float _blink;
    private bool _caretShown = true;

    /// <summary>Initializes a new instance of the <see cref="TextField"/> class.</summary>
    /// <param name="theme">The theme.</param>
    public TextField(EditorTheme theme)
        : base(theme)
    {
    }

    /// <summary>Raised on every edit, with the text as it now stands.</summary>
    public event Action<string>? TextChanged;

    /// <summary>Raised when the text is committed: Enter, or focus leaving the field with the text changed.</summary>
    public event Action<string>? Committed;

    /// <summary>Gets or sets the text. Setting it does not raise <see cref="Committed"/>.</summary>
    public string Text
    {
        get => _edit.Text;
        set
        {
            string text = value ?? string.Empty;
            if (text == _edit.Text && text == _committed)
            {
                return;
            }

            // A value pushed in from outside (the selection changed) must not clobber what is being typed.
            if (!IsFocused)
            {
                _edit.SetText(text);
                _committed = text;
                _scroll = 0f;
            }
        }
    }

    /// <summary>Gets or sets the faint text shown while the field is empty.</summary>
    public string? Placeholder { get; set; }

    /// <summary>Gets or sets whether the field selects all its text when it gains focus.</summary>
    public bool SelectAllOnFocus { get; set; } = true;

    /// <summary>Gets or sets whether the field shows the monospaced face, for code and paths.</summary>
    public bool Monospace { get; set; }

    /// <summary>Gets or sets a function that rejects text the field cannot hold, drawing it red while typed.</summary>
    public Func<string, bool>? Validate { get; set; }

    /// <summary>Gets the clipboard; the root's when attached.</summary>
    protected IUiClipboard? Clipboard => Root?.Clipboard;

    /// <inheritdoc/>
    public override bool Focusable => true;

    /// <inheritdoc/>
    public override EditorCursor Cursor => EditorCursor.Text;

    /// <summary>Gets the editing state, for derived fields.</summary>
    protected TextEditState Edit => _edit;

    /// <summary>Gets the area text is drawn in.</summary>
    protected virtual UiRect TextBox => Bounds.Deflate(new Thickness(Theme.Px(6f), 0f));

    private UiFont Font => Monospace ? Theme.MonoFont : Theme.Font;

    private bool IsValid => Validate?.Invoke(_edit.Text) ?? true;

    /// <summary>Commits the current text now, as Enter would.</summary>
    public void Commit()
    {
        if (!IsValid)
        {
            Revert();
            return;
        }

        if (_edit.Text != _committed)
        {
            _committed = _edit.Text;
            Committed?.Invoke(_committed);
        }
    }

    /// <summary>Throws away the typing and shows the last committed text again.</summary>
    public void Revert()
    {
        if (_edit.Text != _committed)
        {
            _edit.SetText(_committed);
            TextChanged?.Invoke(_committed);
        }
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) =>
        new(float.IsFinite(available.X) ? available.X : Theme.Px(120f), Theme.ControlHeight);

    /// <inheritdoc/>
    protected internal override bool OnPointerDown(in PointerEvent e)
    {
        if (e.Button != MouseButton.Left)
        {
            return false;
        }

        switch (e.ClickCount)
        {
            case 2:
                _edit.MoveTo(IndexAt(e.Position.X));
                _edit.SelectWord();
                break;
            case >= 3:
                _edit.SelectAll();
                break;
            default:
                _edit.MoveTo(IndexAt(e.Position.X), e.Shift);
                break;
        }

        ShowCaret();
        return true;
    }

    /// <inheritdoc/>
    protected internal override void OnPointerMove(in PointerEvent e)
    {
        if (IsCapturing && e.ClickCount == 1)
        {
            _edit.MoveTo(IndexAt(e.Position.X), extend: true);
            ShowCaret();
        }
    }

    /// <inheritdoc/>
    protected internal override bool OnKeyDown(in KeyEvent e)
    {
        switch (e.Key)
        {
            case KeyCode.Return or KeyCode.NumpadEnter:
                Commit();
                return true;
            case KeyCode.Escape when _edit.Text != _committed:
                Revert();
                return true;
        }

        if (!TextEditKeys.Apply(_edit, e.Key, e.Control, e.Shift, Clipboard, out bool changed))
        {
            return false;
        }

        ShowCaret();
        if (changed)
        {
            TextChanged?.Invoke(_edit.Text);
        }

        return true;
    }

    /// <inheritdoc/>
    protected internal override bool OnText(char character)
    {
        if (!TextEditKeys.IsPrintable(character) || !_edit.Insert(character.ToString()))
        {
            return false;
        }

        ShowCaret();
        TextChanged?.Invoke(_edit.Text);
        return true;
    }

    /// <inheritdoc/>
    protected internal override void OnFocusChanged(bool focused)
    {
        if (focused)
        {
            if (SelectAllOnFocus)
            {
                _edit.SelectAll();
            }

            ShowCaret();
        }
        else
        {
            Commit();
            _edit.ClearSelection();
            _scroll = 0f;
        }
    }

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        _blink += elapsedSeconds;
        if (_blink >= BlinkPeriod)
        {
            _blink -= BlinkPeriod;
            _caretShown = !_caretShown;
        }
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        Bgra32 border = !IsValid ? Theme.Error : IsFocused ? Theme.Accent : IsPointerOver ? Theme.BorderStrong : Theme.Border;
        EditorDraw.Frame(painter, Theme, Bounds, Theme.Input, border);

        UiRect box = TextBox;
        UiFont font = Font;
        float size = Theme.TextSize;
        float y = MathF.Round(box.Y + ((box.Height - font.LineHeight(size)) * 0.5f));
        // Only a field being typed in follows its caret; any other shows the start of its text.
        if (IsFocused)
        {
            KeepCaretVisible(font, size, box.Width);
        }
        else
        {
            _scroll = 0f;
        }

        painter.PushClip(box);
        if (_edit.Text.Length == 0 && !IsFocused && Placeholder is { } placeholder)
        {
            painter.DrawText(font, placeholder, new UiVec2(box.X, y), size, Theme.TextDisabled);
        }
        else
        {
            float origin = box.X - _scroll;
            if (IsFocused && _edit.HasSelection)
            {
                float from = origin + font.MeasureWidth(_edit.Text.AsSpan(0, _edit.SelectionStart), size);
                float to = origin + font.MeasureWidth(_edit.Text.AsSpan(0, _edit.SelectionEnd), size);
                painter.FillRect(new UiRect(from, y, to - from, font.LineHeight(size)), Theme.Selection);
            }

            painter.DrawText(font, _edit.Text, new UiVec2(MathF.Round(origin), y), size, IsEnabled ? Theme.Text : Theme.TextDisabled);
            if (IsFocused && _caretShown)
            {
                float x = MathF.Round(origin + font.MeasureWidth(_edit.Text.AsSpan(0, _edit.Caret), size));
                painter.FillRect(new UiRect(x, y, Theme.Hairline, font.LineHeight(size)), Theme.Text);
            }
        }

        painter.PopClip();
    }

    /// <summary>Gets the character index nearest an x coordinate.</summary>
    /// <param name="x">The x coordinate in pixels.</param>
    /// <returns>The caret index.</returns>
    protected int IndexAt(float x)
    {
        UiFont font = Font;
        float size = Theme.TextSize;
        float local = x - TextBox.X + _scroll;
        string text = _edit.Text;
        float previous = 0f;
        for (int i = 1; i <= text.Length; i++)
        {
            float width = font.MeasureWidth(text.AsSpan(0, i), size);
            if (local < (previous + width) * 0.5f)
            {
                return i - 1;
            }

            previous = width;
        }

        return text.Length;
    }

    private void KeepCaretVisible(UiFont font, float size, float width)
    {
        float caret = font.MeasureWidth(_edit.Text.AsSpan(0, _edit.Caret), size);
        float margin = Theme.Px(4f);
        if (caret - _scroll > width - margin)
        {
            _scroll = caret - width + margin;
        }
        else if (caret - _scroll < 0f)
        {
            _scroll = MathF.Max(0f, caret - margin);
        }
    }

    private void ShowCaret()
    {
        _blink = 0f;
        _caretShown = true;
    }
}
