using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Graphics;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.Sprites;
using OpenIGI.Rendering;

namespace OpenIGI.Game.Igi2.World;

/// <summary>Cached retail screen and game sprites, with their fonts, owned by one presenter.</summary>
public sealed class Igi2SpriteResources : IDisposable
{
    private readonly IGraphicsDevice _device;
    private readonly IOpenIgiLogger _logger;
    private readonly Dictionary<string, IresEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SpriteResource?> _sprites = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Loads the archives used by status screens and game sprites.</summary>
    /// <param name="device">The upload device.</param>
    /// <param name="files">The title's installed data.</param>
    /// <param name="logger">Where invalid assets are reported.</param>
    public Igi2SpriteResources(IGraphicsDevice device, VirtualFileSystem files, IOpenIgiLogger logger)
    {
        _device = device;
        _logger = logger;
        // statusscreen.c StatusScreen_CreateHandler; scamera.c SCamera_Open; fontmanager.c FontManager_Open.
        string[] archives = ["LOCAL:screens/game/status/status.res", "LOCAL:common/sprites/sprites.res", "LOCAL:common/fonts/fonts_med.res", "LOCAL:weapons/weapons.res"];
        foreach (string path in archives)
        {
            if (!files.Exists(path)) { continue; }
            foreach (IresEntry entry in IresArchive.Read(files.ReadAllBytes(path)).Entries)
            {
                _entries.TryAdd(Path.GetFileName(entry.Name.Replace('\\', '/')), entry);
            }
        }
    }

    /// <summary>Gets a sprite by resource name, uploading it only on its first use.</summary>
    /// <param name="name">The resource name, with an optional alias or path.</param>
    /// <returns>The uploaded sprite, or null when the archive does not contain it.</returns>
    public SpriteResource? Get(string name)
    {
        string key = Path.GetFileName(name[(name.IndexOf(':') + 1)..].Replace('\\', '/'));
        if (_sprites.TryGetValue(key, out SpriteResource? cached)) { return cached; }
        SpriteResource? sprite = null;
        if (_entries.TryGetValue(key, out IresEntry? entry))
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

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (SpriteResource? sprite in _sprites.Values) { sprite?.Dispose(); }
        _sprites.Clear();
        _entries.Clear();
    }
}
