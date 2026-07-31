using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The death camera's vantage search (<c>FindStartPosition</c>): the first elevation-and-azimuth guess
/// whose ray clears the world wins, else the farthest-reaching one, and the eye sits half a metre short.
/// </summary>
public sealed class DeathCameraVantageTests
{
    private static readonly Vector3d Pivot = new(1000.0, 2000.0, 3000.0);

    /// <summary>With nothing blocking, the very first guess is taken at the full pulled-in ideal.</summary>
    [Fact]
    public void OpenSpaceTakesTheFirstGuessAtTheIdeal()
    {
        DeathCameraVantage.Vantage v = DeathCameraVantage.Find(Pivot, gamma: 0f, walls: null);

        Assert.True(v.Clear);
        Assert.Equal(
            HumanPlayerBody.DeathCameraDistanceInUnits - HumanPlayerBody.DeathCameraPullInInUnits,
            v.DistanceInUnits,
            3);

        // The first elevation guess (~0.5825) at the first azimuth (gamma - pi/2): the direction is
        // (sin(azimuth)cos(elev), -cos(azimuth)cos(elev), sin(elev)).
        float elevation = HumanPlayerBody.DeathCameraElevationGuesses[0];
        double azimuth = HumanPlayerBody.DeathCameraAzimuthStart;
        Assert.Equal(Math.Sin(azimuth) * Math.Cos(elevation), v.Direction.X, 4);
        Assert.Equal(-Math.Cos(azimuth) * Math.Cos(elevation), v.Direction.Y, 4);
        Assert.Equal(Math.Sin(elevation), v.Direction.Z, 4);

        // The direction is a unit vector.
        double length = Math.Sqrt(
            (v.Direction.X * v.Direction.X) + (v.Direction.Y * v.Direction.Y) + (v.Direction.Z * v.Direction.Z));
        Assert.Equal(1.0, length, 4);
    }

    /// <summary>When the first direction is blocked but the next is clear, the next is taken.</summary>
    [Fact]
    public void ABlockedFirstDirectionFallsToTheNextClearOne()
    {
        int calls = 0;
        bool Walls(Vector3d from, Vector3d to, out Vector3d hit)
        {
            calls++;
            if (calls == 1)
            {
                // Block the very first ray, halfway along it.
                hit = new Vector3d((from.X + to.X) / 2.0, (from.Y + to.Y) / 2.0, (from.Z + to.Z) / 2.0);
                return true;
            }

            hit = default;
            return false;
        }

        DeathCameraVantage.Vantage v = DeathCameraVantage.Find(Pivot, gamma: 0f, Walls);

        Assert.True(v.Clear);

        // The second azimuth of the first elevation: gamma + start + one step.
        float elevation = HumanPlayerBody.DeathCameraElevationGuesses[0];
        double azimuth = HumanPlayerBody.DeathCameraAzimuthStart + HumanPlayerBody.DeathCameraAzimuthStep;
        Assert.Equal(Math.Sin(azimuth) * Math.Cos(elevation), v.Direction.X, 4);
        Assert.Equal(-Math.Cos(azimuth) * Math.Cos(elevation), v.Direction.Y, 4);
    }

    /// <summary>
    /// When every direction is blocked, the farthest-reaching is taken, half a metre short of its wall,
    /// and the vantage is marked not clear.
    /// </summary>
    [Fact]
    public void EverythingBlockedTakesTheFarthestReach()
    {
        // One direction (the fifth ray) is blocked far out; every other is blocked right at the body.
        int calls = 0;
        const double farReach = 9000.0;
        bool Walls(Vector3d from, Vector3d to, out Vector3d hit)
        {
            calls++;
            double along = calls == 5 ? farReach : 100.0;
            double dx = to.X - from.X;
            double dy = to.Y - from.Y;
            double dz = to.Z - from.Z;
            double length = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
            double s = along / length;
            hit = new Vector3d(from.X + (dx * s), from.Y + (dy * s), from.Z + (dz * s));
            return true;
        }

        DeathCameraVantage.Vantage v = DeathCameraVantage.Find(Pivot, gamma: 0f, Walls);

        Assert.False(v.Clear);
        Assert.Equal(farReach - HumanPlayerBody.DeathCameraPullInInUnits, v.DistanceInUnits, 2);
    }
}
