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
}

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
            if (!terrain.TryGetHeight(at.X, at.Y, out double ground))
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
                    bool below = terrain.TryGetHeight(probe.X, probe.Y, out double z) && probe.Z <= z;
                    (low, high) = below ? (low, middle) : (middle, high);
                }

                Vector3d found = origin + (step * high);
                hit = terrain.TryGetHeight(found.X, found.Y, out double surface) ? new Vector3d(found.X, found.Y, surface) : found;
                return true;
            }

            previousAbove = above;
            previous = t;
        }

        return false;
    }
}
