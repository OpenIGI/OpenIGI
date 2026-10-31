using OpenIGI.Core.Graphics;

namespace OpenIGI.Rendering.Headless;

public sealed partial class RecordingGraphicsDevice
{
    /// <summary>The managed threads used for animated mesh creation and updates.</summary>
    public List<int> AnimatedMeshThreadIds { get; } = [];

    private uint _nextMesh = 1;

    /// <inheritdoc/>
    public Dictionary<uint, (Vertex3D[] Vertices, ushort[] Indices)> Meshes { get; } = [];

    /// <inheritdoc/>
    public MeshHandle CreateMesh(ReadOnlySpan<Vertex3D> vertices, ReadOnlySpan<ushort> indices)
    {
        MeshHandle handle = new(_nextMesh++);
        Meshes[handle.Id] = (vertices.ToArray(), indices.ToArray());
        return handle;
    }

    /// <summary>The storage meshes' capacities, by mesh id.</summary>
    public Dictionary<uint, (int VertexCapacity, int IndexCapacity)> MeshStorageCapacities { get; } = [];

    /// <summary>How many ranges each storage mesh received.</summary>
    public Dictionary<uint, int> MeshRangeUploads { get; } = [];

    /// <inheritdoc/>
    public MeshHandle CreateMeshStorage(int vertexCapacity, int indexCapacity)
    {
        if (vertexCapacity <= 0 || indexCapacity <= 0)
        {
            throw new ArgumentException(
                $"A storage mesh needs positive capacities, got {vertexCapacity} vertices and {indexCapacity} indices.",
                nameof(vertexCapacity));
        }

        if (vertexCapacity > ushort.MaxValue + 1)
        {
            throw new ArgumentException(
                $"A 16-bit index buffer addresses at most {ushort.MaxValue + 1} vertices, got {vertexCapacity}.",
                nameof(vertexCapacity));
        }

        MeshHandle handle = new(_nextMesh++);
        Meshes[handle.Id] = (new Vertex3D[vertexCapacity], new ushort[indexCapacity]);
        MeshStorageCapacities[handle.Id] = (vertexCapacity, indexCapacity);
        MeshRangeUploads[handle.Id] = 0;
        return handle;
    }

    /// <inheritdoc/>
    public void UploadMeshRange(
        MeshHandle mesh,
        int vertexOffset,
        ReadOnlySpan<Vertex3D> vertices,
        int indexOffset,
        ReadOnlySpan<ushort> indices)
    {
        if (!Meshes.TryGetValue(mesh.Id, out (Vertex3D[] Vertices, ushort[] Indices) entry))
        {
            throw new ArgumentException($"{mesh} does not belong to this device.", nameof(mesh));
        }

        if (!MeshStorageCapacities.TryGetValue(mesh.Id, out (int VertexCapacity, int IndexCapacity) capacity))
        {
            throw new ArgumentException($"{mesh} was not created with {nameof(CreateMeshStorage)}.", nameof(mesh));
        }

        if (vertexOffset < 0 || vertexOffset + vertices.Length > capacity.VertexCapacity)
        {
            throw new ArgumentException(
                $"Vertices {vertexOffset}..{vertexOffset + vertices.Length} lie outside the mesh's capacity of {capacity.VertexCapacity}.",
                nameof(vertices));
        }

        if (indexOffset < 0 || indexOffset + indices.Length > capacity.IndexCapacity)
        {
            throw new ArgumentException(
                $"Indices {indexOffset}..{indexOffset + indices.Length} lie outside the mesh's capacity of {capacity.IndexCapacity}.",
                nameof(indices));
        }

        vertices.CopyTo(entry.Vertices.AsSpan(vertexOffset));
        indices.CopyTo(entry.Indices.AsSpan(indexOffset));
        MeshRangeUploads[mesh.Id]++;
    }

    /// <summary>The meshes created animated, with each one's previous-pose vertices.</summary>
    public Dictionary<uint, Vertex3D[]> PreviousPoseStreams { get; } = [];

    /// <summary>How many times each animated mesh was given a new pose.</summary>
    public Dictionary<uint, int> AnimatedMeshUpdates { get; } = [];

    /// <inheritdoc/>
    public MeshHandle CreateAnimatedMesh(ReadOnlySpan<Vertex3D> vertices, ReadOnlySpan<ushort> indices)
    {
        Record(AnimatedMeshThreadIds, Environment.CurrentManagedThreadId);
        MeshHandle handle = CreateMesh(vertices, indices);
        PreviousPoseStreams[handle.Id] = vertices.ToArray();
        AnimatedMeshUpdates[handle.Id] = 0;
        return handle;
    }

    /// <inheritdoc/>
    public void UpdateAnimatedMesh(MeshHandle mesh, ReadOnlySpan<Vertex3D> vertices)
    {
        Record(AnimatedMeshThreadIds, Environment.CurrentManagedThreadId);
        if (!Meshes.TryGetValue(mesh.Id, out (Vertex3D[] Vertices, ushort[] Indices) entry))
        {
            throw new ArgumentException($"{mesh} does not belong to this device.", nameof(mesh));
        }

        if (!PreviousPoseStreams.ContainsKey(mesh.Id))
        {
            throw new ArgumentException($"{mesh} was not created with {nameof(CreateAnimatedMesh)}.", nameof(mesh));
        }

        if (vertices.Length != entry.Vertices.Length)
        {
            throw new ArgumentException(
                $"An animated mesh's vertex count is fixed: created with {entry.Vertices.Length}, got {vertices.Length}.",
                nameof(vertices));
        }

        // Exactly the backend's contract: what was current becomes the previous-pose stream.
        PreviousPoseStreams[mesh.Id] = entry.Vertices;
        Meshes[mesh.Id] = (vertices.ToArray(), entry.Indices);
        AnimatedMeshUpdates[mesh.Id]++;
    }

    /// <inheritdoc/>
    public void DestroyMesh(MeshHandle mesh)
    {
        Meshes.Remove(mesh.Id);
        PreviousPoseStreams.Remove(mesh.Id);
        AnimatedMeshUpdates.Remove(mesh.Id);
        MeshStorageCapacities.Remove(mesh.Id);
        MeshRangeUploads.Remove(mesh.Id);
    }
}
