using OpenIGI.Core.Configuration;
using OpenIGI.Input;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Editor.Widgets;

/// <summary>One thing the command palette can do.</summary>
/// <param name="Title">What it is called, which the query is matched against.</param>
/// <param name="Run">What choosing it does.</param>
public sealed record PaletteEntry(string Title, Action Run)
{
    /// <summary>Gets a muted word at the right, such as the entry's category.</summary>
    public string? Detail { get; init; }

    /// <summary>Gets the shortcut that does the same thing, shown at the right.</summary>
    public string? Shortcut { get; init; }

    /// <summary>Gets the icon.</summary>
    public KitIcon Icon { get; init; }

    /// <summary>Gets whether it can be chosen now.</summary>
    public bool IsEnabled { get; init; } = true;
}

/// <summary>
/// A search box over everything the editor can do, at the top of the window as scene editors have it: type a few letters,
/// Up and Down to choose, Enter to run, Escape to close. Ranked as the console ranks its commands (exact, prefix, word,
/// anywhere, then letters in order), so both find things the same way.
/// </summary>
public sealed class CommandPalette : Popup
{
    private const int MaxRows = 12;

    private readonly IReadOnlyList<PaletteEntry> _entries;
    private readonly QueryField _query;
    private readonly List<PaletteEntry> _matches = [];
    private int _selected;
    private float _scroll;
    private Func<string, PaletteEntry?>? _typed;

    private CommandPalette(EditorTheme theme, IReadOnlyList<PaletteEntry> entries, string placeholder)
        : base(theme)
    {
        _entries = entries;
        _query = new QueryField(theme, this) { Placeholder = placeholder, SelectAllOnFocus = false };
        _query.TextChanged += _ => Refresh();
        AddChild(_query);
        Placement = PopupPlacement.At;
        Refresh();
    }

    /// <summary>
    /// Gets or sets what typing itself offers: given the text typed, an entry shown first (<c>Set to 12</c>), so a palette can
    /// take a value as well as pick one. Null offers nothing.
    /// </summary>
    public Func<string, PaletteEntry?>? Typed
    {
        get => _typed;
        set
        {
            _typed = value;
            Refresh();
        }
    }

    /// <summary>Gets or sets the text in the search box.</summary>
    public string Query
    {
        get => _query.Text;
        set => _query.Text = value;
    }

    /// <summary>Gets the entries that match what has been typed, best first.</summary>
    public IReadOnlyList<PaletteEntry> Matches => _matches;

    /// <inheritdoc/>
    public override bool SwallowsDismissingPress => false;

    private float Row => Theme.RowHeight + Theme.Px(4f);

