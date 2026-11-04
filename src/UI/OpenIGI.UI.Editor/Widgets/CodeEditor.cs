using System.Text;
using OpenIGI.Core.Graphics;
using OpenIGI.Input;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;

namespace OpenIGI.UI.Editor.Widgets;

/// <summary>A run of a line drawn in one colour.</summary>
/// <param name="Start">The first column.</param>
/// <param name="Length">The number of characters.</param>
/// <param name="Color">The colour.</param>
public readonly record struct CodeSpan(int Start, int Length, Bgra32 Color);

/// <summary>
/// A multi-line code editor: line numbers, syntax colours from a highlighter, selection by mouse and keyboard, the
/// clipboard, auto-indent, its own undo, and an error marker. Only the lines on screen are drawn, so a script of
/// tens of thousands of lines scrolls as smoothly as a short one.
/// </summary>
public sealed class CodeEditor : EditorElement
{
    private const float BlinkPeriod = 0.53f;
    private const int UndoDepth = 200;
    private const string Indent = "    ";

    private readonly List<string> _lines = [string.Empty];
    private readonly ScrollModel _scroll = new();
    private readonly Stack<(string Text, int Line, int Column)> _undo = new();
    private readonly Stack<(string Text, int Line, int Column)> _redo = new();
    private int _line;
    private int _column;
    private int _anchorLine;
    private int _anchorColumn;
    private float _horizontal;
    private float _blink;
    private bool _caretShown = true;
    private bool _typingRun;

    /// <summary>Initializes a new instance of the <see cref="CodeEditor"/> class.</summary>
    /// <param name="theme">The theme.</param>
    public CodeEditor(EditorTheme theme)
        : base(theme)
    {
    }

    /// <summary>Raised after every edit.</summary>
    public event Action? TextChanged;

    /// <summary>Gets or sets the function that colours a line, or <see langword="null"/> for plain text.</summary>
    public Func<string, IReadOnlyList<CodeSpan>>? Highlight { get; set; }

    /// <summary>Gets or sets whether the text can be changed.</summary>
    public bool IsReadOnly { get; set; }

    /// <summary>Gets or sets the line (zero-based) an error is reported on, or <see langword="null"/>.</summary>
    public int? ErrorLine { get; set; }

    /// <summary>Gets the number of lines.</summary>
    public int LineCount => _lines.Count;

    /// <summary>Gets the caret's line, zero-based.</summary>
    public int CaretLine => _line;

    /// <summary>Gets the caret's column, zero-based.</summary>
    public int CaretColumn => _column;

    /// <summary>Gets or sets the whole text; setting it clears the undo history and puts the caret at the start.</summary>
    public string Text
    {
        get => string.Join('\n', _lines);
        set
        {
            Load(value);
            _undo.Clear();
            _redo.Clear();
            _line = _column = _anchorLine = _anchorColumn = 0;
            _scroll.Offset = 0f;
            _horizontal = 0f;
        }
    }

    /// <inheritdoc/>
    public override bool Focusable => true;

    /// <inheritdoc/>
    public override bool AcceptsText => !IsReadOnly;

    /// <inheritdoc/>
    public override EditorCursor Cursor => EditorCursor.Text;

    private bool HasSelection => _line != _anchorLine || _column != _anchorColumn;

    private float LineHeight => MathF.Round(Theme.MonoFont.LineHeight(Theme.TextSize) * 1.35f);

    private float Gutter => Theme.MonoFont.MeasureWidth(new string('0', Math.Max(3, _lines.Count.ToString(System.Globalization.CultureInfo.InvariantCulture).Length)), Theme.TextSize) + Theme.Px(22f);

    private float CharWidth => Theme.MonoFont.MeasureWidth("0", Theme.TextSize);

    /// <summary>Moves the caret to a line and scrolls it into view.</summary>
    /// <param name="line">The line, zero-based.</param>
    /// <param name="column">The column.</param>
    public void GoTo(int line, int column = 0)
    {
        _line = _anchorLine = Math.Clamp(line, 0, _lines.Count - 1);
        _column = _anchorColumn = Math.Clamp(column, 0, _lines[_line].Length);
        RevealCaret(centre: true);
    }

