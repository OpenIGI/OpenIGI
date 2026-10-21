using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Vulkan.RayTracing;
using OpenIGI.Rendering.Vulkan.Sync;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Rhi;

/// <summary>
/// Acceleration structures on Vulkan: <c>VK_KHR_acceleration_structure</c> behind the interface.
/// </summary>
/// <remarks>
/// <para>
/// Only the API: sizing, creating and recording builds. Which meshes are built, when, and how long a
/// structure outlives the frames tracing it is the traced world's, above the RHI, written once for every
/// backend.
/// </para>
/// <para>
/// Every structure is built to allow data access where the adapter offers position fetch, so a hit can
/// report the triangle it struck without the shader reaching back into the mesh's buffers.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanRhiAccelerationStructures : IRhiAccelerationStructures, IDisposable
{
    private readonly Vk _vk;
    private readonly VulkanRayTracingApi _api;
    private readonly VulkanDevice _device;
    private readonly Memory.VulkanAllocator _allocator;
    private readonly VulkanBarrierBatcher _barriers;

    /// <summary>Initialises a new instance of the <see cref="VulkanRhiAccelerationStructures"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="api">The acceleration-structure entry points, which this now owns.</param>
    /// <param name="device">The device to build on.</param>
    /// <param name="allocator">The allocator backing every structure.</param>
    public VulkanRhiAccelerationStructures(Vk vk, VulkanRayTracingApi api, VulkanDevice device, Memory.VulkanAllocator allocator)
    {
        _vk = vk;
        _api = api;
        _device = device;
        _allocator = allocator;
        _barriers = new VulkanBarrierBatcher(vk);
    }

    /// <inheritdoc/>
    public ulong ScratchAlignment => _api.ScratchAlignment;

    /// <inheritdoc/>
    public int MaxInstances => (int)Math.Min(_api.MaxInstanceCount, int.MaxValue);

    /// <inheritdoc/>
    public RhiAccelerationStructureSizes BottomLevelSizes(
        ReadOnlySpan<RhiTriangleGeometry> geometries,
        RhiAccelerationBuildOptions flags)
    {
        if (geometries.IsEmpty)
        {
            throw new ArgumentException("A bottom-level structure needs at least one geometry.", nameof(geometries));
        }

        AccelerationStructureGeometryKHR[] described = new AccelerationStructureGeometryKHR[geometries.Length];
        uint[] primitiveCounts = new uint[geometries.Length];
        for (int i = 0; i < geometries.Length; i++)
        {
            (described[i], _) = Describe(geometries[i]);
            primitiveCounts[i] = geometries[i].PrimitiveCount;
        }

        fixed (AccelerationStructureGeometryKHR* firstGeometry = described)
        fixed (uint* firstCount = primitiveCounts)
        {
            AccelerationStructureBuildGeometryInfoKHR info = new()
            {
                SType = StructureType.AccelerationStructureBuildGeometryInfoKhr,
                Type = AccelerationStructureTypeKHR.BottomLevelKhr,
                Flags = BuildFlags(flags),
                Mode = BuildAccelerationStructureModeKHR.BuildKhr,
                GeometryCount = (uint)described.Length,
                PGeometries = firstGeometry,
            };

            return Sizes(&info, firstCount);
        }
    }

    /// <inheritdoc/>
    public RhiAccelerationStructureSizes TopLevelSizes(int instanceCount, RhiAccelerationBuildOptions flags)
    {
        AccelerationStructureGeometryKHR geometry = InstancesGeometry(0);
        AccelerationStructureBuildGeometryInfoKHR info = new()
        {
            SType = StructureType.AccelerationStructureBuildGeometryInfoKhr,
            Type = AccelerationStructureTypeKHR.TopLevelKhr,
            Flags = BuildFlags(flags),
            Mode = BuildAccelerationStructureModeKHR.BuildKhr,
            GeometryCount = 1,
            PGeometries = &geometry,
        };

        uint count = (uint)Math.Max(1, instanceCount);
        return Sizes(&info, &count);
    }

    /// <inheritdoc/>
    public IRhiAccelerationStructure Create(in RhiAccelerationStructureDescriptor descriptor)
    {
        VulkanAccelerationStructure structure = VulkanAccelerationStructure.Create(
            _vk,
            _api,
            _device,
            _allocator,
            descriptor.Kind == RhiAccelerationStructureKind.TopLevel
                ? AccelerationStructureTypeKHR.TopLevelKhr
                : AccelerationStructureTypeKHR.BottomLevelKhr,
            descriptor.Size);

        return new VulkanRhiAccelerationStructure(structure, descriptor.Kind, descriptor.Label);
    }

    /// <summary>Records a batch of builds, with the barriers that order it against what came before and after.</summary>
    /// <param name="commandBuffer">Where to record, outside any render pass.</param>
    /// <param name="bottomLevel">The bottom-level builds and refits.</param>
    /// <param name="topLevel">The top-level build, if any.</param>
    public void RecordBuilds(
        CommandBuffer commandBuffer,
        ReadOnlySpan<RhiBottomLevelBuild> bottomLevel,
        RhiTopLevelBuild? topLevel)
    {
        // Before anything is written: the structures about to be rebuilt are the ones the frames already in
        // flight are tracing rays against, and a build is a write over what those rays are reading. And the
        // geometry the builds read was written this same frame -- a terrain page refilled, a body re-skinned
        // -- by a copy or a compute pass recorded earlier in the frame.
        _barriers.AddAccelerationStructureReadToBuild();
        _barriers.AddGeometryWriteToBuild();
        _barriers.Flush(commandBuffer);

        // No barrier between the bottom-level builds: one covering all of them is both correct and far
        // cheaper than draining the pipeline between each.
        foreach (RhiBottomLevelBuild build in bottomLevel)
        {
            RecordBottomLevel(commandBuffer, build);
        }

        if (topLevel is { } top)
        {
            // Everything above wrote acceleration structures the scene build is about to read.
            _barriers.AddAccelerationStructureBuild();
            _barriers.Flush(commandBuffer);
            RecordTopLevel(commandBuffer, top);
        }

        // And the last build is finished before anything after this traces against it.
        _barriers.AddAccelerationStructureBuild();
        _barriers.Flush(commandBuffer);
    }

    /// <inheritdoc/>
    public void Dispose() => _api.Dispose();

    private void RecordBottomLevel(CommandBuffer commandBuffer, in RhiBottomLevelBuild build)
    {
        ReadOnlySpan<RhiTriangleGeometry> source = build.Geometries.Span;
        AccelerationStructureGeometryKHR[] geometries = new AccelerationStructureGeometryKHR[source.Length];
        AccelerationStructureBuildRangeInfoKHR[] ranges = new AccelerationStructureBuildRangeInfoKHR[source.Length];

        for (int i = 0; i < source.Length; i++)
        {
            (geometries[i], ranges[i]) = Describe(source[i]);
        }

        AccelerationStructureKHR destination = ((VulkanRhiAccelerationStructure)build.Destination).Handle;

        fixed (AccelerationStructureGeometryKHR* firstGeometry = geometries)
        fixed (AccelerationStructureBuildRangeInfoKHR* firstRange = ranges)
        {
            AccelerationStructureBuildGeometryInfoKHR info = new()
            {
                SType = StructureType.AccelerationStructureBuildGeometryInfoKhr,
                Type = AccelerationStructureTypeKHR.BottomLevelKhr,
                Flags = BuildFlags(build.Flags),
                Mode = build.Update
                    ? BuildAccelerationStructureModeKHR.UpdateKhr
                    : BuildAccelerationStructureModeKHR.BuildKhr,
                SrcAccelerationStructure = build.Update ? destination : default,
                DstAccelerationStructure = destination,
                GeometryCount = (uint)geometries.Length,
                PGeometries = firstGeometry,
                ScratchData = new DeviceOrHostAddressKHR { DeviceAddress = Address(build.Scratch) + build.ScratchOffset },
            };

            AccelerationStructureBuildRangeInfoKHR* ranged = firstRange;
            _api.Structures.CmdBuildAccelerationStructures(commandBuffer, 1, &info, &ranged);
        }
    }

    private void RecordTopLevel(CommandBuffer commandBuffer, in RhiTopLevelBuild build)
    {
        AccelerationStructureGeometryKHR geometry = InstancesGeometry(Address(build.Instances) + build.InstanceOffset);

        AccelerationStructureBuildRangeInfoKHR range = new()
        {
            PrimitiveCount = (uint)build.InstanceCount,
            PrimitiveOffset = 0,
            FirstVertex = 0,
            TransformOffset = 0,
        };

        AccelerationStructureBuildGeometryInfoKHR info = new()
        {
            SType = StructureType.AccelerationStructureBuildGeometryInfoKhr,
            Type = AccelerationStructureTypeKHR.TopLevelKhr,
            Flags = BuildFlags(build.Flags),
            Mode = BuildAccelerationStructureModeKHR.BuildKhr,
            DstAccelerationStructure = ((VulkanRhiAccelerationStructure)build.Destination).Handle,
            GeometryCount = 1,
            PGeometries = &geometry,
            ScratchData = new DeviceOrHostAddressKHR { DeviceAddress = Address(build.Scratch) + build.ScratchOffset },
        };

        AccelerationStructureBuildRangeInfoKHR* ranged = &range;
        _api.Structures.CmdBuildAccelerationStructures(commandBuffer, 1, &info, &ranged);
    }

    private RhiAccelerationStructureSizes Sizes(AccelerationStructureBuildGeometryInfoKHR* info, uint* counts)
    {
        AccelerationStructureBuildSizesInfoKHR sizes = new()
        {
            SType = StructureType.AccelerationStructureBuildSizesInfoKhr,
        };

        _api.Structures.GetAccelerationStructureBuildSizes(
            _device.Device,
            AccelerationStructureBuildTypeKHR.DeviceKhr,
            info,
            counts,
            &sizes);

        return new RhiAccelerationStructureSizes(
            sizes.AccelerationStructureSize,
            VulkanRayTracingApi.Align(sizes.BuildScratchSize, _api.ScratchAlignment),
            VulkanRayTracingApi.Align(sizes.UpdateScratchSize, _api.ScratchAlignment));
    }

    private BuildAccelerationStructureFlagsKHR BuildFlags(RhiAccelerationBuildOptions flags)
    {
        BuildAccelerationStructureFlagsKHR mapped = 0;

        if (flags.HasFlag(RhiAccelerationBuildOptions.PreferFastTrace))
        {
            mapped |= BuildAccelerationStructureFlagsKHR.PreferFastTraceBitKhr;
        }

        if (flags.HasFlag(RhiAccelerationBuildOptions.AllowUpdate))
        {
            mapped |= BuildAccelerationStructureFlagsKHR.AllowUpdateBitKhr;
        }

        if (_api.PositionFetch)
        {
            mapped |= BuildAccelerationStructureFlagsKHR.AllowDataAccessKhr;
        }

        return mapped;
    }

    private static ulong Address(IRhiBuffer buffer) => ((VulkanRhiBuffer)buffer).Buffer.DeviceAddress;

    private static AccelerationStructureGeometryKHR InstancesGeometry(ulong address) => new()
    {
        SType = StructureType.AccelerationStructureGeometryKhr,
        GeometryType = GeometryTypeKHR.InstancesKhr,
        Geometry = new AccelerationStructureGeometryDataKHR
        {
            Instances = new AccelerationStructureGeometryInstancesDataKHR
            {
                SType = StructureType.AccelerationStructureGeometryInstancesDataKhr,
                ArrayOfPointers = false,
                Data = new DeviceOrHostAddressConstKHR { DeviceAddress = address },
            },
        },
        Flags = GeometryFlagsKHR.OpaqueBitKhr,
    };

    private static (AccelerationStructureGeometryKHR Geometry, AccelerationStructureBuildRangeInfoKHR Range) Describe(
        in RhiTriangleGeometry source)
    {
        bool indexed = source.Indices is not null;

        AccelerationStructureGeometryTrianglesDataKHR triangles = new()
        {
            SType = StructureType.AccelerationStructureGeometryTrianglesDataKhr,
            VertexFormat = Format.R32G32B32Sfloat,
            VertexData = new DeviceOrHostAddressConstKHR { DeviceAddress = Address(source.Vertices) },
            VertexStride = source.VertexStride,

            // The highest vertex index the build will address, which is one less than the count.
            MaxVertex = source.VertexCount == 0 ? 0 : source.VertexCount - 1,
            IndexType = indexed ? IndexType.Uint16 : IndexType.NoneKhr,
            IndexData = new DeviceOrHostAddressConstKHR { DeviceAddress = indexed ? Address(source.Indices!) : 0 },

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
            PrimitiveOffset = indexed
                ? source.FirstIndex * RhiTriangleGeometry.IndexStride
                : source.FirstIndex * source.VertexStride,
            FirstVertex = 0,
            TransformOffset = 0,
        };

        return (geometry, range);
    }
}
