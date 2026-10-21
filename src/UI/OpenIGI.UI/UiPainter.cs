using OpenIGI.Core.Graphics;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;

namespace OpenIGI.UI;

/// <summary>
/// The interface's drawing surface: rectangles, polygons, gradients, sprites, frosted glass and text,
/// emitted through an <see cref="IUiRenderer"/> with clip, opacity and transform stacks.
/// </summary>
/// <remarks>
/// <para>
/// One painter serves every backend. The game hands it a renderer over its sprite batch, so UI draws
/// interleave with the HUD and the console in submission order exactly as the original's painter's
/// algorithm expects; the launcher hands it a renderer over a Skia canvas. Everything that should look
/// the same on both — clipping, edge feathering, gradients, text layout — happens here, and the renderer
/// only puts the resulting device-space quads and glyphs on screen.
/// </para>
/// <para>
/// <b>Clipping is done on the CPU.</b> A clip rectangle is applied by trimming each rectangle to it and
/// adjusting its texture coordinates in proportion, and each polygon by Sutherland–Hodgman. That is
/// enough for axis-aligned UI: a scroll view pushes a clip, its rows draw trimmed, and it pops. It keeps
/// renderers free of scissor state, and it is what lets the draw output be asserted through a
/// recording renderer without a GPU.
/// </para>
/// </remarks>
public sealed class UiPainter
{
    private readonly Stack<UiRect> _clips = new();
    private readonly Stack<float> _opacityStack = new();
    private readonly Stack<UiTransform> _transforms = new();
    private float _opacity = 1f;
    private UiTransform _transform = UiTransform.Identity;

