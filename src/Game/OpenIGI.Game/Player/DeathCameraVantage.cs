using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Player;

/// <summary>
/// Where the death camera should sit — the search <c>FindStartPosition</c> (<c>0x4d36a0</c>) runs to frame
/// the fallen body against open space.
/// </summary>
/// <remarks>
/// <para>
/// For each of the eight elevation guesses in preference order, and eight azimuths a quarter-turn apart
/// around the body, it casts a ray of the ideal length out from the body. It takes the <b>first direction
/// whose ray clears the world</b> — a vantage in open space — and, failing that, the direction whose ray
/// reached <b>farthest</b> before it was blocked. Either way the eye sits half a metre short of the ideal
/// or of the wall (<see cref="HumanPlayerBody.DeathCameraPullInInUnits"/>), so it never ends up inside
/// geometry.
/// </para>
/// <para>
/// This is the pure geometry of the search, kept out of the renderer so it can be tested: it takes a ray
/// test and returns the chosen direction and distance, in engine units. Read from I.G.I. 2's named camera
/// (the shared engine's); I.G.I. 1's own death camera is a different, simpler mode and does not carry this
/// table — see <see cref="HumanPlayerBody.DeathCameraElevationGuesses"/>.
/// </para>
/// </remarks>
public static class DeathCameraVantage
{
    /// <summary>A world ray test: whether the straight line from <paramref name="fromUnits"/> to
    /// <paramref name="toUnits"/> is blocked, and where.</summary>
    /// <param name="fromUnits">The start, in engine units.</param>
    /// <param name="toUnits">The end, in engine units.</param>
    /// <param name="hitUnits">Receives where the line was first blocked, in engine units.</param>
    /// <returns><see langword="true"/> when something blocks the line.</returns>
    public delegate bool Blocked(Vector3d fromUnits, Vector3d toUnits, out Vector3d hitUnits);

    /// <summary>A chosen vantage — a unit direction from the body and how far along it the eye sits.</summary>
    /// <param name="Direction">The unit direction from the body to the eye.</param>
    /// <param name="DistanceInUnits">How far along it the eye sits, already pulled in from the ideal or wall.</param>
    /// <param name="Clear"><see langword="true"/> when a fully-clear vantage was found, not the fallback.</param>
    public readonly record struct Vantage(Vector3d Direction, double DistanceInUnits, bool Clear);

    /// <summary>Finds where the death camera should sit relative to the body.</summary>
    /// <param name="pivot">What the camera looks at — normally the body's chest, in engine units.</param>
    /// <param name="gamma">The heading the body faces; the sweep is measured around behind it.</param>
    /// <param name="walls">The world ray test, or <see langword="null"/> to treat everything as clear.</param>
    /// <returns>The chosen direction and distance.</returns>
    public static Vantage Find(Vector3d pivot, float gamma, Blocked? walls)
    {
        double ideal = HumanPlayerBody.DeathCameraDistanceInUnits;
        double pullIn = HumanPlayerBody.DeathCameraPullInInUnits;

        Vector3d bestDirection = default;
        double bestDistance = 0.0;   // the farthest a ray reached, for the fallback

        foreach (float elevation in HumanPlayerBody.DeathCameraElevationGuesses)
        {
            double ce = Math.Cos(elevation);
            double se = Math.Sin(elevation);

            for (int step = 0; step < HumanPlayerBody.DeathCameraAzimuthCount; step++)
            {
                double delta = HumanPlayerBody.DeathCameraAzimuthStart
                    + (step * HumanPlayerBody.DeathCameraAzimuthStep);

                // Behind the body, rotated by delta about the vertical. A body facing gamma looks along
                // (-sin gamma, cos gamma), so directly behind it is (sin gamma, -cos gamma); rotating that
                // by delta gives (sin(gamma+delta), -cos(gamma+delta)). The elevation lifts it out of the
                // ground plane.
                double azimuth = gamma + delta;
                Vector3d direction = new(
                    Math.Sin(azimuth) * ce, -Math.Cos(azimuth) * ce, se);

                Vector3d far = new(
                    pivot.X + (direction.X * ideal),
                    pivot.Y + (direction.Y * ideal),
                    pivot.Z + (direction.Z * ideal));

                if (walls is null || !walls(pivot, far, out Vector3d hit))
                {
                    // Clear all the way: the ideal vantage. First one wins.
                    return new Vantage(direction, Math.Max(0.0, ideal - pullIn), Clear: true);
                }

                double reached = Vector3d.Distance(pivot, hit);
                if (reached > bestDistance)
                {
                    bestDirection = direction;
                    bestDistance = reached;
                }
            }
        }

        if (bestDistance <= 0.0)
        {
            // The loop cast nothing usable -- fall straight back at the ideal, level with the body.
            return new Vantage(new Vector3d(Math.Sin(gamma), -Math.Cos(gamma), 0.0), ideal, Clear: false);
        }

        // Reached only here when no ray was fully clear: the fallback is the farthest wall, never clear.
        return new Vantage(bestDirection, Math.Max(0.0, bestDistance - pullIn), Clear: false);
    }

}
