using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>
/// A level's ground as grids of height samples that an editor can change while the level runs: the drawing, the
/// collision and anything else built on the ground see a change once it is <see cref="Refresh">refreshed</see>.
/// </summary>
/// <remarks>
/// A title that draws its ground from height samples implements this; the editor's brushes are written once against it,
/// for every title and every terrain system that does.
/// </remarks>
public interface IEditableTerrain
{
    /// <summary>Gets the grids, coarsest first; where two overlap, the later one is the ground.</summary>
    IReadOnlyList<ITerrainHeightGrid> Grids { get; }

    /// <summary>Gets the ground's height under a point.</summary>
    /// <param name="xMetres">The point's X, in world metres.</param>
    /// <param name="yMetres">The point's Y, in world metres.</param>
    /// <param name="zMetres">Receives the height, in world metres.</param>
    /// <returns><see langword="false"/> when no grid covers the point.</returns>
    bool TryGetHeight(double xMetres, double yMetres, out double zMetres);

    /// <summary>Finds where a ray first meets the ground.</summary>
    /// <param name="origin">Where the ray starts, in world metres.</param>
    /// <param name="direction">Its direction; need not be unit length.</param>
    /// <param name="maximumMetres">How far to look.</param>
    /// <param name="hit">Receives the point on the ground.</param>
    /// <returns><see langword="false"/> when the ray meets no ground in range.</returns>
    /// <remarks>By default a march over <see cref="TryGetHeight"/>; a ground with a faster exact trace answers with it.</remarks>
    bool Raycast(Vector3d origin, Vector3d direction, double maximumMetres, out Vector3d hit) =>
        TerrainHeightGrids.March(TryGetHeight, origin, direction, maximumMetres, out hit);

    /// <summary>Tells everything built on a grid that some of its samples changed.</summary>
    /// <param name="grid">The grid.</param>
    /// <param name="left">The first changed column.</param>
    /// <param name="top">The first changed row.</param>
    /// <param name="right">The last changed column.</param>
    /// <param name="bottom">The last changed row.</param>
    void Refresh(ITerrainHeightGrid grid, int left, int top, int right, int bottom);

    /// <summary>Gets the files that store the grids changed so far, ready to be written where the level reads them.</summary>
    /// <param name="read">Reads a file of the mission's own folder, by its path relative to it; <see langword="null"/> when it has none.</param>
    /// <returns>Each file's path relative to the mission's folder, and its new contents.</returns>
    IReadOnlyList<(string MissionPath, byte[] Bytes)> ChangedFiles(Func<string, ReadOnlyMemory<byte>?> read);

    /// <summary>Records that the changes so far have been saved, so <see cref="ITerrainHeightGrid.IsChanged"/> starts over.</summary>
    void MarkSaved();

    /// <summary>Gets how many samples were asked to go further than the ground can store since this was last asked, and starts over.</summary>
    /// <returns>The number of samples held at a limit; always nought for a ground with no limit.</returns>
    int TakeClampCount() => 0;

    /// <summary>Gets a sentence saying how far the ground can be shaped, for when <see cref="TakeClampCount"/> reports samples held back.</summary>
    string? LimitDescription => null;

    /// <summary>Gets whether only parts of the ground can be shaped: the grids' outlines are then worth showing.</summary>
    bool IsPartial => false;

    /// <summary>Lays a new grid over the ground around a point, for a ground that can only be shaped in places.</summary>
    /// <param name="pointMetres">A point on the ground, in world metres.</param>
    /// <param name="id">The id to give the grid, to lay again one <see cref="RemoveArea"/> took away; <see langword="null"/> for the next free one.</param>
    /// <returns>The grid and the task that saves it, or <see langword="null"/> when the point is in a grid already, off the ground, or no more grids fit.</returns>
    TerrainArea? AddArea(Vector3d pointMetres, int? id = null) => null;

    /// <summary>Takes away a grid <see cref="AddArea"/> laid, and whatever was shaped on it.</summary>
    /// <param name="area">The grid.</param>
    void RemoveArea(TerrainArea area)
    {
        _ = area;
    }

    /// <summary>Lays a mountain area over the ground around a point: a square shaped with no height limit, for a ground whose
    /// grids can otherwise only nudge it.</summary>
    /// <param name="pointMetres">A point on the ground, in world metres.</param>
    /// <returns>The area's grid, or <see langword="null"/> when the point is in one already, off the ground, or the ground cannot have one.</returns>
    ITerrainHeightGrid? AddMountain(Vector3d pointMetres) => null;

