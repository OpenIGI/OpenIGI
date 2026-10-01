using OpenIGI.Core.Graphics;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Widgets;

/// <summary>
/// One cell of an option strip: a monospaced caption, the value large in the condensed face, a row of
/// pips marking where the value sits in the list, and an up / down pair to step through it.
/// </summary>
/// <remarks>
/// <para>
/// The campaign's Difficulty, Time and Weather. A cell is a whole choice at a glance — the pips say how many
/// options there are and which one this is, so a player never has to cycle through to find out.
/// </para>
/// <para>
/// Up and down step the value while the cell holds focus (they are the arrows drawn on it); left and right
/// are left to move between cells, so a pad walks the strip the way it reads.
/// </para>
/// </remarks>
public class OptionCycler : Control
{
    private readonly List<string> _options;
    private readonly List<string> _shown;
    private readonly List<Spring> _pips = [];
    private UiRect _up;
    private UiRect _down;
    private UiVec2 _pointer;
    private int _selected;

    /// <summary>Initializes a new instance of the <see cref="OptionCycler"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="label">The caption, shown in capitals.</param>
    /// <param name="options">The values, in order.</param>
    /// <param name="selected">The initial value's index.</param>
    public OptionCycler(UiTheme theme, string label, IEnumerable<string> options, int selected = 0)
        : base(theme)
    {
        ArgumentNullException.ThrowIfNull(options);
        Label = (label ?? string.Empty).ToUpperInvariant();
        _options = [.. options];
        _shown = _options.ConvertAll(o => o.ToUpperInvariant());
        _selected = _options.Count == 0 ? 0 : Math.Clamp(selected, 0, _options.Count - 1);
        for (int i = 0; i < _options.Count; i++)
        {
            _pips.Add(new Spring(i == _selected ? 1f : 0f, smoothTime: 0.08f));
        }
    }

    /// <summary>Raised when the value changes, with its new index.</summary>
    public event Action<int>? SelectionChanged;

    /// <summary>Gets the caption.</summary>
    public string Label { get; }

    /// <summary>Gets the values.</summary>
    public IReadOnlyList<string> Options => _options;

    /// <summary>Gets or sets the cell's content width, in design units, not counting its padding and arrows.</summary>
    public float ContentWidth { get; set; } = 150f;

    /// <summary>Gets or sets the selected value's index.</summary>
    public int SelectedIndex
    {
        get => _selected;
        set
        {
            if (_options.Count == 0)
            {
                return;
            }

            int clamped = Math.Clamp(value, 0, _options.Count - 1);
            if (clamped == _selected)
            {
                return;
            }

            _selected = clamped;
            SelectionChanged?.Invoke(_selected);
        }
    }

    /// <summary>Steps the value, wrapping at either end.</summary>
    /// <param name="delta">+1 for the next value, -1 for the previous.</param>
    public void Step(int delta)
    {
        if (_options.Count == 0 || delta == 0)
        {
            return;
        }

        SelectedIndex = ((_selected + Math.Sign(delta)) % _options.Count + _options.Count) % _options.Count;
    }

    /// <inheritdoc/>
    public override bool HandleNavigate(NavDirection direction)
    {
        switch (direction)
        {
            case NavDirection.Up:
                Step(1);
                return true;
            case NavDirection.Down:
                Step(-1);
                return true;
            default:
                return false;
        }
    }

    /// <inheritdoc/>
    public override void ActivateAt(UiVec2 point)
    {
        Step(_down.Contains(point) ? -1 : 1);
        RaiseActivated();
    }

    /// <inheritdoc/>
    public override void PointerMoved(UiVec2 point) => _pointer = point;

    /// <inheritdoc/>
    protected override void OnActivate() => Step(1);

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        base.OnUpdate(elapsedSeconds);
        for (int i = 0; i < _pips.Count; i++)
        {
            _pips[i].Target = i == _selected ? 1f : 0f;
            _pips[i].Update(elapsedSeconds);
        }
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) =>
        new(context.Px(26f + ContentWidth + 6f + 30f + 10f), available.Y);

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        float arrow = context.Px(30f);
        float x = contentRect.Right - context.Px(10f) - arrow;
        float mid = contentRect.Center.Y;
        _up = new UiRect(x, mid - arrow - context.Px(1f), arrow, arrow);
        _down = new UiRect(x, mid + context.Px(1f), arrow, arrow);
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        float appear = Visual.Appear;
        float hot = MathF.Max(Visual.Focus, IsPointerOver ? 1f : 0f);

        if (hot > 0.001f)
        {
            painter.FillRect(Bounds, UiPalette.White(0.03f * hot * appear));
        }

        painter.FillRect(new UiRect(Bounds.Right - context.Px(1f), Bounds.Y, context.Px(1f), Bounds.Height), UiPalette.White(0.06f * appear));

        float labelSize = context.Px(10f);
        float valueSize = context.Px(26f);
        float gap = context.Px(9f);
        float block = Theme.Mono.LineHeight(labelSize) + gap + valueSize + gap + context.Px(2f);
        float x = Bounds.X + context.Px(26f);
        float y = Bounds.Center.Y - (block * 0.5f);

        // Focus from a pad needs more than the faint wash the mock-up's hover has, so the caption warms too.
        Bgra32 label = Interpolate.Lerp(Theme.TextMuted, Theme.Accent, Visual.Focus);
        painter.DrawLabel(Theme.Mono, Label, new UiVec2(x, y), labelSize, UiPalette.Fade(label, appear), 0.3f);
        y += Theme.Mono.LineHeight(labelSize) + gap;

        if (_options.Count > 0)
        {
            var valueBox = new UiRect(x, y, context.Px(ContentWidth), valueSize);
            painter.DrawLabel(Theme.Heading, _shown[_selected], new UiVec2(x, UiRowStyle.CapsCenterY(Theme.Heading, valueSize, valueBox)), valueSize, UiPalette.Fade(Theme.TextBright, appear), 0.08f);
        }

        y += valueSize + gap;
        float px = x;
        for (int i = 0; i < _pips.Count; i++)
        {
            float t = _pips[i].Value;
            float w = context.Px(8f + (14f * t));
            painter.FillRect(new UiRect(px, y, w, context.Px(2f)), UiPalette.Fade(Interpolate.Lerp(UiPalette.White(0.18f), Theme.Accent, t), appear));
            px += w + context.Px(4f);
        }

        DrawArrow(painter, _up, UiDirection.Up, appear);
        DrawArrow(painter, _down, UiDirection.Down, appear);
    }

    private void DrawArrow(UiPainter painter, UiRect box, UiDirection direction, float appear)
    {
        bool hot = IsPointerOver && box.Contains(_pointer);
        Bgra32 color = hot ? Theme.Accent : Theme.TextDim;
        float size = painter.Context.Px(9f);
        Span<UiVec2> triangle = stackalloc UiVec2[3];
        UiPath.Chevron(box.Center, size, direction == UiDirection.Up ? -MathF.PI / 2f : MathF.PI / 2f, triangle);
        painter.FillPolygon(triangle, UiPalette.Fade(color, appear));
    }
}
