using OpenIGI.Core.Graphics;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using SkiaSharp;

namespace OpenIGI.UI.Skia;

/// <summary>
/// The launcher's <see cref="IUiRenderer"/>: the painter's quads become Skia vertex meshes, its glyphs vector
/// text, and frosted glass a backdrop-filtered layer — all onto one <see cref="SKCanvas"/>, in order.
/// </summary>
/// <remarks>
/// <para>
/// Quads that share an image and a blend are gathered and drawn as one mesh, the way the game's sprite batch
/// gathers them, and glyphs that share a face, size and colour as one text blob. Anything that changes the
/// state — or a glyph, or a pane of glass — draws what has gathered first, so the order the painter asked
/// for is the order things land in.
/// </para>
/// <para>
/// Text is drawn from the same TrueType file the game rasterises its atlas from, at the pen positions the
/// atlas metrics computed, so a line breaks and measures exactly as it does in the game — only sharper,
/// because Skia renders the outlines at the size they are shown.
/// </para>
/// </remarks>
public sealed class SkiaUiRenderer : IUiRenderer, IDisposable
{
    private readonly SKCanvas _canvas;
    private readonly SkiaImageStore _images;
    private readonly SkiaFontCache _fonts;
    private readonly List<SKPoint> _positions = [];
    private readonly List<SKPoint> _texCoords = [];
    private readonly List<SKColor> _colors = [];
    private readonly List<ushort> _glyphs = [];
    private readonly List<SKPoint> _glyphPositions = [];
    private readonly SKPaint _paint = new() { IsAntialias = true };

    private UiImage _meshImage;
    private UiBlend _meshBlend;
    private SkiaFontCache.Face? _textFace;
    private float _textSize;
    private Bgra32 _textColor;
    private UiTextEffects? _textEffects;
    private UiBlend _textBlend;