    /// <summary>Gets the mountain areas laid so far.</summary>
    IReadOnlyList<ITerrainHeightGrid> Mountains => [];

    /// <summary>Takes a mountain area <see cref="AddMountain"/> laid away, and what was shaped in it.</summary>
    /// <param name="area">The area.</param>
    void RemoveMountain(ITerrainHeightGrid area) => _ = area;

    /// <summary>Finishes work a batch of <see cref="Refresh"/> calls put off, as a stroke ends or an undo lands.</summary>
    void Settle()
    {
    }

    /// <summary>Gets the ground materials that can be painted, each over its own square; empty for a ground that cannot be.</summary>
    IReadOnlyList<TerrainPaintLayer> PaintLayers => [];

    /// <summary>Paints a material on or off the ground in a circle.</summary>
    /// <param name="layer">The layer's <see cref="TerrainPaintLayer.Index"/>.</param>
    /// <param name="xMetres">The circle's centre, world X in metres.</param>
    /// <param name="yMetres">The circle's centre, world Y in metres.</param>
    /// <param name="radiusMetres">The circle's radius, in metres.</param>
    /// <param name="lay">Whether to lay the material (or take it away).</param>
    /// <param name="changed">Receives every cell that changed, with its value before and after, for undoing.</param>
    void Paint(int layer, double xMetres, double yMetres, double radiusMetres, bool lay, ICollection<TerrainPaintCell> changed) =>
        _ = (layer, xMetres, yMetres, radiusMetres, lay, changed);

    /// <summary>Puts paint cells to their value before or after, as undo and redo do.</summary>
    /// <param name="cells">Cells <see cref="Paint"/> reported.</param>
    /// <param name="after">Whether to put each to its value after (redo) rather than before (undo).</param>
    void SetPaint(IEnumerable<TerrainPaintCell> cells, bool after) => _ = (cells, after);
}

/// <summary>One cell of paint a stroke changed.</summary>
/// <param name="Grid">Which grid of cells: a mask (I.G.I.'s layer) or a tile's material map (I.G.I. 2).</param>
/// <param name="X">The column.</param>
/// <param name="Y">The row.</param>
/// <param name="Before">The value before: set or clear (1 or 0) in a mask, a material id in a map.</param>
/// <param name="After">The value after.</param>
public readonly record struct TerrainPaintCell(int Grid, int X, int Y, int Before, int After);

/// <summary>A ground material that can be painted, over the square of ground its mask covers.</summary>
/// <param name="Index">Its index, for <see cref="IEditableTerrain.Paint"/>.</param>
/// <param name="Name">A name for it, for lists.</param>
/// <param name="MaterialId">The ground material it lays.</param>
/// <param name="MinX">The square's lowest world X, in metres.</param>
/// <param name="MinY">The square's lowest world Y, in metres.</param>
/// <param name="MaxX">The square's highest world X, in metres.</param>
/// <param name="MaxY">The square's highest world Y, in metres.</param>
/// <param name="CellMetres">How wide one paint cell is, in metres.</param>
public sealed record TerrainPaintLayer(int Index, string Name, int MaterialId, double MinX, double MinY, double MaxX, double MaxY, double CellMetres);

/// <summary>One grid of ground height samples, laid out over the world.</summary>
public interface ITerrainHeightGrid
{
    /// <summary>Gets a name for the grid, for lists and messages.</summary>
    string Name { get; }

    /// <summary>Gets the number of samples along a row.</summary>
    int Width { get; }

    /// <summary>Gets the number of rows.</summary>
    int Height { get; }

    /// <summary>Gets whether any sample has changed since the grid was loaded or last saved.</summary>
    bool IsChanged { get; }

    /// <summary>Gets where a sample stands on the ground plane.</summary>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <returns>The X and Y, in world metres.</returns>
    (double X, double Y) SampleMetres(int x, int y);

    /// <summary>Gets the fractional sample coordinates of a point on the ground plane.</summary>
    /// <param name="xMetres">The point's X, in world metres.</param>
    /// <param name="yMetres">The point's Y, in world metres.</param>
    /// <returns>The column and row, fractional, unbounded.</returns>
    (double Column, double Row) ToGrid(double xMetres, double yMetres);

    /// <summary>Gets a sample's height.</summary>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <returns>The height, in world metres.</returns>
    double GetHeight(int x, int y);

    /// <summary>Sets a sample's height. Call <see cref="IEditableTerrain.Refresh"/> once a batch of changes is done.</summary>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <param name="zMetres">The height, in world metres.</param>
    void SetHeight(int x, int y, double zMetres);
}

