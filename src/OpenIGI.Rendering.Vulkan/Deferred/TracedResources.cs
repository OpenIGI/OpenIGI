using OpenIGI.Core.Numerics;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Deferred;

/// <summary>
/// The world a reflected ray is fired at, and the lit cards that say what it found — resolved to this
/// backend's own handles, ready to bind.
/// </summary>
/// <remarks>
/// <para>
/// The engine's side of this is <see cref="SurfaceCacheBinding"/>, which names the same things by their
/// device handles. This is what those handles resolve to, and the resolution happens once a frame at the
/// point of use rather than when the binding arrives: a mission being torn down can leave the device
/// holding handles whose resources are already gone, and a resolve at handover would throw on a frame
/// that was never going to trace.
/// </para>
/// <para>
/// A frame with nothing to trace still binds every descriptor the pass declares — a set with a hole in it
/// is undefined to bind, not merely unread — so the caller hands over placeholders and the parameter
/// block says to fire no rays. <see cref="Traces"/> is that question, asked once rather than field by
/// field.
/// </para>
/// </remarks>
/// <param name="Scene">The world's acceleration structure.</param>
/// <param name="Pages">The card page records.</param>
/// <param name="PageBytes">How many bytes of those records there are.</param>
/// <param name="Cells">The lookup grid's buckets.</param>
/// <param name="CellBytes">How many bytes of buckets there are.</param>
/// <param name="CellPages">The page indices the buckets point into.</param>
/// <param name="CellPageBytes">How many bytes of indices there are.</param>
/// <param name="CardAlbedo">The card atlas's albedo, whose alpha says which texels were written.</param>
/// <param name="CardDepth">The card atlas's depth, which decides between cards a point lies inside.</param>
/// <param name="CardLighting">The lighting atlas: the outgoing radiance each card settled at.</param>
/// <param name="CameraToCache">Where the camera stands relative to the cache's own origin.</param>
/// <param name="LookupMinimum">The lookup grid's low corner, in the cache's space.</param>
/// <param name="LookupCell">How much world one grid cell covers, in metres.</param>
/// <param name="LookupBuckets">How many buckets the grid has.</param>
/// <param name="AtlasWidth">How wide the lighting atlas is, in texels.</param>
/// <param name="AtlasHeight">How tall the lighting atlas is, in texels.</param>
/// <param name="RecordAddress">
/// The device address of the traced world's per-geometry material records, or zero where there are
/// none. What lets a pass shade a hit from the hit's OWN texture rather than from the frame or the
/// cards — the only route that can put a colour on a body the camera cannot see.
/// </param>
/// <param name="AlbedoArray">
/// The reduced colour of every world texture, one layer each, indexed by the layer a record names.
/// Optional: a pass with no array simply never takes that route, so this is deliberately NOT part of
/// <see cref="TracedResources.Traces"/>.
/// </param>
/// <param name="DistanceFieldVolume">
/// The level's distance-field clipmaps, for a pass that MARCHES rather than traces. Absent on a frame
/// with no field, and unused by the traced variant, which has an acceleration structure instead.
/// </param>
/// <param name="Field">
/// The boxes and scales those clipmaps are read with. Camera-relative, so it changes every frame.
/// </param>
/// <param name="BounceScale">How much of a card's radiance a traced ray keeps.</param>
internal readonly record struct TracedResources(
    AccelerationStructureKHR Scene,
    Buffer Pages,
    ulong PageBytes,
    Buffer Cells,
    ulong CellBytes,
    Buffer CellPages,
    ulong CellPageBytes,
    ImageView CardAlbedo,
    ImageView CardDepth,
    ImageView CardLighting,
    Vector3f CameraToCache,
    Vector3f LookupMinimum,
    float LookupCell,
    int LookupBuckets,
    int AtlasWidth,
    int AtlasHeight,
    float BounceScale,
    ulong RecordAddress = 0,
    ImageView AlbedoArray = default,
    ImageView DistanceFieldVolume = default,
    DistanceFieldBinding Field = default)
{
    /// <summary>Nothing to trace against, for a frame that is to fire no rays.</summary>
    public static TracedResources None { get; }

    /// <summary>Gets whether there is a field to march and cards to resolve what it finds.</summary>
    /// <remarks>
    /// The software counterpart of <see cref="Traces"/>, and deliberately a separate question. An adapter
    /// that cannot trace still has both of these, and a reflection built on them is the difference
    /// between a window that shows the level and one that shows the sky. Same all-or-nothing rule: a
    /// march with no cards finds geometry and has nothing to say about it.
    /// </remarks>
    public bool Marches =>
        Field.IsPresent
        && DistanceFieldVolume.Handle != 0
        && Pages.Handle != 0
        && Cells.Handle != 0
        && CellPages.Handle != 0
        && CardAlbedo.Handle != 0
        && CardDepth.Handle != 0
        && CardLighting.Handle != 0
        && AtlasWidth > 0
        && AtlasHeight > 0
        && LookupBuckets > 0
        && LookupCell > 0f;

    /// <summary>Gets whether there is a world AND a complete set of cards to resolve a hit against.</summary>
    /// <remarks>
    /// All or nothing, deliberately. A trace with a structure and no cards would find geometry and have
    /// nothing to say about it; a lookup missing one of its three buffers reads whatever the descriptor
    /// last held. Neither degrades into a worse answer — both are wrong answers — so the pass asks this
    /// and stands down rather than trying.
    /// </remarks>
    public bool Traces =>
        Scene.Handle != 0
        && Pages.Handle != 0
        && Cells.Handle != 0
        && CellPages.Handle != 0
        && CardAlbedo.Handle != 0
        && CardDepth.Handle != 0
        && CardLighting.Handle != 0
        && LookupBuckets > 0
        && LookupCell > 0f
        && AtlasWidth > 0
        && AtlasHeight > 0;
}
