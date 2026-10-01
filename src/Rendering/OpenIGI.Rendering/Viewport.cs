namespace OpenIGI.Rendering;

/// <summary>
/// A rectangular render target region with a depth range.
/// </summary>
/// <param name="X">The left edge in pixels.</param>
/// <param name="Y">The top edge in pixels.</param>
/// <param name="Width">The width in pixels.</param>
/// <param name="Height">The height in pixels.</param>
/// <param name="MinDepth">The near end of the depth range mapped to the depth buffer.</param>
/// <param name="MaxDepth">The far end of the depth range mapped to the depth buffer.</param>
/// <remarks>
/// This mirrors the primitive consumed by the original's <c>ClipWindowPrimitive</c> method
/// (<c>0x493CA0</c>), which carries a position and a size and converts to Direct3D's right/bottom pair
/// on the way in.
/// </remarks>
public readonly record struct Viewport(int X, int Y, int Width, int Height, float MinDepth, float MaxDepth)
{
    /// <summary>Initializes a new instance of the <see cref="Viewport"/> struct with the full depth range.</summary>
    /// <param name="x">The left edge in pixels.</param>
    /// <param name="y">The top edge in pixels.</param>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    public Viewport(int x, int y, int width, int height)
        : this(x, y, width, height, 0.0f, 1.0f)
    {
    }

    /// <summary>The nearest sliver of the depth range, which the first-person layer is drawn into.</summary>
    /// <remarks>
    /// Reverse-Z, so one is the near plane and the whole world sits below this floor: at a tenth of a
    /// metre near plane, 0.9 is about eleven centimetres, and no world surface is ever that close to the
    /// eye. Anything drawn inside the sliver therefore wins the depth test against everything the world
    /// wrote, which is what clearing the depth used to buy, while the parts of the rig still occlude each
    /// other normally within it — and the scene's depth, which every pass after reads, survives.
    /// </remarks>
    public const float ForegroundDepthFloor = 0.9f;

    /// <summary>Gets the right edge, exclusive.</summary>
    public int Right => X + Width;

    /// <summary>Gets the bottom edge, exclusive.</summary>
    public int Bottom => Y + Height;
}
