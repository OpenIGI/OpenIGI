using OpenIGI.Core.Graphics;
using OpenIGI.Rendering;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Rhi;
using OpenIGI.UI.Text;

// In the framework's own namespace on purpose: game code that already imports OpenIGI.UI picks these up
// without a new using, so the engine's types keep working at every call site that predates the seam.
namespace OpenIGI.UI;

/// <summary>
/// Bridges between the interface framework's backend-neutral types and the game's graphics types, and
/// the game-side entry points: a painter over a sprite batch, a font or surface atlas on a device.
/// </summary>
public static class RhiUiExtensions
{
    /// <summary>Gets the device texture an interface image made by <see cref="RhiImageFactory"/> refers to.</summary>
    /// <param name="image">The image.</param>
    /// <returns>The texture handle.</returns>
    public static TextureHandle ToTextureHandle(this UiImage image) => new(image.Id);

    /// <summary>Wraps a device texture as an interface image.</summary>
    /// <param name="texture">The texture.</param>
    /// <returns>The image.</returns>
    public static UiImage ToUiImage(this TextureHandle texture) => new(texture.Id);

    /// <summary>Converts an interface rectangle to the renderer's.</summary>
    /// <param name="rect">The rectangle.</param>
    /// <returns>The same rectangle.</returns>
    public static RectF ToRectF(this UiRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    /// <summary>Converts a renderer rectangle to the interface's.</summary>
    /// <param name="rect">The rectangle.</param>
    /// <returns>The same rectangle.</returns>
    public static UiRect ToUiRect(this RectF rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    /// <summary>Converts an interface blend to the renderer's.</summary>
    /// <param name="blend">The blend.</param>
    /// <returns>The matching blend mode.</returns>
    public static BlendMode ToBlendMode(this UiBlend blend) => blend switch
    {
        UiBlend.Opaque => BlendMode.Opaque,
        UiBlend.Additive => BlendMode.Additive,
        _ => BlendMode.AlphaBlend,
    };

    /// <summary>Converts a renderer blend mode to the interface's.</summary>
    /// <param name="blend">The blend mode.</param>
    /// <returns>The matching blend; modes the interface does not have become alpha blending.</returns>
    public static UiBlend ToUiBlend(this BlendMode blend) => blend switch
    {
        BlendMode.Opaque => UiBlend.Opaque,
        BlendMode.Additive => UiBlend.Additive,
        _ => UiBlend.AlphaBlend,
    };

    /// <summary>Converts interface text effects to the SDF shader's parameters.</summary>
    /// <param name="effects">The effects.</param>
    /// <param name="pxRange">The font's distance-field range.</param>
    /// <returns>The shader parameters.</returns>
    public static SdfTextParams ToSdfTextParams(this UiTextEffects effects, float pxRange) =>
        new(pxRange, effects.OutlineColor, effects.OutlineWidth, effects.GlowColor, effects.GlowWidth, effects.GlowIntensity, effects.Dissolve, effects.DissolveSeed);

    /// <summary>Converts SDF shader parameters to interface text effects.</summary>
    /// <param name="parameters">The shader parameters; their range is the font's business and is dropped.</param>
    /// <returns>The effects.</returns>
    public static UiTextEffects ToUiTextEffects(this SdfTextParams parameters) =>
        new(parameters.OutlineColor, parameters.OutlineWidth, parameters.GlowColor, parameters.GlowWidth, parameters.GlowIntensity, parameters.Dissolve, parameters.DissolveSeed);

    /// <summary>Fills a rectangle with a solid colour.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="rect">The rectangle in pixels.</param>
    /// <param name="color">The fill colour.</param>
    /// <param name="blend">How the fill combines with what is behind it.</param>
    public static void FillRect(this UiPainter painter, UiRect rect, Bgra32 color, BlendMode blend)
    {
        ArgumentNullException.ThrowIfNull(painter);
        painter.FillRect(rect, color, blend.ToUiBlend());
    }

    /// <summary>Strokes a rectangle's outline.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="rect">The rectangle in pixels.</param>
    /// <param name="thickness">The stroke thickness in pixels.</param>
    /// <param name="color">The stroke colour.</param>
    /// <param name="blend">How the stroke combines with what is behind it.</param>
    public static void StrokeRect(this UiPainter painter, UiRect rect, float thickness, Bgra32 color, BlendMode blend)
    {
        ArgumentNullException.ThrowIfNull(painter);
        painter.StrokeRect(rect, thickness, color, blend.ToUiBlend());
    }

    /// <summary>Fills a rounded rectangle.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="rect">The rectangle in pixels.</param>
    /// <param name="slice">The corner sprite.</param>
    /// <param name="color">The fill colour.</param>
    /// <param name="radius">The corner radius in pixels.</param>
    /// <param name="blend">How the fill combines with what is behind it.</param>
    public static void FillRoundedRect(this UiPainter painter, UiRect rect, in NineSlice slice, Bgra32 color, float radius, BlendMode blend)
    {
        ArgumentNullException.ThrowIfNull(painter);
        painter.FillRoundedRect(rect, slice, color, radius, blend.ToUiBlend());
    }

    /// <summary>Draws a textured sprite from a device texture.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="texture">The texture to sample.</param>
    /// <param name="destination">The destination rectangle in pixels.</param>
    /// <param name="source">The source rectangle in texture coordinates.</param>
    /// <param name="color">The colour modulated with the texture.</param>
    /// <param name="blend">How the sprite combines with what is behind it.</param>
    public static void DrawSprite(
        this UiPainter painter,
        TextureHandle texture,
        UiRect destination,
        RectF source,
        Bgra32 color,
        BlendMode blend = BlendMode.AlphaBlend)
    {
        ArgumentNullException.ThrowIfNull(painter);
        painter.DrawSprite(texture.ToUiImage(), destination, source.ToUiRect(), color, blend.ToUiBlend());
    }

    /// <summary>Draws a string.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="font">The font to draw with.</param>
    /// <param name="text">The text.</param>
    /// <param name="topLeft">The top-left of the first line, in pixels.</param>
    /// <param name="sizePx">The text size in pixels.</param>
    /// <param name="color">The text colour.</param>
    /// <param name="blend">How the glyphs combine with what is behind them.</param>
    /// <returns>The pen position after the last glyph.</returns>
    public static UiVec2 DrawText(this UiPainter painter, UiFont font, ReadOnlySpan<char> text, UiVec2 topLeft, float sizePx, Bgra32 color, BlendMode blend)
    {
        ArgumentNullException.ThrowIfNull(painter);
        return painter.DrawText(font, text, topLeft, sizePx, color, blend.ToUiBlend());
    }

    /// <summary>Draws distance-field text with the SDF shader's own parameters.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="font">The SDF font to draw with.</param>
    /// <param name="text">The text.</param>
    /// <param name="topLeft">The top-left of the first line, in pixels.</param>
    /// <param name="sizePx">The text size in pixels.</param>
    /// <param name="color">The fill colour.</param>
    /// <param name="effects">The outline, glow and dissolve; the range is taken from the font.</param>
    /// <param name="blend">How the glyphs combine with what is behind them.</param>
    /// <returns>The pen position after the last glyph.</returns>
    public static UiVec2 DrawText(
        this UiPainter painter,
        UiFont font,
        ReadOnlySpan<char> text,
        UiVec2 topLeft,
        float sizePx,
        Bgra32 color,
        in SdfTextParams effects,
        BlendMode blend = BlendMode.AlphaBlend)
    {
        ArgumentNullException.ThrowIfNull(painter);
        return painter.DrawText(font, text, topLeft, sizePx, color, effects.ToUiTextEffects(), blend.ToUiBlend());
    }

    /// <summary>Draws a letterspaced string.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="font">The font to draw with.</param>
    /// <param name="text">The text.</param>
    /// <param name="topLeft">The top-left of the first line, in pixels.</param>
    /// <param name="sizePx">The text size in pixels.</param>
    /// <param name="color">The text colour.</param>
    /// <param name="tracking">Extra pixels added after each glyph.</param>
    /// <param name="blend">How the glyphs combine with what is behind them.</param>
    /// <returns>The pen position after the last glyph.</returns>
    public static UiVec2 DrawTextTracked(
        this UiPainter painter,
        UiFont font,
        ReadOnlySpan<char> text,
        UiVec2 topLeft,
        float sizePx,
        Bgra32 color,
        float tracking,
        BlendMode blend)
    {
        ArgumentNullException.ThrowIfNull(painter);
        return painter.DrawTextTracked(font, text, topLeft, sizePx, color, tracking, blend.ToUiBlend());
    }
}

/// <summary>Creates painters that draw into the game's sprite batch.</summary>
public static class RhiPainterExtensions
{
    extension(UiPainter)
    {
        /// <summary>Creates a painter that draws into the game's open sprite batch.</summary>
        /// <param name="batch">The open sprite batch.</param>
        /// <param name="context">The layout context for this frame.</param>
        /// <param name="backdrop">The blurred scene frosted glass samples, or <see langword="null"/>.</param>
        /// <returns>The painter.</returns>
        public static UiPainter Create(SpriteBatch batch, LayoutContext context, RhiBackdrop? backdrop = null) =>
            new(new RhiUiRenderer(batch) { Backdrop = backdrop }, context);
    }
}

/// <summary>Uploads font atlases to the game's graphics device.</summary>
public static class RhiFontExtensions
{
    extension(UiFont)
    {
        /// <summary>Uploads an atlas to the game's device and returns a font that can measure and draw.</summary>
        /// <param name="device">The device to upload to.</param>
        /// <param name="atlas">The rasterised atlas.</param>
        /// <returns>A drawable font.</returns>
        public static UiFont Upload(IGraphicsDevice device, FontAtlasData atlas) =>
            UiFont.Create(new RhiImageFactory(device), atlas);
    }
}

/// <summary>Uploads the rounded-corner and shadow atlas to the game's graphics device.</summary>
public static class RhiSurfaceExtensions
{
    extension(UiSurfaces)
    {
        /// <summary>Generates the rounded-corner and shadow atlas and uploads it to the game's device.</summary>
        /// <param name="device">The device to upload to.</param>
        /// <returns>The uploaded surfaces.</returns>
        public static UiSurfaces Create(IGraphicsDevice device) => UiSurfaces.Generate(new RhiImageFactory(device));
    }
}
