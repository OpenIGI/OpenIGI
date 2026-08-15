using OpenIGI.Core.Configuration;
using OpenIGI.Core.Graphics;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Desktop;

/// <summary>
/// A live table of every console variable: filtered as the reader types, showing value, default,
/// flags and help, and editable in place.
/// </summary>
/// <remarks>
/// <para>
/// The registry holds around a hundred variables and <c>cvarlist</c> prints all of them into a log
/// that then has to be scrolled. A table that narrows as you type answers "what is this called" and
/// "what is it set to" in one place, which is the question the log was a poor answer to.
/// </para>
/// <para>
/// <b>Editing happens in the row.</b> Copying a name to the prompt and typing a value there works, but
/// it puts the value being changed and the value it had on different parts of the screen. Editing in
/// place keeps them together, and a rejected value can be reported against the row that refused it.
/// </para>
/// <para>
/// Columns sit on multiples of one character advance, which is exact because the face is fixed-pitch.
/// </para>
/// </remarks>
public sealed class CVarBrowserBody : UiElement
{
    private const int NameColumns = 30;
    private const int ValueColumns = 14;
    private const int DefaultColumns = 12;
    private const int FlagColumns = 7;

    private readonly CVarRegistry _registry;
    private readonly DevConsoleTheme _theme;
    private readonly VirtualListView _rows;
    private readonly List<ICVar> _matches = [];

    private string _filter = string.Empty;
    private int _selected = -1;
    private bool _editing;
    private string _error = string.Empty;
    private float _cell = 8f;
    private float _textSize = 14f;

    /// <summary>Initialises a new instance of the <see cref="CVarBrowserBody"/> class.</summary>
    /// <param name="registry">The registry listed.</param>
    /// <param name="theme">The palette and face.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public CVarBrowserBody(CVarRegistry registry, DevConsoleTheme theme)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(theme);

        _registry = registry;
        _theme = theme;
        _rows = new VirtualListView(theme.Ui) { StickToEnd = false };
        _rows.DrawRow = DrawVariableRow;
        AddChild(_rows);

