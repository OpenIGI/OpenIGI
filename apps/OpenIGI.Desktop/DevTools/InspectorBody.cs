using System.Globalization;
using OpenIGI.Core.Configuration;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Desktop;

/// <summary>
/// Lists what is in the world and reports what is known about whichever entry is selected.
/// </summary>
/// <remarks>
/// <para>
/// The list is filtered as the reader types, exactly as the variable browser is, because it answers
/// the same shape of question: there are a hundred soldiers in a level and the reader wants one.
/// </para>
/// <para>
/// It draws nothing at all when no world is attached — in the menu shell, or before a mission has
/// loaded — rather than showing an empty table that looks broken.
/// </para>
/// </remarks>
public sealed class InspectorBody : UiElement
{
    private const int NameColumns = 26;
    private const int KindColumns = 9;

    private readonly DevConsoleTheme _theme;
    private readonly VirtualListView _rows;
    private readonly List<InspectableEntity> _entities = [];
    private readonly List<string> _detail = [];

    private IWorldInspector? _inspector;
    private int _selected = -1;
    private float _cell = 8f;
    private float _textSize = 13f;

    /// <summary>Initialises a new instance of the <see cref="InspectorBody"/> class.</summary>
    /// <param name="theme">The palette and face.</param>
    /// <exception cref="ArgumentNullException"><paramref name="theme"/> is <see langword="null"/>.</exception>
    public InspectorBody(DevConsoleTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        _theme = theme;
        _rows = new VirtualListView(theme.Ui) { StickToEnd = false };
        _rows.DrawRow = DrawEntityRow;
        AddChild(_rows);
    }

    /// <summary>Gets the filter box's editing state.</summary>
    public TextEditState FilterEdit { get; } = new();

    /// <summary>Gets the entities currently listed.</summary>
    public IReadOnlyList<InspectableEntity> Entities => _entities;

    /// <summary>Gets the table's viewport, so the shell can route the wheel to it.</summary>
    public VirtualListView Rows => _rows;

    /// <summary>Gets a value indicating whether a world is attached to inspect.</summary>
    public bool HasWorld => _inspector is not null;

    /// <summary>Gets or sets the selected row, or -1 for none.</summary>
    public int SelectedIndex
    {
        get => _selected;
        set => _selected = _entities.Count == 0 ? -1 : Math.Clamp(value, -1, _entities.Count - 1);
    }

    /// <summary>Gets the selected entity, or <see langword="null"/> when nothing is selected.</summary>
    public InspectableEntity? Selected =>
        _selected >= 0 && _selected < _entities.Count ? _entities[_selected] : null;

    /// <summary>Attaches the world to inspect, or detaches when a mission ends.</summary>
    /// <param name="inspector">The world, or <see langword="null"/> for none.</param>
    public void Attach(IWorldInspector? inspector)
    {
        _inspector = inspector;
        _entities.Clear();
        _detail.Clear();
        _selected = -1;
        _rows.RowCount = 0;
    }

    /// <summary>Re-reads the world through the current filter.</summary>
    /// <remarks>
    /// Every frame the panel draws, unlike the variable browser: soldiers move, take damage and die,
    /// and a list that only refreshed on demand would be describing the level as it was.
    /// </remarks>
    public void Refresh()
    {
        if (_inspector is null)
        {
            return;
        }

        _inspector.Describe(FilterEdit.Text, _entities);
        _rows.RowCount = _entities.Count;
        _selected = _entities.Count == 0 ? -1 : Math.Clamp(_selected, -1, _entities.Count - 1);

        if (Selected is { } chosen)
        {
            _inspector.Detail(chosen.Id, _detail);
        }
        else
        {
            _detail.Clear();
        }
    }

    /// <summary>Returns the row under a point, or -1.</summary>
    /// <param name="point">The point in back-buffer pixels.</param>
    /// <returns>The index into <see cref="Entities"/>, or -1.</returns>
    public int RowAt(UiVec2 point) => _rows.RowAt(point);

    /// <summary>Brings the selected row into view.</summary>
    public void RevealSelection() => _rows.EnsureVisible(_selected);

    private float RowHeight => _theme.Mono.LineHeight(_textSize) * 1.32f;

    private float HeaderHeight => RowHeight + 10f;

