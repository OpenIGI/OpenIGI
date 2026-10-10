using OpenIGI.Rendering.D3D12.Interop;
using OpenIGI.Rendering.Rhi;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;

namespace OpenIGI.Rendering.D3D12.Rhi;

/// <summary>Acceleration structures on Direct3D 12: DXR 1.1 behind the interface, for inline ray queries.</summary>
/// <remarks>
/// <para>
/// Only the API, as on Vulkan: sizing, creating and recording builds. Which meshes are built and how long a
/// structure outlives the frames tracing it is the traced world's, above the RHI.
/// </para>
/// <para>
/// The instance record is the same 64 bytes on both APIs with the same flag bits, so the traced world's
/// instance buffer is consumed as it stands; and the geometry is the same triangles, its indices 16 bits.
/// </para>
/// </remarks>
internal sealed unsafe class D3D12RhiAccelerationStructures : IRhiAccelerationStructures
{
    /// <summary><c>D3D12_RAYTRACING_ACCELERATION_STRUCTURE_BYTE_ALIGNMENT</c>, for structures and scratch both.</summary>
    private const ulong Alignment = 256;

    /// <summary><c>D3D12_RESOURCE_FLAG_RAYTRACING_ACCELERATION_STRUCTURE</c>, which a structure's buffer is made with.</summary>
    private const ResourceFlags AccelerationStructureFlag = (ResourceFlags)0x100;

    private readonly D3D12RhiDevice _device;

    /// <summary>Initialises a new instance of the <see cref="D3D12RhiAccelerationStructures"/> class.</summary>
    /// <param name="device">The device.</param>
    public D3D12RhiAccelerationStructures(D3D12RhiDevice device)
    {
        _device = device;
    }

    /// <inheritdoc/>
    public ulong ScratchAlignment => Alignment;

    /// <inheritdoc/>
    /// <remarks><c>D3D12_RAYTRACING_MAX_INSTANCES_PER_TOP_LEVEL_ACCELERATION_STRUCTURE</c>.</remarks>
    public int MaxInstances => 1 << 24;

    /// <inheritdoc/>
    public RhiAccelerationStructureSizes BottomLevelSizes(ReadOnlySpan<RhiTriangleGeometry> geometries, RhiAccelerationBuildOptions flags)
    {
        if (geometries.IsEmpty)
        {
            throw new ArgumentException("A bottom-level structure needs at least one geometry.", nameof(geometries));
        }

        RaytracingGeometryDesc[] described = new RaytracingGeometryDesc[geometries.Length];
        for (int i = 0; i < geometries.Length; i++)
        {
            described[i] = Describe(geometries[i]);
        }

        fixed (RaytracingGeometryDesc* first = described)
        {
            BuildRaytracingAccelerationStructureInputs inputs = new()
            {
                Type = RaytracingAccelerationStructureType.BottomLevel,
                Flags = BuildFlags(flags, update: false),
                NumDescs = (uint)described.Length,
                DescsLayout = ElementsLayout.Array,
                PGeometryDescs = first,
            };
            return Sizes(&inputs);
        }
    }

    /// <inheritdoc/>
    public RhiAccelerationStructureSizes TopLevelSizes(int instanceCount, RhiAccelerationBuildOptions flags)
    {
        BuildRaytracingAccelerationStructureInputs inputs = new()
        {
            Type = RaytracingAccelerationStructureType.TopLevel,
            Flags = BuildFlags(flags, update: false),
            NumDescs = (uint)Math.Max(1, instanceCount),
            DescsLayout = ElementsLayout.Array,
        };
        return Sizes(&inputs);
    }

    /// <inheritdoc/>
    public IRhiAccelerationStructure Create(in RhiAccelerationStructureDescriptor descriptor)
    {
        ulong size = Align(Math.Max(descriptor.Size, Alignment));
        ResourceDesc1 description = new()
        {
            Dimension = ResourceDimension.Buffer,
            Width = size,
            Height = 1,
            DepthOrArraySize = 1,
            MipLevels = 1,
            Format = Format.FormatUnknown,
            SampleDesc = new SampleDesc(1, 0),
            Layout = TextureLayout.LayoutRowMajor,
            Flags = ResourceFlags.AllowUnorderedAccess | AccelerationStructureFlag,
        };

        HeapProperties properties = new() { Type = HeapType.Default };
        ID3D12Resource* resource = null;
        Guid iid = ID3D12Resource.Guid;
        D3D12Check.Ok(
            _device.Native->CreateCommittedResource3(
                &properties, HeapFlags.CreateNotZeroed, &description, BarrierLayout.Undefined, null, null, 0, null, &iid, (void**)&resource),
            $"CreateCommittedResource3 (acceleration structure '{descriptor.Label}', {size} bytes)");
        D3D12Names.Set((ID3D12Object*)resource, descriptor.Label);
        D3D12Residency.High(_device.Native, resource);
        return new D3D12RhiAccelerationStructure(_device, resource, descriptor.Kind, size, descriptor.Label);
    }

