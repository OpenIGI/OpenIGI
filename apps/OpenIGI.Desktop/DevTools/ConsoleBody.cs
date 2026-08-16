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
        _log.StickToEnd = _model.ScrollOffset == 0;
        DrawFilterBar(painter);
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

        foreach (ConsoleSeverity severity in FilterableSeverities)
        {
            bool shown = _model.Filter.IsSeverityShown(severity);
            var chip = new UiRect(x, controlY, chipWidth, _theme.ControlHeight);
            _toggles.Add(chip);

            Bgra32 tint = _theme.ColorFor(severity);
            painter.FillRoundedRect(
                chip,
                Surfaces?.Rounded ?? default,
                shown ? tint.WithAlpha(34) : _theme.Ui.Panel,
                unit);

            painter.DrawText(
                _theme.SmallFont,
                Label(severity),
                CentreText(_theme.SmallFont, _theme.SmallSize, Label(severity), chip),
                _theme.SmallSize,
                shown ? tint : _theme.Ui.TextDisabled);

            x = MathF.Round(chip.Right + unit);
        }

        // The find box takes the rest of the bar. Giving it the leftover width rather than a fixed
        // one is what keeps the layout looking deliberate as the window is resized.
        float findRight = MathF.Round(bar.Right - _theme.Gutter - (_model.Filter.IsActive ? _theme.Cell * 12f : 0f));
        var find = new UiRect(x + unit, controlY, MathF.Max(_theme.Cell * 8f, findRight - x - unit), _theme.ControlHeight);
        FindRect = find;

        bool searching = _model.Filter.Search.Length > 0;
        painter.FillRoundedRect(find, Surfaces?.Rounded ?? default, _theme.Ui.Panel, unit);

        if (FindFocused || searching)
        {
            painter.StrokeRect(find, 1f, FindFocused ? _theme.Ui.Accent : _theme.Ui.Border);
        }

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

    private void DrawLogRow(UiPainter painter, int row, UiRect rect)
    {
        IReadOnlyList<int> visible = _model.VisibleLineIndices;
        if (row < 0 || row >= visible.Count)
        {
            return;
        }

        ConsoleLine line = _model.Lines[visible[row]];
        Bgra32 colour = _theme.ColorFor(line.Severity);
        float y = MathF.Round(rect.Y + ((rect.Height - _theme.BodyFont.LineHeight(_theme.BodySize)) * 0.5f));
        float x = MathF.Round(rect.X + (_theme.Unit * 2f));

        // A warning or an error tints its whole row very faintly and marks the gutter. Both together,
        // because the tint is what the eye catches while scrolling and the mark is what it lands on.
        if (line.Severity is ConsoleSeverity.Warning or ConsoleSeverity.Error)
        {
            painter.FillRect(rect, colour.WithAlpha(14));
            painter.FillRect(new UiRect(rect.X, rect.Y, MathF.Max(2f, _theme.Unit * 0.5f), rect.Height), colour);
        }

        if (DevConsoleCVars.Timestamps.Value)
        {
            painter.DrawText(_theme.SmallFont, FormatTime(line.Timestamp), new UiVec2(x, y), _theme.SmallSize, _theme.Ui.TextDisabled);
            x = MathF.Round(x + (_theme.Cell * 13f));
        }

        if (DevConsoleCVars.Categories.Value)
        {
            if (line.Category.Length > 0)
            {
                painter.DrawText(
                    _theme.SmallFont,
                    Fit(line.Category, 13),
                    new UiVec2(x, y),
                    _theme.SmallSize,
                    _theme.Ui.TextMuted);
            }

            x = MathF.Round(x + (_theme.Cell * 14f));
        }

        painter.DrawText(_theme.BodyFont, line.Text, new UiVec2(x, y), _theme.BodySize, colour);
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
        painter.FillRoundedRect(field, Surfaces?.Rounded ?? default, _theme.Ui.Panel, unit);
        if (active)
        {
            painter.StrokeRect(field, 1f, _theme.Ui.Border);
        }

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

        painter.DrawShadow(rect, Surfaces?.Shadow ?? default, _theme.Ui.Shadow, unit * 4f, new UiVec2(0f, unit));
        painter.FillRoundedRect(rect, Surfaces?.Rounded ?? default, _theme.Ui.SurfaceRaised, _theme.CornerRadius);
        painter.StrokeRect(rect, 1f, _theme.Ui.Border);

        for (int i = 0; i < shown; i++)
        {
            ConsoleSuggestion suggestion = suggestions[i];
            var line = new UiRect(rect.X + unit, MathF.Round(rect.Y + unit + (i * rowHeight)), rect.Width - (unit * 2f), rowHeight);

            if (i == _model.SuggestionIndex)
            {
                painter.FillRoundedRect(line, Surfaces?.Rounded ?? default, _theme.Ui.Selection, unit);
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
