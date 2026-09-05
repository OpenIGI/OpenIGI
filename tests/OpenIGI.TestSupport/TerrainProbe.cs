using OpenIGI.Game.World;

namespace OpenIGI.TestSupport;

/// <summary>
/// A full-coverage terrain probe in world metres. The plain near-ground terrain query only answers
/// within a few metres below its probe point, so these helpers step the probe down the plausible
/// terrain band at four-metre intervals and return the first surface they land inside.
/// </summary>
public static class TerrainProbe
{
    /// <summary>
    /// Searches for terrain beneath the given X/Y by walking a probe down from
    /// <paramref name="topMetres"/> to <paramref name="bottomMetres"/> in four-metre steps.
    /// Returns false when no ground is found so callers can distinguish "helicopter sank" from
    /// "no terrain at this XY" instead of flowing a sentinel into a comparison.
    /// </summary>
    public static bool TryGetGroundHeight(
        TerrainData terrain,
        double metresX,
        double metresY,
        double topMetres,
        double bottomMetres,
        out double heightMetres)
    {
        const double units = WorldScene.UnitsPerMetre;
        for (double probeZ = topMetres * units; probeZ >= bottomMetres * units; probeZ -= 4.0 * units)
        {
            if (TerrainHeightQuery.TryGetGround(
                    terrain,
                    metresX * units,
                    metresY * units,
                    probeZ,
                    out TerrainGround ground))
            {
                heightMetres = ground.Height / units;
                return true;
            }
        }

        heightMetres = double.NaN;
        return false;
    }
}
