using System.Collections.Concurrent;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Threading;
using OpenIGI.Engine.Resources;
using OpenIGI.Formats.Textures;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering;

/// <summary>
/// Decodes a level's textures on worker threads, so the render thread only ever uploads them.
/// </summary>
/// <remarks>
/// <para>
/// Turning a <c>.tex</c> into something uploadable is all CPU and none of it is graphics: the file is
/// decoded, a cut-out is classified and its alpha widened, a mip chain is built by repeated box filters,
/// and the chain's coverage is corrected against the alpha-test cutoff. Measured over a menu session it
/// was the largest thing left on the render thread once the uploads stopped blocking — the mip chains
/// alone, one texture at a time on the thread that also has to present.
/// </para>
/// <para>
/// None of it needs the device, and every texture is independent of every other, so it belongs where the
/// rest of a backdrop is built: on a worker, ahead of the frame that wants it. The render thread's
/// <c>WorldTextureCache.GetTexture</c> then finds the image already decoded and does nothing but
/// create and upload.
/// </para>
/// <para>
/// <b>The bytes are gathered once, here, before any worker starts.</b> A
/// <see cref="ResourceRegistry"/> registers what it reads from disk, so <c>TryLoad</c> writes to a plain
/// dictionary and cannot be called from several threads at once. Reading <see cref="ResourceRegistry.Entries"/>
/// on the building thread and handing the workers nothing but byte spans keeps every registry access on
/// one thread, which is what makes this safe rather than merely fast.
/// </para>
/// <para>
/// Nothing here is load-bearing. A name the cache never saw, or one whose decode threw, simply is not in
/// it, and the renderer decodes that one itself exactly as it always did. The cache is an optimisation
/// with no semantics of its own.
/// </para>
/// </remarks>
public sealed class TextureDecodeCache
{
    /// <summary>The alpha below which a world texel is discarded rather than blended.</summary>
    /// <remarks>
    /// Lives here rather than on a renderer because two things have to agree on it: the draw that tests
    /// against it, and the decode that widens a cutout's alpha to survive it. The decode runs on a
    /// worker, ahead of any renderer existing, so the constant cannot belong to one.
    /// </remarks>
    public const float AlphaTestCutoff = 0.5f;

    private const string TexturePrefix = "LOCAL:textures/";
    private const string TextureSuffix = ".tex";

    /// <summary>How many cores the decode may take, leaving the rest to the thread drawing the frame.</summary>
    /// <remarks>
    /// Unbounded, this and the detail pack's own pass between them took every core the machine had, and
    /// the render thread was one of the things they took it from -- measured as a 600 ms frame on the
    /// level whose global-illumination convergence is heaviest, against 128 ms for the same frame when
    /// the decode was not running. Half the machine finishes the decode well inside the time the swap
    /// takes anyway, so the ceiling costs nothing and the frame keeps its cores.
    /// </remarks>
    internal static int MaxDecodeThreads => Math.Max(1, Environment.ProcessorCount / 2);

    private readonly ConcurrentDictionary<string, Lazy<DecodedSurface?>> _images =
        new(StringComparer.OrdinalIgnoreCase);

    private TextureDecodeCache()
    {
    }

    /// <summary>Gets how many textures the cache was seeded with.</summary>
    public int Count => _images.Count;

