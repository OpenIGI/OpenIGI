using OpenIGI.Core.Graphics;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Widgets;

/// <summary>
/// A horizontal selector that slides between items, the current one centred and its neighbours peeking
/// in from the sides.
/// </summary>
/// <remarks>
/// Left and right change the selection (and are consumed so focus stays on the carousel), and a spring
/// carries a continuous <see cref="Position"/> — a fractional index — so the row glides between items
/// rather than jumping. The mission carousel in the shell reads <see cref="Position"/> to place its
/// cards; the built-in drawing shows the item titles, which is enough on its own for a simple picker.
/// </remarks>
public class Carousel : Control
{
    private readonly List<string> _items;
    private readonly Spring _position = new(0f, smoothTime: 0.16f);
    private int _selected;

    /// <summary>Initializes a new instance of the <see cref="Carousel"/> class.</summary>
    /// <param name="theme">The theme to draw from.</param>
    /// <param name="items">The item titles.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public Carousel(UiTheme theme, IEnumerable<string> items)
        : base(theme)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = [.. items];
    }

    /// <summary>Raised when the selected item changes, with the new index.</summary>
    public event Action<int>? SelectionChanged;

    /// <summary>Gets or sets whether moving past an end wraps to the other.</summary>
    public bool Wrap { get; set; }

    /// <summary>Gets the item titles.</summary>
    public IReadOnlyList<string> Items => _items;

    /// <summary>Gets the animated fractional position, between item indices, for external rendering.</summary>
    public float Position => _position.Value;

    /// <summary>Gets or sets the selected index.</summary>
    public int SelectedIndex
    {
        get => _selected;
        set
        {
            if (_items.Count == 0)
            {
                return;
            }

            int next = Wrap
                ? ((value % _items.Count) + _items.Count) % _items.Count
                : Math.Clamp(value, 0, _items.Count - 1);
            if (next == _selected)
            {
                return;
            }

            _selected = next;
            _position.Target = _selected;
            SelectionChanged?.Invoke(_selected);
        }
    }

    /// <inheritdoc/>
    public override bool HandleNavigate(NavDirection direction)
    {
        switch (direction)
        {
            case NavDirection.Left:
                SelectedIndex -= 1;
                return true;
            case NavDirection.Right:
                SelectedIndex += 1;
                return true;
            default:
                return false;
        }
    }

    /// <inheritdoc/>
    /// <remarks>Clicking either side of the centred item steps that way, the pointer's reading of the row.</remarks>
    public override void ActivateAt(UiVec2 point)
    {
        float dead = Bounds.Width * 0.12f;
        float offset = point.X - Bounds.Center.X;
        if (offset < -dead)
        {
            SelectedIndex -= 1;
        }
        else if (offset > dead)
        {
            SelectedIndex += 1;
        }

        RaiseActivated();
    }

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        base.OnUpdate(elapsedSeconds);
        _position.Update(elapsedSeconds);
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) =>
        new(available.X, context.Px(Theme.HeadingTextSize) + context.Px(24f));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        if (_items.Count == 0)
        {
            return;
        }

        painter.PushClip(Bounds);

        float sizePx = painter.Context.Px(Theme.HeadingTextSize);
        float spacing = painter.Context.Px(360f);
        float centerX = Bounds.Center.X;
        float centerY = Bounds.Y + ((Bounds.Height - Theme.Heading.LineHeight(sizePx)) * 0.5f);

        // Draw a window of items around the animated position, the centre one bright and the rest dimmed
        // and shrinking with distance from centre.
        int center = (int)MathF.Round(_position.Value);
        for (int i = center - 2; i <= center + 2; i++)
        {
            if (i < 0 || i >= _items.Count)
            {
                continue;
            }

            float offset = i - _position.Value;
            float x = centerX + (offset * spacing);
            float distance = MathF.Abs(offset);
            byte alpha = (byte)(255 * MathF.Max(0f, 1f - (distance * 0.55f)));
            Bgra32 color = (distance < 0.5f ? Interpolate.Lerp(Theme.Text, Theme.Accent, Visual.Focus) : Theme.TextMuted).WithAlpha(alpha);

            float width = Theme.Heading.MeasureWidth(_items[i], sizePx);
            painter.DrawTextShadowed(Theme.Heading, _items[i], new UiVec2(x - (width * 0.5f), centerY), sizePx, color, Theme.Shadow.WithAlpha(alpha), new UiVec2(0f, painter.Context.Px(2f)));
        }

        painter.PopClip();
    }
}
