using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Presentation.Igi1;
using OpenIGI.Rendering;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI;

namespace OpenIGI.Presentation.Rendering;

/// <summary>
/// A flat surface standing in the level, laid out in the same units an interface is, and drawn by
/// projecting each quad's corners through the frame's own eye.
/// </summary>
/// <remarks>
/// <para>
/// The technique <c>WorldTitleRenderer</c> sets the mission briefing in: there is no third
/// dimension in the sprite path, so a plane in the world is drawn by putting its corners where the
/// camera says they are and letting the batch interpolate between them. What that buys over an overlay
/// is everything perspective does — the surface shrinks as the player backs away from it, foreshortens
/// as they look down on it, and slides across the object it belongs to as they walk past, because it
/// is a thing in the level rather than a label about one.
/// </para>
/// <para>
/// <b>It stands up and turns to the viewer.</b> Its up is the world's, and its face is the horizontal
/// direction back to the eye, so it is always square-on to read and never lies down or rolls with the
/// camera. A surface pinned to the object's own facing would be more diegetic still and is a worse
/// idea: half the things a player can use are approached from an angle the level author never set a
/// facing for, and text seen at eighty degrees is not text.
/// </para>
/// <para>
/// Canvas coordinates are ordinary interface coordinates — x to the right, <b>y downwards</b>, origin
/// at the anchor — so a layout written for the screen can be laid on the world unchanged.
/// </para>
/// </remarks>
internal readonly struct WorldCanvas
{
    private readonly Vector3d _anchor;
    private readonly Vector3d _right;
    private readonly Vector3d _up;
    private readonly Vector3d _eyePosition;
    private readonly Matrix3f _worldToView;
    private readonly double _tangentH;
    private readonly double _tangentV;
    private readonly float _width;
    private readonly float _height;
    private readonly float _metres;

    /// <summary>Initializes a canvas standing at a point in the level.</summary>
    /// <param name="anchorMetres">Where the canvas's origin stands, in metres.</param>
    /// <param name="eye">The camera the frame was drawn from.</param>
    /// <param name="horizontalTangent">The tangent of half the horizontal field of view.</param>
    /// <param name="aspectRatio">The output's width over its height.</param>
    /// <param name="width">The output width in pixels.</param>
    /// <param name="height">The output height in pixels.</param>
    /// <param name="metresPerUnit">How much of the world one canvas unit covers.</param>
    public WorldCanvas(
        Vector3d anchorMetres,
        in Camera3D eye,
        float horizontalTangent,
        float aspectRatio,
        int width,
        int height,
        float metresPerUnit)
    {
        _anchor = anchorMetres;
        _eyePosition = eye.Position;
        _worldToView = eye.Orientation.Transposed();
        _tangentH = Math.Max(0.0001f, horizontalTangent);
        _tangentV = _tangentH / Math.Max(0.0001f, aspectRatio);
        _width = Math.Max(1, width);
        _height = Math.Max(1, height);
        _metres = metresPerUnit;

        // The face turns to the eye about the world's vertical only. Directly above or below the
        // anchor there is no horizontal direction to turn to, so the camera's own forward stands in.
        Vector3d toEye = new(
            _eyePosition.X - anchorMetres.X,
            _eyePosition.Y - anchorMetres.Y,
            0.0);
        double flat = Math.Sqrt((toEye.X * toEye.X) + (toEye.Y * toEye.Y));
        if (flat < 1e-4)
        {
            Vector3f forward = eye.Orientation.Row3;
            toEye = new Vector3d(-forward.X, -forward.Y, 0.0);
            flat = Math.Sqrt((toEye.X * toEye.X) + (toEye.Y * toEye.Y));
        }

        Vector3d normal = flat < 1e-4 ? new Vector3d(0.0, -1.0, 0.0) : new Vector3d(toEye.X / flat, toEye.Y / flat, 0.0);
        _right = new Vector3d(-normal.Y, normal.X, 0.0);
        _up = new Vector3d(0.0, 0.0, 1.0);
    }

    /// <summary>Gets the distance from the eye to the canvas's origin, in metres.</summary>
    public double DistanceMetres => Math.Sqrt(
        ((_anchor.X - _eyePosition.X) * (_anchor.X - _eyePosition.X))
        + ((_anchor.Y - _eyePosition.Y) * (_anchor.Y - _eyePosition.Y))
        + ((_anchor.Z - _eyePosition.Z) * (_anchor.Z - _eyePosition.Z)));

    /// <summary>Puts a canvas point on the screen.</summary>
    /// <param name="x">The point's horizontal position in canvas units.</param>
    /// <param name="y">The point's vertical position in canvas units, downwards.</param>
    /// <param name="screenX">Receives the horizontal pixel position.</param>
    /// <param name="screenY">Receives the vertical pixel position.</param>
    /// <returns><see langword="false"/> when the point is behind the eye.</returns>
    public bool TryProject(float x, float y, out float screenX, out float screenY)
    {
        double px = x * _metres;
        double py = y * _metres;
        Vector3d world = new(
            _anchor.X + (_right.X * px) - (_up.X * py),
            _anchor.Y + (_right.Y * px) - (_up.Y * py),
            _anchor.Z + (_right.Z * px) - (_up.Z * py));

        Vector3d offset = new(
            world.X - _eyePosition.X,
            world.Y - _eyePosition.Y,
            world.Z - _eyePosition.Z);
        Vector3d view = _worldToView.Transform(offset);
        if (view.Z <= 0.05)
        {
            screenX = 0f;
            screenY = 0f;
            return false;
        }

        double ndcX = view.X / view.Z / _tangentH;
        double ndcY = view.Y / view.Z / _tangentV;
        screenX = (float)(((ndcX * 0.5) + 0.5) * _width);
        screenY = (float)((1.0 - ((ndcY * 0.5) + 0.5)) * _height);
        return true;
    }

    /// <summary>Draws a textured rectangle of the canvas.</summary>
    /// <param name="batch">The open sprite batch.</param>
    /// <param name="texture">The texture to sample.</param>
    /// <param name="x">The rectangle's left edge in canvas units.</param>
    /// <param name="y">The rectangle's top edge in canvas units.</param>
    /// <param name="width">The rectangle's width in canvas units.</param>
    /// <param name="height">The rectangle's height in canvas units.</param>
    /// <param name="source">The source rectangle in texture coordinates.</param>
    /// <param name="colour">The colour modulated with the sample.</param>
    /// <param name="blend">How the quad combines with what is behind it.</param>
    /// <exception cref="ArgumentNullException"><paramref name="batch"/> is <see langword="null"/>.</exception>
    public void Draw(
        SpriteBatch batch,
        TextureHandle texture,
        float x,
        float y,
        float width,
        float height,
        in RectF source,
        Bgra32 colour,
        BlendMode blend = BlendMode.AlphaBlend)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (!TryCorners(x, y, width, height, out Corners corners))
        {
            return;
        }

        batch.DrawQuad(
            texture,
            new Vertex2D(corners.AX, corners.AY, source.X, source.Y, colour),
            new Vertex2D(corners.BX, corners.BY, source.Right, source.Y, colour),
            new Vertex2D(corners.CX, corners.CY, source.Right, source.Bottom, colour),
            new Vertex2D(corners.DX, corners.DY, source.X, source.Bottom, colour),
            blend);
    }

    /// <summary>Draws one distance-field glyph, or any textured rectangle needing its shading.</summary>
    /// <param name="batch">The open sprite batch.</param>
    /// <param name="atlas">The glyph atlas.</param>
    /// <param name="x">The rectangle's left edge in canvas units.</param>
    /// <param name="y">The rectangle's top edge in canvas units.</param>
    /// <param name="width">The rectangle's width in canvas units.</param>
    /// <param name="height">The rectangle's height in canvas units.</param>
    /// <param name="source">The glyph's rectangle in the atlas.</param>
    /// <param name="colour">The fill colour.</param>
    /// <param name="shading">The edge, outline and glow.</param>
    /// <param name="blend">How the glyph combines with what is under it.</param>
    /// <exception cref="ArgumentNullException"><paramref name="batch"/> is <see langword="null"/>.</exception>
    public void DrawText(
        SpriteBatch batch,
        TextureHandle atlas,
        float x,
        float y,
        float width,
        float height,
        in RectF source,
        Bgra32 colour,
        in SdfTextParams shading,
        BlendMode blend = BlendMode.AlphaBlend)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (!TryCorners(x, y, width, height, out Corners corners))
        {
            return;
        }

        batch.DrawTextQuad(
            atlas,
            new Vertex2D(corners.AX, corners.AY, source.X, source.Y, colour),
            new Vertex2D(corners.BX, corners.BY, source.Right, source.Y, colour),
            new Vertex2D(corners.CX, corners.CY, source.Right, source.Bottom, colour),
            new Vertex2D(corners.DX, corners.DY, source.X, source.Bottom, colour),
            shading,
            blend);
    }

    /// <summary>Draws a flat four-cornered patch of the canvas, for a shape no rectangle describes.</summary>
    /// <param name="batch">The open sprite batch.</param>
    /// <param name="ax">The first corner's horizontal position in canvas units.</param>
    /// <param name="ay">The first corner's vertical position.</param>
    /// <param name="bx">The second corner's horizontal position.</param>
    /// <param name="by">The second corner's vertical position.</param>
    /// <param name="cx">The third corner's horizontal position.</param>
    /// <param name="cy">The third corner's vertical position.</param>
    /// <param name="dx">The fourth corner's horizontal position.</param>
    /// <param name="dy">The fourth corner's vertical position.</param>
    /// <param name="colour">The fill colour.</param>
    /// <exception cref="ArgumentNullException"><paramref name="batch"/> is <see langword="null"/>.</exception>
    public void Fill(
        SpriteBatch batch,
        float ax,
        float ay,
        float bx,
        float by,
        float cx,
        float cy,
        float dx,
        float dy,
        Bgra32 colour)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (!TryProject(ax, ay, out float sax, out float say)
            || !TryProject(bx, by, out float sbx, out float sby)
            || !TryProject(cx, cy, out float scx, out float scy)
            || !TryProject(dx, dy, out float sdx, out float sdy))
        {
            return;
        }

        batch.DrawQuad(
            TextureHandle.None,
            new Vertex2D(sax, say, 0f, 0f, colour),
            new Vertex2D(sbx, sby, 1f, 0f, colour),
            new Vertex2D(scx, scy, 1f, 1f, colour),
            new Vertex2D(sdx, sdy, 0f, 1f, colour));
    }

    /// <summary>Sets a run of text on the canvas.</summary>
    /// <param name="batch">The open sprite batch.</param>
    /// <param name="font">The face to set it in.</param>
    /// <param name="text">The text.</param>
    /// <param name="x">The left edge of the run in canvas units.</param>
    /// <param name="y">The top of the run's line box in canvas units.</param>
    /// <param name="size">The text size in canvas units.</param>
    /// <param name="colour">The fill colour.</param>
    /// <param name="shading">The edge, outline and glow, for a distance-field face.</param>
    /// <param name="signedDistanceField">Whether the face is a distance field and wants that shading.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// The layout is <see cref="GlyphRun"/>'s, the same walk the screen painter uses; only where the
    /// glyphs land differs.
    /// </remarks>
    public void DrawText(
        SpriteBatch batch,
        UiFont font,
        ReadOnlySpan<char> text,
        float x,
        float y,
        float size,
        Bgra32 colour,
        in SdfTextParams shading,
        bool signedDistanceField = true)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(font);
        if (!font.Texture.IsValid)
        {
            return;
        }

        var run = new GlyphRun(font, text, size, new UiVec2(x, y));
        while (run.MoveNext())
        {
            UiRect box = run.Current.Destination;
            if (signedDistanceField)
            {
                DrawText(batch, font.Texture.ToTextureHandle(), box.X, box.Y, box.Width, box.Height, run.Current.Source.ToRectF(), colour, shading);
            }
            else
            {
                Draw(batch, font.Texture.ToTextureHandle(), box.X, box.Y, box.Width, box.Height, run.Current.Source.ToRectF(), colour);
            }
        }
    }

    /// <summary>Lays a rounded figure from an interface surface atlas on the canvas.</summary>
    /// <param name="batch">The open sprite batch.</param>
    /// <param name="slice">The corner sprite, normally from <see cref="OpenIGI.UI.UiSurfaces"/>.</param>
    /// <param name="centreX">The figure's centre in canvas units.</param>
    /// <param name="centreY">The figure's centre in canvas units.</param>
    /// <param name="halfWidth">Half the figure's width; equal to <paramref name="radius"/> for a disc.</param>
    /// <param name="radius">Half the figure's height, which is also its corner radius.</param>
    /// <param name="colour">The fill colour.</param>
    /// <param name="blend">How the figure combines with what is behind it.</param>
    /// <exception cref="ArgumentNullException"><paramref name="batch"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The pieces are <see cref="NineSlice.GetPieces"/>'s, the same ones the screen painter fills a
    /// rounded rectangle from. A radius of half the height makes it a disc or a capsule, which is what
    /// a marker standing in the level wants from it.
    /// </remarks>
    public void DrawSlice(
        SpriteBatch batch,
        in NineSlice slice,
        float centreX,
        float centreY,
        float halfWidth,
        float radius,
        Bgra32 colour,
        BlendMode blend = BlendMode.AlphaBlend)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (!slice.IsValid || radius <= 0f || halfWidth <= 0f || colour.A == 0)
        {
            return;
        }

        Span<NineSlicePiece> pieces = stackalloc NineSlicePiece[NineSlice.MaxPieces];
        var rect = new UiRect(centreX - halfWidth, centreY - radius, halfWidth * 2f, radius * 2f);
        int count = slice.GetPieces(rect, radius, pieces);
        for (int piece = 0; piece < count; piece++)
        {
            UiRect destination = pieces[piece].Destination;
            Draw(
                batch, slice.Texture.ToTextureHandle(), destination.X, destination.Y, destination.Width, destination.Height,
                pieces[piece].Source.ToRectF(), colour, blend);
        }
    }

    /// <summary>Sweeps an arc across the canvas, with a softened edge on each side.</summary>
    /// <param name="batch">The open sprite batch.</param>
    /// <param name="radius">The radius through the middle of the stroke, in canvas units.</param>
    /// <param name="thickness">The stroke thickness in canvas units.</param>
    /// <param name="startRadians">Where the sweep begins; <c>-π/2</c> is twelve o'clock.</param>
    /// <param name="sweepRadians">How far it sweeps; positive is clockwise on the canvas.</param>
    /// <param name="colour">The stroke colour.</param>
    /// <exception cref="ArgumentNullException"><paramref name="batch"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// One quad per unit of arc, plus two dimmer bands either side of the solid one. The bands are the
    /// coverage the batch will not give a flat quad: without them the arc's edges step, and a single
    /// band still reads as a second thin arc beside the first. An arc is the one figure here that no
    /// rounded rectangle describes, which is why it is drawn rather than sliced.
    /// </remarks>
    public void DrawArc(
        SpriteBatch batch,
        float radius,
        float thickness,
        float startRadians,
        float sweepRadians,
        Bgra32 colour)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (radius <= 0f || thickness <= 0f || sweepRadians <= 0f || colour.A == 0)
        {
            return;
        }

        DrawArcBand(batch, radius, thickness, startRadians, sweepRadians, colour);

        ReadOnlySpan<float> ramp = [0.6f, 0.24f];
        const float Feather = 0.6f;
        float offset = thickness * 0.5f;
        foreach (float step in ramp)
        {
            Bgra32 soft = colour.WithAlpha((byte)(colour.A * step));
            DrawArcBand(batch, radius + offset + (Feather * 0.5f), Feather, startRadians, sweepRadians, soft);
            DrawArcBand(batch, radius - offset - (Feather * 0.5f), Feather, startRadians, sweepRadians, soft);
            offset += Feather;
        }
    }

    /// <summary>One even band of an arc, a quad to about each unit of its length.</summary>
    private void DrawArcBand(
        SpriteBatch batch,
        float radius,
        float thickness,
        float startRadians,
        float sweepRadians,
        Bgra32 colour)
    {
        if (radius <= 0f)
        {
            return;
        }

        float sweep = MathF.Min(sweepRadians, MathF.Tau);
        int steps = Math.Clamp((int)MathF.Ceiling(radius * sweep), 2, 360);
        float inner = MathF.Max(0f, radius - (thickness * 0.5f));
        float outer = radius + (thickness * 0.5f);
        float step = sweep / steps;
        for (int i = 0; i < steps; i++)
        {
            float a = startRadians + (i * step);
            float b = startRadians + ((i + 1) * step);
            Fill(
                batch,
                MathF.Cos(a) * inner, MathF.Sin(a) * inner,
                MathF.Cos(b) * inner, MathF.Sin(b) * inner,
                MathF.Cos(b) * outer, MathF.Sin(b) * outer,
                MathF.Cos(a) * outer, MathF.Sin(a) * outer,
                colour);
        }
    }

    private bool TryCorners(float x, float y, float width, float height, out Corners corners)
    {
        corners = default;
        if (width <= 0f || height <= 0f)
        {
            return false;
        }

        if (!TryProject(x, y, out float ax, out float ay)
            || !TryProject(x + width, y, out float bx, out float by)
            || !TryProject(x + width, y + height, out float cx, out float cy)
            || !TryProject(x, y + height, out float dx, out float dy))
        {
            return false;
        }

        corners = new Corners(ax, ay, bx, by, cx, cy, dx, dy);
        return true;
    }

    private readonly record struct Corners(
        float AX,
        float AY,
        float BX,
        float BY,
        float CX,
        float CY,
        float DX,
        float DY);
}
