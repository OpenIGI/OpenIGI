using OpenIGI.Core.Graphics;
using OpenIGI.UI.Drawing;
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
    private float _rowGap;
    private float _listPad;
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
    public float ListWidth { get; set; } = 360f;

    /// <summary>Gets or sets where the list opens relative to the button's bottom-left, in design units.</summary>
    public UiVec2 ListOffset { get; set; } = new(-12f, 18f);

    /// <summary>Gets or sets a second line shown under each item's name, or <see langword="null"/> for none.</summary>
    public Func<int, string?>? ItemDetail { get; set; }

    /// <summary>Gets or sets the soft shadow sprite cast under the open list, normally <see cref="UiSurfaces.Shadow"/>.</summary>
    public NineSlice? Shadow { get; set; }

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
        _rowHeight = context.Px(ItemDetail is null ? 48f : 64f);
        _rowGap = context.Px(4f);
        _listPad = context.Px(8f);
        float pad = _listPad;

        float width = ListWidth > 0f ? context.Px(ListWidth) : contentRect.Width;
        width = MathF.Max(width, contentRect.Width);

        _listBounds = _items.Count == 0
            ? UiRect.Empty
            : new UiRect(
                contentRect.X + context.Px(ListOffset.X),
                contentRect.Bottom + context.Px(ListOffset.Y),
                width,
                (_rowHeight * _items.Count) + (context.Px(4f) * (_items.Count - 1)) + (pad * 2f));
    }

    /// <inheritdoc/>
    protected override void OnDrawOverlay(UiPainter painter)
    {
        float amount = _open.Value;
        if (amount <= 0.002f || _listBounds.IsEmpty)
        {
            return;
        }

        LayoutContext context = painter.Context;

        // The menu drops a few units as it opens and fades in, the way the design's does.
        float drop = context.Px(8f) * (1f - amount);
        var box = new UiRect(_listBounds.X, _listBounds.Y - drop, _listBounds.Width, _listBounds.Height);
        painter.PushOpacity(amount);

        if (Shadow is { } shadow && shadow.IsValid)
        {
            painter.DrawShadow(box, shadow, UiPalette.InkAt(0.6f), context.Px(40f), new UiVec2(0f, context.Px(20f)));
        }

        painter.DrawBackdrop(box, context.Px(Theme.GlassBlur), Theme.SurfaceRaised);
        painter.StrokeRect(box, context.Px(1f), UiPalette.White(0.1f));

        float pad = context.Px(8f);
        float gap = context.Px(4f);
        float titleSize = context.Px(22f);
        float detailSize = context.Px(11f);

        for (int i = 0; i < _items.Count; i++)
        {
            var row = new UiRect(box.X + pad, box.Y + pad + (i * (_rowHeight + gap)), box.Width - (pad * 2f), _rowHeight);
            bool enabled = ItemEnabled?.Invoke(i) ?? true;
            bool current = i == CurrentIndex;

            if (current)
            {
                painter.FillRect(row, Theme.Selection);
                painter.FillRect(new UiRect(row.X, row.Y, context.Px(3f), row.Height), Theme.Accent);
            }
            else if (enabled && i == _highlighted)
            {
                painter.FillRect(row, UiPalette.White(0.06f));
            }

            Bgra32 title = !enabled ? Theme.TextDisabled : current || i == _highlighted ? Theme.TextBright : Theme.TextSecondary;
            string caption = _items[i].ToUpperInvariant();
            string? detail = ItemDetail?.Invoke(i);
            float textX = row.X + context.Px(16f);

            if (detail is null)
            {
                painter.DrawLabel(Theme.Display, caption, new UiVec2(textX, UiRowStyle.CapsCenterY(Theme.Display, titleSize, row)), titleSize, title, 0.14f);
            }
            else
            {
                float lineGap = context.Px(4f);
                float block = (titleSize * 0.7f) + lineGap + Theme.Mono.LineHeight(detailSize);
                float top = row.Y + ((row.Height - block) * 0.5f);
                painter.DrawLabel(Theme.Display, caption, new UiVec2(textX, top - (Theme.Display.Ascent(titleSize) - (titleSize * 0.7f))), titleSize, title, 0.14f);
                painter.DrawLabel(Theme.Mono, detail, new UiVec2(textX, top + (titleSize * 0.7f) + lineGap), detailSize, enabled ? Theme.TextMuted : Theme.TextDisabled, 0.2f);
            }

            // The running one is named as such, and one that exists but cannot be taken says why, rather
            // than looking like a dead click.
            string? pill = current ? "ACTIVE" : !enabled ? "UNAVAILABLE" : null;
            if (pill is not null)
            {
                float pillSize = context.Px(10f);
                float pillWidth = UiPainter.MeasureLabel(Theme.Mono, pill, pillSize, 0.2f) + context.Px(14f);
                float pillHeight = Theme.Mono.LineHeight(pillSize) + context.Px(6f);
                var pillBox = new UiRect(row.Right - context.Px(16f) - pillWidth, row.Y + ((row.Height - pillHeight) * 0.5f), pillWidth, pillHeight);
                if (current)
                {
                    painter.FillRect(pillBox, Theme.Accent);
                }
                else
                {
                    painter.StrokeRect(pillBox, context.Px(1f), UiPalette.White(0.12f));
                }

                painter.DrawLabel(
                    Theme.Mono,
                    pill,
                    new UiVec2(pillBox.X + context.Px(7f), UiRowStyle.CenterY(Theme.Mono, pillSize, pillBox)),
                    pillSize,
                    current ? Theme.OnAccent : Theme.TextDisabled,
                    0.2f);
            }
        }

        painter.PopOpacity();
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

        // Rows are separated by a small gap and inset by the menu's padding; a point in a gap belongs to
        // the row above it, which is what keeps a slow sweep down the list from flickering.
        float local = point.Y - _listBounds.Y - _listPad;
        int index = local < 0f ? -1 : (int)(local / (_rowHeight + _rowGap));
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
