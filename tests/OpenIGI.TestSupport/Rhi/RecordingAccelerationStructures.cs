using OpenIGI.Rendering.Rhi;

namespace OpenIGI.TestSupport.Rhi;

/// <summary>An acceleration structure that only remembers what it was created as and whether it was freed.</summary>
/// <param name="descriptor">What it was created as.</param>
/// <param name="reference">What a placement names it by.</param>
public sealed class RecordedAccelerationStructure(in RhiAccelerationStructureDescriptor descriptor, ulong reference)
    : RecordedResource(descriptor.Label), IRhiAccelerationStructure
{
    /// <inheritdoc/>
    public RhiAccelerationStructureKind Kind { get; } = descriptor.Kind;

    /// <inheritdoc/>
    public ulong Size { get; } = descriptor.Size;

    /// <inheritdoc/>
    public ulong InstanceReference { get; } = reference;
}

/// <summary>
/// Acceleration structures with nothing behind them: sizes from a fixed rule and structures that record
/// their own lifetime, so the traced world above the RHI can be tested without a GPU.
/// </summary>
public sealed class RecordingAccelerationStructures : IRhiAccelerationStructures
{
    private readonly List<RecordedAccelerationStructure> _created = [];
    private ulong _nextReference = 0x1000;

    /// <inheritdoc/>
    public ulong ScratchAlignment => 256;

    /// <inheritdoc/>
    public int MaxInstances => 1 << 20;

    /// <summary>Gets every structure created, in order.</summary>
    public IReadOnlyList<RecordedAccelerationStructure> Created => _created;

    /// <inheritdoc/>
    public RhiAccelerationStructureSizes BottomLevelSizes(
        ReadOnlySpan<RhiTriangleGeometry> geometries,
        RhiAccelerationBuildOptions flags)
    {
        ulong primitives = 0;
        foreach (RhiTriangleGeometry geometry in geometries)
        {
            primitives += geometry.PrimitiveCount;
        }

        return new RhiAccelerationStructureSizes(256 + (primitives * 64), 512 + (primitives * 32), 256);
    }

    /// <inheritdoc/>
    public RhiAccelerationStructureSizes TopLevelSizes(int instanceCount, RhiAccelerationBuildOptions flags) =>
        new(256 + ((ulong)instanceCount * 128), 512 + ((ulong)instanceCount * 64), 256);

    /// <inheritdoc/>
    public IRhiAccelerationStructure Create(in RhiAccelerationStructureDescriptor descriptor)
    {
        RecordedAccelerationStructure structure = new(descriptor, _nextReference);
        _nextReference += 0x1000;
        _created.Add(structure);
        return structure;
    }
}
