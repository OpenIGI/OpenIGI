using OpenIGI.Rendering;

namespace OpenIGI.TestSupport;

public sealed partial class RecordingGraphicsDevice
{
    public List<DrawCall> Draws { get; } = [];

    public void DrawTriangles2D(
        ReadOnlySpan<Vertex2D> vertices,
        ReadOnlySpan<ushort> indices,
        TextureHandle texture,
        BlendMode blend) =>
        Draws.Add(new DrawCall(vertices.ToArray(), indices.ToArray(), texture, blend));

    public void Set2DProjection(int width, int height)
    {
    }
}
