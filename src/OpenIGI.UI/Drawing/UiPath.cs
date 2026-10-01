using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Drawing;

/// <summary>One of the four screen directions, for a chevron or an arrow.</summary>
public enum UiDirection
{
    /// <summary>Towards the right edge.</summary>
    Right,

    /// <summary>Towards the bottom edge.</summary>
    Down,

    /// <summary>Towards the left edge.</summary>
    Left,

    /// <summary>Towards the top edge.</summary>
    Up,
}

/// <summary>Which corners of a rectangle are cut off at 45°.</summary>
[Flags]
public enum UiCorners
{
    /// <summary>No corner is cut.</summary>
    None = 0,

    /// <summary>The top-left corner.</summary>
    TopLeft = 1,

    /// <summary>The top-right corner.</summary>
    TopRight = 2,

    /// <summary>The bottom-right corner.</summary>
    BottomRight = 4,

    /// <summary>The bottom-left corner.</summary>
    BottomLeft = 8,

    /// <summary>Every corner.</summary>
    All = TopLeft | TopRight | BottomRight | BottomLeft,
}

/// <summary>Builders for the convex shapes the design is drawn from: cut-corner boxes, diamonds and chevrons.</summary>
/// <remarks>
/// The design has no rounded corners. Its buttons and panels are squared off, and the accented ones lose
/// a corner to a 45° cut (CSS <c>clip-path: polygon(...)</c>); its bullets and logo are squares turned
/// 45°. Each builder writes a convex polygon, clockwise on a Y-down screen, into a caller's span, so a
/// draw allocates nothing and the shape fills and strokes through the same path on every renderer.
/// </remarks>
public static class UiPath
{
    /// <summary>The most points any builder here writes.</summary>
    public const int MaxPoints = 8;

    /// <summary>Writes a rectangle with some of its corners cut at 45°.</summary>
    /// <param name="rect">The rectangle.</param>
    /// <param name="cut">The length cut from each side at a cut corner; clamped to half the shorter side.</param>
    /// <param name="corners">Which corners are cut.</param>
    /// <param name="points">Receives the polygon; must hold <see cref="MaxPoints"/>.</param>
    /// <returns>The number of points written.</returns>
    public static int CutCornerRect(UiRect rect, float cut, UiCorners corners, Span<UiVec2> points)
    {
        float c = MathF.Max(0f, MathF.Min(cut, MathF.Min(rect.Width, rect.Height) * 0.5f));
        int n = 0;
        float l = rect.X;
        float t = rect.Y;
        float r = rect.Right;
        float b = rect.Bottom;

        if (c > 0f && corners.HasFlag(UiCorners.TopLeft))
        {
            points[n++] = new UiVec2(l, t + c);
            points[n++] = new UiVec2(l + c, t);
        }
        else
        {
            points[n++] = new UiVec2(l, t);
        }

        if (c > 0f && corners.HasFlag(UiCorners.TopRight))
        {
            points[n++] = new UiVec2(r - c, t);
            points[n++] = new UiVec2(r, t + c);
        }
        else
        {
            points[n++] = new UiVec2(r, t);
        }

        if (c > 0f && corners.HasFlag(UiCorners.BottomRight))
        {
            points[n++] = new UiVec2(r, b - c);
            points[n++] = new UiVec2(r - c, b);
        }
        else
        {
            points[n++] = new UiVec2(r, b);
        }

        if (c > 0f && corners.HasFlag(UiCorners.BottomLeft))
        {
            points[n++] = new UiVec2(l + c, b);
            points[n++] = new UiVec2(l, b - c);
        }
        else
        {
            points[n++] = new UiVec2(l, b);
        }

        return n;
    }

    /// <summary>Writes a square turned 45° — the design's bullet, status marker and logo.</summary>
    /// <param name="center">The diamond's centre.</param>
    /// <param name="size">The side of the square before it is turned; the diamond's width is <c>size × √2</c>.</param>
    /// <param name="points">Receives the polygon; must hold four points.</param>
    /// <returns>The number of points written: four.</returns>
    public static int Diamond(UiVec2 center, float size, Span<UiVec2> points)
    {
        float h = size * 0.70710678f;
        points[0] = new UiVec2(center.X, center.Y - h);
        points[1] = new UiVec2(center.X + h, center.Y);
        points[2] = new UiVec2(center.X, center.Y + h);
        points[3] = new UiVec2(center.X - h, center.Y);
        return 4;
    }

    /// <summary>Writes a solid triangular chevron pointing in a direction — a dropdown caret or a cycler arrow.</summary>
    /// <param name="center">The chevron's centre.</param>
    /// <param name="size">The chevron's width across its base.</param>
    /// <param name="angle">The direction it points, in radians: 0 right, π/2 down, π left, -π/2 up.</param>
    /// <param name="points">Receives the polygon; must hold three points.</param>
    /// <returns>The number of points written: three.</returns>
    public static int Chevron(UiVec2 center, float size, float angle, Span<UiVec2> points)
    {
        float half = size * 0.5f;
        float depth = size * 0.3f;
        (float sin, float cos) = MathF.SinCos(angle);
        points[0] = Rotate(depth, 0f);
        points[1] = Rotate(-depth, half);
        points[2] = Rotate(-depth, -half);

        // Keep clockwise winding whatever the angle: the tip, then the two base corners.
        if (UiGeometry.SignedArea2(points[..3]) < 0f)
        {
            (points[1], points[2]) = (points[2], points[1]);
        }

        return 3;

        UiVec2 Rotate(float x, float y) => new(center.X + (x * cos) - (y * sin), center.Y + (x * sin) + (y * cos));
    }
}
