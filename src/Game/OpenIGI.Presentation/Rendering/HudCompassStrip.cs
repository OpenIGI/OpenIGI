using OpenIGI.Core.Graphics;
using OpenIGI.UI;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Presentation.Rendering;

/// <summary>The remastered compass strip across the top of the view: a window onto a ring of bearings, the heading's own over it.</summary>
public static class HudCompassStrip
{
    /// <summary>Draws the strip.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="context">The layout.</param>
    /// <param name="type">The interface's faces.</param>
    /// <param name="headingRadians">Where the player is looking.</param>
    public static void Draw(UiPainter painter, LayoutContext context, UiTheme type, float headingRadians)
    {
        float width = MathF.Min(context.Px(320f), context.SafeRect.Width * 0.26f);
        float centreX = context.SafeRect.X + (context.SafeRect.Width * 0.5f);
        float x = centreX - (width * 0.5f);
        float y = context.SafeRect.Y + context.Px(12f);
        float lineY = y + context.Px(30f);
        float heading = RemasteredHudPresentation.HeadingDegrees(headingRadians);
        const float visibleDegrees = 100f;
        const float halfVisible = visibleDegrees * 0.5f;
        int centreDegrees = ((int)MathF.Round(heading) + 360) % 360;

        // The rule fades out at both ends rather than stopping, so the dial reads as a window onto a ring.
        Span<UiGradientStop> rule =
        [
            new(0f, UiPalette.White(0f)),
            new(0.2f, UiPalette.White(0.18f)),
            new(0.8f, UiPalette.White(0.18f)),
            new(1f, UiPalette.White(0f)),
        ];
        painter.FillGradient(new UiRect(x, lineY, width, context.Px(1f)), rule, UiGradientDirection.Horizontal);

        float firstTick = MathF.Ceiling((heading - halfVisible) / 10f) * 10f;
        for (float degrees = firstTick; degrees <= heading + halfVisible; degrees += 10f)
        {
            int normalized = ((int)degrees % 360 + 360) % 360;
            float tickX = centreX + (((degrees - heading) / visibleDegrees) * width);
            bool major = normalized % 30 == 0;
            float tickHeight = context.Px(major ? 7f : 3f);
            float edge = 1f - MathF.Abs((degrees - heading) / halfVisible);
            painter.FillRect(
                new UiRect(tickX - context.Px(0.5f), lineY, context.Px(1f), tickHeight),
                UiPalette.Fade(Muted, (major ? 0.8f : 0.4f) * Math.Clamp(edge * 2f, 0f, 1f)));
        }

        // The heading marker: an accent diamond on the rule, the bearing in monospace over it.
        painter.FillDiamond(new UiVec2(centreX, lineY), context.Px(7f), Accent);
        string readout = $"{centreDegrees:000}";
        float readoutSize = context.Px(14f);
        float readoutWidth = UiPainter.MeasureLabel(type.Mono, readout, readoutSize, 0.12f);
        painter.DrawLabel(type.Mono, readout, new UiVec2(centreX - (readoutWidth * 0.5f), y + context.Px(4f)), readoutSize, Text, 0.12f);

        for (int cardinal = 0; cardinal < 360; cardinal += 45)
        {
            float relative = cardinal - heading;
            if (relative > 180f)
            {
                relative -= 360f;
            }
            else if (relative < -180f)
            {
                relative += 360f;
            }

            if (MathF.Abs(relative) > halfVisible)
            {
                continue;
            }

            string label = CompassCardinal(cardinal);
            bool major = cardinal % 90 == 0;
            float labelSize = context.Px(major ? 16f : 13f);
            float labelWidth = UiPainter.MeasureLabel(type.Heading, label, labelSize, 0.12f);
            float edge = Math.Clamp((1f - (MathF.Abs(relative) / halfVisible)) * 2f, 0f, 1f);
            painter.DrawLabel(
                type.Heading,
                label,
                new UiVec2(centreX + ((relative / visibleDegrees) * width) - (labelWidth * 0.5f), lineY + context.Px(12f)),
                labelSize,
                UiPalette.Fade(major ? Text : Muted, edge),
                0.12f);
        }
    }

    private static string CompassCardinal(int degrees) => degrees switch
    {
        0 => "N",
        45 => "NE",
        90 => "E",
        135 => "SE",
        180 => "S",
        225 => "SW",
        270 => "W",
        315 => "NW",
        _ => string.Empty,
    };

    private static Bgra32 Text => UiPalette.Text;

    private static Bgra32 Muted => UiPalette.TextSecondary;

    private static Bgra32 Accent => UiPalette.Accent;
}
