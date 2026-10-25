using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Resources;
using OpenIGI.Formats.Textures;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering;

/// <summary>The world's diffuse textures: loaded by name the first time a draw asks, uploaded once and kept.</summary>
/// <remarks>
/// Takes a texture decoded ahead on a worker when <see cref="Decoded"/> holds one, and otherwise reads
/// and prepares it here; both paths upload through the same code and register the same detail maps.
/// </remarks>
internal sealed class WorldTextureCache
{
    private const string LogCategory = "World";

    private readonly IGraphicsDevice _device;
    private readonly ResourceRegistry _resources;
    private readonly IOpenIgiLogger _logger;
    private readonly Dictionary<string, TextureHandle> _textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _missingTextures = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Initializes the cache over the device it uploads to and the registry it reads from.</summary>
    /// <param name="device">The device to upload to.</param>
    /// <param name="resources">Where texture bytes are read from.</param>
    /// <param name="logger">Where to report upload problems.</param>
    internal WorldTextureCache(IGraphicsDevice device, ResourceRegistry resources, IOpenIgiLogger logger)
    {
        _device = device;
        _resources = resources;
        _logger = logger;
    }

    /// <summary>Gets or sets the derived surface-detail maps, or <see langword="null"/> for none.</summary>
    internal MaterialDetailPack? DetailMaps { get; set; }

    /// <summary>Gets or sets the level's textures, decoded ahead on worker threads.</summary>
    internal TextureDecodeCache? Decoded { get; set; }

    /// <summary>Drops every uploaded diffuse texture and the decode cache, so the next draw loads them all again.</summary>
    internal void Reload()
    {
        foreach (TextureHandle texture in _textures.Values)
        {
            if (texture.IsValid)
            {
                DetailMaps?.Release(texture);
                _device.DestroyTexture(texture);
            }
        }

        _textures.Clear();

        // A name that had no texture before may well have one now, and the other way round.
        _missingTextures.Clear();

        Decoded = null;
    }

    /// <summary>Releases every uploaded texture and its detail maps, as disposing the renderer does.</summary>
    internal void Release()
    {
        foreach (TextureHandle texture in _textures.Values)
        {
            DetailMaps?.Release(texture);
            _device.DestroyTexture(texture);
        }

        _textures.Clear();
    }

    /// <summary>Gets a texture by name, loading and uploading it the first time it is asked for.</summary>
    /// <param name="name">The texture's name, as a pack's material slot gives it.</param>
    /// <returns>The texture, or none when it could not be loaded.</returns>
    internal TextureHandle GetTexture(string name)
    {
        if (_textures.TryGetValue(name, out TextureHandle cached))
        {
            return cached;
        }

        // Decoded on a worker while the backdrop was being built, if one was offered. Everything from
        // the file down to the corrected mip chain has then already happened, and what is left here is
        // the upload -- which is the only part that had to be on this thread in the first place.
        if (Decoded is not null && Decoded.TryTakeSurface(name, out DecodedSurface? ready))
        {
            TextureHandle prepared = TextureHandle.None;
            if (ready is not null)
            {
                try
                {
                    prepared = ready.Compressed is { } blocks
                        ? CreateFromCompressed(name, blocks, ready.CompressedDetail)
                        : ready.Image is { } image
                            ? CreateFromDecoded(name, image, ready.Detail)
                            : TextureHandle.None;
                }
                catch (GraphicsDeviceException ex)
                {
                    _logger.Warning(LogCategory, $"Texture '{name}' did not load: {ex.Message}");
                }
            }

            _textures[name] = prepared;
            return prepared;
        }

        TextureHandle handle = TextureHandle.None;
        string resource = LoadedPack.TextureResourceName(name);

        if (_resources.TryLoad(resource, out ReadOnlyMemory<byte> bytes))
        {
            try
            {
                // An upscaled replacement if the player built one, the game's own texture otherwise. Asked
                // only for names the registry HAS -- a name the level never declared is still one the level
                // never declared, and letting a stray PNG conjure one would let a pack add textures the game
                // does not ask for. Normally the decode cache has already done this on a worker; this is the
                // same substitution for whatever the cache never saw, so the two paths cannot disagree.
                SurfaceAsset asset = _device.Capabilities.SupportsBlockCompression
                    ? SurfaceAssetReader.ReadKeepingBlocks(bytes)
                    : SurfaceAssetReader.Read(bytes.Span);

                // A texture pack's stored chain: everything below was done to it when the pack was built.
                if (asset.CompressedDiffuse is { } blocks)
                {
                    handle = CreateFromCompressed(name, blocks, asset.CompressedDetail);
                    _textures[name] = handle;
                    return handle;
                }

                TextureImage image = asset.Diffuse;

                // The wire of a fence, the slats of a grille, the leaves of a canopy: everything drawn as
                // a cut-out quad loses its coverage as the chain averages down, so it thins with distance
                // and flickers as the level crosses over. Restored before the chain is uploaded, against
                // the same cutoff the shaders test. Textures that are opaque, or that are entirely holes,
                // are left alone -- there is no coverage to preserve and scaling would corrupt a channel
                // nothing reads.
                // Most of this game's textures carry a single level, and this backend generates none --
                // so without this the whole world was sampled at full resolution however far away it was,
                // which sparkles on every surface and makes thin things crawl.
                // A cut-out's partly-covered edge texels would be discarded by the half-alpha test, which
                // thins barbed wire visibly against the sky (see AlphaCoverage.WidenCutout). Widened
                // first, so the chain below filters the alpha we actually intend to keep. Real
                // transparencies -- window glass above all -- are classified out and left alone.
                // Classified from level zero before the alpha is touched. A cut-out needs its chain rebuilt
                // even when it shipped with one: a chain imported by the original tool carries the black of
                // the transparent gaps in every level, so the wire goes dark at distance -- the far fences
                // that read pure black under a render scale. Rebuilding from level zero runs the alpha-
                // weighted filter that keeps the wire's colour; an opaque surface keeps whatever chain it
                // has and only generates one if it shipped without.
                bool cutout = AlphaCoverage.IsCutout(image.GetLevel(0));

                image.WidenCutoutAlpha(TextureDecodeCache.AlphaTestCutoff);

                image = cutout ? image.RebuildMipChain() : image.WithMipChain();
                image.PreserveAlphaCoverage(TextureDecodeCache.AlphaTestCutoff);

                handle = CreateFromDecoded(name, image, asset.Detail);
            }
            catch (Exception ex) when (ex is TextureFormatException or ArgumentException or GraphicsDeviceException)
            {
                _logger.Warning(LogCategory, $"Texture '{name}' did not load: {ex.Message}");
            }
        }
        else if (_missingTextures.Add(name))
        {
            _logger.Warning(LogCategory, $"Texture '{resource}' is not registered.");
        }

        _textures[name] = handle;
        return handle;
    }

