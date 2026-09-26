using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline.Draw2D;

/// <summary>What a vertex of the 2D path is, laid out for the RHI.</summary>
public static class SpriteVertexLayout
{
    /// <summary>Gets the layout of the vertex buffer every 2D pipeline reads.</summary>
    /// <returns>The layout: position, texture coordinate, and a packed colour.</returns>
    public static RhiVertexBufferLayout Layout() => new(
        Vertex2D.Size,
        RhiVertexStepMode.Vertex,
        [
            new RhiVertexElement(0, RhiVertexFormat.Float32x2, 0),
            new RhiVertexElement(1, RhiVertexFormat.Float32x2, sizeof(float) * 2),
            new RhiVertexElement(2, RhiVertexFormat.Unorm8x4, sizeof(float) * 4),
        ]);
}
