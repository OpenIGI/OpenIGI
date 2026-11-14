using OpenIGI.Core.Graphics;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;

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
        if (LargeRow)
        {
            // A row of the large look: an arrow a thumb can take at either end of a control of one width, so the
            // values of a column of rows stand under one another.
            float box = context.Px(44f);
            float control = MathF.Min(context.Px(Theme.BodyTextSize < 18f ? 196f : 248f), contentRect.Width * 0.55f);
            float edge = contentRect.Right - context.Px(Theme.RowInset) + context.Px(8f);
            float top = contentRect.Center.Y - (box * 0.5f);
            _previousRect = new UiRect(edge - control, top, box, box);
            _nextRect = new UiRect(edge - box, top, box, box);
            _valueRect = new UiRect(_previousRect.Right, contentRect.Y, MathF.Max(0f, _nextRect.X - _previousRect.Right), contentRect.Height);
            return;
        }

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

        if (_shownIndex != _selected || _shown is null)
        {
            _shownIndex = _selected;
            _shown = SelectedOption.ToUpperInvariant();
        }

        if (LargeRow)
        {
            DrawLargeRow(painter, _shown, appear);
            return;
        }

        DrawChevron(painter, _previousRect, UiDirection.Left, appear);
        DrawChevron(painter, _nextRect, UiDirection.Right, appear);

        float sizePx = context.Px(ValueSizeUnits);

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

    // Whether the control is a settings row of the large look, drawn as the design's stepper.
    private bool LargeRow => Theme.Large && Style != DropdownStyle.Compact;

    // The large look's row: the value centred between the arrows, and under it a pip for each choice, the one held lit.
    private void DrawLargeRow(UiPainter painter, string shown, float appear)
    {
        LayoutContext context = painter.Context;
        float centre = _valueRect.Center.X;
        float valueSize = context.Px(Theme.BodyTextSize < 18f ? 16f : 18f);
        float caps = valueSize * 0.72f;
        bool pips = _options.Count is > 1 and <= 8;
        float pipHeight = context.Px(3f);
        float block = caps + (pips ? context.Px(6f) + pipHeight : 0f);
        float top = _valueRect.Center.Y - (block * 0.5f);
        string value = TextFit.ToWidth(Theme.Heading, shown, valueSize, _valueRect.Width, 0.1f);
        float width = UiPainter.MeasureLabel(Theme.Heading, value, valueSize, 0.1f);
        var valueBox = new UiRect(centre - (width * 0.5f), top, width, caps);
        painter.DrawLabel(Theme.Heading, value, new UiVec2(valueBox.X, UiRowStyle.CapsCenterY(Theme.Heading, valueSize, valueBox)), valueSize, UiPalette.Fade(IsEnabled ? Theme.Text : Theme.TextDisabled, appear), 0.1f);
        if (pips)
        {
            float pip = context.Px(10f);
            float gap = context.Px(3f);
            float x = centre - (((_options.Count * pip) + ((_options.Count - 1) * gap)) * 0.5f);
            for (int i = 0; i < _options.Count; i++)
            {
                Bgra32 ink = i != _selected ? UiPalette.White(0.22f) : IsEnabled ? Theme.Accent : Theme.TextDisabled;
                painter.FillRect(new UiRect(x + (i * (pip + gap)), top + caps + context.Px(6f), pip, pipHeight), UiPalette.Fade(ink, appear));
            }
        }

        DrawLargeArrow(painter, _previousRect, -1f, appear);
        DrawLargeArrow(painter, _nextRect, 1f, appear);
    }

    private void DrawLargeArrow(UiPainter painter, UiRect box, float sign, float appear)
    {
        bool hot = IsEnabled && IsPointerOver && box.Contains(_pointer);
        float reach = painter.Context.Px(6f);
        float thick = painter.Context.Px(2f);
        UiVec2 c = box.Center;
        Bgra32 ink = UiPalette.Fade(!IsEnabled ? Theme.TextDisabled : hot ? Theme.Accent : Theme.TextSecondary, appear);
        var tip = new UiVec2(c.X + (sign * reach * 0.5f), c.Y);
        painter.StrokeLine(new UiVec2(tip.X - (sign * reach), c.Y - reach), tip, thick, ink);
        painter.StrokeLine(new UiVec2(tip.X - (sign * reach), c.Y + reach), tip, thick, ink);
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
