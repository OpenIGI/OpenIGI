namespace OpenIGI.Rendering.Headless;

public sealed partial class RecordingGraphicsDevice
{
    /// <summary>Gets the 2-D draws, in order.</summary>
    public List<DrawCall> Draws { get; } = [];

    /// <inheritdoc/>
    public void DrawTriangles2D(
        ReadOnlySpan<Vertex2D> vertices,
        ReadOnlySpan<ushort> indices,
        TextureHandle texture,
        BlendMode blend) =>
        Record(Draws, new DrawCall(vertices.ToArray(), indices.ToArray(), texture, blend));

    /// <inheritdoc/>
    public void Set2DProjection(int width, int height)
    {
    }
}
