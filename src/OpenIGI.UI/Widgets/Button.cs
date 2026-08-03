using OpenIGI.Core.Graphics;
using OpenIGI.Rendering;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;

namespace OpenIGI.UI.Widgets;

/// <summary>How a button shows that it is the current one in a set.</summary>
public enum ButtonAccent
{
    /// <summary>A vertical accent bar down the leading edge — a row in a vertical menu.</summary>
    Bar,

    /// <summary>An accent underline across the bottom — a tab in a horizontal bar.</summary>
    Underline,

    /// <summary>A filled accent block with dark text — a primary call to action.</summary>
    Solid,
}

/// <summary>A labelled button: a focus-highlighted row of text that activates on accept or a click.</summary>
/// <remarks>
/// The archetype the other controls follow. Focus is shown by a fill and an accent marker that slide in
/// with the state machine's <see cref="WidgetVisualStateMachine.Focus"/> amount, and the label warms
/// towards the accent colour; pressing nudges it. Nothing snaps, so focus flicking down a menu reads as
/// one moving highlight. <see cref="Accent"/> picks which marker, because a bar down the left edge reads
/// as "this row" in a column and as noise in a horizontal nav bar, where an underline is what a tab wants.
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

    /// <summary>Gets or sets the font the caption is drawn with, or <see langword="null"/> for the theme's body font.</summary>
    public UiFont? Font { get; set; }

    /// <summary>Gets or sets the padding around the caption, in design units.</summary>
    public Thickness Padding { get; set; } = new(24f, 10f);

    /// <summary>Gets or sets a value indicating whether the button is drawn as a persistently selected tab.</summary>
    /// <remarks>A selected button keeps a partial focus highlight and accent tint even without focus, so a
    /// tab strip built from buttons can show which one is current.</remarks>
    public bool Selected { get; set; }

    /// <summary>Gets or sets which marker shows focus and selection.</summary>
    public ButtonAccent Accent { get; set; } = ButtonAccent.Bar;

    private UiFont Face => Font ?? Theme.Body;

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        float sizePx = context.Px(TextSize > 0f ? TextSize : Theme.BodyTextSize);
        UiVec2 text = Face.Measure(Text, sizePx);
        Thickness padding = context.Px(Padding);
        return new UiVec2(text.X + padding.Horizontal, text.Y + padding.Vertical);
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        // A selected tab reads as focused-but-quieter even when focus is elsewhere, and a hovered one
        // lights just enough for the pointer to see what it is about to hit.
        float focus = MathF.Max(Visual.Focus, MathF.Max(Selected ? 0.55f : 0f, IsPointerOver ? 0.4f : 0f));
        float press = Visual.Press;
        float appear = Visual.Appear;

        float sizePx = painter.Context.Px(TextSize > 0f ? TextSize : Theme.BodyTextSize);
        Thickness padding = painter.Context.Px(Padding);
        Bgra32 baseColor = IsEnabled ? Interpolate.Lerp(Theme.Text, Theme.Accent, focus) : Theme.TextDisabled;

        if (Accent == ButtonAccent.Solid)
        {
            // A primary action: the accent is the fill, and the caption is punched out of it dark.
            byte fill = (byte)(255 * appear);
            painter.FillRect(Bounds, Theme.Accent.WithAlpha((byte)(fill * (0.78f + (0.22f * focus)))), BlendMode.AlphaBlend);
            baseColor = Bgra32.FromRgb(14, 17, 20);
        }
        else if (focus > 0.001f)
        {
            painter.FillRect(Bounds, Theme.FocusFill.WithAlpha((byte)(Theme.FocusFill.A * focus)), BlendMode.AlphaBlend);
            float thickness = painter.Context.Px(3f);
            UiRect marker = Accent == ButtonAccent.Underline
                ? new UiRect(Bounds.X, Bounds.Bottom - thickness, Bounds.Width, thickness)
                : new UiRect(Bounds.X, Bounds.Y, thickness, Bounds.Height);
            painter.FillRect(marker, Theme.Accent.WithAlpha((byte)(255 * focus)), BlendMode.AlphaBlend);
        }

        byte alpha = (byte)(baseColor.A * appear);
        Bgra32 color = baseColor.WithAlpha(alpha);

        float textHeight = Face.LineHeight(sizePx);
        float pressNudge = painter.Context.Px(1.5f) * press;
        var origin = new UiVec2(
            Bounds.X + padding.Left,
            Bounds.Y + ((Bounds.Height - textHeight) * 0.5f) + pressNudge);
        painter.DrawTextShadowed(
            Face,
            Text,
            origin,
            sizePx,
            color,
            Theme.Shadow.WithAlpha((byte)(Theme.Shadow.A * appear)),
            new UiVec2(0f, painter.Context.Px(1.5f)));
    }
}
