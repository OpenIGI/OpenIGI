using OpenIGI.Core.Graphics;

namespace OpenIGI.Game.World.Sectors;

/// <summary>
/// A rectangle in screen pixels, which is the only thing the sector cull ever narrows.
/// </summary>
/// <param name="MinX">The left edge.</param>
/// <param name="MinY">The top edge.</param>
/// <param name="MaxX">The right edge.</param>
/// <param name="MaxY">The bottom edge.</param>
/// <remarks>
/// The original's <c>real32x2_t</c> pair. It seeds a portal's box at <c>±FLT_MAX</c>
/// (<c>igi2.exe 0x587D1C</c>) and treats an unfilled box as a refusal, which is what
/// <see cref="Empty"/> reproduces: an inverted rectangle that overlaps nothing.
/// </remarks>
public readonly record struct SectorScreenRect(float MinX, float MinY, float MaxX, float MaxY)
{
    /// <summary>Gets the inverted rectangle a portal's bounding box starts from.</summary>
    public static SectorScreenRect Empty { get; } =
        new(float.MaxValue, float.MaxValue, -float.MaxValue, -float.MaxValue);

    /// <summary>Gets a value indicating whether this rectangle encloses nothing.</summary>
    public bool IsEmpty => MinX > MaxX || MinY > MaxY;

    /// <summary>Gets the width, which is negative for an empty rectangle.</summary>
    public float Width => MaxX - MinX;

    /// <summary>Gets the height, which is negative for an empty rectangle.</summary>
    public float Height => MaxY - MinY;

    /// <summary>Tests whether two rectangles touch or overlap.</summary>
    /// <param name="other">The rectangle to test against.</param>
    /// <returns><see langword="true"/> when they share at least an edge.</returns>
    /// <remarks>
    /// Inclusive on all four sides, because the original's test at <c>igi2.exe 0x58841E</c> is
    /// <c>&lt;=</c> and <c>&gt;=</c>: a portal that lines up exactly with the edge of the rectangle it is
    /// seen through still admits what is beyond it.
    /// </remarks>
    public bool Overlaps(SectorScreenRect other) =>
        MinX <= other.MaxX && MinY <= other.MaxY && MaxX >= other.MinX && MaxY >= other.MinY;

    /// <summary>Narrows this rectangle to the part inside another.</summary>
    /// <param name="other">The rectangle to clip against.</param>
    /// <returns>The intersection, which may be empty.</returns>
    /// <remarks>
    /// <c>SectorObject_TraverseSector</c> does this to the box <c>Sector_IsPortalVisible</c> hands back
    /// (<c>igi2.exe 0x5888FD</c>-<c>0x58893E</c>) before recursing. The narrowing lives in the caller,
    /// not in the visibility test, which returns the portal's own box unclipped.
    /// </remarks>
    public SectorScreenRect Intersect(SectorScreenRect other) => new(
        MathF.Max(MinX, other.MinX),
        MathF.Max(MinY, other.MinY),
        MathF.Min(MaxX, other.MaxX),
        MathF.Min(MaxY, other.MaxY));

    /// <summary>Widens this rectangle to enclose another.</summary>
    /// <param name="other">The rectangle to take in.</param>
    /// <returns>The union.</returns>
    /// <remarks>
    /// <c>SectorObject_RequestSector</c> (<c>igi2.exe 0x588470</c>) keeps one union per sector across
    /// every route that reached it, alongside the individual windows.
    /// </remarks>
    public SectorScreenRect Union(SectorScreenRect other) => new(
        MathF.Min(MinX, other.MinX),
        MathF.Min(MinY, other.MinY),
        MathF.Max(MaxX, other.MaxX),
        MathF.Max(MaxY, other.MaxY));

    /// <summary>Widens this rectangle to enclose one point.</summary>
    /// <param name="x">The point's horizontal position.</param>
    /// <param name="y">The point's vertical position.</param>
    /// <returns>The widened rectangle.</returns>
    public SectorScreenRect Include(float x, float y) => new(
        MathF.Min(MinX, x),
        MathF.Min(MinY, y),
        MathF.Max(MaxX, x),
        MathF.Max(MaxY, y));

    /// <inheritdoc/>
    public override string ToString() =>
        $"[{MinX:0.##}, {MinY:0.##}]..[{MaxX:0.##}, {MaxY:0.##}]";
}

