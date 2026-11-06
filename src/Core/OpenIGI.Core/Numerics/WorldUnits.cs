namespace OpenIGI.Core.Numerics;

/// <summary>The games' world unit.</summary>
/// <remarks>
/// Both games measure the world in units of 1/4096 m: the scripts' <c>WORLD_METER</c> constant, every position in
/// level data, every physics and AI distance. This is the one place the figure is written; each subsystem's own
/// named constant is defined from it.
/// </remarks>
public static class WorldUnits
{
    /// <summary>The number of world units in one metre.</summary>
    public const double PerMetre = 4096.0;

    /// <summary>Converts world units to metres.</summary>
    /// <param name="units">A length in world units.</param>
    /// <returns>The length in metres.</returns>
    public static double ToMetres(double units) => units / PerMetre;

    /// <summary>Converts metres to world units.</summary>
    /// <param name="metres">A length in metres.</param>
    /// <returns>The length in world units.</returns>
    public static double FromMetres(double metres) => metres * PerMetre;
}
