namespace OpenIGI.Rendering.Vulkan.RayTracing;

/// <summary>One run of triangles a bottom-level acceleration structure is built from.</summary>
/// <remarks>
/// <para>
/// A structure can be built from several of these at once, and traversal reports which one it hit. That
/// is how a mesh drawn as several material ranges stays one structure: each range is a geometry, the hit
/// carries its index, and the shading looks up the material from that rather than the mesh needing to be
/// split.
/// </para>
/// <para>
/// Everything here is expressed as device addresses rather than buffer handles because that is what a
/// build command takes — it reads the geometry directly out of memory with no descriptor in between.
/// </para>
/// </remarks>
/// <param name="VertexAddress">The device address of the first vertex's position.</param>
/// <param name="VertexStride">The byte distance between consecutive vertices.</param>
/// <param name="VertexCount">How many vertices the geometry addresses.</param>
/// <param name="IndexAddress">The device address of the first index, or zero when unindexed.</param>
/// <param name="PrimitiveCount">How many triangles the geometry contains.</param>
/// <param name="FirstIndex">The index of the first index to read, for a range within a shared buffer.</param>
/// <param name="Opaque">
/// Whether traversal may accept a hit without running an any-hit shader. True for solid geometry; false
/// only where a cut-out texture decides whether the hit exists at all.
/// </param>
internal readonly record struct VulkanTriangleGeometry(
    ulong VertexAddress,
    uint VertexStride,
    uint VertexCount,
    ulong IndexAddress,
    uint PrimitiveCount,
    uint FirstIndex = 0,
    bool Opaque = true)
{
    /// <summary>The number of bytes one index occupies. The backend's meshes index with 16-bit values.</summary>
    public const uint IndexStride = 2;

    /// <summary>Gets a value indicating whether the geometry reads through an index buffer.</summary>
    public bool Indexed => IndexAddress != 0;
}
