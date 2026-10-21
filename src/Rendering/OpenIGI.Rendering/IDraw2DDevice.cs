namespace OpenIGI.Rendering;

/// <summary>Screen-space drawing: the primitive every 2D path funnels through, and the coordinate system it is expressed in.</summary>
/// <remarks>Part of <see cref="IGraphicsDevice"/>.</remarks>
public interface IDraw2DDevice
{
    /// <summary>Draws indexed screen-space triangles.</summary>
    /// <param name="vertices">The vertex buffer.</param>
    /// <param name="indices">Indices into <paramref name="vertices"/>, three per triangle.</param>
    /// <param name="texture">The texture to sample, or <see cref="TextureHandle.None"/> for untextured geometry.</param>
    /// <param name="blend">How fragments combine with the render target.</param>
    /// <exception cref="ArgumentException"><paramref name="indices"/> is not a multiple of three, or an index is out of range.</exception>
    /// <remarks>
    /// This is the primitive every 2D path funnels through: <see cref="SpriteBatch"/> builds the two
    /// buffers and calls this once per texture and blend-mode run.
    /// </remarks>
    void DrawTriangles2D(
        ReadOnlySpan<Vertex2D> vertices,
        ReadOnlySpan<ushort> indices,
        TextureHandle texture,
        BlendMode blend);

    /// <summary>Sets the pixel size of the coordinate system <see cref="Vertex2D"/> positions are expressed in.</summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <remarks>
    /// The original's UI is authored against the back-buffer resolution and needs no projection of its
    /// own; this is the equivalent of that implicit mapping, made explicit so that a UI authored at
    /// 640×480 can be presented at any window size.
    /// </remarks>
    void Set2DProjection(int width, int height);
}
