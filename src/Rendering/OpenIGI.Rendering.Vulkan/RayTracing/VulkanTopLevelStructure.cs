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
    // Every instance buffer ever allocated, with the submission that last read it.
    private readonly List<(VulkanBuffer Buffer, ulong LastUsed)> _instancePool = [];
    private readonly VulkanRayTracingInstance[] _staged;

    // Every structure ever built, with the submission that last traced it -- the same rule the instance
    // pool keeps. A build overwrites the structure it targets, and a frame already in flight is tracing
    // against the one it built, so this frame must build into a different one: writing the structure a
    // trace is reading is a scene built over rays mid-flight, which the hardware does not survive.
    private readonly List<(VulkanAccelerationStructure Structure, ulong LastUsed)> _structurePool = [];
    private VulkanAccelerationStructure? _current;
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

        _staged = new VulkanRayTracingInstance[_capacity];

        // Sized once, for the worst case. The structure's size depends only on how many instances it can
        // hold, so allocating for capacity means a frame that adds an instance never has to reallocate
        // mid-build.
        AccelerationStructureBuildSizesInfoKHR sizes = QuerySizes(_capacity);
        BuildScratchSize = VulkanRayTracingApi.Align(sizes.BuildScratchSize, api.ScratchAlignment);
        _structureSize = sizes.AccelerationStructureSize;
    }

    /// <summary>Gets the structure the last build wrote, or <see langword="null"/> before the first build.</summary>
    public VulkanAccelerationStructure? Structure => _current;

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

        // Staged rather than written: which buffer this frame's build reads is not known until the build
        // is recorded, because it depends on what the queue has finished.
        instances.CopyTo(_staged);
        InstanceCount = (uint)instances.Length;
    }

    /// <summary>Records the build of the scene over the instances last written.</summary>
    /// <param name="commandBuffer">Where to record.</param>
    /// <param name="scratchAddress">
    /// The device address of scratch memory at least <see cref="BuildScratchSize"/> bytes long.
    /// </param>
    /// <param name="submissionValue">The timeline value this frame's work will signal.</param>
    /// <param name="completedValue">The timeline value the queue has reached.</param>
    /// <remarks>
    /// The caller is responsible for the barriers on either side: the bottom-level builds this reads must
    /// have completed before it runs, and it must have completed before anything traces against it.
    /// </remarks>
    public void RecordBuild(
        CommandBuffer commandBuffer,
        ulong scratchAddress,
        ulong submissionValue,
        ulong completedValue)
    {
        VulkanBuffer instanceBuffer = AcquireInstanceBuffer(submissionValue, completedValue);

        // Refilled every frame, not only when the placements change: this is a different buffer from the
        // one the last frame read, so whatever it held is not this scene.
        instanceBuffer.Write<VulkanRayTracingInstance>(_staged.AsSpan(0, (int)InstanceCount));

        // A structure nothing still on the GPU is tracing, for the same reason the instance buffer is
        // taken this way -- built in place, it would otherwise be written over the rays a frame in flight
        // is casting through it.
        VulkanAccelerationStructure structure = AcquireStructure(submissionValue, completedValue);
        _current = structure;

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
                    Data = new DeviceOrHostAddressConstKHR { DeviceAddress = instanceBuffer.DeviceAddress },
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
            DstAccelerationStructure = structure.Handle,
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
        foreach ((VulkanAccelerationStructure structure, _) in _structurePool)
        {
            structure.Dispose();
        }

        _structurePool.Clear();
        foreach ((VulkanBuffer buffer, _) in _instancePool)
        {
            buffer.Dispose();
        }

        _instancePool.Clear();
    }

    /// <summary>Takes an instance buffer that nothing still running is reading, growing the pool if none is.</summary>
    /// <param name="submissionValue">The timeline value this frame's work will signal.</param>
    /// <param name="completedValue">The timeline value the queue has reached.</param>
    /// <returns>The buffer to fill and build from.</returns>
    /// <remarks>
    /// The same rule the build scratch keeps, and for the same reason. The instances are written by the
    /// CPU while the previous frame is still on the GPU, and that frame's scene build is reading them —
    /// so writing into the buffer it is reading hands it a scene half of one frame and half of the next,
    /// including placements whose structures have since been freed. An instance buffer created inside a
    /// render graph each frame never hits this, because the graph does not hand back memory that is still
    /// in flight; this is that rule, kept by hand.
    /// </remarks>
    private VulkanBuffer AcquireInstanceBuffer(ulong submissionValue, ulong completedValue)
    {
        for (int i = 0; i < _instancePool.Count; i++)
        {
            (VulkanBuffer buffer, ulong lastUsed) = _instancePool[i];
            if (lastUsed > completedValue)
            {
                continue;
            }

            _instancePool[i] = (buffer, submissionValue);
            return buffer;
        }

        VulkanBuffer fresh = new(
            _vk,
            _device,
            _allocator,
            (ulong)_capacity * VulkanRayTracingInstance.Size,
            BufferUsageFlags.AccelerationStructureBuildInputReadOnlyBitKhr
                | BufferUsageFlags.ShaderDeviceAddressBit
                | BufferUsageFlags.TransferDstBit,
            _allocator.UploadMemoryProperties(),
            persistentlyMapped: true);

        _instancePool.Add((fresh, submissionValue));
        return fresh;
    }

    /// <summary>Takes a structure nothing still running is tracing against, growing the pool if none is.</summary>
    /// <param name="submissionValue">The timeline value this frame's work will signal.</param>
    /// <param name="completedValue">The timeline value the queue has reached.</param>
    /// <returns>The structure to build into and trace against this frame.</returns>
    private VulkanAccelerationStructure AcquireStructure(ulong submissionValue, ulong completedValue)
    {
        for (int i = 0; i < _structurePool.Count; i++)
        {
            (VulkanAccelerationStructure structure, ulong lastUsed) = _structurePool[i];
            if (lastUsed > completedValue)
            {
                continue;
            }

            _structurePool[i] = (structure, submissionValue);
            return structure;
        }

        VulkanAccelerationStructure fresh = VulkanAccelerationStructure.Create(
            _vk,
            _api,
            _device,
            _allocator,
            AccelerationStructureTypeKHR.TopLevelKhr,
            _structureSize);

        _structurePool.Add((fresh, submissionValue));
        return fresh;
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
