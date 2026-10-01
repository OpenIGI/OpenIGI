using OpenIGI.Core.Graphics;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;

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

    private readonly IUiImageFactory _images;
    private bool _disposed;

    private UiSurfaces(IUiImageFactory images, UiImage texture)
    {
        _images = images;
        Texture = texture;

        Rounded = new NineSlice(texture, new UiRect(0f, 0f, Quadrant / (float)AtlasWidth, Quadrant / (float)AtlasHeight), Quadrant);
        Shadow = new NineSlice(
            texture,
            new UiRect(Quadrant * 2 / (float)AtlasWidth, 0f, Quadrant / (float)AtlasWidth, Quadrant / (float)AtlasHeight),
            Quadrant);
    }

    /// <summary>Gets the generated atlas.</summary>
    public UiImage Texture { get; }

    /// <summary>Gets the anti-aliased rounded-corner quadrant.</summary>
    public NineSlice Rounded { get; }

    /// <summary>Gets the soft shadow-corner quadrant.</summary>
    public NineSlice Shadow { get; }

    /// <summary>Generates the atlas and uploads it.</summary>
    /// <param name="images">The factory that makes the atlas image and later releases it.</param>
    /// <returns>The uploaded surfaces.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="images"/> is <see langword="null"/>.</exception>
    public static UiSurfaces Generate(IUiImageFactory images)
    {
        ArgumentNullException.ThrowIfNull(images);

        Bgra32[] pixels = new Bgra32[AtlasWidth * AtlasHeight];
        WriteRoundedCorner(pixels, originX: 0);
        WriteShadowCorner(pixels, originX: Quadrant * 2);
        WriteInsideGuard(pixels, originX: 0);
        WriteInsideGuard(pixels, originX: Quadrant * 2);

        UiImage texture = images.CreateImage(AtlasWidth, AtlasHeight, pixels);
        return new UiSurfaces(images, texture);
    }

    /// <summary>Releases the atlas.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _images.DestroyImage(Texture);
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
    /// Continues each quadrant's solid inside past its edge, so filtering never samples off the shape.
    /// </summary>
    /// <param name="pixels">The atlas being filled.</param>
    /// <param name="originX">The quadrant's left edge in the atlas.</param>
    /// <remarks>
    /// <para>
    /// A quadrant is authored with the shape's outside at its top-left and its solid inside at the
    /// bottom-right, and a draw samples right up to that inside edge. Bilinear filtering there reaches
    /// one texel further, which without this lands in the empty atlas between the quadrants and halves
    /// the alpha along the seam.
    /// </para>
    /// <para>
    /// It goes unnoticed for an ordinary rounded panel, whose corners meet stretched edge strips carrying
    /// the same solid value. A figure whose radius is half its height — a circle, a capsule — has no
    /// edge strips at all: its four corners meet each other, and the seam draws a faint cross through
    /// the middle of the shape. Half the guard band is left clear so the neighbouring quadrant's own
    /// outside edge still filters into nothing.
    /// </para>
    /// </remarks>
    private static void WriteInsideGuard(Bgra32[] pixels, int originX)
    {
        const int Guard = Quadrant / 2;
        Bgra32 inside = Bgra32.FromRgba(255, 255, 255, 255);
        for (int y = 0; y < Quadrant + Guard; y++)
        {
            for (int x = 0; x < Quadrant + Guard; x++)
            {
                if (x < Quadrant && y < Quadrant)
                {
                    continue;
                }

                pixels[(y * AtlasWidth) + originX + x] = inside;
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
