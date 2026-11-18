using OpenIGI.Core.Graphics;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Hud;

/// <summary>
/// A place a teammate marked for the others (a ping): a map pin in the accent, its point on the spot, who marked it and how far
/// over it. It lands with a feel -- the pin drops in a touch large and settles, and two rings ripple out from the spot -- so
/// the eye goes to it; then it holds, quiet, and fades at the end of its time. Out of view it waits on the screen's edge with
/// an arrow pointing the way.
/// </summary>
public static class PingMark
{
    // The pin's head and how far its point stands under it, in layout pixels.
    private const float HeadRadius = 13f;
    private const float PointDrop = 19f;

    // How long the landing lasts: the pin's settle, and the ripples' run.
    private const float SettleSeconds = 0.22f;
    private const float RippleSeconds = 0.9f;

    /// <summary>Draws a ping.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="font">The type its caption is set in.</param>
    /// <param name="spot">Where it is on the screen: the marked place, or where it waits on the edge.</param>
    /// <param name="name">Who marked it.</param>
    /// <param name="distanceMetres">How far it is.</param>
    /// <param name="ageSeconds">How long ago it was put.</param>
    /// <param name="opacity">How much of it is left, 1 to 0 as it fades at the end of its time.</param>
    /// <param name="pointing">The way to the place when it waits on the screen's edge out of view; <see langword="null"/> in view.</param>
    public static void Draw(UiPainter painter, UiFont font, UiVec2 spot, string name, double distanceMetres, float ageSeconds, float opacity, UiVec2? pointing = null)
    {
        ArgumentNullException.ThrowIfNull(painter);
        ArgumentNullException.ThrowIfNull(font);
        LayoutContext context = painter.Context;
        opacity = Math.Clamp(opacity, 0f, 1f);
        if (opacity <= 0f)
        {
            return;
        }

        Bgra32 accent = UiPalette.Accent.WithAlpha((byte)(255 * opacity));
        bool inView = pointing is null;

        // The landing's ripples, out from the spot: two rings, the second a beat behind, thinning and fading as they go.
        if (inView)
        {
            for (int ring = 0; ring < 2; ring++)
            {
                float t = (ageSeconds - (ring * 0.18f)) / RippleSeconds;
                if (t is > 0f and < 1f)
                {
                    float eased = 1f - ((1f - t) * (1f - t));
                    float radius = context.Px(6f + (46f * eased));
                    float width = MathF.Max(1f, context.Px(2.5f * (1f - t)));
                    painter.FillArc(spot, radius - width, radius, 0f, MathF.Tau, UiPalette.Accent.WithAlpha((byte)(200 * (1f - t) * opacity)));
                }
            }
        }

        // The pin, a touch large as it lands, settling to its size; in view its point is on the spot, on the edge it is centred.
        float settle = Math.Clamp(ageSeconds / SettleSeconds, 0f, 1f);
        float scale = 1f + (0.35f * (1f - settle) * (1f - settle));
        float head = context.Px(HeadRadius * scale);
        float drop = context.Px(PointDrop * scale);
        var headCentre = inView ? new UiVec2(spot.X, spot.Y - drop) : spot;
        Pin(painter, headCentre, head, drop, UiPalette.InkAt(0.7f * opacity), grow: MathF.Max(1.5f, context.Px(2f)));
        Pin(painter, headCentre, head, drop, accent, grow: 0f);
        painter.FillDot(headCentre, head * 0.38f, UiPalette.InkAt(0.85f * opacity));

        if (pointing is { } way)
        {
            HudInk.EdgeArrow(painter, headCentre, head + context.Px(6f), way, accent);
        }

        // Who marked it and how far, over the pin.
        float size = context.Px(16f);
        float top = headCentre.Y - head - context.Px(5f) - font.LineHeight(size);
        HudInk.Caption(painter, font, headCentre.X, top, name, HudInk.Distance(distanceMetres), size, opacity);
    }

    // A map pin: a round head, and a point under it on the spot; grown by a margin for its dark edge.
    private static void Pin(UiPainter painter, UiVec2 head, float radius, float drop, Bgra32 colour, float grow)
    {
        float r = radius + grow;
        painter.FillDot(head, r, colour);

        // The point: from where the head's sides turn down, to the tip under it.
        float shoulder = r * 0.82f;
        float lift = r * 0.55f;
        painter.FillTriangle(
            new UiVec2(head.X - shoulder, head.Y + lift),
            new UiVec2(head.X + shoulder, head.Y + lift),
            new UiVec2(head.X, head.Y + drop + (grow * 1.6f)),
            colour);
    }
}
