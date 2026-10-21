using System.Runtime.InteropServices;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering.Rhi;

/// <summary>Which level of the two-level hierarchy a structure is.</summary>
public enum RhiAccelerationStructureKind
{
    /// <summary>Over one mesh's triangles, in the space its vertices are stored in.</summary>
    BottomLevel,

    /// <summary>Over placements of bottom-level structures: the scene a ray is traced against.</summary>
    TopLevel,
}

/// <summary>How a structure is built.</summary>
[Flags]
public enum RhiAccelerationBuildOptions
{
    /// <summary>Nothing asked for.</summary>
    None = 0,

    /// <summary>Spend the build's time on a tree that traces fast, for a structure traced far more than built.</summary>
    PreferFastTrace = 1 << 0,

    /// <summary>Allow the structure to be refitted in place when its vertices move, rather than rebuilt.</summary>
    AllowUpdate = 1 << 1,
}

/// <summary>A placement's per-instance switches. The bit values are both Vulkan's and Direct3D 12's.</summary>
[Flags]
public enum RhiAccelerationInstanceOptions : byte
{
    /// <summary>None.</summary>
    None = 0,

    /// <summary>Neither face of a triangle is culled.</summary>
    TriangleCullDisable = 1 << 0,

    /// <summary>Counter-clockwise triangles face forward.</summary>
    TriangleFrontCounterClockwise = 1 << 1,

    /// <summary>Every geometry is treated as opaque.</summary>
    ForceOpaque = 1 << 2,

    /// <summary>Every geometry is treated as non-opaque.</summary>
    ForceNonOpaque = 1 << 3,
}

/// <summary>One run of a mesh's triangles, as a bottom-level build reads it.</summary>
/// <param name="Vertices">
/// The vertex buffer, created with <see cref="RhiBufferUsage.RayTracingInput"/>. Each vertex starts with
/// its position as three 32-bit floats.
/// </param>
/// <param name="VertexStride">The bytes from one vertex to the next.</param>
/// <param name="VertexCount">How many vertices the buffer holds; the highest index addressed is one less.</param>
/// <param name="Indices">The 16-bit index buffer, or <see langword="null"/> for a non-indexed list.</param>
/// <param name="FirstIndex">Where the run starts, in indices (or in vertices when there are none).</param>
/// <param name="PrimitiveCount">How many triangles the run holds.</param>
/// <param name="Opaque">
/// Whether a hit is final. A cut-out run is not: a ray query asks the shader whether the texel it struck is
/// a hole before it commits.
/// </param>
public readonly record struct RhiTriangleGeometry(
    IRhiBuffer Vertices,
    uint VertexStride,
    uint VertexCount,
    IRhiBuffer? Indices,
    uint FirstIndex,
    uint PrimitiveCount,
    bool Opaque = true)
{
    /// <summary>The size of one index: the meshes this renderer draws are 16-bit indexed.</summary>
    public const uint IndexStride = 2;
}

/// <summary>What a build needs: the structure's own size and the scratch a build or a refit uses.</summary>
/// <param name="StructureSize">The bytes the structure occupies.</param>
/// <param name="BuildScratchSize">The scratch a full build needs, already aligned.</param>
/// <param name="UpdateScratchSize">The scratch a refit needs, already aligned.</param>
public readonly record struct RhiAccelerationStructureSizes(
    ulong StructureSize,
    ulong BuildScratchSize,
    ulong UpdateScratchSize);

/// <summary>What an acceleration structure is created as.</summary>
/// <param name="Kind">Which level it is.</param>
/// <param name="Size">How many bytes, as <see cref="IRhiAccelerationStructures"/> sized it.</param>
/// <param name="Label">A name for captures and diagnostics.</param>
public readonly record struct RhiAccelerationStructureDescriptor(
    RhiAccelerationStructureKind Kind,
    ulong Size,
    string Label = "");

