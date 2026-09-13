using OpenIGI.Core.Graphics;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Layout;

namespace OpenIGI.Desktop;

/// <summary>
/// The interaction marker's key badge, generated once as a small antialiased sprite.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it is baked rather than drawn.</b> The painter has no antialiased line and no blur, so a
/// badge assembled at draw time is a stack of filled shapes, each covering the middle of the one
/// before it. That composes correctly only at full opacity: the marker fades in and out, every layer's
/// alpha is scaled by the fade, and a face that no longer covers what is under it leaves the rim
/// showing through as a lighter middle — with a hard seam wherever the light along the top was clipped.
/// Baked, the whole badge is one image with one alpha, so the fade multiplies it uniformly and the
/// figure is identical at every opacity.
/// </para>
/// <para>
/// It also buys the light. The rim brightens towards the top by the angle of each texel rather than by
/// clipping bands across the shape, which is a gradient no combination of filled rectangles can draw.
/// </para>
/// <para>
/// Two quadrants are stored, not one: the badge is symmetrical left to right but not top to bottom, so
/// the upper half is mirrored horizontally into two corners and the lower half into the other two. A
/// key cap stretches the column through each quadrant's inner edge across its straight middle, exactly
/// as <see cref="NineSlice"/> does — that column is the shape's vertical profile and is constant along
/// a straight edge.
/// </para>
/// </remarks>
internal sealed class InteractionBadgeSprite : IDisposable
{
    /// <summary>The sprite's outer radius as a multiple of the rim's radius.</summary>
    /// <remarks>
    /// The dark separation edge lives outside the rim, so the drawn figure is wider than the badge the
    /// caller asks for. Callers size the rim and this converts.
    /// </remarks>
    public const float OuterScale = 1.09f;

    /// <summary>The quadrant's size in texels.</summary>
    /// <remarks>
    /// Forty-eight carries the rim and the light inside it cleanly at the sizes a badge is drawn — a
    /// little over 40 pixels across at 1080p and about twice that at 4K — and the atlas is 32 KB.
    /// </remarks>
    private const int Quadrant = 48;

    /// <summary>How far each quadrant's inside is continued past its edge, for filtering.</summary>
    private const int Guard = 8;

    /// <summary>How many samples a texel is averaged from, along each axis.</summary>
    /// <remarks>
    /// Eight by eight gives sixty-five levels of coverage on an edge, which is finer than the eye can
    /// separate at any size this is drawn, and the whole atlas is a third of a million samples — a few
    /// milliseconds, once, while the mission's fonts are loading.
    /// </remarks>
    private const int Samples = 8;

    /// <summary>The generated atlas's width in texels.</summary>
    internal const int AtlasWidth = 128;

    /// <summary>The generated atlas's height in texels.</summary>
    internal const int AtlasHeight = 64;

    /// <summary>The rim's outer radius in texels.</summary>
    private const float RimOuter = 44f;

    /// <summary>The rim's inner radius in texels.</summary>
    private const float RimInner = 39f;

    /// <summary>The outer radius of the light band inside the rim.</summary>
    private const float GlowOuter = 37.4f;

    /// <summary>The inner radius of the light band inside the rim.</summary>
    private const float GlowInner = 35.8f;

    private readonly IGraphicsDevice _device;
    private readonly TextureHandle _texture;
    private bool _disposed;

    private InteractionBadgeSprite(IGraphicsDevice device, TextureHandle texture)
    {
        _device = device;
        _texture = texture;
    }

    /// <summary>Generates the badge atlas and uploads it.</summary>
    /// <param name="device">The device to upload to.</param>
    /// <returns>The uploaded sprite.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> is <see langword="null"/>.</exception>
    public static InteractionBadgeSprite Create(IGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        Bgra32[] pixels = BuildPixels();
        TextureHandle texture = device.CreateTexture(new TextureDescription(AtlasWidth, AtlasHeight), pixels);
        return new InteractionBadgeSprite(device, texture);
    }

