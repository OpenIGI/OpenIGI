using OpenIGI.Core.Graphics;
using OpenIGI.Rendering;

namespace OpenIGI.UI;

/// <summary>
/// The small generated texture that gives the interface rounded corners and soft shadows.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> <see cref="UiPainter"/> can fill axis-aligned rectangles, draw textured
/// quads and draw text. It has no rounded-rectangle primitive, no line, and no blur — and a modern
/// floating window needs a soft corner and a shadow under it or it reads as a debug overlay from
/// 1999. Both shapes are radially symmetric, so a single quadrant of each, mirrored into four corners
/// and stretched along the straight edges, is enough to draw either at any size.
/// </para>
/// <para>
/// It is generated rather than shipped: it is a few thousand <see cref="MathF"/> operations once at
/// start-up, it never needs an artist, and it cannot go missing from an install. There is deliberately
/// no disk cache — the font atlases are the expensive thing and already have one; caching this would
/// cost more in file handling than it saves.
/// </para>
/// <para>
/// Both shapes live in the alpha channel over white, so a draw modulates them with a vertex colour
/// exactly as text does. One texture, two sprites, nine quads per rounded figure.
/// </para>
/// </remarks>
public sealed class UiSurfaces : IDisposable
{
    /// <summary>The size in texels of each generated quadrant.</summary>
    /// <remarks>
    /// 32 carries a 12-16 px radius cleanly at ordinary interface scales and still minifies without
    /// stepping. Larger buys nothing: the corner is a smooth ramp, not detail.
    /// </remarks>
    private const int Quadrant = 32;

    /// <summary>The atlas width, holding the two quadrants side by side with a texel of guard between.</summary>
    private const int AtlasWidth = 128;

    /// <summary>The atlas height.</summary>
    private const int AtlasHeight = 64;

    private readonly IGraphicsDevice _device;
    private bool _disposed;

    private UiSurfaces(IGraphicsDevice device, TextureHandle texture)
    {
        _device = device;
        Texture = texture;

        Rounded = new NineSlice(texture, new RectF(0f, 0f, Quadrant / (float)AtlasWidth, Quadrant / (float)AtlasHeight), Quadrant);
        Shadow = new NineSlice(
            texture,
            new RectF(Quadrant * 2 / (float)AtlasWidth, 0f, Quadrant / (float)AtlasWidth, Quadrant / (float)AtlasHeight),
            Quadrant);
    }

    /// <summary>Gets the generated atlas.</summary>
    public TextureHandle Texture { get; }

    /// <summary>Gets the anti-aliased rounded-corner quadrant.</summary>
    public NineSlice Rounded { get; }

    /// <summary>Gets the soft shadow-corner quadrant.</summary>
    public NineSlice Shadow { get; }

    /// <summary>Generates the atlas and uploads it.</summary>
    /// <param name="device">The device to upload to.</param>
    /// <returns>The uploaded surfaces.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> is <see langword="null"/>.</exception>
    public static UiSurfaces Create(IGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        Bgra32[] pixels = new Bgra32[AtlasWidth * AtlasHeight];
        WriteRoundedCorner(pixels, originX: 0);
        WriteShadowCorner(pixels, originX: Quadrant * 2);

        TextureHandle texture = device.CreateTexture(new TextureDescription(AtlasWidth, AtlasHeight), pixels);
        return new UiSurfaces(device, texture);
    }

    /// <summary>Releases the atlas.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _device.DestroyTexture(Texture);
    }

    /// <summary>
    /// Writes a quarter disc whose edge is antialiased by coverage.
    /// </summary>
    /// <param name="pixels">The atlas being filled.</param>
    /// <param name="originX">The quadrant's left edge in the atlas.</param>
    /// <remarks>
    /// The corner is authored with its centre at the quadrant's <em>far</em> corner, so texel (0,0) is
    /// the outside of the curve and the inner edges are solid — which is what lets the same quadrant be
    /// stretched along a straight edge and stay opaque there.
    /// </remarks>
    private static void WriteRoundedCorner(Bgra32[] pixels, int originX)
    {
        const float Radius = Quadrant;

        for (int y = 0; y < Quadrant; y++)
        {
            for (int x = 0; x < Quadrant; x++)
            {
                // Distance from the arc's centre to this texel's middle.
                float dx = Radius - (x + 0.5f);
                float dy = Radius - (y + 0.5f);
                float distance = MathF.Sqrt((dx * dx) + (dy * dy));

                // One texel of linear falloff across the edge is the cheapest coverage estimate that
                // reads as smooth at every size this is drawn at.
                float coverage = Math.Clamp(Radius - distance + 0.5f, 0f, 1f);
                pixels[((y * AtlasWidth) + originX + x)] = Bgra32.FromRgba(255, 255, 255, (byte)(coverage * 255f));
            }
        }
    }

    /// <summary>
    /// Writes a corner whose alpha falls off smoothly, for the shadow cast under a floating panel.
    /// </summary>
    /// <param name="pixels">The atlas being filled.</param>
    /// <param name="originX">The quadrant's left edge in the atlas.</param>
    /// <remarks>
    /// A smoothstep on the radius rather than a true Gaussian: it is one expression, it is monotonic,
    /// and at the sizes a shadow is drawn nobody can tell the difference between the two.
    /// </remarks>
    private static void WriteShadowCorner(Bgra32[] pixels, int originX)
    {
        const float Radius = Quadrant;

        for (int y = 0; y < Quadrant; y++)
        {
            for (int x = 0; x < Quadrant; x++)
            {
                float dx = Radius - (x + 0.5f);
                float dy = Radius - (y + 0.5f);
                float distance = MathF.Min(Radius, MathF.Sqrt((dx * dx) + (dy * dy)));

                float t = 1f - (distance / Radius);
                float falloff = t * t * (3f - (2f * t));
                pixels[((y * AtlasWidth) + originX + x)] = Bgra32.FromRgba(255, 255, 255, (byte)(Math.Clamp(falloff, 0f, 1f) * 255f));
            }
        }
    }
}
