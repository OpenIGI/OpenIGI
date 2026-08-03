using OpenIGI.Core.Graphics;
using OpenIGI.Rendering;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Widgets;

/// <summary>
/// A button that opens a list of choices beneath itself, any of which may be shown but not selectable.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="Dropdown"/> cycles through its options in place, which is what a gamepad wants from a
/// settings row. This is the other kind: a caption that stays exactly what it was and drops a real list
/// under it when pressed. It is what the shell's title is — the game being played, with the other titles
/// listed under it — and the reason it is a separate widget is that a list which is <em>shown</em> but
/// unselectable is the whole point. An option nobody can pick still tells the player it exists.
/// </para>
/// <para>
/// It derives from <see cref="Button"/> so the shut state is drawn by exactly the same code as any other
/// button: whatever it looked like before it gained a list, it looks like now.
/// </para>
/// <para>
/// The list is drawn in the overlay pass, after the widget's own children, so it lands over whatever is
/// beneath it — and because the shell's top bar is drawn last of all, over the page as well. It also
/// answers for the list's rectangle through <see cref="PointerBounds"/>, which is what lets a click down
/// there reach it: the ordinary tree walk descends only into elements whose bounds contain the point, and
/// the list is deliberately outside them.
/// </para>
/// </remarks>
public class DropdownButton : Button
{
    private readonly List<string> _items;
    private readonly Tween _open = new(0f, duration: 0.16f, Ease.OutCubic);

    private UiRect _listBounds;
    private float _rowHeight;
    private int _highlighted;

