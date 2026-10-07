using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Player;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>A body's footing, <c>Human_GroundCollision</c> (<c>igi2 0x4FD130</c>), against flat floors and ceilings.</summary>
public sealed class Igi2HumanGroundTests
{
    private const float Tolerance = 819.2f;

    /// <summary>Ground within the tolerance below the feet is stood on: the feet drop onto it and it is held.</summary>
    [Fact]
    public void Ground_within_the_tolerance_is_stood_on()
    {
        (Igi2HumanPlayer player, Igi2PlayerFakes.FakeSurroundings surroundings) = Body();
        Igi2PlayerFakes.FakeCollider floor = new(1000.0, material: 7, light: 1.5f);
        surroundings.World.Surfaces.Add(floor);

        Vector3d to = new(10.0, 20.0, 1500.0);
        player.GroundCollision(new Vector3d(10.0, 20.0, 1500.0), ref to, Tolerance);

        Assert.True(player.Signal.HasFlag(Igi2PlayerSignal.Grounded));
        Assert.Equal(1000.0, to.Z);
        Assert.Equal(500.0, player.DistanceToGround);
        Assert.Same(floor, player.GroundCollider);
        Assert.True(player.GroundIntersection.IsHit);
        Assert.Equal(7, player.GroundIntersection.EnterMaterial);
    }

    /// <summary>The struck object alone is probed again for its material, and its light is averaged into the body's.</summary>
    [Fact]
    public void The_struck_object_is_probed_for_material_and_light()
    {
        (Igi2HumanPlayer player, Igi2PlayerFakes.FakeSurroundings surroundings) = Body();
        Igi2PlayerFakes.FakeCollider floor = new(1000.0, material: 7, light: 1.5f);
        surroundings.World.Surfaces.Add(floor);
        player.Lightmap = 0.5f;

        Vector3d to = new(0.0, 0.0, 1200.0);
        player.GroundCollision(to, ref to, Tolerance);

        Assert.Equal(2, floor.Probes);
        Assert.Equal(1f, player.Lightmap);
    }

    /// <summary>Ground further below than the tolerance is not stood on, and what was held is let go.</summary>
    [Fact]
    public void Ground_beyond_the_tolerance_is_a_fall()
    {
        (Igi2HumanPlayer player, Igi2PlayerFakes.FakeSurroundings surroundings) = Body();
        surroundings.World.Surfaces.Add(new Igi2PlayerFakes.FakeCollider(1000.0));
        Vector3d to = new(0.0, 0.0, 1200.0);
        player.GroundCollision(to, ref to, Tolerance);
        Assert.NotNull(player.GroundCollider);

        to = new Vector3d(0.0, 0.0, 3000.0);
        player.GroundCollision(to, ref to, Tolerance);

        Assert.False(player.Signal.HasFlag(Igi2PlayerSignal.Grounded));
        Assert.Null(player.GroundCollider);
        Assert.Equal(3000.0, to.Z);
        Assert.Equal(2000.0, player.DistanceToGround);
    }

    /// <summary>While a slope slides the body, ground up to two metres below still holds it.</summary>
    [Fact]
    public void A_sliding_body_keeps_ground_two_metres_below()
    {
        (Igi2HumanPlayer player, Igi2PlayerFakes.FakeSurroundings surroundings) = Body();
        surroundings.World.Surfaces.Add(new Igi2PlayerFakes.FakeCollider(1000.0));
        player.IdealSlidingSpeed = new Vector3f(0f, 0f, -10f);

        Vector3d to = new(0.0, 0.0, 8000.0);
        player.GroundCollision(to, ref to, Tolerance);

        Assert.True(player.Signal.HasFlag(Igi2PlayerSignal.Grounded));
        Assert.Equal(1000.0, to.Z);
    }

