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
/// <b>JPEG is the gap.</b> 76 <c>.jpg</c> files ship in the mission texture directories against 95
/// <c>.tga</c> and 24 <c>.tex</c>, and the terrain is textured entirely from the first kind. Those
/// still resolve to <see cref="TextureHandle.None"/> and the ground draws with its baked lighting and
/// no surface — visibly wrong, and honestly wrong rather than quietly so.
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
    private readonly IIgiLogger _logger;
    private readonly Dictionary<string, TextureHandle> _byName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates a loader.</summary>
    /// <param name="device">The device to upload through.</param>
    /// <param name="resources">The registry textures are read from.</param>
    /// <param name="logger">Where load diagnostics go.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public Igi2TextureLoader(IGraphicsDevice device, ResourceRegistry resources, IIgiLogger? logger = null)
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
            else if (TgaImage.IsTga(bytes.Span))
            {
                image = TgaImage.Read(bytes.Span);
            }

            if (image is null)
            {
                // A JPEG, on the evidence of the corpus. Warned once per name, because the dictionary
                // caches the miss.
                _logger.Warning(LogCategory, $"Texture '{resource}' is in no format read here; it draws untextured.");
                return TextureHandle.None;
            }

            return _device.CreateTexture(
                new TextureDescription(image.Width, image.Height),
                image.Pixels);
        }
        catch (Exception ex) when (ex is PackFormatException or TextureFormatException)
        {
            _logger.Warning(LogCategory, $"Texture '{resource}' did not decode: {ex.Message}");
            return TextureHandle.None;
        }
    }
}
