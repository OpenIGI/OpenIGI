using OpenIGI.Core.Graphics;

namespace OpenIGI.Rendering;

/// <summary>
/// Accumulates screen-space quads and submits them to an <see cref="IGraphicsDevice"/> in as few draw
/// calls as the texture and blend-mode changes allow.
/// </summary>
/// <remarks>
/// <para>
/// Quads are emitted in submission order and flushed whenever the texture or blend mode changes, so
/// the painter's-algorithm layering the original's UI relies on is preserved exactly. Reordering to
/// merge more draws would be faster and wrong.
/// </para>
/// <para>
/// The batch is not thread-safe and must be used from the device's thread.
/// </para>
/// </remarks>
public sealed class SpriteBatch
{
    /// <summary>The largest number of quads one flush can hold, bounded by the 16-bit index format.</summary>
    public const int MaxQuadsPerFlush = ushort.MaxValue / 4;

    private readonly IGraphicsDevice _device;
    private readonly ISdfTextDevice? _sdfDevice;
    private readonly Vertex2D[] _vertices;
    private readonly ushort[] _indices;
    private readonly int _capacityInQuads;

    private int _quadCount;
    private TextureHandle _texture;
    private BlendMode _blend;
    private bool _isText;
    private SdfTextParams _textParams;
    private bool _inBatch;

    /// <summary>Initializes a new instance of the <see cref="SpriteBatch"/> class.</summary>
    /// <param name="device">The device to submit to.</param>
    /// <param name="capacityInQuads">The number of quads buffered before an automatic flush.</param>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacityInQuads"/> is not between one and <see cref="MaxQuadsPerFlush"/>.</exception>
    public SpriteBatch(IGraphicsDevice device, int capacityInQuads = 2048)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacityInQuads, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(capacityInQuads, MaxQuadsPerFlush);

        _device = device;
        _sdfDevice = device as ISdfTextDevice;
        _capacityInQuads = capacityInQuads;
        _vertices = new Vertex2D[capacityInQuads * 4];
        _indices = new ushort[capacityInQuads * 6];

