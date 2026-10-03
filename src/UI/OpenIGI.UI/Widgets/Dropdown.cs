using OpenIGI.Core.Graphics;
using OpenIGI.UI.Drawing;
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
    private const float ChevronBoxUnits = 30f;
    private const float ValueSizeUnits = 19f;
    private const float ValueMinUnits = 110f;
    private const float ValueTracking = 0.12f;

    private UiRect _previousRect;
    private UiRect _nextRect;
    private UiRect _valueRect;
    private UiVec2 _pointer;
    private float _valueWidth;
    private float _valueWidthScale = -1f;
    private string? _shown;
    private int _shownIndex = -1;

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
        if (Style != DropdownStyle.Compact)
        {
            return UiRowStyle.Measure(this, context, available);
        }

        // A compact picker sizes to its widest option so it does not resize as the value changes.
        float chevron = context.Px(ChevronBoxUnits);
        return new UiVec2(ValueWidth(context) + (chevron * 2f) + context.Px(16f), context.Px(44f));
    }

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        float chevron = context.Px(ChevronBoxUnits);
        float valueWidth = ValueWidth(context);
        float right = Style == DropdownStyle.Compact ? contentRect.Right - context.Px(8f) : contentRect.Right - context.Px(UiRowStyle.Inset);
        _nextRect = new UiRect(right - chevron, contentRect.Y, chevron, contentRect.Height);
        _valueRect = new UiRect(_nextRect.X - valueWidth, contentRect.Y, valueWidth, contentRect.Height);
        _previousRect = new UiRect(_valueRect.X - chevron, contentRect.Y, chevron, contentRect.Height);
    }

    /// <inheritdoc/>
    public override void PointerMoved(UiVec2 point) => _pointer = point;

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        float highlight = UiRowStyle.Highlight(this);
        float appear = Visual.Appear;
        LayoutContext context = painter.Context;

        if (Style == DropdownStyle.Compact)
        {
            // A glass cell: nothing but the value between two chevrons, which is what a picker sitting in a
            // bar should be. The caption belongs to whatever labels the bar, not to the control.
            painter.FillRect(Bounds, UiPalette.Fade(Theme.Panel, appear));
            painter.StrokeRect(Bounds, context.Px(1f), UiPalette.Fade(Interpolate.Lerp(Theme.Border, UiPalette.AccentAt(0.45f), highlight), appear));
        }
        else
        {
            UiRowStyle.DrawFrame(painter, this, Label, highlight, _previousRect.X);
        }

        DrawChevron(painter, _previousRect, UiDirection.Left, appear);
        DrawChevron(painter, _nextRect, UiDirection.Right, appear);

        float sizePx = context.Px(ValueSizeUnits);
        if (_shownIndex != _selected || _shown is null)
        {
            _shownIndex = _selected;
            _shown = SelectedOption.ToUpperInvariant();
        }

        string value = _shown;
        float width = UiPainter.MeasureLabel(Theme.Heading, value, sizePx, ValueTracking);
        Bgra32 color = IsEnabled ? Theme.TextBright : Theme.TextDisabled;
        painter.DrawLabel(
            Theme.Heading,
            value,
            new UiVec2(_valueRect.X + ((_valueRect.Width - width) * 0.5f), UiRowStyle.CapsCenterY(Theme.Heading, sizePx, _valueRect)),
            sizePx,
            UiPalette.Fade(color, appear),
            ValueTracking);
    }

    private void DrawChevron(UiPainter painter, UiRect box, UiDirection direction, float appear)
    {
        bool hot = IsEnabled && IsPointerOver && box.Contains(_pointer);
        Bgra32 color = !IsEnabled ? Theme.TextDisabled
            : hot || Visual.Focus > 0.5f ? Theme.Accent
            : Theme.TextMuted;
        float size = painter.Context.Px(12f);
        painter.DrawChevron(box.Center, size, direction, painter.Context.Px(1.6f), UiPalette.Fade(color, appear));
    }

    private float ValueWidth(LayoutContext context)
    {
        if (_valueWidthScale == context.Scale)
        {
            return _valueWidth;
        }

        float sizePx = context.Px(ValueSizeUnits);
        float widest = context.Px(ValueMinUnits);
        foreach (string option in _options)
        {
            widest = MathF.Max(widest, UiPainter.MeasureLabel(Theme.Heading, option.ToUpperInvariant(), sizePx, ValueTracking) + context.Px(8f));
        }

        _valueWidthScale = context.Scale;
        _valueWidth = widest;
        return widest;
    }
}
