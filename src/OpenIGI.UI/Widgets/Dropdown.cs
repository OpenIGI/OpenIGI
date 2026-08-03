using OpenIGI.Core.Graphics;
using OpenIGI.Rendering;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Widgets;

/// <summary>How a <see cref="Dropdown"/> arranges its caption and its value.</summary>
public enum DropdownStyle
{
    /// <summary>A full-width settings row: the caption on the left, the value between chevrons on the right.</summary>
    Row,

    /// <summary>A self-sized pill holding just the value between chevrons — a picker that sits in a bar.</summary>
    Compact,
}

/// <summary>A caption with a value chosen from a fixed list by cycling through it with left and right.</summary>
/// <remarks>
/// A cycling selector rather than a pop-up list, because that is the form a gamepad drives without a
/// pointer: left and right step through the options and wrap, and the current one is shown between two
/// chevrons. It is what a preset picker or a "Windowed / Borderless / Fullscreen" row is. A pointer gets
/// the same two steps by clicking the chevron it wants — the chevrons' rectangles are resolved at layout
/// time, so a click reads as "back one" or "on one" instead of always advancing.
/// </remarks>
public class Dropdown : Control
{
    private readonly List<string> _options;
    private int _selected;
    private UiRect _previousRect;
    private UiRect _nextRect;

    /// <summary>Initializes a new instance of the <see cref="Dropdown"/> class.</summary>
    /// <param name="theme">The theme to draw from.</param>
    /// <param name="label">The caption.</param>
    /// <param name="options">The options to choose between.</param>
    /// <param name="selected">The initially selected index.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public Dropdown(UiTheme theme, string label, IEnumerable<string> options, int selected = 0)
        : base(theme)
    {
        ArgumentNullException.ThrowIfNull(options);
        Label = label;
        _options = [.. options];
        _selected = _options.Count > 0 ? Math.Clamp(selected, 0, _options.Count - 1) : 0;
    }

    /// <summary>Raised when the selection changes, with the new index.</summary>
    public event Action<int>? SelectionChanged;

    /// <summary>Gets or sets the caption.</summary>
    public string Label { get; set; }

    /// <summary>Gets or sets how the caption and value are laid out.</summary>
    public DropdownStyle Style { get; set; } = DropdownStyle.Row;

    /// <summary>Gets or sets a predicate saying whether an option can be chosen.</summary>
    /// <remarks>
    /// A disabled option is drawn muted and is stepped over rather than landed on, which is how the game
    /// selector shows a title that is not installed without letting it be picked.
    /// </remarks>
    public Func<int, bool>? OptionEnabled { get; set; }

    /// <summary>Gets the options.</summary>
    public IReadOnlyList<string> Options => _options;

    /// <summary>Gets or sets the selected index.</summary>
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

    /// <summary>Gets the selected option's text, or the empty string when there are none.</summary>
    public string SelectedOption => _options.Count > 0 ? _options[_selected] : string.Empty;

    /// <summary>Steps the selection by a number of options, skipping any that are disabled.</summary>
    /// <param name="delta">How many options to move, negative for backwards.</param>
    public void Step(int delta)
    {
        if (_options.Count == 0 || delta == 0)
        {
            return;
        }

        int direction = Math.Sign(delta);
        int candidate = _selected;
        for (int tried = 0; tried < _options.Count; tried++)
        {
            candidate = ((candidate + direction) % _options.Count + _options.Count) % _options.Count;
            if (OptionEnabled is null || OptionEnabled(candidate))
            {
                if (candidate != _selected)
                {
                    _selected = candidate;
                    SelectionChanged?.Invoke(_selected);
                }

                return;
            }
        }
    }

    /// <inheritdoc/>
    public override bool HandleNavigate(NavDirection direction)
    {
        if (_options.Count == 0)
        {
            return false;
        }

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
        // The back chevron steps back; the value and the forward chevron — and anything else on the row —
        // step on, which is what an accept press does too.
        Step(_previousRect.Contains(point) ? -1 : 1);
        RaiseActivated();
    }