    /// <summary>Generates the atlas's texels without uploading them.</summary>
    /// <returns>The atlas, row by row.</returns>
    /// <remarks>Separate from <see cref="Create"/> so the badge can be inspected without a device.</remarks>
    internal static Bgra32[] BuildPixels()
    {
        Bgra32[] pixels = new Bgra32[AtlasWidth * AtlasHeight];
        WriteQuadrant(pixels, originX: 0, upper: true);
        WriteQuadrant(pixels, originX: Quadrant + (Guard * 2), upper: false);
        return pixels;
    }

    /// <summary>Draws the badge onto a surface standing in the level.</summary>
    /// <param name="canvas">The canvas the badge stands on.</param>
    /// <param name="batch">The open sprite batch.</param>
    /// <param name="centreX">The badge's centre in canvas units.</param>
    /// <param name="centreY">The badge's centre in canvas units.</param>
    /// <param name="halfWidth">Half the badge's width through its round ends.</param>
    /// <param name="radius">The rim's radius in canvas units.</param>
    /// <param name="colour">The tint, which carries the marker's fade.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <remarks>The same six pieces <see cref="Draw"/> lays in pixels, laid on the canvas instead.</remarks>
    public void Draw(
        in WorldCanvas canvas,
        SpriteBatch batch,
        float centreX,
        float centreY,
        float halfWidth,
        float radius,
        Bgra32 colour)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (radius <= 0f)
        {
            return;
        }

        float outer = radius * OuterScale;
        float straight = MathF.Max(0f, halfWidth - radius);

        canvas.Draw(batch, _texture, centreX - straight - outer, centreY - outer, outer, outer, Corner(upper: true, mirrored: false), colour);
        canvas.Draw(batch, _texture, centreX + straight, centreY - outer, outer, outer, Corner(upper: true, mirrored: true), colour);
        canvas.Draw(batch, _texture, centreX - straight - outer, centreY, outer, outer, Corner(upper: false, mirrored: false), colour);
        canvas.Draw(batch, _texture, centreX + straight, centreY, outer, outer, Corner(upper: false, mirrored: true), colour);

        if (straight <= 0f)
        {
            return;
        }

