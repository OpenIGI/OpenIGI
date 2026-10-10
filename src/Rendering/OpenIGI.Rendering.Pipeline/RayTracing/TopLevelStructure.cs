using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline.RayTracing;

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
internal sealed class TopLevelStructure : IDisposable
{
    private readonly IRhiDevice _device;
    private readonly IRhiAccelerationStructures _structures;
    private readonly int _capacity;

    // Every instance buffer ever allocated, with the frame that last read it.
    private readonly List<(IRhiBuffer Buffer, ulong LastUsed)> _instancePool = [];
    private readonly RhiAccelerationInstance[] _staged;

    // Every structure ever built, with the frame that last traced it -- the same rule the instance pool
    // keeps. A build overwrites the structure it targets, and a frame already in flight is tracing against
    // the one it built, so this frame must build into a different one: writing the structure a trace is
    // reading is a scene built over rays mid-flight, which the hardware does not survive.
    private readonly List<(IRhiAccelerationStructure Structure, ulong LastUsed)> _structurePool = [];
    private IRhiAccelerationStructure? _current;
    private readonly ulong _structureSize;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="TopLevelStructure"/> class.</summary>
    /// <param name="device">The device the instance arrays are allocated on.</param>
    /// <param name="structures">The device's acceleration structures.</param>
    /// <param name="capacity">The most instances the scene will ever hold.</param>
    public TopLevelStructure(IRhiDevice device, IRhiAccelerationStructures structures, int capacity)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(structures);

        _device = device;
        _structures = structures;
        _capacity = Math.Max(1, capacity);
        _staged = new RhiAccelerationInstance[_capacity];

        // Sized once, for the worst case. The structure's size depends only on how many instances it can
        // hold, so allocating for capacity means a frame that adds an instance never has to reallocate
        // mid-build.
        RhiAccelerationStructureSizes sizes = structures.TopLevelSizes(_capacity, Flags);
        BuildScratchSize = sizes.BuildScratchSize;
        _structureSize = sizes.StructureSize;
    }

    /// <summary>Gets the structure the last build wrote, or <see langword="null"/> before the first build.</summary>
    public IRhiAccelerationStructure? Structure => _current;

    /// <summary>Gets the scratch memory a build needs.</summary>
    public ulong BuildScratchSize { get; }

    /// <summary>Gets how many instances the last build covered.</summary>
    public int InstanceCount { get; private set; }

    /// <summary>Gets how many instances the scene can hold.</summary>
    public int Capacity => _capacity;

    private static RhiAccelerationBuildOptions Flags => RhiAccelerationBuildOptions.PreferFastTrace;

    /// <summary>Writes the instance array the next build will read.</summary>
    /// <param name="instances">The placements, at most <see cref="Capacity"/> of them.</param>
    /// <exception cref="ArgumentException">More instances were given than the scene can hold.</exception>
    public void SetInstances(ReadOnlySpan<RhiAccelerationInstance> instances)
    {
        if (instances.Length > _capacity)
        {
            throw new ArgumentException(
                $"The scene holds at most {_capacity} instances but {instances.Length} were given.",
                nameof(instances));
        }

        // Staged rather than written: which buffer this frame's build reads is not known until the build
        // is described, because it depends on what the GPU has finished.
        instances.CopyTo(_staged);
        InstanceCount = instances.Length;
    }

    /// <summary>Describes the build of the scene over the instances last written.</summary>
    /// <param name="scratch">Scratch at least <see cref="BuildScratchSize"/> bytes long.</param>
    /// <param name="frame">The frame this build is recorded in.</param>
    /// <param name="completedFrame">The newest frame the GPU has finished.</param>
    /// <returns>The build.</returns>
    /// <remarks>
    /// The barriers either side are the encoder's: the bottom-level builds this reads have completed before
    /// it runs, and it has completed before anything traces against it.
    /// </remarks>
    public RhiTopLevelBuild DescribeBuild((IRhiBuffer Buffer, ulong Offset) scratch, ulong frame, ulong completedFrame)
    {
        IRhiBuffer instances = AcquireInstanceBuffer(frame, completedFrame);

        // Refilled every frame, not only when the placements change: this is a different buffer from the
        // one the last frame read, so whatever it held is not this scene.
        _device.WriteBuffer<RhiAccelerationInstance>(instances, 0, _staged.AsSpan(0, InstanceCount));

        // A structure nothing still on the GPU is tracing, for the same reason the instance buffer is
        // taken this way -- built in place, it would otherwise be written over the rays a frame in flight
        // is casting through it.
        IRhiAccelerationStructure structure = AcquireStructure(frame, completedFrame);
        _current = structure;

        return new RhiTopLevelBuild(structure, instances, 0, InstanceCount, Flags, scratch.Buffer, scratch.Offset);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach ((IRhiAccelerationStructure structure, _) in _structurePool)
        {
            structure.Dispose();
        }

        _structurePool.Clear();
        foreach ((IRhiBuffer buffer, _) in _instancePool)
        {
            buffer.Dispose();
        }

        _instancePool.Clear();
    }

    /// <summary>Takes an instance buffer that nothing still running is reading, growing the pool if none is.</summary>
    /// <remarks>
    /// The same rule the build scratch keeps, and for the same reason. The instances are written by the
    /// CPU while the previous frame is still on the GPU, and that frame's scene build is reading them —
    /// so writing into the buffer it is reading hands it a scene half of one frame and half of the next,
    /// including placements whose structures have since been freed. An instance buffer created inside a
    /// render graph each frame never hits this, because the graph does not hand back memory that is still
    /// in flight; this is that rule, kept by hand.
    /// </remarks>
    private IRhiBuffer AcquireInstanceBuffer(ulong frame, ulong completedFrame)
    {
        for (int i = 0; i < _instancePool.Count; i++)
        {
            (IRhiBuffer buffer, ulong lastUsed) = _instancePool[i];
            if (lastUsed > completedFrame)
            {
                continue;
            }

            _instancePool[i] = (buffer, frame);
            return buffer;
        }

        IRhiBuffer fresh = _device.CreateBuffer(new RhiBufferDescriptor(
            (ulong)_capacity * RhiAccelerationInstance.Size,
            RhiBufferUsage.RayTracingInput | RhiBufferUsage.MapWrite,
            "traced world instances"));

        _instancePool.Add((fresh, frame));
        return fresh;
    }

    /// <summary>Takes a structure nothing still running is tracing against, growing the pool if none is.</summary>
    private IRhiAccelerationStructure AcquireStructure(ulong frame, ulong completedFrame)
    {
        for (int i = 0; i < _structurePool.Count; i++)
        {
            (IRhiAccelerationStructure structure, ulong lastUsed) = _structurePool[i];
            if (lastUsed > completedFrame)
            {
                continue;
            }

            _structurePool[i] = (structure, frame);
            return structure;
        }

        IRhiAccelerationStructure fresh = _structures.Create(new RhiAccelerationStructureDescriptor(
            RhiAccelerationStructureKind.TopLevel, _structureSize, "world"));

        _structurePool.Add((fresh, frame));
        return fresh;
    }
}
