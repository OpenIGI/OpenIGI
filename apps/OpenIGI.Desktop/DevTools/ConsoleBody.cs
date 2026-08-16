using OpenIGI.Core.Configuration;
using OpenIGI.Core.Graphics;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Desktop;

/// <summary>
/// The developer console's contents: a filter bar, the scrollback, the prompt, and the completion
/// list floating over them.
/// </summary>
/// <remarks>
/// <para>
/// The log draws through a <see cref="VirtualListView"/>, so a full four-thousand-line scrollback
/// costs the same as an empty one. The prompt is drawn directly rather than being a widget: it needs
/// a caret at an arbitrary index, a selection wash, per-token syntax colouring and an inline ghost
/// completion, none of which the shared text field has any business knowing about.
/// </para>
/// <para>
/// Every measurement comes from the theme's spacing unit and character cell, and everything that
/// lands on screen is rounded to a whole pixel. Half-pixel text and half-pixel hairlines are most of
/// what separates an interface that looks designed from one that looks approximate.
/// </para>
/// </remarks>
public sealed class ConsoleBody : UiElement
{
    /// <summary>
    /// The severities the filter bar offers, which is not every severity there is.
    /// </summary>
    /// <remarks>
    /// Echo is deliberately absent. It is the reader's own submitted line played back, and hiding what
    /// you just typed is never what anyone wants from a log filter — it would make the scrollback read
    /// as though commands had run themselves.
    /// </remarks>
    private static readonly ConsoleSeverity[] FilterableSeverities =
        [ConsoleSeverity.Output, ConsoleSeverity.Notice, ConsoleSeverity.Warning, ConsoleSeverity.Error];

    private readonly ConsoleModel _model;
    private readonly DevConsoleTheme _theme;
    private readonly VirtualListView _log;
    private readonly List<UiRect> _toggles = [];
    private readonly LogSelection _selection = new();

    // One buffer, reused for every row drawn and every row copied. A line longer than this is elided
    // rather than allowed to overrun, which no engine message comes close to anyway.
    private readonly char[] _rowScratch = new char[1024];

    /// <summary>Initialises a new instance of the <see cref="ConsoleBody"/> class.</summary>
    /// <param name="model">The console state drawn and edited.</param>
    /// <param name="theme">The palette, type scale and spacing.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public ConsoleBody(ConsoleModel model, DevConsoleTheme theme)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(theme);

