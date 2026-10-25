namespace OpenIGI.Core.Graphics;

/// <summary>A part of the view, as fractions of its width and height measured from its top-left corner.</summary>
/// <param name="Left">The left edge, nought at the view's left.</param>
/// <param name="Top">The top edge, nought at the view's top.</param>
/// <param name="Right">The right edge, one at the view's right.</param>
/// <param name="Bottom">The bottom edge, one at the view's bottom.</param>
/// <remarks>
/// In fractions rather than pixels because whoever decides it -- a title's sector cull -- knows the view's angles, not the size the
/// renderer draws it at, which changes with the render scale and the upscaler.
/// </remarks>
public readonly record struct ViewClip(float Left, float Top, float Right, float Bottom)
{
    /// <summary>Gets the whole view.</summary>
    public static ViewClip Full { get; } = new(0f, 0f, 1f, 1f);

    /// <summary>Gets a value indicating whether the clip covers nothing.</summary>
    public bool IsEmpty => Right <= Left || Bottom <= Top;

    /// <summary>Takes a rectangle in pixels to the part of a viewport it covers, clamped to the viewport.</summary>
    /// <param name="x">The viewport's left edge, in pixels.</param>
    /// <param name="y">The viewport's top edge, in pixels.</param>
    /// <param name="width">The viewport's width, in pixels.</param>
    /// <param name="height">The viewport's height, in pixels.</param>
    /// <returns>The scissor: its left, top, width and height in pixels, never outside the viewport and never negative.</returns>
    /// <remarks>Widened outwards to whole pixels, so a window's edge is never shaved off.</remarks>
    public (int X, int Y, int Width, int Height) ToScissor(int x, int y, int width, int height)
    {
        int left = x + (int)MathF.Floor(Math.Clamp(Left, 0f, 1f) * width);
        int top = y + (int)MathF.Floor(Math.Clamp(Top, 0f, 1f) * height);
        int right = x + (int)MathF.Ceiling(Math.Clamp(Right, 0f, 1f) * width);
        int bottom = y + (int)MathF.Ceiling(Math.Clamp(Bottom, 0f, 1f) * height);
        return (left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }
}
