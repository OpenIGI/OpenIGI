using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering.Rhi;

/// <summary>A structure a shader may trace rays against.</summary>
public interface IRhiAccelerationStructure : IRhiResource;

/// <summary>
/// The triangles of one placement, as the structure builder reads them.
/// </summary>
/// <param name="VertexBuffer">The buffer holding the positions.</param>
/// <param name="VertexOffset">Where the positions start.</param>
/// <param name="VertexCount">How many there are.</param>
/// <param name="VertexStride">How many bytes one vertex occupies.</param>
/// <param name="IndexBuffer">The buffer holding the indices.</param>
/// <param name="IndexOffset">Where they start.</param>
/// <param name="IndexCount">How many there are.</param>
/// <param name="IndexFormat">How wide one index is.</param>
/// <param name="Opaque">
/// Whether every triangle covers its whole area. A cut-out surface — a wire fence, a ladder, foliage —
/// is not, and taking it at face value makes it a solid occluder of light that in fact passes through
/// it.
/// </param>
public readonly record struct RhiRayTracingGeometry(
    IRhiBuffer VertexBuffer,
    ulong VertexOffset,
    int VertexCount,
    int VertexStride,
    IRhiBuffer IndexBuffer,
    ulong IndexOffset,
    int IndexCount,
    RhiIndexFormat IndexFormat,
    bool Opaque);

/// <summary>Which rays an instance answers.</summary>
/// <remarks>
/// A ray carries a mask and an instance carries one; the instance is considered only where the two
/// share a bit. What this renderer needs it for is the first-person rig: a hand-sized occluder wrapped
/// permanently around the camera would darken whatever the player happened to be facing, so the gather's
/// rays are traced against everything except it.
/// </remarks>
[Flags]
public enum RhiRayMask : byte
{
    /// <summary>Nothing traces against it.</summary>
    None = 0,

    /// <summary>The world: everything a gather or a shadow should find.</summary>
    World = 1 << 0,

    /// <summary>The first-person rig, which only a ray asking for it finds.</summary>
    ViewModel = 1 << 7,

    /// <summary>Everything.</summary>
    All = 0xFF,
}

/// <summary>
/// Tracing rays against the world, where the device can.
/// </summary>
/// <remarks>
/// <para>
/// A capability interface, offered only by a device reporting <see cref="RhiFeatures.RayQuery"/>. A pass
/// that wants it asks for it and stands down when it is not there, which is what already happens on a
/// Mac and what will happen in a browser.
/// </para>
/// <para>
/// The structure itself is the implementation's business, and deliberately so: scratch allocation,
/// compaction, device addresses and the three-frame handshake that keeps a compacted structure alive
/// until nothing reads the original are Vulkan's problems and have no meaning anywhere else. What is
/// shared is the world as placements — this mesh, here, opaque or not — which is the level the renderer
/// already thinks at.
/// </para>
/// </remarks>
public interface IRhiRayTracing
{
    /// <summary>Gets the world's structure, to bind to an acceleration-structure slot.</summary>
    /// <remarks>Null until enough of the world has been observed for one to exist.</remarks>
    IRhiAccelerationStructure? WorldScene { get; }

    /// <summary>Gets whether the world's structure is built and may be traced against.</summary>
    /// <remarks>
    /// A structure that exists is not yet a structure that answers: a build recorded this frame is not
    /// complete until it has run. A pass that traces before this is true gets misses everywhere, which
    /// reads as a room flooded with sky rather than as an error.
    /// </remarks>
    bool IsWorldReady { get; }

    /// <summary>Gets how many placements the world holds.</summary>
    int PlacementCount { get; }

    /// <summary>Tells the world where a mesh stands.</summary>
    /// <param name="meshId">The mesh, which identifies its geometry for as long as it exists.</param>
    /// <param name="geometry">Its triangles. Read once per mesh; later calls may pass the same values.</param>
    /// <param name="transform">Where it stands in the world.</param>
    /// <param name="mask">Which rays find it.</param>
    void Observe(uint meshId, in RhiRayTracingGeometry geometry, in Matrix4f transform, RhiRayMask mask);

    /// <summary>Takes a mesh out of the world.</summary>
    /// <param name="meshId">The mesh.</param>
    void Forget(uint meshId);

    /// <summary>Empties the world, for a level change.</summary>
    void Reset();

    /// <summary>Records whatever building or updating the world needs this frame.</summary>
    /// <param name="encoder">The encoder to record into.</param>
    /// <remarks>
    /// Recorded before anything traces, and cheap when nothing has moved: what this costs is decided by
    /// what was observed since the last call, not by the size of the world.
    /// </remarks>
    void RecordSceneUpdate(IRhiCommandEncoder encoder);
}
