using OpenIGI.Core.Numerics;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>The bomb's blast wave: open air, a corner and a wall, and the damage that follows from each.</summary>
public sealed class BlastWaveFieldTests
{
    private const double M = WorldUnits.PerMetre;

    /// <summary>In the open the wave's effective distance is within a few percent of the straight line.</summary>
    [Fact]
    public void OpenAirReadsAsTheStraightLine()
    {
        BlastWaveField field = Built(static (Vector3d a, Vector3d b, out WorldRayHit hit) =>
        {
            hit = default;
            return false;
        });

        double effective = field.EffectiveMetres(new Vector3d(18 * M, 7 * M, 1.6 * M));
        double straight = (new Vector3d(18 * M, 7 * M, 1.6 * M) - field.Bomb).Length / M;
        Assert.InRange(effective, straight, straight * 1.12);
    }

    /// <summary>Behind an unbroken wall the wave all but fails to reach: the damage there is a scratch at most.</summary>
    [Fact]
    public void AWallStiflesTheBlast()
    {
        // A wall across the whole field at x = 6 m.
        BlastWaveField field = Built(static (Vector3d a, Vector3d b, out WorldRayHit hit) => Crosses(a, b, 6 * M, out hit));

        double behind = field.EffectiveMetres(new Vector3d(9 * M, 0, 1.2 * M));
        Assert.True(behind > 30, $"behind the wall the wave travelled {behind:0.0} m");
        Assert.True(BombDamage.Share(behind, 20) < 0.2f);
        Assert.True(BombDamage.Share(9, 20) >= 1f, "the same spot in the open is lethal");
    }

    /// <summary>Round a corner the wave arrives, but weaker than the straight line says.</summary>
    [Fact]
    public void ACornerShedsSomeOfIt()
    {
        // A wall at x = 6 m that stops at y = 4 m: round its end is open.
        BlastWaveField field = Built(static (Vector3d a, Vector3d b, out WorldRayHit hit) =>
        {
            hit = default;
            if (!Crosses(a, b, 6 * M, out hit))
            {
                return false;
            }

            return hit.Position.Y < 4 * M;
        });

        Vector3d spot = new(10 * M, -2 * M, 1.2 * M);
        double straight = (spot - field.Bomb).Length / M;
        double effective = field.EffectiveMetres(spot);
        Assert.True(effective > straight * 1.2, $"round the corner {effective:0.0} m against {straight:0.0} m straight");
        Assert.True(effective < 30 + straight, "but it is reached round the corner, not only through the wall");
    }

    /// <summary>The bell: lethal to the killing radius, a few percent ten metres past it, nothing far off.</summary>
    [Fact]
    public void DamageFallsOffAsABell()
    {
        Assert.True(BombDamage.Share(0, 20) >= 4.9f);
        Assert.InRange(BombDamage.Share(20, 20), 0.99f, 1.01f);
        Assert.InRange(BombDamage.Share(30, 20), 0.05f, 0.3f);
        Assert.Equal(0f, BombDamage.Share(60, 20));
    }

    private static BlastWaveField Built(WorldRayCast cast)
    {
        var field = new BlastWaveField(cast, default);
        field.Advance(TimeSpan.MaxValue);
        Assert.True(field.IsReady);
        return field;
    }

    // Whether a segment crosses the plane x = wall, and where.
    private static bool Crosses(Vector3d a, Vector3d b, double wall, out WorldRayHit hit)
    {
        hit = default;
        if ((a.X - wall) * (b.X - wall) > 0 || a.X == b.X)
        {
            return false;
        }

        double t = (wall - a.X) / (b.X - a.X);
        Vector3d at = a + ((b - a) * t);
        hit = new WorldRayHit(at, new Vector3f(-1, 0, 0), (at - a).Length);
        return true;
    }
}
