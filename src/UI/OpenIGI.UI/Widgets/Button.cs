using OpenIGI.Core.Graphics;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;

namespace OpenIGI.UI.Widgets;

/// <summary>Which of the design's button styles a <see cref="Button"/> is drawn in.</summary>
public enum ButtonAccent
{
    /// <summary>A row in a vertical menu: a faint wash and a 3-unit accent bar down the leading edge when current.</summary>
    Bar,

    /// <summary>A tab in a header bar: muted text that lights white, with a 2-unit accent underline when selected.</summary>
    Underline,

    /// <summary>A primary call to action: an amber block with the caption punched out of it in near-black.</summary>
    Solid,

    /// <summary>A secondary action: a hairline outline that turns amber under the pointer.</summary>
    Ghost,

    /// <summary>An action that destroys something: a red outline and caption, washed red under the pointer.</summary>
    Destructive,

    /// <summary>A quiet action: the caption alone, muted until the pointer is on it.</summary>
    Text,

    /// <summary>A link: the caption alone in the accent, lightening under the pointer.</summary>
    Link,
}

/// <summary>A labelled button: an uppercase, letterspaced caption that activates on accept or a click.</summary>
/// <remarks>
/// <para>
/// The archetype the other controls follow, in the design's six styles (<see cref="ButtonAccent"/>). The
/// caption is set in the theme's condensed face, uppercase and tracked, unless a screen asks otherwise;
/// a primary button can lose a corner to a 45° cut (<see cref="CutCorner"/>).
/// </para>
/// <para>
/// The design defines only a hover state; keyboard and pad focus look exactly like it, eased with the state
/// machine's <see cref="WidgetVisualStateMachine.Focus"/> so focus flicking down a menu reads as one moving
/// highlight. Pressing nudges the caption down a pixel.
/// </para>
/// </remarks>
public class Button : Control
{
    private string? _shownSource;
    private string _shown = string.Empty;

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

    /// <summary>Gets or sets the text size in design units; zero for the theme's label size.</summary>
    public float TextSize { get; set; }

    /// <summary>Gets or sets the font the caption is drawn with, or <see langword="null"/> for the theme's condensed face.</summary>
    public UiFont? Font { get; set; }

    /// <summary>Gets or sets the padding around the caption, in design units.</summary>
    public Thickness Padding { get; set; } = new(24f, 12f);

    /// <summary>Gets or sets a value indicating whether the button is drawn as the current one of a set.</summary>
    public bool Selected { get; set; }

    /// <summary>Gets or sets which of the design's styles the button is drawn in.</summary>
    public ButtonAccent Accent { get; set; } = ButtonAccent.Bar;

    /// <summary>Gets a value indicating whether an <see cref="ButtonAccent.Underline"/> button draws its own underline when selected.</summary>
    /// <remarks>Off where the bar it sits in draws one underline that moves between its tabs.</remarks>
    protected virtual bool DrawsSelectedUnderline => true;

    /// <summary>Gets or sets a value indicating whether the caption is shown in capitals.</summary>
    public bool Uppercase { get; set; } = true;

    /// <summary>Gets or sets the caption's letterspacing in ems, or a negative value for the theme's label tracking.</summary>
    public float Tracking { get; set; } = -1f;

    /// <summary>Gets or sets the length cut from the bottom-right corner at 45°, in design units; zero for square.</summary>
    public float CutCorner { get; set; }

    /// <summary>Gets the font the caption is drawn with.</summary>
    protected UiFont Face => Font ?? Theme.Heading;

    /// <summary>Gets the caption's size in design units.</summary>
    protected float CaptionSize => TextSize > 0f ? TextSize : Theme.LabelTextSize;

    /// <summary>Gets the caption's letterspacing in ems.</summary>
    protected float CaptionTracking => Tracking >= 0f ? Tracking : Theme.LabelTracking;

    /// <summary>Gets how lit the button is: focus, the pointer, or being the current one of a set.</summary>
    protected float Highlight => MathF.Max(Visual.Focus, IsPointerOver ? 1f : 0f);

