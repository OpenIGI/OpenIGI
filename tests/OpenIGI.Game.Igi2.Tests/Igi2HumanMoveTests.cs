using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Player;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The integrator, <c>Human_Move</c> (<c>igi2 0x5015F0</c>).</summary>
public sealed class Igi2HumanMoveTests
{
    /// <summary>On flat ground a step goes where the speed says, and the speeds the move measures follow it.</summary>
    [Fact]
    public void A_step_on_flat_ground_goes_where_the_speed_says()
    {
        (Igi2HumanPlayer player, _) = OnFloor();
        player.Speed = new Vector3f(100f, -50f, 0f);
        player.TotalSpeed = new Vector3f(10f, 0f, 0f);

        player.Move();

        Assert.Equal(new Vector3d(100.0, -50.0, 0.0), player.Position);
        Assert.Equal(new Vector3f(100f, -50f, 0f), player.TotalSpeed);
        Assert.Equal(new Vector3f(90f, -50f, 0f), player.Acceleration);
        Assert.Equal(new Vector3f(100f, -50f, 0f), player.WorldSpeed);
        Assert.Equal(new Vector3f(100f, -50f, 0f), player.AccumulatedSpeed);
    }

    /// <summary>
    /// The animation's step is added once and then cleared. Even on flat ground it is first multiplied by the sine of a
    /// quarter turn truncated to 24 bits, which is a hair under one, so thirty units is a step of 29.999998.
    /// </summary>
    [Fact]
    public void The_animation_step_is_spent()
    {
        (Igi2HumanPlayer player, _) = OnFloor();
        player.DeltaTranslation = new Vector3f(30f, 0f, 0f);

        player.Move();

        Assert.Equal(29.999998f, (float)player.Position.X);
        Assert.Equal(default, player.DeltaTranslation);
    }

    /// <summary>An impact decays to nine tenths each move, and pushes the body by what is left.</summary>
    [Fact]
    public void An_impact_decays_by_a_tenth()
    {
        (Igi2HumanPlayer player, _) = OnFloor();
        player.ImpactSpeed = new Vector3f(100f, 0f, 0f);

        player.Move();

        Assert.Equal(new Vector3f(89.999992f, 0f, 0f), player.ImpactSpeed);
        Assert.Equal(89.999992f, (float)player.Position.X);
    }

    /// <summary>A slope steeper than fifty degrees sets an ideal slide, and the slide eases a tenth of the way toward it.</summary>
    [Fact]
    public void A_steep_slope_starts_a_slide()
    {
        (Igi2HumanPlayer player, _) = OnFloor();
        player.GroundIntersection.Normal = new Vector3f(0.6f, 0f, 0.6f);

        player.Move();

        Assert.True(player.IdealSlidingSpeed.X > 0f);
        Assert.True(player.IdealSlidingSpeed.Z < 0f && player.IdealSlidingSpeed.Z >= -409.6f);
        Assert.InRange(player.SlidingSpeed.X, player.IdealSlidingSpeed.X * 0.0999f, player.IdealSlidingSpeed.X * 0.1001f);
    }

    /// <summary>A conveyor's push moves the body and protects it from fall damage while it lasts.</summary>
    [Fact]
    public void A_conveyor_push_carries_the_body()
    {
        (Igi2HumanPlayer player, Igi2PlayerFakes.FakeSurroundings surroundings) = OnFloor();
        surroundings.Conveyor = new Vector3f(25f, 0f, 0f);

        player.Move();

        Assert.Equal(25.0, player.Position.X);
        Assert.True(player.IsProtectedFromFallDamage);
    }

    /// <summary>Standing still on a building floor in mission 1, the body stays where it is tick after tick.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Standing_on_a_rigid_floor_stays_put()
    {
        Igi2Level level = Igi2Level.Find("location1", "level1");
        Igi2CollisionWorld world = Igi2CollisionWorld.Build(level.Scene, level.ModelFor, level.Terrain(), "terrain");
        (Igi2HumanPlayer player, Igi2PlayerFakes.FakeSurroundings surroundings) = Body();
        surroundings.Query = world;
        player.IsObjectCollision = true;

        // Somewhere a body dropped from eight metres above a building's middle lands on the building itself.
        Vector3d feet = default;
        foreach (Igi2RigidObject building in world.Objects.Where(static o => o.Owner is Igi2CollisionOwner { TypeName: "Building", Depth: 0 }))
        {
            Vector3d above = new(building.Position.X, building.Position.Y, building.Position.Z + 32768.0);
            feet = above;
            player.GroundCollision(above, ref feet, 65536f);
            if (player.IsGroundRigidObject)
            {
                break;
            }
        }

        Assert.True(player.IsGroundRigidObject, "no building in mission 1 has a floor under its middle.");
        player.Position = feet;
        player.GroundTolerance = 819.2f;
        player.UpdatePlatformPosition(forcePosition: true, forceOrientation: true);

        for (int tick = 0; tick < 30; tick++)
        {
            player.Speed = default;
            player.Move();
        }

        Assert.InRange(player.Position.X, feet.X - 1.0, feet.X + 1.0);
        Assert.InRange(player.Position.Y, feet.Y - 1.0, feet.Y + 1.0);
        Assert.InRange(player.Position.Z, feet.Z - 1.0, feet.Z + 1.0);
        Assert.True(player.IsGroundRigidObject);
        Assert.Equal(default, player.GroundSpeed);
    }

    private static (Igi2HumanPlayer Player, Igi2PlayerFakes.FakeSurroundings Surroundings) OnFloor()
    {
        (Igi2HumanPlayer player, Igi2PlayerFakes.FakeSurroundings surroundings) = Body();
        surroundings.World.Surfaces.Add(new Igi2PlayerFakes.FakeCollider(0.0));
        Vector3d feet = default;
        player.GroundCollision(feet, ref feet, 819.2f);
        return (player, surroundings);
    }

    private static (Igi2HumanPlayer Player, Igi2PlayerFakes.FakeSurroundings Surroundings) Body()
    {
        (Igi2HumanPlayer player, _, Igi2PlayerFakes.FakeSurroundings surroundings) = Igi2PlayerFakes.Player();
        player.GroundCheckZOffset = 4096f;
        return (player, surroundings);
    }
}