        // Index layout is fixed for the lifetime of the batch: quad n always spans vertices 4n..4n+3.
        for (int quad = 0; quad < capacityInQuads; quad++)
        {
            int v = quad * 4;
            int i = quad * 6;
            _indices[i + 0] = (ushort)(v + 0);
            _indices[i + 1] = (ushort)(v + 1);
            _indices[i + 2] = (ushort)(v + 2);
            _indices[i + 3] = (ushort)(v + 0);
            _indices[i + 4] = (ushort)(v + 2);
            _indices[i + 5] = (ushort)(v + 3);
        }
    }

    /// <summary>Gets the number of draw calls the last <see cref="Begin"/>/<see cref="End"/> pair issued.</summary>
    public int DrawCallCount { get; private set; }

    /// <summary>Gets the number of quads the last <see cref="Begin"/>/<see cref="End"/> pair submitted.</summary>
    public int QuadCount { get; private set; }

    /// <summary>Opens a batch.</summary>
    /// <exception cref="InvalidOperationException">A batch is already open.</exception>
    public void Begin()
    {
        if (_inBatch)
        {
            throw new InvalidOperationException("Begin was called while a batch was already open.");
        }

        _inBatch = true;
        _quadCount = 0;
        _texture = TextureHandle.None;
        _blend = BlendMode.AlphaBlend;
        _isText = false;
        _textParams = default;
        DrawCallCount = 0;
        QuadCount = 0;
    }

    /// <summary>Queues an axis-aligned textured quad.</summary>
    /// <param name="texture">The texture to sample, or <see cref="TextureHandle.None"/> for a flat fill.</param>
    /// <param name="destination">The destination rectangle in pixels.</param>
    /// <param name="source">The source rectangle in texture coordinates.</param>
    /// <param name="color">The colour modulated with the texture sample.</param>
    /// <param name="blend">How the quad combines with what is behind it.</param>
    /// <exception cref="InvalidOperationException">No batch is open.</exception>
    public void Draw(
        TextureHandle texture,
        in RectF destination,
        in RectF source,
        Bgra32 color,
        BlendMode blend = BlendMode.AlphaBlend)
    {
        float left = destination.X;
        float top = destination.Y;
        float right = destination.Right;
        float bottom = destination.Bottom;

        DrawQuad(
            texture,
            new Vertex2D(left, top, source.X, source.Y, color),
            new Vertex2D(right, top, source.Right, source.Y, color),
            new Vertex2D(right, bottom, source.Right, source.Bottom, color),
            new Vertex2D(left, bottom, source.X, source.Bottom, color),
            blend);
    }

    /// <summary>Queues a solid-colour rectangle.</summary>
    /// <param name="destination">The destination rectangle in pixels.</param>
    /// <param name="color">The fill colour.</param>
    /// <param name="blend">How the rectangle combines with what is behind it.</param>
    /// <exception cref="InvalidOperationException">No batch is open.</exception>
    public void FillRect(in RectF destination, Bgra32 color, BlendMode blend = BlendMode.AlphaBlend) =>
        Draw(TextureHandle.None, destination, RectF.Unit, color, blend);

    /// <summary>Queues an arbitrary quad from its four corners, in clockwise order from the top-left.</summary>
    /// <param name="texture">The texture to sample, or <see cref="TextureHandle.None"/> for a flat fill.</param>
    /// <param name="topLeft">The first corner.</param>
    /// <param name="topRight">The second corner.</param>
    /// <param name="bottomRight">The third corner.</param>
    /// <param name="bottomLeft">The fourth corner.</param>
    /// <param name="blend">How the quad combines with what is behind it.</param>
    /// <exception cref="InvalidOperationException">No batch is open.</exception>
    public void DrawQuad(
        TextureHandle texture,
        in Vertex2D topLeft,
        in Vertex2D topRight,
        in Vertex2D bottomRight,
        in Vertex2D bottomLeft,
        BlendMode blend = BlendMode.AlphaBlend)
    {
        if (!_inBatch)
        {
            throw new InvalidOperationException("Draw was called outside a Begin/End pair.");
        }

        // A pending run of text-shaded quads cannot merge with a plain textured one, so switching mode
        // flushes as surely as switching texture or blend does.
        if (_quadCount > 0 && (texture != _texture || blend != _blend || _isText))
        {
            Flush();
        }

        if (_quadCount == _capacityInQuads)
        {
            Flush();
        }

        _texture = texture;
        _blend = blend;
        _isText = false;

        int v = _quadCount * 4;
        _vertices[v + 0] = topLeft;
        _vertices[v + 1] = topRight;
        _vertices[v + 2] = bottomRight;
        _vertices[v + 3] = bottomLeft;
        _quadCount++;
    }

    /// <summary>Queues a distance-field text quad, shaded by the SDF program when the device supports it.</summary>
    /// <param name="atlas">The signed-distance-field atlas to sample.</param>
    /// <param name="destination">The destination rectangle in pixels.</param>
    /// <param name="source">The source rectangle in texture coordinates.</param>
    /// <param name="color">The fill colour modulated with the glyph.</param>
    /// <param name="parameters">How to shade the run — spread, outline and glow.</param>
    /// <param name="blend">How the quad combines with what is behind it.</param>
    /// <exception cref="InvalidOperationException">No batch is open.</exception>
    /// <remarks>
    /// When the device does not implement <see cref="ISdfTextDevice"/> — the recording device the tests
    /// use — this draws exactly as <see cref="Draw"/> does, so a coverage atlas still renders and the
    /// draw is still recorded; only the crisp thresholding and the outline and glow are absent.
    /// </remarks>
    public void DrawText(
        TextureHandle atlas,
        in RectF destination,
        in RectF source,
        Bgra32 color,
        in SdfTextParams parameters,
        BlendMode blend = BlendMode.AlphaBlend)
    {
        if (!_inBatch)
        {
            throw new InvalidOperationException("DrawText was called outside a Begin/End pair.");
        }

        // A device that cannot shade a distance field draws the atlas as ordinary coverage instead.
        if (_sdfDevice is null)
        {
            Draw(atlas, destination, source, color, blend);
            return;
        }

        bool differentRun = atlas != _texture || blend != _blend || !_isText || !_textParams.Equals(parameters);
        if (_quadCount > 0 && differentRun)
        {
            Flush();
        }

        if (_quadCount == _capacityInQuads)
        {
            Flush();
        }

        _texture = atlas;
        _blend = blend;
        _isText = true;
        _textParams = parameters;

        float left = destination.X;
        float top = destination.Y;
        float right = destination.Right;
        float bottom = destination.Bottom;

        int v = _quadCount * 4;
        _vertices[v + 0] = new Vertex2D(left, top, source.X, source.Y, color);
        _vertices[v + 1] = new Vertex2D(right, top, source.Right, source.Y, color);
        _vertices[v + 2] = new Vertex2D(right, bottom, source.Right, source.Bottom, color);
        _vertices[v + 3] = new Vertex2D(left, bottom, source.X, source.Bottom, color);
        _quadCount++;
    }

    /// <summary>Queues a distance-field glyph on four arbitrary corners, for text projected into the world.</summary>
    /// <param name="atlas">The distance-field atlas.</param>
    /// <param name="topLeft">The glyph's top-left corner, with its atlas coordinate and colour.</param>
    /// <param name="topRight">The top-right corner.</param>
    /// <param name="bottomRight">The bottom-right corner.</param>
    /// <param name="bottomLeft">The bottom-left corner.</param>
    /// <param name="parameters">The edge, outline and glow.</param>
    /// <param name="blend">How the glyph combines with what is under it.</param>
    /// <remarks>
    /// <see cref="DrawText"/> with the corners free: a glyph standing in the level is a perspective
    /// quadrilateral on screen, not a rectangle. The atlas coordinates interpolate affinely across it,
    /// which is exact for a rectangle and a hair off for a strongly foreshortened one -- invisible at
    /// the sizes and angles a title is read at. On a device with no distance-field shading it falls
    /// back to coverage, as the rectangular path does.
    /// </remarks>
    public void DrawTextQuad(
        TextureHandle atlas,
        in Vertex2D topLeft,
        in Vertex2D topRight,
        in Vertex2D bottomRight,
        in Vertex2D bottomLeft,
        in SdfTextParams parameters,
        BlendMode blend = BlendMode.AlphaBlend)
    {
        if (!_inBatch)
        {
            throw new InvalidOperationException("DrawTextQuad was called outside a Begin/End pair.");
        }

        if (_sdfDevice is null)
        {
            DrawQuad(atlas, topLeft, topRight, bottomRight, bottomLeft, blend);
            return;
        }

        bool differentRun = atlas != _texture || blend != _blend || !_isText || !_textParams.Equals(parameters);
        if (_quadCount > 0 && differentRun)
        {
            Flush();
        }

        if (_quadCount == _capacityInQuads)
        {
            Flush();
        }

        _texture = atlas;
        _blend = blend;
        _isText = true;
        _textParams = parameters;

        int v = _quadCount * 4;
        _vertices[v + 0] = topLeft;
        _vertices[v + 1] = topRight;
        _vertices[v + 2] = bottomRight;
        _vertices[v + 3] = bottomLeft;
        _quadCount++;
    }


    /// <summary>Flushes any pending quads and closes the batch.</summary>
    /// <exception cref="InvalidOperationException">No batch is open.</exception>

    public void End()
    {
        if (!_inBatch)
        {
            throw new InvalidOperationException("End was called without a matching Begin.");
        }

        Flush();
        _inBatch = false;
    }

    private void Flush()
    {
        if (_quadCount == 0)
        {
            return;
        }

        if (_isText && _sdfDevice is not null)
        {
            _sdfDevice.DrawText2D(
                _vertices.AsSpan(0, _quadCount * 4),
                _indices.AsSpan(0, _quadCount * 6),
                _texture,
                _textParams,
                _blend);
        }
        else
        {
            _device.DrawTriangles2D(
                _vertices.AsSpan(0, _quadCount * 4),
                _indices.AsSpan(0, _quadCount * 6),
                _texture,
                _blend);
        }

        DrawCallCount++;
        QuadCount += _quadCount;
        _quadCount = 0;
    }
}
