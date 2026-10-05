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
    /// <param name="work">Runs the rows side by side.</param>
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
        float[] height = new float[width * depth];

        work.ForRange(
            0,
            depth,
            Math.Max(1, Environment.ProcessorCount - 1),
            row =>
            {
                double y = (minY + ((row + 0.5) * cellMetres)) * WorldScene.UnitsPerMetre;
                for (int column = 0; column < width; column++)
                {
                    double x = (minX + ((column + 0.5) * cellMetres)) * WorldScene.UnitsPerMetre;
                    height[(row * width) + column] = terrain.TryTraceDescent(
                            new Vector3d(x, y, top),
                            new Vector3d(x, y, bottom),
                            out TerrainGround ground)
                        ? (float)(ground.Height / WorldScene.UnitsPerMetre)
                        : ShelterHeightField.OpenSky;
                }
            },
            cancellation);

        return ShelterHeightField.FromHeights(height, width, depth, minX, minY, cellMetres);
    }
}
