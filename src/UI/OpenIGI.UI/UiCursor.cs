using OpenIGI.UI.Layout;

namespace OpenIGI.UI;

/// <summary>The pointer the interface draws itself, where the platform's own is hidden: the shell's and the scoreboard's.</summary>
public static class UiCursor
{
    /// <summary>Draws the arrow with its tip at a point.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="tip">Where the pointer is.</param>
    public static void Draw(UiPainter painter, UiVec2 tip)
    {
        ArgumentNullException.ThrowIfNull(painter);

        // A classic arrow: a white triangle over a slightly larger dark one for contrast, its tip at the
        // pointer. Sized gently with the viewport so it stays a sensible size from 480p to 4K.
        float size = Math.Clamp(painter.Context.Viewport.Y / 42f, 18f, 40f);
        DrawArrow(painter, tip, size + 3f, OpenIGI.Core.Graphics.Bgra32.FromRgba(0, 0, 0, 200));
        DrawArrow(painter, tip, size, OpenIGI.Core.Graphics.Bgra32.White);
    }

    private static void DrawArrow(UiPainter painter, UiVec2 tip, float size, OpenIGI.Core.Graphics.Bgra32 color)
    {
        var down = new UiVec2(tip.X, tip.Y + size);
        var elbow = new UiVec2(tip.X + (size * 0.36f), tip.Y + (size * 0.72f));
        var barb = new UiVec2(tip.X + (size * 0.72f), tip.Y + (size * 0.72f));
        painter.FillTriangle(tip, down, elbow, color);
        painter.FillTriangle(tip, elbow, barb, color);
    }
}