/// <summary>One bottom-level build or refit.</summary>
/// <param name="Destination">The structure built into.</param>
/// <param name="Geometries">The runs it covers, in the order a hit reports their index.</param>
/// <param name="Flags">How it was created; a refit must repeat the flags of the build it refits.</param>
/// <param name="Update">Whether to refit the structure's last build in place rather than build afresh.</param>
/// <param name="Scratch">The scratch buffer, created with <see cref="RhiBufferUsage.AccelerationStructureScratch"/>.</param>
/// <param name="ScratchOffset">Where in it this build's scratch starts, aligned to the device's scratch alignment.</param>
public readonly record struct RhiBottomLevelBuild(
    IRhiAccelerationStructure Destination,
    ReadOnlyMemory<RhiTriangleGeometry> Geometries,
    RhiAccelerationBuildOptions Flags,
    bool Update,
    IRhiBuffer Scratch,
    ulong ScratchOffset);

/// <summary>One top-level build over a buffer of placements.</summary>
/// <param name="Destination">The structure built into.</param>
/// <param name="Instances">
/// The placements, <see cref="RhiAccelerationInstance"/> after another, in a buffer created with
/// <see cref="RhiBufferUsage.RayTracingInput"/>.
/// </param>
/// <param name="InstanceOffset">Where the first placement starts.</param>
/// <param name="InstanceCount">How many placements; nought builds a structure every ray misses.</param>
/// <param name="Flags">How it is built.</param>
/// <param name="Scratch">The scratch buffer.</param>
/// <param name="ScratchOffset">Where in it this build's scratch starts.</param>
public readonly record struct RhiTopLevelBuild(
    IRhiAccelerationStructure Destination,
    IRhiBuffer Instances,
    ulong InstanceOffset,
    int InstanceCount,
    RhiAccelerationBuildOptions Flags,
    IRhiBuffer Scratch,
    ulong ScratchOffset);

/// <summary>A structure a shader may trace rays against.</summary>
public interface IRhiAccelerationStructure : IRhiResource
{
    /// <summary>Gets which level it is.</summary>
    RhiAccelerationStructureKind Kind { get; }

    /// <summary>Gets how many bytes it occupies.</summary>
    ulong Size { get; }

    /// <summary>Gets what a top-level placement names this structure by.</summary>
    /// <remarks>Its device address on Vulkan, its GPU virtual address on Direct3D 12.</remarks>
    ulong InstanceReference { get; }
}

/// <summary>
/// Hardware acceleration structures: the low half of ray tracing, where a device offers it.
/// </summary>
/// <remarks>
/// <para>
/// Sizes, creation and the alignment a build's scratch keeps; the builds themselves are recorded with
/// <see cref="IRhiCommandEncoder.BuildAccelerationStructures"/>. What is built out of what, when, and how
/// long a structure must outlive the frames tracing it is not here -- that is the traced world's business,
/// above the RHI, written once for every backend.
/// </para>
/// <para>
/// Offered only by a device reporting <see cref="RhiFeatures.RayQuery"/>.
/// </para>
/// </remarks>
public interface IRhiAccelerationStructures
{
    /// <summary>Gets the alignment every scratch range must start on.</summary>
    ulong ScratchAlignment { get; }

    /// <summary>Gets the most placements one top-level structure may hold.</summary>
    int MaxInstances { get; }

    /// <summary>Sizes a bottom-level structure over some runs of triangles.</summary>
    /// <param name="geometries">The runs.</param>
    /// <param name="flags">How it will be built.</param>
    /// <returns>The sizes.</returns>
    RhiAccelerationStructureSizes BottomLevelSizes(ReadOnlySpan<RhiTriangleGeometry> geometries, RhiAccelerationBuildOptions flags);

    /// <summary>Sizes a top-level structure for a number of placements.</summary>
    /// <param name="instanceCount">The most placements it will be built over.</param>
    /// <param name="flags">How it will be built.</param>
    /// <returns>The sizes.</returns>
    RhiAccelerationStructureSizes TopLevelSizes(int instanceCount, RhiAccelerationBuildOptions flags);

