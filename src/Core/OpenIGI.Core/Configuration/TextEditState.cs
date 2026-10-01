namespace OpenIGI.Core.Configuration;

/// <summary>
/// An editable line of text with a caret and a selection: insertion, deletion, word motion and
/// selection extension, with no notion of how any of it is drawn or which keys drive it.
/// </summary>
/// <remarks>
/// <para>
/// The console prompt, the variable browser's filter box and the command palette's query all need the
/// same editing behaviour, and it is the kind of behaviour that is wrong in a dozen small ways when
/// it is written a second time. Keeping it here means it is implemented once and tested once, and the
/// widgets above are left holding only presentation.
/// </para>
/// <para>
/// The selection is an <see cref="Anchor"/> plus the <see cref="Caret"/> rather than a start and a
/// length, because that is what makes shift-extension work in both directions without the two ends
/// ever swapping roles: the anchor is where selecting began and stays put, the caret is where it has
/// reached and moves.
/// </para>
/// </remarks>
public sealed class TextEditState
{
    private string _text = string.Empty;
    private int _caret;
    private int _anchor;

    /// <summary>Gets the text being edited.</summary>
    public string Text => _text;

    /// <summary>Gets the caret position as an index into <see cref="Text"/>.</summary>
    public int Caret => _caret;

    /// <summary>Gets the fixed end of the selection; equal to <see cref="Caret"/> when nothing is selected.</summary>
    public int Anchor => _anchor;

    /// <summary>Gets a value indicating whether any text is selected.</summary>
    public bool HasSelection => _caret != _anchor;

    /// <summary>Gets the lower bound of the selection.</summary>
    public int SelectionStart => Math.Min(_caret, _anchor);

    /// <summary>Gets the upper bound of the selection.</summary>
    public int SelectionEnd => Math.Max(_caret, _anchor);

    /// <summary>Gets the selected text, or an empty string when nothing is selected.</summary>
    public string SelectedText =>
        HasSelection ? _text[SelectionStart..SelectionEnd] : string.Empty;

    /// <summary>Replaces the whole line, putting the caret at the end and clearing the selection.</summary>
    /// <param name="text">The new text. <see langword="null"/> is treated as empty.</param>
    public void SetText(string? text)
    {
        _text = text ?? string.Empty;
        _caret = _text.Length;
        _anchor = _caret;
    }

    /// <summary>Inserts text at the caret, replacing the selection when there is one.</summary>
    /// <param name="text">The text to insert. Control characters are dropped.</param>
    /// <returns><see langword="true"/> when the text changed.</returns>
    /// <remarks>Control characters are dropped rather than refused so that pasting a multi-line block yields its text instead of nothing.</remarks>
    public bool Insert(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        string cleaned = string.Concat(text.Where(static c => !char.IsControl(c)));
        if (cleaned.Length == 0)
        {
            return false;
        }

        DeleteSelection();
        _text = _text.Insert(_caret, cleaned);
        _caret += cleaned.Length;
        _anchor = _caret;
        return true;
    }

    /// <summary>Deletes the selection, or the character before the caret when nothing is selected.</summary>
    /// <returns><see langword="true"/> when the text changed.</returns>
    public bool Backspace()
    {
        if (DeleteSelection())
        {
            return true;
        }

        if (_caret == 0)
        {
            return false;
        }

        _text = _text.Remove(_caret - 1, 1);
        _caret--;
        _anchor = _caret;
        return true;
    }

    /// <summary>Deletes the selection, or the character after the caret when nothing is selected.</summary>
    /// <returns><see langword="true"/> when the text changed.</returns>
    public bool Delete()
    {
        if (DeleteSelection())
        {
            return true;
        }

        if (_caret >= _text.Length)
        {
            return false;
        }

        _text = _text.Remove(_caret, 1);
        return true;
    }

    /// <summary>Removes the selected text and puts the caret where it was.</summary>
    /// <returns><see langword="true"/> when something was selected.</returns>
    public bool DeleteSelection()
    {
        if (!HasSelection)
        {
            return false;
        }

        int start = SelectionStart;
        _text = _text.Remove(start, SelectionEnd - start);
        _caret = start;
        _anchor = start;
        return true;
    }

    /// <summary>Moves the caret to an absolute position.</summary>
    /// <param name="index">The position, clamped to the text.</param>
    /// <param name="extend">Whether to keep the anchor and extend the selection.</param>
    public void MoveTo(int index, bool extend = false)
    {
        _caret = Math.Clamp(index, 0, _text.Length);
        if (!extend)
        {
            _anchor = _caret;
        }
    }

    /// <summary>Moves the caret by a number of characters.</summary>
    /// <param name="delta">How far to move; negative moves left.</param>
    /// <param name="extend">Whether to extend the selection.</param>
    /// <remarks>
    /// Without extension, an arrow key pressed while text is selected collapses to the near edge
    /// rather than moving from the caret — the behaviour every text field has, and the one that makes
    /// "select, then arrow away" do what the hand expects.
    /// </remarks>
    public void Move(int delta, bool extend = false)
    {
        if (!extend && HasSelection)
        {
            _caret = delta < 0 ? SelectionStart : SelectionEnd;
            _anchor = _caret;
            return;
        }

        MoveTo(_caret + delta, extend);
    }

    /// <summary>Moves the caret one word.</summary>
    /// <param name="direction">Negative to move left, positive to move right.</param>
    /// <param name="extend">Whether to extend the selection.</param>
    public void MoveByWord(int direction, bool extend = false)
    {
        MoveTo(direction < 0 ? PreviousWord(_caret) : NextWord(_caret), extend);
    }

    /// <summary>Moves the caret to the start of the line.</summary>
    /// <param name="extend">Whether to extend the selection.</param>
    public void MoveToStart(bool extend = false) => MoveTo(0, extend);

    /// <summary>Moves the caret to the end of the line.</summary>
    /// <param name="extend">Whether to extend the selection.</param>
    public void MoveToEnd(bool extend = false) => MoveTo(_text.Length, extend);

    /// <summary>Selects the whole line.</summary>
    public void SelectAll()
    {
        _anchor = 0;
        _caret = _text.Length;
    }

    /// <summary>Drops the selection, leaving the caret where it is.</summary>
    public void ClearSelection() => _anchor = _caret;

    /// <summary>Selects the word the caret is in, as a double-click does.</summary>
    public void SelectWord()
    {
        _anchor = PreviousWordBoundary(_caret);
        _caret = NextWordBoundary(_caret);
    }

    private int PreviousWord(int from)
    {
        int index = from;
        while (index > 0 && char.IsWhiteSpace(_text[index - 1]))
        {
            index--;
        }

        while (index > 0 && !char.IsWhiteSpace(_text[index - 1]))
        {
            index--;
        }

        return index;
    }

    private int NextWord(int from)
    {
        int index = from;
        while (index < _text.Length && !char.IsWhiteSpace(_text[index]))
        {
            index++;
        }

        while (index < _text.Length && char.IsWhiteSpace(_text[index]))
        {
            index++;
        }

        return index;
    }

    private int PreviousWordBoundary(int from)
    {
        int index = Math.Clamp(from, 0, _text.Length);
        while (index > 0 && !char.IsWhiteSpace(_text[index - 1]))
        {
            index--;
        }

        return index;
    }

    private int NextWordBoundary(int from)
    {
        int index = Math.Clamp(from, 0, _text.Length);
        while (index < _text.Length && !char.IsWhiteSpace(_text[index]))
        {
            index++;
        }

        return index;
    }
}
