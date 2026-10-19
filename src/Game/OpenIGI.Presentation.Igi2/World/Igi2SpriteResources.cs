using OpenIGI.Core.Diagnostics;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.Sprites;
using OpenIGI.Rendering;
using OpenIGI.Game.Igi2;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Presentation.Igi2.World;

/// <summary>Cached retail screen and game sprites, with their fonts, owned by one presenter.</summary>
public sealed class Igi2SpriteResources : IDisposable
{
    private readonly IGraphicsDevice _device;
    private readonly IOpenIgiLogger _logger;
    private readonly Dictionary<string, IresEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IresEntry> _paths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SpriteResource?> _sprites = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextureHandle> _textures = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Loads the archives used by status screens and game sprites.</summary>
    /// <param name="device">The upload device.</param>
    /// <param name="files">The title's installed data.</param>
    /// <param name="logger">Where invalid assets are reported.</param>
    public Igi2SpriteResources(IGraphicsDevice device, VirtualFileSystem files, IOpenIgiLogger logger)
    {
        _device = device;
        _logger = logger;
        // statusscreen.c StatusScreen_CreateHandler; scamera.c SCamera_Open; fontmanager.c FontManager_Open.
        string[] archives = ["LOCAL:screens/game/status/status.res", "LOCAL:common/sprites/sprites.res", "LOCAL:common/fonts/fonts_med.res", "LOCAL:weapons/weapons.res", "LOCAL:computer/computer.res", "LOCAL:common/textures/textures.res"];
        foreach (string path in archives)
        {
            if (!files.Exists(path)) { continue; }
            foreach (IresEntry entry in IresArchive.Read(files.ReadAllBytes(path)).Entries)
            {
                _entries.TryAdd(Path.GetFileName(entry.Name.Replace('\\', '/')), entry);
                _paths.TryAdd(entry.Name.Replace('\\', '/'), entry);
            }
        }
    }

    /// <summary>Gets a sprite by resource name, uploading it only on its first use.</summary>
    /// <param name="name">
    /// The resource name. A bare file name finds the first archive's file of that name; an alias-qualified one
    /// (<c>WEAPONS:binoculars.spr</c>, <c>LOCAL:weapons/binoculars.spr</c>) finds only that file, as <c>QSprite_Load</c>
    /// loads it, because the common sprites and the weapons' icons share names.
    /// </param>
    /// <returns>The uploaded sprite, or null when the archive does not contain it.</returns>
    public SpriteResource? Get(string name)
    {
        int colon = name.IndexOf(':');
        string key = colon < 0 ? Path.GetFileName(name.Replace('\\', '/')) : Qualified(name, colon);
        if (_sprites.TryGetValue(key, out SpriteResource? cached)) { return cached; }
        SpriteResource? sprite = null;
        if ((colon < 0 ? _entries : _paths).TryGetValue(key, out IresEntry? entry))
        {
            try { sprite = SpriteResource.Upload(_device, SpriteLoader.Load(entry.Data, entry.Name)); }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or EngineFatalException)
            {
                _logger.Warning("igi2.hud", $"Could not load {name}: {ex.Message}");
            }
        }
        _sprites[key] = sprite;
        return sprite;
    }

    /// <summary>Gets a Targa or JPEG picture by its full resource name, uploading it only on its first use.</summary>
    /// <param name="name">The name, such as <c>LOCAL:common/textures/linefilter1.tga</c> or <c>computer/blueprint.jpg</c>.</param>
    /// <returns>The texture, or <see cref="TextureHandle.None"/> when the archives do not hold it.</returns>
    public TextureHandle GetTexture(string name)
    {
        if (_textures.TryGetValue(name, out TextureHandle cached)) { return cached; }
        TextureHandle texture = TextureHandle.None;
        if (_paths.TryGetValue(name, out IresEntry? entry)
            && (Formats.Textures.TgaImage.IsTga(entry.Data.Span) || Formats.Textures.JpegImage.IsJpeg(entry.Data.Span)))
        {
            try
            {
                Formats.Textures.TextureImage image = Formats.Textures.JpegImage.IsJpeg(entry.Data.Span)
                    ? Formats.Textures.JpegImage.Read(entry.Data.Span)
                    : Formats.Textures.TgaImage.Read(entry.Data.Span);
                texture = _device.CreateTexture(
                    new TextureDescription(image.Width, image.Height, 1, TextureFilter.Linear, TextureAddressMode.Clamp), image.Pixels);
            }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or EngineFatalException)
            {
                _logger.Warning("igi2.hud", $"Could not load {name}: {ex.Message}");
            }
        }
        _textures[name] = texture;
        return texture;
    }

    // An alias names one of the game's folders: WEAPONS:x is LOCAL:weapons/x.
    private static string Qualified(string name, int colon)
    {
        string alias = name[..colon], rest = name[(colon + 1)..].Replace('\\', '/');
        return alias.Equals("LOCAL", StringComparison.OrdinalIgnoreCase)
            ? "LOCAL:" + rest
            : "LOCAL:" + alias.ToLowerInvariant() + "/" + rest;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (SpriteResource? sprite in _sprites.Values) { sprite?.Dispose(); }
        _sprites.Clear();
        foreach (TextureHandle texture in _textures.Values)
        {
            if (texture != TextureHandle.None) { _device.DestroyTexture(texture); }
        }
        _textures.Clear();
        _entries.Clear();
        _paths.Clear();
    }
}
