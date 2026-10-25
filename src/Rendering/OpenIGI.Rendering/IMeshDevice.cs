using OpenIGI.Core.Graphics;

namespace OpenIGI.Rendering;

/// <summary>The meshes a device holds: immutable, animated, and the fixed-capacity storage meshes filled by range.</summary>
/// <remarks>Part of <see cref="IGraphicsDevice"/>. Drawing them is <see cref="IMeshDrawDevice"/>.</remarks>
public interface IMeshDevice
{
    /// <summary>Uploads a mesh and returns a handle to it.</summary>
    /// <param name="vertices">The vertices.</param>
    /// <param name="indices">Indices into <paramref name="vertices"/>, three per triangle.</param>
    /// <returns>A handle to the new mesh.</returns>
    /// <exception cref="ArgumentException"><paramref name="indices"/> is not a multiple of three, or an index is out of range.</exception>
    /// <exception cref="GraphicsDeviceException">The mesh could not be created.</exception>
    /// <remarks>
    /// Meshes are immutable once uploaded. The original packs many models into a shared vertex buffer
    /// and flushes it when the running vertex count would pass 65535 (<c>0x49B430</c>/<c>0x49B4B0</c>) —
    /// a constraint of the hardware it targeted, not of the data, so OpenIGI gives each model its own
    /// buffer and keeps the 16-bit indices the models already use.
    /// </remarks>
    MeshHandle CreateMesh(ReadOnlySpan<Vertex3D> vertices, ReadOnlySpan<ushort> indices);

    /// <summary>Uploads a mesh whose vertices will be replaced every pose, keeping the pose before.</summary>
    /// <param name="vertices">The rest pose, replaced by the first <see cref="IGraphicsDevice.UpdateAnimatedMesh"/>.</param>
    /// <param name="indices">The triangle list, three indices a triangle. Fixed for the mesh's life.</param>
    /// <returns>A handle naming the mesh.</returns>
    /// <remarks>
    /// <para>
    /// For CPU-skinned bodies. An ordinary mesh is immutable; an animated one is double-buffered, and
    /// each <see cref="IGraphicsDevice.UpdateAnimatedMesh"/> makes the vertices it replaces the mesh's <em>previous
    /// pose</em> — a second vertex stream the shaders can reach. That stream is what makes two things
    /// possible for a skinned body that a rigid transform cannot express: exact per-vertex motion for
    /// the temporal resolve, and drawing <em>between</em> two
    /// simulation poses (see <see cref="IDrawStateDevice.SetPoseInterpolation"/>).
    /// </para>
    /// <para>
    /// A mesh made by <see cref="CreateMesh"/> reports its own vertices as its previous pose, so every
    /// static mesh has exactly zero pose motion without paying for a second buffer.
    /// </para>
    /// </remarks>
    MeshHandle CreateAnimatedMesh(ReadOnlySpan<Vertex3D> vertices, ReadOnlySpan<ushort> indices);

    /// <summary>Allocates an empty mesh with fixed capacity, to be filled by <see cref="UploadMeshRange"/>.</summary>
    /// <param name="vertexCapacity">How many vertices the mesh can ever hold.</param>
    /// <param name="indexCapacity">How many indices the mesh can ever hold.</param>
    /// <returns>A handle naming the mesh, which draws nothing until a range is uploaded.</returns>
    /// <exception cref="ArgumentException">A capacity is not positive, or the vertex capacity exceeds 16-bit indexing.</exception>
    /// <exception cref="GraphicsDeviceException">The mesh could not be created.</exception>
    /// <remarks>
    /// For meshes that grow by appending — the terrain's batch pages. A mesh from
    /// <see cref="CreateMesh"/> is immutable, so growing one means destroying it and re-uploading
    /// everything it held; done every time a streamed-in chunk joins a page, those re-uploads were the
    /// single largest source of frame spikes. A storage mesh uploads each appended range once.
    /// </remarks>
    MeshHandle CreateMeshStorage(int vertexCapacity, int indexCapacity);

    /// <summary>Writes a range of vertices and indices into a mesh made by <see cref="CreateMeshStorage"/>.</summary>
    /// <param name="mesh">A mesh from <see cref="CreateMeshStorage"/>.</param>
    /// <param name="vertexOffset">The first vertex slot written.</param>
    /// <param name="vertices">The vertices to write; may be empty.</param>
    /// <param name="indexOffset">The first index slot written.</param>
    /// <param name="indices">The indices to write, addressing the mesh's own vertex slots; may be empty.</param>
    /// <exception cref="ArgumentException">
    /// The handle is unknown or not a storage mesh, a range runs past its capacity, or an index
    /// addresses a slot past the vertex capacity.
    /// </exception>
    /// <remarks>
    /// Ranges written are drawable immediately; the mesh's drawable index count is the highest index
    /// slot ever written plus one. Callers append, so ranges beyond what the GPU has drawn are new
    /// storage and never stall the pipeline.
    /// </remarks>
    void UploadMeshRange(
        MeshHandle mesh,
        int vertexOffset,
        ReadOnlySpan<Vertex3D> vertices,
        int indexOffset,
        ReadOnlySpan<ushort> indices);

    /// <summary>Releases a mesh.</summary>
    /// <param name="mesh">The mesh to release. <see cref="MeshHandle.None"/> is ignored.</param>
    void DestroyMesh(MeshHandle mesh);
}
