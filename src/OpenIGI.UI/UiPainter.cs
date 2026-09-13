using OpenIGI.Core.Graphics;
using OpenIGI.Rendering;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;

namespace OpenIGI.UI;

/// <summary>
/// The interface's drawing surface: rectangles, sprites and text emitted into an open
/// <see cref="SpriteBatch"/>, with a clip stack for scroll views.
/// </summary>
/// <remarks>
/// <para>
/// Everything funnels through the one <see cref="SpriteBatch"/> the game already uses, so UI draws
/// interleave with the HUD and the console in submission order exactly as the original's painter's
/// algorithm expects. The caller opens and closes the batch around a frame's worth of screens; the
/// painter never does.
/// </para>
/// <para>
/// <b>Clipping is done on the CPU.</b> The 2D seam has no scissor — <see cref="OpenIGI.Rendering.IGraphicsDevice.SetViewport"/>
/// would remap the coordinate space, not just mask it — so a clip rectangle is applied by trimming each
/// quad to it and adjusting its texture coordinates in proportion. That is enough for axis-aligned UI:
/// a scroll view pushes a clip, its rows draw trimmed, and it pops. It costs a little arithmetic per
/// quad and keeps the whole path inside the sprite batch, which is what lets the draw output be asserted
/// through <c>RecordingGraphicsDevice</c> without a GPU.
/// </para>
/// </remarks>
public sealed class UiPainter
{
    private readonly SpriteBatch _batch;
    private readonly Stack<UiRect> _clips = new();
    private readonly Stack<float> _opacityStack = new();
    private float _opacity = 1f;

