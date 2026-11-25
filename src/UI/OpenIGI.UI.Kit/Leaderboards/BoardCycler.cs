using OpenIGI.Core.Graphics;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Leaderboards;

/// <summary>
/// A board's picker of one of many in a line: its name, then the value between two chevrons with a pip for each choice under
/// it, the picked one long. Left and right step it, as do the chevrons under a pointer.
/// </summary>
public sealed class BoardCycler : Control
{
    private readonly IReadOnlyList<string> _options;
    private UiRect _down;
    private UiRect _up;
    private UiVec2 _pointer;

    /// <summary>Initializes a new instance of the <see cref="BoardCycler"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="label">Its name, before it; empty for none.</param>
    /// <param name="options">The choices.</param>
    /// <param name="selected">The choice picked.</param>
    public BoardCycler(UiTheme theme, string label, IReadOnlyList<string> options, int selected)
        : base(theme)
    {
        Label = label ?? string.Empty;
        _options = options ?? throw new ArgumentNullException(nameof(options));
        SelectedIndex = Math.Clamp(selected, 0, Math.Max(0, options.Count - 1));
        VerticalAlignment = VerticalAlignment.Center;
    }

    /// <summary>Raised when another choice is picked.</summary>
    public event Action<int>? SelectionChanged;

    /// <summary>Gets its name.</summary>
    public string Label { get; }

    /// <summary>Gets or sets the choice picked.</summary>
    public int SelectedIndex { get; set; }

    /// <summary>Gets or sets the value's room between the chevrons, in design units.</summary>
    public float ValueWidth { get; set; } = 170f;

    /// <summary>Gets or sets the most pips drawn: more choices than this draw none.</summary>
    public int MaxPips { get; set; } = 16;

    /// <inheritdoc/>
    public override bool HandleNavigate(NavDirection direction)
    {
        switch (direction)
        {
            case NavDirection.Left:
                Step(-1);
                return true;
            case NavDirection.Right:
                Step(1);
                return true;
            default:
                return false;
        }
    }

    /// <inheritdoc/>
    public override void ActivateAt(UiVec2 point)
    {
        if (_down.Contains(point))
        {
            Step(-1);
        }
        else if (_up.Contains(point))
        {
            Step(1);
        }
        else
        {
            Step(1);
        }
    }

    /// <inheritdoc/>
    public override void PointerMoved(UiVec2 point) => _pointer = point;

    /// <inheritdoc/>
    protected override void OnActivate() => Step(1);

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        float label = Label.Length > 0 ? Theme.Body.MeasureWidth(Label, context.Px(15f)) + context.Px(12f) : 0f;
        return new UiVec2(label + context.Px(40f + ValueWidth + 40f), context.Px(40f));
    }

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        float label = Label.Length > 0 ? Theme.Body.MeasureWidth(Label, context.Px(15f)) + context.Px(12f) : 0f;
        float box = context.Px(40f);
        _down = new UiRect(contentRect.X + label, contentRect.Center.Y - (box / 2f), box, box);
        _up = new UiRect(_down.Right + context.Px(ValueWidth), _down.Y, box, box);
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        float appear = Visual.Appear * (IsEnabled ? 1f : 0.4f);
        UiRect bounds = Bounds;
        float focus = Visual.Focus;
        if (focus > 0.001f)
        {
            painter.FillRect(bounds, UiPalette.AccentAt(0.06f * focus * appear));
            painter.FillRect(new UiRect(bounds.X, bounds.Bottom - context.Px(1f), bounds.Width, context.Px(1f)), UiPalette.Fade(Theme.Accent, focus * appear));
        }

        if (Label.Length > 0)
        {
            float px = context.Px(15f);
            painter.DrawText(Theme.Body, Label, new UiVec2(bounds.X, UiRowStyle.CenterY(Theme.Body, px, bounds)), px, UiPalette.Fade(focus > 0.5f ? Theme.Accent : Theme.TextSecondary, appear));
        }

        Chevron(painter, _down, UiDirection.Left, appear);
        Chevron(painter, _up, UiDirection.Right, appear);
        if (_options.Count == 0)
        {
            return;
        }

        float valueSize = context.Px(16f);
        float centre = (_down.Right + _up.X) / 2f;
        string value = Theme.Heading.Fit(_options[SelectedIndex].ToUpperInvariant(), valueSize, context.Px(ValueWidth - 8f));
        float width = UiPainter.MeasureLabel(Theme.Heading, value, valueSize, 0.1f);
        float valueTop = bounds.Center.Y - (valueSize * 0.62f);
        painter.DrawLabel(Theme.Display, value, new UiVec2(centre - (width / 2f), valueTop), valueSize, UiPalette.Fade(Theme.TextBright, appear), 0.1f);
        if (_options.Count > MaxPips)
        {
            return;
        }

        float pip = context.Px(6f);
        float gap = context.Px(3f);
        float total = (_options.Count * pip) + context.Px(6f) + ((_options.Count - 1) * gap);
        float x = centre - (total / 2f);
        float y = valueTop + (valueSize * 1.05f) + context.Px(3f);
        for (int i = 0; i < _options.Count; i++)
        {
            bool picked = i == SelectedIndex;
            float w = picked ? pip * 2f : pip;
            painter.FillRect(new UiRect(x, y, w, context.Px(2f)), picked ? UiPalette.Fade(Theme.Accent, appear) : UiPalette.White(0.22f * appear));
            x += w + gap;
        }
    }

    private void Chevron(UiPainter painter, UiRect box, UiDirection direction, float appear)
    {
        bool hot = IsPointerOver && box.Contains(_pointer);
        painter.DrawChevron(box.Center, painter.Context.Px(9f), direction, painter.Context.Px(1.6f), UiPalette.Fade(hot ? Theme.Accent : Theme.TextSecondary, appear));
    }

    private void Step(int direction)
    {
        if (_options.Count == 0 || !IsEnabled)
        {
            return;
        }

        SelectedIndex = ((SelectedIndex + direction) % _options.Count + _options.Count) % _options.Count;
        SelectionChanged?.Invoke(SelectedIndex);
    }
}
