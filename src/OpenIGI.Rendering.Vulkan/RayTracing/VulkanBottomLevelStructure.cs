using OpenIGI.Rendering.Vulkan.Memory;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.RayTracing;

/// <summary>
/// The acceleration structure over one mesh's triangles, in the space its vertices are stored in.
/// </summary>
/// <remarks>
/// <para>
/// A bottom-level structure is built once for a mesh and then referenced by every instance of it. That
/// split is what makes tracing a scene of repeated geometry cheap: a thousand crates share one structure
/// and differ only by the transform their instances carry.
/// </para>
/// <para>
/// Static geometry is built for traversal speed rather than build speed and asks to be compacted
/// afterwards, because a built structure typically shrinks by a third to a half and the world's meshes
/// never change. Geometry that will be rebuilt each frame asks for the opposite and cannot be compacted
/// at all — the two are mutually exclusive, since compaction discards exactly the slack an update needs.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanBottomLevelStructure : IDisposable
{
    // Compacting costs a query, a wait and a copy. Below a few hundred triangles the structure is small
    // enough that the bytes saved are not worth the round trip, which is the same threshold the reference draws.
    private const uint MinimumPrimitivesToCompact = 128;

    private readonly Vk _vk;
    private readonly VulkanRayTracingApi _api;
    private readonly VulkanDevice _device;
    private readonly VulkanAllocator _allocator;
    private readonly VulkanTriangleGeometry[] _geometries;

    private VulkanAccelerationStructure _structure;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VulkanBottomLevelStructure"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="api">The ray-tracing entry points.</param>
    /// <param name="device">The device to build on.</param>
    /// <param name="allocator">The allocator backing the structure.</param>
    /// <param name="geometries">The triangle runs the structure covers.</param>
    /// <param name="allowUpdate">
    /// Whether the structure will be refitted rather than rebuilt when its vertices move. Skinned and
    /// animated geometry wants this; static world geometry does not, and asking for it would forfeit
    /// compaction.
    /// </param>
    public VulkanBottomLevelStructure(
        Vk vk,
        VulkanRayTracingApi api,
        VulkanDevice device,
        VulkanAllocator allocator,
        IReadOnlyList<VulkanTriangleGeometry> geometries,
        bool allowUpdate = false)
    {
        ArgumentNullException.ThrowIfNull(vk);
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(allocator);
        ArgumentNullException.ThrowIfNull(geometries);
        if (geometries.Count == 0)
        {
            throw new ArgumentException("A bottom-level structure needs at least one geometry.", nameof(geometries));
        }

        _vk = vk;
        _api = api;
        _device = device;
        _allocator = allocator;
        _geometries = [.. geometries];
        AllowUpdate = allowUpdate;

        uint primitives = 0;
        foreach (VulkanTriangleGeometry geometry in _geometries)
        {
            primitives += geometry.PrimitiveCount;
        }

        PrimitiveCount = primitives;
        AllowCompaction = !allowUpdate && primitives >= MinimumPrimitivesToCompact;

        AccelerationStructureBuildSizesInfoKHR sizes = QuerySizes();
        BuildScratchSize = VulkanRayTracingApi.Align(sizes.BuildScratchSize, api.ScratchAlignment);
        UpdateScratchSize = VulkanRayTracingApi.Align(sizes.UpdateScratchSize, api.ScratchAlignment);
        _structure = VulkanAccelerationStructure.Create(
            vk,
            api,
            device,
            allocator,
            AccelerationStructureTypeKHR.BottomLevelKhr,
            sizes.AccelerationStructureSize);
    }

    /// <summary>Gets the structure as it currently stands, which changes when it is compacted.</summary>
    public VulkanAccelerationStructure Structure => _structure;

    /// <summary>Gets the structure's device address, for naming it from a top-level instance.</summary>
    public ulong DeviceAddress => _structure.DeviceAddress;

    /// <summary>Gets how many triangles the structure covers.</summary>
    public uint PrimitiveCount { get; }

    /// <summary>Gets the scratch memory a full build needs.</summary>
    public ulong BuildScratchSize { get; }

    /// <summary>Gets the scratch memory a refit needs.</summary>
    public ulong UpdateScratchSize { get; }

    /// <summary>Gets a value indicating whether the structure can be refitted instead of rebuilt.</summary>
    public bool AllowUpdate { get; }

    /// <summary>Gets a value indicating whether the structure asked to be compacted after building.</summary>
    public bool AllowCompaction { get; }

    /// <summary>Gets a value indicating whether the structure has been compacted.</summary>
    public bool IsCompacted { get; private set; }

    /// <summary>Gets a value indicating whether a build has been recorded at least once.</summary>
    public bool IsBuilt { get; private set; }

    /// <summary>Records the build into a command buffer.</summary>
    /// <param name="commandBuffer">Where to record.</param>
    /// <param name="scratchAddress">
    /// The device address of scratch memory at least <see cref="BuildScratchSize"/> bytes long, aligned
    /// to the adapter's scratch alignment.
    /// </param>
    /// <param name="update">
    /// Whether to refit the existing structure rather than rebuild it. Only legal on a structure created
    /// with updates allowed, and only after a full build.
    /// </param>
    /// <remarks>
    /// No barrier is issued here. A batch of builds is followed by one barrier covering all of them,
    /// which is both correct and far cheaper than draining the pipeline between each one.
    /// </remarks>
    public void RecordBuild(CommandBuffer commandBuffer, ulong scratchAddress, bool update = false)
    {
        bool refit = update && AllowUpdate && IsBuilt;

        AccelerationStructureGeometryKHR[] geometries = new AccelerationStructureGeometryKHR[_geometries.Length];
        AccelerationStructureBuildRangeInfoKHR[] ranges =
            new AccelerationStructureBuildRangeInfoKHR[_geometries.Length];

        for (int i = 0; i < _geometries.Length; i++)
        {
            (geometries[i], ranges[i]) = Describe(_geometries[i]);
        }

        fixed (AccelerationStructureGeometryKHR* firstGeometry = geometries)
        fixed (AccelerationStructureBuildRangeInfoKHR* firstRange = ranges)
        {
            AccelerationStructureBuildGeometryInfoKHR info = new()
            {
                SType = StructureType.AccelerationStructureBuildGeometryInfoKhr,
                Type = AccelerationStructureTypeKHR.BottomLevelKhr,
                Flags = BuildFlags(),
                Mode = refit
                    ? BuildAccelerationStructureModeKHR.UpdateKhr
                    : BuildAccelerationStructureModeKHR.BuildKhr,
                SrcAccelerationStructure = refit ? _structure.Handle : default,
                DstAccelerationStructure = _structure.Handle,
                GeometryCount = (uint)geometries.Length,
                PGeometries = firstGeometry,
                ScratchData = new DeviceOrHostAddressKHR { DeviceAddress = scratchAddress },
            };

            AccelerationStructureBuildRangeInfoKHR* ranged = firstRange;
            _api.Structures.CmdBuildAccelerationStructures(commandBuffer, 1, &info, &ranged);
        }

        IsBuilt = true;
    }

    /// <summary>
    /// Records a query of how many bytes the built structure would occupy if compacted.
    /// </summary>
    /// <param name="commandBuffer">Where to record.</param>
    /// <param name="queryPool">The compacted-size query pool.</param>
    /// <param name="queryIndex">Which slot in the pool to write.</param>
    /// <remarks>
    /// Must be recorded after the build has completed on the GPU, not merely after it was recorded — the
    /// size is a property of the finished structure. In practice that means a later frame's command
    /// buffer, which is why the result is collected asynchronously rather than waited for.
    /// </remarks>
    public void RecordCompactedSizeQuery(CommandBuffer commandBuffer, QueryPool queryPool, uint queryIndex)
    {
        AccelerationStructureKHR handle = _structure.Handle;
        _api.Structures.CmdWriteAccelerationStructuresProperties(
            commandBuffer,
            1,
            &handle,
            QueryType.AccelerationStructureCompactedSizeKhr,
            queryPool,
            queryIndex);
    }

    /// <summary>
    /// Records a copy of the structure into a new, exactly-sized one, and hands back the structure it
    /// replaced so the caller can retire it once the GPU is done reading it.
    /// </summary>
    /// <param name="commandBuffer">Where to record.</param>
    /// <param name="compactedSize">The size the query reported.</param>
    /// <returns>The structure that has been replaced, for deferred disposal.</returns>
    /// <remarks>
    /// The old structure cannot be destroyed here: the copy that reads it has only been recorded, not
    /// executed. It is returned instead so it can be attached to the payload whose completion means the
    /// copy has actually happened.
    /// </remarks>
    public VulkanAccelerationStructure RecordCompaction(CommandBuffer commandBuffer, ulong compactedSize)
    {
        VulkanAccelerationStructure previous = _structure;
        VulkanAccelerationStructure compacted = VulkanAccelerationStructure.Create(
            _vk,
            _api,
            _device,
            _allocator,
            AccelerationStructureTypeKHR.BottomLevelKhr,
            compactedSize);

        CopyAccelerationStructureInfoKHR info = new()
        {
            SType = StructureType.CopyAccelerationStructureInfoKhr,
            Src = previous.Handle,
            Dst = compacted.Handle,
            Mode = CopyAccelerationStructureModeKHR.CompactKhr,
        };
        _api.Structures.CmdCopyAccelerationStructure(commandBuffer, &info);

        _structure = compacted;
        IsCompacted = true;
        return previous;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _structure.Dispose();
    }

    private BuildAccelerationStructureFlagsKHR BuildFlags()
    {
        // Static world geometry is traversed far more often than it is built, so the build is told to
        // spend its time producing a tree that traces fast.
        BuildAccelerationStructureFlagsKHR flags = BuildAccelerationStructureFlagsKHR.PreferFastTraceBitKhr;

        if (AllowUpdate)
        {
            flags |= BuildAccelerationStructureFlagsKHR.AllowUpdateBitKhr;
        }

        if (AllowCompaction)
        {
            flags |= BuildAccelerationStructureFlagsKHR.AllowCompactionBitKhr;
        }

        if (_api.PositionFetch)
        {
            flags |= BuildAccelerationStructureFlagsKHR.AllowDataAccessKhr;
        }

        return flags;
    }

    private static (AccelerationStructureGeometryKHR Geometry, AccelerationStructureBuildRangeInfoKHR Range) Describe(
        in VulkanTriangleGeometry source)
    {
        AccelerationStructureGeometryTrianglesDataKHR triangles = new()
        {
            SType = StructureType.AccelerationStructureGeometryTrianglesDataKhr,
            VertexFormat = Format.R32G32B32Sfloat,
            VertexData = new DeviceOrHostAddressConstKHR { DeviceAddress = source.VertexAddress },
            VertexStride = source.VertexStride,

            // The highest vertex index the build will address, which is one less than the count.
            MaxVertex = source.VertexCount == 0 ? 0 : source.VertexCount - 1,
            IndexType = source.Indexed ? IndexType.Uint16 : IndexType.NoneKhr,
            IndexData = new DeviceOrHostAddressConstKHR { DeviceAddress = source.IndexAddress },

            // Per-geometry transforms are not used: instance transforms carry every placement, so a
            // bottom-level structure is always built in the space its vertices are already stored in.
            TransformData = default,
        };

        AccelerationStructureGeometryKHR geometry = new()
        {
            SType = StructureType.AccelerationStructureGeometryKhr,
            GeometryType = GeometryTypeKHR.TrianglesKhr,
            Geometry = new AccelerationStructureGeometryDataKHR { Triangles = triangles },
            Flags = source.Opaque
                ? GeometryFlagsKHR.OpaqueBitKhr
                : GeometryFlagsKHR.NoDuplicateAnyHitInvocationBitKhr,
        };

        AccelerationStructureBuildRangeInfoKHR range = new()
        {
            PrimitiveCount = source.PrimitiveCount,
            PrimitiveOffset = source.Indexed
                ? source.FirstIndex * VulkanTriangleGeometry.IndexStride
                : source.FirstIndex * source.VertexStride,
            FirstVertex = 0,
            TransformOffset = 0,
        };

        return (geometry, range);
    }

    private AccelerationStructureBuildSizesInfoKHR QuerySizes()
    {
        AccelerationStructureGeometryKHR[] geometries = new AccelerationStructureGeometryKHR[_geometries.Length];
        uint[] primitiveCounts = new uint[_geometries.Length];
        for (int i = 0; i < _geometries.Length; i++)
        {
            (geometries[i], _) = Describe(_geometries[i]);
            primitiveCounts[i] = _geometries[i].PrimitiveCount;
        }

        AccelerationStructureBuildSizesInfoKHR sizes = new()
        {
            SType = StructureType.AccelerationStructureBuildSizesInfoKhr,
        };

        fixed (AccelerationStructureGeometryKHR* firstGeometry = geometries)
        fixed (uint* firstCount = primitiveCounts)
        {
            AccelerationStructureBuildGeometryInfoKHR info = new()
            {
                SType = StructureType.AccelerationStructureBuildGeometryInfoKhr,
                Type = AccelerationStructureTypeKHR.BottomLevelKhr,
                Flags = BuildFlags(),
                Mode = BuildAccelerationStructureModeKHR.BuildKhr,
                GeometryCount = (uint)geometries.Length,
                PGeometries = firstGeometry,
            };

            _api.Structures.GetAccelerationStructureBuildSizes(
                _device.Device,
                AccelerationStructureBuildTypeKHR.DeviceKhr,
                &info,
                firstCount,
                &sizes);
        }

        return sizes;
    }
}
