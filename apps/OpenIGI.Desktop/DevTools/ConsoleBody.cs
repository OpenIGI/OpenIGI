using OpenIGI.Core.Configuration;
using OpenIGI.Core.Graphics;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Desktop;

/// <summary>
/// The developer console's contents: the scrollback above, the prompt below, and the completion
/// dropdown floating over both.
/// </summary>
/// <remarks>
/// <para>
/// Draws the log through a <see cref="VirtualListView"/>, so a full four-thousand-line scrollback
/// costs the same as an empty one. The prompt is drawn directly rather than being a widget: it needs a
/// caret at an arbitrary index, a selection wash, per-token syntax colouring and an inline ghost
/// completion, none of which the shared text field has any business knowing about.
/// </para>
/// <para>
/// Columns are placed on multiples of one character advance, measured once per size. That is only
/// correct because the face is fixed-pitch — which is asserted by a test, precisely so that swapping
/// in a proportional face fails loudly instead of quietly misaligning every row.
/// </para>
/// </remarks>
public sealed class ConsoleBody : UiElement
{
    private const float PromptGap = 8f;
    // Each column is one wider than its widest content, so the gap between columns is the padding.
    // "00:00:00.000" is exactly twelve characters and would otherwise touch the category beside it.
    private const int TimeColumns = 13;
    private const int CategoryColumns = 14;

    private readonly ConsoleModel _model;
    private readonly DevConsoleTheme _theme;
    private readonly VirtualListView _log;

    private float _cell = 8f;
    private float _textSize = 14f;

    /// <summary>Initialises a new instance of the <see cref="ConsoleBody"/> class.</summary>
    /// <param name="model">The console state drawn and edited.</param>
    /// <param name="theme">The palette and face.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public ConsoleBody(ConsoleModel model, DevConsoleTheme theme)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(theme);