    private float DetailHeight => _detail.Count == 0 ? 0f : (_detail.Count + 1) * RowHeight;

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        _textSize = context.Px(DevConsoleCVars.FontSize.Value * 0.95f);
        _cell = _theme.Mono.MeasureWidth("0", _textSize);
        _rows.RowHeight = RowHeight;
        _rows.Measure(context, available);
        return available;
    }

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        _rows.Arrange(UiRect.FromEdges(
            contentRect.X,
            contentRect.Y + HeaderHeight,
            contentRect.Right,
            MathF.Max(contentRect.Y + HeaderHeight, contentRect.Bottom - DetailHeight)));
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        if (_inspector is null)
        {
            painter.DrawText(
                _theme.Mono,
                "no world loaded",
                new UiVec2(Bounds.X + _cell, Bounds.Y + 10f),
                _textSize,
                _theme.Ui.TextDisabled);

            _rows.RowCount = 0;
            return;
        }

        Refresh();
        DrawFilterBox(painter);

        if (_entities.Count == 0)
        {
            painter.DrawText(
                _theme.Mono,
                FilterEdit.Text.Length > 0 ? "nothing matches" : "nothing to inspect — no mission loaded",
                new UiVec2(Bounds.X + (_cell * 2f), Bounds.Y + HeaderHeight + (RowHeight * 0.4f)),
                _textSize,
                _theme.Ui.TextDisabled);
        }
    }

    /// <inheritdoc/>
    protected override void OnDrawOverlay(UiPainter painter)
    {
        if (_detail.Count == 0)
        {
            return;
        }

        float height = DetailHeight;
        var panel = new UiRect(Bounds.X, Bounds.Bottom - height, Bounds.Width, height);

        painter.FillRect(panel, _theme.Ui.Surface);
        painter.FillRect(new UiRect(panel.X, panel.Y, panel.Width, 1f), _theme.Ui.Border);

        float y = panel.Y + (RowHeight * 0.4f);
        foreach (string line in _detail)
        {
            painter.DrawText(_theme.Mono, line, new UiVec2(panel.X + _cell, y), _textSize, _theme.Ui.TextMuted);
            y += RowHeight;
        }
    }

    private void DrawFilterBox(UiPainter painter)
    {
        var box = new UiRect(Bounds.X + _cell, Bounds.Y + 5f, Bounds.Width - (_cell * 2f), RowHeight);

        painter.FillRect(box, _theme.Ui.Surface);
        painter.StrokeRect(box, 1f, _theme.Ui.Border);

        float y = box.Y + ((box.Height - _theme.Mono.LineHeight(_textSize)) * 0.5f);
        painter.DrawText(_theme.Mono, "find", new UiVec2(box.X + (_cell * 0.75f), y), _textSize, _theme.Ui.TextDisabled);
        painter.DrawText(_theme.Mono, FilterEdit.Text, new UiVec2(box.X + (_cell * 5.5f), y), _textSize, _theme.Ui.Text);

        painter.DrawText(
            _theme.Mono,
            _entities.Count.ToString(CultureInfo.InvariantCulture),
            new UiVec2(box.Right - (_cell * 6f), y),
            _textSize,
            _theme.Ui.TextMuted);
    }

    private void DrawEntityRow(UiPainter painter, int row, UiRect rect)
    {
        if (row < 0 || row >= _entities.Count)
        {
            return;
        }

        InspectableEntity entity = _entities[row];

        if (row == _selected)
        {
            painter.FillRect(rect, _theme.Ui.Selection);
            painter.FillRect(new UiRect(rect.X, rect.Y, 3f, rect.Height), _theme.Ui.Accent);
        }
        else if ((row & 1) == 1)
        {
            painter.FillRect(rect, _theme.Ui.Text.WithAlpha(6));
        }

        float y = rect.Y + ((rect.Height - _theme.Mono.LineHeight(_textSize)) * 0.5f);
        float x = rect.X + _cell;

        painter.DrawText(_theme.Mono, Fit(entity.Name, NameColumns - 1), new UiVec2(x, y), _textSize, _theme.Ui.Text);

        painter.DrawText(
            _theme.Mono,
            Fit(entity.Kind, KindColumns - 1),
            new UiVec2(x + (_cell * NameColumns), y),
            _textSize,
            _theme.Ui.TextMuted);

        painter.DrawText(
            _theme.Mono,
            entity.State,
            new UiVec2(x + (_cell * (NameColumns + KindColumns)), y),
            _textSize,
            _theme.Ui.TextMuted);
    }

    private static string Fit(string text, int columns) =>
        columns <= 1 || text.Length <= columns ? text : string.Concat(text.AsSpan(0, columns - 1), "…");
}
