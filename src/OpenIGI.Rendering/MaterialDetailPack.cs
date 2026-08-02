using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;

namespace OpenIGI.Rendering;

/// <summary>
/// A directory of derived surface-detail maps, attached to textures as they are loaded.
/// </summary>
/// <remarks>
/// <para>
/// I.G.I. ships a diffuse texture per surface and nothing else. The maps a physically-based shading
/// model needs — the normal, the roughness, whether it is metal — are derived from those textures by
/// <see cref="MaterialMapGenerator"/> and written out as PNGs named after them. This finds them and
/// registers each one against the texture it belongs to.
/// </para>
/// <para>
/// <b>Absent is a supported state, not an error.</b> An installation that has never run the tool has no
/// pack, and it renders exactly as it did before any of this existed. That is what lets the pack be an
/// optional asset rather than a shipping requirement, and it is why every failure below is a warning
/// and a null handle rather than an exception.
/// </para>
/// <para>
/// One instance per device. It owns the textures it uploads, so disposing it releases them; the
/// diffuse textures it attaches them to belong to whoever loaded those.
/// </para>
/// </remarks>
public sealed class MaterialDetailPack : IDisposable
{
    private const string LogCategory = "Material";

    private readonly IGraphicsDevice _device;
    private readonly IIgiLogger _logger;
    private readonly string? _root;
    private readonly Dictionary<string, TextureHandle> _maps = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _missing = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="MaterialDetailPack"/> class.</summary>
    /// <param name="device">The device the maps are uploaded to.</param>
    /// <param name="directory">Where the maps are, or empty or missing for no pack at all.</param>
    /// <param name="logger">Where to report what was found and what would not load.</param>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> is <see langword="null"/>.</exception>
    public MaterialDetailPack(IGraphicsDevice device, string? directory, IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(device);

        _device = device;
        _logger = logger ?? NullLogger.Instance;

        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        string resolved = Path.GetFullPath(directory);

        if (!Directory.Exists(resolved))
        {
            _logger.Warning(LogCategory, $"No detail maps at '{resolved}'; surfaces keep their flat normals.");
            return;
        }

        _root = resolved;
        _logger.Information(LogCategory, $"Detail maps from '{resolved}'.");
    }

    /// <summary>Gets a value indicating whether there is a pack to read from at all.</summary>
    public bool IsAvailable => _root is not null;

    /// <summary>Gets how many maps have been uploaded.</summary>
    public int Count => _maps.Count(entry => entry.Value.IsValid);

    /// <summary>Attaches this texture's detail map, if the pack has one.</summary>
    /// <param name="name">The texture's name, as the pack records it and as the file is named.</param>
    /// <param name="diffuse">The loaded diffuse texture to attach it to.</param>
    /// <remarks>
    /// Safe to call for every texture as it loads. A texture with no map in the pack is remembered as
    /// having none, so a level full of them costs one failed lookup each rather than one per frame.
    /// </remarks>
    public void Attach(string name, TextureHandle diffuse)
    {
        if (_root is null || !diffuse.IsValid || string.IsNullOrEmpty(name))
        {
            return;
        }

        if (!_maps.TryGetValue(name, out TextureHandle map))
        {
            map = Load(name);
            _maps[name] = map;
        }

        if (map.IsValid)
        {
            _device.SetTextureDetail(diffuse, map);
        }
    }

    /// <summary>Derives a detail map from pixels in hand and attaches it.</summary>
    /// <param name="diffuse">The loaded diffuse texture to attach it to.</param>
    /// <param name="pixels">That texture's pixels, row-major.</param>
    /// <param name="width">The texture's width in pixels.</param>
    /// <param name="height">The texture's height in pixels.</param>
    /// <remarks>
    /// <para>
    /// For the textures the pack cannot be asked about, because they have no name to ask with. The
    /// terrain's ground tiles are the case that matters: they arrive as an indexed atlas rather than as
    /// named resources, and they are also the single largest surface on screen, so leaving them flat
    /// while every wall gained detail would be the most visible half of the job left undone.
    /// </para>
    /// <para>
    /// Runs the derivation in process rather than reading a file. That is affordable here precisely
    /// because it is the exception — a level has a handful of ground tiles and a thousand other
    /// textures, and the thousand come from the pack.
    /// </para>
    /// </remarks>
    public void AttachDerived(TextureHandle diffuse, ReadOnlySpan<Bgra32> pixels, int width, int height)
    {
        if (!diffuse.IsValid || width <= 0 || height <= 0 || pixels.Length < width * height)
        {
            return;
        }

        try
        {
            MaterialMapGenerator.Maps maps = MaterialMapGenerator.Generate(pixels, width, height);
            Bgra32[] packed = new Bgra32[width * height];
            MaterialMapGenerator.PackDetail(maps, packed);

            TextureHandle map = _device.CreateTexture(
                new TextureDescription(width, height, 1, TextureFilter.Linear, TextureAddressMode.Repeat),
                packed);

            // Keyed by the diffuse it was made for, so Dispose releases it. The name is synthetic
            // because there is none -- that is the whole reason this overload exists.
            _maps[$"#{diffuse.Id}"] = map;
            _device.SetTextureDetail(diffuse, map);
        }
        catch (Exception error) when (error is ArgumentException or GraphicsDeviceException)
        {
            _logger.Warning(LogCategory, $"Could not derive a detail map for {diffuse}: {error.Message}");
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (TextureHandle map in _maps.Values)
        {
            if (map.IsValid)
            {
                _device.DestroyTexture(map);
            }
        }

        _maps.Clear();
    }

    private TextureHandle Load(string name)
    {
        string path = Path.Combine(_root!, name + ".png");

        if (!File.Exists(path))
        {
            if (_missing.Add(name))
            {
                _logger.Debug(LogCategory, $"No detail map for '{name}'.");
            }

            return TextureHandle.None;
        }

        try
        {
            (Bgra32[] pixels, int width, int height) = PngReader.Read(path);

            // Repeat, because the map tiles exactly as the texture it belongs to does -- and mipmapped,
            // because a normal map minified without one aliases far worse than a colour does. A wrong
            // normal is a wrong lighting direction, and the eye reads that as sparkle rather than as
            // detail.
            return _device.CreateTexture(
                new TextureDescription(width, height, 1, TextureFilter.Linear, TextureAddressMode.Repeat),
                pixels);
        }
        catch (Exception error)
            when (error is InvalidDataException or IOException or ArgumentException or GraphicsDeviceException)
        {
            _logger.Warning(LogCategory, $"Detail map for '{name}' did not load: {error.Message}");
            return TextureHandle.None;
        }
    }
}
