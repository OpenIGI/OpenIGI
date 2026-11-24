using OpenIGI.Core.Graphics;
using OpenIGI.UI;
using OpenIGI.UI.Layout;

namespace OpenIGI.Presentation.Hud.Remastered.Widgets;

/// <summary>
/// The remastered display's own line pictograms -- the eye that opens when the player is seen -- drawn as smoothed strokes, so
/// they stay crisp at any size and in any colour.
/// </summary>
public static class HudPictograms
{
    /// <summary>Draws an open eye: an almond outline round an iris.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="centre">Its centre, in pixels.</param>
    /// <param name="half">Half its width, in pixels.</param>
    /// <param name="weight">Its line's width, in pixels.</param>
    /// <param name="colour">Its colour.</param>
    public static void Eye(UiPainter painter, UiVec2 centre, float half, float weight, Bgra32 colour)
    {
        ArgumentNullException.ThrowIfNull(painter);
        Span<UiVec2> lid = stackalloc UiVec2[11];
        float lift = half * 0.62f;
        Curve(lid, centre, half, -lift);
        painter.StrokeSoftPolyline(lid, weight, colour);
        Curve(lid, centre, half, lift);
        painter.StrokeSoftPolyline(lid, weight, colour);
        painter.FillDot(centre, half * 0.36f, colour);
    }

    // One lid: a half sine from corner to corner, lifted (or dropped, for a positive lift) at its middle.
    private static void Curve(Span<UiVec2> points, UiVec2 centre, float half, float lift)
    {
        int last = points.Length - 1;
        for (int i = 0; i <= last; i++)
        {
            float t = (float)i / last;
            points[i] = new UiVec2(centre.X - half + (2f * half * t), centre.Y + (MathF.Sin(t * MathF.PI) * lift));
        }
    }
}