    /// <summary>Records a batch of builds, with the barriers that order it against what came before and after.</summary>
    /// <param name="encoder">Where to record, outside any pass.</param>
    /// <param name="bottomLevel">The bottom-level builds and refits.</param>
    /// <param name="topLevel">The top-level build, if any.</param>
    public static void RecordBuilds(D3D12RhiCommandEncoder encoder, ReadOnlySpan<RhiBottomLevelBuild> bottomLevel, RhiTopLevelBuild? topLevel)
    {
        // What came before -- the geometry written this frame, the rays traced against the structures about
        // to be rebuilt -- is finished by the global barrier the encoder records ahead of this.

        // No barrier between the bottom-level builds: their scratch ranges are their own, and one barrier
        // covering all of them is both correct and far cheaper than draining the pipeline between each.
        foreach (RhiBottomLevelBuild build in bottomLevel)
        {
            RecordBottomLevel(encoder, build);
        }

        if (topLevel is { } top)
        {
            // Everything above wrote structures the scene build is about to read.
            Barrier(
                encoder,
                BarrierSync.BuildRaytracingAccelerationStructure,
                BarrierAccess.RaytracingAccelerationStructureWrite,
                BarrierSync.BuildRaytracingAccelerationStructure,
                BarrierAccess.RaytracingAccelerationStructureRead);
            RecordTopLevel(encoder, top);
        }

        // And the last build is finished before anything after this traces against it.
        Barrier(
            encoder,
            BarrierSync.BuildRaytracingAccelerationStructure,
            BarrierAccess.RaytracingAccelerationStructureWrite,
            BarrierSync.All,
            BarrierAccess.RaytracingAccelerationStructureRead);
    }

    private static void Barrier(D3D12RhiCommandEncoder encoder, BarrierSync syncBefore, BarrierAccess accessBefore, BarrierSync syncAfter, BarrierAccess accessAfter)
    {
        GlobalBarrier global = new()
        {
            SyncBefore = syncBefore,
            SyncAfter = syncAfter,
            AccessBefore = accessBefore,
            AccessAfter = accessAfter,
        };
        BarrierGroup group = new() { Type = BarrierType.Global, NumBarriers = 1 };
        group.PGlobalBarriers = &global;
        encoder.List->Barrier(1, &group);
    }

    private static void RecordBottomLevel(D3D12RhiCommandEncoder encoder, in RhiBottomLevelBuild build)
    {
        ReadOnlySpan<RhiTriangleGeometry> source = build.Geometries.Span;
        RaytracingGeometryDesc[] geometries = new RaytracingGeometryDesc[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            geometries[i] = Describe(source[i]);
        }

        ulong destination = ((D3D12RhiAccelerationStructure)build.Destination).GpuAddress;
        fixed (RaytracingGeometryDesc* first = geometries)
        {
            BuildRaytracingAccelerationStructureDesc description = new()
            {
                DestAccelerationStructureData = destination,
                SourceAccelerationStructureData = build.Update ? destination : 0,
                ScratchAccelerationStructureData = Address(build.Scratch) + build.ScratchOffset,
                Inputs = new BuildRaytracingAccelerationStructureInputs
                {
                    Type = RaytracingAccelerationStructureType.BottomLevel,
                    Flags = BuildFlags(build.Flags, build.Update),
                    NumDescs = (uint)geometries.Length,
                    DescsLayout = ElementsLayout.Array,
                    PGeometryDescs = first,
                },
            };
            encoder.List->BuildRaytracingAccelerationStructure(&description, 0, null);
        }
    }

    private static void RecordTopLevel(D3D12RhiCommandEncoder encoder, in RhiTopLevelBuild build)
    {
        BuildRaytracingAccelerationStructureDesc description = new()
        {
            DestAccelerationStructureData = ((D3D12RhiAccelerationStructure)build.Destination).GpuAddress,
            ScratchAccelerationStructureData = Address(build.Scratch) + build.ScratchOffset,
            Inputs = new BuildRaytracingAccelerationStructureInputs
            {
                Type = RaytracingAccelerationStructureType.TopLevel,
                Flags = BuildFlags(build.Flags, update: false),
                NumDescs = (uint)build.InstanceCount,
                DescsLayout = ElementsLayout.Array,
                InstanceDescs = Address(build.Instances) + build.InstanceOffset,
            },
        };
        encoder.List->BuildRaytracingAccelerationStructure(&description, 0, null);
    }