        Refresh();
    }

    /// <summary>Gets the editing state of the filter box.</summary>
    public TextEditState FilterEdit { get; } = new();

    /// <summary>Gets the editing state of the value being changed, valid while <see cref="IsEditing"/>.</summary>
    public TextEditState ValueEdit { get; } = new();

    /// <summary>Gets a value indicating whether a value is being edited in place.</summary>
    public bool IsEditing => _editing;

    /// <summary>Gets the variables currently passing the filter.</summary>
    public IReadOnlyList<ICVar> Matches => _matches;

    /// <summary>Gets or sets the selected row, or -1 for none.</summary>
    public int SelectedIndex
    {
        get => _selected;
        set => _selected = _matches.Count == 0 ? -1 : Math.Clamp(value, -1, _matches.Count - 1);
    }

    /// <summary>Gets the selected variable, or <see langword="null"/> when nothing is selected.</summary>
    public ICVar? Selected => _selected >= 0 && _selected < _matches.Count ? _matches[_selected] : null;

    /// <summary>Gets or sets whether the caret is in its visible blink phase.</summary>
    public bool CaretVisible { get; set; } = true;

    /// <summary>Gets the table's viewport, so the shell can route the wheel to it.</summary>
    public VirtualListView Rows => _rows;

    /// <summary>Gets the filter box's rectangle, for hit testing.</summary>
    public UiRect FilterRect { get; private set; }

    /// <summary>Re-reads the registry through the current filter.</summary>
    /// <remarks>
    /// Called when the filter changes and when the panel is shown, not every frame: the set of
    /// variables only changes when something registers one, while their <em>values</em> are read
    /// straight off the registry as each row draws and so are always current.
    /// </remarks>
    public void Refresh()
    {
        _matches.Clear();

        foreach (ICVar variable in _registry.Search(_filter, includeHidden: false))
        {
            _matches.Add(variable);
        }

        _rows.RowCount = _matches.Count;
        SelectedIndex = _selected;
    }

    /// <summary>Applies what has been typed into the filter box.</summary>
    public void ApplyFilter()
    {
        if (!string.Equals(_filter, FilterEdit.Text, StringComparison.Ordinal))
        {
            _filter = FilterEdit.Text;
            _selected = -1;
            _rows.SnapTo(0f);
            Refresh();
        }
    }

    /// <summary>Opens an in-place edit on the selected row.</summary>
    /// <returns><see langword="true"/> when an edit was opened.</returns>
    /// <remarks>A read-only variable refuses, and says why against its own row rather than in the log.</remarks>
    public bool BeginEdit()
    {
        if (Selected is not { } variable)
        {
            return false;
        }

        if ((variable.Flags & CVarOptions.ReadOnly) != 0)
        {
            _error = $"{variable.Name} is read-only.";
            return false;
        }

        ValueEdit.SetText(variable.ValueText);
        ValueEdit.SelectAll();
        _editing = true;
        _error = string.Empty;
        return true;
    }

    /// <summary>Commits the in-place edit.</summary>
    /// <returns><see langword="true"/> when the value was accepted.</returns>
    public bool CommitEdit()
    {
        if (!_editing || Selected is not { } variable)
        {
            return false;
        }

        if (!variable.TrySetText(ValueEdit.Text, out string? reason))
        {
            _error = reason ?? $"'{ValueEdit.Text}' is not valid for {variable.Name}.";
            return false;
        }

        _editing = false;
        _error = string.Empty;
        return true;
    }

    /// <summary>Abandons the in-place edit, leaving the value as it was.</summary>
    public void CancelEdit()
    {
        _editing = false;
        _error = string.Empty;
    }

    /// <summary>Steps a boolean or an enumeration to its next value without opening an edit.</summary>
    /// <returns><see langword="true"/> when something was cycled.</returns>
    /// <remarks>
    /// The fastest thing a variable browser can do. Most of what a reader wants from this table is to
    /// flip a switch, and making them type <c>1</c> to do it is a step too many.
    /// </remarks>
    public bool CycleSelected()
    {
        if (Selected is not { } variable || (variable.Flags & CVarOptions.ReadOnly) != 0)
        {
            return false;
        }

        IReadOnlyList<string> choices = variable.ValueChoices;
        if (choices.Count == 0)
        {
            return false;
        }

        int at = 0;
        for (int i = 0; i < choices.Count; i++)
        {
            if (choices[i].Equals(variable.ValueText, StringComparison.OrdinalIgnoreCase))
            {
                at = i;
                break;
            }
        }

        string next = choices[(at + 1) % choices.Count];
        if (!variable.TrySetText(next, out string? reason))
        {
            _error = reason ?? string.Empty;
            return false;
        }

        _error = string.Empty;
        return true;
    }

    /// <summary>Resets the selected variable to its default.</summary>
    /// <returns><see langword="true"/> when something was reset.</returns>
    public bool ResetSelected()
    {
        if (Selected is not { } variable || (variable.Flags & CVarOptions.ReadOnly) != 0)
        {
            return false;
        }

        variable.Reset();
        return true;
    }

    /// <summary>Returns the row under a point, or -1.</summary>
    /// <param name="point">The point in back-buffer pixels.</param>
    /// <returns>The index into <see cref="Matches"/>, or -1.</returns>
    public int RowAt(UiVec2 point) => _rows.RowAt(point);

    /// <summary>Brings the selected row into view.</summary>
    public void RevealSelection() => _rows.EnsureVisible(_selected);

    private float RowHeight => _theme.Mono.LineHeight(_textSize) * 1.32f;

    private float HeaderHeight => (RowHeight * 2f) + 10f;

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        _textSize = context.Px(DevConsoleCVars.FontSize.Value);
        _cell = _theme.Mono.MeasureWidth("0", _textSize);
        _rows.RowHeight = RowHeight;
        _rows.Measure(context, available);
        return available;
    }

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        float footer = _error.Length > 0 ? RowHeight : 0f;
        _rows.Arrange(UiRect.FromEdges(
            contentRect.X,
            contentRect.Y + HeaderHeight,
            contentRect.Right,
            MathF.Max(contentRect.Y + HeaderHeight, contentRect.Bottom - footer)));
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        DrawFilterBox(painter);
        DrawColumnHeadings(painter);
    }

    /// <inheritdoc/>
    protected override void OnDrawOverlay(UiPainter painter)
    {
        if (_error.Length == 0)
        {
            return;
        }

        var row = new UiRect(Bounds.X, Bounds.Bottom - RowHeight, Bounds.Width, RowHeight);
        painter.FillRect(row, _theme.Ui.Danger.WithAlpha(40));
        painter.DrawText(
            _theme.Mono,
            _error,
            new UiVec2(row.X + _cell, row.Y + ((row.Height - _theme.Mono.LineHeight(_textSize)) * 0.5f)),
            _textSize,
            _theme.Ui.Danger);
    }

    private void DrawFilterBox(UiPainter painter)
    {
        var box = new UiRect(Bounds.X + _cell, Bounds.Y + 5f, Bounds.Width - (_cell * 2f), RowHeight);
        FilterRect = box;

        painter.FillRect(box, _theme.Ui.Surface);
        painter.StrokeRect(box, 1f, _theme.Ui.Border);

        float y = box.Y + ((box.Height - _theme.Mono.LineHeight(_textSize)) * 0.5f);
        float x = box.X + (_cell * 0.75f);

        painter.DrawText(_theme.Mono, "filter", new UiVec2(x, y), _textSize, _theme.Ui.TextDisabled);
        float textX = x + (_cell * 7f);

        if (FilterEdit.HasSelection)
        {
            painter.FillRect(
                new UiRect(
                    textX + (FilterEdit.SelectionStart * _cell),
                    box.Y + 2f,
                    (FilterEdit.SelectionEnd - FilterEdit.SelectionStart) * _cell,
                    box.Height - 4f),
                _theme.Ui.Selection);
        }

        painter.DrawText(_theme.Mono, FilterEdit.Text, new UiVec2(textX, y), _textSize, _theme.Ui.Text);

        painter.DrawText(
            _theme.Mono,
            $"{_matches.Count}",
            new UiVec2(box.Right - (_cell * 6f), y),
            _textSize,
            _theme.Ui.TextMuted);
    }

    private void DrawColumnHeadings(UiPainter painter)
    {
        float y = Bounds.Y + RowHeight + 8f;
        float x = Bounds.X + _cell;
        Bgra32 colour = _theme.Ui.TextDisabled;

        painter.DrawText(_theme.Mono, "NAME", new UiVec2(x, y), _textSize * 0.85f, colour);
        painter.DrawText(_theme.Mono, "VALUE", new UiVec2(x + (_cell * NameColumns), y), _textSize * 0.85f, colour);
        painter.DrawText(_theme.Mono, "DEFAULT", new UiVec2(x + (_cell * (NameColumns + ValueColumns)), y), _textSize * 0.85f, colour);
        painter.DrawText(
            _theme.Mono,
            "FLAGS",
            new UiVec2(x + (_cell * (NameColumns + ValueColumns + DefaultColumns)), y),
            _textSize * 0.85f,
            colour);

        painter.FillRect(
            new UiRect(Bounds.X, Bounds.Y + HeaderHeight - 1f, Bounds.Width, 1f),
            _theme.Ui.Border);
    }

    private void DrawVariableRow(UiPainter painter, int row, UiRect rect)
    {
        if (row < 0 || row >= _matches.Count)
        {
            return;
        }

        ICVar variable = _matches[row];
        bool selected = row == _selected;

        if (selected)
        {
            painter.FillRect(rect, _theme.Ui.Selection);
        }
        else if ((row & 1) == 1)
        {
            // A whisper of a stripe. Enough to keep the eye on one row across a wide table, not enough
            // to read as two kinds of row.
            painter.FillRect(rect, _theme.Ui.Text.WithAlpha(6));
        }

        // A variable the reader has changed is the one they are looking for; a bar in the gutter says
        // so without tinting the row and breaking the scan.
        if (!variable.IsDefault)
        {
            painter.FillRect(new UiRect(rect.X, rect.Y, 3f, rect.Height), _theme.Ui.Accent);
        }

        float y = rect.Y + ((rect.Height - _theme.Mono.LineHeight(_textSize)) * 0.5f);
        float x = rect.X + _cell;

        painter.DrawText(_theme.Mono, Fit(variable.Name, NameColumns - 1), new UiVec2(x, y), _textSize, _theme.SyntaxVariable);

        float valueX = x + (_cell * NameColumns);
        if (selected && _editing)
        {
            DrawValueEditor(painter, new UiRect(valueX - 2f, rect.Y + 1f, _cell * ValueColumns, rect.Height - 2f), y);
        }
        else
        {
            painter.DrawText(
                _theme.Mono,
                Fit(variable.ValueText, ValueColumns - 1),
                new UiVec2(valueX, y),
                _textSize,
                variable.IsDefault ? _theme.Ui.Text : _theme.Ui.Accent);
        }

        painter.DrawText(
            _theme.Mono,
            Fit(variable.DefaultText, DefaultColumns - 1),
            new UiVec2(x + (_cell * (NameColumns + ValueColumns)), y),
            _textSize,
            _theme.Ui.TextDisabled);

        painter.DrawText(
            _theme.Mono,
            FlagLetters(variable.Flags),
            new UiVec2(x + (_cell * (NameColumns + ValueColumns + DefaultColumns)), y),
            _textSize,
            _theme.Ui.TextMuted);

        float helpX = x + (_cell * (NameColumns + ValueColumns + DefaultColumns + FlagColumns));
        int helpColumns = Math.Max(0, (int)((rect.Right - helpX) / _cell) - 1);
        string trailing = variable.Help.Length > 0 ? variable.Help : variable.RangeText;

        if (helpColumns > 4 && trailing.Length > 0)
        {
            painter.DrawText(_theme.Mono, Fit(trailing, helpColumns), new UiVec2(helpX, y), _textSize, _theme.Ui.TextMuted);
        }
    }

    private void DrawValueEditor(UiPainter painter, UiRect box, float textY)
    {
        painter.FillRect(box, _theme.Ui.SurfaceRaised);
        painter.StrokeRect(box, 1f, _theme.Ui.Accent);

        float textX = box.X + 3f;

        if (ValueEdit.HasSelection)
        {
            painter.FillRect(
                new UiRect(
                    textX + (ValueEdit.SelectionStart * _cell),
                    box.Y + 1f,
                    (ValueEdit.SelectionEnd - ValueEdit.SelectionStart) * _cell,
                    box.Height - 2f),
                _theme.Ui.Selection);
        }

        painter.DrawText(_theme.Mono, ValueEdit.Text, new UiVec2(textX, textY), _textSize, _theme.Ui.Text);

        if (CaretVisible)
        {
            painter.FillRect(
                new UiRect(textX + (ValueEdit.Caret * _cell), box.Y + 2f, MathF.Max(1f, _cell * 0.12f), box.Height - 4f),
                _theme.Ui.Accent);
        }
    }

    /// <summary>Renders a variable's flags as one letter each.</summary>
    /// <param name="flags">The flags to render.</param>
    /// <returns>A short string, or spaces when the variable is unremarkable.</returns>
    /// <remarks>
    /// Letters rather than words: the column has to stay narrow enough that the help text still fits
    /// beside it, and a reader learns five letters far faster than they scroll horizontally.
    /// </remarks>
    private static string FlagLetters(CVarOptions flags)
    {
        Span<char> letters = stackalloc char[6];
        int at = 0;

        if ((flags & CVarOptions.Archive) != 0)
        {
            letters[at++] = 'A';
        }

        if ((flags & CVarOptions.Cheat) != 0)
        {
            letters[at++] = 'C';
        }

        if ((flags & CVarOptions.ReadOnly) != 0)
        {
            letters[at++] = 'R';
        }

        if ((flags & CVarOptions.RequiresRestart) != 0)
        {
            letters[at++] = '!';
        }

        if ((flags & CVarOptions.Developer) != 0)
        {
            letters[at++] = 'D';
        }

        return at == 0 ? string.Empty : new string(letters[..at]);
    }

    private static string Fit(string text, int columns)
    {
        if (columns <= 1 || text.Length <= columns)
        {
            return text;
        }

        return string.Concat(text.AsSpan(0, columns - 1), "…");
    }
}
