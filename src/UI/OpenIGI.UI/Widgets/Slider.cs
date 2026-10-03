using OpenIGI.Core.Graphics;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Widgets;

/// <summary>A caption with a value dragged between a minimum and a maximum by pressing left and right.</summary>
/// <remarks>
/// The value steps by <see cref="Step"/> on each left/right press, which the control consumes so focus
/// stays put while the player scrubs it. A spring chases the value so the fill and knob glide rather than
/// jump when the value is nudged repeatedly. This is what a graphics or audio slider is built on, each
/// row bound to a CVar in the settings screen.
/// </remarks>
public class Slider : Control
{
    // The design's metrics, in design units: a 180-unit track box, a 3-unit rail and fill, a 4×16 thumb,
    // and a monospaced readout at least 44 units wide, 14 units off the track.
    private const float TrackUnits = 180f;
    private const float RailUnits = 3f;
    private const float ThumbWidthUnits = 4f;
    private const float ThumbHeightUnits = 16f;
    private const float ReadoutGapUnits = 14f;
    private const float ReadoutMinUnits = 44f;
    private const float ReadoutSizeUnits = 14f;

    private readonly Spring _display;
    private float _value;
    private UiRect _track;
    private string? _readout;
    private float _readoutValue;
    private float _readoutWidth;
    private float _readoutWidthScale = -1f;
    private float _readoutWidthValue;