    /// <summary>Initializes a new instance of the <see cref="UiPainter"/> class.</summary>
    /// <param name="batch">The open sprite batch to draw into.</param>
    /// <param name="context">The layout context mapping design units to this back buffer.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public UiPainter(SpriteBatch batch, LayoutContext context)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(context);
        _batch = batch;
        Context = context;
    }

    /// <summary>Gets the layout context for this frame.</summary>
    public LayoutContext Context { get; }

    /// <summary>Gets the current clip rectangle, the whole viewport when nothing is pushed.</summary>
    public UiRect Clip => _clips.Count > 0 ? _clips.Peek() : new UiRect(0f, 0f, Context.Viewport.X, Context.Viewport.Y);

    /// <summary>Gets the current opacity multiplier applied to every drawn colour's alpha.</summary>
    public float Opacity => _opacity;

    /// <summary>Multiplies the current opacity for subsequent draws — the screen fade a transition uses.</summary>
    /// <param name="opacity">The factor to multiply in, 0 transparent to 1 opaque.</param>
    public void PushOpacity(float opacity)
    {
        _opacityStack.Push(_opacity);
        _opacity = Math.Clamp(_opacity * opacity, 0f, 1f);
    }

    /// <summary>Restores the opacity in force before the matching <see cref="PushOpacity"/>.</summary>
    /// <exception cref="InvalidOperationException">No opacity is pushed.</exception>
    public void PopOpacity()
    {
        if (_opacityStack.Count == 0)
        {
            throw new InvalidOperationException("PopOpacity was called without a matching PushOpacity.");
        }

        _opacity = _opacityStack.Pop();
    }

    private Bgra32 Fade(Bgra32 color) =>
        _opacity >= 1f ? color : color.WithAlpha((byte)(color.A * _opacity));

    /// <summary>Restricts drawing to the intersection of the current clip and a rectangle.</summary>
    /// <param name="rect">The rectangle to clip to.</param>
    public void PushClip(UiRect rect) => _clips.Push(_clips.Count > 0 ? _clips.Peek().Intersect(rect) : rect);

    /// <summary>Restores the clip in force before the matching <see cref="PushClip"/>.</summary>
    /// <exception cref="InvalidOperationException">No clip is pushed.</exception>
    public void PopClip()
    {
        if (_clips.Count == 0)
        {
            throw new InvalidOperationException("PopClip was called without a matching PushClip.");
        }

        _clips.Pop();
    }

    /// <summary>Fills a rectangle with a solid colour.</summary>
    /// <param name="rect">The rectangle in pixels.</param>
    /// <param name="color">The fill colour.</param>
    /// <param name="blend">How the fill combines with what is behind it.</param>
    public void FillRect(UiRect rect, Bgra32 color, BlendMode blend = BlendMode.AlphaBlend)
    {
        UiRect dest = rect;
        RectF source = RectF.Unit;
        if (TryClip(Clip, ref dest, ref source))
        {
            _batch.Draw(TextureHandle.None, dest.ToRectF(), source, Fade(color), blend);
        }
    }

    /// <summary>Strokes a rectangle's outline with four thin fills.</summary>
    /// <param name="rect">The rectangle in pixels.</param>
    /// <param name="thickness">The stroke thickness in pixels.</param>
    /// <param name="color">The stroke colour.</param>
    /// <param name="blend">How the stroke combines with what is behind it.</param>
    public void StrokeRect(UiRect rect, float thickness, Bgra32 color, BlendMode blend = BlendMode.AlphaBlend)
    {
        if (thickness <= 0f)
        {
            return;
        }

        FillRect(new UiRect(rect.X, rect.Y, rect.Width, thickness), color, blend);
        FillRect(new UiRect(rect.X, rect.Bottom - thickness, rect.Width, thickness), color, blend);
        FillRect(new UiRect(rect.X, rect.Y, thickness, rect.Height), color, blend);
        FillRect(new UiRect(rect.Right - thickness, rect.Y, thickness, rect.Height), color, blend);
    }

    /// <summary>Fills a rectangle whose corners are rounded, using a generated corner sprite.</summary>
    /// <param name="rect">The rectangle in pixels.</param>
    /// <param name="slice">The corner sprite, normally <see cref="UiSurfaces.Rounded"/>.</param>
    /// <param name="color">The fill colour.</param>
    /// <param name="radius">The corner radius in pixels; clamped so it can never exceed half the rectangle.</param>
    /// <param name="blend">How the fill combines with what is behind it.</param>
    /// <remarks>
    /// <para>
    /// Nine quads: four mirrored corners, four stretched edges and a flat middle. Every one of them
    /// goes through the ordinary clipped paths, so a rounded panel inside a scroll view is trimmed
    /// correctly rather than spilling — the CPU clip earns its keep here.
    /// </para>
    /// <para>
    /// Falls back to a plain <see cref="FillRect"/> when the slice is missing or the radius is zero, so
    /// a caller never has to check whether the atlas was built.
    /// </para>
    /// </remarks>
    public void FillRoundedRect(
        UiRect rect,
        in NineSlice slice,
        Bgra32 color,
        float radius,
        BlendMode blend = BlendMode.AlphaBlend)
    {
        if (!slice.IsValid || radius <= 0.5f || rect.IsEmpty)
        {
            FillRect(rect, color, blend);
            return;
        }

        Span<NineSlicePiece> pieces = stackalloc NineSlicePiece[NineSlice.MaxPieces];
        int count = slice.GetPieces(rect, radius, pieces);
        if (count == 0)
        {
            FillRect(rect, color, blend);
            return;
        }

        for (int piece = 0; piece < count; piece++)
        {
            DrawSprite(slice.Texture, pieces[piece].Destination, pieces[piece].Source, color, blend);
        }
    }

    /// <summary>Draws a soft shadow behind a rectangle.</summary>
    /// <param name="rect">The rectangle casting the shadow, in pixels.</param>
    /// <param name="slice">The shadow corner sprite, normally <see cref="UiSurfaces.Shadow"/>.</param>
    /// <param name="color">The shadow colour, normally translucent black.</param>
    /// <param name="spread">How far the shadow reaches beyond the rectangle, in pixels.</param>
    /// <param name="offset">How far the shadow is displaced, normally a little downwards.</param>
    /// <remarks>
    /// The one cue that separates a floating window from a panel painted onto the background, and the
    /// only depth the interface has — there is no blur to fake it with, so it is drawn.
    /// </remarks>
    public void DrawShadow(
        UiRect rect,
        in NineSlice slice,
        Bgra32 color,
        float spread,
        UiVec2 offset = default)
    {
        if (!slice.IsValid || spread <= 0f || rect.IsEmpty)
        {
            return;
        }

        UiRect outer = new(
            rect.X - spread + offset.X,
            rect.Y - spread + offset.Y,
            rect.Width + (spread * 2f),
            rect.Height + (spread * 2f));

        FillRoundedRect(outer, slice, color, spread);
    }

    /// <summary>Draws a string with extra space between its glyphs.</summary>
    /// <param name="font">The font to draw with.</param>
    /// <param name="text">The text; a line feed starts a new line.</param>
    /// <param name="topLeft">The top-left of the first line, in pixels.</param>
    /// <param name="sizePx">The text size in pixels.</param>
    /// <param name="color">The text colour.</param>
    /// <param name="tracking">Extra pixels added after each glyph.</param>
    /// <param name="blend">How the glyphs combine with what is behind them.</param>
    /// <returns>The pen position after the last glyph, in pixels.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="font"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Small uppercase headings need letterspacing to stop reading as a solid block, and it is the one
    /// typographic control the plain draw path cannot express. Drawn glyph by glyph rather than by
    /// scaling, so the letterforms keep their weight.
    /// </remarks>
    public UiVec2 DrawTextTracked(
        UiFont font,
        ReadOnlySpan<char> text,
        UiVec2 topLeft,
        float sizePx,
        Bgra32 color,
        float tracking,
        BlendMode blend = BlendMode.AlphaBlend)
    {
        ArgumentNullException.ThrowIfNull(font);

        if (tracking == 0f)
        {
            return DrawText(font, text, topLeft, sizePx, color, blend);
        }

        UiVec2 pen = topLeft;
        Span<char> one = stackalloc char[1];

        foreach (char c in text)
        {
            one[0] = c;
            DrawText(font, one, pen, sizePx, color, blend);
            pen = new UiVec2(pen.X + (font.GetGlyph(c).Advance * font.ScaleFor(sizePx)) + tracking, pen.Y);
        }

        return pen;
    }

    /// <summary>Measures a string drawn with extra space between its glyphs.</summary>
    /// <param name="font">The font that would draw it.</param>
    /// <param name="text">The text.</param>
    /// <param name="sizePx">The text size in pixels.</param>
    /// <param name="tracking">Extra pixels added after each glyph.</param>
    /// <returns>The width in pixels.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="font"/> is <see langword="null"/>.</exception>
    public static float MeasureTracked(UiFont font, ReadOnlySpan<char> text, float sizePx, float tracking)
    {
        ArgumentNullException.ThrowIfNull(font);
        return font.MeasureWidth(text, sizePx) + (tracking * text.Length);
    }

    /// <summary>Fills a triangle, unclipped — for small overlay shapes such as the mouse cursor.</summary>
    /// <param name="a">The first corner.</param>
    /// <param name="b">The second corner.</param>
    /// <param name="c">The third corner.</param>
    /// <param name="color">The fill colour.</param>
    /// <remarks>
    /// Emitted as a degenerate quad (its last two corners coincide), so it rides the same batch as every
    /// other 2D draw. It does not honour the clip stack; the cursor is always on screen and never inside
    /// a scroll view.
    /// </remarks>
    public void FillTriangle(UiVec2 a, UiVec2 b, UiVec2 c, Bgra32 color)
    {
        Bgra32 faded = Fade(color);
        _batch.DrawQuad(
            TextureHandle.None,
            new Vertex2D(a.X, a.Y, 0f, 0f, faded),
            new Vertex2D(b.X, b.Y, 0f, 0f, faded),
            new Vertex2D(c.X, c.Y, 0f, 0f, faded),
            new Vertex2D(c.X, c.Y, 0f, 0f, faded),
            BlendMode.AlphaBlend);
    }

    /// <summary>Fills an arbitrary convex quad, unclipped.</summary>
    /// <remarks>
    /// This is the quad counterpart to <see cref="FillTriangle"/>. It is intended for display-space
    /// markers whose native geometry is rotated around a tracked target, rather than for normal UI
    /// panels which should continue to use <see cref="FillRect"/> and honour the clip stack.
    /// </remarks>
    public void FillQuad(UiVec2 a, UiVec2 b, UiVec2 c, UiVec2 d, Bgra32 color)
    {
        Bgra32 faded = Fade(color);
        _batch.DrawQuad(
            TextureHandle.None,
            new Vertex2D(a.X, a.Y, 0f, 0f, faded),
            new Vertex2D(b.X, b.Y, 1f, 0f, faded),
            new Vertex2D(c.X, c.Y, 1f, 1f, faded),
            new Vertex2D(d.X, d.Y, 0f, 1f, faded),
            BlendMode.AlphaBlend);
    }

    /// <summary>Draws a textured sprite.</summary>
    /// <param name="texture">The texture to sample.</param>
    /// <param name="destination">The destination rectangle in pixels.</param>
    /// <param name="source">The source rectangle in texture coordinates.</param>
    /// <param name="color">The colour modulated with the texture.</param>
    /// <param name="blend">How the sprite combines with what is behind it.</param>
    public void DrawSprite(
        TextureHandle texture,
        UiRect destination,
        RectF source,
        Bgra32 color,
        BlendMode blend = BlendMode.AlphaBlend)
    {
        UiRect dest = destination;
        RectF src = source;
        if (TryClip(Clip, ref dest, ref src))
        {
            _batch.Draw(texture, dest.ToRectF(), src, Fade(color), blend);
        }
    }

    /// <summary>Draws a string with its top-left at a point.</summary>
    /// <param name="font">The font to draw with.</param>
    /// <param name="text">The text; a line feed starts a new line at the original left edge.</param>
    /// <param name="topLeft">The top-left of the first line, in pixels.</param>
    /// <param name="sizePx">The text size in pixels.</param>
    /// <param name="color">The text colour.</param>
    /// <param name="blend">How the glyphs combine with what is behind them.</param>
    /// <returns>The pen position after the last glyph, in pixels.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="font"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The font has no atlas texture (it is measurement-only).</exception>
    public UiVec2 DrawText(
        UiFont font,
        ReadOnlySpan<char> text,
        UiVec2 topLeft,
        float sizePx,
        Bgra32 color,
        BlendMode blend = BlendMode.AlphaBlend)
    {
        ArgumentNullException.ThrowIfNull(font);

        // A distance-field font routes through the SDF program with plain fill; a coverage font draws as
        // an ordinary textured quad. Either way the caller just asks for text.
        if (font.Kind == FontAtlasKind.SignedDistanceField)
        {
            return DrawTextCore(font, text, topLeft, sizePx, color, blend, true, SdfTextParams.Plain(font.Atlas.SdfPadding));
        }

        return DrawTextCore(font, text, topLeft, sizePx, color, blend, false, default);
    }

    /// <summary>Draws distance-field text with an outline and glow, for a title or a heading.</summary>
    /// <param name="font">The SDF font to draw with.</param>
    /// <param name="text">The text; a line feed starts a new line.</param>
    /// <param name="topLeft">The top-left of the first line, in pixels.</param>
    /// <param name="sizePx">The text size in pixels.</param>
    /// <param name="color">The fill colour.</param>
    /// <param name="effects">The outline and glow; its <see cref="SdfTextParams.PxRange"/> is overridden by the font's.</param>
    /// <param name="blend">How the glyphs combine with what is behind them.</param>
    /// <returns>The pen position after the last glyph, in pixels.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="font"/> is <see langword="null"/>.</exception>
    public UiVec2 DrawText(
        UiFont font,
        ReadOnlySpan<char> text,
        UiVec2 topLeft,
        float sizePx,
        Bgra32 color,
        in SdfTextParams effects,
        BlendMode blend = BlendMode.AlphaBlend)
    {
        ArgumentNullException.ThrowIfNull(font);
        SdfTextParams withRange = effects with { PxRange = font.Atlas.SdfPadding };
        return DrawTextCore(font, text, topLeft, sizePx, color, blend, true, withRange);
    }

    private UiVec2 DrawTextCore(
        UiFont font,
        ReadOnlySpan<char> text,
        UiVec2 topLeft,
        float sizePx,
        Bgra32 color,
        BlendMode blend,
        bool useSdf,
        in SdfTextParams parameters)
    {
        if (!font.Texture.IsValid)
        {
            throw new InvalidOperationException("This font was built for measurement only and cannot draw.");
        }

        UiRect clip = Clip;
        var run = new GlyphRun(font, text, sizePx, topLeft);
        while (run.MoveNext())
        {
            UiRect dest = run.Current.Destination;
            RectF source = run.Current.Source;
            if (!TryClip(clip, ref dest, ref source))
            {
                continue;
            }

            Bgra32 faded = Fade(color);
            if (useSdf)
            {
                _batch.DrawText(font.Texture, dest.ToRectF(), source, faded, parameters, blend);
            }
            else
            {
                _batch.Draw(font.Texture, dest.ToRectF(), source, faded, blend);
            }
        }

        return run.Pen;
    }

    /// <summary>Draws a string with a soft drop shadow, then the text on top.</summary>
    /// <param name="font">The font to draw with.</param>
    /// <param name="text">The text.</param>
    /// <param name="topLeft">The top-left of the first line, in pixels.</param>
    /// <param name="sizePx">The text size in pixels.</param>
    /// <param name="color">The text colour.</param>
    /// <param name="shadow">The shadow colour, typically translucent black.</param>
    /// <param name="offset">The shadow offset in pixels.</param>
    /// <exception cref="ArgumentNullException"><paramref name="font"/> is <see langword="null"/>.</exception>
    public void DrawTextShadowed(
        UiFont font,
        ReadOnlySpan<char> text,
        UiVec2 topLeft,
        float sizePx,
        Bgra32 color,
        Bgra32 shadow,
        UiVec2 offset)
    {
        DrawText(font, text, topLeft + offset, sizePx, shadow);
        DrawText(font, text, topLeft, sizePx, color);
    }

    /// <summary>Trims a destination rectangle to a clip, adjusting its source rectangle to match.</summary>
    /// <param name="clip">The clip rectangle.</param>
    /// <param name="dest">The destination, trimmed in place.</param>
    /// <param name="source">The source, trimmed in proportion in place.</param>
    /// <returns><see langword="false"/> when the destination lies wholly outside the clip.</returns>
    internal static bool TryClip(UiRect clip, ref UiRect dest, ref RectF source)
    {
        if (dest.Width <= 0f || dest.Height <= 0f)
        {
            return false;
        }

        float left = MathF.Max(dest.X, clip.X);
        float top = MathF.Max(dest.Y, clip.Y);
        float right = MathF.Min(dest.Right, clip.Right);
        float bottom = MathF.Min(dest.Bottom, clip.Bottom);
        if (right <= left || bottom <= top)
        {
            return false;
        }

        // Nothing to trim: leave the source untouched so an unclipped draw is bit-for-bit what it was.
        if (left == dest.X && top == dest.Y && right == dest.Right && bottom == dest.Bottom)
        {
            return true;
        }

        float uPerPx = source.Width / dest.Width;
        float vPerPx = source.Height / dest.Height;
        source = new RectF(
            source.X + ((left - dest.X) * uPerPx),
            source.Y + ((top - dest.Y) * vPerPx),
            (right - left) * uPerPx,
            (bottom - top) * vPerPx);
        dest = UiRect.FromEdges(left, top, right, bottom);
        return true;
    }
}
