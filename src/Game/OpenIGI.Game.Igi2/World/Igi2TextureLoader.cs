using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Engine.Resources;
using OpenIGI.Formats;
using OpenIGI.Formats.Textures;
using OpenIGI.Formats.World;
using OpenIGI.Rendering;

namespace OpenIGI.Game.Igi2.World;

/// <summary>
/// Turns an I.G.I. 2 texture name into a handle on the device, once per name.
/// </summary>
/// <remarks>
/// <para>
/// Several materials share a texture — a level's ground is a handful of them over a great many tiles —
/// so decoding and uploading each on demand, once, is the whole of this type.
/// </para>
/// <para>
/// <b>A name is used exactly as written.</b> A terrain material carries a full alias-qualified path
/// with its own extension, <c>MISSION:textures/k_grass.jpg</c>, and that resolves through the file
/// system to a loose file beside the level's archive. Only a bare name — which is how a model's
/// materials name theirs — is expanded into <c>LOCAL:textures/&lt;name&gt;.tex</c>.
/// </para>
/// <para>
/// <b>Three formats, because the game does not decode two of them itself.</b>
/// <c>Direct3DTexture_DownloadFromFile</c> (<c>igi2ed 0x662FE0</c>) hands the raw bytes to
/// <c>D3DXCreateTextureFromFileInMemoryEx</c>, which sniffs and decodes whatever they are — so the
/// sequel names <c>.tex</c>, <c>.tga</c> and <c>.jpg</c> interchangeably and the engine never notices.
/// This sniffs the same way: a <c>LOOP</c> magic is the engine's own format, otherwise a TGA header
/// shape, otherwise nothing.
/// </para>
/// <para>
/// All three are read here, so the ground, the sky and the props all texture. The sniffing order
/// matters: <c>LOOP</c> and JPEG both have a magic number and TGA has none, so TGA is tried last and
/// only its header <em>shape</em> can be checked.
/// </para>
/// <para>
/// A name the level does not ship resolves to <see cref="TextureHandle.None"/> rather than throwing.
/// That is the original's own tolerance: a material whose texture is absent draws untextured, and the
/// corpus has materials painted with textures their author later removed.
/// </para>
/// </remarks>
public sealed class Igi2TextureLoader
{
    /// <summary>The category load diagnostics are logged under.</summary>
    public const string LogCategory = "igi2.textures";

    private readonly IGraphicsDevice _device;
    private readonly ResourceRegistry _resources;
    private readonly IOpenIgiLogger _logger;
    private readonly Dictionary<string, TextureHandle> _byName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates a loader.</summary>
    /// <param name="device">The device to upload through.</param>
    /// <param name="resources">The registry textures are read from.</param>
    /// <param name="logger">Where load diagnostics go.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public Igi2TextureLoader(IGraphicsDevice device, ResourceRegistry resources, IOpenIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(resources);

        _device = device;
        _resources = resources;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Gets how many distinct textures were uploaded.</summary>
    public int Count => _byName.Values.Count(static h => h != TextureHandle.None);

    /// <summary>Resolves a texture by its bare name, uploading it the first time.</summary>
    /// <param name="name">The bare name, as a terrain material carries it.</param>
    /// <returns>The handle, or <see cref="TextureHandle.None"/> when the level does not ship it.</returns>
    public TextureHandle Resolve(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return TextureHandle.None;
        }

        if (_byName.TryGetValue(name, out TextureHandle existing))
        {
            return existing;
        }

        TextureHandle handle = Upload(name);
        _byName[name] = handle;
        return handle;
    }

    /// <inheritdoc/>
    public override string ToString() => $"I.G.I. 2 textures, {Count} uploaded";

    /// <summary>Turns a material's texture name into the resource name it is registered under.</summary>
    /// <param name="name">The name as the material carries it.</param>
    /// <returns>The resource name.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null or empty.</exception>
    /// <remarks>
    /// Already-qualified names pass through untouched, because that is what a terrain material carries
    /// and the file it names really is at that path.
    /// </remarks>
    public static string ResourceNameFor(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        return name.Contains(':', StringComparison.Ordinal)
            ? name
            : Igi2ModelTable.TextureResourceName(name);
    }

    private TextureHandle Upload(string name)
    {
        string resource = ResourceNameFor(name);
        if (!_resources.TryLoad(resource, out ReadOnlyMemory<byte> bytes))
        {
            _logger.Warning(LogCategory, $"Texture '{resource}' is absent; what names it draws untextured.");
            return TextureHandle.None;
        }

        try
        {
            // The same order the game's own sniffing amounts to: the engine's format first, because it
            // has a magic number, then TGA, whose header is a shape rather than an identity.
            TextureImage? image = null;
            if (TexFile.IsLoopAsset(bytes.Span))
            {
                image = TexFile.Load(bytes.Span);
            }
            else if (JpegImage.IsJpeg(bytes.Span))
            {
                image = JpegImage.Read(bytes.Span);
            }
            else if (TgaImage.IsTga(bytes.Span))
            {
                image = TgaImage.Read(bytes.Span);
            }

            // Built here rather than shipped: the sequel's ground materials are JPEG and TGA, neither of
            // which carries a chain, so every one of them arrived with a single level. Project I.G.I.'s
            // terrain, sky and world paths all call this for the same reason.
            image = image?.WithMipChain();

            if (image is null)
            {
                // Warned once per name, because the dictionary caches the miss.
                _logger.Warning(LogCategory, $"Texture '{resource}' is in no format read here; it draws untextured.");
                return TextureHandle.None;
            }

            // Repeating, with whatever mip chain the file carries. The two-argument constructor is the
            // one 2D drawing uses -- a single level, clamped -- and taking it here was wrong twice over.
            //
            // Clamping is the visible half: a terrain material tiles across the ground, so its U and V
            // run to the hundreds, and clamping smears the edge texel of every material into long
            // streaks down the hillside. TerrainRender_SetTextureState (igi2ed 0x66B810) sets
            // D3DTSS_ADDRESSU and ADDRESSV to D3DTADDRESS_WRAP on stage 0 and stage 1 -- the material
            // and the detail map -- and only stage 2 clamps.
            //
            // The single mip level is the quieter half: ground seen at a distance is minified hard, and
            // one level means it aliases into a shimmer instead of resolving. The chain is built above,
            // because these files ship without one.
            //
            // WithMipChain rather than RebuildMipChain, which is the cut-out case: a tree billboard's
            // transparent gaps have to be filtered with the alpha weighted or the foliage darkens with
            // distance. Nothing here knows which textures are cut-outs yet, and the ground -- which is
            // what this was written for -- is opaque.
            return _device.CreateTexture(
                new TextureDescription(
                    image.Width,
                    image.Height,
                    image.MipLevels,
                    TextureFilter.Linear,
                    TextureAddressMode.Repeat),
                image.Pixels);
        }
        catch (Exception ex) when (ex is PackFormatException or TextureFormatException)
        {
            _logger.Warning(LogCategory, $"Texture '{resource}' did not decode: {ex.Message}");
            return TextureHandle.None;
        }
    }
}
