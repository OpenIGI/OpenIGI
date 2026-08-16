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

    /// <summary>How many ranked candidates are kept for the dropdown.</summary>
    /// <remarks>More than a screenful is wasted work on every keystroke; the view shows fewer still.</remarks>
    private const int SuggestionLimit = 32;

    private readonly ConsoleSystem _console;
    private readonly List<ConsoleLine> _lines = [];
    private readonly List<string> _history = [];
    private readonly List<string> _suggestions = [];
    private readonly List<ConsoleSuggestion> _richSuggestions = [];
    private readonly List<int> _visible = [];
    private readonly TextEditState _edit = new();

    private int _historyCursor = -1;
    private string _historyDraft = string.Empty;
    private int _suggestionIndex = -1;
    private bool _visibleDirty = true;

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

        // Any change to what is shown invalidates the index map; recomputing it lazily keeps a filter
        // that nobody is touching from costing anything per frame.
        Filter.Changed += (_, _) => _visibleDirty = true;
    }

    /// <summary>Gets the filter deciding which scrollback lines are shown.</summary>
    public ConsoleLogFilter Filter { get; } = new();

    /// <summary>Gets the console this view drives.</summary>
    /// <remarks>
    /// Exposed so a view can ask what a token means as it is typed — colouring the first word by
    /// whether it resolves to a command, a variable or nothing at all needs the registries, and going
    /// back to the shell for them would mean every host wiring the same lookup by hand.
    /// </remarks>
    public ConsoleSystem Console => _console;

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

    /// <summary>Gets the ranked, described completion candidates for what is currently typed.</summary>
    /// <remarks>
    /// The list the dropdown draws: each candidate carries its current value, its accepted range and
    /// its help, so the reader can choose without running anything. <see cref="Suggestions"/> remains
    /// the plain-name view of the same moment.
    /// </remarks>
    public IReadOnlyList<ConsoleSuggestion> RichSuggestions => _richSuggestions;

    /// <summary>Gets the highlighted candidate's index, or -1 when none is highlighted.</summary>
    public int SuggestionIndex => _suggestionIndex;

    /// <summary>Gets the editing state behind the input line.</summary>
    /// <remarks>Exposed so a view can draw the selection; editing still goes through this model's own methods.</remarks>
    public TextEditState Edit => _edit;

    /// <summary>Gets the indices into <see cref="Lines"/> that pass <see cref="Filter"/>, oldest first.</summary>
    /// <remarks>
    /// Recomputed only when the scrollback or the filter has changed. With no filter set this is every
    /// index in order, so an unfiltered console behaves exactly as it did before filtering existed.
    /// </remarks>
    public IReadOnlyList<int> VisibleLineIndices
    {
        get
        {
            if (_visibleDirty)
            {
                RebuildVisible();
            }

            return _visible;
        }
    }

    /// <summary>Gets or sets how many lines the view is scrolled back from the newest.</summary>
    /// <remarks>Clamped to the scrollback, so paging up at the top of the buffer does nothing.</remarks>
    public int ScrollOffset { get; set; }

    /// <summary>Gets the text being edited.</summary>
    public string Input => _edit.Text;

    /// <summary>Gets the caret position, as an index into <see cref="Input"/>.</summary>
    public int CaretIndex => _edit.Caret;

    /// <inheritdoc/>
    public void Write(ConsoleLine line)
    {
        _lines.Add(line);

        if (_lines.Count > ScrollbackCapacity)
        {
            _lines.RemoveRange(0, TrimBatch);
        }

        _visibleDirty = true;

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
        _visible.Clear();
        _visibleDirty = false;
        ScrollOffset = 0;
    }

    /// <summary>Replaces the text being edited.</summary>
    /// <param name="text">The new text. <see langword="null"/> is treated as empty.</param>
    /// <remarks>The caret moves to the end, which is what completion and history recall both want.</remarks>
    public void SetInput(string? text)
    {
        _edit.SetText(text);
        RefreshSuggestions();
    }

    /// <summary>Inserts text at the caret, replacing the selection when there is one.</summary>
    /// <param name="text">The text to insert. Control characters are ignored.</param>
    public void Insert(string? text)
    {
        if (_edit.Insert(text))
        {
            RefreshSuggestions();
        }
    }

    /// <summary>Deletes the selection, or the character before the caret.</summary>
    public void Backspace()
    {
        if (_edit.Backspace())
        {
            RefreshSuggestions();
        }
    }

    /// <summary>Deletes the selection, or the character after the caret.</summary>
    public void Delete()
    {
        if (_edit.Delete())
        {
            RefreshSuggestions();
        }
    }

    /// <summary>Moves the caret by a number of characters.</summary>
    /// <param name="delta">How far to move. Negative moves left.</param>
    /// <param name="extend">Whether to extend the selection.</param>
    public void MoveCaret(int delta, bool extend = false) => _edit.Move(delta, extend);

    /// <summary>Moves the caret by one word.</summary>
    /// <param name="direction">Negative to move left, positive to move right.</param>
    /// <param name="extend">Whether to extend the selection.</param>
    public void MoveCaretByWord(int direction, bool extend = false)
    {
        if (direction != 0)
        {
            _edit.MoveByWord(direction, extend);
        }
    }

    /// <summary>Moves the caret to the start of the line.</summary>
    /// <param name="extend">Whether to extend the selection.</param>
    public void CaretToStart(bool extend = false) => _edit.MoveToStart(extend);

    /// <summary>Moves the caret to the end of the line.</summary>
    /// <param name="extend">Whether to extend the selection.</param>
    public void CaretToEnd(bool extend = false) => _edit.MoveToEnd(extend);

    /// <summary>Selects the whole input line.</summary>
    public void SelectAll() => _edit.SelectAll();

    /// <summary>Runs what is typed, records it in the history and clears the input.</summary>
    /// <returns><see langword="true"/> when the input was understood, or when it was blank.</returns>
    public bool Submit()
    {
        string line = _edit.Text.Trim();
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
            _historyDraft = _edit.Text;
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
        // A highlighted candidate is an explicit choice and outranks the common prefix: once the
        // reader has arrowed to something, Tab must take that rather than second-guess them.
        if (_suggestionIndex >= 0 && _suggestionIndex < _richSuggestions.Count)
        {
            AcceptSuggestion();
            return _suggestions;
        }

        ConsoleCompletion completion = _console.Complete(_edit.Text);
        if (completion.HasCandidates)
        {
            SetInput(completion.IsUnambiguous ? completion.Candidates[0] + " " : completion.CommonPrefix);
            return completion.Candidates;
        }

        return CompleteFromRanked();
    }

    /// <summary>
    /// Completes from the ranked list when the plain one had nothing to offer.
    /// </summary>
    /// <returns>The candidate names considered.</returns>
    /// <remarks>
    /// <para>
    /// This is what makes Tab finish a <em>value</em>. <see cref="ConsoleSystem.Complete"/> answers
    /// only for the first token — deliberately, since that is the question it was written for — so
    /// without this, typing <c>r_vsync </c> and pressing Tab did nothing at all even though the
    /// dropdown was sitting there listing <c>0</c> and <c>1</c>. It also picks up the fuzzy first-token
    /// matches the plain path cannot see.
    /// </para>
    /// <para>
    /// One candidate is taken outright. Several extend to their common prefix; when that adds nothing
    /// the first is highlighted instead, so a second Tab takes it and a reader who wants a different
    /// one can arrow to it — the behaviour a shell has trained everyone to expect.
    /// </para>
    /// </remarks>
    private List<string> CompleteFromRanked()
    {
        if (_richSuggestions.Count == 0)
        {
            return _suggestions;
        }

        if (_richSuggestions.Count == 1)
        {
            _suggestionIndex = 0;
            AcceptSuggestion();
            return _suggestions;
        }

        string prefix = CommonPrefixOfRanked();
        string token = TokenUnderCaret();

        if (prefix.Length > token.Length)
        {
            ReplaceTokenUnderCaret(prefix, addTrailingSpace: false);
            return _suggestions;
        }

        _suggestionIndex = 0;
        return _suggestions;
    }

    private string CommonPrefixOfRanked()
    {
        string prefix = _richSuggestions[0].Text;

        foreach (ConsoleSuggestion suggestion in _richSuggestions)
        {
            int length = 0;
            while (length < prefix.Length && length < suggestion.Text.Length &&
                   char.ToLowerInvariant(prefix[length]) == char.ToLowerInvariant(suggestion.Text[length]))
            {
                length++;
            }

            prefix = prefix[..length];
        }

        return prefix;
    }

    /// <summary>Returns the word the caret is inside, which is what a completion replaces.</summary>
    /// <returns>The token, which may be empty when the caret sits after a space.</returns>
    private string TokenUnderCaret()
    {
        string text = _edit.Text;
        int start = TokenStart();
        return text[start.._edit.Caret];
    }

    private int TokenStart()
    {
        string text = _edit.Text;
        int start = text.LastIndexOfAny([' ', ';'], Math.Max(0, _edit.Caret - 1)) + 1;
        return Math.Min(start, text.Length);
    }

    /// <summary>Replaces the token under the caret, leaving anything after it alone.</summary>
    /// <param name="replacement">The text to put in its place.</param>
    /// <param name="addTrailingSpace">Whether to leave a space ready for what comes next.</param>
    private void ReplaceTokenUnderCaret(string replacement, bool addTrailingSpace)
    {
        string text = _edit.Text;
        int start = TokenStart();
        string head = text[..start];
        string tail = text[_edit.Caret..];

        SetInput(head + replacement + (addTrailingSpace ? " " : string.Empty) + tail);
    }

    /// <summary>Moves the highlight through the suggestion list.</summary>
    /// <param name="delta">How far to move; negative moves towards the top.</param>
    /// <remarks>
    /// Wraps at both ends, and moving up from nothing highlights the last candidate, so the list can
    /// be reached from either direction without first pressing down.
    /// </remarks>
    public void MoveSuggestion(int delta)
    {
        if (_richSuggestions.Count == 0 || delta == 0)
        {
            return;
        }

        int count = _richSuggestions.Count;
        int next = _suggestionIndex < 0
            ? (delta > 0 ? 0 : count - 1)
            : _suggestionIndex + delta;

        _suggestionIndex = ((next % count) + count) % count;
    }

    /// <summary>Drops the highlight without changing the input.</summary>
    public void ClearSuggestion() => _suggestionIndex = -1;

    /// <summary>Highlights a candidate outright, as clicking one does.</summary>
    /// <param name="index">The index to highlight, or -1 to highlight nothing.</param>
    /// <returns><see langword="true"/> when the index named a candidate.</returns>
    public bool SelectSuggestion(int index)
    {
        if (index < 0 || index >= _richSuggestions.Count)
        {
            _suggestionIndex = -1;
            return false;
        }

        _suggestionIndex = index;
        return true;
    }

    /// <summary>Replaces the token being typed with the highlighted candidate.</summary>
    /// <returns><see langword="true"/> when a candidate was applied.</returns>
    public bool AcceptSuggestion()
    {
        if (_suggestionIndex < 0 || _suggestionIndex >= _richSuggestions.Count)
        {
            return false;
        }

        ConsoleSuggestion chosen = _richSuggestions[_suggestionIndex];

        // Replace only the token under the caret. A value completion must leave the variable's name
        // standing, and a name completion must not swallow a value already typed after it. A name gets
        // a trailing space because something follows it; a value ends the statement, so it does not.
        ReplaceTokenUnderCaret(chosen.Text, addTrailingSpace: chosen.Kind != ConsoleSuggestionKind.Value);
        return true;
    }

    /// <summary>Scrolls the view.</summary>
    /// <param name="lines">How far to scroll. Positive scrolls back towards older output.</param>
    /// <param name="visibleLines">How many lines the view shows, used to clamp the offset.</param>
    /// <remarks>Counts shown lines, so scrolling a filtered log moves by what is on screen rather than by what is hidden behind it.</remarks>
    public void Scroll(int lines, int visibleLines)
    {
        int maximum = Math.Max(0, VisibleLineIndices.Count - Math.Max(1, visibleLines));
        ScrollOffset = Math.Clamp(ScrollOffset + lines, 0, maximum);
    }

    private void RebuildVisible()
    {
        _visible.Clear();

        if (!Filter.IsActive)
        {
            for (int i = 0; i < _lines.Count; i++)
            {
                _visible.Add(i);
            }
        }
        else
        {
            for (int i = 0; i < _lines.Count; i++)
            {
                if (Filter.Matches(_lines[i]))
                {
                    _visible.Add(i);
                }
            }
        }

        _visibleDirty = false;
    }

    private void RefreshSuggestions()
    {
        _suggestions.Clear();
        _richSuggestions.Clear();
        _suggestionIndex = -1;

        if (_edit.Text.Length == 0)
        {
            return;
        }

        ConsoleCompletion completion = _console.Complete(_edit.Text);
        _suggestions.AddRange(completion.Candidates);

        foreach (ConsoleSuggestion suggestion in _console.Suggest(_edit.Text, SuggestionLimit))
        {
            _richSuggestions.Add(suggestion);
        }
    }
}
