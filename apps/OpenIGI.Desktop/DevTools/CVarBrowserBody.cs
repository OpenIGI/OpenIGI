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
/// <b>Editing happens in the row.</b> Copying a name to the prompt and typing a value there works,
/// but it puts the value being changed and the value it had on different parts of the screen. Editing
/// in place keeps them together, and a rejected value can be reported against the row that refused it.
/// </para>
/// </remarks>
public sealed class CVarBrowserBody : UiElement
{
    private const int NameColumns = 30;
    private const int ValueColumns = 13;
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

    /// <summary>Initialises a new instance of the <see cref="CVarBrowserBody"/> class.</summary>
    /// <param name="registry">The registry listed.</param>
    /// <param name="theme">The palette, type scale and spacing.</param>
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

    /// <summary>Gets or sets the generated sprites used for rounded controls.</summary>
    public UiSurfaces? Surfaces { get; set; }

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

    private float HeaderHeight => _theme.ControlHeight + (_theme.Unit * 4f) + _theme.RowHeight;

    private float FooterHeight => _error.Length == 0 ? 0f : _theme.ControlHeight + _theme.Unit;

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        _rows.RowHeight = _theme.RowHeight;
        _rows.Measure(context, available);
        return available;
    }

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        float top = MathF.Round(contentRect.Y + HeaderHeight);
        _rows.Arrange(UiRect.FromEdges(
            contentRect.X,
            top,
            contentRect.Right,
            MathF.Max(top, MathF.Round(contentRect.Bottom - FooterHeight))));
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

        var row = new UiRect(Bounds.X, MathF.Round(Bounds.Bottom - FooterHeight), Bounds.Width, FooterHeight);
        painter.FillRect(row, _theme.Ui.Danger.WithAlpha(28));
        painter.FillRect(new UiRect(row.X, row.Y, MathF.Max(2f, _theme.Unit * 0.5f), row.Height), _theme.Ui.Danger);

        painter.DrawText(
            _theme.BodyFont,
            _error,
            new UiVec2(
                MathF.Round(row.X + _theme.Gutter),
                MathF.Round(row.Y + ((row.Height - _theme.BodyFont.LineHeight(_theme.BodySize)) * 0.5f))),
            _theme.BodySize,
            _theme.Ui.Danger);
    }

    private void DrawFilterBox(UiPainter painter)
    {
        float unit = _theme.Unit;
        var box = new UiRect(
            MathF.Round(Bounds.X + _theme.Gutter),
            MathF.Round(Bounds.Y + (unit * 2f)),
            MathF.Round(Bounds.Width - (_theme.Gutter * 2f)),
            _theme.ControlHeight);

        FilterRect = box;


        float y = MathF.Round(box.Y + ((box.Height - _theme.BodyFont.LineHeight(_theme.BodySize)) * 0.5f));
        float textX = MathF.Round(box.X + (unit * 2f));

        painter.DrawText(
            _theme.BodyFont,
            FilterEdit.Text.Length > 0 ? FilterEdit.Text : "filter variables",
            new UiVec2(textX, y),
            _theme.BodySize,
            FilterEdit.Text.Length > 0 ? _theme.Ui.Text : _theme.Ui.TextDisabled);

        string count = $"{_matches.Count}";
        painter.DrawText(
            _theme.SmallFont,
            count,
            new UiVec2(
                MathF.Round(box.Right - (unit * 2f) - _theme.SmallFont.MeasureWidth(count, _theme.SmallSize)),
                MathF.Round(box.Y + ((box.Height - _theme.SmallFont.LineHeight(_theme.SmallSize)) * 0.5f))),
            _theme.SmallSize,
            _theme.Ui.TextMuted);
    }

    /// <summary>
    /// Draws the column headings, on exactly the cells the rows below use.
    /// </summary>
    /// <param name="painter">The painter to draw into.</param>
    /// <remarks>
    /// Placed from the same character cell as the data, so a heading can never drift out of line with
    /// the column it names — which is what happens the moment a heading is set at a different size and
    /// positioned by eye.
    /// </remarks>
    private void DrawColumnHeadings(UiPainter painter)
    {
        float cell = _theme.Cell;
        float y = MathF.Round(Bounds.Y + (_theme.Unit * 3f) + _theme.ControlHeight);
        float x = MathF.Round(Bounds.X + _theme.Gutter);
        Bgra32 colour = _theme.Ui.TextDisabled;

        painter.DrawText(_theme.SmallFont, "NAME", new UiVec2(x, y), _theme.SmallSize, colour);
        painter.DrawText(_theme.SmallFont, "VALUE", new UiVec2(MathF.Round(x + (cell * NameColumns)), y), _theme.SmallSize, colour);
        painter.DrawText(_theme.SmallFont, "DEFAULT", new UiVec2(MathF.Round(x + (cell * (NameColumns + ValueColumns))), y), _theme.SmallSize, colour);
        painter.DrawText(
            _theme.SmallFont,
            "FLAGS",
            new UiVec2(MathF.Round(x + (cell * (NameColumns + ValueColumns + DefaultColumns))), y),
            _theme.SmallSize,
            colour);

        painter.FillRect(
            new UiRect(Bounds.X, MathF.Round(Bounds.Y + HeaderHeight - 1f), Bounds.Width, 1f),
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
        float cell = _theme.Cell;

        if (selected)
        {
            painter.FillRect(rect, _theme.Ui.Selection);
        }
        else if ((row & 1) == 1)
        {
            // A whisper of a stripe. Enough to keep the eye on one row across a wide table, not enough
            // to read as two kinds of row.
            painter.FillRect(rect, _theme.Ui.Text.WithAlpha(8));
        }

        // A variable the reader has changed is the one they are looking for; a bar in the gutter says
        // so without tinting the row and breaking the scan.
        if (!variable.IsDefault)
        {
            painter.FillRect(new UiRect(rect.X, rect.Y, MathF.Max(2f, _theme.Unit * 0.5f), rect.Height), _theme.Ui.Accent);
        }

        float y = MathF.Round(rect.Y + ((rect.Height - _theme.BodyFont.LineHeight(_theme.BodySize)) * 0.5f));
        float x = MathF.Round(rect.X + _theme.Gutter);

        painter.DrawText(_theme.BodyFont, Fit(variable.Name, NameColumns - 1), new UiVec2(x, y), _theme.BodySize, _theme.SyntaxVariable);

        float valueX = MathF.Round(x + (cell * NameColumns));
        if (selected && _editing)
        {
            DrawValueEditor(painter, new UiRect(valueX - _theme.Unit, rect.Y + 1f, cell * ValueColumns, rect.Height - 2f), y);
        }
        else
        {
            painter.DrawText(
                _theme.BodyFont,
                Fit(variable.ValueText, ValueColumns - 1),
                new UiVec2(valueX, y),
                _theme.BodySize,
                variable.IsDefault ? _theme.Ui.Text : _theme.Ui.Accent);
        }

        painter.DrawText(
            _theme.SmallFont,
            Fit(variable.DefaultText, DefaultColumns - 1),
            new UiVec2(MathF.Round(x + (cell * (NameColumns + ValueColumns))), MathF.Round(rect.Y + ((rect.Height - _theme.SmallFont.LineHeight(_theme.SmallSize)) * 0.5f))),
            _theme.SmallSize,
            _theme.Ui.TextDisabled);

        painter.DrawText(
            _theme.SmallFont,
            FlagLetters(variable.Flags),
            new UiVec2(MathF.Round(x + (cell * (NameColumns + ValueColumns + DefaultColumns))), MathF.Round(rect.Y + ((rect.Height - _theme.SmallFont.LineHeight(_theme.SmallSize)) * 0.5f))),
            _theme.SmallSize,
            _theme.Ui.TextMuted);

        float helpX = MathF.Round(x + (cell * (NameColumns + ValueColumns + DefaultColumns + FlagColumns)));
        int helpColumns = Math.Max(0, (int)((rect.Right - helpX - _theme.Gutter) / cell));
        string trailing = variable.Help.Length > 0 ? variable.Help : variable.RangeText;

        if (helpColumns > 6 && trailing.Length > 0)
        {
            painter.DrawText(
                _theme.SmallFont,
                Fit(trailing, helpColumns),
                new UiVec2(helpX, MathF.Round(rect.Y + ((rect.Height - _theme.SmallFont.LineHeight(_theme.SmallSize)) * 0.5f))),
                _theme.SmallSize,
                _theme.Ui.TextMuted);
        }
    }

    private void DrawValueEditor(UiPainter painter, UiRect box, float textY)
    {
        painter.FillRoundedRect(box, Surfaces?.Rounded ?? default, _theme.Ui.SurfaceRaised, _theme.Unit);
        painter.StrokeRect(box, 1f, _theme.Ui.Accent);

        float textX = MathF.Round(box.X + _theme.Unit);

        if (ValueEdit.HasSelection)
        {
            painter.FillRect(
                new UiRect(
                    MathF.Round(textX + (ValueEdit.SelectionStart * _theme.Cell)),
                    box.Y + 2f,
                    MathF.Round((ValueEdit.SelectionEnd - ValueEdit.SelectionStart) * _theme.Cell),
                    box.Height - 4f),
                _theme.Ui.Selection);
        }

        painter.DrawText(_theme.BodyFont, ValueEdit.Text, new UiVec2(textX, textY), _theme.BodySize, _theme.Ui.Text);

        if (CaretVisible)
        {
            painter.FillRect(
                new UiRect(
                    MathF.Round(textX + (ValueEdit.Caret * _theme.Cell)),
                    box.Y + 2f,
                    MathF.Max(1f, MathF.Round(_theme.Cell * 0.1f)),
                    box.Height - 4f),
                _theme.Ui.Accent);
        }
    }

    /// <summary>Renders a variable's flags as one letter each.</summary>
    /// <param name="flags">The flags to render.</param>
    /// <returns>A short string, or empty when the variable is unremarkable.</returns>
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

    private static string Fit(string text, int columns) =>
        columns <= 1 || text.Length <= columns ? text : string.Concat(text.AsSpan(0, columns - 1), "…");
}
