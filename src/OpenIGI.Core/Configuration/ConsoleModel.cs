namespace OpenIGI.Core.Configuration;

/// <summary>
/// The state of an on-screen console: what has been printed, what is being typed, and where the caret is.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately free of any drawing or input-device code. The view reads <see cref="Lines"/>,
/// <see cref="Input"/>, <see cref="CaretIndex"/> and <see cref="Suggestions"/> and draws them however
/// it likes; the platform shell translates key presses into the methods here. That split is what lets
/// the whole editing model — history, completion, word motion, scrollback — be tested without a
/// window.
/// </para>
/// <para>
/// The model registers itself as a console output sink, so anything written anywhere in the game
/// appears in the scrollback.
/// </para>
/// </remarks>
public sealed class ConsoleModel : IConsoleOutput
{
    /// <summary>How many lines are dropped at once when the scrollback overflows.</summary>
    /// <remarks>Trimming in batches keeps the cost amortised instead of shifting the list on every line.</remarks>
    private const int TrimBatch = 64;

    private readonly ConsoleSystem _console;
    private readonly List<ConsoleLine> _lines = [];
    private readonly List<string> _history = [];
    private readonly List<string> _suggestions = [];

    private string _input = string.Empty;
    private int _caret;
    private int _historyCursor = -1;
    private string _historyDraft = string.Empty;

