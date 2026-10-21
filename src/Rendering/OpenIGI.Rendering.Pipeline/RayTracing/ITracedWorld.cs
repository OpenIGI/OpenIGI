using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline.RayTracing;

/// <summary>A mesh as the traced world reads it: its buffers, and whether they change.</summary>
/// <param name="Vertices">The vertices this frame draws: 48-byte <c>Vertex3D</c>s, position first.</param>
/// <param name="Indices">The 16-bit indices.</param>
/// <param name="VertexCapacity">How many vertices the buffer holds.</param>
/// <param name="IndexCapacity">How many indices the buffer holds.</param>
/// <param name="DrawableIndexCount">How many indices are worth tracing.</param>
/// <param name="Animated">Whether the vertices are rewritten each pose, so its structure is refitted.</param>
/// <param name="Storage">Whether it is a storage mesh its owner rewrites.</param>
/// <param name="ContentVersion">How many times the contents have changed.</param>
public readonly record struct TracedMeshBuffers(
    IRhiBuffer Vertices,
    IRhiBuffer Indices,
    int VertexCapacity,
    int IndexCapacity,
    int DrawableIndexCount,
    bool Animated,
    bool Storage,
    int ContentVersion);

/// <summary>One drawn run of a mesh's indices, and what it is made of.</summary>
/// <param name="IndexOffset">The first index of the run.</param>
/// <param name="IndexCount">How many indices it covers; a multiple of three.</param>
/// <param name="TextureId">
/// Its diffuse texture, as <see cref="ITracedWorld.ObserveTexture"/> named it: whether the run is cut
/// out, and by which mask, is a question about its texture.
/// </param>
/// <param name="Emissive">
/// How strongly the run glows, as a multiple of its albedo -- the strength the G-buffer gives it; nought for
/// a surface that does not. A reflection that stops on a lamp shows it lit by this.
/// </param>
public readonly record struct TracedSurface(int IndexOffset, int IndexCount, uint TextureId, float Emissive = 0f);

/// <summary>Which rays an instance answers.</summary>
/// <remarks>
/// A ray carries a mask and an instance carries one; the instance is considered only where the two
/// share a bit. What this renderer needs it for is the first-person rig: a hand-sized occluder wrapped
/// permanently around the camera would darken whatever the player happened to be facing, so shadows are
/// traced against everything except it.
/// </remarks>
[Flags]
public enum RayMask : byte
{
    /// <summary>Nothing traces against it.</summary>
    None = 0,

    /// <summary>The world: everything below the first-person bit.</summary>
    World = 0x7F,

    /// <summary>The first-person rig, which only a ray asking for it finds.</summary>
    ViewModel = 0x80,

    /// <summary>Everything.</summary>
    All = 0xFF,
}

/// <summary>
/// The world as rays see it, where the device can trace them: built out of what the renderer draws, over the
/// RHI's acceleration structures.
/// </summary>
/// <remarks>
/// <para>
/// There is one only where the device offers <see cref="IRhiDevice.AccelerationStructures"/>; a pass that
/// wants it asks for it and stands down when it is absent, as it is in a browser.
/// </para>
/// <para>
/// The world is built out of what the renderer draws. A ray has to be able to strike geometry that is not
/// on screen -- the wall casting a shadow is very often behind the camera -- so placements accumulate: every
/// geometry-pass draw is observed, a placement seen once stays, and within the first seconds of a level the
/// set is the whole level. Movers are traced where they are this frame. How the structures are built,
/// refitted, pooled and kept alive until no frame in flight reads them is the implementation's business.
/// </para>
/// </remarks>
public interface ITracedWorld
{
    /// <summary>Gets the world's structure, to bind to an acceleration-structure slot; null before one is built.</summary>
    IRhiAccelerationStructure? WorldScene { get; }

    /// <summary>Gets whether the world's structure holds anything worth tracing against.</summary>
    bool IsWorldReady { get; }

    /// <summary>Gets whether the world's structure has stopped growing: nothing new admitted for a while.</summary>
    bool IsWorldSettled => IsWorldReady;

    /// <summary>Lifts, or restores, the per-frame budget on meshes newly admitted to the world.</summary>
    /// <param name="unlimited">Whether every mesh offered is admitted in the frame it is offered.</param>
    void SetUnlimitedAdmission(bool unlimited)
    {
        _ = unlimited;
    }

