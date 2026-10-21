using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Formats.Textures;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi1.World;

/// <summary>The level's ground tiles on the device, with the surface-detail maps derived from them.</summary>
/// <remarks>
/// The chunk cache names tiles by index rather than by handle, so the whole set can be destroyed and put
/// back up from another atlas without the geometry noticing. See <see cref="Reload"/>.
/// </remarks>
internal sealed class TerrainTileTextures
{
    private const string LogCategory = "Terrain";

    private readonly IGraphicsDevice _device;
    private readonly IOpenIgiLogger _logger;
    private readonly MaterialDetailPack? _detailMaps;
    private TextureHandle[] _tiles;

    /// <summary>Initializes a new instance of the <see cref="TerrainTileTextures"/> class and uploads the tiles.</summary>
    /// <param name="device">The device to upload to.</param>
    /// <param name="logger">Where to report upload problems.</param>
    /// <param name="detailMaps">The derived surface-detail maps, or <see langword="null"/> for none.</param>
    /// <param name="tiles">The atlas to draw the ground from, or <see langword="null"/> for none.</param>
    /// <param name="derivedDetail">Detail maps already derived for the tiles, or <see langword="null"/>.</param>
    public TerrainTileTextures(
        IGraphicsDevice device,
        IOpenIgiLogger logger,
        MaterialDetailPack? detailMaps,
        TexAtlasFile? tiles,
        IReadOnlyList<Bgra32[]>? derivedDetail)
    {
        _device = device;
        _logger = logger;
        _detailMaps = detailMaps;
        _tiles = UploadTiles(tiles, derivedDetail);
    }

    /// <summary>Gets how many ground tiles were uploaded from the level's atlas.</summary>
    public int Count => _tiles.Length;

    /// <summary>Gets a tile's texture, or <see cref="TextureHandle.None"/> for an index outside the atlas.</summary>
    public TextureHandle this[int index] =>
        (uint)index < (uint)_tiles.Length ? _tiles[index] : TextureHandle.None;

    /// <summary>Uploads the level's ground tiles once.</summary>
    /// <remarks>
    /// Eighteen 128×128 tiles is a third of a megabyte, and a chunk can name any of them, so there is
    /// nothing to gain from doing this on demand. They repeat across the ground, hence the wrapping.
    /// </remarks>
    private TextureHandle[] UploadTiles(TexAtlasFile? tiles, IReadOnlyList<Bgra32[]>? derivedDetail)
    {
        if (tiles is not { } atlas || atlas.Tiles.Count == 0)
        {
            _logger.Warning(LogCategory, "The level ships no ground texture atlas; the terrain draws untextured.");
            return [];
        }

        TextureHandle[] handles = new TextureHandle[atlas.Tiles.Count];
        int uploadedWidth = atlas.TileWidth;
        int uploadedHeight = atlas.TileHeight;
        for (int i = 0; i < handles.Length; i++)
        {
            TextureImage tile = atlas.Tiles[i].WithMipChain();
            uploadedWidth = tile.Width;
            uploadedHeight = tile.Height;
            try
            {
                handles[i] = _device.CreateTexture(
                    new TextureDescription(
                        tile.Width,
                        tile.Height,
                        tile.MipLevels,
                        TextureFilter.Linear,
                        TextureAddressMode.Repeat),
                    tile.Pixels);

                // A caller that has already done the arithmetic off this thread hands the result in; the
                // menu's backdrop loader does, because deriving a tile measured at tens of milliseconds
                // and a level's worth is most of a second on the thread holding the menu up.
                if (derivedDetail is not null && i < derivedDetail.Count && derivedDetail[i].Length > 0)
                {
                    _detailMaps?.AttachPacked(handles[i], derivedDetail[i], tile.Width, tile.Height);
                }
                else
                {
                    // Derived in process from whatever the tile turned out to be. A ground tile has no
                    // name to look a map up by -- it is an index into an atlas -- and it does not need
                    // one: when the overrides folder replaces the atlas, this derives from the
                    // replacement, which is the picture actually on the ground.
                    _detailMaps?.AttachDerived(
                        handles[i], tile.GetLevel(0), tile.Width, tile.Height);
                }
            }
            catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException)
            {
                _logger.Warning(LogCategory, $"Ground tile {i} did not upload: {ex.Message}");
            }
        }

        // The UPLOADED size, not the atlas's declared one. Those are the same number until the overrides
        // folder replaces the atlas, and then reporting the header's is actively misleading -- it would
        // say 128x128 for ground that went up at 1024.
        _logger.Information(
            LogCategory,
            $"Ground tiles: {handles.Length} of {uploadedWidth}x{uploadedHeight}"
                + (uploadedWidth != atlas.TileWidth ? $" (atlas declares {atlas.TileWidth}x{atlas.TileHeight})." : "."));
        return handles;
    }

    /// <summary>Destroys the tiles and uploads the new atlas's in their place.</summary>
    /// <param name="tiles">The atlas to draw the ground from, or <see langword="null"/> for none.</param>
    /// <remarks>See <see cref="TerrainRenderer.ReloadTiles"/> for why the detail maps are derived again.</remarks>
    public void Reload(TexAtlasFile? tiles)
    {
        foreach (TextureHandle tile in _tiles)
        {
            if (tile.IsValid)
            {
                _detailMaps?.Release(tile);
                _device.DestroyTexture(tile);
            }
        }

        _tiles = UploadTiles(tiles, derivedDetail: null);
    }

    /// <summary>Gives every tile and its detail maps back.</summary>
    public void Destroy()
    {
        foreach (TextureHandle tile in _tiles)
        {
            _detailMaps?.Release(tile);
            _device.DestroyTexture(tile);
        }
    }
}
