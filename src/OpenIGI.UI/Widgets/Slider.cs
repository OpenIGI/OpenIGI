using OpenIGI.Core.Graphics;
using OpenIGI.Rendering;
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
    private readonly Spring _display;
    private float _value;
    private UiRect _track;

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

        float t = Math.Clamp((point.X - track.X) / track.Width, 0f, 1f);
        float raw = Minimum + (t * (Maximum - Minimum));
        Value = Step > 0f ? Minimum + (MathF.Round((raw - Minimum) / Step) * Step) : raw;
        RaiseActivated();
    }

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        // The track is resolved here rather than in the draw so a click can be mapped onto it whether or
        // not the widget has been drawn yet.
        float pad = context.Px(8f);
        float trackH = context.Px(6f);
        _track = new UiRect(
            contentRect.X + pad,
            contentRect.Bottom - pad - trackH,
            MathF.Max(0f, contentRect.Width - (pad * 2f)),
            trackH);
    }

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        base.OnUpdate(elapsedSeconds);
        _display.Update(elapsedSeconds);
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        float sizePx = context.Px(Theme.BodyTextSize);
        float height = Theme.Body.LineHeight(sizePx) + context.Px(20f);
        return new UiVec2(available.X, height);
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        float focus = MathF.Max(Visual.Focus, IsPointerOver ? 0.35f : 0f);
        if (focus > 0.001f)
        {
            painter.FillRect(Bounds, Theme.FocusFill.WithAlpha((byte)(Theme.FocusFill.A * focus)), BlendMode.AlphaBlend);
        }

        float sizePx = painter.Context.Px(Theme.BodyTextSize);
        Bgra32 textColor = IsEnabled ? Interpolate.Lerp(Theme.Text, Theme.Accent, focus) : Theme.TextDisabled;
        float pad = painter.Context.Px(8f);

        painter.DrawText(Theme.Body, Label, new UiVec2(Bounds.X + pad, Bounds.Y + pad), sizePx, textColor);

        string readout = Format?.Invoke(_value) ?? _value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        float readoutWidth = Theme.Body.MeasureWidth(readout, sizePx);
        painter.DrawText(Theme.Body, readout, new UiVec2(Bounds.Right - pad - readoutWidth, Bounds.Y + pad), sizePx, textColor);

        // The track sits below the caption row, where Arrange put it.
        UiRect track = _track;
        painter.FillRect(track, Theme.Border, BlendMode.AlphaBlend);

        float t = Math.Clamp(_display.Value, 0f, 1f);
        painter.FillRect(new UiRect(track.X, track.Y, track.Width * t, track.Height), Theme.Accent, BlendMode.AlphaBlend);

        float knob = painter.Context.Px(14f);
        float knobX = track.X + (track.Width * t) - (knob * 0.5f);
        painter.FillRect(new UiRect(knobX, track.Y + (track.Height * 0.5f) - (knob * 0.5f), knob, knob), Theme.Text, BlendMode.AlphaBlend);
    }
}
