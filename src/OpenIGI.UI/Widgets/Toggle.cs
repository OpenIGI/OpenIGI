using OpenIGI.Core.Graphics;
using OpenIGI.Rendering;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Widgets;

/// <summary>A caption with an on/off switch, toggled by accept or by pressing left/right.</summary>
/// <remarks>
/// The knob slides between the two ends on the state machine's own timing, so the switch animates rather
/// than jumping. Left forces off and right forces on so a definite value can be dialled in, while accept
/// flips it — the behaviour a settings screen wants.
/// </remarks>
public class Toggle : Control
{
    private readonly Tween _knob = new(0f, duration: 0.12f, Ease.OutCubic);

    /// <summary>Initializes a new instance of the <see cref="Toggle"/> class.</summary>
    /// <param name="theme">The theme to draw from.</param>
    /// <param name="label">The caption.</param>
    /// <param name="value">The initial state.</param>
    /// <exception cref="ArgumentNullException"><paramref name="theme"/> is <see langword="null"/>.</exception>
    public Toggle(UiTheme theme, string label, bool value = false)
        : base(theme)
    {
        Label = label ?? string.Empty;
        _value = value;
        _knob.SnapTo(value ? 1f : 0f);
    }

    /// <summary>Raised when the value changes.</summary>
    public event Action<bool>? ValueChanged;

    /// <summary>Gets or sets the caption.</summary>
    public string Label { get; set; }

    /// <summary>Gets or sets whether the switch is on.</summary>
    public bool Value
    {
        get => _value;
        set
        {
            if (_value == value)
            {
                return;
            }

            _value = value;
            _knob.Retarget(value ? 1f : 0f);
            ValueChanged?.Invoke(value);
        }
    }

    private bool _value;

    /// <inheritdoc/>
    public override bool HandleNavigate(NavDirection direction)
    {
        switch (direction)
        {
            case NavDirection.Left when Value:
                Value = false;
                return true;
            case NavDirection.Right when !Value:
                Value = true;
                return true;
            default:
                return false;
        }
    }

    /// <inheritdoc/>
    protected override void OnActivate() => Value = !Value;

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        base.OnUpdate(elapsedSeconds);
        _knob.Update(elapsedSeconds);
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        float sizePx = context.Px(Theme.BodyTextSize);
        float height = Theme.Body.LineHeight(sizePx) + context.Px(12f);
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
        Bgra32 text = IsEnabled ? Interpolate.Lerp(Theme.Text, Theme.Accent, focus) : Theme.TextDisabled;
        float textY = Bounds.Y + ((Bounds.Height - Theme.Body.LineHeight(sizePx)) * 0.5f);
        painter.DrawText(Theme.Body, Label, new UiVec2(Bounds.X + painter.Context.Px(8f), textY), sizePx, text);

        // The switch on the right: a track with a knob sliding between the ends.
        float trackW = painter.Context.Px(52f);
        float trackH = painter.Context.Px(24f);
        float trackX = Bounds.Right - trackW - painter.Context.Px(8f);
        float trackY = Bounds.Y + ((Bounds.Height - trackH) * 0.5f);
        float t = _knob.Value;
        Bgra32 trackColor = Interpolate.Lerp(Theme.Border, Theme.Accent, t);
        painter.FillRect(new UiRect(trackX, trackY, trackW, trackH), trackColor, BlendMode.AlphaBlend);

        float knob = trackH - painter.Context.Px(6f);
        float knobX = trackX + painter.Context.Px(3f) + (t * (trackW - knob - painter.Context.Px(6f)));
        painter.FillRect(new UiRect(knobX, trackY + painter.Context.Px(3f), knob, knob), Theme.Text, BlendMode.AlphaBlend);
    }
}
