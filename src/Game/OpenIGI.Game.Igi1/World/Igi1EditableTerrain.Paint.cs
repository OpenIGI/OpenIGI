using System.Globalization;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>
/// Painting ground materials: each of the level's <c>TextureModifier</c> tasks lays a material over its square wherever its
/// <c>terrain.bit</c> mask is set (<see cref="TerrainTileLayer"/>), so painting sets and clears those bits, and the masks are
/// saved as the level's <c>terrain.bit</c>.
/// </summary>
public sealed partial class Igi1EditableTerrain
{
    /// <summary>Where the masks are saved, relative to the mission folder.</summary>
    public const string MaskMissionPath = "terrain/terrain.bit";

    private int _savedMaskRevision;

    /// <summary>Raised when paint changed: the ground rectangle it covers, in engine units (minimum X, minimum Y, maximum X, maximum Y).</summary>
    public event Action<double, double, double, double>? Painted;

    /// <inheritdoc/>
    /// <remarks>One per <c>TextureModifier</c>, smallest square first, as the renderer consults them.</remarks>
    public IReadOnlyList<TerrainPaintLayer> PaintLayers => field ??= [.. _surface.Layers.Select((layer, index) =>
    {
        double metres = 1.0 / WorldScene.UnitsPerMetre;
        double side = 2.0 * layer.Extent * metres;
        return new TerrainPaintLayer(
            index,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Material {layer.MaterialId}  •  {side:0} m square at {layer.OriginX * metres:0}, {layer.OriginY * metres:0}  •  {side / layer.Size:0.#} m cells"),
            layer.MaterialId,
            layer.OriginX * metres,
            layer.OriginY * metres,
            (layer.OriginX * metres) + side,
            (layer.OriginY * metres) + side,
            side / layer.Size);
    })];

    /// <inheritdoc/>
    public void Paint(int layer, double xMetres, double yMetres, double radiusMetres, bool lay, ICollection<TerrainPaintCell> changed)
    {
        ArgumentNullException.ThrowIfNull(changed);
        if ((uint)layer >= (uint)_surface.Layers.Length)
        {
            return;
        }

        TerrainTileLayer tile = _surface.Layers[layer];
        double units = WorldScene.UnitsPerMetre;
        double x = xMetres * units, y = yMetres * units, radius = radiusMetres * units;
        (int left, int top) = Cell(tile, x - radius, y - radius);
        (int right, int bottom) = Cell(tile, x + radius, y + radius);
        int count = changed.Count;
        for (int row = Math.Max(0, top); row <= Math.Min(tile.Size - 1, bottom); row++)
        {
            for (int column = Math.Max(0, left); column <= Math.Min(tile.Size - 1, right); column++)
            {
                (double cx, double cy) = CellCentre(tile, column, row);
                bool before = tile.Mask.IsSet(column, row);
                if (((cx - x) * (cx - x)) + ((cy - y) * (cy - y)) <= radius * radius
                    && _surface.Terrain.Visibility.SetCell(tile.BitmapId, column, row, lay))
                {
                    changed.Add(new TerrainPaintCell(layer, column, row, before ? 1 : 0, lay ? 1 : 0));
                }
            }
        }

        if (changed.Count > count)
        {
            Painted?.Invoke(x - radius, y - radius, x + radius, y + radius);
        }
    }

    /// <inheritdoc/>
    public void SetPaint(IEnumerable<TerrainPaintCell> cells, bool after)
    {
        ArgumentNullException.ThrowIfNull(cells);
        (double minX, double minY, double maxX, double maxY) = (double.MaxValue, double.MaxValue, double.MinValue, double.MinValue);
        double cell = 0.0;
        foreach (TerrainPaintCell paint in cells)
        {
            if ((uint)paint.Grid >= (uint)_surface.Layers.Length)
            {
                continue;
            }

            TerrainTileLayer tile = _surface.Layers[paint.Grid];
            if (_surface.Terrain.Visibility.SetCell(tile.BitmapId, paint.X, paint.Y, (after ? paint.After : paint.Before) != 0))
            {
                (double cx, double cy) = CellCentre(tile, paint.X, paint.Y);
                (minX, minY, maxX, maxY) = (Math.Min(minX, cx), Math.Min(minY, cy), Math.Max(maxX, cx), Math.Max(maxY, cy));
                cell = Math.Max(cell, 1.0 / tile.Scale);
            }
        }

        if (minX <= maxX)
        {
            Painted?.Invoke(minX - cell, minY - cell, maxX + cell, maxY + cell);
        }
    }

    // The masks, when anything was painted since the last save.
    private (string MissionPath, byte[] Bytes)? ChangedMasks()
    {
        TerrainVisibility masks = _surface.Terrain.Visibility;
        return masks.Revision == _savedMaskRevision ? null : (MaskMissionPath, masks.Write());
    }

    private void MarkMasksSaved() => _savedMaskRevision = _surface.Terrain.Visibility.Revision;

    // The cell a world point falls in, by the renderer's own mapping (TerrainTileLayer.Covers).
    private static (int Column, int Row) Cell(TerrainTileLayer tile, double x, double y) =>
        ((int)Math.Floor((x - tile.OriginX) * tile.Scale), (int)Math.Floor((y - tile.OriginY) * tile.Scale));

    private static (double X, double Y) CellCentre(TerrainTileLayer tile, int column, int row) =>
        (tile.OriginX + ((column + 0.5) / tile.Scale), tile.OriginY + ((row + 0.5) / tile.Scale));
}