    /// <inheritdoc/>
    protected override void OnActivate() => Step(1);

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        float sizePx = context.Px(Theme.BodyTextSize);
        float height = Theme.Body.LineHeight(sizePx) + context.Px(Style == DropdownStyle.Compact ? 14f : 12f);
        if (Style != DropdownStyle.Compact)
        {
            return new UiVec2(available.X, height);
        }

        // A compact picker sizes to its widest option so the pill does not resize as the value changes.
        float widest = 0f;
        foreach (string option in _options)
        {
            widest = MathF.Max(widest, Theme.Body.MeasureWidth(option, sizePx));
        }

        float chevron = context.Px(16f);
        return new UiVec2(widest + (chevron * 2f) + context.Px(44f), height);
    }

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        float chevron = context.Px(18f);
        float pad = context.Px(8f);

        if (Style == DropdownStyle.Compact)
        {
            _previousRect = new UiRect(contentRect.X, contentRect.Y, chevron + pad, contentRect.Height);
            _nextRect = new UiRect(contentRect.Right - chevron - pad, contentRect.Y, chevron + pad, contentRect.Height);
            return;
        }

        float sizePx = context.Px(Theme.BodyTextSize);
        float valueWidth = Theme.Body.MeasureWidth(SelectedOption, sizePx);
        float right = contentRect.Right - pad;
        _nextRect = new UiRect(right - chevron, contentRect.Y, chevron + pad, contentRect.Height);
        _previousRect = new UiRect(
            right - chevron - context.Px(12f) - valueWidth - chevron,
            contentRect.Y,
            chevron + pad,
            contentRect.Height);
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        float focus = MathF.Max(Visual.Focus, IsPointerOver ? 0.35f : 0f);
        float sizePx = painter.Context.Px(Theme.BodyTextSize);
        Bgra32 textColor = IsEnabled ? Interpolate.Lerp(Theme.Text, Theme.Accent, focus) : Theme.TextDisabled;
        float pad = painter.Context.Px(8f);
        float textY = Bounds.Y + ((Bounds.Height - Theme.Body.LineHeight(sizePx)) * 0.5f);
        string value = SelectedOption;
        float valueWidth = Theme.Body.MeasureWidth(value, sizePx);
        float chevron = painter.Context.Px(18f);
        Bgra32 chevronColor = Theme.Accent.WithAlpha((byte)(255 * MathF.Max(0.35f, focus)));

        if (Style == DropdownStyle.Compact)
        {
            // A bordered pill: nothing but the value between two chevrons, which is what a picker sitting
            // in a bar should be. The caption belongs to whatever labels the bar, not to the control.
            painter.FillRect(Bounds, Theme.Panel.WithAlpha((byte)(120 + (80 * focus))), BlendMode.AlphaBlend);
            painter.StrokeRect(
                Bounds,
                painter.Context.Px(1f),
                Interpolate.Lerp(Theme.Border, Theme.Accent, focus),
                BlendMode.AlphaBlend);

            painter.DrawText(Theme.Body, "<", new UiVec2(Bounds.X + pad, textY), sizePx, chevronColor);
            painter.DrawText(Theme.Body, ">", new UiVec2(Bounds.Right - pad - chevron, textY), sizePx, chevronColor);
            painter.DrawText(
                Theme.Body,
                value,
                new UiVec2(Bounds.X + ((Bounds.Width - valueWidth) * 0.5f), textY),
                sizePx,
                textColor);
            return;
        }

        if (focus > 0.001f)
        {
            painter.FillRect(Bounds, Theme.FocusFill.WithAlpha((byte)(Theme.FocusFill.A * focus)));
        }

        painter.DrawText(Theme.Body, Label, new UiVec2(Bounds.X + pad, textY), sizePx, textColor);

        // The value sits at the right between two chevrons.
        float right = Bounds.Right - pad;
        painter.DrawText(Theme.Body, ">", new UiVec2(right - chevron, textY), sizePx, chevronColor);
        painter.DrawText(Theme.Body, value, new UiVec2(right - chevron - painter.Context.Px(6f) - valueWidth, textY), sizePx, textColor);
        painter.DrawText(Theme.Body, "<", new UiVec2(right - chevron - painter.Context.Px(12f) - valueWidth - chevron, textY), sizePx, chevronColor);
    }
}
