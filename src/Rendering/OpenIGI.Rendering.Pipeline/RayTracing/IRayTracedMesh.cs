using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline.RayTracing;

/// <summary>A mesh as the traced world reads it: its buffers, as of its latest sighting.</summary>
internal interface IRayTracedMesh
{
    /// <summary>Gets the vertex buffer this frame draws from, created as ray-tracing input.</summary>
    IRhiBuffer Vertices { get; }

    /// <summary>Gets the 16-bit index buffer, created as ray-tracing input.</summary>
    IRhiBuffer Indices { get; }

    /// <summary>Gets how many vertices the buffer holds.</summary>
    int VertexCapacity { get; }

    /// <summary>Gets how many indices the buffer holds.</summary>
    int IndexCapacity { get; }

    /// <summary>Gets how many indices are worth tracing.</summary>
    int DrawableIndexCount { get; }

    /// <summary>Gets a value indicating whether the vertices are rewritten each pose, so the structure is refitted.</summary>
    bool Animated { get; }

    /// <summary>Gets a value indicating whether it is a storage mesh its owner rewrites.</summary>
    bool Storage { get; }

    /// <summary>Gets how many times the contents have changed.</summary>
    int ContentVersion { get; }
}
