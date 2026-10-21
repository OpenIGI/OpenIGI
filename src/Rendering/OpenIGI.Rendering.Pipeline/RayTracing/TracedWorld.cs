using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline.RayTracing;

/// <summary>
/// The traced world, over the RHI's acceleration structures: one implementation for every backend that
/// traces.
/// </summary>
/// <remarks>
/// <para>
/// Structures per mesh, placements accumulated from the frame's draws, skinned meshes refitted, the
/// terrain placed as drawn, cut-out masks and the hit records that name them. Meshes arrive as RHI buffers;
/// the GPU's progress comes from the device's own work markers, through <see cref="SubmissionClock"/>.
/// A backend supplies only <see cref="IRhiAccelerationStructures"/> and the encoder's build call.
/// </para>
/// <para>
/// A cut-out texture is decided by <see cref="OpacityMasks.IsCutout"/>. Such a surface with no mask to test
/// against is kept out of the world rather than traced as a solid board.
/// </para>
/// </remarks>
internal sealed class TracedWorld : ITracedWorld, IDisposable
{
    private readonly SubmissionClock _clock;
    private readonly WorldScene _scene;
    private readonly OpacityMasks _masks;
    private readonly PlaceholderScene _placeholder;
    private readonly TracedAlbedo _albedo;
    private readonly HashSet<uint> _cutout = [];

    // One adapter per mesh, refreshed on every sighting: observed once per draw, a fresh one each time
    // would be thousands of allocations a frame.
    private readonly Dictionary<uint, TracedMesh> _meshes = [];
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="TracedWorld"/> class.</summary>
    /// <param name="device">The device, whose submissions the world's lifetimes follow.</param>
    /// <param name="structures">Its acceleration structures.</param>
    /// <param name="albedoResolution">How many texels square each texture's colour is kept at for a reflected hit.</param>
    /// <param name="logger">Where the world reports running out of room.</param>
    public TracedWorld(
        IRhiDevice device,
        IRhiAccelerationStructures structures,
        int albedoResolution,
        IOpenIgiLogger logger)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(structures);
        ArgumentNullException.ThrowIfNull(logger);

        _clock = new SubmissionClock(device);
        _scene = new WorldScene(device, structures, logger);
        _albedo = new TracedAlbedo(device, albedoResolution);
        _masks = new OpacityMasks(device);

