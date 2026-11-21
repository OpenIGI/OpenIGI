using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>
/// Ground that holds a human up in deep water: where the water stands deeper than a human's draught over the ground beneath,
/// what is stood on is a level plane that far under the surface, so the human goes across with its head out.
/// </summary>
/// <remarks>
/// For a human of a game that does not swim, in the level of one that does. Wading, the ground is the bed's; out of its depth,
/// it treads water at the surface and walks on as it would on the level, where the level's own human would swim.
/// </remarks>
/// <param name="bed">The level's ground, or <see langword="null"/> for a level without any.</param>
/// <param name="surface">The height of the water's surface over a point, in world units, or <see langword="null"/> where there is no water.</param>
public sealed class FloatingGround(IGroundHeightField? bed, Func<Vector3d, double?> surface) : IGroundHeightField
{
    /// <summary>How far under the surface a floating human's feet are, in world units: 1.35 metres, its eyes a hand above the water.</summary>
    public const double Draught = 1.35 * 4096.0;

    // How far over the plane the bed is looked for, to find land standing over the water: higher than any level's ground.
    private const double LandAboveUnits = 4096.0 * 10000.0;

    private readonly Func<Vector3d, double?> _surface = surface ?? throw new ArgumentNullException(nameof(surface));

    /// <inheritdoc/>
    public bool TryGetGround(double x, double y, double z, double aboveUnits, out TerrainGround ground)
    {
        bool onBed = bed is not null && bed.TryGetGround(x, y, z, aboveUnits, out ground) || Unset(out ground);
        if (_surface(new Vector3d(x, y, z)) is not { } top)
        {
            return onBed;
        }

        // The plane counts only within the window, as ground does, and only where the bed is deeper: a level's water can run on
        // under its land, and a search from high over the land would otherwise meet the plane before it reached the land.
        double afloat = top - Draught;
        if (afloat > z + aboveUnits || afloat < z - TerrainHeightQuery.SearchDepthUnits || (onBed && ground.Height >= afloat))
        {
            return onBed;
        }

        if (!onBed && bed is not null && bed.TryGetGround(x, y, afloat, LandAboveUnits, out TerrainGround land) && land.Height >= afloat)
        {
            return false;
        }

        ground = new TerrainGround(afloat, 0.0, 0.0, 1.0);
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>A body falling into deep water crosses the plane it floats at before it would reach the bed.</remarks>
    public bool TryTraceDescent(Vector3d from, Vector3d target, out TerrainGround ground)
    {
        bool onBed = bed is not null && bed.TryTraceDescent(from, target, out ground) || Unset(out ground);
        if (_surface(target) is not { } top)
        {
            return onBed;
        }

        double afloat = top - Draught;
        if (from.Z < afloat || target.Z > afloat || (onBed && ground.Height >= afloat))
        {
            return onBed;
        }

        ground = new TerrainGround(afloat, 0.0, 0.0, 1.0);
        return true;
    }

    private static bool Unset(out TerrainGround ground)
    {
        ground = default;
        return false;
    }
}