        canvas.Draw(
            batch, _texture, centreX - straight, centreY - outer, straight * 2f, outer,
            InnerColumn(upper: true), colour);
        canvas.Draw(
            batch, _texture, centreX - straight, centreY, straight * 2f, outer,
            InnerColumn(upper: false), colour);
    }

    /// <summary>Releases the atlas.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _device.DestroyTexture(_texture);
    }

    /// <summary>One of the four corners' source rectangles, mirrored out of the stored quadrant.</summary>
    private static RectF Corner(bool upper, bool mirrored)
    {
        float originX = (upper ? 0f : Quadrant + (Guard * 2)) / AtlasWidth;
        float width = Quadrant / (float)AtlasWidth;
        float height = Quadrant / (float)AtlasHeight;
        return new RectF(
            mirrored ? originX + width : originX,
            0f,
            mirrored ? -width : width,
            height);
    }

    /// <summary>The column through a quadrant's inner edge, which a key cap's straight middle stretches.</summary>
    /// <remarks>
    /// Taken through the middle of that texel and given no width at all, so the stretch samples one
    /// value across its whole length rather than reaching half a texel into the guard beside it.
    /// </remarks>
    private static RectF InnerColumn(bool upper)
    {
        float originX = (upper ? 0f : Quadrant + (Guard * 2)) / AtlasWidth;
        float texel = 1f / AtlasWidth;
        return new RectF(
            originX + (Quadrant / (float)AtlasWidth) - (texel * 0.5f),
            0f,
            0f,
            Quadrant / (float)AtlasHeight);
    }

    /// <summary>
    /// Writes one half of the badge: the dark separation edge, the rim, the light inside it and the face.
    /// </summary>
    /// <param name="pixels">The atlas being filled.</param>
    /// <param name="originX">The quadrant's left edge in the atlas.</param>
    /// <param name="upper">Whether this is the upper half, which carries the light along the rim.</param>
    /// <remarks>
    /// The quadrant is authored with the badge's centre at its inner corner — bottom-right for the
    /// upper half, top-right for the lower — so the two mirror horizontally into four corners without
    /// the light ever ending up on the wrong side.
    /// </remarks>
    private static void WriteQuadrant(Bgra32[] pixels, int originX, bool upper)
    {
        for (int y = 0; y < Quadrant + Guard; y++)
        {
            for (int x = 0; x < Quadrant + Guard; x++)
            {
                // Averaged over a grid of samples inside the texel, in premultiplied alpha. This is the
                // same thing an image tool does when it renders a shape large and scales it down, and it
                // is why no edge in the badge — the silhouette, the rim, or the light inside it — has to
                // be approximated by a coverage formula of its own.
                float red = 0f;
                float green = 0f;
                float blue = 0f;
                float alpha = 0f;
                for (int sy = 0; sy < Samples; sy++)
                {
                    for (int sx = 0; sx < Samples; sx++)
                    {
                        float px = x + ((sx + 0.5f) / Samples);
                        float py = y + ((sy + 0.5f) / Samples);
                        (float sampleRed, float sampleGreen, float sampleBlue, float sampleAlpha) =
                            SampleBadge(px, py, upper);
                        red += sampleRed * sampleAlpha;
                        green += sampleGreen * sampleAlpha;
                        blue += sampleBlue * sampleAlpha;
                        alpha += sampleAlpha;
                    }
                }

                const float Total = Samples * Samples;
                alpha /= Total;
                if (alpha <= 0.0001f)
                {
                    pixels[(y * AtlasWidth) + originX + x] = default;
                    continue;
                }

                red /= Total * alpha;
                green /= Total * alpha;
                blue /= Total * alpha;
                pixels[(y * AtlasWidth) + originX + x] = Bgra32.FromRgba(
                    (byte)Math.Clamp(red * 255f, 0f, 255f),
                    (byte)Math.Clamp(green * 255f, 0f, 255f),
                    (byte)Math.Clamp(blue * 255f, 0f, 255f),
                    (byte)Math.Clamp(alpha * 255f, 0f, 255f));
            }
        }
    }

    /// <summary>The badge's colour at one point of a quadrant, with hard edges between its bands.</summary>
    /// <param name="x">The horizontal position in texels, measured from the quadrant's outer corner.</param>
    /// <param name="y">The vertical position in texels.</param>
    /// <param name="upper">Whether this is the upper half, which carries the light along the rim.</param>
    /// <returns>The colour and coverage at that point, in unit range.</returns>
    /// <remarks>
    /// Deliberately hard-edged. Every boundary here is resolved by the sampling grid above, and a shape
    /// that softens its own edges as well ends up soft twice — which is the difference between a rim
    /// that looks drawn and one that looks blurred.
    /// </remarks>
    private static (float Red, float Green, float Blue, float Alpha) SampleBadge(float x, float y, bool upper)
    {
        // Inside the guard the shape is its own middle continued, so filtering at a quadrant's inner
        // edge never samples the empty atlas beside it.
        float dx = MathF.Max(0f, Quadrant - x);
        float dy = MathF.Max(0f, upper ? Quadrant - y : y);
        float distance = MathF.Sqrt((dx * dx) + (dy * dy));
        if (distance > Quadrant)
        {
            return (0f, 0f, 0f, 0f);
        }

        // How nearly straight up this point lies from the centre, which is where the rim is lit.
        float up = distance > 0.001f && upper ? MathF.Max(0f, dy / distance) : 0f;
        float light = MathF.Pow(up, 1.6f);

        if (distance > RimOuter)
        {
            return (0f, 0f, 0f, 116f / 255f);
        }

        if (distance > RimInner)
        {
            float tint = (245f + (10f * light)) / 255f;
            return (tint, MathF.Min(1f, tint + 0.012f), MathF.Min(1f, tint + 0.02f), (236f + (19f * light)) / 255f);
        }

        // The face, with the light inside the rim laid over it where the rim itself is lit.
        float face = 1f;
        (float red, float green, float blue) = (13f / 255f, 16f / 255f, 21f / 255f);
        if (distance > GlowInner && distance < GlowOuter)
        {
            float glow = (96f / 255f) * light;
            red = (red * (1f - glow)) + glow;
            green = (green * (1f - glow)) + glow;
            blue = (blue * (1f - glow)) + glow;
        }

        return (red, green, blue, face);
    }

}
