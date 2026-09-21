using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering;

/// <summary>
/// Everything a backend pass needs to turn a traced hit into the light that surface sends: the cards the
/// level was captured onto, the grid that finds one, and the atlas holding what each card ended up lit to.
/// </summary>
/// <remarks>
/// <para>
/// The surface cache is the engine's, built and lit above the renderer seam, and the passes that read it
/// have so far been the engine's own compute shaders — which bind it by unit through
/// <see cref="IGraphicsDevice.BindStorageBuffer"/> and friends. A backend-owned pass cannot do that: it
/// owns its descriptor set outright and has to be handed the resources by name. This is that handover,
/// and nothing more — the backend never builds, lights or invalidates any of it.
/// </para>
/// <para>
/// <b>Why a backend pass wants it at all.</b> A reflected ray that leaves the screen has no answer in the
/// frame, and the only record of what stands off-screen is the cache. See
/// <see cref="SurfaceCacheShaders.Lookup"/> for the lookup itself, which is shared source precisely so
/// that the gather and the reflection land on the same card.
/// </para>
/// </remarks>
/// <param name="Pages">The page records: one run of vectors per card, as the lookup reads them.</param>
/// <param name="Cells">The lookup grid's buckets, each a start and a count into <paramref name="CellPages"/>.</param>
/// <param name="CellPages">The page indices the buckets point into.</param>
/// <param name="Albedo">The card atlas's albedo, whose alpha says which texels were written at all.</param>
/// <param name="Depth">The card atlas's depth, which decides between cards a point lies inside.</param>
/// <param name="Lighting">The lighting atlas: the outgoing radiance each card settled at.</param>
/// <param name="AtlasWidth">How wide the lighting atlas is, in texels.</param>
/// <param name="AtlasHeight">How tall the lighting atlas is, in texels.</param>
/// <param name="LookupBuckets">How many buckets the grid has. A power of two; the hash masks with it.</param>
/// <param name="LookupMinimum">The grid's low corner, in the cache's own space.</param>
/// <param name="LookupCell">How much world one grid cell covers, in metres.</param>
/// <param name="CameraToCache">
/// Where the camera stands relative to the cache's origin. The cache measures everything from its own
/// centre so that a level tens of kilometres from the world origin still resolves in a float; a pass
/// working in camera-relative world has to add this to get into the space the lookup reads.
/// </param>
/// <param name="BounceScale">
/// How much of the cache's radiance a traced ray keeps — the same artistic control the gather applies,
/// so a reflection of a wall and the indirect light off that wall agree about how bright it is.
/// </param>
public readonly record struct SurfaceCacheBinding(
    StorageBufferHandle Pages,
    StorageBufferHandle Cells,
    StorageBufferHandle CellPages,
    TextureHandle Albedo,
    TextureHandle Depth,
    TextureHandle Lighting,
    int AtlasWidth,
    int AtlasHeight,
    int LookupBuckets,
    Vector3f LookupMinimum,
    float LookupCell,
    Vector3f CameraToCache,
    float BounceScale)
{
    /// <summary>A handover of nothing, under which no pass can resolve a hit.</summary>
    public static SurfaceCacheBinding None { get; }

    /// <summary>Gets whether every resource a lookup needs actually arrived.</summary>
    /// <remarks>
    /// All or nothing on purpose. A lookup with a page buffer and no grid, or a grid and no lighting
    /// atlas, does not degrade into a coarser answer — it reads whatever the unbound descriptor happens
    /// to hold. A pass asks this and traces nothing when it is false.
    /// </remarks>
    public bool IsPresent =>
        Pages.IsValid
        && Cells.IsValid
        && CellPages.IsValid
        && Albedo.IsValid
        && Depth.IsValid
        && Lighting.IsValid
        && AtlasWidth > 0
        && AtlasHeight > 0
        && LookupBuckets > 0
        && LookupCell > 0f;
}