    /// <summary>Gets the caption as it is shown, in capitals when <see cref="Uppercase"/> is set.</summary>
    protected string ShownText
    {
        get
        {
            if (!ReferenceEquals(_shownSource, Text))
            {
                _shownSource = Text;
                _shown = Uppercase ? Text.ToUpperInvariant() : Text;
            }

            return _shown;
        }
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        float sizePx = context.Px(CaptionSize);
        float width = UiPainter.MeasureLabel(Face, ShownText, sizePx, CaptionTracking);
        Thickness padding = context.Px(Padding);
        return new UiVec2(width + padding.Horizontal, Face.LineHeight(sizePx) + padding.Vertical);
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        float hot = Highlight;
        float appear = Visual.Appear;
        LayoutContext context = painter.Context;
        float cut = context.Px(CutCorner);
        UiRect bounds = Bounds;
        Bgra32 caption;

        switch (Accent)
        {
            case ButtonAccent.Solid:
            {
                Bgra32 fill = !IsEnabled ? UiPalette.White(0.12f) : Interpolate.Lerp(Theme.Accent, Theme.AccentHover, hot);
                painter.FillCutRect(bounds, cut, UiCorners.BottomRight, UiPalette.Fade(fill, appear));
                caption = IsEnabled ? Theme.OnAccent : Theme.TextDisabled;
                break;
            }

            case ButtonAccent.Ghost:
            {
                Bgra32 border = !IsEnabled ? UiPalette.White(0.12f) : Interpolate.Lerp(UiPalette.GhostBorder, Theme.Accent, hot);
                painter.StrokeCutRect(bounds, cut, UiCorners.BottomRight, context.Px(1f), UiPalette.Fade(border, appear));
                caption = !IsEnabled ? Theme.TextDim : Interpolate.Lerp(Theme.Text, Theme.Accent, hot);
                break;
            }

            case ButtonAccent.Destructive:
            {
                if (IsEnabled && hot > 0.001f)
                {
                    painter.FillCutRect(bounds, cut, UiCorners.BottomRight, UiPalette.Fade(UiPalette.DestructiveWash, hot * appear));
                }

                painter.StrokeCutRect(bounds, cut, UiCorners.BottomRight, context.Px(1f), UiPalette.Fade(IsEnabled ? Theme.DestructiveBorder : UiPalette.White(0.12f), appear));
                caption = IsEnabled ? Theme.Destructive : Theme.TextDim;
                break;
            }

            case ButtonAccent.Text:
                caption = !IsEnabled ? Theme.TextDisabled : Interpolate.Lerp(Theme.TextMuted, Theme.TextBright, hot);
                break;

            case ButtonAccent.Link:
                caption = !IsEnabled ? Theme.TextDisabled : Interpolate.Lerp(Theme.Accent, UiPalette.AccentLight, hot);
                break;

            case ButtonAccent.Underline:
            {
                float lit = MathF.Max(hot, Selected ? 1f : 0f);
                caption = !IsEnabled ? Theme.TextDisabled : Interpolate.Lerp(Theme.TextMuted, Theme.TextBright, lit);
                if (Selected && DrawsSelectedUnderline)
                {
                    float inset = context.Px(Padding.Left);
                    float thickness = context.Px(2f);
                    painter.FillRect(
                        new UiRect(bounds.X + inset, bounds.Bottom - thickness, MathF.Max(0f, bounds.Width - (inset * 2f)), thickness),
                        UiPalette.Fade(Theme.Accent, appear));
                }

                break;
            }

            default:
            {
                float lit = MathF.Max(hot, Selected ? 1f : 0f);
                if (lit > 0.001f)
                {
                    painter.FillRect(bounds, UiPalette.Fade(Selected ? Theme.Selection : Theme.FocusFill, lit * appear));
                    painter.FillRect(
                        new UiRect(bounds.X, bounds.Y, context.Px(3f), bounds.Height),
                        UiPalette.Fade(Theme.Accent, lit * appear));
                }

                caption = !IsEnabled ? Theme.TextDisabled : Interpolate.Lerp(Theme.TextSecondary, Theme.TextBright, lit);
                break;
            }
        }

        DrawCaption(painter, caption, appear);
    }

    /// <summary>Draws the caption where the padding puts it, nudged down while pressed.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="color">The caption colour.</param>
    /// <param name="appear">The entrance opacity.</param>
    protected void DrawCaption(UiPainter painter, Bgra32 color, float appear)
    {
        ArgumentNullException.ThrowIfNull(painter);
        LayoutContext context = painter.Context;
        float sizePx = context.Px(CaptionSize);
        Thickness padding = context.Px(Padding);
        float width = UiPainter.MeasureLabel(Face, ShownText, sizePx, CaptionTracking);
        float x = Accent is ButtonAccent.Bar or ButtonAccent.Link
            ? Bounds.X + padding.Left
            : Bounds.X + ((Bounds.Width - width) * 0.5f);
        float y = UiRowStyle.CapsCenterY(Face, sizePx, Bounds) + (context.Px(1f) * Visual.Press);
        painter.DrawLabel(Face, ShownText, new UiVec2(x, y), sizePx, UiPalette.Fade(color, appear), CaptionTracking);
    }
}