    private RhiAccelerationStructureSizes Sizes(BuildRaytracingAccelerationStructureInputs* inputs)
    {
        RaytracingAccelerationStructurePrebuildInfo info = default;
        _device.Native->GetRaytracingAccelerationStructurePrebuildInfo(inputs, &info);
        return new RhiAccelerationStructureSizes(
            Align(info.ResultDataMaxSizeInBytes),
            Align(info.ScratchDataSizeInBytes),
            Align(info.UpdateScratchDataSizeInBytes));
    }

    private static RaytracingAccelerationStructureBuildFlags BuildFlags(RhiAccelerationBuildOptions flags, bool update)
    {
        RaytracingAccelerationStructureBuildFlags mapped = RaytracingAccelerationStructureBuildFlags.None;
        if (flags.HasFlag(RhiAccelerationBuildOptions.PreferFastTrace))
        {
            mapped |= RaytracingAccelerationStructureBuildFlags.PreferFastTrace;
        }

        if (flags.HasFlag(RhiAccelerationBuildOptions.AllowUpdate))
        {
            mapped |= RaytracingAccelerationStructureBuildFlags.AllowUpdate;
        }

        if (update)
        {
            mapped |= RaytracingAccelerationStructureBuildFlags.PerformUpdate;
        }

        return mapped;
    }

    private static RaytracingGeometryDesc Describe(in RhiTriangleGeometry source)
    {
        bool indexed = source.Indices is not null;
        RaytracingGeometryTrianglesDesc triangles = new()
        {
            // Per-geometry transforms are not used: instance transforms carry every placement.
            Transform3x4 = 0,
            VertexFormat = Format.FormatR32G32B32Float,
            VertexBuffer = new GpuVirtualAddressAndStride
            {
                StartAddress = Address(source.Vertices) + (indexed ? 0 : (ulong)source.FirstIndex * source.VertexStride),
                StrideInBytes = source.VertexStride,
            },
        };

        if (indexed)
        {
            triangles.IndexFormat = Format.FormatR16Uint;
            triangles.IndexBuffer = Address(source.Indices!) + ((ulong)source.FirstIndex * RhiTriangleGeometry.IndexStride);
            triangles.IndexCount = source.PrimitiveCount * 3;
            triangles.VertexCount = source.VertexCount;
        }
        else
        {
            triangles.IndexFormat = Format.FormatUnknown;
            triangles.VertexCount = source.PrimitiveCount * 3;
        }

        return new RaytracingGeometryDesc
        {
            Type = RaytracingGeometryType.Triangles,
            Flags = source.Opaque ? RaytracingGeometryFlags.Opaque : RaytracingGeometryFlags.NoDuplicateAnyhitInvocation,
            Triangles = triangles,
        };
    }

    private static ulong Address(IRhiBuffer buffer) => ((D3D12RhiBuffer)buffer).GpuAddress;

    private static ulong Align(ulong value) => (value + Alignment - 1) & ~(Alignment - 1);
}

/// <summary>An acceleration structure: a buffer the builds write and a ray query reads by address.</summary>
internal sealed unsafe class D3D12RhiAccelerationStructure : IRhiAccelerationStructure, ID3D12AccelerationStructureView
{
    private readonly D3D12RhiDevice _device;
    private ID3D12Resource* _resource;

    /// <summary>Initialises a new instance of the <see cref="D3D12RhiAccelerationStructure"/> class.</summary>
    /// <param name="device">The device.</param>
    /// <param name="resource">The buffer, which this owns.</param>
    /// <param name="kind">Which level it is.</param>
    /// <param name="size">Its size.</param>
    /// <param name="label">A name.</param>
    public D3D12RhiAccelerationStructure(D3D12RhiDevice device, ID3D12Resource* resource, RhiAccelerationStructureKind kind, ulong size, string label)
    {
        _device = device;
        _resource = resource;
        Kind = kind;
        Size = size;
        Label = label;
        GpuAddress = resource->GetGPUVirtualAddress();
    }

    /// <inheritdoc/>
    public ulong GpuAddress { get; }

    /// <inheritdoc/>
    public RhiAccelerationStructureKind Kind { get; }

    /// <inheritdoc/>
    public ulong Size { get; }

    /// <inheritdoc/>
    /// <remarks>Its GPU address, which an instance names its structure by on both APIs.</remarks>
    public ulong InstanceReference => GpuAddress;

    /// <inheritdoc/>
    public string Label { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
        ID3D12Resource* resource = _resource;
        _resource = null;
        if (resource is not null)
        {
            _device.Retire(() => ((IUnknown*)resource)->Release());
        }
    }
}
