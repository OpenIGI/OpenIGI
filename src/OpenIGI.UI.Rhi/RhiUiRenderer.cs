using OpenIGI.Core.Graphics;
using OpenIGI.Rendering;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;

namespace OpenIGI.UI.Rhi;

/// <summary>
/// The game's <see cref="IUiRenderer"/>: every quad and glyph the painter emits goes straight into the
/// engine's open <see cref="SpriteBatch"/>, in submission order.
/// </summary>
/// <remarks>
/// <para>
/// A thin adapter by design — it holds no state between frames and allocates nothing per draw, so the
/// game can wrap its batch in one every frame. A quad maps onto <see cref="SpriteBatch.DrawQuad"/>
/// one-to-one; a glyph onto the distance-field path when its font is a field (or effects are asked
/// for), else onto a plain textured quad, exactly as the painter drew before it had a seam.
/// </para>
/// <para>
/// Frosted glass samples <see cref="Backdrop"/>, the blurred pyramid of the scene the host captured
/// this frame. Without one the glass is its tint alone.
/// </para>
/// </remarks>
public sealed class RhiUiRenderer : IUiRenderer
{
    private readonly SpriteBatch _batch;

    /// <summary>Initializes a new instance of the <see cref="RhiUiRenderer"/> class.</summary>
    /// <param name="batch">The open sprite batch to draw into.</param>
    /// <exception cref="ArgumentNullException"><paramref name="batch"/> is <see langword="null"/>.</exception>
    public RhiUiRenderer(SpriteBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        _batch = batch;
    }

    /// <summary>Gets or sets the blurred scene frosted glass samples, or <see langword="null"/> for none.</summary>
    public RhiBackdrop? Backdrop { get; set; }

    /// <inheritdoc/>
    public void DrawQuad(
        UiImage image,
        in UiVertex topLeft,
        in UiVertex topRight,
        in UiVertex bottomRight,
        in UiVertex bottomLeft,
        UiBlend blend) =>
        _batch.DrawQuad(
            image.ToTextureHandle(),
            ToVertex(topLeft),
            ToVertex(topRight),
            ToVertex(bottomRight),
            ToVertex(bottomLeft),
            blend.ToBlendMode());

    /// <inheritdoc/>
    public void DrawGlyph(UiFont font, in UiGlyph glyph, Bgra32 color, in UiTextEffects? effects, UiBlend blend)
    {
        ArgumentNullException.ThrowIfNull(font);
        TextureHandle atlas = font.Texture.ToTextureHandle();
        if (!atlas.IsValid)
        {
            return;
        }

        RectF destination = glyph.Destination.ToRectF();
        RectF source = glyph.Source.ToRectF();
        if (effects is { } decorated)
        {
            _batch.DrawText(atlas, destination, source, color, decorated.ToSdfTextParams(font.Atlas.SdfPadding), blend.ToBlendMode());
        }
        else if (font.Kind == FontAtlasKind.SignedDistanceField)
        {
            _batch.DrawText(atlas, destination, source, color, SdfTextParams.Plain(font.Atlas.SdfPadding), blend.ToBlendMode());
        }
        else
        {
            _batch.Draw(atlas, destination, source, color, blend.ToBlendMode());
        }
    }

    /// <inheritdoc/>
    public void DrawBackdrop(ReadOnlySpan<UiVec2> polygon, float sigmaPx, Bgra32 tint, float opacity, float zoom)
    {
        if (polygon.Length < 3)
        {
            return;
        }

        if (Backdrop is { } backdrop && backdrop.TryPick(sigmaPx, out RhiBackdropLevel lower, out RhiBackdropLevel upper, out float blend))
        {
            // Below the sharpest level's blur there is nothing finer to sample, so the glass fades in over
            // the sharp frame instead: a blur easing up from zero grows rather than jumping.
            float partial = lower.SigmaPx <= 0f ? 1f : Math.Clamp(sigmaPx / lower.SigmaPx, 0f, 1f);
            byte glass = (byte)(Math.Clamp(opacity * partial, 0f, 1f) * 255f);
            if (glass > 0)
            {
                Fan(polygon, lower.Texture, backdrop, zoom, Bgra32.White.WithAlpha(glass), BlendMode.AlphaBlend);
                if (blend > 0.004f && upper.Texture != lower.Texture)
                {
                    Fan(polygon, upper.Texture, backdrop, zoom, Bgra32.White.WithAlpha((byte)(blend * glass)), BlendMode.AlphaBlend);
                }
            }
        }

        if (tint.A > 0)
        {
            Fan(polygon, TextureHandle.None, null, 1f, tint, BlendMode.AlphaBlend);
        }
    }

    private void Fan(ReadOnlySpan<UiVec2> polygon, TextureHandle texture, RhiBackdrop? screen, float zoom, Bgra32 color, BlendMode blend)
    {
        int n = polygon.Length;
        for (int i = 1; i < n - 1; i += 2)
        {
            UiVec2 c = polygon[i + 1];
            UiVec2 d = i + 2 < n ? polygon[i + 2] : c;
            _batch.DrawQuad(
                texture,
                ScreenVertex(polygon[0], screen, zoom, color),
                ScreenVertex(polygon[i], screen, zoom, color),
                ScreenVertex(c, screen, zoom, color),
                ScreenVertex(d, screen, zoom, color),
                blend);
        }
    }

    // The texture coordinate is the point's place on the screen, pulled towards the centre by the zoom, so
    // the scene behind is magnified about the middle of the screen as a camera pushing in would.
    private static Vertex2D ScreenVertex(UiVec2 p, RhiBackdrop? screen, float zoom, Bgra32 color) =>
        screen is null
            ? new Vertex2D(p.X, p.Y, 0f, 0f, color)
            : new Vertex2D(p.X, p.Y, 0.5f + (((p.X / screen.Width) - 0.5f) / zoom), 0.5f + (((p.Y / screen.Height) - 0.5f) / zoom), color);

    private static Vertex2D ToVertex(in UiVertex v) => new(v.X, v.Y, v.U, v.V, v.Color);
}