    /// <summary>Initializes a new instance of the <see cref="SkiaUiRenderer"/> class.</summary>
    /// <param name="canvas">The canvas to draw on.</param>
    /// <param name="images">The image table images are looked up in.</param>
    /// <param name="fonts">The typeface cache text is drawn from.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public SkiaUiRenderer(SKCanvas canvas, SkiaImageStore images, SkiaFontCache fonts)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(fonts);
        _canvas = canvas;
        _images = images;
        _fonts = fonts;
    }

    /// <inheritdoc/>
    public void DrawQuad(UiImage image, in UiVertex topLeft, in UiVertex topRight, in UiVertex bottomRight, in UiVertex bottomLeft, UiBlend blend)
    {
        FlushText();
        if (_positions.Count > 0 && (image != _meshImage || blend != _meshBlend || _positions.Count > 60000))
        {
            FlushMesh();
        }

        _meshImage = image;
        _meshBlend = blend;

        float w = 1f;
        float h = 1f;
        if (image.IsValid && _images.TryGet(image, out SKImage skia, out _))
        {
            w = skia.Width;
            h = skia.Height;
        }

        // Two triangles: TL, TR, BR and TL, BR, BL.
        Add(topLeft);
        Add(topRight);
        Add(bottomRight);
        Add(topLeft);
        Add(bottomRight);
        Add(bottomLeft);

        void Add(in UiVertex v)
        {
            _positions.Add(new SKPoint(v.X, v.Y));
            _texCoords.Add(new SKPoint(v.U * w, v.V * h));
            _colors.Add(new SKColor(v.Color.R, v.Color.G, v.Color.B, v.Color.A));
        }
    }

    /// <inheritdoc/>
    public void DrawGlyph(UiFont font, in UiGlyph glyph, Bgra32 color, in UiTextEffects? effects, UiBlend blend)
    {
        ArgumentNullException.ThrowIfNull(font);
        FlushMesh();

        SkiaFontCache.Face? face = _fonts.Get(font);
        if (face is null)
        {
            // No outlines to draw from: fall back to the atlas, which is right for a coverage atlas.
            DrawAtlasGlyph(font, glyph, color, blend);
            return;
        }

        if (glyph.Trimmed)
        {
            FlushText();
            _canvas.Save();
            _canvas.ClipRect(ToSk(glyph.Clip));
            StartRun(face, glyph.SizePx, color, effects, blend);
            AddGlyph(face, glyph);
            FlushText();
            _canvas.Restore();
            return;
        }

        if (_glyphs.Count > 0 && (!ReferenceEquals(face, _textFace) || glyph.SizePx != _textSize || color != _textColor || !Nullable.Equals(effects, _textEffects) || blend != _textBlend))
        {
            FlushText();
        }

        if (_glyphs.Count == 0)
        {
            StartRun(face, glyph.SizePx, color, effects, blend);
        }

        AddGlyph(face, glyph);
    }

    /// <inheritdoc/>
    public void DrawBackdrop(ReadOnlySpan<UiVec2> polygon, float sigmaPx, Bgra32 tint, float opacity, float zoom)
    {
        if (polygon.Length < 3)
        {
            return;
        }

        Flush();
        using var path = new SKPath();
        path.MoveTo(polygon[0].X, polygon[0].Y);
        for (int i = 1; i < polygon.Length; i++)
        {
            path.LineTo(polygon[i].X, polygon[i].Y);
        }

        path.Close();
        _canvas.Save();
        _canvas.ClipPath(path, SKClipOperation.Intersect, antialias: true);

        if (sigmaPx > 0.25f && opacity > 0.004f)
        {
            // The layer starts as a blurred copy of what is already on the canvas behind the pane; drawn
            // back at the painter's opacity it is the glass, fading in with its screen.
            // A zoom magnifies what is behind about the canvas's centre before it is blurred.
            SKRect bounds = _canvas.DeviceClipBounds;
            using SKImageFilter? magnify = zoom == 1f
                ? null
                : SKImageFilter.CreateMatrix(SKMatrix.CreateScale(zoom, zoom, bounds.MidX, bounds.MidY), new SKSamplingOptions(SKFilterMode.Linear));
            using SKImageFilter blur = SKImageFilter.CreateBlur(sigmaPx, sigmaPx, SKShaderTileMode.Clamp, magnify);
            using var layerPaint = new SKPaint { Color = SKColors.White.WithAlpha((byte)(Math.Clamp(opacity, 0f, 1f) * 255f)) };
            _canvas.SaveLayer(new SKCanvasSaveLayerRec { Backdrop = blur, Paint = layerPaint });
            _canvas.Restore();
        }

        if (tint.A > 0)
        {
            _paint.Shader = null;
            _paint.MaskFilter = null;
            _paint.Style = SKPaintStyle.Fill;
            _paint.BlendMode = SKBlendMode.SrcOver;
            _paint.Color = new SKColor(tint.R, tint.G, tint.B, tint.A);
            _canvas.DrawPath(path, _paint);
        }

        _canvas.Restore();
    }

    /// <summary>Draws everything gathered so far. Call at the end of a frame, before reading the canvas.</summary>
    public void Flush()
    {
        FlushMesh();
        FlushText();
    }

    private void StartRun(SkiaFontCache.Face face, float size, Bgra32 color, in UiTextEffects? effects, UiBlend blend)
    {
        _textFace = face;
        _textSize = size;
        _textColor = color;
        _textEffects = effects;
        _textBlend = blend;
    }

    private void AddGlyph(SkiaFontCache.Face face, in UiGlyph glyph)
    {
        _glyphs.Add(face.Glyph(glyph.Character));
        _glyphPositions.Add(new SKPoint(glyph.PenX, glyph.BaselineY));
    }

    private void FlushMesh()
    {
        if (_positions.Count == 0)
        {
            return;
        }

        SKShader? shader = null;
        if (_meshImage.IsValid && _images.TryGet(_meshImage, out _, out SKShader found))
        {
            shader = found;
        }

        using SKVertices vertices = SKVertices.CreateCopy(
            SKVertexMode.Triangles,
            [.. _positions],
            shader is null ? null : [.. _texCoords],
            [.. _colors]);

        _paint.Shader = shader;
        _paint.MaskFilter = null;
        _paint.Style = SKPaintStyle.Fill;
        _paint.Color = SKColors.White;
        _paint.BlendMode = ToSk(_meshBlend);

        // The vertex colour multiplies the image (or stands alone without one), exactly as the sprite
        // batch's texture × vertex-colour modulation does.
        _canvas.DrawVertices(vertices, SKBlendMode.Modulate, _paint);
        _paint.Shader = null;

        _positions.Clear();
        _texCoords.Clear();
        _colors.Clear();
    }

    private void FlushText()
    {
        if (_glyphs.Count == 0 || _textFace is null)
        {
            _glyphs.Clear();
            _glyphPositions.Clear();
            return;
        }

        using SKFont font = _textFace.CreateFont(_textSize);
        using var builder = new SKTextBlobBuilder();
        SKPositionedRunBuffer run = builder.AllocatePositionedRun(font, _glyphs.Count);
        Span<ushort> ids = run.Glyphs;
        Span<SKPoint> positions = run.Positions;
        for (int i = 0; i < _glyphs.Count; i++)
        {
            ids[i] = _glyphs[i];
            positions[i] = _glyphPositions[i];
        }

        using SKTextBlob? blob = builder.Build();
        if (blob is not null)
        {
            _paint.Shader = null;
            _paint.BlendMode = ToSk(_textBlend);
            if (_textEffects is { } fx)
            {
                DrawEffects(blob, fx);
            }

            _paint.Style = SKPaintStyle.Fill;
            _paint.MaskFilter = null;
            _paint.Color = new SKColor(_textColor.R, _textColor.G, _textColor.B, _textColor.A);
            _canvas.DrawText(blob, 0f, 0f, _paint);
        }

        _glyphs.Clear();
        _glyphPositions.Clear();
    }

    private void DrawEffects(SKTextBlob blob, in UiTextEffects fx)
    {
        // Distance-field widths are fractions of the glyph's field; at the game's 64-pixel atlas with a
        // 4-pixel spread, one unit is about a sixteenth of the text size, which is what maps them here.
        float unit = _textSize / 16f;
        if (fx.GlowWidth > 0f && fx.GlowIntensity > 0f && fx.GlowColor.A > 0)
        {
            using SKMaskFilter glow = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, MathF.Max(0.5f, fx.GlowWidth * unit * 2f));
            _paint.MaskFilter = glow;
            _paint.Style = SKPaintStyle.Fill;
            _paint.Color = new SKColor(fx.GlowColor.R, fx.GlowColor.G, fx.GlowColor.B, (byte)(fx.GlowColor.A * Math.Clamp(fx.GlowIntensity, 0f, 1f)));
            _canvas.DrawText(blob, 0f, 0f, _paint);
            _paint.MaskFilter = null;
        }

        if (fx.OutlineWidth > 0f && fx.OutlineColor.A > 0)
        {
            _paint.Style = SKPaintStyle.Stroke;
            _paint.StrokeWidth = MathF.Max(0.5f, fx.OutlineWidth * unit * 2f);
            _paint.StrokeJoin = SKStrokeJoin.Round;
            _paint.Color = new SKColor(fx.OutlineColor.R, fx.OutlineColor.G, fx.OutlineColor.B, fx.OutlineColor.A);
            _canvas.DrawText(blob, 0f, 0f, _paint);
            _paint.Style = SKPaintStyle.Fill;
        }
    }

    private void DrawAtlasGlyph(UiFont font, in UiGlyph glyph, Bgra32 color, UiBlend blend)
    {
        FlushText();
        if (!font.Texture.IsValid)
        {
            return;
        }

        UiRect d = glyph.Destination;
        UiRect s = glyph.Source;
        DrawQuad(
            font.Texture,
            new UiVertex(d.X, d.Y, s.X, s.Y, color),
            new UiVertex(d.Right, d.Y, s.Right, s.Y, color),
            new UiVertex(d.Right, d.Bottom, s.Right, s.Bottom, color),
            new UiVertex(d.X, d.Bottom, s.X, s.Bottom, color),
            blend);
    }

    /// <inheritdoc/>
    public void Dispose() => _paint.Dispose();

    private static SKRect ToSk(UiRect rect) => new(rect.X, rect.Y, rect.Right, rect.Bottom);

    private static SKBlendMode ToSk(UiBlend blend) => blend switch
    {
        UiBlend.Additive => SKBlendMode.Plus,
        UiBlend.Opaque => SKBlendMode.Src,
        _ => SKBlendMode.SrcOver,
    };
}
