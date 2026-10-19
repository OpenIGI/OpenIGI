using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>A mission's walls and ground, as one collision anything can ask.</summary>
public sealed class LevelCollisionTests
{
    [Fact]
    public void A_level_with_nothing_placed_casts_through_everything()
    {
        LevelCollision empty = new MissionState().Collision;

        Assert.True(empty.IsEmpty);
        Assert.False(empty.Cast(Vector3d.Zero, new Vector3d(0, 0, -1000), out _));
        Assert.Null(empty.Ground);
    }

    [Fact]
    public void The_missions_collision_is_kept_until_what_it_is_made_of_changes()
    {
        MissionState mission = new();
        LevelCollision first = mission.Collision;

        Assert.Same(first, mission.Collision);

        Flat ground = new();
        mission.Ground = ground;
        LevelCollision second = mission.Collision;

        Assert.NotSame(first, second);
        Assert.Same(ground, second.Ground);
        Assert.Same(second, mission.Collision);
    }

    private sealed class Flat : IGroundHeightField
    {
        public bool TryGetGround(double x, double y, double z, double aboveUnits, out TerrainGround ground)
        {
            ground = new TerrainGround(0, 0, 0, 1);
            return true;
        }

        public bool TryTraceDescent(Vector3d from, Vector3d target, out TerrainGround ground)
        {
            ground = new TerrainGround(0, 0, 0, 1);
            return true;
        }
    }
}