    /// <summary>Gets the decode and the detail maps' reads, finished once every texture is ready to upload.</summary>
    /// <remarks>
    /// Waited on by whoever builds a backdrop off the render thread, before handing it over. Adopted any
    /// earlier, the surface-cache capture on the render thread reached textures the pool had not, and
    /// decoded them itself -- most of a second of every backdrop swap in the menu.
    /// </remarks>
    public Task Completion { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Seeds a cache from every texture a registry holds and starts decoding them on the thread pool.
    /// </summary>
    /// <param name="resources">The level's own registry, read on the calling thread and only there.</param>
    /// <param name="cutoff">The alpha-test cutoff the shaders compare against.</param>
    /// <param name="work">
    /// Where the textures are decoded ahead of the frames that ask for them. Where nothing runs beside the
    /// caller, nothing is decoded ahead: each is decoded when it is taken.
    /// </param>
    /// <param name="detail">
    /// The session's detail-map pack, told to read the same names' PNGs on the pool. Optional; without it
    /// the maps are read on the render thread as they always were.
    /// </param>
    /// <param name="only">
    /// The textures worth decoding -- see <see cref="TexturesUsedBy"/> -- or <see langword="null"/> for
    /// every texture the registry holds.
    /// </param>
    /// <param name="keepBlocks">
    /// Whether the device samples block formats, so a texture pack's stored chains are handed over as they
    /// are -- a slice of the registry's own bytes, nothing decoded and nothing copied -- rather than
    /// expanded here.
    /// </param>
    /// <returns>The cache, already decoding.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="resources"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Given the scene's own textures, it decodes those and nothing else. It used to decode every texture
    /// the registry held -- which includes the whole common pack every level mounts -- on the grounds
    /// that the surface-cache capture walks every model anyway; but every decoded chain is held until it
    /// is taken, and what no placement draws was never taken. With a texture pack installed that was a
    /// couple of hundred megabytes of pictures nobody would look at, decoded on every backdrop swap.
    /// </remarks>
    public static TextureDecodeCache StartFor(
        ResourceRegistry resources,
        float cutoff,
        IWorkScheduler work,
        MaterialDetailPack? detail = null,
        IReadOnlySet<string>? only = null,
        bool keepBlocks = false)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(work);

        TextureDecodeCache cache = new();
        List<Lazy<DecodedSurface?>> pending = [];
        List<string> names = [];

        foreach (ResourceEntry entry in resources.Entries)
        {
            if (!entry.Name.StartsWith(TexturePrefix, StringComparison.OrdinalIgnoreCase)
                || !entry.Name.EndsWith(TextureSuffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string name = entry.Name[TexturePrefix.Length..^TextureSuffix.Length];
            if (only is not null && !only.Contains(name))
            {
                continue;
            }

            ReadOnlyMemory<byte> bytes = entry.Data;

            // Lazy rather than a bare task, so the render thread asking for one the pool has not reached
            // decodes it itself instead of queueing behind everything else, and one already in flight is
            // waited for rather than done twice.
            Lazy<DecodedSurface?> lazy = new(
                () => Decode(bytes, cutoff, keepBlocks), LazyThreadSafetyMode.ExecutionAndPublication);

            if (cache._images.TryAdd(name, lazy))
            {
                pending.Add(lazy);
                names.Add(MaterialDetailPack.KeyFor(name, resources.OverrideStamp(entry.Name)));
            }
        }

        // The other half of what a texture costs the render thread: its derived normal and roughness,
        // which live in a PNG beside the pack and were being inflated inside the upload.
        Task maps = detail?.Prewarm(names) ?? Task.CompletedTask;

        // The renderer's own Take forces any value this has not got to yet, so nothing HAS to wait -- but a
        // builder that can should, see Completion. Where nothing runs beside the caller there is nothing to
        // get ahead of, so each texture is decoded when it is taken.
        Task textures = pending.Count > 0 && work.IsConcurrent
            ? work.Run(() => work.ForEach(pending, MaxDecodeThreads, item => _ = item.Value))
            : Task.CompletedTask;

        cache.Completion = Task.WhenAll(textures, maps);
        return cache;
    }

    /// <summary>Lists the textures a scene's placements draw with, for <see cref="StartFor"/>.</summary>
    /// <param name="scene">The level's scene.</param>
    /// <returns>Their bare names, compared without regard to case as the registry compares them.</returns>
    /// <remarks>
    /// Every material slot of every placement's instance, which is a superset of what the renderer asks
    /// for: it resolves a texture per drawn subset, and a subset's slot is one of its instance's.
    /// </remarks>
    public static IReadOnlySet<string> TexturesUsedBy(WorldScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        foreach (WorldPlacement placement in scene.Placements)
        {
            int slots = placement.Instance.MaterialIndices.Length;
            for (int slot = 0; slot < slots; slot++)
            {
                if (placement.Pack.TryGetTextureName(placement.Instance, slot, out string name))
                {
                    names.Add(name);
                }
            }
        }

        return names;
    }

    /// <summary>Drops every decoded texture nobody took.</summary>
    /// <remarks>
    /// For when a level is resident and nothing more will be asked of the cache. Whatever is left is a
    /// picture no placement drew, and holding it would be holding it for as long as the level lives.
    /// A name asked for afterwards is decoded by the renderer itself, as one the cache never saw is.
    /// </remarks>
    public void ReleaseUntaken() => _images.Clear();

    /// <summary>Takes a decoded texture out of the cache, decoding it here if the pool has not yet.</summary>
    /// <param name="name">The bare texture name, without the resource prefix or extension.</param>
    /// <param name="image">The decoded image, or <see langword="null"/> when there is none to be had.</param>
    /// <returns><see langword="true"/> when the cache owned this name, whether or not it decoded.</returns>
    /// <remarks>
    /// Removed as it is taken. A level's decoded chains are a couple of hundred megabytes of managed
    /// memory between them and the caller is about to put the only copy that matters on the GPU, so
    /// holding them after that would be keeping every texture of every backdrop ever shown.
    /// </remarks>
    public bool TryTake(string name, out TextureImage? image) =>
        TryTake(name, out image, out _);

    /// <summary>Takes a decoded texture and the surface map its asset carried, if any.</summary>
    /// <param name="name">The bare texture name, without the resource prefix or extension.</param>
    /// <param name="image">The decoded image, or <see langword="null"/> when there is none to be had.</param>
    /// <param name="detail">The packed surface map the asset carried, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the cache owned this name, whether or not it decoded.</returns>
    public bool TryTake(string name, out TextureImage? image, out TextureImage? detail)
    {
        bool owned = TryTakeSurface(name, out DecodedSurface? surface);
        image = surface?.Image;
        detail = surface?.Detail;
        return owned;
    }

    /// <summary>Takes a decoded surface, compressed or not, decoding it here if the pool has not yet.</summary>
    /// <param name="name">The bare texture name, without the resource prefix or extension.</param>
    /// <param name="surface">The surface, or <see langword="null"/> when there is none to be had.</param>
    /// <returns><see langword="true"/> when the cache owned this name, whether or not it decoded.</returns>
    public bool TryTakeSurface(string name, out DecodedSurface? surface)
    {
        if (!_images.TryRemove(name, out Lazy<DecodedSurface?>? lazy))
        {
            surface = null;
            return false;
        }

        surface = lazy.Value;
        return true;
    }

    private static DecodedSurface? Decode(ReadOnlyMemory<byte> bytes, float cutoff, bool keepBlocks)
    {
        try
        {
            // The game's own texture, or whatever the overrides folder put in its place -- the
            // substitution happens in the resource registry now, so by the time bytes reach here they are
            // already the ones that should be drawn and this path does not need to know which. It does
            // need to know the FORM: a replacement may carry its derived surface map as a second layer,
            // and that map has to travel with the picture rather than be looked up separately.
            SurfaceAsset asset = keepBlocks
                ? SurfaceAssetReader.ReadKeepingBlocks(bytes)
                : SurfaceAssetReader.Read(bytes.Span);

            // A stored chain had all of what follows done to it when the pack was built.
            if (asset.CompressedDiffuse is { } compressed)
            {
                return new DecodedSurface(null, null, compressed, asset.CompressedDetail);
            }

            TextureImage image = asset.Diffuse;

            // The same sequence WorldTextureCache.GetTexture ran inline, in the same order and for the same
            // reasons -- classify the cut-out from level zero before the alpha is touched, widen it,
            // then build or rebuild the chain and correct its coverage. See that method for why each
            // step is here; this is a move, not a change.
            bool cutout = AlphaCoverage.IsCutout(image.GetLevel(0));

            image.WidenCutoutAlpha(cutoff);
            image = cutout ? image.RebuildMipChain() : image.WithMipChain();
            image.PreserveAlphaCoverage(cutoff);

            // The map is NOT put through that sequence. It is not a picture: its channels are a normal's
            // x and y, a roughness and an occlusion, so widening an alpha cutoff in it would corrupt the
            // occlusion and rebuilding its chain in display-encoded space would bias every level. The
            // device builds its chain linearly when it uploads.
            return new DecodedSurface(image, asset.Detail, null, null);
        }
        catch (Exception ex) when (ex is TextureFormatException or ArgumentException)
        {
            // Reported where it matters, by the renderer, when it finds nothing here and decodes the
            // same bytes itself. Swallowing it on the worker keeps the pool out of the logger.
            return null;
        }
    }
}

/// <summary>A surface ready to upload: either a decoded chain, or a stored block-compressed one.</summary>
/// <param name="Image">The diffuse, corrected and mipped, or <see langword="null"/> when it is compressed.</param>
/// <param name="Detail">The packed surface map, or <see langword="null"/>.</param>
/// <param name="Compressed">The diffuse as a stored block-compressed chain, or <see langword="null"/>.</param>
/// <param name="CompressedDetail">The surface map as a stored block-compressed chain, or <see langword="null"/>.</param>
public sealed record DecodedSurface(
    TextureImage? Image,
    TextureImage? Detail,
    CompressedTextureImage? Compressed,
    CompressedTextureImage? CompressedDetail);
