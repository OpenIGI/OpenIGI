using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Resources;
using OpenIGI.Formats.Textures;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering;

/// <summary>
/// Model textures by name, each read from its <c>.tex</c> and uploaded on first use, and kept until the cache is cleared.
/// </summary>
/// <remarks>
/// <para>
/// The cache every model renderer wrote for itself: a name is looked up under each of its resource names in turn, the first
/// that reads is decoded and uploaded with linear filtering, and a name that loads nothing is remembered as
/// <see cref="TextureHandle.None"/>, so a missing texture is reported once rather than every frame.
/// </para>
/// <para>
/// The renderers differ only in how the texture wraps, where the file may be, and what they do with a texture once it is
/// up -- the soldiers derive a detail map from it -- which are the options here.
/// </para>
/// </remarks>
public sealed class TexTextureCache : IDisposable
{
    private readonly IGraphicsDevice _device;
    private readonly ResourceRegistry _resources;
    private readonly IOpenIgiLogger _logger;
    private readonly string _logCategory;
    private readonly TextureAddressMode _addressMode;
    private readonly Func<string, IEnumerable<string>> _resourceNames;
    private readonly Dictionary<string, TextureHandle> _textures = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Initializes a new instance of the <see cref="TexTextureCache"/> class.</summary>
    /// <param name="device">The device the textures are uploaded to.</param>
    /// <param name="resources">Where their files are read from.</param>
    /// <param name="logger">Where a texture that does not load is reported.</param>
    /// <param name="logCategory">The category it is reported under.</param>
    /// <param name="addressMode">How the textures wrap.</param>
    /// <param name="resourceNames">
    /// The resource names a texture name is tried under, in order; the model packs' own (<see cref="LoadedPack.TextureResourceName"/>)
    /// when none is given.
    /// </param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public TexTextureCache(
        IGraphicsDevice device,
        ResourceRegistry resources,
        IOpenIgiLogger logger,
        string logCategory,
        TextureAddressMode addressMode = TextureAddressMode.Repeat,
        Func<string, IEnumerable<string>>? resourceNames = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(logger);
        _device = device;
        _resources = resources;
        _logger = logger;
        _logCategory = logCategory ?? string.Empty;
        _addressMode = addressMode;
        _resourceNames = resourceNames ?? (static name => [LoadedPack.TextureResourceName(name)]);
    }

    /// <summary>Gets or sets what is done with a texture the moment it is uploaded: its name, its handle and its decoded image.</summary>
    public Action<string, TextureHandle, TextureImage>? Loaded { get; set; }

    /// <summary>Gets or sets a value indicating whether a name none of whose files is registered is reported as well.</summary>
    public bool ReportMissing { get; set; }

    /// <summary>Gets a texture, loading it the first time it is asked for.</summary>
    /// <param name="name">The texture's name, as a model's material names it.</param>
    /// <returns>The texture, or <see cref="TextureHandle.None"/> when it did not load.</returns>
    public TextureHandle Get(string name)
    {
        if (_textures.TryGetValue(name, out TextureHandle cached))
        {
            return cached;
        }

        TextureHandle handle = TextureHandle.None;
        bool found = false;
        foreach (string resource in _resourceNames(name))
        {
            if (!_resources.TryLoad(resource, out ReadOnlyMemory<byte> bytes))
            {
                continue;
            }

            found = true;
            try
            {
                TextureImage image = TexFile.Load(bytes.Span);
                handle = _device.CreateTexture(
                    new TextureDescription(image.Width, image.Height, image.MipLevels, TextureFilter.Linear, _addressMode),
                    image.Pixels);
                Loaded?.Invoke(name, handle, image);
                break;
            }
            catch (Exception ex) when (ex is TextureFormatException or ArgumentException or GraphicsDeviceException)
            {
                _logger.Warning(_logCategory, $"Texture '{name}' did not load: {ex.Message}");
            }
        }

        if (!found && ReportMissing)
        {
            _logger.Warning(_logCategory, $"Texture '{name}' is not registered.");
        }

        _textures[name] = handle;
        return handle;
    }

    /// <summary>Destroys every texture loaded so far and forgets them all.</summary>
    public void Clear()
    {
        foreach (TextureHandle texture in _textures.Values)
        {
            if (texture.IsValid)
            {
                _device.DestroyTexture(texture);
            }
        }

        _textures.Clear();
    }

    /// <inheritdoc/>
    public void Dispose() => Clear();
}
