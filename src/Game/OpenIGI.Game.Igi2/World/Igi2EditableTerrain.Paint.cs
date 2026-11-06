using System.Globalization;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.World;

/// <summary>
/// Painting ground materials: every tile carries a material map, one <c>TerrainMaterial</c> id a sample, which the renderer
/// blends between (<see cref="Igi2TerrainLod"/>), so painting writes ids into it and the maps are saved with the heights in
/// <c>heightmaps.res</c>. A layer is one of the level's declared materials, over the whole terrain.
/// </summary>
public sealed partial class Igi2EditableTerrain
{
    /// <summary>Raised when paint changed samples of a tile: the tile's index in the surface, and the changed columns and rows.</summary>
    public event Action<int, int, int, int, int>? Painted;

    /// <inheritdoc/>
    public IReadOnlyList<TerrainPaintLayer> PaintLayers => field ??= Layers();

    /// <inheritdoc/>
    public void Paint(int layer, double xMetres, double yMetres, double radiusMetres, bool lay, ICollection<TerrainPaintCell> changed)
    {
        ArgumentNullException.ThrowIfNull(changed);
        if ((uint)layer >= (uint)PaintLayers.Count)
        {
            return;
        }

        byte material = (byte)PaintLayers[layer].MaterialId;
        foreach (Grid grid in _grids)
        {
            if (grid.Tile.Materials is not { } map)
            {
                continue;
            }

            (double c0, double r0) = grid.ToGrid(xMetres - radiusMetres, yMetres - radiusMetres);
            (double c1, double r1) = grid.ToGrid(xMetres + radiusMetres, yMetres + radiusMetres);
            int left = Math.Max(0, (int)Math.Floor(Math.Min(c0, c1)));
            int right = Math.Min(grid.Width - 1, (int)Math.Ceiling(Math.Max(c0, c1)));
            int top = Math.Max(0, (int)Math.Floor(Math.Min(r0, r1)));
            int bottom = Math.Min(grid.Height - 1, (int)Math.Ceiling(Math.Max(r0, r1)));
            int before = changed.Count;
            for (int y = top; y <= bottom; y++)
            {
                for (int x = left; x <= right; x++)
                {
                    (double sx, double sy) = grid.SampleMetres(x, y);
                    if (((sx - xMetres) * (sx - xMetres)) + ((sy - yMetres) * (sy - yMetres)) > radiusMetres * radiusMetres)
                    {
                        continue;
                    }

                    // Shift takes the material away by putting back what the level had there; a map has no "nothing".
                    byte now = map.GetMaterial(x, y);
                    byte next = lay ? material : Loaded(grid)[(y * map.Width) + x];
                    if (next != now && (lay || now == material))
                    {
                        map.SetMaterial(x, y, next);
                        changed.Add(new TerrainPaintCell(grid.Index, x, y, now, next));
                    }
                }
            }

            if (changed.Count > before)
            {
                Painted?.Invoke(grid.Index, left, top, right, bottom);
            }
        }
    }

    /// <inheritdoc/>
    public void SetPaint(IEnumerable<TerrainPaintCell> cells, bool after)
    {
        ArgumentNullException.ThrowIfNull(cells);
        Dictionary<int, (int Left, int Top, int Right, int Bottom)> touched = [];
        foreach (TerrainPaintCell cell in cells)
        {
            if ((uint)cell.Grid >= (uint)_grids.Length || _grids[cell.Grid].Tile.Materials is not { } map)
            {
                continue;
            }

            map.SetMaterial(cell.X, cell.Y, (byte)(after ? cell.After : cell.Before));
            touched[cell.Grid] = touched.TryGetValue(cell.Grid, out (int Left, int Top, int Right, int Bottom) box)
                ? (Math.Min(box.Left, cell.X), Math.Min(box.Top, cell.Y), Math.Max(box.Right, cell.X), Math.Max(box.Bottom, cell.Y))
                : (cell.X, cell.Y, cell.X, cell.Y);
        }

        foreach ((int grid, (int left, int top, int right, int bottom)) in touched)
        {
            Painted?.Invoke(grid, left, top, right, bottom);
        }
    }

    // The material ids a tile was loaded with.
    private static byte[] Loaded(Grid grid) => grid.LoadedMaterials;

    // One layer per declared material, over the ground every tile with a material map covers.
    private TerrainPaintLayer[] Layers()
    {
        Grid[] painted = [.. _grids.Where(static g => g.Tile.Materials is not null)];
        if (painted.Length == 0)
        {
            return [];
        }

        double units = WorldScene.UnitsPerMetre;
        double minX = painted.Min(static g => g.Tile.SampleX(0)) / units;
        double minY = painted.Min(static g => g.Tile.SampleY(0)) / units;
        double maxX = painted.Max(static g => g.Tile.SampleX(g.Width - 1)) / units;
        double maxY = painted.Max(static g => g.Tile.SampleY(g.Height - 1)) / units;
        double cell = painted.Min(static g => g.Tile.TexelWidth) / units;
        return [.. _materials.Values.OrderBy(static m => m.MaterialId).Select((material, index) => new TerrainPaintLayer(
            index,
            string.Create(CultureInfo.InvariantCulture, $"Material {material.MaterialId}  •  {(material.Texture.Length == 0 ? "untextured" : Path.GetFileNameWithoutExtension(material.Texture))}"),
            material.MaterialId,
            minX,
            minY,
            maxX,
            maxY,
            cell))];
    }
}