    /// <summary>Reverses the last edit.</summary>
    /// <returns>Whether there was one.</returns>
    public bool Undo() => Swap(_undo, _redo);

    /// <summary>Reapplies the last undone edit.</summary>
    /// <returns>Whether there was one.</returns>
    public bool Redo() => Swap(_redo, _undo);

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => available;

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect) =>
        _scroll.Measure((_lines.Count + 3) * LineHeight, contentRect.Height);

    /// <inheritdoc/>
    protected internal override bool OnPointerDown(in PointerEvent e)
    {
        if (e.Button != MouseButton.Left)
        {
            return false;
        }

        if (_scroll.TryGrab(Theme, Bounds, e.Position))
        {
            return true;
        }

        (int line, int column) = PositionAt(e.Position);
        _line = line;
        _column = column;
        if (e.ClickCount == 2)
        {
            (_anchorColumn, _column) = WordAround(_lines[line], column);
            _anchorLine = line;
        }
        else if (e.ClickCount >= 3)
        {
            _anchorLine = line;
            _anchorColumn = 0;
            _column = _lines[line].Length;
        }
        else if (!e.Shift)
        {
            _anchorLine = line;
            _anchorColumn = column;
        }

        ShowCaret();
        return true;
    }

    /// <inheritdoc/>
    protected internal override void OnPointerMove(in PointerEvent e)
    {
        if (_scroll.FollowGrab(Theme, Bounds, e.Position) || !IsCapturing || e.ClickCount > 1)
        {
            return;
        }

        (_line, _column) = PositionAt(e.Position);
        RevealCaret(centre: false);
    }

    /// <inheritdoc/>
    protected internal override void OnPointerUp(in PointerEvent e) => _scroll.ReleaseGrab();

    /// <inheritdoc/>
    protected internal override bool OnWheel(UiVec2 position, float notches, KeyModifiers modifiers) =>
        _scroll.Wheel(notches, LineHeight * 3f);

    /// <inheritdoc/>
    protected internal override bool OnKeyDown(in KeyEvent e)
    {
        bool shift = e.Shift;
        int page = Math.Max(1, (int)(Bounds.Height / LineHeight) - 1);
        switch (e.Key)
        {
            case KeyCode.Left when e.Control:
                (_line, _column) = WordStep(-1);
                break;
            case KeyCode.Right when e.Control:
                (_line, _column) = WordStep(1);
                break;
            case KeyCode.Left:
                (_line, _column) = Step(-1, collapse: !shift);
                break;
            case KeyCode.Right:
                (_line, _column) = Step(1, collapse: !shift);
                break;
            case KeyCode.Up:
                MoveLines(-1);
                break;
            case KeyCode.Down:
                MoveLines(1);
                break;
            case KeyCode.PageUp:
                MoveLines(-page);
                break;
            case KeyCode.PageDown:
                MoveLines(page);
                break;
            case KeyCode.Home when e.Control:
                _line = _column = 0;
                break;
            case KeyCode.End when e.Control:
                _line = _lines.Count - 1;
                _column = _lines[_line].Length;
                break;
            case KeyCode.Home:
                int indent = _lines[_line].Length - _lines[_line].TrimStart().Length;
                _column = _column == indent ? 0 : indent;
                break;
            case KeyCode.End:
                _column = _lines[_line].Length;
                break;
            case KeyCode.A when e.Control:
                _anchorLine = _anchorColumn = 0;
                _line = _lines.Count - 1;
                _column = _lines[_line].Length;
                ShowCaret();
                return true;
            case KeyCode.C when e.Control:
                if (HasSelection && Root?.Clipboard is { } copyTo)
                {
                    copyTo.Text = SelectedText();
                }

                return true;
            case KeyCode.Z when e.Control && !e.Shift:
                Undo();
                return true;
            case KeyCode.Y when e.Control:
            case KeyCode.Z when e.Control && e.Shift:
                Redo();
                return true;
            default:
                return !IsReadOnly && Edit(e);
        }

        if (!shift)
        {
            _anchorLine = _line;
            _anchorColumn = _column;
        }

        _typingRun = false;
        RevealCaret(centre: false);
        return true;
    }

    /// <inheritdoc/>
    protected internal override bool OnText(char character)
    {
        if (IsReadOnly || char.IsControl(character))
        {
            return false;
        }

        Replace(character.ToString(), mergeTyping: true);
        return true;
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
        painter.FillRect(Bounds, Theme.Input);
        UiFont font = Theme.MonoFont;
        float size = Theme.TextSize;
        float lineHeight = LineHeight;
        float gutter = Gutter;
        float textX = Bounds.X + gutter + Theme.Px(6f) - _horizontal;
        float glyphY = MathF.Round((lineHeight - font.LineHeight(size)) * 0.5f);

        painter.FillRect(new UiRect(Bounds.X, Bounds.Y, gutter, Bounds.Height), Theme.Panel);
        int first = Math.Max(0, (int)(_scroll.Offset / lineHeight));
        int last = Math.Min(_lines.Count - 1, (int)((_scroll.Offset + Bounds.Height) / lineHeight));
        (int sl, int sc, int el, int ec) = Ordered();

        UiRect body = UiRect.FromEdges(Bounds.X + gutter, Bounds.Y, Bounds.Right, Bounds.Bottom);
        for (int i = first; i <= last; i++)
        {
            float y = Bounds.Y + (i * lineHeight) - _scroll.Offset;
            UiRect row = new(body.X, y, body.Width, lineHeight);
            if (i == ErrorLine)
            {
                painter.FillRect(row, Theme.Error.WithAlpha(40));
            }
            else if (i == _line && IsFocused && !HasSelection)
            {
                painter.FillRect(row, Theme.Hover);
            }

            string number = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            float numberWidth = font.MeasureWidth(number, size);
            painter.DrawText(font, number, new UiVec2(Bounds.X + gutter - numberWidth - Theme.Px(10f), y + glyphY), size, i == _line ? Theme.Text : Theme.TextDisabled);

            painter.PushClip(body);
            if (HasSelection && i >= sl && i <= el)
            {
                int from = i == sl ? sc : 0;
                int to = i == el ? ec : _lines[i].Length + 1;
                painter.FillRect(new UiRect(textX + (from * CharWidth), y, Math.Max(1, to - from) * CharWidth, lineHeight), IsFocused ? Theme.Selection : Theme.SelectionInactive);
            }

            DrawLine(painter, _lines[i], new UiVec2(textX, y + glyphY));
            if (i == _line && IsFocused && _caretShown)
            {
                painter.FillRect(new UiRect(MathF.Round(textX + (_column * CharWidth)), y + Theme.Px(2f), Theme.Hairline * 2f, lineHeight - Theme.Px(4f)), Theme.Accent);
            }

            painter.PopClip();
        }

        painter.FillRect(new UiRect(Bounds.X + gutter - Theme.Hairline, Bounds.Y, Theme.Hairline, Bounds.Height), Theme.Border);
        _scroll.DrawVertical(painter, Theme, Bounds, IsPointerOver ? Root?.PointerPosition : null);
    }

    private static (int Start, int End) WordAround(string line, int column)
    {
        int start = Math.Min(column, line.Length);
        int end = start;
        while (start > 0 && IsWord(line[start - 1]))
        {
            start--;
        }

        while (end < line.Length && IsWord(line[end]))
        {
            end++;
        }

        return (start, end);
    }

    private static bool IsWord(char c) => char.IsLetterOrDigit(c) || c is '_' or '.';

    private void DrawLine(UiPainter painter, string line, UiVec2 at)
    {
        UiFont font = Theme.MonoFont;
        float size = Theme.TextSize;
        if (Highlight?.Invoke(line) is not { Count: > 0 } spans)
        {
            painter.DrawText(font, line, at, size, Theme.Text);
            return;
        }

        int column = 0;
        foreach (CodeSpan span in spans)
        {
            if (span.Start > column)
            {
                painter.DrawText(font, line.AsSpan(column, span.Start - column), new UiVec2(at.X + (column * CharWidth), at.Y), size, Theme.Text);
            }

            int end = Math.Min(line.Length, span.Start + span.Length);
            painter.DrawText(font, line.AsSpan(span.Start, end - span.Start), new UiVec2(at.X + (span.Start * CharWidth), at.Y), size, span.Color);
            column = end;
        }

        if (column < line.Length)
        {
            painter.DrawText(font, line.AsSpan(column), new UiVec2(at.X + (column * CharWidth), at.Y), size, Theme.Text);
        }
    }

    private (int Line, int Column) PositionAt(UiVec2 point)
    {
        int line = Math.Clamp((int)((point.Y - Bounds.Y + _scroll.Offset) / LineHeight), 0, _lines.Count - 1);
        float x = point.X - (Bounds.X + Gutter + Theme.Px(6f) - _horizontal);
        int column = Math.Clamp((int)MathF.Round(x / CharWidth), 0, _lines[line].Length);
        return (line, column);
    }

    private (int, int, int, int) Ordered() =>
        (_line < _anchorLine || (_line == _anchorLine && _column < _anchorColumn))
            ? (_line, _column, _anchorLine, _anchorColumn)
            : (_anchorLine, _anchorColumn, _line, _column);

    private string SelectedText()
    {
        (int sl, int sc, int el, int ec) = Ordered();
        if (sl == el)
        {
            return _lines[sl][sc..ec];
        }

        StringBuilder text = new(_lines[sl][sc..]);
        for (int i = sl + 1; i < el; i++)
        {
            text.Append('\n').Append(_lines[i]);
        }

        return text.Append('\n').Append(_lines[el][..ec]).ToString();
    }

    private (int, int) Step(int direction, bool collapse = false)
    {
        // An arrow without Shift over a selection lands on the selection's edge, as every text editor does.
        if (collapse && HasSelection)
        {
            (int sl, int sc, int el, int ec) = Ordered();
            return direction < 0 ? (sl, sc) : (el, ec);
        }

        if (direction < 0)
        {
            return _column > 0 ? (_line, _column - 1) : _line > 0 ? (_line - 1, _lines[_line - 1].Length) : (0, 0);
        }

        return _column < _lines[_line].Length ? (_line, _column + 1) : _line < _lines.Count - 1 ? (_line + 1, 0) : (_line, _column);
    }

    private (int, int) WordStep(int direction)
    {
        string line = _lines[_line];
        int column = _column;
        if (direction < 0)
        {
            if (column == 0)
            {
                return Step(-1);
            }

            while (column > 0 && !IsWord(line[column - 1]))
            {
                column--;
            }

            while (column > 0 && IsWord(line[column - 1]))
            {
                column--;
            }
        }
        else
        {
            if (column == line.Length)
            {
                return Step(1);
            }

            while (column < line.Length && IsWord(line[column]))
            {
                column++;
            }

            while (column < line.Length && !IsWord(line[column]))
            {
                column++;
            }
        }

        return (_line, column);
    }

    private void MoveLines(int delta)
    {
        _line = Math.Clamp(_line + delta, 0, _lines.Count - 1);
        _column = Math.Min(_column, _lines[_line].Length);
    }

    private bool Edit(in KeyEvent e)
    {
        switch (e.Key)
        {
            case KeyCode.Return or KeyCode.NumpadEnter:
                string current = _lines[_line];
                string indent = current[..(current.Length - current.TrimStart().Length)];
                if (current.TrimEnd().EndsWith('{'))
                {
                    indent += Indent;
                }

                Replace("\n" + indent, mergeTyping: false);
                return true;
            case KeyCode.Tab:
                Replace(Indent, mergeTyping: true);
                return true;
            case KeyCode.Backspace:
                if (!HasSelection)
                {
                    (_anchorLine, _anchorColumn) = e.Control ? WordStep(-1) : Step(-1);
                }

                Replace(string.Empty, mergeTyping: !e.Control);
                return true;
            case KeyCode.Delete:
                if (!HasSelection)
                {
                    (_anchorLine, _anchorColumn) = e.Control ? WordStep(1) : Step(1);
                }

                Replace(string.Empty, mergeTyping: false);
                return true;
            case KeyCode.X when e.Control:
                if (HasSelection && Root?.Clipboard is { } cutTo)
                {
                    cutTo.Text = SelectedText();
                    Replace(string.Empty, mergeTyping: false);
                }

                return true;
            case KeyCode.V when e.Control:
                if (Root?.Clipboard.Text is { Length: > 0 } pasted)
                {
                    Replace(pasted.ReplaceLineEndings("\n").Replace("\t", Indent, StringComparison.Ordinal), mergeTyping: false);
                }

                return true;
            default:
                return false;
        }
    }

    // Every edit is the selection (possibly empty) replaced by some text, which keeps undo and the caret simple.
    private void Replace(string text, bool mergeTyping)
    {
        if (!(mergeTyping && _typingRun))
        {
            _undo.Push((Text, _line, _column));
            if (_undo.Count > UndoDepth)
            {
                Trim(_undo);
            }
        }

        _redo.Clear();
        _typingRun = mergeTyping;
        (int sl, int sc, int el, int ec) = Ordered();
        string before = _lines[sl][..sc];
        string after = _lines[el][ec..];
        _lines.RemoveRange(sl, el - sl + 1);
        string[] inserted = (before + text + after).Split('\n');
        _lines.InsertRange(sl, inserted);
        int lastLine = sl + inserted.Length - 1;
        int column = inserted[^1].Length - after.Length;
        _line = _anchorLine = lastLine;
        _column = _anchorColumn = column;
        ErrorLine = null;
        RevealCaret(centre: false);
        ShowCaret();
        TextChanged?.Invoke();
    }

    private static void Trim(Stack<(string Text, int Line, int Column)> stack)
    {
        (string, int, int)[] kept = [.. stack.Take(UndoDepth)];
        stack.Clear();
        for (int i = kept.Length - 1; i >= 0; i--)
        {
            stack.Push(kept[i]);
        }
    }

    private bool Swap(Stack<(string Text, int Line, int Column)> from, Stack<(string Text, int Line, int Column)> to)
    {
        if (from.Count == 0)
        {
            return false;
        }

        to.Push((Text, _line, _column));
        (string text, int line, int column) = from.Pop();
        Load(text);
        _typingRun = false;
        GoTo(line, column);
        TextChanged?.Invoke();
        return true;
    }

    private void Load(string? text)
    {
        _lines.Clear();
        // Tabs become spaces so every column is one cell wide: the caret, the selection and a click all count cells.
        _lines.AddRange((text ?? string.Empty).ReplaceLineEndings("\n").Replace("\t", Indent, StringComparison.Ordinal).Split('\n'));
    }

    private void RevealCaret(bool centre)
    {
        float lineHeight = LineHeight;
        if (centre)
        {
            _scroll.Offset = (_line * lineHeight) - (Bounds.Height * 0.4f);
        }
        else
        {
            _scroll.Reveal(_line * lineHeight, lineHeight);
        }

        float caretX = _column * CharWidth;
        float visible = Bounds.Width - Gutter - Theme.Px(24f);
        if (caretX - _horizontal > visible)
        {
            _horizontal = caretX - visible;
        }
        else if (caretX < _horizontal)
        {
            _horizontal = MathF.Max(0f, caretX - Theme.Px(40f));
        }
    }

    private void ShowCaret()
    {
        _blink = 0f;
        _caretShown = true;
    }
}
