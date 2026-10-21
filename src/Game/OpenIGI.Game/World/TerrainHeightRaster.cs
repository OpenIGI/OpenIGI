using OpenIGI.Core.Numerics;
using OpenIGI.Core.Threading;

namespace OpenIGI.Game.World;

/// <summary>A top-down raster of the terrain's height, for the passes that march without tracing it.</summary>
/// <remarks>
/// <para>
/// The level's distance field is built from its placements, and the terrain is not one of them; a ray
/// marched through it towards the ground goes straight through the ground and out of the field. Without
/// an acceleration structure to trace, that ray could only be answered with a guess at what open ground
/// gives back, which knows nothing of the pool a lamp throws on the pavement in front of a wall, and the
/// wall's foot took none of that pool's bounce where the traced gather showed it.
/// </para>
/// <para>
/// Held as a <see cref="ShelterHeightField"/> because it is the same thing -- a height per square of
/// ground, packed the same way, read by the same shader code -- with the terrain in place of the roofs.
/// </para>
/// </remarks>
public static class TerrainHeightRaster
{
    /// <summary>The default side of one cell, in metres.</summary>
    public const double DefaultCellMetres = 1.0;

    /// <summary>How far above and below the given span the descent looks, in metres.</summary>
    private const double VerticalSlack = 200.0;

    /// <summary>Samples the terrain's height over a rectangle of the level.</summary>
    /// <param name="work">The scheduler the caller runs the build on; the fill itself is one pass.</param>
    /// <param name="terrain">The level's terrain.</param>
    /// <param name="minX">The rectangle's low X, in metres.</param>
    /// <param name="minY">Its low Y, in metres.</param>
    /// <param name="maxX">Its high X, in metres.</param>
    /// <param name="maxY">Its high Y, in metres.</param>
    /// <param name="lowZ">The lowest the ground could be there, in metres.</param>
    /// <param name="highZ">The highest, in metres.</param>
    /// <param name="cellMetres">The side of one cell.</param>
    /// <param name="cancellation">Stops the sampling, which runs to a few hundred thousand descents.</param>
    /// <returns>The raster, with open sky wherever no terrain lies under a cell.</returns>
    public static ShelterHeightField Build(
        IWorkScheduler work,
        TerrainData terrain,
        double minX,
        double minY,
        double maxX,
        double maxY,
        double lowZ,
        double highZ,
        double cellMetres = DefaultCellMetres,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellMetres);

        int width = Math.Clamp((int)Math.Ceiling((maxX - minX) / cellMetres), 0, ShelterHeightField.MaxTexelsPerAxis);
        int depth = Math.Clamp((int)Math.Ceiling((maxY - minY) / cellMetres), 0, ShelterHeightField.MaxTexelsPerAxis);
        if (width == 0 || depth == 0)
        {
            return ShelterHeightField.Empty;
        }

        double top = (highZ + VerticalSlack) * WorldScene.UnitsPerMetre;
        double bottom = (lowZ - VerticalSlack) * WorldScene.UnitsPerMetre;
        double unitsPerCell = cellMetres * WorldScene.UnitsPerMetre;
        double originX = minX * WorldScene.UnitsPerMetre;
        double originY = minY * WorldScene.UnitsPerMetre;
        float[] height = new float[width * depth];
        Array.Fill(height, ShelterHeightField.OpenSky);

        // A descent from above answers with the highest triangle under the cell's centre, so every triangle is filled into the
        // cells whose centres it covers, the highest kept: one pass over the ground instead of a descent a cell.
        List<CollisionTriangle> triangles = [];
        TerrainRayTrace.CollectTriangles(
            terrain,
            new Vector3d(originX, originY, bottom),
            new Vector3d(originX + (width * unitsPerCell), originY + (depth * unitsPerCell), top),
            triangles);
        for (int i = 0; i < triangles.Count; i++)
        {
            if ((i & 0xFFF) == 0)
            {
                cancellation.ThrowIfCancellationRequested();
            }

            CollisionTriangle t = triangles[i];
            double lowY = Math.Min(t.A.Y, Math.Min(t.B.Y, t.C.Y));
            double highY = Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y));
            int firstRow = Math.Max(0, (int)Math.Ceiling(((lowY - originY) / unitsPerCell) - 0.5));
            int lastRow = Math.Min(depth - 1, (int)Math.Floor(((highY - originY) / unitsPerCell) - 0.5));
            for (int row = firstRow; row <= lastRow; row++)
            {
                FillRow(t, originY + ((row + 0.5) * unitsPerCell), row, width, originX, unitsPerCell, bottom, top, height);
            }
        }

        return ShelterHeightField.FromHeights(height, width, depth, minX, minY, cellMetres);
    }

    // The cells of one row whose centres the triangle covers, seen from above, each given the triangle's height there when it
    // is higher than what the cell holds and lies within the span the descent looked over.
    private static void FillRow(
        in CollisionTriangle t,
        double y,
        int row,
        int width,
        double originX,
        double unitsPerCell,
        double bottom,
        double top,
        float[] height)
    {
        double area = ((t.B.X - t.A.X) * (t.C.Y - t.A.Y)) - ((t.C.X - t.A.X) * (t.B.Y - t.A.Y));
        if (area == 0.0)
        {
            return;
        }

        double lowX = Math.Min(t.A.X, Math.Min(t.B.X, t.C.X));
        double highX = Math.Max(t.A.X, Math.Max(t.B.X, t.C.X));
        int first = Math.Max(0, (int)Math.Ceiling(((lowX - originX) / unitsPerCell) - 0.5));
        int last = Math.Min(width - 1, (int)Math.Floor(((highX - originX) / unitsPerCell) - 0.5));
        for (int column = first; column <= last; column++)
        {
            double x = originX + ((column + 0.5) * unitsPerCell);
            double wa = (((t.B.X - x) * (t.C.Y - y)) - ((t.C.X - x) * (t.B.Y - y))) / area;
            double wb = (((t.C.X - x) * (t.A.Y - y)) - ((t.A.X - x) * (t.C.Y - y))) / area;
            double wc = 1.0 - wa - wb;
            if (wa < 0.0 || wb < 0.0 || wc < 0.0)
            {
                continue;
            }

            double z = (wa * t.A.Z) + (wb * t.B.Z) + (wc * t.C.Z);
            float metres = (float)(z / WorldScene.UnitsPerMetre);
            int cell = (row * width) + column;
            if (z >= bottom && z <= top && metres > height[cell])
            {
                height[cell] = metres;
            }
        }
    }
}
