using OpenIGI.Core.Graphics;
using OpenIGI.Rendering.Vulkan.Interop;
using OpenIGI.Rendering.Vulkan.Memory;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.RayTracing;

/// <summary>
/// The cut-out shapes a traced ray needs, in one array a ray shader can reach without knowing which
/// texture it hit.
/// </summary>
/// <remarks>
/// <para>
/// A ray query has no per-texel test of its own. Traversal either accepts a triangle or it does not, so
/// a wire fence built as one quad with an alpha channel stops a ray exactly as a board would — which is
/// why cut-out surfaces were kept out of the traceable world entirely and cast no shadow at all. Giving
/// them back their shadow means the shader has to sample the alpha at the point the ray met the
/// triangle, and that means reaching a texture chosen by the hit rather than bound before the dispatch.
/// </para>
/// <para>
/// <b>Why an array of masks rather than the textures themselves.</b> The bindless heaps in
/// <c>Bindless</c> are the general answer to that and are the right home for it eventually. This is the
/// narrow one: a shadow ray does not need colour, or mips, or the source resolution — it needs one bit,
/// is this texel there. So every cut-out texture is reduced once, at load, to a single-channel layer of
/// a fixed-size array, and a hit carries the layer number. One extra sampled image on the pass, no
/// change to how any pass binds its resources, and the whole thing is a few megabytes.
/// </para>
/// <para>
/// The cost of that choice is resolution: a mask is <see cref="Resolution"/> square whatever the source
/// was, so a texture larger than that loses detail its rasterised silhouette keeps. For chain-link at
/// the distances a shadow is read from, that is not visible; for a texture whose cut-out is finer than
/// its own quarter-texel it would be. Point sampling is deliberate — the answer is binary, and filtering
/// it would turn a wire's edge into a half-open texel that is neither there nor not.
/// </para>
/// </remarks>
internal sealed class VulkanOpacityMasks : IDisposable
{
    /// <summary>How many texels square each mask is stored at.</summary>
    public const int Resolution = 256;

    /// <summary>How many distinct cut-out textures the array can hold.</summary>
    /// <remarks>
    /// Mission one uses far fewer than this. A texture that arrives past the limit keeps the old
    /// behaviour — it stays out of the traceable world rather than being traced with the wrong mask —
    /// and the overflow is logged once, because silently casting somebody else's holes would be worse
    /// than casting none.
    /// </remarks>
    public const int MaxLayers = 128;

    // The array. Once its hundred and twenty-eight layers are spent, every cut-out that comes after has no mask -- and a
    // cut-out with no mask is left out of the traced world altogether -- which is why destroyed textures give theirs back.
    private readonly VulkanTextureLayers _layers;

    /// <summary>Initialises a new instance of the <see cref="VulkanOpacityMasks"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the array lives on.</param>
    /// <param name="allocator">The allocator backing the image.</param>
    /// <param name="commands">The pool the one-off upload commands come from.</param>
    public VulkanOpacityMasks(
        Vk vk,
        VulkanDevice device,
        VulkanAllocator allocator,
        VulkanCommands commands)
    {
        // Every layer starts fully opaque, so a record that names a layer nothing was ever written into behaves like solid
        // geometry rather than like a hole.
        ClearColorValue opaque = default;
        opaque.Float32_0 = 1f;
        _layers = new VulkanTextureLayers(vk, device, allocator, commands, Format.R8Unorm, Resolution, MaxLayers, opaque, "opacity masks");
    }

    /// <summary>Gets the array image.</summary>
    public Image Image => _layers.Image;

    /// <summary>Gets the view a ray shader reads the array through.</summary>
    /// <remarks>
    /// Read with <c>texelFetch</c> rather than a filtered sample, so whichever sampler the pass that
    /// binds it happens to own is irrelevant: the value is a decision, and interpolating it would turn a
    /// wire's edge into a half-open texel that is neither there nor not.
    /// </remarks>
    public ImageView View => _layers.View;

    /// <summary>Gets how many masks have been registered.</summary>
    public int Count => _layers.Count;