    /// <summary>Initializes a new instance of the <see cref="DropdownButton"/> class.</summary>
    /// <param name="theme">The theme to draw from.</param>
    /// <param name="text">The caption, which does not change with the selection.</param>
    /// <param name="items">The choices listed under it.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public DropdownButton(UiTheme theme, string text, IEnumerable<string> items)
        : base(theme, text)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = [.. items];
    }

    /// <summary>Raised when a selectable choice is picked, with its index.</summary>
    public event Action<int>? ItemSelected;

    /// <summary>Gets the choices.</summary>
    public IReadOnlyList<string> Items => _items;

    /// <summary>Gets or sets which choice is the current one, or -1 for none.</summary>
    public int CurrentIndex { get; set; } = -1;

    /// <summary>Gets or sets a predicate saying whether a choice can be picked; the rest are shown greyed.</summary>
    public Func<int, bool>? ItemEnabled { get; set; }

    /// <summary>Gets a value indicating whether the list is showing.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>Gets or sets the width of the list, in design units; zero follows the caption's width.</summary>
    public float ListWidth { get; set; } = 260f;

    /// <summary>Shuts the list.</summary>
    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        IsOpen = false;
        _open.Retarget(0f);
    }

    /// <inheritdoc/>
    public override UiRect PointerBounds =>
        IsOpen && !_listBounds.IsEmpty ? Union(base.PointerBounds, _listBounds) : base.PointerBounds;

    /// <inheritdoc/>
    public override void ActivateAt(UiVec2 point)
    {
        if (!IsOpen)
        {
            Open();
            RaiseActivated();
            return;
        }

        int index = IndexAt(point);
        if (index >= 0)
        {
            Choose(index);
            return;
        }

        // A click anywhere else while the list is open shuts it, which is what a menu is expected to do.
        Close();
    }

    /// <inheritdoc/>
    public override void PointerMoved(UiVec2 point)
    {
        if (IsOpen && IndexAt(point) is var index && index >= 0)
        {
            _highlighted = index;
        }
    }

    /// <inheritdoc/>
    /// <remarks>While the list is open, up and down walk it and are consumed so focus stays put.</remarks>
    public override bool HandleNavigate(NavDirection direction)
    {
        if (!IsOpen || _items.Count == 0)
        {
            return false;
        }

        switch (direction)
        {
            case NavDirection.Up:
                _highlighted = (_highlighted - 1 + _items.Count) % _items.Count;
                return true;
            case NavDirection.Down:
                _highlighted = (_highlighted + 1) % _items.Count;
                return true;
            default:
                return true; // left and right belong to the list too while it is up
        }
    }

    /// <inheritdoc/>
    protected override void OnActivate()
    {
        if (IsOpen)
        {
            Choose(_highlighted);
        }
        else
        {
            Open();
        }
    }

    /// <inheritdoc/>
    protected override void OnPointerOverChanged(bool over)
    {
        if (!over)
        {
            _highlighted = Math.Max(0, CurrentIndex);
        }
    }

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        base.OnUpdate(elapsedSeconds);
        _open.Update(elapsedSeconds);
    }

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        // Resolved at layout time so a click can be mapped onto the list whether or not it has been drawn,
        // and so the rectangle this answers for is never a frame out of date.
        float sizePx = context.Px(Theme.BodyTextSize);
        _rowHeight = Theme.Body.LineHeight(sizePx) + context.Px(16f);

        float width = ListWidth > 0f ? context.Px(ListWidth) : contentRect.Width;
        width = MathF.Max(width, contentRect.Width);

        _listBounds = _items.Count == 0
            ? UiRect.Empty
            : new UiRect(contentRect.X, contentRect.Bottom, width, _rowHeight * _items.Count);
    }

    /// <inheritdoc/>
    protected override void OnDrawOverlay(UiPainter painter)
    {
        float amount = _open.Value;
        if (amount <= 0.002f || _listBounds.IsEmpty)
        {
            return;
        }

        // The list drops down as it opens rather than appearing whole.
        var box = new UiRect(_listBounds.X, _listBounds.Y, _listBounds.Width, _listBounds.Height * amount);
        byte alpha = (byte)(255 * amount);

        painter.FillRect(box, Theme.Panel.WithAlpha((byte)(240 * amount)), BlendMode.AlphaBlend);
        painter.StrokeRect(box, painter.Context.Px(1f), Theme.Border.WithAlpha(alpha), BlendMode.AlphaBlend);

        painter.PushClip(box);

        float sizePx = painter.Context.Px(Theme.BodyTextSize);
        float pad = painter.Context.Px(16f);

        for (int i = 0; i < _items.Count; i++)
        {
            var row = new UiRect(_listBounds.X, _listBounds.Y + (i * _rowHeight), _listBounds.Width, _rowHeight);
            bool enabled = ItemEnabled?.Invoke(i) ?? true;

            if (enabled && i == _highlighted)
            {
                painter.FillRect(row, Theme.FocusFill.WithAlpha((byte)(Theme.FocusFill.A * amount)), BlendMode.AlphaBlend);
                painter.FillRect(
                    new UiRect(row.X, row.Y, painter.Context.Px(3f), row.Height),
                    Theme.Accent.WithAlpha(alpha),
                    BlendMode.AlphaBlend);
            }

            Bgra32 colour = !enabled ? Theme.TextDisabled
                : i == CurrentIndex ? Theme.Accent
                : Theme.Text;

            painter.DrawText(
                Theme.Body,
                _items[i],
                new UiVec2(row.X + pad, row.Y + ((row.Height - Theme.Body.LineHeight(sizePx)) * 0.5f)),
                sizePx,
                colour.WithAlpha((byte)(colour.A * amount)));

            // An option that exists but cannot be taken says so, rather than looking like a dead click.
            if (!enabled)
            {
                const string note = "unavailable";
                float noteSize = sizePx * 0.78f;
                float noteWidth = Theme.Body.MeasureWidth(note, noteSize);
                painter.DrawText(
                    Theme.Body,
                    note,
                    new UiVec2(row.Right - pad - noteWidth, row.Y + ((row.Height - Theme.Body.LineHeight(noteSize)) * 0.5f)),
                    noteSize,
                    Theme.TextDisabled.WithAlpha((byte)(Theme.TextDisabled.A * amount)));
            }
        }

        painter.PopClip();
    }

    private void Open()
    {
        if (_items.Count == 0)
        {
            return;
        }

        IsOpen = true;
        _highlighted = Math.Max(0, CurrentIndex);
        _open.Retarget(1f);
    }

    private void Choose(int index)
    {
        if (index < 0 || index >= _items.Count)
        {
            Close();
            return;
        }

        // A choice that cannot be taken does not close the list: the player pressed something disabled and
        // the honest response is that nothing happens, not that the menu shuts as though it had worked.
        if (ItemEnabled is not null && !ItemEnabled(index))
        {
            return;
        }

        Close();
        CurrentIndex = index;
        ItemSelected?.Invoke(index);
    }

    private int IndexAt(UiVec2 point)
    {
        if (!IsOpen || _listBounds.IsEmpty || _rowHeight <= 0f || !_listBounds.Contains(point))
        {
            return -1;
        }

        int index = (int)((point.Y - _listBounds.Y) / _rowHeight);
        return index >= 0 && index < _items.Count ? index : -1;
    }

    private static UiRect Union(UiRect a, UiRect b)
    {
        if (a.IsEmpty)
        {
            return b;
        }

        return UiRect.FromEdges(
            MathF.Min(a.X, b.X),
            MathF.Min(a.Y, b.Y),
            MathF.Max(a.Right, b.Right),
            MathF.Max(a.Bottom, b.Bottom));
    }
}
