using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>The plain world line test the cameras and the flashlight keep themselves out of walls with.</summary>
public static class WorldWalls
{
    /// <summary>A world ray test: whether the straight line from <paramref name="fromUnits"/> to
    /// <paramref name="toUnits"/> is blocked, and where.</summary>
    /// <param name="fromUnits">The start, in engine units.</param>
    /// <param name="toUnits">The end, in engine units.</param>
    /// <param name="hitUnits">Receives where the line was first blocked, in engine units.</param>
    /// <returns><see langword="true"/> when something blocks the line.</returns>
    public delegate bool Blocked(Vector3d fromUnits, Vector3d toUnits, out Vector3d hitUnits);
}
