using System.Collections.Concurrent;
using OpenIGI.Core.Graphics;
using OpenIGI.Engine.Resources;
using OpenIGI.Formats.Textures;
using OpenIGI.Rendering;

namespace OpenIGI.Desktop;

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
/// <see cref="WorldRenderer.GetTexture"/> then finds the image already decoded and does nothing but
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

    private readonly ConcurrentDictionary<string, Lazy<TextureImage?>> _images =
        new(StringComparer.OrdinalIgnoreCase);

    private TextureDecodeCache()
    {
    }

    /// <summary>Gets how many textures the cache was seeded with.</summary>
    public int Count => _images.Count;

    /// <summary>
    /// Seeds a cache from every texture a registry holds and starts decoding them on the thread pool.
    /// </summary>
    /// <param name="resources">The level's own registry, read on the calling thread and only there.</param>
    /// <param name="cutoff">The alpha-test cutoff the shaders compare against.</param>
    /// <param name="detail">
    /// The session's detail-map pack, told to read the same names' PNGs on the pool. Optional; without it
    /// the maps are read on the render thread as they always were.
    /// </param>
    /// <param name="upscaled">
    /// The session's upscaled-texture pack, consulted before the archive for every name. Optional; without
    /// it, and for every name it does not have, the game's own texture is decoded exactly as before.
    /// </param>
    /// <returns>The cache, already decoding.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="resources"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Every registered texture rather than the ones the scene happens to name: the surface-cache capture
    /// walks every distinct model in the level, so the set it asks for is very nearly all of them, and
    /// working out which by walking geometry would cost more than decoding the few extra.
    /// </remarks>
    public static TextureDecodeCache StartFor(
        ResourceRegistry resources,
        float cutoff,
        MaterialDetailPack? detail = null,
        HdTexturePack? upscaled = null)
    {
        ArgumentNullException.ThrowIfNull(resources);

        TextureDecodeCache cache = new();
        List<Lazy<TextureImage?>> pending = [];
        List<string> names = [];

        foreach (ResourceEntry entry in resources.Entries)
        {
            if (!entry.Name.StartsWith(TexturePrefix, StringComparison.OrdinalIgnoreCase)
                || !entry.Name.EndsWith(TextureSuffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string name = entry.Name[TexturePrefix.Length..^TextureSuffix.Length];
            ReadOnlyMemory<byte> bytes = entry.Data;

            // Lazy rather than a bare task, so the render thread asking for one the pool has not reached
            // decodes it itself instead of queueing behind everything else, and one already in flight is
            // waited for rather than done twice.
            Lazy<TextureImage?> lazy = new(
                () => Decode(name, bytes, cutoff, upscaled), LazyThreadSafetyMode.ExecutionAndPublication);

            if (cache._images.TryAdd(name, lazy))
            {
                pending.Add(lazy);
                names.Add(name);
            }
        }

        // The other half of what a texture costs the render thread: its derived normal and roughness,
        // which live in a PNG beside the pack and were being inflated inside the upload.
        detail?.Prewarm(names);

        if (pending.Count > 0)
        {
            // Fire and forget: the renderer's own Take is what makes the result observable, and it forces
            // any value this has not got to yet. Nothing waits on the task itself.
            ParallelOptions options = new() { MaxDegreeOfParallelism = MaxDecodeThreads };
            _ = Task.Run(() => Parallel.ForEach(pending, options, item => _ = item.Value));
        }

        return cache;
    }

    /// <summary>Takes a decoded texture out of the cache, decoding it here if the pool has not yet.</summary>
    /// <param name="name">The bare texture name, without the resource prefix or extension.</param>
    /// <param name="image">The decoded image, or <see langword="null"/> when there is none to be had.</param>
    /// <returns><see langword="true"/> when the cache owned this name, whether or not it decoded.</returns>
    /// <remarks>
    /// Removed as it is taken. A level's decoded chains are a couple of hundred megabytes of managed
    /// memory between them and the caller is about to put the only copy that matters on the GPU, so
    /// holding them after that would be keeping every texture of every backdrop ever shown.
    /// </remarks>
    public bool TryTake(string name, out TextureImage? image)
    {
        if (!_images.TryRemove(name, out Lazy<TextureImage?>? lazy))
        {
            image = null;
            return false;
        }

        image = lazy.Value;
        return true;
    }

    private static TextureImage? Decode(
        string name, ReadOnlyMemory<byte> bytes, float cutoff, HdTexturePack? upscaled)
    {
        try
        {
            // An upscaled replacement, if the player built one, and the game's own texture otherwise. The
            // substitution is HERE and nowhere else -- everything below this line is the correction
            // sequence, and it has to be the same sequence for both or the two would be subtly different
            // renderers. A replacement that will not decode has already warned and fallen through.
            TextureImage image =
                upscaled is not null && upscaled.TryRead(name, out Bgra32[] pixels, out int width, out int height)
                    ? TextureImage.FromPixels(width, height, pixels)
                    : TexFile.Load(bytes.Span);

            // The same sequence WorldRenderer.GetTexture ran inline, in the same order and for the same
            // reasons -- classify the cut-out from level zero before the alpha is touched, widen it,
            // then build or rebuild the chain and correct its coverage. See that method for why each
            // step is here; this is a move, not a change.
            bool cutout = AlphaCoverage.IsCutout(image.GetLevel(0));

            image.WidenCutoutAlpha(cutoff);
            image = cutout ? image.RebuildMipChain() : image.WithMipChain();
            image.PreserveAlphaCoverage(cutoff);
            return image;
        }
        catch (Exception ex) when (ex is TextureFormatException or ArgumentException)
        {
            // Reported where it matters, by the renderer, when it finds nothing here and decodes the
            // same bytes itself. Swallowing it on the worker keeps the pool out of the logger.
            return null;
        }
    }
}
