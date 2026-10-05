using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Host;

/// <summary>
/// The terrain's height raster, for the global illumination's untraced march: built off the frame loop
/// at load, uploaded when it is ready, and handed to whichever surface-cache lighting the level has.
/// </summary>
/// <remarks>
/// The march finds the level's placements in its distance field and nothing else; the terrain is not one
/// of them. See <see cref="TerrainHeightRaster"/>.
/// </remarks>
internal sealed partial class GameHost
{
    /// <summary>How far past the built-up area the raster reaches, in metres.</summary>
    private const double TerrainRasterMargin = 32.0;

    private Task<ShelterHeightField>? _terrainRasterTask;
    private CancellationTokenSource? _terrainRasterCancel;
    private long _terrainRasterStarted;
    private ShelterHeightField? _terrainRaster;
    private TextureHandle _terrainHeights;
    private float _terrainHeightScale = 1f;
    private SurfaceCacheLighting? _terrainHeightsGivenTo;

    /// <summary>Starts sampling the terrain over the level's built-up area, off the frame loop.</summary>
    /// <remarks>Over the roof raster's rectangle and a margin: where the buildings are is where the bounce off the ground matters.</remarks>
    private void StartTerrainRaster()
    {
        ReleaseTerrainRaster();
        if (_playerTerrain is not { } terrain || _shelterField.IsEmpty)
        {
            return;
        }

        ShelterHeightField roofs = _shelterField;
        double minX = roofs.OriginX - TerrainRasterMargin;
        double minY = roofs.OriginY - TerrainRasterMargin;
        double maxX = roofs.OriginX + (roofs.Width * roofs.CellMetres) + TerrainRasterMargin;
        double maxY = roofs.OriginY + (roofs.Depth * roofs.CellMetres) + TerrainRasterMargin;
        double lowZ = roofs.LowestZ;
        double highZ = roofs.HighestZ;

        CancellationTokenSource cancel = new();
        _terrainRasterCancel = cancel;
        _terrainRasterStarted = _loop.TimeSource.Milliseconds;
        _terrainRasterTask = Task.Run(
            () => TerrainHeightRaster.Build(
                terrain, minX, minY, maxX, maxY, lowZ, highZ, TerrainHeightRaster.DefaultCellMetres, cancel.Token),
            cancel.Token);
    }

    /// <summary>Uploads the raster once it is built, and keeps the current surface-cache lighting holding it.</summary>
    private void UpdateTerrainRaster()
    {
        if (_terrainRasterTask is { IsCompleted: true } task)
        {
            _terrainRasterTask = null;
            if (task.IsCompletedSuccessfully && !task.Result.IsEmpty)
            {
                ShelterHeightField raster = task.Result;
                Bgra32[] texels = raster.Encode(out float scale);
                _terrainHeights = _device.CreateTexture(
                    new TextureDescription(
                        raster.Width,
                        raster.Depth,
                        MipLevels: 1,
                        TextureFilter.Point,
                        TextureAddressMode.Clamp) { HoldsData = true },
                    texels);
                _terrainRaster = raster;
                _terrainHeightScale = scale;
                _terrainHeightsGivenTo = null;
                _logger.Information(
                    "Terrain",
                    FormattableString.Invariant(
                        $"height raster {raster.Width}x{raster.Depth} at {raster.CellMetres:0.0} m, z {raster.LowestZ:0.0}..{raster.HighestZ:0.0}, built in {_loop.TimeSource.Milliseconds - _terrainRasterStarted} ms"));
            }
        }

        if (_cardLighting is { } cards && !ReferenceEquals(cards, _terrainHeightsGivenTo))
        {
            cards.SetTerrainHeights(_terrainHeights, _terrainRaster, _terrainHeightScale);
            _terrainHeightsGivenTo = cards;
        }
    }

    /// <summary>Stops a raster still being built and lets the uploaded one go.</summary>
    private void ReleaseTerrainRaster()
    {
        _terrainRasterCancel?.Cancel();
        _terrainRasterCancel?.Dispose();
        _terrainRasterCancel = null;
        _terrainRasterTask = null;
        _terrainRaster = null;
        _cardLighting?.SetTerrainHeights(TextureHandle.None, null, 1f);
        _terrainHeightsGivenTo = null;
        if (_terrainHeights.IsValid)
        {
            _device.DestroyTexture(_terrainHeights);
            _terrainHeights = TextureHandle.None;
        }
    }
}
