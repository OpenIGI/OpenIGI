using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline.RayTracing;

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
/// Static geometry is built for traversal speed rather than build speed. Geometry that moves -- skinned
/// bodies -- is built to be refitted in place instead, since its topology never changes.
/// </para>
/// <para>
/// Not compacted. Compaction was built once and switched off: its retirement path freed a structure a
/// frame in flight could still be tracing, which a device fault caught as an invalid read when a fast
/// camera streamed terrain. Should it come back, it belongs here and in the RHI as a compacted-size query
/// and a compacting copy, retired by <see cref="SubmissionClock"/> like everything else.
/// </para>
/// </remarks>
internal sealed class BottomLevelStructure : IDisposable
{
    private readonly RhiTriangleGeometry[] _geometries;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="BottomLevelStructure"/> class.</summary>
    /// <param name="structures">The device's acceleration structures.</param>
    /// <param name="geometries">The triangle runs the structure covers.</param>
    /// <param name="allowUpdate">
    /// Whether the structure will be refitted rather than rebuilt when its vertices move. Skinned and
    /// animated geometry wants this; static world geometry does not.
    /// </param>
    public BottomLevelStructure(
        IRhiAccelerationStructures structures,
        IReadOnlyList<RhiTriangleGeometry> geometries,
        bool allowUpdate = false)
    {
        ArgumentNullException.ThrowIfNull(structures);
        ArgumentNullException.ThrowIfNull(geometries);
        if (geometries.Count == 0)
        {
            throw new ArgumentException("A bottom-level structure needs at least one geometry.", nameof(geometries));
        }

        _geometries = [.. geometries];
        AllowUpdate = allowUpdate;

        // Static world geometry is traversed far more often than it is built, so the build is told to
        // spend its time producing a tree that traces fast.
        Flags = RhiAccelerationBuildOptions.PreferFastTrace
            | (allowUpdate ? RhiAccelerationBuildOptions.AllowUpdate : RhiAccelerationBuildOptions.None);

        RhiAccelerationStructureSizes sizes = structures.BottomLevelSizes(_geometries, Flags);
        BuildScratchSize = sizes.BuildScratchSize;
        UpdateScratchSize = sizes.UpdateScratchSize;
        Structure = structures.Create(new RhiAccelerationStructureDescriptor(
            RhiAccelerationStructureKind.BottomLevel, sizes.StructureSize, "mesh structure"));
    }

    /// <summary>Gets the structure.</summary>
    public IRhiAccelerationStructure Structure { get; }

    /// <summary>Gets what a top-level placement names the structure by.</summary>
    public ulong InstanceReference => Structure.InstanceReference;

    /// <summary>Gets how it is built.</summary>
    public RhiAccelerationBuildOptions Flags { get; }

    /// <summary>Gets the scratch memory a full build needs.</summary>
    public ulong BuildScratchSize { get; }

    /// <summary>Gets the scratch memory a refit needs.</summary>
    public ulong UpdateScratchSize { get; }

    /// <summary>Gets a value indicating whether the structure can be refitted instead of rebuilt.</summary>
    public bool AllowUpdate { get; }

    /// <summary>Gets a value indicating whether a build has been recorded at least once.</summary>
    public bool IsBuilt { get; private set; }

    /// <summary>Gets a value indicating whether the next build is a refit rather than a full build.</summary>
    /// <remarks>
    /// Asked in two places -- when the batch's scratch is sized and when the build is described -- and it
    /// has to give the same answer to both, or a refit is handed the scratch a full build asked for or, far
    /// worse, the other way round. Nothing between the two can change it: a structure is only marked built
    /// once its own build has been described.
    /// </remarks>
    public bool Refits => AllowUpdate && IsBuilt;

    /// <summary>Gets how much scratch this structure's turn in the next batch needs.</summary>
    /// <remarks>
    /// The update scratch for a refit, the build scratch otherwise. The distinction pays for itself the moment
    /// skinned geometry is in the world: every soldier and the player's own body is refitted EVERY frame, and
    /// a refit's scratch is a fraction of a build's.
    /// </remarks>
    public ulong ScratchSize => Refits ? UpdateScratchSize : BuildScratchSize;

    /// <summary>Points every geometry at a new vertex buffer, before a refit reads them.</summary>
    /// <param name="vertices">The buffer holding the pose this frame is to be refitted from.</param>
    /// <remarks>
    /// <para>
    /// For skinned geometry, whose vertices are not merely rewritten but rewritten into a DIFFERENT
    /// buffer each frame: the mesh keeps a ring of pose buffers so that a frame still in flight is never
    /// reading the one being written, so the buffer the structure was built against is stale by the next
    /// frame. A refit against it would fit the structure to the pose of two frames ago.
    /// </para>
    /// <para>
    /// Legal because an update build re-describes its geometries: what an update may not change is the
    /// shape of the problem -- the geometry count, the primitive counts, the flags -- and none of that
    /// moves here. Only where the positions are read from does.
    /// </para>
    /// </remarks>
    public void RepointVertices(IRhiBuffer vertices)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        for (int i = 0; i < _geometries.Length; i++)
        {
            _geometries[i] = _geometries[i] with { Vertices = vertices };
        }
    }

    /// <summary>Describes this structure's build for the next batch, and marks it built.</summary>
    /// <param name="scratch">Its scratch range, at least <see cref="ScratchSize"/> bytes long.</param>
    /// <returns>The build, a refit where <see cref="Refits"/> said so.</returns>
    public RhiBottomLevelBuild DescribeBuild((IRhiBuffer Buffer, ulong Offset) scratch)
    {
        RhiBottomLevelBuild build = new(
            Structure,
            _geometries.ToArray(),
            Flags,
            Refits,
            scratch.Buffer,
            scratch.Offset);

        IsBuilt = true;
        return build;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Structure.Dispose();
    }
}
