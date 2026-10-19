using OpenIGI.Core.Graphics;

namespace OpenIGI.Rendering.Vulkan.RayTracing;

/// <summary>What the traced world reads of a mesh: where its triangles are and whether they move.</summary>
/// <remarks>
/// The RHI's meshes answer it, so the traced world reads a mesh without knowing how it was made. Every
/// vertex is a <see cref="Vertex3D"/>, which is what the hit records tell a ray to expect.
/// </remarks>
internal interface IRayTracedMesh
{
    /// <summary>Gets the device address of the vertices this frame draws.</summary>
    ulong VertexBufferAddress { get; }

    /// <summary>Gets the device address of the 16-bit indices.</summary>
    ulong IndexBufferAddress { get; }

    /// <summary>Gets how many vertices the mesh can hold.</summary>
    int VertexCapacity { get; }

    /// <summary>Gets how many indices the mesh can hold.</summary>
    int IndexCapacity { get; }

    /// <summary>Gets how many indices are worth tracing.</summary>
    int DrawableIndexCount { get; }

    /// <summary>Gets whether the vertices are rewritten each pose, so its structure is refitted.</summary>
    bool Animated { get; }

    /// <summary>Gets whether it is a storage mesh whose owner rewrites it.</summary>
    bool Storage { get; }

    /// <summary>Gets how many times the contents have changed.</summary>
    int ContentVersion { get; }
}

/// <summary>Where the queue's submissions have got to, for recycling what the GPU has finished with.</summary>
internal interface ISubmissionClock
{
    /// <summary>Gets the count the newest submission signals.</summary>
    ulong LastSubmittedValue { get; }

    /// <summary>Gets the count the GPU has reached.</summary>
    ulong CompletedValue { get; }
}
