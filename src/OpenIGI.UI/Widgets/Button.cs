using OpenIGI.Core.Graphics;
using OpenIGI.Rendering;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Widgets;

/// <summary>A labelled button: a focus-highlighted row of text that activates on accept or a click.</summary>
/// <remarks>
/// The archetype the other controls follow. Focus is shown by a fill and a leading accent marker that
/// slide in with the state machine's <see cref="WidgetVisualStateMachine.Focus"/> amount, and the label
/// warms towards the accent colour; pressing nudges it. Nothing snaps, so focus flicking down a menu
/// reads as one moving highlight.
/// </remarks>
public class Button : Control
{
    /// <summary>Initializes a new instance of the <see cref="Button"/> class.</summary>
    /// <param name="theme">The theme to draw from.</param>
    /// <param name="text">The button's caption.</param>
    /// <exception cref="ArgumentNullException"><paramref name="theme"/> is <see langword="null"/>.</exception>
    public Button(UiTheme theme, string text)
        : base(theme)
    {
        Text = text ?? string.Empty;
    }

    /// <summary>Gets or sets the button's caption.</summary>
    public string Text { get; set; }

    /// <summary>Gets or sets the text size in design units.</summary>
    public float TextSize { get; set; }

    /// <summary>Gets or sets the padding around the caption, in design units.</summary>
    public Thickness Padding { get; set; } = new(24f, 10f);

    /// <summary>Gets or sets a value indicating whether the button is drawn as a persistently selected tab.</summary>
    /// <remarks>A selected button keeps a partial focus highlight and accent tint even without focus, so a
    /// tab strip built from buttons can show which one is current.</remarks>
    public bool Selected { get; set; }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        float sizePx = context.Px(TextSize > 0f ? TextSize : Theme.BodyTextSize);
        UiVec2 text = Theme.Body.Measure(Text, sizePx);
        Thickness padding = context.Px(Padding);
        return new UiVec2(text.X + padding.Horizontal, text.Y + padding.Vertical);
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        // A selected tab reads as focused-but-quieter even when focus is elsewhere.
        float focus = MathF.Max(Visual.Focus, Selected ? 0.55f : 0f);
        float press = Visual.Press;
        float appear = Visual.Appear;

        // Focus fill and a leading accent bar that grow with the focus amount.
        if (focus > 0.001f)
        {
            painter.FillRect(Bounds, Theme.FocusFill.WithAlpha((byte)(Theme.FocusFill.A * focus)), BlendMode.AlphaBlend);
            float barWidth = painter.Context.Px(3f);
            painter.FillRect(
                new UiRect(Bounds.X, Bounds.Y, barWidth, Bounds.Height),
                Theme.Accent.WithAlpha((byte)(255 * focus)),
                BlendMode.AlphaBlend);
        }

        float sizePx = painter.Context.Px(TextSize > 0f ? TextSize : Theme.BodyTextSize);
        Thickness padding = painter.Context.Px(Padding);
        Bgra32 baseColor = IsEnabled ? Interpolate.Lerp(Theme.Text, Theme.Accent, focus) : Theme.TextDisabled;
        byte alpha = (byte)(baseColor.A * appear);
        Bgra32 color = baseColor.WithAlpha(alpha);

        float textHeight = Theme.Body.LineHeight(sizePx);
        float pressNudge = painter.Context.Px(1.5f) * press;
        var origin = new UiVec2(
            Bounds.X + padding.Left,
            Bounds.Y + ((Bounds.Height - textHeight) * 0.5f) + pressNudge);
        painter.DrawTextShadowed(Theme.Body, Text, origin, sizePx, color, Theme.Shadow.WithAlpha((byte)(Theme.Shadow.A * appear)), new UiVec2(0f, painter.Context.Px(1.5f)));
    }
}
