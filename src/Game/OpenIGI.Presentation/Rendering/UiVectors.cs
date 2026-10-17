using OpenIGI.Core.Graphics;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;

namespace OpenIGI.Presentation.Rendering;

/// <summary>
/// Resolution-independent vector primitives the remastered interfaces share: discs, rings, diamonds,
/// chevrons and letterspaced text, all composed from the painter's triangles and quads.
/// </summary>
/// <remarks>
/// The tactical computer and the binocular optics draw the same family of marks, so the geometry lives
/// here once rather than being copied into each renderer. Everything takes already-scaled pixel values;
/// callers apply <see cref="LayoutContext.Px(float)"/> before handing sizes in.
/// </remarks>
public static class UiVectors
{
    /// <summary>Draws a run of text with an even extra gap between glyphs.</summary>
    /// <returns>The advance width of the run.</returns>
    public static float DrawTracked(
        UiPainter painter,
        UiFont font,
        ReadOnlySpan<char> text,
        UiVec2 at,
        float size,
        Bgra32 colour,
        float tracking)
    {
        Span<char> one = stackalloc char[1];
        float x = at.X;
        foreach (char c in text)
        {
            one[0] = c;
            painter.DrawText(font, one, new UiVec2(x, at.Y), size, colour);
            x += font.MeasureWidth(one, size) + tracking;
        }

        return MathF.Max(0f, x - at.X - tracking);
    }

    /// <summary>Measures the advance width of a letterspaced run without drawing it.</summary>
    public static float MeasureTracked(UiFont font, ReadOnlySpan<char> text, float size, float tracking) =>
        text.Length == 0 ? 0f : font.MeasureWidth(text, size) + (tracking * (text.Length - 1));

    /// <summary>Fills a circle as a triangle fan.</summary>
    public static void FillDisc(UiPainter painter, UiVec2 centre, float radius, Bgra32 colour, int segments = 16)
    {
        float step = MathF.Tau / segments;
        for (int i = 0; i < segments; i++)
        {
            float a = i * step;
            float b = (i + 1) * step;
            painter.FillTriangle(
                centre,
                new UiVec2(centre.X + (MathF.Cos(a) * radius), centre.Y + (MathF.Sin(a) * radius)),
                new UiVec2(centre.X + (MathF.Cos(b) * radius), centre.Y + (MathF.Sin(b) * radius)),
                colour);
        }
    }

    /// <summary>Strokes a ring of the given thickness as a triangle strip.</summary>
    public static void StrokeRing(
        UiPainter painter,
        UiVec2 centre,
        float radius,
        float thickness,
        Bgra32 colour,
        int segments = 22)
    {
        float inner = MathF.Max(0f, radius - (thickness * 0.5f));
        float outer = radius + (thickness * 0.5f);
        float step = MathF.Tau / segments;

        for (int i = 0; i < segments; i++)
        {
            float a = i * step;
            float b = (i + 1) * step;
            var innerA = new UiVec2(centre.X + (MathF.Cos(a) * inner), centre.Y + (MathF.Sin(a) * inner));
            var innerB = new UiVec2(centre.X + (MathF.Cos(b) * inner), centre.Y + (MathF.Sin(b) * inner));
            var outerA = new UiVec2(centre.X + (MathF.Cos(a) * outer), centre.Y + (MathF.Sin(a) * outer));
            var outerB = new UiVec2(centre.X + (MathF.Cos(b) * outer), centre.Y + (MathF.Sin(b) * outer));
            painter.FillTriangle(innerA, outerA, outerB, colour);
            painter.FillTriangle(innerA, outerB, innerB, colour);
        }
    }

    /// <summary>Fills a diamond (a square rotated 45 degrees).</summary>
    public static void DrawDiamond(UiPainter painter, UiVec2 centre, float radius, Bgra32 colour)
    {
        var top = new UiVec2(centre.X, centre.Y - radius);
        var right = new UiVec2(centre.X + radius, centre.Y);
        var bottom = new UiVec2(centre.X, centre.Y + radius);
        var left = new UiVec2(centre.X - radius, centre.Y);
        painter.FillTriangle(top, right, bottom, colour);
        painter.FillTriangle(top, bottom, left, colour);
    }

    /// <summary>Fills a downward-pointing chevron (an inverted triangle) whose apex sits at <paramref name="apex"/>.</summary>
    /// <param name="painter">The painter to draw into.</param>
    /// <param name="apex">The bottom point, which sits above the marked target.</param>
    /// <param name="halfWidth">Half the width of the chevron's top edge.</param>
    /// <param name="height">How far the top edge sits above the apex.</param>
    /// <param name="colour">The fill colour.</param>
    public static void DrawChevron(UiPainter painter, UiVec2 apex, float halfWidth, float height, Bgra32 colour)
    {
        painter.FillTriangle(
            apex,
            new UiVec2(apex.X - halfWidth, apex.Y - height),
            new UiVec2(apex.X + halfWidth, apex.Y - height),
            colour);
    }
}