    /// <summary>Initialises a new instance of the <see cref="ConsoleModel"/> class.</summary>
    /// <param name="console">The console this view drives.</param>
    /// <param name="scrollbackCapacity">How many lines are kept before the oldest are dropped.</param>
    /// <exception cref="ArgumentNullException"><paramref name="console"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="scrollbackCapacity"/> is not positive.</exception>
    public ConsoleModel(ConsoleSystem console, int scrollbackCapacity = 4096)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scrollbackCapacity);

        _console = console;
        ScrollbackCapacity = scrollbackCapacity;
        console.AddOutput(this);
    }

    /// <summary>Gets how many lines are kept before the oldest are dropped.</summary>
    public int ScrollbackCapacity { get; }

    /// <summary>Gets or sets a value indicating whether the console is showing.</summary>
    public bool IsOpen { get; set; }

    /// <summary>Gets the scrollback, oldest first.</summary>
    public IReadOnlyList<ConsoleLine> Lines => _lines;

    /// <summary>Gets the lines submitted so far, oldest first.</summary>
    public IReadOnlyList<string> History => _history;

    /// <summary>Gets the completion candidates for what is currently typed.</summary>
    /// <remarks>Refreshed on every edit so the view can show them as the player types, not only on Tab.</remarks>
    public IReadOnlyList<string> Suggestions => _suggestions;

    /// <summary>Gets or sets how many lines the view is scrolled back from the newest.</summary>
    /// <remarks>Clamped to the scrollback, so paging up at the top of the buffer does nothing.</remarks>
    public int ScrollOffset { get; set; }

    /// <summary>Gets the text being edited.</summary>
    public string Input => _input;

    /// <summary>Gets the caret position, as an index into <see cref="Input"/>.</summary>
    public int CaretIndex => _caret;

    /// <inheritdoc/>
    public void Write(ConsoleLine line)
    {
        _lines.Add(line);

        if (_lines.Count > ScrollbackCapacity)
        {
            _lines.RemoveRange(0, TrimBatch);
        }

        // New output pulls the view back to the bottom unless the player has scrolled up to read.
        if (ScrollOffset > 0)
        {
            ScrollOffset++;
        }
    }

    /// <summary>Empties the scrollback.</summary>
    public void ClearScrollback()
    {
        _lines.Clear();
        ScrollOffset = 0;
    }

    /// <summary>Replaces the text being edited.</summary>
    /// <param name="text">The new text. <see langword="null"/> is treated as empty.</param>
    /// <remarks>The caret moves to the end, which is what completion and history recall both want.</remarks>
    public void SetInput(string? text)
    {
        _input = text ?? string.Empty;
        _caret = _input.Length;
        RefreshSuggestions();
    }

    /// <summary>Inserts text at the caret.</summary>
    /// <param name="text">The text to insert. Control characters are ignored.</param>
    public void Insert(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        string cleaned = string.Concat(text.Where(static c => !char.IsControl(c)));
        if (cleaned.Length == 0)
        {
            return;
        }

        _input = _input.Insert(_caret, cleaned);
        _caret += cleaned.Length;
        RefreshSuggestions();
    }

    /// <summary>Deletes the character before the caret.</summary>
    public void Backspace()
    {
        if (_caret == 0)
        {
            return;
        }

        _input = _input.Remove(_caret - 1, 1);
        _caret--;
        RefreshSuggestions();
    }

    /// <summary>Deletes the character after the caret.</summary>
    public void Delete()
    {
        if (_caret >= _input.Length)
        {
            return;
        }

        _input = _input.Remove(_caret, 1);
        RefreshSuggestions();
    }

    /// <summary>Moves the caret by a number of characters.</summary>
    /// <param name="delta">How far to move. Negative moves left.</param>
    public void MoveCaret(int delta) => _caret = Math.Clamp(_caret + delta, 0, _input.Length);

    /// <summary>Moves the caret by one word.</summary>
    /// <param name="direction">Negative to move left, positive to move right.</param>
    public void MoveCaretByWord(int direction)
    {
        if (direction < 0)
        {
            int index = _caret;
            while (index > 0 && char.IsWhiteSpace(_input[index - 1]))
            {
                index--;
            }

            while (index > 0 && !char.IsWhiteSpace(_input[index - 1]))
            {
                index--;
            }

            _caret = index;
            return;
        }

        if (direction > 0)
        {
            int index = _caret;
            while (index < _input.Length && !char.IsWhiteSpace(_input[index]))
            {
                index++;
            }

            while (index < _input.Length && char.IsWhiteSpace(_input[index]))
            {
                index++;
            }

            _caret = index;
        }
    }

    /// <summary>Moves the caret to the start of the line.</summary>
    public void CaretToStart() => _caret = 0;

    /// <summary>Moves the caret to the end of the line.</summary>
    public void CaretToEnd() => _caret = _input.Length;

    /// <summary>Runs what is typed, records it in the history and clears the input.</summary>
    /// <returns><see langword="true"/> when the input was understood, or when it was blank.</returns>
    public bool Submit()
    {
        string line = _input.Trim();
        SetInput(string.Empty);
        _historyCursor = -1;
        _historyDraft = string.Empty;
        ScrollOffset = 0;

        if (line.Length == 0)
        {
            return true;
        }

        // Re-running the same line does not add a duplicate entry, so history stays useful.
        if (_history.Count == 0 || !string.Equals(_history[^1], line, StringComparison.Ordinal))
        {
            _history.Add(line);
        }

        return _console.Execute(line, echo: true);
    }

    /// <summary>Recalls the previous history entry.</summary>
    public void HistoryPrevious()
    {
        if (_history.Count == 0)
        {
            return;
        }

        if (_historyCursor < 0)
        {
            _historyDraft = _input;
            _historyCursor = _history.Count;
        }

        if (_historyCursor > 0)
        {
            _historyCursor--;
            SetInput(_history[_historyCursor]);
        }
    }

    /// <summary>Recalls the next history entry, or restores what was being typed before the recall began.</summary>
    public void HistoryNext()
    {
        if (_historyCursor < 0)
        {
            return;
        }

        _historyCursor++;

        if (_historyCursor >= _history.Count)
        {
            _historyCursor = -1;
            SetInput(_historyDraft);
            return;
        }

        SetInput(_history[_historyCursor]);
    }

    /// <summary>Completes what is typed as far as the candidates agree.</summary>
    /// <returns>The candidates that matched.</returns>
    /// <remarks>
    /// A single match completes and adds a trailing space, ready for a value. Several matches extend
    /// the input to their common prefix and leave the list showing, which is the behaviour a shell
    /// user already has in their fingers.
    /// </remarks>
    public IReadOnlyList<string> Complete()
    {
        ConsoleCompletion completion = _console.Complete(_input);
        if (!completion.HasCandidates)
        {
            return completion.Candidates;
        }

        SetInput(completion.IsUnambiguous ? completion.Candidates[0] + " " : completion.CommonPrefix);
        return completion.Candidates;
    }

    /// <summary>Scrolls the view.</summary>
    /// <param name="lines">How far to scroll. Positive scrolls back towards older output.</param>
    /// <param name="visibleLines">How many lines the view shows, used to clamp the offset.</param>
    public void Scroll(int lines, int visibleLines)
    {
        int maximum = Math.Max(0, _lines.Count - Math.Max(1, visibleLines));
        ScrollOffset = Math.Clamp(ScrollOffset + lines, 0, maximum);
    }

    private void RefreshSuggestions()
    {
        _suggestions.Clear();

        if (_input.Length == 0)
        {
            return;
        }

        ConsoleCompletion completion = _console.Complete(_input);
        _suggestions.AddRange(completion.Candidates);
    }
}