    /// <summary>Opens a palette at the top of the window, with the keyboard in its search box.</summary>
    /// <param name="root">The window.</param>
    /// <param name="entries">What it offers, in the order shown before anything is typed.</param>
    /// <param name="placeholder">The search box's hint.</param>
    /// <returns>The palette.</returns>
    public static CommandPalette Open(EditorRoot root, IReadOnlyList<PaletteEntry> entries, string placeholder = "Type a command or an object's name")
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(entries);
        CommandPalette palette = new(root.Theme, entries, placeholder);
        float width = MathF.Min(root.Theme.Px(620f), root.Bounds.Width - root.Theme.Px(40f));
        palette.Anchor = new UiRect(MathF.Round(root.Bounds.Center.X - (width * 0.5f)), root.Bounds.Y + root.Theme.Px(56f), width, 0f);
        root.CloseAllPopups();
        root.ShowPopup(palette);
        palette._query.Focus();
        return palette;
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        int rows = Math.Max(1, Math.Min(MaxRows, _matches.Count));
        return new UiVec2(Anchor.Width, Header + (rows * Row) + Theme.Px(6f));
    }

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        float pad = Theme.Px(8f);
        _query.Place(context, new UiRect(contentRect.X + pad, contentRect.Y + pad, contentRect.Width - (pad * 2f), Theme.ControlHeight + Theme.Px(4f)));
    }

    /// <inheritdoc/>
    protected internal override bool OnPointerDown(in PointerEvent e)
    {
        int index = EntryAt(e.Position);
        if (e.Button == MouseButton.Left && index >= 0)
        {
            _selected = index;
            RunSelected();
            return true;
        }

        return Bounds.Contains(e.Position);
    }

    /// <inheritdoc/>
    protected internal override void OnPointerMove(in PointerEvent e)
    {
        int index = EntryAt(e.Position);
        if (index >= 0)
        {
            _selected = index;
        }
    }

    /// <inheritdoc/>
    protected internal override bool OnWheel(UiVec2 position, float notches, KeyModifiers modifiers)
    {
        _scroll = Math.Clamp(_scroll - notches, 0f, Math.Max(0, _matches.Count - MaxRows));
        return true;
    }

    /// <inheritdoc/>
    protected internal override bool OnKeyDown(in KeyEvent e)
    {
        switch (e.Key)
        {
            case KeyCode.Escape:
                Close();
                return true;
            case KeyCode.Return or KeyCode.NumpadEnter:
                RunSelected();
                return true;
            case KeyCode.Down:
                Select(_selected + 1);
                return true;
            case KeyCode.Up:
                Select(_selected - 1);
                return true;
            case KeyCode.PageDown:
                Select(_selected + MaxRows);
                return true;
            case KeyCode.PageUp:
                Select(_selected - MaxRows);
                return true;
            default:
                return false;
        }
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        base.OnDraw(painter);
        if (_matches.Count == 0)
        {
            EditorDraw.Label(painter, Theme, "Nothing matches.", new UiRect(Bounds.X + Theme.Px(14f), Bounds.Y + Header, Bounds.Width, Row), Theme.TextDisabled);
            return;
        }

        int first = (int)_scroll;
        for (int i = first; i < Math.Min(_matches.Count, first + MaxRows); i++)
        {
            DrawEntry(painter, _matches[i], RowRect(i), i == _selected);
        }
    }

    private float Header => Theme.ControlHeight + Theme.Px(4f) + Theme.Px(16f);

    private UiRect RowRect(int index) =>
        new(Bounds.X + Theme.Px(6f), Bounds.Y + Header + ((index - (int)_scroll) * Row), Bounds.Width - Theme.Px(12f), Row);

    private int EntryAt(UiVec2 point)
    {
        for (int i = (int)_scroll; i < Math.Min(_matches.Count, (int)_scroll + MaxRows); i++)
        {
            if (RowRect(i).Contains(point))
            {
                return i;
            }
        }

        return -1;
    }

    private void Refresh()
    {
        string query = _query.Text.Trim();
        _matches.Clear();
        if (query.Length == 0)
        {
            // What can run now first; what cannot stays listed, greyed, for finding its shortcut.
            _matches.AddRange(_entries.Where(static e => e.IsEnabled));
            _matches.AddRange(_entries.Where(static e => !e.IsEnabled));
        }
        else
        {
            _matches.AddRange(_entries
                .Select((entry, order) => (entry, order, score: Math.Max(ConsoleSuggester.Score(entry.Title, query) ?? -1, (ConsoleSuggester.Score(entry.Detail ?? string.Empty, query) ?? -1) - 300)))
                .Where(static m => m.score >= 0)
                .OrderByDescending(static m => m.entry.IsEnabled)
                .ThenByDescending(static m => m.score)
                .ThenBy(static m => m.order)
                .Select(static m => m.entry));
        }

        if (query.Length > 0 && _typed?.Invoke(query) is { } typed)
        {
            _matches.Insert(0, typed);
        }

        _selected = 0;
        _scroll = 0f;
    }

    private void Select(int index)
    {
        if (_matches.Count == 0)
        {
            return;
        }

        _selected = Math.Clamp(index, 0, _matches.Count - 1);
        if (_selected < _scroll)
        {
            _scroll = _selected;
        }
        else if (_selected >= _scroll + MaxRows)
        {
            _scroll = _selected - MaxRows + 1;
        }
    }

    private void RunSelected()
    {
        if (_selected < 0 || _selected >= _matches.Count || !_matches[_selected].IsEnabled)
        {
            return;
        }

        PaletteEntry chosen = _matches[_selected];
        Close();
        chosen.Run();
    }

    private void DrawEntry(UiPainter painter, PaletteEntry entry, UiRect row, bool selected)
    {
        if (selected)
        {
            EditorDraw.Fill(painter, Theme, row, Theme.Selection);
        }

        float x = row.X + Theme.Px(8f);
        if (entry.Icon != KitIcon.None)
        {
            EditorDraw.Icon(painter, Theme, entry.Icon, new UiRect(x, row.Y, Theme.IconSize, row.Height), EditorIcons.KindColour(entry.Icon) ?? Theme.Icon);
        }

        x += Theme.IconSize + Theme.Px(8f);
        float right = row.Right - Theme.Px(10f);
        foreach (string? side in (string?[])[entry.Shortcut, entry.Detail])
        {
            if (side is { Length: > 0 })
            {
                float width = Theme.SmallFont.MeasureWidth(side, Theme.SmallTextSize);
                float y = MathF.Round(row.Y + ((row.Height - Theme.SmallFont.LineHeight(Theme.SmallTextSize)) * 0.5f));
                painter.DrawText(Theme.SmallFont, side, new UiVec2(right - width, y), Theme.SmallTextSize, Theme.TextMuted);
                right -= width + Theme.Px(14f);
            }
        }

        EditorDraw.Label(painter, Theme, entry.Title, new UiRect(x, row.Y, right - x, row.Height), entry.IsEnabled ? Theme.Text : Theme.TextDisabled);
    }

    // The search box, which leaves the keys that choose and run entries to the palette.
    private sealed class QueryField(EditorTheme theme, CommandPalette palette) : TextField(theme)
    {
        protected internal override bool OnKeyDown(in KeyEvent e) =>
            e.Key is KeyCode.Return or KeyCode.NumpadEnter or KeyCode.Up or KeyCode.Down or KeyCode.PageUp or KeyCode.PageDown or KeyCode.Escape
                ? palette.OnKeyDown(e)
                : base.OnKeyDown(e);
    }
}
