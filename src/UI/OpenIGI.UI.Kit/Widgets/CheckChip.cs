using OpenIGI.Core.Graphics;
using OpenIGI.UI;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Widgets;

/// <summary>A filter that is on or off, as a small switch before its name: HIDE FULL.</summary>
public sealed class CheckChip : Control
{
    private const float TextSize = 17f;
    private const float Tracking = 0.12f;

    private readonly string _text;

    /// <summary>Initializes a new instance of the <see cref="CheckChip"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="text">Its name, shown in capitals.</param>
    public CheckChip(UiTheme theme, string text)
        : base(theme)
    {
        _text = text.ToUpperInvariant();
    }

    /// <summary>Gets or sets a value indicating whether it is on.</summary>
    public bool Checked { get; set; }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) =>
        new(context.Px(44f + 10f) + UiPainter.MeasureLabel(Theme.Heading, _text, context.Px(TextSize), Tracking), context.Px(44f));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        float appear = Visual.Appear;
        float hot = MathF.Max(Visual.Focus, IsPointerOver ? 1f : 0f);
        var track = new UiRect(Bounds.X, Bounds.Center.Y - context.Px(11.5f), context.Px(44f), context.Px(23f));
        painter.FillRect(track, UiPalette.Fade(Checked ? Theme.Accent : UiPalette.White(0.06f), appear));
        painter.StrokeRect(track, context.Px(1f), UiPalette.Fade(Checked || hot > 0f ? Theme.Accent : UiPalette.White(0.28f), appear));
        float knob = context.Px(15f);
        float knobX = Checked ? track.Right - context.Px(4f) - knob : track.X + context.Px(4f);
        painter.FillRect(new UiRect(knobX, track.Center.Y - (knob / 2f), knob, knob), UiPalette.Fade(Checked ? Theme.OnAccent : Theme.TextSecondary, appear));

        float size = context.Px(TextSize);
        painter.DrawLabel(Theme.Heading, _text, new UiVec2(track.Right + context.Px(10f), UiRowStyle.CapsCenterY(Theme.Heading, size, Bounds)), size, UiPalette.Fade(Checked || hot > 0f ? Theme.Text : Theme.TextSecondary, appear), Tracking);
        if (Visual.Focus > 0.001f)
        {
            painter.StrokeRect(Bounds.Inflate(new Thickness(context.Px(4f), 0f)), context.Px(2f), UiPalette.Fade(Theme.AccentHover, Visual.Focus * appear));
        }
    }
}