        // Built at once: the tracing shaders bind a structure from their first dispatch, before the world
        // has seen a draw.
        _placeholder = new PlaceholderScene(device, structures, _clock);
    }

    /// <inheritdoc/>
    public IRhiAccelerationStructure? WorldScene => _scene.TopLevel;

    /// <inheritdoc/>
    public bool IsWorldReady => _scene.IsReady;

    /// <inheritdoc/>
    public bool IsWorldSettled => _scene.IsSettled;

    /// <inheritdoc/>
    public void SetUnlimitedAdmission(bool unlimited) => _scene.UnlimitedAdmission = unlimited;

    /// <inheritdoc/>
    public IRhiAccelerationStructure TraceableScene =>
        _scene.IsReady && WorldScene is { } world
            ? world
            : _placeholder.TopLevel ?? throw new InvalidOperationException("The placeholder scene built no structure.");

    /// <inheritdoc/>
    public int PlacementCount => _scene.PlacementCount;

    /// <inheritdoc/>
    public TracedWorldStatistics Statistics => new(
        _scene.GeometryCount,
        _scene.SkinnedGeometryCount,
        _scene.CasterGeometryCount,
        _scene.PlacementCount,
        _albedo.Count,
        _albedo.Overflowed);

    /// <inheritdoc/>
    public ulong HitRecordHandle => _scene.RecordHandle;

    /// <inheritdoc/>
    public IRhiTextureView CutoutMasks => _masks.View;

    /// <inheritdoc/>
    public IRhiTextureView TracedAlbedo => _albedo.View;

    /// <inheritdoc/>
    public void ObserveTexture(uint textureId, ReadOnlySpan<Bgra32> pixels, int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (width <= 0 || height <= 0 || pixels.Length < width * height)
        {
            return;
        }

        if (OpacityMasks.IsCutout(pixels, width, height))
        {
            _cutout.Add(textureId);
            _masks.Register(textureId, pixels, width, height);
        }

        // Every texture, not only the cut-outs: a reflected ray can strike anything. The array turns away
        // what it does not want, so the size question lives there.
        _albedo.Register(textureId, pixels, width, height);
    }

    /// <inheritdoc/>
    public void ForgetTexture(uint textureId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _albedo.Release(textureId);
        _masks.Release(textureId);
        _cutout.Remove(textureId);
    }

    /// <inheritdoc/>
    public void Observe(uint meshId, in TracedMeshBuffers mesh, in Matrix4f worldTransform, in TracedSurface surface, RayMask mask)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        bool cutout = _cutout.Contains(surface.TextureId);
        int maskLayer = -1;
        if (cutout && !_masks.TryGetLayer(surface.TextureId, out maskLayer))
        {
            return;
        }

        _scene.Observe(
            meshId,
            Adapt(meshId, mesh),
            worldTransform,
            new RayTracedSurface(
                surface.IndexOffset,
                surface.IndexCount,
                !cutout,
                maskLayer,

                // A miss is common and fine -- the hit then resolves as it would with no array -- so it
                // is not a reason to keep the run out, as a missing mask is.
                _albedo.TryGetLayer(surface.TextureId, out int albedoLayer) ? albedoLayer : -1,
                mesh.Animated,
                surface.Emissive),
            (byte)mask);
    }

    /// <inheritdoc/>
    public void ObserveCaster(uint meshId, in TracedMeshBuffers mesh, in Matrix4f worldTransform, int indexCount)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _scene.ObserveCaster(meshId, Adapt(meshId, mesh), worldTransform, indexCount);
    }

    /// <inheritdoc/>
    public void Forget(uint meshId)
    {
        _meshes.Remove(meshId);
        _scene.Forget(meshId);
    }

    /// <inheritdoc/>
    /// <remarks>Only with the device idle, which a level change already waits for.</remarks>
    public void Reset()
    {
        _meshes.Clear();
        _scene.Reset();
        _clock.Settle();
    }

    /// <inheritdoc/>
    public void RecordSceneUpdate(IRhiCommandEncoder encoder, RhiTimestampWrites? timestamps = null)
    {
        ArgumentNullException.ThrowIfNull(encoder);
        ObjectDisposedException.ThrowIf(_disposed, this);

        _clock.Advance();
        _scene.Record(encoder, _clock, timestamps);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _masks.Dispose();
        _albedo.Dispose();
        _placeholder.Dispose();
        _scene.Dispose();
    }

    private TracedMesh Adapt(uint meshId, in TracedMeshBuffers mesh)
    {
        if (!_meshes.TryGetValue(meshId, out TracedMesh? adapter))
        {
            adapter = new TracedMesh();
            _meshes[meshId] = adapter;
        }

        // This sighting's, because a skinned mesh draws from a different buffer of its pose ring each
        // frame and the world re-points its structure at whichever one this is.
        adapter.Mesh = mesh;
        return adapter;
    }

    /// <summary>A mesh as the traced world reads it, as of its latest sighting.</summary>
    private sealed class TracedMesh : IRayTracedMesh
    {
        public TracedMeshBuffers Mesh { get; set; }

        public IRhiBuffer Vertices => Mesh.Vertices;

        public IRhiBuffer Indices => Mesh.Indices;

        public int VertexCapacity => Mesh.VertexCapacity;

        public int IndexCapacity => Mesh.IndexCapacity;

        public int DrawableIndexCount => Mesh.DrawableIndexCount;

        public bool Animated => Mesh.Animated;

        public bool Storage => Mesh.Storage;

        public int ContentVersion => Mesh.ContentVersion;
    }
}
