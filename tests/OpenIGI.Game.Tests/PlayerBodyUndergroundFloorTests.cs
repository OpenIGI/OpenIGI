using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// A floor below the terrain -- mission 3's cells under the security building (#18). The human's ground query casts up
/// only when its downward cast, objects and terrain together, met nothing; a body on a floor beneath the terrain stays on
/// that floor rather than being stood on the terrain over its head.
/// </summary>
public sealed class PlayerBodyUndergroundFloorTests
{
    private const double FloorZ = -5.0 * WorldScene.UnitsPerMetre;

    [Fact]
    public void A_body_on_a_floor_under_the_terrain_stays_on_the_floor()
    {
        PlayerBody player = new() { Position = new Vector3d(1000.0, 2000.0, FloorZ), Walls = Floor };
        SurfaceAtZero terrain = new();

        for (int tick = 0; tick < 30; tick++)
        {
            player.Tick(default, terrain, default);

            Assert.True(player.IsGrounded, $"tick {tick}: the body left the floor");
            Assert.Equal(FloorZ, player.Position.Z, 1e-6);
        }
    }

    [Fact]
    public void A_body_under_the_terrain_with_no_floor_is_raised_back_onto_it()
    {
        PlayerBody player = new() { Position = new Vector3d(1000.0, 2000.0, FloorZ) };

        player.Tick(default, new SurfaceAtZero(), default);

        Assert.True(player.IsGrounded);
        Assert.Equal(0.0, player.Position.Z, 1e-6);
    }

    private static bool Floor(Vector3d origin, Vector3d end, out WorldRayHit hit)
    {
        hit = default;
        if (end.Z >= origin.Z || origin.Z < FloorZ || end.Z > FloorZ)
        {
            return false;
        }

        hit = new WorldRayHit(new Vector3d(origin.X, origin.Y, FloorZ), new Vector3f(0f, 0f, 1f), origin.Z - FloorZ);
        return true;
    }

    /// <summary>A flat terrain at <c>Z = 0</c> with nothing below it, answered only within each query's window.</summary>
    private sealed class SurfaceAtZero : IGroundHeightField
    {
        public bool TryGetGround(double x, double y, double z, double aboveUnits, out TerrainGround ground)
        {
            ground = new TerrainGround(0.0, 0.0, 0.0, 1.0);
            return z + aboveUnits >= 0.0 && z - TerrainHeightQuery.SearchDepthUnits <= 0.0;
        }

        public bool TryTraceDescent(Vector3d from, Vector3d target, out TerrainGround ground)
        {
            ground = new TerrainGround(0.0, 0.0, 0.0, 1.0);
            return from.Z >= 0.0 && target.Z <= 0.0;
        }
    }
}