    /// <summary>A ceiling nearer than 1.8 metres above the feet raises the low-roof signal; a floor overhead does not count.</summary>
    [Theory]
    [InlineData(true, 6000.0, false)]
    [InlineData(false, 6000.0, true)]
    [InlineData(false, 8000.0, false)]
    public void A_low_ceiling_is_signalled(bool facesUp, double height, bool low)
    {
        (Igi2HumanPlayer player, Igi2PlayerFakes.FakeSurroundings surroundings) = Body();
        surroundings.World.Surfaces.Add(new Igi2PlayerFakes.FakeCollider(-100.0));
        surroundings.World.Surfaces.Add(new Igi2PlayerFakes.FakeCollider(height, facesUp));

        Vector3d to = new(0.0, 0.0, 0.0);
        player.GroundCollision(to, ref to, Tolerance);

        Assert.Equal(low, ((uint)player.Signal & 0x8) != 0);
        Assert.Equal(facesUp ? 0.0 : height, player.DistanceToRoof);
    }

    /// <summary>With nothing below, a floor just above the feet is found by looking up, and the body is lifted onto it.</summary>
    [Fact]
    public void A_buried_body_is_lifted_onto_the_floor_above()
    {
        (Igi2HumanPlayer player, Igi2PlayerFakes.FakeSurroundings surroundings) = Body();
        player.GroundCheckZOffset = 0f;
        surroundings.World.Surfaces.Add(new Igi2PlayerFakes.FakeCollider(500.0));

        Vector3d to = new(0.0, 0.0, 0.0);
        player.GroundCollision(to, ref to, Tolerance);

        Assert.Equal(2, surroundings.World.Queries);
        Assert.True(player.Signal.HasFlag(Igi2PlayerSignal.Grounded));
        Assert.Equal(500.0, to.Z);
    }

    /// <summary>The slope of the step: rising one in one is a quarter turn's half, and its factor half of one.</summary>
    [Fact]
    public void The_step_measures_the_ground_angle()
    {
        (Igi2HumanPlayer player, Igi2PlayerFakes.FakeSurroundings surroundings) = Body();
        surroundings.World.Surfaces.Add(new Igi2PlayerFakes.FakeCollider(1000.0));

        Vector3d to = new(1000.0, 0.0, 1200.0);
        player.GroundCollision(new Vector3d(0.0, 0.0, 0.0), ref to, Tolerance);

        Assert.InRange(player.GroundAngle, 0.785f, 0.7854f);
        Assert.InRange(player.GroundAngleFactor, 0.4999f, 0.5001f);
    }

    /// <summary>Mission 1's spawn stands on its ground through the real collision world.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Mission_1s_spawn_stands()
    {
        Igi2Level level = Igi2Level.Find("location1", "level1");
        Assert.True(PlayerSpawn.TryFindGameplay(level.Root, out PlayerSpawnPoint spawn));
        Igi2CollisionWorld world = Igi2CollisionWorld.Build(level.Scene, level.ModelFor, level.Terrain(), "terrain");
        (Igi2HumanPlayer player, _) = Body(world);

        Vector3d to = spawn.Position;
        player.GroundCollision(to, ref to, Tolerance);

        Assert.True(player.Signal.HasFlag(Igi2PlayerSignal.Grounded), $"the spawn is {player.DistanceToGround} above anything.");
        Assert.NotNull(player.GroundCollider);
        Assert.InRange(to.Z, spawn.Position.Z - Tolerance, spawn.Position.Z + 4096.0);
    }

    private static (Igi2HumanPlayer Player, Igi2PlayerFakes.FakeSurroundings Surroundings) Body(IIgi2CollisionQuery? world = null)
    {
        (Igi2HumanPlayer player, _, Igi2PlayerFakes.FakeSurroundings surroundings) = Igi2PlayerFakes.Player();
        if (world is not null)
        {
            surroundings.Query = world;
        }

        player.GroundCheckZOffset = 4096f;
        return (player, surroundings);
    }
}
