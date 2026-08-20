using OpenIGI.Rendering.Vulkan.Memory;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.RayTracing;

/// <summary>
/// The structure over the whole scene: a list of placements of bottom-level structures, rebuilt each
/// frame.
/// </summary>
/// <remarks>
/// <para>
/// The top level is cheap to build because it is built over instances rather than triangles — a few
/// thousand boxes rather than a few million polygons — so it is rebuilt from scratch every frame instead
/// of being refitted. That keeps the tree well-shaped however far things have moved, which a refit does
/// not, and costs little enough that the alternative is not worth the bookkeeping.
/// </para>
/// <para>
/// The instance array is written by the CPU and read directly by the build, so it lives in memory both
/// can reach — device-local where the adapter maps its video memory to the host, plain host-visible
/// otherwise. Either way it is written in place with no staging copy, because it is rewritten every
/// frame and a copy would be pure overhead.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanTopLevelStructure : IDisposable
{
    private readonly Vk _vk;
    private readonly VulkanRayTracingApi _api;
    private readonly VulkanDevice _device;
    private readonly VulkanAllocator _allocator;
    private readonly uint _capacity;
    private readonly VulkanBuffer _instances;

    private VulkanAccelerationStructure? _structure;
    private readonly ulong _structureSize;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VulkanTopLevelStructure"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="api">The ray-tracing entry points.</param>
    /// <param name="device">The device to build on.</param>
    /// <param name="allocator">The allocator backing the structure and its instance array.</param>
    /// <param name="capacity">The most instances the scene will ever hold.</param>
    public VulkanTopLevelStructure(
        Vk vk,
        VulkanRayTracingApi api,
        VulkanDevice device,
        VulkanAllocator allocator,
        uint capacity)
    {
        ArgumentNullException.ThrowIfNull(vk);
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(allocator);

        _vk = vk;
        _api = api;
        _device = device;
        _allocator = allocator;
        _capacity = Math.Max(1, capacity);

        _instances = new VulkanBuffer(
            vk,
            device,
            allocator,
            (ulong)_capacity * VulkanRayTracingInstance.Size,
            BufferUsageFlags.AccelerationStructureBuildInputReadOnlyBitKhr
                | BufferUsageFlags.ShaderDeviceAddressBit
                | BufferUsageFlags.TransferDstBit,
            allocator.UploadMemoryProperties(),
            persistentlyMapped: true);

        // Sized once, for the worst case. The structure's size depends only on how many instances it can
        // hold, so allocating for capacity means a frame that adds an instance never has to reallocate
        // mid-build.
        AccelerationStructureBuildSizesInfoKHR sizes = QuerySizes(_capacity);
        BuildScratchSize = VulkanRayTracingApi.Align(sizes.BuildScratchSize, api.ScratchAlignment);
        _structureSize = sizes.AccelerationStructureSize;
    }

    /// <summary>Gets the structure, or <see langword="null"/> before the first build.</summary>
    public VulkanAccelerationStructure? Structure => _structure;

    /// <summary>Gets the scratch memory a build needs.</summary>
    public ulong BuildScratchSize { get; }

    /// <summary>Gets how many instances the last build covered.</summary>
    public uint InstanceCount { get; private set; }

    /// <summary>Gets how many instances the scene can hold.</summary>
    public uint Capacity => _capacity;

    /// <summary>Writes the instance array the next build will read.</summary>
    /// <param name="instances">The placements, at most <see cref="Capacity"/> of them.</param>
    /// <exception cref="ArgumentException">More instances were given than the scene can hold.</exception>
    public void SetInstances(ReadOnlySpan<VulkanRayTracingInstance> instances)
    {
        if (instances.Length > _capacity)
        {
            throw new ArgumentException(
                $"The scene holds at most {_capacity} instances but {instances.Length} were given.",
                nameof(instances));
        }

        _instances.Write(instances);
        InstanceCount = (uint)instances.Length;
    }

    /// <summary>Records the build of the scene over the instances last written.</summary>
    /// <param name="commandBuffer">Where to record.</param>
    /// <param name="scratchAddress">
    /// The device address of scratch memory at least <see cref="BuildScratchSize"/> bytes long.
    /// </param>
    /// <remarks>
    /// The caller is responsible for the barriers on either side: the bottom-level builds this reads must
    /// have completed before it runs, and it must have completed before anything traces against it.
    /// </remarks>
    public void RecordBuild(CommandBuffer commandBuffer, ulong scratchAddress)
    {
        _structure ??= VulkanAccelerationStructure.Create(
            _vk,
            _api,
            _device,
            _allocator,
            AccelerationStructureTypeKHR.TopLevelKhr,
            _structureSize);

        AccelerationStructureGeometryKHR geometry = new()
        {
            SType = StructureType.AccelerationStructureGeometryKhr,
            GeometryType = GeometryTypeKHR.InstancesKhr,
            Geometry = new AccelerationStructureGeometryDataKHR
            {
                Instances = new AccelerationStructureGeometryInstancesDataKHR
                {
                    SType = StructureType.AccelerationStructureGeometryInstancesDataKhr,
                    ArrayOfPointers = false,
                    Data = new DeviceOrHostAddressConstKHR { DeviceAddress = _instances.DeviceAddress },
                },
            },
            Flags = GeometryFlagsKHR.OpaqueBitKhr,
        };

        AccelerationStructureBuildRangeInfoKHR range = new()
        {
            PrimitiveCount = InstanceCount,
            PrimitiveOffset = 0,
            FirstVertex = 0,
            TransformOffset = 0,
        };

        AccelerationStructureBuildGeometryInfoKHR info = new()
        {
            SType = StructureType.AccelerationStructureBuildGeometryInfoKhr,
            Type = AccelerationStructureTypeKHR.TopLevelKhr,
            Flags = BuildFlags(),
            Mode = BuildAccelerationStructureModeKHR.BuildKhr,
            DstAccelerationStructure = _structure.Handle,
            GeometryCount = 1,
            PGeometries = &geometry,
            ScratchData = new DeviceOrHostAddressKHR { DeviceAddress = scratchAddress },
        };

        AccelerationStructureBuildRangeInfoKHR* ranged = &range;
        _api.Structures.CmdBuildAccelerationStructures(commandBuffer, 1, &info, &ranged);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _structure?.Dispose();
        _instances.Dispose();
    }

    private BuildAccelerationStructureFlagsKHR BuildFlags()
    {
        BuildAccelerationStructureFlagsKHR flags = BuildAccelerationStructureFlagsKHR.PreferFastTraceBitKhr;
        if (_api.PositionFetch)
        {
            flags |= BuildAccelerationStructureFlagsKHR.AllowDataAccessKhr;
        }

        return flags;
    }

    private AccelerationStructureBuildSizesInfoKHR QuerySizes(uint instanceCount)
    {
        AccelerationStructureGeometryKHR geometry = new()
        {
            SType = StructureType.AccelerationStructureGeometryKhr,
            GeometryType = GeometryTypeKHR.InstancesKhr,
            Geometry = new AccelerationStructureGeometryDataKHR
            {
                Instances = new AccelerationStructureGeometryInstancesDataKHR
                {
                    SType = StructureType.AccelerationStructureGeometryInstancesDataKhr,
                    ArrayOfPointers = false,
                },
            },
            Flags = GeometryFlagsKHR.OpaqueBitKhr,
        };

        AccelerationStructureBuildGeometryInfoKHR info = new()
        {
            SType = StructureType.AccelerationStructureBuildGeometryInfoKhr,
            Type = AccelerationStructureTypeKHR.TopLevelKhr,
            Flags = BuildFlags(),
            Mode = BuildAccelerationStructureModeKHR.BuildKhr,
            GeometryCount = 1,
            PGeometries = &geometry,
        };

        AccelerationStructureBuildSizesInfoKHR sizes = new()
        {
            SType = StructureType.AccelerationStructureBuildSizesInfoKhr,
        };

        uint count = instanceCount;
        _api.Structures.GetAccelerationStructureBuildSizes(
            _device.Device,
            AccelerationStructureBuildTypeKHR.DeviceKhr,
            &info,
            &count,
            &sizes);
        return sizes;
    }
}
