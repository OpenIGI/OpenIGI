using OpenIGI.Core.Graphics;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Servers;

/// <summary>
/// The mark of an official server -- one of ours, whose matches count toward the stats and leaderboards: the verified badge
/// people know from social apps, a scalloped seal in the accent with a tick through it. Wherever the multiplayer page names
/// a server it draws this after the name.
/// </summary>
public static class OfficialMark
{
    // The seal's scallops, how deep they cut into its edge, and how finely its edge is drawn.
    private const int Scallops = 8;
    private const float Depth = 0.13f;
    private const int Points = 96;

    /// <summary>Gets the mark's width, to keep room for it after a name.</summary>
    /// <param name="context">The layout context.</param>
    /// <returns>The width, in pixels.</returns>
    public static float Width(LayoutContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Px(20f);
    }

    /// <summary>Draws the mark, its left edge at a point and its middle on a line.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="leftMiddle">Its left edge, at the height of its middle.</param>
    /// <param name="appear">How far it has faded in, 0 to 1.</param>
    /// <returns>Its box.</returns>
    public static UiRect Draw(UiPainter painter, UiTheme theme, UiVec2 leftMiddle, float appear = 1f)
    {
        ArgumentNullException.ThrowIfNull(painter);
        ArgumentNullException.ThrowIfNull(theme);
        float size = Width(painter.Context);
        var box = new UiRect(leftMiddle.X, leftMiddle.Y - (size / 2f), size, size);
        UiVec2 centre = box.Center;
        float outer = size / 2f;

        // The seal: a fan of triangles about its middle, its edge rounded out into scallops.
        Bgra32 seal = UiPalette.Fade(theme.Accent, appear);
        Span<UiVec2> edge = stackalloc UiVec2[Points];
        for (int i = 0; i < Points; i++)
        {
            float angle = i * MathF.Tau / Points;
            float radius = outer * (1f - Depth + (Depth * MathF.Abs(MathF.Cos(angle * Scallops / 2f))));
            (float sin, float cos) = MathF.SinCos(angle);
            edge[i] = new UiVec2(centre.X + (cos * radius), centre.Y + (sin * radius));
        }

        for (int i = 0; i < Points; i++)
        {
            painter.FillTriangle(centre, edge[i], edge[(i + 1) % Points], seal);
        }

        KitIcons.Draw(painter, KitIcon.Check, new UiVec2(centre.X, centre.Y - (size * 0.03f)), size * 0.5f, Bgra32.FromRgba(10, 12, 13, (byte)(255f * appear)));
        return box;
    }
}
