using OpenIGI.Core.Graphics;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Widgets;

/// <summary>
/// A tab in a header bar: a muted uppercase caption that lights white, with an amber underline on the
/// bar's bottom edge when it is the current section, and an optional count badge beside it.
/// </summary>
public class HeaderTab : Button
{
    /// <summary>Initializes a new instance of the <see cref="HeaderTab"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="text">The caption.</param>
    public HeaderTab(UiTheme theme, string text)
        : base(theme, text)
    {
        Accent = ButtonAccent.Underline;
        Padding = new Thickness(22f, 0f);
        TextSize = 21f;
    }

    /// <summary>Gets or sets the count shown in a badge beside the caption; zero for none.</summary>
    public int Badge { get; set; }

    /// <summary>Gets or sets the badge's colour, or <see langword="null"/> for the accent.</summary>
    public Bgra32? BadgeColor { get; set; }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        UiVec2 size = base.MeasureContent(context, available);
        return Badge > 0 ? new UiVec2(size.X + context.Px(8f) + BadgeWidth(context), size.Y) : size;
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        if (Badge <= 0)
        {
            base.OnDraw(painter);
            return;
        }

        // The caption and its badge are centred together, so the badge shifts the caption left; the
        // underline still spans the whole tab.
        LayoutContext context = painter.Context;
        float appear = Visual.Appear;
        float lit = MathF.Max(Highlight, Selected ? 1f : 0f);
        if (Selected)
        {
            float inset = context.Px(Padding.Left);
            float thickness = context.Px(2f);
            painter.FillRect(new UiRect(Bounds.X + inset, Bounds.Bottom - thickness, MathF.Max(0f, Bounds.Width - (inset * 2f)), thickness), UiPalette.Fade(Theme.Accent, appear));
        }

        float badgeWidth = BadgeWidth(context);
        float shift = (context.Px(8f) + badgeWidth) * 0.5f;
        painter.PushTransform(Drawing.UiTransform.Translate(-shift, 0f));
        DrawCaption(painter, !IsEnabled ? Theme.TextDisabled : Animation.Interpolate.Lerp(Theme.TextMuted, Theme.TextBright, lit), appear);
        painter.PopTransform();

        float captionWidth = UiPainter.MeasureLabel(Face, ShownText, context.Px(CaptionSize), CaptionTracking);
        float x = Bounds.Center.X - shift + (captionWidth * 0.5f) + context.Px(8f);
        float height = context.Px(18f);
        var box = new UiRect(x, Bounds.Center.Y - (height * 0.5f), badgeWidth, height);
        painter.FillRect(box, UiPalette.Fade(BadgeColor ?? Theme.Accent, Visual.Appear));
        string text = Badge.ToString(System.Globalization.CultureInfo.InvariantCulture);
        float size = context.Px(13f);
        float width = Theme.Mono.MeasureWidth(text, size);
        painter.DrawText(Theme.Mono, text, new UiVec2(box.Center.X - (width * 0.5f), UiRowStyle.CenterY(Theme.Mono, size, box)), size, Theme.OnAccent);
    }

    private float BadgeWidth(LayoutContext context)
    {
        string text = Badge.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return MathF.Max(context.Px(20f), Theme.Mono.MeasureWidth(text, context.Px(13f)) + context.Px(10f));
    }
}