    /// <summary>Initializes a new instance of the <see cref="Slider"/> class.</summary>
    /// <param name="theme">The theme to draw from.</param>
    /// <param name="label">The caption.</param>
    /// <param name="minimum">The smallest value.</param>
    /// <param name="maximum">The largest value.</param>
    /// <param name="value">The initial value.</param>
    /// <param name="step">How far one left/right press moves the value.</param>
    /// <exception cref="ArgumentNullException"><paramref name="theme"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maximum"/> is not greater than <paramref name="minimum"/>.</exception>
    public Slider(UiTheme theme, string label, float minimum, float maximum, float value, float step)
        : base(theme)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(minimum, maximum);
        Label = label ?? string.Empty;
        Minimum = minimum;
        Maximum = maximum;
        Step = step;
        _value = Math.Clamp(value, minimum, maximum);
        _display = new Spring(Normalised, smoothTime: 0.08f);
    }

    /// <summary>Raised when the value changes.</summary>
    public event Action<float>? ValueChanged;

    /// <summary>Gets or sets the caption.</summary>
    public string Label { get; set; }

    /// <summary>Gets the smallest value.</summary>
    public float Minimum { get; }

    /// <summary>Gets the largest value.</summary>
    public float Maximum { get; }

    /// <summary>Gets or sets how far one press moves the value.</summary>
    public float Step { get; set; }

    /// <summary>Gets or sets a formatter for the value readout, or <see langword="null"/> for the plain number.</summary>
    public Func<float, string>? Format { get; set; }

    /// <summary>Gets or sets the current value, clamped to the range.</summary>
    public float Value
    {
        get => _value;
        set
        {
            float clamped = Math.Clamp(value, Minimum, Maximum);
            if (clamped == _value)
            {
                return;
            }

            _value = clamped;
            _display.Target = Normalised;
            ValueChanged?.Invoke(_value);
        }
    }

    private float Normalised => (_value - Minimum) / (Maximum - Minimum);

    /// <inheritdoc/>
    public override bool HandleNavigate(NavDirection direction)
    {
        switch (direction)
        {
            case NavDirection.Left:
                Value -= Step;
                return true;
            case NavDirection.Right:
                Value += Step;
                return true;
            default:
                return false;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A click lands the value where the pointer is along the track, snapped to <see cref="Step"/>, which
    /// is the only sensible reading of clicking a slider — activating it as a whole would mean nothing.
    /// </remarks>
    public override void ActivateAt(UiVec2 point)
    {
        UiRect track = _track;
        if (track.Width <= 0f)
        {
            return;
        }

        // Only a click on the track moves the value; one on the caption just focuses the row.
        float slack = track.Height;
        if (point.X < track.X - slack || point.X > track.Right + slack)
        {
            return;
        }

        float t = Math.Clamp((point.X - track.X) / track.Width, 0f, 1f);
        float raw = Minimum + (t * (Maximum - Minimum));
        Value = Step > 0f ? Minimum + (MathF.Round((raw - Minimum) / Step) * Step) : raw;
        RaiseActivated();
    }

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        // The track is resolved here rather than in the draw so a click can be mapped onto it whether or
        // not the widget has been drawn yet. It sits at the right of the row, left of the readout.
        float readout = ReadoutWidth(context);
        float width = context.Px(TrackUnits);
        float right = contentRect.Right - context.Px(UiRowStyle.Inset) - readout - context.Px(ReadoutGapUnits);
        float height = context.Px(ThumbHeightUnits);
        _track = new UiRect(right - width, contentRect.Y + ((contentRect.Height - height) * 0.5f), width, height);
    }

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        base.OnUpdate(elapsedSeconds);
        _display.Update(elapsedSeconds);
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) =>
        UiRowStyle.Measure(this, context, available);

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        float highlight = UiRowStyle.Highlight(this);
        UiRowStyle.DrawFrame(painter, this, Label, highlight, _track.X);

        LayoutContext context = painter.Context;
        float appear = Visual.Appear;
        UiRect track = _track;
        float rail = context.Px(RailUnits);
        float railY = track.Y + ((track.Height - rail) * 0.5f);
        float t = Math.Clamp(_display.Value, 0f, 1f);
        Bgra32 fill = IsEnabled ? Theme.Accent : Theme.TextDisabled;

        painter.FillRect(new UiRect(track.X, railY, track.Width, rail), UiPalette.Fade(Theme.Track, appear));
        painter.FillRect(new UiRect(track.X, railY, track.Width * t, rail), UiPalette.Fade(fill, appear));

        float thumbW = context.Px(ThumbWidthUnits);
        float thumbX = track.X + (track.Width * t) - (thumbW * 0.5f);
        Bgra32 thumb = IsEnabled ? Interpolate.Lerp(Theme.TextBright, Theme.AccentHover, highlight * 0.35f) : Theme.TextDisabled;
        painter.FillRect(new UiRect(thumbX, track.Y, thumbW, track.Height), UiPalette.Fade(thumb, appear));

        float sizePx = context.Px(ReadoutSizeUnits);
        string readout = Readout();
        float readoutWidth = Theme.Mono.MeasureWidth(readout, sizePx);
        float right = Bounds.Right - context.Px(UiRowStyle.Inset);
        var box = new UiRect(right - ReadoutWidth(context), Bounds.Y, ReadoutWidth(context), Bounds.Height);
        painter.DrawText(
            Theme.Mono,
            readout,
            new UiVec2(right - readoutWidth, UiRowStyle.CenterY(Theme.Mono, sizePx, box)),
            sizePx,
            UiPalette.Fade(IsEnabled ? Theme.Text : Theme.TextDisabled, appear));
    }

    // Formatted once per value rather than once per frame: the readout is drawn every frame the menu is
    // up, and a formatter that builds a sentence would otherwise allocate it each time.
    private string Readout()
    {
        if (_readout is null || _readoutValue != _value)
        {
            _readoutValue = _value;
            _readout = Format?.Invoke(_value) ?? _value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        }

        return _readout;
    }

    // Wide enough for either end of the range as well as the current value, so the track does not shift
    // sideways as a formatted readout grows a digit.
    private float ReadoutWidth(LayoutContext context)
    {
        if (_readoutWidthScale == context.Scale && _readoutWidthValue == _value)
        {
            return _readoutWidth;
        }

        float size = context.Px(ReadoutSizeUnits);
        float widest = MathF.Max(Theme.Mono.MeasureWidth(Readout(), size), Theme.Mono.MeasureWidth(Format(Minimum), size));
        widest = MathF.Max(widest, Theme.Mono.MeasureWidth(Format(Maximum), size));

        _readoutWidthScale = context.Scale;
        _readoutWidthValue = _value;
        _readoutWidth = MathF.Max(context.Px(ReadoutMinUnits), widest);
        return _readoutWidth;

        string Format(float value) =>
            this.Format?.Invoke(value) ?? value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }
}
