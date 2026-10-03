using OpenIGI.Core.Configuration;
using OpenIGI.Core.Graphics;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Host.DevTools;

/// <summary>
/// A fuzzy finder over every command and variable, opened with a chord and dismissed by choosing.
/// </summary>
/// <remarks>
/// <para>
/// The same registries the prompt completes against, reached without typing a prompt line first. The
/// difference is intent: the prompt is for composing a statement, the palette is for finding one thing
/// and running it. So it opens centred and modal-feeling, shows more of each entry, and closes the
/// moment something is chosen.
/// </para>
/// <para>
/// A chosen <em>command</em> runs; a chosen <em>variable</em> is placed in the prompt with a trailing
/// space instead. Running a bare variable name only prints it, and silently printing something is a
/// poor answer to a reader who went looking for it — they almost always meant to set it.
/// </para>
/// </remarks>
public sealed class CommandPaletteBody : UiElement
{
    private const int MaxRows = 14;

    private readonly ConsoleSystem _console;
    private readonly DevConsoleTheme _theme;
    private readonly List<ConsoleSuggestion> _matches = [];

    private int _selected;

    /// <summary>Initialises a new instance of the <see cref="CommandPaletteBody"/> class.</summary>
    /// <param name="console">The console whose registries are searched.</param>
    /// <param name="theme">The palette and face.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public CommandPaletteBody(ConsoleSystem console, DevConsoleTheme theme)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(theme);

        _console = console;
        _theme = theme;
    }

    /// <summary>Gets the query being typed.</summary>
    public TextEditState Query { get; } = new();

    /// <summary>Gets the entries matching the query, best first.</summary>
    public IReadOnlyList<ConsoleSuggestion> Matches => _matches;

    /// <summary>Gets or sets the highlighted entry.</summary>
    public int SelectedIndex
    {
        get => _selected;
        set => _selected = _matches.Count == 0 ? 0 : ((value % _matches.Count) + _matches.Count) % _matches.Count;
    }

    /// <summary>Gets the highlighted entry, or <see langword="null"/> when there is none.</summary>
    public ConsoleSuggestion? Selected =>
        _selected >= 0 && _selected < _matches.Count ? _matches[_selected] : null;

    /// <summary>Gets or sets whether the caret is in its visible blink phase.</summary>
    public bool CaretVisible { get; set; } = true;

    /// <summary>Clears the query and the highlight, as opening the palette does.</summary>
    public void Reset()
    {
        Query.SetText(string.Empty);
        _selected = 0;
        Refresh();
    }

    /// <summary>Re-runs the search against what has been typed.</summary>
    public void Refresh()
    {
        _matches.Clear();

        foreach (ConsoleSuggestion suggestion in _console.Suggest(Query.Text, MaxRows))
        {
            if (suggestion.Kind is ConsoleSuggestionKind.Command or ConsoleSuggestionKind.Variable or ConsoleSuggestionKind.Alias)
            {
                _matches.Add(suggestion);
            }
        }

        _selected = Math.Clamp(_selected, 0, Math.Max(0, _matches.Count - 1));
    }

    /// <summary>Returns the entry under a point, or -1.</summary>
    /// <param name="point">The point in back-buffer pixels.</param>
    /// <returns>The index into <see cref="Matches"/>, or -1.</returns>
    public int EntryAt(UiVec2 point)
    {
        float rowHeight = RowHeight;
        float top = Bounds.Y + HeaderHeight;

        if (!Bounds.Contains(point) || point.Y < top)
        {
            return -1;
        }

        int index = (int)((point.Y - top) / rowHeight);
        return index >= 0 && index < _matches.Count ? index : -1;
    }

    /// <summary>Gets or sets the generated sprites used for rounded rows.</summary>
    public UiSurfaces? Surfaces { get; set; }

    private float RowHeight => _theme.RowHeight;

    private float HeaderHeight => _theme.ControlHeight + (_theme.Unit * 4f);

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        DrawQuery(painter);
        DrawEntries(painter);
    }

    private void DrawQuery(UiPainter painter)
    {
        var box = new UiRect(Bounds.X, Bounds.Y, Bounds.Width, RowHeight + 6f);
        painter.FillRect(box, _theme.Ui.Surface);

        float y = box.Y + ((box.Height - _theme.BodyFont.LineHeight(_theme.BodySize)) * 0.5f);
        float x = box.X + _theme.Cell;

        painter.DrawText(_theme.BodyFont, "›", new UiVec2(x, y), _theme.BodySize, _theme.Ui.Accent);
        float textX = x + (_theme.Cell * 2f);

        if (Query.Text.Length == 0)
        {
            painter.DrawText(_theme.BodyFont, "run a command or find a setting", new UiVec2(textX, y), _theme.BodySize, _theme.Ui.TextDisabled);
        }
        else
        {
            painter.DrawText(_theme.BodyFont, Query.Text, new UiVec2(textX, y), _theme.BodySize, _theme.Ui.Text);
        }

        if (CaretVisible)
        {
            painter.FillRect(
                new UiRect(textX + (Query.Caret * _theme.Cell), box.Y + 4f, MathF.Max(1f, _theme.Cell * 0.12f), box.Height - 8f),
                _theme.Ui.Accent);
        }

        painter.FillRect(new UiRect(box.X, box.Bottom - 1f, box.Width, 1f), _theme.Ui.Border);
    }

    private void DrawEntries(UiPainter painter)
    {
        float rowHeight = RowHeight;
        float y = Bounds.Y + HeaderHeight;

        if (_matches.Count == 0)
        {
            painter.DrawText(
                _theme.BodyFont,
                "no matches",
                new UiVec2(Bounds.X + (_theme.Cell * 3f), y + (rowHeight * 0.25f)),
                _theme.BodySize,
                _theme.Ui.TextDisabled);
            return;
        }

        for (int i = 0; i < _matches.Count && y + rowHeight <= Bounds.Bottom; i++)
        {
            ConsoleSuggestion entry = _matches[i];
            var row = new UiRect(Bounds.X, y, Bounds.Width, rowHeight);

            if (i == _selected)
            {
                painter.FillRect(row, _theme.Ui.Selection);
                painter.FillRect(new UiRect(row.X, row.Y, 3f, row.Height), _theme.Ui.Accent);
            }

            float textY = row.Y + ((row.Height - _theme.BodyFont.LineHeight(_theme.BodySize)) * 0.5f);
            Bgra32 colour = entry.IsBlocked ? _theme.Ui.TextDisabled : _theme.ColorFor(entry.Kind);

            painter.DrawText(_theme.BodyFont, TextFit.ToColumns(entry.Text, 34), new UiVec2(row.X + (_theme.Cell * 2f), textY), _theme.BodySize, colour);

            if (entry.Detail.Length > 0)
            {
                painter.DrawText(
                    _theme.BodyFont,
                    TextFit.ToColumns(entry.Detail, 14),
                    new UiVec2(row.X + (_theme.Cell * 37f), textY),
                    _theme.BodySize,
                    _theme.Ui.Text);
            }

            if (entry.Help.Length > 0)
            {
                int columns = Math.Max(0, (int)((row.Right - row.X - (_theme.Cell * 53f)) / _theme.Cell) - 1);
                if (columns > 4)
                {
                    painter.DrawText(
                        _theme.BodyFont,
                        TextFit.ToColumns(entry.Help, columns),
                        new UiVec2(row.X + (_theme.Cell * 53f), textY),
                        _theme.BodySize,
                        _theme.Ui.TextMuted);
                }
            }

            y += rowHeight;
        }
    }
}