        _model = model;
        _theme = theme;
        _log = new VirtualListView(theme.Ui) { StickToEnd = true, AnchorToBottom = true };
        _log.DrawRow = DrawLogRow;
        AddChild(_log);
    }

    /// <summary>Gets or sets whether the caret is in its visible blink phase.</summary>
    public bool CaretVisible { get; set; } = true;

    /// <summary>Gets the scrollback viewport, so the shell can route the wheel to it.</summary>
    public VirtualListView Log => _log;

    /// <summary>Gets how many log lines fit on screen, for page-up and page-down.</summary>
    public int VisibleLines => Math.Max(1, (int)(_log.Bounds.Height / MathF.Max(1f, _log.RowHeight)));

    /// <summary>Gets the rectangle the completion dropdown occupies, empty when none is showing.</summary>
    /// <remarks>
    /// The dropdown is drawn as an overlay above the window's own children but must still be
    /// hit-testable, so its rectangle is public and the shell tests against it before anything else.
    /// </remarks>
    public UiRect DropdownRect { get; private set; }

    /// <summary>Returns the completion row under a point, or -1.</summary>
    /// <param name="point">The point in back-buffer pixels.</param>
    /// <returns>The index into <see cref="ConsoleModel.RichSuggestions"/>, or -1.</returns>
    public int SuggestionAt(UiVec2 point)
    {
        if (DropdownRect.IsEmpty || !DropdownRect.Contains(point))
        {
            return -1;
        }

        float row = MathF.Max(1f, RowHeight);
        int index = (int)((point.Y - DropdownRect.Y) / row);
        return index >= 0 && index < _model.RichSuggestions.Count ? index : -1;
    }

    private float RowHeight => _theme.Mono.LineHeight(_textSize) * 1.30f;

    private float PromptHeight => RowHeight + (PromptGap * 2f);

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        _textSize = context.Px(DevConsoleCVars.FontSize.Value);
        _cell = _theme.Mono.MeasureWidth("0", _textSize);
        _log.RowHeight = RowHeight;
        _log.Measure(context, available);
        return available;
    }

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        float promptHeight = PromptHeight;
        _log.Arrange(UiRect.FromEdges(
            contentRect.X,
            contentRect.Y,
            contentRect.Right,
            MathF.Max(contentRect.Y, contentRect.Bottom - promptHeight)));
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        _log.RowCount = _model.VisibleLineIndices.Count;
        _log.StickToEnd = _model.ScrollOffset == 0;
    }

    /// <inheritdoc/>
    protected override void OnDrawOverlay(UiPainter painter)
    {
        DrawPrompt(painter);
        DrawDropdown(painter);
    }

    private void DrawLogRow(UiPainter painter, int row, UiRect rect)
    {
        IReadOnlyList<int> visible = _model.VisibleLineIndices;
        if (row < 0 || row >= visible.Count)
        {
            return;
        }

        ConsoleLine line = _model.Lines[visible[row]];
        Bgra32 colour = _theme.ColorFor(line.Severity);
        float y = rect.Y + ((rect.Height - _theme.Mono.LineHeight(_textSize)) * 0.5f);
        float x = rect.X + (_cell * 0.75f);

        // A severity stripe in the gutter, so errors are findable while scrolling fast without the
        // text itself having to shout.
        if (line.Severity is ConsoleSeverity.Warning or ConsoleSeverity.Error)
        {
            painter.FillRect(new UiRect(rect.X, rect.Y, 2f, rect.Height), colour);
        }

        if (DevConsoleCVars.Timestamps.Value)
        {
            painter.DrawText(
                _theme.Mono,
                FormatTime(line.Timestamp),
                new UiVec2(x, y),
                _textSize,
                _theme.Ui.TextDisabled);
            x += _cell * TimeColumns;
        }

        if (DevConsoleCVars.Categories.Value && line.Category.Length > 0)
        {
            painter.DrawText(
                _theme.Mono,
                Fit(line.Category, CategoryColumns - 1),
                new UiVec2(x, y),
                _textSize,
                _theme.Ui.TextMuted);
        }

        if (DevConsoleCVars.Categories.Value)
        {
            x += _cell * CategoryColumns;
        }

        painter.DrawText(_theme.Mono, line.Text, new UiVec2(x, y), _textSize, colour);
    }

    private void DrawPrompt(UiPainter painter)
    {
        UiRect row = new(Bounds.X, Bounds.Bottom - PromptHeight, Bounds.Width, PromptHeight);
        painter.FillRect(row, _theme.Ui.Surface);
        painter.FillRect(new UiRect(row.X, row.Y, row.Width, 1f), _theme.Ui.Border);

        float y = row.Y + ((row.Height - _theme.Mono.LineHeight(_textSize)) * 0.5f);
        float x = row.X + (_cell * 0.75f);

        painter.DrawText(_theme.Mono, "]", new UiVec2(x, y), _textSize, _theme.Ui.Accent);
        float textX = x + (_cell * 2f);

        DrawSelection(painter, textX, row);
        DrawInputTokens(painter, textX, y);
        DrawGhost(painter, textX, y);
        DrawCaret(painter, textX, row);
    }

    private void DrawSelection(UiPainter painter, float textX, UiRect row)
    {
        TextEditState edit = _model.Edit;
        if (!edit.HasSelection)
        {
            return;
        }

        float from = textX + (edit.SelectionStart * _cell);
        float width = (edit.SelectionEnd - edit.SelectionStart) * _cell;
        painter.FillRect(
            new UiRect(from, row.Y + PromptGap, width, _theme.Mono.LineHeight(_textSize)),
            _theme.Ui.Selection);
    }

    /// <summary>
    /// Draws the input line with its first token coloured by what it turned out to be.
    /// </summary>
    /// <param name="painter">The painter to draw into.</param>
    /// <param name="textX">Where the input text begins.</param>
    /// <param name="y">The text baseline's top.</param>
    /// <remarks>
    /// The first token is looked up as it is typed and drawn red the moment it stops resolving, which
    /// is the cheapest possible way to say "this command does not exist" — the reader finds out while
    /// typing rather than after pressing return.
    /// </remarks>
    private void DrawInputTokens(UiPainter painter, float textX, float y)
    {
        string input = _model.Input;
        if (input.Length == 0)
        {
            return;
        }

        int split = input.IndexOf(' ', StringComparison.Ordinal);
        string head = split < 0 ? input : input[..split];
        string tail = split < 0 ? string.Empty : input[split..];

        painter.DrawText(_theme.Mono, head, new UiVec2(textX, y), _textSize, ClassifyHead(head));

        if (tail.Length > 0)
        {
            painter.DrawText(
                _theme.Mono,
                tail,
                new UiVec2(textX + (head.Length * _cell), y),
                _textSize,
                _theme.Ui.Text);
        }
    }

    private Bgra32 ClassifyHead(string head)
    {
        ConsoleSystem system = _model.Console;

        if (system.Aliases.ContainsKey(head))
        {
            return _theme.SyntaxAlias;
        }

        if (system.Commands.TryGet(head, out _))
        {
            return _theme.SyntaxCommand;
        }

        if (system.Variables.Contains(head))
        {
            return _theme.SyntaxVariable;
        }

        return _theme.Ui.Danger;
    }

    /// <summary>
    /// Draws the rest of the best candidate ahead of the caret, in a colour that cannot be mistaken
    /// for typed text.
    /// </summary>
    /// <param name="painter">The painter to draw into.</param>
    /// <param name="textX">Where the input text begins.</param>
    /// <param name="y">The text baseline's top.</param>
    private void DrawGhost(UiPainter painter, float textX, float y)
    {
        if (_model.RichSuggestions.Count == 0 || _model.Edit.HasSelection)
        {
            return;
        }

        ReadOnlySpan<char> input = _model.Input.AsSpan();
        string best = _model.RichSuggestions[_model.SuggestionIndex >= 0 ? _model.SuggestionIndex : 0].Text;

        // Only a candidate that actually continues what is typed can be shown inline; a fuzzy match
        // that rearranges the letters would render as nonsense trailing the caret.
        if (!best.AsSpan().StartsWith(input, StringComparison.OrdinalIgnoreCase) || best.Length <= input.Length)
        {
            return;
        }

        painter.DrawText(
            _theme.Mono,
            best.AsSpan(input.Length),
            new UiVec2(textX + (input.Length * _cell), y),
            _textSize,
            _theme.SyntaxGhost);
    }

    private void DrawCaret(UiPainter painter, float textX, UiRect row)
    {
        if (!CaretVisible)
        {
            return;
        }

        float height = _theme.Mono.LineHeight(_textSize);
        painter.FillRect(
            new UiRect(textX + (_model.CaretIndex * _cell), row.Y + PromptGap, MathF.Max(1f, _cell * 0.12f), height),
            _theme.Ui.Accent);
    }

    /// <summary>
    /// Draws the completion list above the prompt.
    /// </summary>
    /// <param name="painter">The painter to draw into.</param>
    /// <remarks>
    /// Above rather than below, because the prompt sits at the bottom of the window and a list hanging
    /// off the bottom edge would be clipped away by the window it belongs to.
    /// </remarks>
    private void DrawDropdown(UiPainter painter)
    {
        IReadOnlyList<ConsoleSuggestion> suggestions = _model.RichSuggestions;
        int shown = Math.Min(suggestions.Count, DevConsoleCVars.MaxSuggestions.Value);
        if (shown == 0)
        {
            DropdownRect = UiRect.Empty;
            return;
        }

        float rowHeight = RowHeight;
        float height = shown * rowHeight;
        float width = MathF.Min(Bounds.Width - (_cell * 2f), _cell * 78f);
        var rect = new UiRect(
            Bounds.X + (_cell * 2.75f),
            Bounds.Bottom - PromptHeight - height,
            width,
            height);

        DropdownRect = rect;

        painter.DrawShadow(rect, Surfaces, _theme.Ui.Shadow, 12f, new UiVec2(0f, 3f));
        painter.FillRect(rect, _theme.Ui.SurfaceRaised);
        painter.StrokeRect(rect, 1f, _theme.Ui.Border);

        for (int i = 0; i < shown; i++)
        {
            ConsoleSuggestion suggestion = suggestions[i];
            var line = new UiRect(rect.X, rect.Y + (i * rowHeight), rect.Width, rowHeight);

            if (i == _model.SuggestionIndex)
            {
                painter.FillRect(line, _theme.Ui.Selection);
                painter.FillRect(new UiRect(line.X, line.Y, 2f, line.Height), _theme.Ui.Accent);
            }

            float y = line.Y + ((line.Height - _theme.Mono.LineHeight(_textSize)) * 0.5f);
            Bgra32 nameColour = suggestion.IsBlocked ? _theme.Ui.TextDisabled : _theme.ColorFor(suggestion.Kind);

            painter.DrawText(_theme.Mono, Fit(suggestion.Text, 30), new UiVec2(line.X + (_cell * 0.75f), y), _textSize, nameColour);

            if (suggestion.Detail.Length > 0)
            {
                painter.DrawText(
                    _theme.Mono,
                    Fit(suggestion.Detail, 12),
                    new UiVec2(line.X + (_cell * 32f), y),
                    _textSize,
                    _theme.Ui.Text);
            }

            string trailing = suggestion.Help.Length > 0 ? suggestion.Help : suggestion.Range;
            if (trailing.Length > 0)
            {
                painter.DrawText(
                    _theme.Mono,
                    Fit(trailing, 32),
                    new UiVec2(line.X + (_cell * 45f), y),
                    _textSize,
                    _theme.Ui.TextMuted);
            }
        }
    }

    /// <summary>Gets or sets the generated sprites used for the dropdown's shadow.</summary>
    public NineSlice Surfaces { get; set; }

    /// <summary>Trims text to a column count, marking where it was cut.</summary>
    /// <param name="text">The text to fit.</param>
    /// <param name="columns">How many character cells are available.</param>
    /// <returns>The text, elided if it did not fit.</returns>
    /// <remarks>
    /// Elided rather than wrapped. A wrapped log line destroys the vertical scan that is the only
    /// reason to read a log at speed, and a wrapped table row stops being a table.
    /// </remarks>
    private static string Fit(string text, int columns)
    {
        if (columns <= 1 || text.Length <= columns)
        {
            return text;
        }

        return string.Concat(text.AsSpan(0, columns - 1), "…");
    }

    private static string FormatTime(double seconds)
    {
        TimeSpan span = TimeSpan.FromSeconds(seconds);
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{span.Hours:00}:{span.Minutes:00}:{span.Seconds:00}.{span.Milliseconds:000}");
    }
}
