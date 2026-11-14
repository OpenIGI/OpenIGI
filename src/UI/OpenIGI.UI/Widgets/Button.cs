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

    /// <summary>A secondary action that should still draw the eye: an amber outline and caption, washed amber under the pointer.</summary>
    Outline,

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

    /// <summary>
    /// Gets or sets a key's name drawn in a small cap after the caption, in the caption's colour -- the key that does what the
    /// button does. Empty for none.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Gets or sets what draws a mark before the caption -- an icon -- or <see langword="null"/> for none.</summary>
    /// <remarks>Given the painter, the mark's centre, its size in pixels and the caption's colour.</remarks>
    public Action<UiPainter, UiVec2, float, Bgra32>? LeadingGlyph { get; set; }

    /// <summary>Gets or sets what draws a mark after the caption, before its key, or <see langword="null"/> for none.</summary>
    public Action<UiPainter, UiVec2, float, Bgra32>? TrailingGlyph { get; set; }

    /// <summary>Gets or sets the size of the marks beside the caption, in design units.</summary>
    public float GlyphSize { get; set; } = 21f;

    /// <summary>Gets or sets a second, small monospaced line under the caption. Empty for none.</summary>
    public string SubCaption { get; set; } = string.Empty;

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
        float width = UiPainter.MeasureLabel(Face, ShownText, sizePx, CaptionTracking) + KeyWidth(context) + GlyphsWidth(context);
        float height = Face.LineHeight(sizePx);
        if (SubCaption.Length > 0)
        {
            float sub = context.Px(SubtitleSize);
            width = MathF.Max(width, UiPainter.MeasureLabel(Theme.Mono, SubCaption, sub, 0.04f));
            height = (sizePx * 0.75f) + context.Px(6f) + Theme.Mono.LineHeight(sub);
        }

        Thickness padding = context.Px(Padding);
        return new UiVec2(width + padding.Horizontal, height + padding.Vertical);
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
                Bgra32 border = !IsEnabled ? UiPalette.White(0.12f) : Interpolate.Lerp(Theme.Large ? UiPalette.White(0.42f) : UiPalette.GhostBorder, Theme.Accent, hot);
                if (Theme.Large)
                {
                    // The large look's secondary button stands on a dark wash, so it reads over a picture.
                    painter.FillCutRect(bounds, cut, UiCorners.BottomRight, UiPalette.Fade(Bgra32.FromRgba(10, 12, 13, 89), appear));
                }

                painter.StrokeCutRect(bounds, cut, UiCorners.BottomRight, context.Px(1f), UiPalette.Fade(border, appear));
                caption = !IsEnabled ? Theme.TextDim : Interpolate.Lerp(Theme.Text, Theme.Accent, hot);
                break;
            }

            case ButtonAccent.Outline:
            {
                if (IsEnabled && hot > 0.001f)
                {
                    painter.FillCutRect(bounds, cut, UiCorners.BottomRight, UiPalette.Fade(Theme.Accent, 0.1f * hot * appear));
                }

                painter.StrokeCutRect(bounds, cut, UiCorners.BottomRight, context.Px(1f), UiPalette.Fade(IsEnabled ? Theme.Accent : UiPalette.White(0.12f), appear));
                caption = !IsEnabled ? Theme.TextDim : Interpolate.Lerp(Theme.Accent, UiPalette.AccentLight, hot);
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
                caption = !IsEnabled ? Theme.TextDisabled : Interpolate.Lerp(Theme.Large ? Theme.TextSecondary : Theme.TextMuted, Theme.TextBright, hot);
                break;

            case ButtonAccent.Link:
                caption = !IsEnabled ? Theme.TextDisabled : Interpolate.Lerp(Theme.Accent, UiPalette.AccentLight, hot);
                break;

            case ButtonAccent.Underline:
            {
                float lit = MathF.Max(hot, Selected ? 1f : 0f);
                caption = !IsEnabled ? Theme.TextDisabled : Interpolate.Lerp(Theme.Large ? Theme.TextSecondary : Theme.TextMuted, Theme.TextBright, lit);
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
        float press = context.Px(1f) * Visual.Press;
        if (SubCaption.Length > 0)
        {
            // Two lines, left-aligned in the padding: the caption, and the subtitle small and muted under it.
            float sub = context.Px(SubtitleSize);
            float block = (sizePx * 0.75f) + context.Px(6f) + Theme.Mono.LineHeight(sub);
            float top = Bounds.Y + ((Bounds.Height - block) * 0.5f) + press;
            var capsBox = new UiRect(Bounds.X, top, Bounds.Width, sizePx * 0.75f);
            painter.DrawLabel(Face, ShownText, new UiVec2(Bounds.X + padding.Left, UiRowStyle.CapsCenterY(Face, sizePx, capsBox)), sizePx, UiPalette.Fade(color, appear), CaptionTracking);
            painter.DrawLabel(Theme.Mono, SubCaption, new UiVec2(Bounds.X + padding.Left, top + (sizePx * 0.75f) + context.Px(6f)), sub, UiPalette.Fade(Theme.TextMuted, appear), 0.04f);
            return;
        }

        float textWidth = ShownText.Length == 0 ? 0f : UiPainter.MeasureLabel(Face, ShownText, sizePx, CaptionTracking);
        float width = textWidth + KeyWidth(context) + GlyphsWidth(context);
        float x = Accent is ButtonAccent.Bar or ButtonAccent.Link
            ? Bounds.X + padding.Left
            : Bounds.X + ((Bounds.Width - width) * 0.5f);
        float y = UiRowStyle.CapsCenterY(Face, sizePx, Bounds) + press;
        Bgra32 ink = UiPalette.Fade(color, appear);
        float glyph = context.Px(GlyphSize);
        float gap = context.Px(GlyphGap);
        if (LeadingGlyph is { } leading)
        {
            leading(painter, new UiVec2(x + (glyph * 0.5f), Bounds.Center.Y + press), glyph, ink);
            x += glyph + (textWidth > 0f ? gap : 0f);
        }

        UiVec2 end = textWidth > 0f ? painter.DrawLabel(Face, ShownText, new UiVec2(x, y), sizePx, ink, CaptionTracking) : new UiVec2(x, y);
        float after = end.X;
        if (TrailingGlyph is { } trailing)
        {
            after += textWidth > 0f || LeadingGlyph is not null ? gap : 0f;
            trailing(painter, new UiVec2(after + (glyph * 0.5f), Bounds.Center.Y + press), glyph, ink);
            after += glyph;
        }

        if (Key.Length == 0)
        {
            return;
        }

        float keyPx = context.Px(KeySize);
        float keyGap = HasLead ? context.Px(KeyGap) : 0f;
        if (Theme.Large)
        {
            // A key as the large look draws it everywhere: a faint fill, a border, a heavier foot.
            bool onAccent = Accent == ButtonAccent.Solid;
            Bgra32 edge = UiPalette.Fade(onAccent ? UiPalette.InkAt(0.55f) : UiPalette.White(0.38f), appear);
            float keyWidth = Theme.Mono.MeasureWidth(Key, keyPx);
            float capW = MathF.Max(context.Px(26f), keyWidth + context.Px(12f));
            float capH = context.Px(24f);
            var cap = new UiRect(after + keyGap, Bounds.Center.Y - (capH * 0.5f) + press, capW, capH);
            painter.FillRect(cap, UiPalette.Fade(onAccent ? UiPalette.InkAt(0.08f) : UiPalette.White(0.07f), appear));
            painter.StrokeRect(cap, context.Px(1f), edge);
            painter.FillRect(new UiRect(cap.X, cap.Bottom - context.Px(2f), cap.Width, context.Px(2f)), edge);
            painter.DrawText(Theme.Mono, Key, new UiVec2(cap.X + ((cap.Width - keyWidth) * 0.5f), UiRowStyle.CenterY(Theme.Mono, keyPx, cap) - context.Px(0.5f)), keyPx, UiPalette.Fade(onAccent ? Theme.OnAccent : Theme.Text, appear));
            return;
        }

        float boxW = Theme.Mono.MeasureWidth(Key, keyPx) + context.Px(8f);
        float boxH = Theme.Mono.LineHeight(keyPx) + context.Px(2f);
        var box = new UiRect(after + keyGap, Bounds.Center.Y - (boxH * 0.5f) + press, boxW, boxH);
        painter.StrokeRect(box, context.Px(1f), ink);
        painter.DrawText(Theme.Mono, Key, new UiVec2(box.X + context.Px(4f), UiRowStyle.CenterY(Theme.Mono, keyPx, box)), keyPx, ink);
    }

    // The subtitle's size and the gap between a mark and the caption. Design units.
    private const float SubtitleSize = 10f;
    private const float GlyphGap = 10f;

    // The key cap's text size and its gap from the caption. Design units.
    private float KeySize => Theme.Large ? 12f : 11f;

    private float KeyGap => Theme.Large ? 10f : 16f;

    // Whether anything stands before the key: the caption or a mark.
    private bool HasLead => ShownText.Length > 0 || LeadingGlyph is not null || TrailingGlyph is not null;

    private float KeyWidth(LayoutContext context)
    {
        if (Key.Length == 0)
        {
            return 0f;
        }

        float text = Theme.Mono.MeasureWidth(Key, context.Px(KeySize));
        float gap = HasLead ? context.Px(KeyGap) : 0f;
        return gap + (Theme.Large ? MathF.Max(context.Px(26f), text + context.Px(12f)) : text + context.Px(8f));
    }

    private float GlyphsWidth(LayoutContext context)
    {
        float glyph = context.Px(GlyphSize);
        float gap = context.Px(GlyphGap);
        float width = 0f;
        if (LeadingGlyph is not null)
        {
            width += glyph + (ShownText.Length > 0 ? gap : 0f);
        }

        if (TrailingGlyph is not null)
        {
            width += glyph + (ShownText.Length > 0 || LeadingGlyph is not null ? gap : 0f);
        }

        return width;
    }
}