    /// <summary>Creates a structure, to be built into.</summary>
    /// <param name="descriptor">Its level and size.</param>
    /// <returns>The structure, whose contents are undefined until a build is recorded.</returns>
    IRhiAccelerationStructure Create(in RhiAccelerationStructureDescriptor descriptor);
}

/// <summary>A 3x4 transform, row-major: twelve floats, as an acceleration-structure placement carries one.</summary>
[System.Runtime.CompilerServices.InlineArray(12)]
public struct RhiTransform3x4
{
    private float _element;
}

/// <summary>One placement in a top-level structure: 64 bytes, laid out as both Vulkan and Direct3D 12 read it.</summary>
/// <remarks>
/// <c>VkAccelerationStructureInstanceKHR</c> and <c>D3D12_RAYTRACING_INSTANCE_DESC</c> are the same bytes: a
/// row-major 3x4 transform, a 24-bit custom index under an 8-bit mask, a 24-bit hit-group offset under 8 bits
/// of flags whose values agree, and the bottom-level structure's address. One struct serves both.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Size = Size)]
public struct RhiAccelerationInstance
{
    /// <summary>The size of one placement.</summary>
    public const int Size = 64;

    /// <summary>The transform's first three rows, row-major.</summary>
    public RhiTransform3x4 Transform;

    /// <summary>The custom index (low 24 bits) and the ray mask (high 8).</summary>
    public uint CustomIndexAndMask;

    /// <summary>The hit-group offset (low 24 bits) and the flags (high 8).</summary>
    public uint HitGroupOffsetAndFlags;

    /// <summary>The bottom-level structure placed, as its <see cref="IRhiAccelerationStructure.InstanceReference"/>.</summary>
    public ulong StructureReference;

    /// <summary>Gets the custom index a hit reports.</summary>
    public readonly uint CustomIndex => CustomIndexAndMask & 0x00FFFFFFu;

    /// <summary>Creates a placement.</summary>
    /// <param name="transform">Where the structure stands, in world space.</param>
    /// <param name="customIndex">What a hit on it reports as its custom index; 24 bits.</param>
    /// <param name="mask">Which rays find it.</param>
    /// <param name="flags">Its switches.</param>
    /// <param name="structure">The structure placed.</param>
    /// <returns>The placement.</returns>
    public static RhiAccelerationInstance Create(
        in Matrix4f transform,
        uint customIndex,
        byte mask,
        RhiAccelerationInstanceOptions flags,
        ulong structure)
    {
        RhiAccelerationInstance instance = new()
        {
            CustomIndexAndMask = (customIndex & 0x00FFFFFFu) | ((uint)mask << 24),
            HitGroupOffsetAndFlags = (uint)flags << 24,
            StructureReference = structure,
        };

        instance.Transform[0] = transform.M11;
        instance.Transform[1] = transform.M12;
        instance.Transform[2] = transform.M13;
        instance.Transform[3] = transform.M14;
        instance.Transform[4] = transform.M21;
        instance.Transform[5] = transform.M22;
        instance.Transform[6] = transform.M23;
        instance.Transform[7] = transform.M24;
        instance.Transform[8] = transform.M31;
        instance.Transform[9] = transform.M32;
        instance.Transform[10] = transform.M33;
        instance.Transform[11] = transform.M34;
        return instance;
    }

    /// <summary>Returns this placement naming another structure under another custom index.</summary>
    /// <param name="structure">The structure now placed.</param>
    /// <param name="customIndex">The custom index now reported; the mask is kept.</param>
    /// <returns>The placement.</returns>
    public readonly RhiAccelerationInstance Repointed(ulong structure, uint customIndex)
    {
        RhiAccelerationInstance copy = this;
        copy.StructureReference = structure;
        copy.CustomIndexAndMask = (customIndex & 0x00FFFFFFu) | (CustomIndexAndMask & 0xFF000000u);
        return copy;
    }
}