    /// <summary>Reduces a texture's alpha to a mask layer, or returns the layer it already has.</summary>
    /// <param name="textureId">The texture's identifier, which is what the mapping is keyed by.</param>
    /// <param name="pixels">The texture's whole mip chain; only the largest level is read.</param>
    /// <param name="width">The width of the largest level.</param>
    /// <param name="height">The height of the largest level.</param>
    /// <returns>The layer, or <c>-1</c> when the array is full.</returns>
    public int Register(uint textureId, ReadOnlySpan<Bgra32> pixels, int width, int height)
    {
        if (_layers.TryGetLayer(textureId, out int existing))
        {
            return existing;
        }

        if (width <= 0 || height <= 0 || width * height > pixels.Length || !_layers.TryAllocate(textureId, out int layer))
        {
            return -1;
        }

        _layers.Upload(layer, Reduce(pixels, width, height));
        return layer;
    }

    /// <summary>Finds the layer a texture was reduced to.</summary>
    /// <param name="textureId">The texture's identifier.</param>
    /// <param name="layer">Receives the layer.</param>
    /// <returns><see langword="true"/> when the texture has a mask.</returns>
    public bool TryGetLayer(uint textureId, out int layer) => _layers.TryGetLayer(textureId, out layer);

    /// <summary>Gives a destroyed texture's layer back to the array.</summary>
    /// <param name="textureId">The texture's identifier.</param>
    /// <remarks>
    /// The layer is left as it is rather than cleared: the next registration overwrites it outright, as the
    /// traced albedo does with its own.
    /// </remarks>
    public void Release(uint textureId) => _layers.Release(textureId);

    /// <inheritdoc/>
    public void Dispose() => _layers.Dispose();

    /// <summary>Samples a texture's alpha down to one mask-sized plane of zeroes and ones.</summary>
    /// <param name="pixels">The source pixels, largest level first.</param>
    /// <param name="width">The source width.</param>
    /// <param name="height">The source height.</param>
    /// <returns>The mask, row-major, 255 where the surface is there and 0 where it is cut away.</returns>
    /// <remarks>
    /// Nearest, and deliberately so. The rasteriser's test is <c>alpha &lt; 0.5</c> against a texture
    /// whose cut-out alpha is strictly zero or one, so the answer at any texel is already binary;
    /// averaging neighbours would invent values on the wrong side of that test along every wire, which
    /// reads as a fence that has thickened or thinned rather than as one that has been resampled. Where
    /// the source is larger than the mask this drops texels, which is the resolution cost named on the
    /// class — not a filter that could have avoided it.
    /// </remarks>
    private static byte[] Reduce(ReadOnlySpan<Bgra32> pixels, int width, int height)
    {
        byte[] mask = new byte[Resolution * Resolution];

        for (int y = 0; y < Resolution; y++)
        {
            // The centre of the mask texel, in source texels, so the sampling matches what the shader
            // asks for rather than being offset half a texel from it.
            int sourceY = Math.Min(height - 1, (int)(((y + 0.5f) / Resolution) * height));
            int sourceRow = sourceY * width;
            int maskRow = y * Resolution;

            for (int x = 0; x < Resolution; x++)
            {
                int sourceX = Math.Min(width - 1, (int)(((x + 0.5f) / Resolution) * width));
                mask[maskRow + x] = pixels[sourceRow + sourceX].A < 128 ? (byte)0 : (byte)255;
            }
        }

        return mask;
    }

    // What share of the top level fails the shaders' alpha cut. One percent is enough to mean the texture
    // is masked at all -- a wire fence is mostly hole, a railing mostly not, and neither is a solid pane.
    private const float CutoutTexelFraction = 0.01f;

    /// <summary>Whether a texture's top level is cut out enough to need a mask of its own.</summary>
    /// <param name="pixels">The top level.</param>
    /// <param name="width">Its width.</param>
    /// <param name="height">Its height.</param>
    /// <returns><see langword="true"/> when enough of it fails the alpha cut to be masked at all.</returns>
    /// <remarks>
    /// The shaders' own cut, a half in alpha, counted over the top level: a texture with a few stray
    /// transparent texels is not a cut-out, and tracing it as one would cost a mask lookup on every hit.
    /// </remarks>
    public static bool IsCutout(ReadOnlySpan<Bgra32> pixels, int width, int height)
    {
        int count = width * height;
        if (count <= 0 || count > pixels.Length)
        {
            return false;
        }

        int cut = 0;
        for (int i = 0; i < count; i++)
        {
            if (pixels[i].A < 128)
            {
                cut++;
            }
        }

        return cut / (float)count > CutoutTexelFraction;
    }
}