    /// <summary>Initializes a new instance of the <see cref="UiPainter"/> class.</summary>
    /// <param name="renderer">The renderer the painter draws through.</param>
    /// <param name="context">The layout context mapping design units to this target.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public UiPainter(IUiRenderer renderer, LayoutContext context)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(context);
        Renderer = renderer;
        Context = context;
    }

    /// <summary>Gets the renderer the painter draws through.</summary>
    public IUiRenderer Renderer { get; }

    /// <summary>Gets the layout context for this frame.</summary>
    public LayoutContext Context { get; }

    /// <summary>Gets the current clip rectangle in device pixels, the whole viewport when nothing is pushed.</summary>
    public UiRect Clip => _clips.Count > 0 ? _clips.Peek() : new UiRect(0f, 0f, Context.Viewport.X, Context.Viewport.Y);

    /// <summary>Gets the current opacity multiplier applied to every drawn colour's alpha.</summary>
    public float Opacity => _opacity;

    /// <summary>Gets the transform currently applied to everything drawn.</summary>
    public UiTransform Transform => _transform;

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

    /// <summary>Applies a transform to everything drawn until the matching <see cref="PopTransform"/>.</summary>
    /// <param name="transform">The transform, applied before the one already in force.</param>
    public void PushTransform(UiTransform transform)
    {
        _transforms.Push(_transform);
        _transform = _transform.Then(transform);
    }

    /// <summary>Restores the transform in force before the matching <see cref="PushTransform"/>.</summary>
    /// <exception cref="InvalidOperationException">No transform is pushed.</exception>
    public void PopTransform()
    {
        if (_transforms.Count == 0)
        {
            throw new InvalidOperationException("PopTransform was called without a matching PushTransform.");
        }

        _transform = _transforms.Pop();
    }

    private Bgra32 Fade(Bgra32 color) =>
        _opacity >= 1f ? color : color.WithAlpha((byte)(color.A * _opacity));

    /// <summary>Restricts drawing to the intersection of the current clip and a rectangle.</summary>
    /// <param name="rect">The rectangle to clip to, under the current transform.</param>
    public void PushClip(UiRect rect)
    {
        UiRect device = _transform.Apply(rect);
        _clips.Push(_clips.Count > 0 ? _clips.Peek().Intersect(device) : device);
    }

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
    public void FillRect(UiRect rect, Bgra32 color, UiBlend blend = UiBlend.AlphaBlend) =>
        DrawSprite(UiImage.None, rect, UiRect.Unit, color, blend);

    /// <summary>Strokes a rectangle's outline with four thin fills, inside its edges.</summary>
    /// <param name="rect">The rectangle in pixels.</param>
    /// <param name="thickness">The stroke thickness in pixels.</param>
    /// <param name="color">The stroke colour.</param>
    /// <param name="blend">How the stroke combines with what is behind it.</param>
    public void StrokeRect(UiRect rect, float thickness, Bgra32 color, UiBlend blend = UiBlend.AlphaBlend)
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

    /// <summary>Fills a rectangle with a linear gradient.</summary>
    /// <param name="rect">The rectangle in pixels.</param>
    /// <param name="stops">The colour stops, in increasing offset order.</param>
    /// <param name="direction">Which way the gradient runs.</param>
    /// <param name="blend">How the fill combines with what is behind it.</param>
    /// <remarks>Drawn as one band per pair of stops, each a quad with a colour at either end.</remarks>
    public void FillGradient(
        UiRect rect,
        ReadOnlySpan<UiGradientStop> stops,
        UiGradientDirection direction,
        UiBlend blend = UiBlend.AlphaBlend)
    {
        if (stops.Length == 0 || rect.IsEmpty)
        {
            return;
        }

        if (stops.Length == 1)
        {
            FillRect(rect, stops[0].Color, blend);
            return;
        }

        bool horizontal = direction == UiGradientDirection.Horizontal;
        float length = horizontal ? rect.Width : rect.Height;
        Span<UiVertex> quad = stackalloc UiVertex[4];

        // Lead-in and tail before the first and after the last stop hold their colours flat.
        float previousOffset = 0f;
        Bgra32 previousColor = stops[0].Color;
        for (int i = 0; i <= stops.Length; i++)
        {
            float offset = i < stops.Length ? Math.Clamp(stops[i].Offset, 0f, 1f) : 1f;
            Bgra32 color = i < stops.Length ? stops[i].Color : stops[^1].Color;
            if (offset > previousOffset)
            {
                float a = previousOffset * length;
                float b = offset * length;
                Bgra32 ca = Fade(previousColor);
                Bgra32 cb = Fade(color);
                if (horizontal)
                {
                    quad[0] = Vertex(rect.X + a, rect.Y, ca);
                    quad[1] = Vertex(rect.X + b, rect.Y, cb);
                    quad[2] = Vertex(rect.X + b, rect.Bottom, cb);
                    quad[3] = Vertex(rect.X + a, rect.Bottom, ca);
                }
                else
                {
                    quad[0] = Vertex(rect.X, rect.Y + a, ca);
                    quad[1] = Vertex(rect.Right, rect.Y + a, ca);
                    quad[2] = Vertex(rect.Right, rect.Y + b, cb);
                    quad[3] = Vertex(rect.X, rect.Y + b, cb);
                }

                EmitPolygon(quad, UiImage.None, blend);
            }

            previousOffset = MathF.Max(previousOffset, offset);
            previousColor = color;
        }
    }

    /// <summary>Fills a rectangle with an elliptical radial gradient.</summary>
    /// <param name="rect">The rectangle the gradient is confined to, in pixels.</param>
    /// <param name="center">The gradient's centre, in pixels.</param>
    /// <param name="radius">The ellipse's radii at offset 1, in pixels.</param>
    /// <param name="stops">The colour stops from the centre outwards, in increasing offset order.</param>
    /// <param name="blend">How the fill combines with what is behind it.</param>
    /// <remarks>
    /// The vignettes and accent glows in the design. Drawn as concentric rings of quads; the area beyond
    /// the last ring is filled flat with the last stop's colour, so the whole rectangle is covered.
    /// </remarks>
    public void FillRadialGradient(
        UiRect rect,
        UiVec2 center,
        UiVec2 radius,
        ReadOnlySpan<UiGradientStop> stops,
        UiBlend blend = UiBlend.AlphaBlend)
    {
        if (stops.Length == 0 || rect.IsEmpty || radius.X <= 0f || radius.Y <= 0f)
        {
            return;
        }

        const int Segments = 48;
        const int Rings = 12;
        PushClip(rect);
        try
        {
            Span<UiVertex> quad = stackalloc UiVertex[4];
            for (int ring = 0; ring < Rings; ring++)
            {
                float r0 = ring / (float)Rings;
                float r1 = (ring + 1) / (float)Rings;
                Bgra32 c0 = Fade(UiGeometry.Sample(stops, r0));
                Bgra32 c1 = Fade(UiGeometry.Sample(stops, r1));
                if (c0.A == 0 && c1.A == 0 && blend != UiBlend.Opaque)
                {
                    continue;
                }

                for (int s = 0; s < Segments; s++)
                {
                    float a0 = s * MathF.Tau / Segments;
                    float a1 = (s + 1) * MathF.Tau / Segments;
                    quad[0] = Vertex(center.X + (MathF.Cos(a0) * radius.X * r0), center.Y + (MathF.Sin(a0) * radius.Y * r0), c0);
                    quad[1] = Vertex(center.X + (MathF.Cos(a0) * radius.X * r1), center.Y + (MathF.Sin(a0) * radius.Y * r1), c1);
                    quad[2] = Vertex(center.X + (MathF.Cos(a1) * radius.X * r1), center.Y + (MathF.Sin(a1) * radius.Y * r1), c1);
                    quad[3] = Vertex(center.X + (MathF.Cos(a1) * radius.X * r0), center.Y + (MathF.Sin(a1) * radius.Y * r0), c0);
                    EmitPolygon(quad, UiImage.None, blend);
                }
            }

            // Beyond the outermost ring: fill the rest of the rectangle with the end colour, as CSS does.
            Bgra32 outer = stops[^1].Color;
            if (outer.A > 0 || blend == UiBlend.Opaque)
            {
                FillOutsideEllipse(rect, center, radius, outer, blend);
            }
        }
        finally
        {
            PopClip();
        }
    }

    /// <summary>Fills a convex polygon with a solid colour.</summary>
    /// <param name="points">The polygon's corners, in order, in pixels.</param>
    /// <param name="color">The fill colour.</param>
    /// <param name="blend">How the fill combines with what is behind it.</param>
    /// <param name="smoothEdges">
    /// Whether to feather the polygon's slanted edges by one device pixel. Axis-aligned edges are never
    /// feathered, so a cut-corner panel keeps its crisp straight sides and only the 45° cut is smoothed.
    /// </param>
    public void FillPolygon(
        ReadOnlySpan<UiVec2> points,
        Bgra32 color,
        UiBlend blend = UiBlend.AlphaBlend,
        bool smoothEdges = true)
    {
        if (points.Length < 3)
        {
            return;
        }

        int count = Math.Min(points.Length, UiGeometry.MaxClippedVertices - 4);
        Bgra32 faded = Fade(color);
        Span<UiVertex> vertices = stackalloc UiVertex[count];
        for (int i = 0; i < count; i++)
        {
            vertices[i] = Vertex(points[i].X, points[i].Y, faded);
        }

        EmitPolygon(vertices, UiImage.None, blend);

        if (smoothEdges)
        {
            FeatherEdges(vertices, faded, blend);
        }
    }

    /// <summary>Strokes the outline of a convex polygon, inside its edges.</summary>
    /// <param name="points">The polygon's corners, in order, in pixels.</param>
    /// <param name="thickness">The stroke thickness in pixels.</param>
    /// <param name="color">The stroke colour.</param>
    /// <param name="blend">How the stroke combines with what is behind it.</param>
    /// <remarks>
    /// Each edge is a band of quads mitred against its neighbours, so a hairline around a cut-corner
    /// button meets itself cleanly at every corner.
    /// </remarks>
    public void StrokePolygon(ReadOnlySpan<UiVec2> points, float thickness, Bgra32 color, UiBlend blend = UiBlend.AlphaBlend)
    {
        int n = points.Length;
        if (n < 3 || thickness <= 0f)
        {
            return;
        }

        // Inward normals depend on winding: clockwise on a Y-down screen has positive signed area.
        float sign = UiGeometry.SignedArea2(points) >= 0f ? 1f : -1f;
        Span<UiVec2> inner = stackalloc UiVec2[n];
        for (int i = 0; i < n; i++)
        {
            UiVec2 prev = points[(i + n - 1) % n];
            UiVec2 here = points[i];
            UiVec2 next = points[(i + 1) % n];
            UiVec2 n0 = UiGeometryEmitter.InwardNormal(prev, here, sign);
            UiVec2 n1 = UiGeometryEmitter.InwardNormal(here, next, sign);
            float mx = n0.X + n1.X;
            float my = n0.Y + n1.Y;
            float dot = (mx * n1.X) + (my * n1.Y);
            float scale = dot <= 1e-4f ? thickness : thickness / dot;
            inner[i] = new UiVec2(here.X + (mx * scale), here.Y + (my * scale));
        }

        Bgra32 faded = Fade(color);
        Span<UiVertex> quad = stackalloc UiVertex[4];
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            quad[0] = Vertex(points[i].X, points[i].Y, faded);
            quad[1] = Vertex(points[j].X, points[j].Y, faded);
            quad[2] = Vertex(inner[j].X, inner[j].Y, faded);
            quad[3] = Vertex(inner[i].X, inner[i].Y, faded);
            EmitPolygon(quad, UiImage.None, blend);
        }
    }

    /// <summary>Fills a rectangle with some corners cut at 45° — the design's accented buttons and panels.</summary>
    /// <param name="rect">The rectangle in pixels.</param>
    /// <param name="cut">The length cut from each side at a cut corner, in pixels.</param>
    /// <param name="corners">Which corners are cut.</param>
    /// <param name="color">The fill colour.</param>
    /// <param name="blend">How the fill combines with what is behind it.</param>
    public void FillCutRect(UiRect rect, float cut, UiCorners corners, Bgra32 color, UiBlend blend = UiBlend.AlphaBlend)
    {
        if (cut <= 0f || corners == UiCorners.None)
        {
            FillRect(rect, color, blend);
            return;
        }

        Span<UiVec2> points = stackalloc UiVec2[UiPath.MaxPoints];
        int count = UiPath.CutCornerRect(rect, cut, corners, points);
        FillPolygon(points[..count], color, blend);
    }

    /// <summary>Strokes the outline of a rectangle with some corners cut at 45°.</summary>
    /// <param name="rect">The rectangle in pixels.</param>
    /// <param name="cut">The length cut from each side at a cut corner, in pixels.</param>
    /// <param name="corners">Which corners are cut.</param>
    /// <param name="thickness">The stroke thickness in pixels.</param>
    /// <param name="color">The stroke colour.</param>
    public void StrokeCutRect(UiRect rect, float cut, UiCorners corners, float thickness, Bgra32 color)
    {
        if (cut <= 0f || corners == UiCorners.None)
        {
            StrokeRect(rect, thickness, color);
            return;
        }

        Span<UiVec2> points = stackalloc UiVec2[UiPath.MaxPoints];
        int count = UiPath.CutCornerRect(rect, cut, corners, points);
        StrokePolygon(points[..count], thickness, color);
    }

    /// <summary>Fills a square turned 45° — the design's bullet and status marker.</summary>
    /// <param name="center">The diamond's centre, in pixels.</param>
    /// <param name="size">The side of the square before it is turned, in pixels.</param>
    /// <param name="color">The fill colour.</param>
    public void FillDiamond(UiVec2 center, float size, Bgra32 color)
    {
        Span<UiVec2> points = stackalloc UiVec2[4];
        UiPath.Diamond(center, size, points);
        FillPolygon(points, color);
    }

    /// <summary>Draws a straight line of a given thickness, smoothed along its length.</summary>
    /// <param name="from">The start point, in pixels.</param>
    /// <param name="to">The end point, in pixels.</param>
    /// <param name="thickness">The line's thickness, in pixels.</param>
    /// <param name="color">The line colour.</param>
    /// <remarks>The chevrons, arrows and crosses the design draws as glyphs are built from these.</remarks>
    public void StrokeLine(UiVec2 from, UiVec2 to, float thickness, Bgra32 color)
    {
        float dx = to.X - from.X;
        float dy = to.Y - from.Y;
        float length = MathF.Sqrt((dx * dx) + (dy * dy));
        if (length <= 1e-4f || thickness <= 0f)
        {
            return;
        }

        float nx = -dy / length * thickness * 0.5f;
        float ny = dx / length * thickness * 0.5f;
        Span<UiVec2> quad =
        [
            new(from.X + nx, from.Y + ny),
            new(to.X + nx, to.Y + ny),
            new(to.X - nx, to.Y - ny),
            new(from.X - nx, from.Y - ny),
        ];
        FillPolygon(quad, color);
    }

    /// <summary>Draws an open chevron — two strokes meeting at a point — pointing in a direction.</summary>
    /// <param name="center">The chevron's centre, in pixels.</param>
    /// <param name="size">The chevron's height across its open side, in pixels.</param>
    /// <param name="direction">Which way it points.</param>
    /// <param name="thickness">The stroke thickness, in pixels.</param>
    /// <param name="color">The stroke colour.</param>
    public void DrawChevron(UiVec2 center, float size, UiDirection direction, float thickness, Bgra32 color)
    {
        float h = size * 0.5f;
        float d = size * 0.28f;
        (UiVec2 tip, UiVec2 a, UiVec2 b) = direction switch
        {
            UiDirection.Left => (new UiVec2(center.X - d, center.Y), new UiVec2(center.X + d, center.Y - h), new UiVec2(center.X + d, center.Y + h)),
            UiDirection.Up => (new UiVec2(center.X, center.Y - d), new UiVec2(center.X - h, center.Y + d), new UiVec2(center.X + h, center.Y + d)),
            UiDirection.Down => (new UiVec2(center.X, center.Y + d), new UiVec2(center.X - h, center.Y - d), new UiVec2(center.X + h, center.Y - d)),
            _ => (new UiVec2(center.X + d, center.Y), new UiVec2(center.X - d, center.Y - h), new UiVec2(center.X - d, center.Y + h)),
        };

        StrokeLine(a, tip, thickness, color);
        StrokeLine(tip, b, thickness, color);
    }

    /// <summary>Draws an uppercase-style label with letterspacing given in ems, the way the design sets every caption.</summary>
    /// <param name="font">The font to draw with.</param>
    /// <param name="text">The text, already in the case it should be shown in.</param>
    /// <param name="topLeft">The top-left of the label, in pixels.</param>
    /// <param name="sizePx">The text size in pixels.</param>
    /// <param name="color">The text colour.</param>
    /// <param name="trackingEm">The letterspacing in ems: <c>letter-spacing: 0.16em</c> is 0.16.</param>
    /// <returns>The pen position after the last glyph, in pixels.</returns>
    public UiVec2 DrawLabel(UiFont font, ReadOnlySpan<char> text, UiVec2 topLeft, float sizePx, Bgra32 color, float trackingEm) =>
        DrawTextTracked(font, text, topLeft, sizePx, color, sizePx * trackingEm);

    /// <summary>Measures a label set with letterspacing in ems.</summary>
    /// <param name="font">The font.</param>
    /// <param name="text">The text.</param>
    /// <param name="sizePx">The text size in pixels.</param>
    /// <param name="trackingEm">The letterspacing in ems.</param>
    /// <returns>The width in pixels, without the trailing letterspace after the last glyph.</returns>
    public static float MeasureLabel(UiFont font, ReadOnlySpan<char> text, float sizePx, float trackingEm)
    {
        ArgumentNullException.ThrowIfNull(font);
        float tracking = sizePx * trackingEm;
        return font.MeasureWidth(text, sizePx) + (tracking * Math.Max(0, text.Length - 1));
    }

    /// <summary>Fills a rectangle with frosted glass: a blurred copy of the scene behind the interface, tinted.</summary>
    /// <param name="rect">The rectangle in pixels.</param>
    /// <param name="blurSigma">The blur's standard deviation in pixels, before the current transform.</param>
    /// <param name="tint">The colour laid over the blurred scene.</param>
    /// <param name="zoom">How magnified the scene behind is, about the screen's centre; 1 samples it in place.</param>
    public void DrawBackdrop(UiRect rect, float blurSigma, Bgra32 tint, float zoom = 1f)
    {
        Span<UiVec2> corners =
        [
            new(rect.X, rect.Y),
            new(rect.Right, rect.Y),
            new(rect.Right, rect.Bottom),
            new(rect.X, rect.Bottom),
        ];
        DrawBackdrop(corners, blurSigma, tint, zoom);
    }

    /// <summary>Fills a convex polygon with frosted glass.</summary>
    /// <param name="points">The polygon's corners, in order, in pixels.</param>
    /// <param name="blurSigma">The blur's standard deviation in pixels, before the current transform.</param>
    /// <param name="tint">The colour laid over the blurred scene.</param>
    /// <param name="zoom">How magnified the scene behind is, about the screen's centre; 1 samples it in place.</param>
    public void DrawBackdrop(ReadOnlySpan<UiVec2> points, float blurSigma, Bgra32 tint, float zoom = 1f)
    {
        if (points.Length < 3)
        {
            return;
        }

        int count = Math.Min(points.Length, UiGeometry.MaxClippedVertices - 4);
        Span<UiVertex> vertices = stackalloc UiVertex[count];
        for (int i = 0; i < count; i++)
        {
            UiVec2 p = _transform.Apply(points[i]);
            vertices[i] = new UiVertex(p.X, p.Y, 0f, 0f, default);
        }

        Span<UiVertex> clipped = stackalloc UiVertex[UiGeometry.MaxClippedVertices];
        int clippedCount = UiGeometry.ClipPolygon(vertices, Clip, clipped);
        if (clippedCount < 3)
        {
            return;
        }

        Span<UiVec2> polygon = stackalloc UiVec2[clippedCount];
        for (int i = 0; i < clippedCount; i++)
        {
            polygon[i] = new UiVec2(clipped[i].X, clipped[i].Y);
        }

        Renderer.DrawBackdrop(polygon, MathF.Max(0f, blurSigma * _transform.Scale), Fade(tint), _opacity, MathF.Max(0.01f, zoom));
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
        UiBlend blend = UiBlend.AlphaBlend)
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
    /// The one cue that separates a floating window or a dropdown from a panel painted onto the
    /// background, drawn as a nine-sliced soft falloff around the rectangle.
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
        UiBlend blend = UiBlend.AlphaBlend) =>
        DrawTextTrackedCore(font, text, topLeft, sizePx, color, tracking, null, blend);

    /// <summary>Draws a string with extra space between its glyphs and distance-field effects.</summary>
    /// <param name="font">The font to draw with.</param>
    /// <param name="text">The text; a line feed starts a new line.</param>
    /// <param name="topLeft">The top-left of the first line, in pixels.</param>
    /// <param name="sizePx">The text size in pixels.</param>
    /// <param name="color">The text colour.</param>
    /// <param name="tracking">Extra pixels added after each glyph.</param>
    /// <param name="effects">The outline, glow and dissolve.</param>
    /// <param name="blend">How the glyphs combine with what is behind them.</param>
    /// <returns>The pen position after the last glyph, in pixels.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="font"/> is <see langword="null"/>.</exception>
    public UiVec2 DrawTextTracked(
        UiFont font,
        ReadOnlySpan<char> text,
        UiVec2 topLeft,
        float sizePx,
        Bgra32 color,
        float tracking,
        in UiTextEffects effects,
        UiBlend blend = UiBlend.AlphaBlend) =>
        DrawTextTrackedCore(font, text, topLeft, sizePx, color, tracking, effects, blend);

    private UiVec2 DrawTextTrackedCore(
        UiFont font,
        ReadOnlySpan<char> text,
        UiVec2 topLeft,
        float sizePx,
        Bgra32 color,
        float tracking,
        in UiTextEffects? effects,
        UiBlend blend)
    {
        ArgumentNullException.ThrowIfNull(font);

        if (tracking == 0f)
        {
            return DrawTextCore(font, text, topLeft, sizePx, color, blend, effects);
        }

        UiVec2 pen = topLeft;
        Span<char> one = stackalloc char[1];
        float lineHeight = font.LineHeight(sizePx);

        foreach (char c in text)
        {
            if (c == '\n')
            {
                pen = new UiVec2(topLeft.X, pen.Y + lineHeight);
                continue;
            }

            one[0] = c;
            DrawTextCore(font, one, pen, sizePx, color, blend, effects);
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
    /// a scroll view. Use <see cref="FillPolygon"/> for a clipped, smoothed shape.
    /// </remarks>
    public void FillTriangle(UiVec2 a, UiVec2 b, UiVec2 c, Bgra32 color)
    {
        Bgra32 faded = Fade(color);
        UiVertex vc = Vertex(c.X, c.Y, faded);
        Renderer.DrawQuad(UiImage.None, Vertex(a.X, a.Y, faded), Vertex(b.X, b.Y, faded), vc, vc, UiBlend.AlphaBlend);
    }

    /// <summary>Fills an arbitrary convex quad, unclipped.</summary>
    /// <param name="a">The first corner.</param>
    /// <param name="b">The second corner.</param>
    /// <param name="c">The third corner.</param>
    /// <param name="d">The fourth corner.</param>
    /// <param name="color">The fill colour.</param>
    /// <remarks>
    /// This is the quad counterpart to <see cref="FillTriangle"/>. It is intended for display-space
    /// markers whose native geometry is rotated around a tracked target, rather than for normal UI
    /// panels which should continue to use <see cref="FillRect"/> and honour the clip stack.
    /// </remarks>
    public void FillQuad(UiVec2 a, UiVec2 b, UiVec2 c, UiVec2 d, Bgra32 color)
    {
        Bgra32 faded = Fade(color);
        Renderer.DrawQuad(
            UiImage.None,
            Vertex(a.X, a.Y, 0f, 0f, faded),
            Vertex(b.X, b.Y, 1f, 0f, faded),
            Vertex(c.X, c.Y, 1f, 1f, faded),
            Vertex(d.X, d.Y, 0f, 1f, faded),
            UiBlend.AlphaBlend);
    }

    /// <summary>Draws a textured sprite.</summary>
    /// <param name="texture">The image to sample.</param>
    /// <param name="destination">The destination rectangle in pixels.</param>
    /// <param name="source">The source rectangle in texture coordinates.</param>
    /// <param name="color">The colour modulated with the image.</param>
    /// <param name="blend">How the sprite combines with what is behind it.</param>
    public void DrawSprite(
        UiImage texture,
        UiRect destination,
        UiRect source,
        Bgra32 color,
        UiBlend blend = UiBlend.AlphaBlend)
    {
        UiRect dest = _transform.Apply(destination);
        UiRect src = source;
        if (!UiGeometryEmitter.TryClip(Clip, ref dest, ref src))
        {
            return;
        }

        Bgra32 faded = Fade(color);
        Renderer.DrawQuad(
            texture,
            new UiVertex(dest.X, dest.Y, src.X, src.Y, faded),
            new UiVertex(dest.Right, dest.Y, src.Right, src.Y, faded),
            new UiVertex(dest.Right, dest.Bottom, src.Right, src.Bottom, faded),
            new UiVertex(dest.X, dest.Bottom, src.X, src.Bottom, faded),
            blend);
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
    public UiVec2 DrawText(
        UiFont font,
        ReadOnlySpan<char> text,
        UiVec2 topLeft,
        float sizePx,
        Bgra32 color,
        UiBlend blend = UiBlend.AlphaBlend)
    {
        ArgumentNullException.ThrowIfNull(font);
        return DrawTextCore(font, text, topLeft, sizePx, color, blend, null);
    }

    /// <summary>Draws distance-field text with an outline and glow, for a title or a heading.</summary>
    /// <param name="font">The SDF font to draw with.</param>
    /// <param name="text">The text; a line feed starts a new line.</param>
    /// <param name="topLeft">The top-left of the first line, in pixels.</param>
    /// <param name="sizePx">The text size in pixels.</param>
    /// <param name="color">The fill colour.</param>
    /// <param name="effects">The outline, glow and dissolve.</param>
    /// <param name="blend">How the glyphs combine with what is behind them.</param>
    /// <returns>The pen position after the last glyph, in pixels.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="font"/> is <see langword="null"/>.</exception>
    public UiVec2 DrawText(
        UiFont font,
        ReadOnlySpan<char> text,
        UiVec2 topLeft,
        float sizePx,
        Bgra32 color,
        in UiTextEffects effects,
        UiBlend blend = UiBlend.AlphaBlend)
    {
        ArgumentNullException.ThrowIfNull(font);
        return DrawTextCore(font, text, topLeft, sizePx, color, blend, effects);
    }

    private UiVec2 DrawTextCore(
        UiFont font,
        ReadOnlySpan<char> text,
        UiVec2 topLeft,
        float sizePx,
        Bgra32 color,
        UiBlend blend,
        in UiTextEffects? effects)
    {
        if (!font.Texture.IsValid && font.FontData is null)
        {
            throw new InvalidOperationException("This font was built for measurement only and cannot draw.");
        }

        UiRect clip = Clip;
        float scale = _transform.Scale;
        UiVec2 origin = _transform.Apply(topLeft);
        float deviceSize = sizePx * scale;
        Bgra32 faded = Fade(color);
        var run = new GlyphRun(font, text, deviceSize, origin);
        while (run.MoveNext())
        {
            GlyphPlacement placed = run.Current;
            UiRect dest = placed.Destination;
            UiRect source = placed.Source;
            if (!UiGeometryEmitter.TryClip(clip, ref dest, ref source))
            {
                continue;
            }

            bool trimmed = dest != placed.Destination;
            var glyph = new UiGlyph(dest, source, placed.Character, placed.PenX, placed.BaselineY, deviceSize, clip, trimmed);
            Renderer.DrawGlyph(font, glyph, faded, effects, blend);
        }

        // Back to the caller's space, so the returned pen continues where they expect.
        UiVec2 pen = run.Pen;
        return scale == 0f
            ? topLeft
            : new UiVec2((pen.X - _transform.Offset.X) / scale, (pen.Y - _transform.Offset.Y) / scale);
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

    private UiVertex Vertex(float x, float y, Bgra32 color) => Vertex(x, y, 0f, 0f, color);

    private UiVertex Vertex(float x, float y, float u, float v, Bgra32 color)
    {
        UiVec2 p = _transform.Apply(new UiVec2(x, y));
        return new UiVertex(p.X, p.Y, u, v, color);
    }

    /// <summary>Clips a device-space convex polygon to the current clip and emits it as a fan of quads.</summary>
    private void EmitPolygon(ReadOnlySpan<UiVertex> polygon, UiImage image, UiBlend blend) =>
        UiGeometryEmitter.EmitPolygon(Renderer, Clip, polygon, image, blend);

    /// <summary>Feathers a device-space polygon's slanted edges inside the current clip.</summary>
    private void FeatherEdges(ReadOnlySpan<UiVertex> polygon, Bgra32 color, UiBlend blend) =>
        UiGeometryEmitter.FeatherEdges(Renderer, Clip, polygon, color, blend);

    /// <summary>Fills the part of a rectangle outside an ellipse, for a radial gradient's flat tail.</summary>
    private void FillOutsideEllipse(UiRect rect, UiVec2 center, UiVec2 radius, Bgra32 color, UiBlend blend)
    {
        // Four bands around the ellipse's bounding box, then the box's corners outside the ellipse as
        // fan segments from the ellipse edge to the box edge.
        UiRect box = UiRect.FromEdges(center.X - radius.X, center.Y - radius.Y, center.X + radius.X, center.Y + radius.Y);
        FillBand(UiRect.FromEdges(rect.X, rect.Y, rect.Right, MathF.Min(rect.Bottom, box.Y)));
        FillBand(UiRect.FromEdges(rect.X, MathF.Max(rect.Y, box.Bottom), rect.Right, rect.Bottom));
        float top = MathF.Max(rect.Y, box.Y);
        float bottom = MathF.Min(rect.Bottom, box.Bottom);
        FillBand(UiRect.FromEdges(rect.X, top, MathF.Min(rect.Right, box.X), bottom));
        FillBand(UiRect.FromEdges(MathF.Max(rect.X, box.Right), top, rect.Right, bottom));

        const int Segments = 48;
        Bgra32 faded = Fade(color);
        Span<UiVertex> quad = stackalloc UiVertex[4];
        for (int s = 0; s < Segments; s++)
        {
            float a0 = s * MathF.Tau / Segments;
            float a1 = (s + 1) * MathF.Tau / Segments;
            UiVec2 e0 = new(center.X + (MathF.Cos(a0) * radius.X), center.Y + (MathF.Sin(a0) * radius.Y));
            UiVec2 e1 = new(center.X + (MathF.Cos(a1) * radius.X), center.Y + (MathF.Sin(a1) * radius.Y));
            UiVec2 b0 = ToBox(a0);
            UiVec2 b1 = ToBox(a1);
            quad[0] = Vertex(e0.X, e0.Y, faded);
            quad[1] = Vertex(b0.X, b0.Y, faded);
            quad[2] = Vertex(b1.X, b1.Y, faded);
            quad[3] = Vertex(e1.X, e1.Y, faded);
            EmitPolygon(quad, UiImage.None, blend);
        }

        void FillBand(UiRect band)
        {
            if (!band.IsEmpty)
            {
                FillRect(band, color, blend);
            }
        }

        UiVec2 ToBox(float angle)
        {
            float cx = MathF.Cos(angle);
            float cy = MathF.Sin(angle);
            float tx = MathF.Abs(cx) < 1e-6f ? float.MaxValue : radius.X / MathF.Abs(cx);
            float ty = MathF.Abs(cy) < 1e-6f ? float.MaxValue : radius.Y / MathF.Abs(cy);
            float t = MathF.Min(tx, ty);
            return new UiVec2(center.X + (cx * t), center.Y + (cy * t));
        }
    }
}