    /// <summary>Gets each observed texture's colour, one layer a texture, for a hit a reflection has to shade.</summary>
    /// <remarks>
    /// A reflected ray can strike a surface the camera cannot see, where neither the frame nor the surface
    /// cache has its colour at the hit's own texel; this array does, addressed through the hit records.
    /// </remarks>
    IRhiTextureView TracedAlbedo { get; }

    /// <summary>Gets the structure a tracing shader binds: the world's once ready, else an empty stand-in.</summary>
    /// <remarks>
    /// A shader that names a structure needs one in its descriptor from its first dispatch, and the world is
    /// not built until the level has been drawn once. Until then this is a scene every ray misses -- the
    /// answer an empty world would give anyway -- so the indirect light can trace from the first frame.
    /// </remarks>
    IRhiAccelerationStructure TraceableScene { get; }

    /// <summary>Gets how many placements the world holds.</summary>
    int PlacementCount { get; }

    /// <summary>Gets what the world holds, for the renderer to report.</summary>
    TracedWorldStatistics Statistics { get; }

    /// <summary>Gets the handle of the hit records a shader looks a hit's material up in.</summary>
    /// <remarks>The records buffer's <see cref="IRhiBuffer.ShaderHandle"/>, which a shader reads through <c>Common/HitRecords.slang</c>.</remarks>
    ulong HitRecordHandle { get; }

    /// <summary>Gets the cut-out masks a hit's record names a layer of: a 2D array, one mask a layer.</summary>
    IRhiTextureView CutoutMasks { get; }

    /// <summary>Hands the world a texture's pixels, the one moment they are in hand.</summary>
    /// <param name="textureId">The texture's identifier, as surfaces name it.</param>
    /// <param name="pixels">Its top level.</param>
    /// <param name="width">Its width.</param>
    /// <param name="height">Its height.</param>
    /// <remarks>
    /// A cut-out texture -- a fence, foliage, a window's bars -- is reduced to a mask here, so a ray can
    /// pass through its holes. Nowhere later has the pixels.
    /// </remarks>
    void ObserveTexture(uint textureId, ReadOnlySpan<Bgra32> pixels, int width, int height);

    /// <summary>Records a run of a mesh drawn somewhere, adding it to the world if it is new.</summary>
    /// <param name="meshId">The mesh's identifier.</param>
    /// <param name="mesh">The mesh's buffers.</param>
    /// <param name="worldTransform">Where it is drawn, in absolute world space.</param>
    /// <param name="surface">Which run of its indices, and its texture.</param>
    /// <param name="mask">Which rays find it.</param>
    void Observe(uint meshId, in TracedMeshBuffers mesh, in Matrix4f worldTransform, in TracedSurface surface, RayMask mask);

    /// <summary>Places a caster for this frame only -- the terrain as drawn -- rebuilt whenever its contents change.</summary>
    /// <param name="meshId">The mesh's identifier.</param>
    /// <param name="mesh">The mesh's buffers.</param>
    /// <param name="worldTransform">Where it is placed, in absolute world space.</param>
    /// <param name="indexCount">How many indices, from the start, make up the caster.</param>
    void ObserveCaster(uint meshId, in TracedMeshBuffers mesh, in Matrix4f worldTransform, int indexCount);

    /// <summary>Takes a mesh out of the world, for one that is destroyed.</summary>
    /// <param name="meshId">The mesh.</param>
    void Forget(uint meshId);

    /// <summary>Lets a destroyed texture's colour layer go, for a later texture to take.</summary>
    /// <param name="textureId">The texture.</param>
    void ForgetTexture(uint textureId);

    /// <summary>Empties the world, for a level change.</summary>
    void Reset();

    /// <summary>Records whatever building or refitting the world needs this frame, before anything traces.</summary>
    /// <param name="encoder">The encoder to record into, outside any pass.</param>
    /// <param name="timestamps">
    /// Where to record when the update began and ended, or <see langword="null"/>. Asked of the update
    /// itself rather than of a pass around it, because building structures happens outside any pass.
    /// </param>
    void RecordSceneUpdate(IRhiCommandEncoder encoder, RhiTimestampWrites? timestamps = null);
}