    /// <summary>Uploads an already-decoded image and registers whatever derives from it.</summary>
    /// <param name="name">The texture's name, which the detail pack keys its maps by.</param>
    /// <param name="image">The decoded chain, from the cache or from this thread.</param>
    /// <param name="detail">
    /// The packed surface map the asset carried, or <see langword="null"/> when it carried none — which
    /// is every texture the game itself ships, and every replacement built without them.
    /// </param>
    /// <returns>The uploaded texture.</returns>
    /// <remarks>
    /// The half of <see cref="GetTexture"/> that has to be on the render thread, split out so the
    /// decoded-ahead path and the decode-here path upload through exactly the same code.
    /// </remarks>
    private TextureHandle CreateFromCompressed(string name, CompressedTextureImage image, CompressedTextureImage? detail)
    {
        TextureImage? proxy = image.Proxy;
        TextureHandle handle = _device.CreateCompressedTexture(
            new TextureDescription(image.Width, image.Height, image.MipLevels, TextureFilter.Linear, TextureAddressMode.Repeat),
            image.Format,
            image.Levels.Span,
            proxy is null ? default : proxy.GetLevel(0),
            proxy?.Width ?? 0,
            proxy?.Height ?? 0);

        // The pack's own map when it carries one, compressed beside the picture; otherwise a map cached
        // by name. Nothing is derived from a compressed picture -- there are no full-size pixels to derive
        // from, and a pack that wants maps ships them.
        if (detail is not null)
        {
            DetailMaps?.AttachCompressed(
                handle, detail.Format, detail.Width, detail.Height, detail.MipLevels, detail.Levels.Span);
        }
        else
        {
            DetailMaps?.Attach(DetailKey(name), handle);
        }

        return handle;
    }

    // The detail pack keys a replaced texture apart from the shipped one, so neither is handed the
    // other's derived map.
    private string DetailKey(string name) =>
        MaterialDetailPack.KeyFor(name, _resources.OverrideStamp(LoadedPack.TextureResourceName(name)));

    private TextureHandle CreateFromDecoded(string name, TextureImage image, TextureImage? detail = null)
    {
        TextureHandle handle = _device.CreateTexture(
            new TextureDescription(image.Width, image.Height, image.MipLevels, TextureFilter.Linear, TextureAddressMode.Repeat),
            image.Pixels);

        // The derived normal, roughness and occlusion this game never shipped. Registered against the
        // texture rather than carried through the draw, so nothing below this line knows the maps exist.
        //
        // The asset's OWN map wins when it carries one: a replacement in the overrides folder holds the
        // map derived from the picture it replaces, at the same resolution and by a better estimator than
        // the one this process can run between frames. Without one, the top level goes to the pack so a
        // texture nothing has derived for can derive its own instead of staying flat forever.
        if (detail is not null)
        {
            DetailMaps?.AttachPacked(handle, detail.GetLevel(0), detail.Width, detail.Height);
        }
        else
        {
            DetailMaps?.Attach(DetailKey(name), handle, image.GetLevel(0), image.Width, image.Height);
        }

        return handle;
    }
}