/// <summary>
/// How a view-space position becomes a pixel, in the four numbers the original keeps for it.
/// </summary>
/// <param name="ScreenFovX">Pixels per unit of <c>x/z</c>; the original's <c>vScreenFOVX</c>.</param>
/// <param name="ScreenFovY">Pixels per unit of <c>y/z</c>; the original's <c>vScreenFOVY</c>.</param>
/// <param name="OriginX">The horizontal pixel <c>x/z == 0</c> lands on.</param>
/// <param name="OriginY">The vertical pixel <c>y/z == 0</c> lands on.</param>
/// <param name="DisplayArea">
/// The rectangle every outcode is measured against, and the rectangle a traversal starts from — the
/// original's <c>RenderContext_tActiveRenderContext.tDisplayArea{Min,Max}</c>.
/// </param>
/// <remarks>
/// <para>
/// <c>Sector_IsPortalVisible</c> projects with scalar arithmetic rather than a matrix
/// (<c>igi2.exe 0x588257</c>-<c>0x58828D</c>):
/// <c>screenX = x/z · vScreenFOVX + vScreenOriginX</c> and the same for <c>y</c>. Those four fields sit
/// in <c>TransContext_s</c> at <c>0x48</c>, <c>0x4C</c>, <c>0x50</c> and <c>0x54</c>.
/// </para>
/// <para>
/// <see cref="FromViewport"/> derives them the way <c>TransContext_Create</c> (<c>igi2.exe 0x569D20</c>)
/// does, which is the only place they are written.
/// </para>
/// </remarks>
public readonly record struct SectorScreenProjection(
    float ScreenFovX,
    float ScreenFovY,
    float OriginX,
    float OriginY,
    SectorScreenRect DisplayArea)
{
    /// <summary>Builds the projection of a viewport, as the original builds it.</summary>
    /// <param name="viewport">The viewport in pixels.</param>
    /// <param name="tangentHalfFovX">
    /// The original's <c>vFOVX</c>: the value of <c>x/z</c> that lands on the viewport's right edge, so
    /// the tangent of half the horizontal field of view.
    /// </param>
    /// <param name="pixelAspect">
    /// The original's <c>(modeHeight · 4) / (modeWidth · 3)</c> — one for a 4:3 display mode, and the
    /// correction that keeps a circle round on one that is not.
    /// </param>
    /// <returns>The projection.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tangentHalfFovX"/> is not positive.</exception>
    /// <remarks>
    /// Read out of <c>TransContext_Create</c> (<c>igi2.exe 0x569D20</c>), which writes
    /// <c>vScreenFOVX = clipRect.vHalfWidth / vFOVX</c>, <c>vScreenFOVY = pixelAspect · vScreenFOVX</c>
    /// and the two origins as the clipping rectangle's centre. The original keeps the clipping rectangle
    /// and the display area apart; for a full-viewport render they are the same rectangle, which is what
    /// this assumes.
    /// </remarks>
    public static SectorScreenProjection FromViewport(
        SectorScreenRect viewport,
        float tangentHalfFovX,
        float pixelAspect = 1f)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tangentHalfFovX);

        float halfWidth = viewport.Width * 0.5f;
        float halfHeight = viewport.Height * 0.5f;
        float scaleX = halfWidth / tangentHalfFovX;

        return new SectorScreenProjection(
            scaleX,
            pixelAspect * scaleX,
            viewport.MinX + halfWidth,
            viewport.MinY + halfHeight,
            viewport);
    }

    /// <summary>The projection of a view given by its two half-angle tangents, on a screen sized to match them.</summary>
    /// <param name="tangentHalfFovX">The tangent of half the horizontal view angle.</param>
    /// <param name="tangentHalfFovY">The tangent of half the vertical view angle.</param>
    /// <returns>The projection, with square pixels.</returns>
    /// <remarks>
    /// Every test the sector cull makes on a rectangle is a ratio of the projection's own scale, so the screen's size decides
    /// nothing; it is fixed at 512 pixels per unit of tangent so that a rectangle reads in familiar numbers.
    /// </remarks>
    public static SectorScreenProjection ForView(float tangentHalfFovX, float tangentHalfFovY)
    {
        const float pixelsPerTangent = 512f;
        SectorScreenRect screen = new(0f, 0f, 2f * tangentHalfFovX * pixelsPerTangent, 2f * tangentHalfFovY * pixelsPerTangent);
        return FromViewport(screen, tangentHalfFovX);
    }

    /// <summary>Takes a rectangle on this projection's screen to the part of the view it covers.</summary>
    /// <param name="window">The rectangle, in this projection's pixels.</param>
    /// <returns>The same part of the view as fractions of it, measured from its top-left corner.</returns>
    /// <remarks>
    /// The sector cull's screen counts its vertical axis up -- a view-space <c>y</c> above the axis lands on a larger pixel row --
    /// and a view clip counts it down, so the rows are turned over.
    /// </remarks>
    public ViewClip ToViewClip(SectorScreenRect window)
    {
        SectorScreenRect area = DisplayArea;
        float width = area.Width, height = area.Height;
        return new ViewClip(
            (window.MinX - area.MinX) / width,
            1f - ((window.MaxY - area.MinY) / height),
            (window.MaxX - area.MinX) / width,
            1f - ((window.MinY - area.MinY) / height));
    }
}
