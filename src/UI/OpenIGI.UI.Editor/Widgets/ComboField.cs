using OpenIGI.Core.Configuration;
using OpenIGI.Core.Graphics;
using OpenIGI.Input;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Editor.Widgets;

/// <summary>One value a <see cref="ComboField"/> offers: what it writes, and a word on what it is.</summary>
/// <param name="Value">The text written into the field when it is picked.</param>
/// <param name="Detail">What it is (a model's file, a key's text), shown muted beside it; null for nothing.</param>
public sealed record ComboItem(string Value, string? Detail = null);

/// <summary>
/// A text field that knows what is usually written in it: a dropdown of the valid values, narrowed as you type, picked with
/// a click or the arrow keys and Enter; and anything else typed is taken as it is.
/// </summary>
/// <remarks>
/// The list is the <see cref="Suggest"/> callback's, asked each time it opens, so it is always the game's as it stands; the
/// field filters and ranks it by what is typed, as the command palette does.
/// </remarks>
public sealed class ComboField : EditorElement
{
    // The most suggestions shown at once.
    private const int MostShown = 200;

    private readonly TextField _text;
    private SuggestionPopup? _popup;
    private bool _picking;

    /// <summary>Initializes a new instance of the <see cref="ComboField"/> class.</summary>
    /// <param name="theme">The theme.</param>
    public ComboField(EditorTheme theme)
        : base(theme)
    {
        _text = new TextField(theme);
        _text.TextChanged += typed =>
        {
            if (_text.IsFocused && !_picking)
            {
                Open(typed);
            }
        };
        _text.Committed += value =>
        {
            if (!_picking)
            {
                ClosePopup();
                Committed?.Invoke(value);
            }
        };
        AddChild(_text);
    }

    /// <summary>Raised when a value is taken: picked from the list, or typed and committed.</summary>
    public event Action<string>? Committed;

    /// <summary>Gets or sets what the list offers: every valid value, unfiltered; null offers nothing.</summary>
    public Func<IReadOnlyList<ComboItem>>? Suggest { get; set; }

    /// <summary>Gets or sets the text.</summary>
    public string Text
    {
        get => _text.Text;
        set => _text.Text = value;
    }

    /// <summary>Gets or sets the text shown greyed when the field is empty.</summary>
    public string? Placeholder
    {
        get => _text.Placeholder;
        set => _text.Placeholder = value;
    }

    /// <summary>Gets the suggestions open now, for tests; empty when the list is closed.</summary>
    public IReadOnlyList<ComboItem> Shown => _popup?.Items ?? [];

    private float ArrowWidth => Theme.Px(22f);

    private UiRect ArrowRect => new(Bounds.Right - ArrowWidth, Bounds.Y, ArrowWidth, Bounds.Height);

    /// <summary>Opens the list, every value when <paramref name="query"/> is empty, else the ones it matches, best first.</summary>
    /// <param name="query">What is typed.</param>
    public void Open(string query)
    {
        if (Root is not { } root || Suggest?.Invoke() is not { Count: > 0 } all)
        {
            ClosePopup();
            return;
        }

        string typed = query.Trim();
        ComboItem[] items = typed.Length == 0 || all.Any(i => i.Value.Equals(typed, StringComparison.OrdinalIgnoreCase))
            ? [.. all.Take(MostShown)]
            : [.. all
                .Select((item, order) => (item, order, score: Math.Max(ConsoleSuggester.Score(item.Value, typed) ?? -1, (ConsoleSuggester.Score(item.Detail ?? string.Empty, typed) ?? -1) - 300)))
                .Where(static r => r.score >= 0)
                .OrderByDescending(static r => r.score)
                .ThenBy(static r => r.order)
                .Take(MostShown)
                .Select(static r => r.item)];
        if (items.Length == 0)
        {
            ClosePopup();
            return;
        }

        if (_popup is null)
        {
            _popup = new SuggestionPopup(Theme, Pick, Key) { Anchor = Bounds, Placement = PopupPlacement.Below };
            _popup.Closed += _ => _popup = null;
            root.ShowPopup(_popup);
        }

        _popup.Anchor = Bounds;
        _popup.Show(items, Text);
    }