/// <summary>The height under a point of a height field.</summary>
/// <param name="xMetres">The point's X, in world metres.</param>
/// <param name="yMetres">The point's Y, in world metres.</param>
/// <param name="zMetres">Receives the height, in world metres.</param>
/// <returns><see langword="false"/> where the field has no height.</returns>
public delegate bool HeightAt(double xMetres, double yMetres, out double zMetres);

/// <summary>Sample-grid helpers every terrain shares.</summary>
public static class TerrainHeightGrids
{
    /// <summary>Gets the height between samples, bilinearly, or nothing outside the grid.</summary>
    /// <param name="grid">The grid.</param>
    /// <param name="xMetres">The point's X, in world metres.</param>
    /// <param name="yMetres">The point's Y, in world metres.</param>
    /// <param name="zMetres">Receives the height, in world metres.</param>
    /// <returns><see langword="false"/> when the point is outside the samples.</returns>
    public static bool TrySample(this ITerrainHeightGrid grid, double xMetres, double yMetres, out double zMetres)
    {
        ArgumentNullException.ThrowIfNull(grid);
        (double column, double row) = grid.ToGrid(xMetres, yMetres);
        zMetres = 0.0;
        if (column < 0.0 || row < 0.0 || column > grid.Width - 1 || row > grid.Height - 1)
        {
            return false;
        }

        int x = Math.Min((int)column, Math.Max(0, grid.Width - 2));
        int y = Math.Min((int)row, Math.Max(0, grid.Height - 2));
        double fx = column - x;
        double fy = row - y;
        int x1 = Math.Min(x + 1, grid.Width - 1);
        int y1 = Math.Min(y + 1, grid.Height - 1);
        double top = (grid.GetHeight(x, y) * (1.0 - fx)) + (grid.GetHeight(x1, y) * fx);
        double bottom = (grid.GetHeight(x, y1) * (1.0 - fx)) + (grid.GetHeight(x1, y1) * fx);
        zMetres = (top * (1.0 - fy)) + (bottom * fy);
        return true;
    }

    /// <summary>Finds where a ray first meets the ground, by marching along it and refining the crossing.</summary>
    /// <param name="terrain">The ground.</param>
    /// <param name="origin">Where the ray starts, in world metres.</param>
    /// <param name="direction">Its direction; need not be unit length.</param>
    /// <param name="maximumMetres">How far to look.</param>
    /// <param name="hit">Receives the point on the ground.</param>
    /// <returns><see langword="false"/> when the ray meets no ground in range.</returns>
    public static bool TryRaycast(this IEditableTerrain terrain, Vector3d origin, Vector3d direction, double maximumMetres, out Vector3d hit)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        return terrain.Raycast(origin, direction, maximumMetres, out hit);
    }

    /// <summary>Finds where a ray first meets a height field, by marching along it and refining the crossing.</summary>
    /// <param name="height">The height field: the height under a point, or false where there is none.</param>
    /// <param name="origin">Where the ray starts, in world metres.</param>
    /// <param name="direction">Its direction; need not be unit length.</param>
    /// <param name="maximumMetres">How far to look.</param>
    /// <param name="hit">Receives the point on the ground.</param>
    /// <returns><see langword="false"/> when the ray meets no ground in range.</returns>
    public static bool March(HeightAt height, Vector3d origin, Vector3d direction, double maximumMetres, out Vector3d hit)
    {
        ArgumentNullException.ThrowIfNull(height);
        hit = default;
        double length = direction.Length;
        if (length <= 0.0)
        {
            return false;
        }

        Vector3d step = direction / length;
        double previous = 0.0;
        double previousAbove = double.NaN;
        for (double t = 0.0; t <= maximumMetres; t += Math.Max(0.25, t * 0.01))
        {
            Vector3d at = origin + (step * t);
            if (!height(at.X, at.Y, out double ground))
            {
                previousAbove = double.NaN;
                previous = t;
                continue;
            }

            double above = at.Z - ground;
            if (above <= 0.0 && !double.IsNaN(previousAbove))
            {
                // Halve the last step until the crossing is within a centimetre.
                double low = previous;
                double high = t;
                while (high - low > 0.01)
                {
                    double middle = (low + high) * 0.5;
                    Vector3d probe = origin + (step * middle);
                    bool below = height(probe.X, probe.Y, out double z) && probe.Z <= z;
                    (low, high) = below ? (low, middle) : (middle, high);
                }

                Vector3d found = origin + (step * high);
                hit = height(found.X, found.Y, out double surface) ? new Vector3d(found.X, found.Y, surface) : found;
                return true;
            }

            previousAbove = above;
            previous = t;
        }

        return false;
    }
}