        _model = model;
        _theme = theme;
        _log = new VirtualListView(theme.Ui) { StickToEnd = true, AnchorToBottom = true, ShowScrollBar = true };
        _log.DrawRow = DrawLogRow;
        AddChild(_log);
    }

    /// <summary>Gets or sets whether the caret is in its visible blink phase.</summary>
    public bool CaretVisible { get; set; } = true;

    /// <summary>Gets the scrollback viewport, so the shell can route the wheel to it.</summary>
    public VirtualListView Log => _log;

    /// <summary>Gets the editing state of the scrollback's find box.</summary>
    public TextEditState FindEdit { get; } = new();

    /// <summary>Gets or sets whether the find box holds the keyboard.</summary>
    public bool FindFocused { get; set; }

    /// <summary>Gets the severity toggles' rectangles, in the order they are drawn.</summary>
    public IReadOnlyList<UiRect> SeverityToggles => _toggles;

    /// <summary>Gets the find box's rectangle, for hit testing.</summary>
    public UiRect FindRect { get; private set; }

    /// <summary>Gets the generated sprites used for the dropdown's shadow and rounded corners.</summary>
    public UiSurfaces? Surfaces { get; set; }

    /// <summary>Gets how many log lines fit on screen, for page-up and page-down.</summary>
    public int VisibleLines => Math.Max(1, (int)(_log.Bounds.Height / MathF.Max(1f, _theme.LineHeight)));

    /// <summary>Gets the rectangle the completion dropdown occupies, empty when none is showing.</summary>
    public UiRect DropdownRect { get; private set; }

    /// <summary>Gets the scrollback text picked out with the pointer.</summary>
    public LogSelection Selection => _selection;

    /// <summary>Gets a value indicating whether the scrollbar's thumb is being dragged.</summary>
    public bool IsScrollDragging { get; private set; }

    /// <summary>Begins a scrollbar drag if a point is on the bar.</summary>
    /// <param name="point">The point in back-buffer pixels.</param>
    /// <returns><see langword="true"/> when the bar took the press.</returns>
    /// <remarks>A press on the empty part of the track jumps there too, rather than doing nothing.</remarks>
    public bool BeginScrollDrag(UiVec2 point)
    {
        if (!_log.HasOverflow || !_log.TrackBounds.Contains(point))
        {
            return false;
        }

        IsScrollDragging = true;
        DragScroll(point);
        return true;
    }

    /// <summary>Moves the view to follow a scrollbar drag.</summary>
    /// <param name="point">The point in back-buffer pixels.</param>
    public void DragScroll(UiVec2 point)
    {
        if (!IsScrollDragging)
        {
            return;
        }

        _log.ScrollToThumb(point.Y);

        // The model counts lines back from the newest, and it is the thing every other scrolling path
        // reads, so the pixel offset has to be converted back rather than kept alongside it.
        float rowHeight = MathF.Max(1f, _log.RowHeight);
        int back = (int)MathF.Round((_log.MaxScroll - _log.ScrollOffset) / rowHeight);
        _model.ScrollOffset = Math.Max(0, back);
    }

    /// <summary>Ends a scrollbar drag.</summary>
    public void EndScrollDrag() => IsScrollDragging = false;

    /// <summary>Begins a scrollback selection at a point.</summary>
    /// <param name="point">The point in back-buffer pixels.</param>
    /// <returns><see langword="true"/> when the point was inside the log.</returns>
    public bool BeginSelect(UiVec2 point)
    {
        if (!_log.Bounds.Contains(point))
        {
            return false;
        }

        (int row, int column) = RowColumnAt(point);
        _selection.Begin(row, column);
        return true;
    }

    /// <summary>Extends a selection in progress to a point.</summary>
    /// <param name="point">The point in back-buffer pixels.</param>
    /// <remarks>
    /// The row is clamped to what is on screen rather than ignored when the pointer leaves the
    /// viewport, so dragging past the top or bottom keeps selecting instead of stopping dead.
    /// </remarks>
    public void ExtendSelect(UiVec2 point)
    {
        if (!_selection.IsDragging)
        {
            return;
        }

        (int row, int column) = RowColumnAt(point);
        _selection.Extend(row, column);
    }

    /// <summary>Ends a selection drag.</summary>
    public void EndSelect() => _selection.End();

    /// <summary>Selects the whole visible scrollback.</summary>
    public void SelectAllLines()
    {
        IReadOnlyList<int> visible = _model.VisibleLineIndices;
        if (visible.Count == 0)
        {
            _selection.Clear();
            return;
        }

        _selection.SelectAll(visible.Count, Compose(_model.Lines[visible[^1]], _rowScratch));
    }

    /// <summary>Drops any scrollback selection.</summary>
    public void ClearSelection() => _selection.Clear();

    /// <summary>Returns the selected scrollback text, or an empty string when nothing is selected.</summary>
    /// <returns>The text, with the lines separated as they appear.</returns>
    public string SelectedText()
    {
        if (!_selection.IsActive)
        {
            return string.Empty;
        }

        IReadOnlyList<int> visible = _model.VisibleLineIndices;
        var text = new System.Text.StringBuilder();

        for (int row = _selection.FirstRow; row <= _selection.LastRow && row < visible.Count; row++)
        {
            int length = Compose(_model.Lines[visible[row]], _rowScratch);
            if (_selection.TryRangeFor(row, length, out int from, out int to))
            {
                text.Append(_rowScratch, from, to - from);
            }

            if (row < _selection.LastRow)
            {
                text.AppendLine();
            }
        }

        return text.ToString();
    }

    /// <summary>Returns the whole visible scrollback as text.</summary>
    /// <returns>Every shown line, one per line.</returns>
    public string AllVisibleText()
    {
        var text = new System.Text.StringBuilder();
        foreach (int index in _model.VisibleLineIndices)
        {
            int length = Compose(_model.Lines[index], _rowScratch);
            text.AppendLine(new string(_rowScratch, 0, length));
        }

        return text.ToString();
    }

    /// <summary>Maps a point to the row and character column under it.</summary>
    /// <param name="point">The point in back-buffer pixels.</param>
    /// <returns>The row among the filtered lines, and the column within it.</returns>
    /// <remarks>
    /// Division rather than measurement, which is exact because the face is fixed-pitch. The row is
    /// clamped into range so a drag off the edge of the viewport selects to the end rather than
    /// resolving to nothing.
    /// </remarks>
    public (int Row, int Column) RowColumnAt(UiVec2 point)
    {
        int count = _model.VisibleLineIndices.Count;
        if (count == 0)
        {
            return (0, 0);
        }

        float rowHeight = MathF.Max(1f, _log.RowHeight);
        float shift = MathF.Max(0f, _log.Bounds.Height - (count * rowHeight));
        int row = (int)MathF.Floor((point.Y - _log.Bounds.Y - shift + _log.ScrollOffset) / rowHeight);

        float textX = MathF.Round(_log.Bounds.X + (_theme.Unit * 2f));
        int column = (int)MathF.Round((point.X - textX) / MathF.Max(1f, _theme.Cell));

        return (Math.Clamp(row, 0, count - 1), Math.Max(0, column));
    }

    /// <summary>Returns which severity a toggle at an index controls.</summary>
    /// <param name="index">The toggle's index.</param>
    /// <returns>The severity.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not a toggle.</exception>
    public static ConsoleSeverity SeverityAt(int index) =>
        index >= 0 && index < FilterableSeverities.Length
            ? FilterableSeverities[index]
            : throw new ArgumentOutOfRangeException(nameof(index));

    /// <summary>Returns the completion row under a point, or -1.</summary>
    /// <param name="point">The point in back-buffer pixels.</param>
    /// <returns>The index into <see cref="ConsoleModel.RichSuggestions"/>, or -1.</returns>
    public int SuggestionAt(UiVec2 point)
    {
        if (DropdownRect.IsEmpty || !DropdownRect.Contains(point))
        {
            return -1;
        }

        int index = (int)((point.Y - DropdownRect.Y - _theme.Unit) / MathF.Max(1f, _theme.RowHeight));
        return index >= 0 && index < _model.RichSuggestions.Count ? index : -1;
    }

    private float PromptHeight => _theme.ControlHeight + (_theme.Unit * 2f);

    private float FilterBarHeight => _theme.ControlHeight + (_theme.Unit * 2f);

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        _log.RowHeight = _theme.LineHeight;
        _log.Measure(context, available);
        return available;
    }

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        float top = MathF.Round(contentRect.Y + FilterBarHeight);
        float bottom = MathF.Round(contentRect.Bottom - PromptHeight);

        _log.Arrange(UiRect.FromEdges(
            contentRect.X + _theme.Gutter,
            top + _theme.Unit,
            contentRect.Right - _theme.Gutter,
            MathF.Max(top + _theme.Unit, bottom - _theme.Unit)));
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        _log.RowCount = _model.VisibleLineIndices.Count;
        SyncScroll();
        DrawFilterBar(painter);
    }

    /// <summary>
    /// Points the viewport at wherever the model says the reader has scrolled to.
    /// </summary>
    /// <remarks>
    /// The model counts <em>lines back from the newest</em>, because that is the unit page-up, the
    /// wheel and "stay put while new output arrives" all work in. The viewport counts pixels from the
    /// top. Nothing was converting between the two, so scrolling changed a number that nothing read
    /// and the log never moved.
    /// </remarks>
    private void SyncScroll()
    {
        if (_model.ScrollOffset <= 0)
        {
            _log.StickToEnd = true;
            return;
        }

        _log.StickToEnd = false;
        _log.ScrollOffset = _log.MaxScroll - (_model.ScrollOffset * _log.RowHeight);
    }

    /// <inheritdoc/>
    protected override void OnDrawOverlay(UiPainter painter)
    {
        DrawPrompt(painter);
        DrawDropdown(painter);
    }

    /// <summary>
    /// Draws the severity chips and the find box across the top of the scrollback.
    /// </summary>
    /// <param name="painter">The painter to draw into.</param>
    /// <remarks>
    /// Chips rather than a dropdown, because the answer to "is anything being hidden from me right
    /// now" has to be readable at a glance. A closed dropdown answers nothing, and a console quietly
    /// withholding errors is the worst thing this tool could do.
    /// </remarks>
    private void DrawFilterBar(UiPainter painter)
    {
        float unit = _theme.Unit;
        var bar = new UiRect(Bounds.X, Bounds.Y, Bounds.Width, FilterBarHeight);

        float controlY = MathF.Round(bar.Y + ((bar.Height - _theme.ControlHeight) * 0.5f));
        float x = MathF.Round(bar.X + _theme.Gutter);
        float chipWidth = MathF.Round(_theme.Cell * 5.5f);

        _toggles.Clear();

        // Tabs, not buttons: the label with a coloured rule under it when it is on, and nothing at all
        // when it is off. A bordered box around four short words is a lot of furniture for a control
        // whose whole job is to be glanced at, and boxes are what made the first pass look dated.
        foreach (ConsoleSeverity severity in FilterableSeverities)
        {
            bool shown = _model.Filter.IsSeverityShown(severity);
            var tab = new UiRect(x, bar.Y, chipWidth, bar.Height);
            _toggles.Add(tab);

            Bgra32 tint = _theme.ColorFor(severity);
            string label = Label(severity);

            painter.DrawText(
                _theme.BodyFont,
                label,
                CentreText(_theme.BodyFont, _theme.BodySize, label, tab),
                _theme.BodySize,
                shown ? tint : _theme.Ui.TextDisabled);

            // The rule sits on the bar's own bottom edge, so the row of tabs reads as attached to the
            // log beneath rather than floating above it.
            float rule = MathF.Max(2f, unit * 0.5f);
            painter.FillRect(
                new UiRect(tab.X, MathF.Round(bar.Bottom - rule), tab.Width, rule),
                shown ? tint : Bgra32.Transparent);

            x = MathF.Round(tab.Right + unit);
        }

        // The find box takes the rest of the bar. Giving it the leftover width rather than a fixed
        // one is what keeps the layout looking deliberate as the window is resized.
        float findRight = MathF.Round(bar.Right - _theme.Gutter - (_model.Filter.IsActive ? _theme.Cell * 12f : 0f));
        var find = new UiRect(x + unit, controlY, MathF.Max(_theme.Cell * 8f, findRight - x - unit), _theme.ControlHeight);
        FindRect = find;

        // No box and no rule. The placeholder says what the field is for and the caret says it has the
        // keyboard, so a line under it was decoration standing in for information already on screen.

        float textY = MathF.Round(find.Y + ((find.Height - _theme.BodyFont.LineHeight(_theme.BodySize)) * 0.5f));
        float textX = MathF.Round(find.X + (unit * 2f));

        painter.DrawText(
            _theme.BodyFont,
            FindEdit.Text.Length > 0 ? FindEdit.Text : "search the log",
            new UiVec2(textX, textY),
            _theme.BodySize,
            FindEdit.Text.Length > 0 ? _theme.Ui.Text : _theme.Ui.TextDisabled);

        if (FindFocused && CaretVisible)
        {
            painter.FillRect(
                new UiRect(
                    MathF.Round(textX + (FindEdit.Caret * _theme.Cell)),
                    find.Y + unit,
                    MathF.Max(1f, MathF.Round(_theme.Cell * 0.1f)),
                    find.Height - (unit * 2f)),
                _theme.Ui.Accent);
        }

        // How much is being withheld, stated plainly, so a filtered console can never be mistaken for
        // a quiet one.
        if (_model.Filter.IsActive)
        {
            int hidden = _model.Lines.Count - _model.VisibleLineIndices.Count;
            painter.DrawText(
                _theme.SmallFont,
                $"{hidden} hidden",
                new UiVec2(
                    MathF.Round(find.Right + (unit * 2f)),
                    MathF.Round(find.Y + ((find.Height - _theme.SmallFont.LineHeight(_theme.SmallSize)) * 0.5f))),
                _theme.SmallSize,
                _theme.Ui.Accent);
        }

        painter.FillRect(new UiRect(bar.X, MathF.Round(bar.Bottom - 1f), bar.Width, 1f), _theme.Ui.Border);
    }

    /// <summary>
    /// Draws one scrollback line, with its selection wash if any of it is selected.
    /// </summary>
    /// <param name="painter">The painter to draw into.</param>
    /// <param name="row">The row's index among the lines passing the filter.</param>
    /// <param name="rect">The row's rectangle.</param>
    /// <remarks>
    /// The whole line is composed into one run of characters and drawn once. That is what makes it
    /// selectable: with a fixed-pitch face and a single origin, a pointer maps to a column by division
    /// and the text under a selection is a substring rather than a reconstruction. Drawing the
    /// timestamp and category as separate columns looked tidier and made both of those impossible.
    /// </remarks>
    private void DrawLogRow(UiPainter painter, int row, UiRect rect)
    {
        IReadOnlyList<int> visible = _model.VisibleLineIndices;
        if (row < 0 || row >= visible.Count)
        {
            return;
        }

        ConsoleLine line = _model.Lines[visible[row]];
        int length = Compose(line, _rowScratch);
        ReadOnlySpan<char> text = _rowScratch.AsSpan(0, length);

        float y = MathF.Round(rect.Y + ((rect.Height - _theme.BodyFont.LineHeight(_theme.BodySize)) * 0.5f));
        float x = MathF.Round(rect.X + (_theme.Unit * 2f));

        DrawRowSelection(painter, row, rect, x, length);

        // The category rides inline in brackets, the way every console that reads well does it. As its
        // own dim column it was a stripe of repeated words down the left edge -- more noise than the
        // alignment was worth.
        int prefix = PrefixLength(line);
        if (prefix > 0)
        {
            painter.DrawText(_theme.BodyFont, text[..prefix], new UiVec2(x, y), _theme.BodySize, _theme.Ui.TextMuted);
        }

        painter.DrawText(
            _theme.BodyFont,
            text[prefix..],
            new UiVec2(MathF.Round(x + (prefix * _theme.Cell)), y),
            _theme.BodySize,
            _theme.ColorFor(line.Severity));
    }

    private void DrawRowSelection(UiPainter painter, int row, UiRect rect, float textX, int length)
    {
        if (!_selection.TryRangeFor(row, length, out int from, out int to))
        {
            return;
        }

        painter.FillRect(
            new UiRect(
                MathF.Round(textX + (from * _theme.Cell)),
                rect.Y,
                MathF.Round((to - from) * _theme.Cell),
                rect.Height),
            _theme.Ui.Selection);
    }

    /// <summary>
    /// Writes a line into a character buffer exactly as it is drawn and copied.
    /// </summary>
    /// <param name="line">The line to compose.</param>
    /// <param name="into">The buffer to write into.</param>
    /// <returns>How many characters were written.</returns>
    /// <remarks>
    /// One composition serves drawing, hit-testing and the clipboard, so what is copied is character
    /// for character what was on screen. A second formatting path would drift from the first the
    /// moment either changed.
    /// </remarks>
    private static int Compose(in ConsoleLine line, Span<char> into)
    {
        int at = 0;

        if (DevConsoleCVars.Timestamps.Value)
        {
            TimeSpan span = TimeSpan.FromSeconds(line.Timestamp);
            span.Hours.TryFormat(into[at..], out _, "00", System.Globalization.CultureInfo.InvariantCulture);
            at += 2;
            into[at++] = ':';
            span.Minutes.TryFormat(into[at..], out _, "00", System.Globalization.CultureInfo.InvariantCulture);
            at += 2;
            into[at++] = ':';
            span.Seconds.TryFormat(into[at..], out _, "00", System.Globalization.CultureInfo.InvariantCulture);
            at += 2;
            into[at++] = '.';
            span.Milliseconds.TryFormat(into[at..], out _, "000", System.Globalization.CultureInfo.InvariantCulture);
            at += 3;
            into[at++] = ' ';
        }

        if (DevConsoleCVars.Categories.Value && line.Category.Length > 0)
        {
            into[at++] = '[';
            int room = Math.Min(line.Category.Length, into.Length - at - 3);
            line.Category.AsSpan(0, Math.Max(0, room)).CopyTo(into[at..]);
            at += Math.Max(0, room);
            into[at++] = ']';
            into[at++] = ' ';
        }

        int remaining = Math.Max(0, into.Length - at);
        int copied = Math.Min(line.Text.Length, remaining);
        line.Text.AsSpan(0, copied).CopyTo(into[at..]);
        return at + copied;
    }

    /// <summary>Returns how many leading characters of a composed line are its timestamp and category.</summary>
    /// <param name="line">The line.</param>
    /// <returns>The prefix length in characters.</returns>
    private static int PrefixLength(in ConsoleLine line)
    {
        int at = 0;

        if (DevConsoleCVars.Timestamps.Value)
        {
            at += 13;
        }

        if (DevConsoleCVars.Categories.Value && line.Category.Length > 0)
        {
            at += line.Category.Length + 3;
        }

        return at;
    }

    private void DrawPrompt(UiPainter painter)
    {
        float unit = _theme.Unit;
        var row = new UiRect(Bounds.X, MathF.Round(Bounds.Bottom - PromptHeight), Bounds.Width, PromptHeight);

        painter.FillRect(row, _theme.Ui.Surface);
        painter.FillRect(new UiRect(row.X, row.Y, row.Width, 1f), _theme.Ui.Border);

        var field = new UiRect(
            MathF.Round(row.X + _theme.Gutter),
            MathF.Round(row.Y + ((row.Height - _theme.ControlHeight) * 0.5f)),
            MathF.Round(row.Width - (_theme.Gutter * 2f)),
            _theme.ControlHeight);

        bool active = !FindFocused;

        float y = MathF.Round(field.Y + ((field.Height - _theme.BodyFont.LineHeight(_theme.BodySize)) * 0.5f));
        float sigilX = MathF.Round(field.X + (unit * 2f));

        painter.DrawText(_theme.BodyFont, "›", new UiVec2(sigilX, y), _theme.BodySize, _theme.Ui.Accent);
        float textX = MathF.Round(sigilX + (_theme.Cell * 2f));

        DrawSelection(painter, textX, field);
        DrawInputTokens(painter, textX, y);
        DrawGhost(painter, textX, y);

        if (_model.Input.Length == 0 && !_model.Edit.HasSelection)
        {
            painter.DrawText(
                _theme.BodyFont,
                "type a command, or press Tab",
                new UiVec2(textX, y),
                _theme.BodySize,
                _theme.Ui.TextDisabled);
        }

        if (active && CaretVisible)
        {
            painter.FillRect(
                new UiRect(
                    MathF.Round(textX + (_model.CaretIndex * _theme.Cell)),
                    field.Y + unit,
                    MathF.Max(1f, MathF.Round(_theme.Cell * 0.1f)),
                    field.Height - (unit * 2f)),
                _theme.Ui.Accent);
        }
    }

    private void DrawSelection(UiPainter painter, float textX, UiRect field)
    {
        TextEditState edit = _model.Edit;
        if (!edit.HasSelection)
        {
            return;
        }

        painter.FillRect(
            new UiRect(
                MathF.Round(textX + (edit.SelectionStart * _theme.Cell)),
                field.Y + _theme.Unit,
                MathF.Round((edit.SelectionEnd - edit.SelectionStart) * _theme.Cell),
                field.Height - (_theme.Unit * 2f)),
            _theme.Ui.Selection);
    }

    /// <summary>
    /// Draws the input line with its first token coloured by what it turned out to be.
    /// </summary>
    /// <param name="painter">The painter to draw into.</param>
    /// <param name="textX">Where the input text begins.</param>
    /// <param name="y">The text's top.</param>
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

        painter.DrawText(_theme.BodyFont, head, new UiVec2(textX, y), _theme.BodySize, ClassifyHead(head));

        if (tail.Length > 0)
        {
            painter.DrawText(
                _theme.BodyFont,
                tail,
                new UiVec2(MathF.Round(textX + (head.Length * _theme.Cell)), y),
                _theme.BodySize,
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
            _theme.BodyFont,
            best.AsSpan(input.Length),
            new UiVec2(MathF.Round(textX + (input.Length * _theme.Cell)), y),
            _theme.BodySize,
            _theme.SyntaxGhost);
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

        if (shown == 0 || FindFocused)
        {
            DropdownRect = UiRect.Empty;
            return;
        }

        float unit = _theme.Unit;
        float rowHeight = _theme.RowHeight;
        float height = MathF.Round((shown * rowHeight) + (unit * 2f));
        float width = MathF.Round(MathF.Min(Bounds.Width - (_theme.Gutter * 2f), _theme.Cell * 82f));

        var rect = new UiRect(
            MathF.Round(Bounds.X + _theme.Gutter),
            MathF.Round(Bounds.Bottom - PromptHeight - height - unit),
            width,
            height);

        DropdownRect = rect;

        painter.FillRect(rect, _theme.Ui.SurfaceRaised);
        painter.StrokeRect(rect, 1f, _theme.Ui.Border);

        for (int i = 0; i < shown; i++)
        {
            ConsoleSuggestion suggestion = suggestions[i];
            var line = new UiRect(rect.X + unit, MathF.Round(rect.Y + unit + (i * rowHeight)), rect.Width - (unit * 2f), rowHeight);

            if (i == _model.SuggestionIndex)
            {
                painter.FillRect(line, _theme.Ui.Selection);
            }

            float y = MathF.Round(line.Y + ((line.Height - _theme.BodyFont.LineHeight(_theme.BodySize)) * 0.5f));
            Bgra32 nameColour = suggestion.IsBlocked ? _theme.Ui.TextDisabled : _theme.ColorFor(suggestion.Kind);
            float x = MathF.Round(line.X + (unit * 2f));

            painter.DrawText(_theme.BodyFont, Fit(suggestion.Text, 28), new UiVec2(x, y), _theme.BodySize, nameColour);

            if (suggestion.Detail.Length > 0)
            {
                painter.DrawText(
                    _theme.BodyFont,
                    Fit(suggestion.Detail, 11),
                    new UiVec2(MathF.Round(x + (_theme.Cell * 29f)), y),
                    _theme.BodySize,
                    _theme.Ui.Text);
            }

            string trailing = suggestion.Help.Length > 0 ? suggestion.Help : suggestion.Range;
            if (trailing.Length == 0)
            {
                continue;
            }

            float helpX = MathF.Round(x + (_theme.Cell * 41f));
            int columns = Math.Max(0, (int)((line.Right - helpX - (unit * 2f)) / _theme.Cell));
            if (columns > 6)
            {
                painter.DrawText(
                    _theme.SmallFont,
                    Fit(trailing, columns),
                    new UiVec2(helpX, MathF.Round(line.Y + ((line.Height - _theme.SmallFont.LineHeight(_theme.SmallSize)) * 0.5f))),
                    _theme.SmallSize,
                    _theme.Ui.TextMuted);
            }
        }
    }

    private static UiVec2 CentreText(UiFont font, float size, string text, UiRect within) => new(
        MathF.Round(within.X + ((within.Width - font.MeasureWidth(text, size)) * 0.5f)),
        MathF.Round(within.Y + ((within.Height - font.LineHeight(size)) * 0.5f)));

    private static string Label(ConsoleSeverity severity) => severity switch
    {
        ConsoleSeverity.Notice => "note",
        ConsoleSeverity.Warning => "warn",
        ConsoleSeverity.Error => "error",
        _ => "info",
    };

    /// <summary>Trims text to a column count, marking where it was cut.</summary>
    /// <param name="text">The text to fit.</param>
    /// <param name="columns">How many character cells are available.</param>
    /// <returns>The text, elided if it did not fit.</returns>
    /// <remarks>
    /// Elided rather than wrapped. A wrapped log line destroys the vertical scan that is the only
    /// reason to read a log at speed, and a wrapped table row stops being a table.
    /// </remarks>
    private static string Fit(string text, int columns) =>
        columns <= 1 || text.Length <= columns ? text : string.Concat(text.AsSpan(0, columns - 1), "…");

    private static string FormatTime(double seconds)
    {
        TimeSpan span = TimeSpan.FromSeconds(seconds);
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{span.Hours:00}:{span.Minutes:00}:{span.Seconds:00}.{span.Milliseconds:000}");
    }
}