    /// <summary>Takes a value as if it were picked from the list.</summary>
    /// <param name="value">The value.</param>
    public void Pick(string value)
    {
        _picking = true;
        try
        {
            _text.Accept(value);
        }
        finally
        {
            _picking = false;
        }

        ClosePopup();
        Committed?.Invoke(value);
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) =>
        new(float.IsFinite(available.X) ? available.X : Theme.Px(180f), Theme.ControlHeight);

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect) =>
        _text.Place(context, contentRect with { Width = contentRect.Width - ArrowWidth + Theme.Px(1f) });

    /// <inheritdoc/>
    protected internal override bool OnPointerDown(in PointerEvent e)
    {
        if (e.Button != MouseButton.Left || !ArrowRect.Contains(e.Position))
        {
            return false;
        }

        if (_popup is not null)
        {
            ClosePopup();
        }
        else
        {
            _text.Focus();
            Open(string.Empty);
        }

        return true;
    }

    /// <inheritdoc/>
    protected internal override bool OnKeyDown(in KeyEvent e) => Key(e);

    // A key for the field, while the list is open too (an open popup takes the keyboard, so it hands its keys back here):
    // the list's keys first, the rest to the text.
    private bool Key(KeyEvent e)
    {
        if (ListKey(e))
        {
            return true;
        }

        return _popup is not null && _text.OnKeyDown(e);
    }

    private bool ListKey(KeyEvent e)
    {
        switch (e.Key)
        {
            case KeyCode.Down when _popup is null && (e.Alt || e.Modifiers == KeyModifiers.None):
                Open(string.Empty);
                return true;

            case KeyCode.Down or KeyCode.Up when _popup is not null:
                _popup.Move(e.Key == KeyCode.Down ? 1 : -1);
                return true;

            case KeyCode.Return or KeyCode.NumpadEnter or KeyCode.Tab when _popup is { Highlighted: { } chosen }:
                Pick(chosen.Value);
                return true;

            case KeyCode.Escape when _popup is not null:
                ClosePopup();
                return true;

            default:
                return false;
        }
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        UiRect arrow = ArrowRect;
        Bgra32 border = _text.IsFocused || _popup is not null ? Theme.Accent : IsPointerOver ? Theme.BorderStrong : Theme.Border;
        EditorDraw.Frame(painter, Theme, arrow, IsPointerOver && arrow.Contains(Root?.PointerPosition ?? default) ? Theme.Hover : Theme.Button, border);
        painter.DrawChevron(new UiVec2(arrow.Center.X, arrow.Center.Y), Theme.Px(3.5f), _popup is null ? UiDirection.Down : UiDirection.Up, Theme.Hairline * 1.5f, Theme.TextMuted);
    }

    private void ClosePopup()
    {
        _popup?.Close();
        _popup = null;
    }

    // The list under the field: rows of value and detail, scrolled, one highlighted for the arrow keys.
    private sealed class SuggestionPopup(EditorTheme theme, Action<string> pick, Func<KeyEvent, bool> key) : Popup(theme)
    {
        private const int VisibleRows = 12;

        private ComboItem[] _items = [];
        private int _highlight = -1;
        private int _top;

        public IReadOnlyList<ComboItem> Items => _items;

        public ComboItem? Highlighted => _highlight >= 0 && _highlight < _items.Length ? _items[_highlight] : null;

        public override bool SwallowsDismissingPress => false;

        // The keyboard stays the field's: an open popup is handed the keys, and gives them back.
        protected internal override bool OnKeyDown(in KeyEvent e) => key(e);

        private float Row => Theme.RowHeight;

        public void Show(ComboItem[] items, string current)
        {
            _items = items;
            _highlight = Math.Max(0, IndexOf(items, current));
            _top = Math.Clamp(_highlight - (VisibleRows / 2), 0, Math.Max(0, items.Length - VisibleRows));
        }

        public void Move(int by)
        {
            if (_items.Length == 0)
            {
                return;
            }

            _highlight = Math.Clamp(_highlight + by, 0, _items.Length - 1);
            _top = Math.Clamp(_top, Math.Max(0, _highlight - VisibleRows + 1), _highlight);
        }

        protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) =>
            new(Math.Max(Anchor.Width, Theme.Px(220f)), (Math.Min(VisibleRows, _items.Length) * Row) + Theme.Px(6f));

        protected internal override bool OnPointerDown(in PointerEvent e)
        {
            int row = (int)MathF.Floor((e.Position.Y - Bounds.Y - Theme.Px(3f)) / Row) + _top;
            if (e.Button == MouseButton.Left && row >= 0 && row < _items.Length)
            {
                pick(_items[row].Value);
            }

            return true;
        }

        protected internal override void OnPointerMove(in PointerEvent e)
        {
            int row = (int)MathF.Floor((e.Position.Y - Bounds.Y - Theme.Px(3f)) / Row) + _top;
            if (row >= 0 && row < _items.Length)
            {
                _highlight = row;
            }
        }

        protected internal override bool OnWheel(UiVec2 position, float notches, KeyModifiers modifiers)
        {
            _top = Math.Clamp(_top - (int)MathF.Round(notches * 3), 0, Math.Max(0, _items.Length - VisibleRows));
            return true;
        }

        protected override void OnDraw(UiPainter painter)
        {
            base.OnDraw(painter);
            painter.PushClip(Bounds);
            float y = Bounds.Y + Theme.Px(3f);
            for (int i = _top; i < _items.Length && i < _top + VisibleRows; i++)
            {
                UiRect row = new(Bounds.X + Theme.Px(3f), y, Bounds.Width - Theme.Px(6f), Row);
                if (i == _highlight)
                {
                    EditorDraw.Fill(painter, Theme, row, Theme.Selection, Theme.Px(3f));
                }

                ComboItem item = _items[i];
                float valueWidth = Theme.Font.MeasureWidth(item.Value, Theme.TextSize);
                EditorDraw.Label(painter, Theme, item.Value, row with { X = row.X + Theme.Px(6f), Width = MathF.Min(valueWidth + 2f, row.Width * 0.6f) }, Theme.Text);
                if (item.Detail is { Length: > 0 } detail)
                {
                    float x = row.X + Theme.Px(14f) + MathF.Min(valueWidth, row.Width * 0.6f);
                    EditorDraw.Label(painter, Theme, detail, UiRect.FromEdges(x, row.Y, row.Right - Theme.Px(6f), row.Bottom), Theme.TextMuted);
                }

                y += Row;
            }

            if (_items.Length > VisibleRows)
            {
                float track = Bounds.Height - Theme.Px(6f);
                float thumb = MathF.Max(Theme.Px(16f), track * VisibleRows / _items.Length);
                float at = Bounds.Y + Theme.Px(3f) + ((track - thumb) * _top / Math.Max(1, _items.Length - VisibleRows));
                painter.FillRect(new UiRect(Bounds.Right - Theme.Px(5f), at, Theme.Px(3f), thumb), Theme.BorderStrong);
            }

            painter.PopClip();
        }

        private static int IndexOf(ComboItem[] items, string value)
        {
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].Value.Equals(value, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
