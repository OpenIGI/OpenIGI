using OpenIGI.Core.Graphics;
using OpenIGI.TestSupport;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// A player standing, walking, crouching, jumping and falling on real terrain.
/// </summary>
public sealed class PlayerBodyTests
{
    private const double UnitsPerMetre = 4096.0;

    [RequiresGameDataFact]
    public void APlayerCanBePlacedOnLevelOnesGround()
    {
        (TerrainData terrain, PlayerBody player) = OnLevelOne();

        Assert.True(player.IsGrounded);
        Assert.True(
            TerrainHeightQuery.TryGetGround(
                terrain, player.Position.X, player.Position.Y,
                player.Position.Z + HumanGroundQuery.ProbeOriginStandingInUnits,
                out TerrainGround ground));

        Assert.Equal(ground.Height, player.Position.Z, 1e-6);

        // And the eye is at head height above the feet, not at the feet.
        Assert.Equal(player.Position.Z + PlayerBody.StandingEyeHeight, player.Eye.Z, 1e-6);
        Assert.Equal(1.637, PlayerBody.StandingEyeHeight / UnitsPerMetre, 0.001);
    }

    [RequiresGameDataFact]
    public void AStandingPlayerStaysPutAndStaysGrounded()
    {
        (TerrainData terrain, PlayerBody player) = OnLevelOne();
        Vector3d start = player.Position;

        for (int tick = 0; tick < 90; tick++)
        {
            player.Tick(default, terrain, default);
        }

        Assert.True(player.IsGrounded, "three seconds of standing still should not fall through");
        Assert.Equal(start.X, player.Position.X);
        Assert.Equal(start.Y, player.Position.Y);
        Assert.Equal(start.Z, player.Position.Z, 1e-6);
    }

    [RequiresGameDataFact]
    public void APlayerWalksAlongTheAxisItIsLookingDown()
    {
        // The check that stops the walk drifting away from the view again: the step the body takes is
        // compared against the camera's OWN forward axis, not against a hand-written expectation. If
        // the facing basis is ever transposed or the delta's axes swapped, this fails.
        Vector3f delta = new(0f, (float)(UnitsPerMetre / 30.0 / HumanMotion.DefaultDeltaTranslationScale), 0f);

        foreach (float yaw in new[] { 0f, MathF.PI / 2f, 2.3f, -1.1f })
        {
            (TerrainData terrain, PlayerBody player) = OnLevelOne();
            player.Yaw = yaw;
            Vector3d start = player.Position;

            for (int tick = 0; tick < 30; tick++)
            {
                player.Tick(default, terrain, delta);
            }

            double movedX = player.Position.X - start.X;
            double movedY = player.Position.Y - start.Y;
            double distance = Math.Sqrt((movedX * movedX) + (movedY * movedY));

            Assert.True(distance > 0.9 * UnitsPerMetre, $"yaw {yaw}: walked only {distance / UnitsPerMetre:0.##} m");

            // The camera's forward at this yaw, flattened.
            Matrix3f view = Camera3D.LookRotation(yaw, 0f);
            Vector3f forward = view.Row3;

            double dot = ((movedX / distance) * forward.X) + ((movedY / distance) * forward.Y);
            Assert.True(dot > 0.999, $"yaw {yaw}: walked {dot:0.###} along the way it was looking");
        }
    }

    [RequiresGameDataFact]
    public void StrafingGoesSidewaysNotForward()
    {
        (TerrainData terrain, PlayerBody player) = OnLevelOne();
        player.Yaw = 0f;
        Vector3d start = player.Position;

        // X is the sideways axis; at yaw zero the view looks along +Y, so strafing must move along X.
        Vector3f delta = new((float)(UnitsPerMetre / 30.0 / HumanMotion.DefaultDeltaTranslationScale), 0f, 0f);

        for (int tick = 0; tick < 30; tick++)
        {
            player.Tick(default, terrain, delta);
        }

        Assert.True(player.Position.X - start.X > 0.9 * UnitsPerMetre);
        Assert.Equal(start.Y, player.Position.Y, 1.0);
    }

    [RequiresGameDataFact]
    public void APlayerFollowsTheGroundRatherThanWalkingThroughIt()
    {
        // The point of having a real height query: over 60 ticks of walking the player stays on the
        // surface, whatever the surface does.
        (TerrainData terrain, PlayerBody player) = OnLevelOne();
        Vector3f delta = new((float)(UnitsPerMetre / 30.0), 0f, 0f);

        for (int tick = 0; tick < 60; tick++)
        {
            player.Tick(default, terrain, delta);

            if (!player.IsGrounded)
            {
                continue;
            }

            Assert.True(
                TerrainHeightQuery.TryGetGround(
                    terrain, player.Position.X, player.Position.Y,
                    player.Position.Z + HumanGroundQuery.ProbeOriginStandingInUnits,
                    out TerrainGround ground));

            Assert.Equal(ground.Height, player.Position.Z, 1e-6);
        }
    }

    [RequiresGameDataFact]
    public void APlayerStandsOnAPlacedObjectAboveTheTerrain()
    {
        // The object half of the ground query (IGI2 Human_GroundCollision's World_GetNextZSpecificMesh,
        // IGI1 sub_4CBE40 -> sub_4CDB20): a downward probe that meets a placed surface above the terrain
        // stands the body on it. Before this, a crate top, a ramp or a building floor was ignored and the
        // body sank to the terrain beneath.
        (TerrainData terrain, PlayerBody player) = OnLevelOne();
        double terrainZ = player.Position.Z;
        double platformZ = terrainZ + 500.0;

        // A synthetic platform: any downward probe through the body's column meets a flat, upward-facing
        // surface 500 units above the terrain.
        player.Walls = (Vector3d origin, Vector3d end, out WorldRayHit hit) =>
        {
            hit = default;
            if (origin.Z < platformZ || end.Z > platformZ)
            {
                return false;
            }

            hit = new WorldRayHit(
                new Vector3d(origin.X, origin.Y, platformZ),
                new Vector3f(0f, 0f, 1f),
                origin.Z - platformZ);
            return true;
        };

        player.Tick(default, terrain, default);

        Assert.True(player.IsGrounded, "a body over a placed floor should stand on it, not fall through");
        Assert.Equal(platformZ, player.Position.Z, 1e-6);
    }

    [Fact]
    public void APlayerCanBePlacedAndRemainGroundedOnAnObjectWithoutTerrain()
    {
        const double floorZ = 1000.0;
        PlayerBody player = new()
        {
            Position = new Vector3d(2000.0, 3000.0, 10000.0),
        };
        player.Walls = (Vector3d origin, Vector3d end, out WorldRayHit hit) =>
        {
            hit = default;
            if (end.Z >= origin.Z || origin.Z < floorZ || end.Z > floorZ)
            {
                return false;
            }

            hit = new WorldRayHit(
                new Vector3d(origin.X, origin.Y, floorZ),
                new Vector3f(0f, 0f, 1f),
                origin.Z - floorZ);
            return true;
        };

        Assert.True(player.PlaceOnGround(terrain: null));
        Assert.True(player.IsGrounded);
        Assert.Equal(floorZ, player.Position.Z);

        player.Tick(default, terrain: null, rootDelta: default);

        Assert.True(player.IsGrounded);
        Assert.Equal(floorZ, player.Position.Z);
    }

    [RequiresGameDataFact]
    public void InteractionSnapFindsThePlacedFloorBelowAnAuthoredRoot()
    {
        (TerrainData terrain, PlayerBody player) = OnLevelOne();
        double terrainZ = player.Position.Z;
        double platformZ = terrainZ + (5.0 * UnitsPerMetre);
        player.Position = new Vector3d(
            player.Position.X,
            player.Position.Y,
            terrainZ + (10.0 * UnitsPerMetre));
        player.Walls = (Vector3d origin, Vector3d end, out WorldRayHit hit) =>
        {
            hit = default;
            if (origin.Z < platformZ || end.Z > platformZ)
            {
                return false;
            }

            hit = new WorldRayHit(
                new Vector3d(origin.X, origin.Y, platformZ),
                new Vector3f(0f, 0f, 1f),
                origin.Z - platformZ);
            return true;
        };

        Assert.True(player.SnapInteractionToGround(terrain));
        Assert.True(player.IsGrounded);
        Assert.Equal(platformZ, player.Position.Z, 1e-6);
    }

    [RequiresGameDataFact]
    public void WalkingOffAPlatformCarriesTheLastGroundRootMotionIntoTheFall()
    {
        (TerrainData terrain, PlayerBody player) = OnLevelOne();
        double terrainZ = player.Position.Z;
        double platformZ = terrainZ + (4.0 * UnitsPerMetre);
        double edgeX = player.Position.X + 200.0;

        // A high shelf ending 200 units ahead. Horizontal wall probes cannot hit it; only the downward
        // ground query can, and only while the destination remains on the shelf side of the edge.
        player.Walls = (Vector3d origin, Vector3d end, out WorldRayHit hit) =>
        {
            hit = default;
            if (end.Z >= origin.Z || origin.X > edgeX || origin.Z < platformZ || end.Z > platformZ)
            {
                return false;
            }

            hit = new WorldRayHit(
                new Vector3d(origin.X, origin.Y, platformZ),
                new Vector3f(0f, 0f, 1f),
                origin.Z - platformZ);
            return true;
        };

        player.Position = new Vector3d(player.Position.X, player.Position.Y, platformZ);
        player.Tick(default, terrain, default);
        Assert.True(player.IsGrounded);

        Vector3d onShelf = player.Position;
        Vector3f authoredGroundDelta = new(300f, 0f, 0f);
        float carriedX = authoredGroundDelta.X * HumanMotion.DefaultDeltaTranslationScale;

        // This tick spends the animation delta crossing the lip and clears the ground bit.
        player.Tick(default, terrain, authoredGroundDelta);
        Assert.False(player.IsGrounded);
        Assert.Equal(onShelf.X + carriedX, player.Position.X, 1e-3);

        Vector3d overLip = player.Position;

        // On the following tick retail state 29 copies that prior root delta into velocity and returns:
        // horizontal motion continues, while gravity deliberately has not run on the entry tick.
        player.Tick(default, terrain, default);
        Assert.Equal(carriedX, player.Velocity.X, 1e-3f);
        Assert.Equal(0f, player.Velocity.Y, 1e-3f);
        Assert.Equal(0f, player.Velocity.Z, 1e-3f);
        Assert.Equal(overLip.X + carriedX, player.Position.X, 1e-3);
        Assert.Equal(overLip.Z, player.Position.Z, 1e-6);

        // The airborne per-tick starts after that entry tick: gravity now bites, while X keeps carrying.
        Vector3d enteredFall = player.Position;
        player.Tick(default, terrain, default);
        Assert.Equal(carriedX, player.Velocity.X, 1e-3f);
        Assert.Equal(-HumanMotion.GravityPerTick, player.Velocity.Z, 1e-3f);
        Assert.Equal(enteredFall.X + carriedX, player.Position.X, 1e-3);
        Assert.True(player.Position.Z < enteredFall.Z);
    }

    [RequiresGameDataFact]
    public void APlayerJumpsAndComesBackDown()
    {
        (TerrainData terrain, PlayerBody player) = OnLevelOne();
        double floor = player.Position.Z;

        player.Tick(new PlayerCommand(0f, 0f, 0f, 0f, false, false, Jump: true), terrain, default);
        Assert.False(player.IsGrounded);

        double apex = player.Position.Z;
        int ticks = 0;
        while (!player.IsGrounded && ticks < 300)
        {
            player.Tick(default, terrain, default);
            apex = Math.Max(apex, player.Position.Z);
            ticks++;
        }

        Assert.True(player.IsGrounded, "the player never landed");
        Assert.Equal(floor, player.Position.Z, 1e-6);
        Assert.True(apex > floor + UnitsPerMetre, "the jump did not clear a metre");
        Assert.InRange(ticks / 30.0, 0.5, 2.5);
    }

    [RequiresGameDataFact]
    public void APlayerCrouchesAndStandsBackUp()
    {
        (TerrainData terrain, PlayerBody player) = OnLevelOne();
        Assert.Equal(PlayerBody.StandingEyeHeight, player.EyeHeight);

        PlayerCommand crouch = new(0f, 0f, 0f, 0f, false, Crouch: true, false);
        for (int tick = 0; tick < 60; tick++)
        {
            player.Tick(crouch, terrain, default);
        }

        Assert.True(player.IsCrouched);
        Assert.Equal(PlayerBody.CrouchingEyeHeight, player.EyeHeight, 1e-6);
        Assert.True(player.Eye.Z < player.Position.Z + PlayerBody.StandingEyeHeight);

        for (int tick = 0; tick < 60; tick++)
        {
            player.Tick(default, terrain, default);
        }

        Assert.False(player.IsCrouched);
        Assert.Equal(PlayerBody.StandingEyeHeight, player.EyeHeight, 1e-6);
    }

    [RequiresGameDataFact]
    public void LookingIsClampedAndDoesNotTurnTheBody()
    {
        (TerrainData terrain, PlayerBody player) = OnLevelOne();
        Vector3d start = player.Position;

        PlayerCommand lookUp = new(0f, 0f, 0f, 0.5f, false, false, false);
        for (int tick = 0; tick < 60; tick++)
        {
            player.Tick(lookUp, terrain, default);
        }

        Assert.Equal(PlayerBody.PitchLimit, player.Pitch, 1e-5f);

        // Looking about does not move the player, and pitch does not tilt the walk.
        Assert.Equal(start.X, player.Position.X);
        Assert.Equal(start.Y, player.Position.Y);

        Vector3f delta = new(1000f, 0f, 0f);
        player.Tick(default, terrain, delta);
        Assert.Equal(start.Z, player.Position.Z, 1e-6);
    }

    [RequiresGameDataFact]
    public void APlayerDroppedFromHeightFallsAndLands()
    {
        (TerrainData terrain, PlayerBody player) = OnLevelOne();
        double floor = player.Position.Z;

        player.Position = new Vector3d(player.Position.X, player.Position.Y, floor + (3.0 * UnitsPerMetre));

        int ticks = 0;
        while (!player.IsGrounded && ticks < 600)
        {
            player.Tick(default, terrain, default);
            ticks++;
        }

        Assert.True(player.IsGrounded, "the player never landed");

        // It lands on ground at or below where it was dropped. Not necessarily the floor it started
        // on: terrain between the two is real geometry and catching on it is correct behaviour, not a
        // failure. What matters is that it stops on a surface the query agrees is there.
        Assert.True(player.Position.Z <= floor + (3.0 * UnitsPerMetre));

        // Not necessarily the floor it started on. Terrain between the two is real geometry and
        // catching on it is correct, and the query keeps the LOWEST surface within its reach rather
        // than the first one below the start - so a probe from a different height can legitimately
        // answer with a different surface. What matters is that it stopped and is standing.
        Assert.False(float.IsNaN(player.Velocity.Z));
        Assert.Equal(0f, player.Velocity.Z);
    }

    [RequiresGameDataFact]
    public void ALadderSlideUsesItsOwnDampedGravityAndStopsOnGround()
    {
        (TerrainData terrain, PlayerBody player) = OnLevelOne();
        double floor = player.Position.Z;
        player.Position = new Vector3d(player.Position.X, player.Position.Y, floor + UnitsPerMetre);
        player.BeginLadderTraversal();

        Assert.False(player.TickLadderSlide(terrain, default));
        Assert.Equal(
            -HumanMotion.LadderSlideGravityPerTick * HumanMotion.LadderSlideDragPerTick,
            player.Velocity.Z,
            1e-3f);

        int ticks = 1;
        while (!player.IsGrounded && ticks < 300)
        {
            player.TickLadderSlide(terrain, default);
            ticks++;
        }

        Assert.True(player.IsGrounded);
        Assert.Equal(floor, player.Position.Z, 1e-6);
        Assert.Equal(0f, player.Velocity.Z);
    }

    [RequiresGameDataFact]
    public void ARunningJumpCarriesTheDirectionItTookOffIn()
    {
        // The original takes the direction you are already moving, normalises it, and gives it a fixed
        // horizontal speed. You do not steer in mid-air - you carry the momentum you had.
        (TerrainData terrain, PlayerBody player) = OnLevelOne();
        player.Yaw = 0f;
        Vector3d start = player.Position;

        Vector3f delta = new(0f, 300f, 0f);
        PlayerCommand run = new(1f, 0f, 0f, 0f, false, false, Jump: false);
        PlayerCommand jump = new(1f, 0f, 0f, 0f, false, false, Jump: true);

        // Run for a tick BEFORE jumping, or there is no run to carry. The take-off reads the PREVIOUS
        // tick's step, because 0x4109E7 runs the locomotion state machine before the root motion is
        // sampled at 0x410B7E -- so state 28's entry (0x40BA70) reads what last tick left in
        // Human+1312/+1316. Taking off on the body's very first tick would read a zero step and hop on
        // the spot, which is the standing jump AStandingJumpGoesStraightUp already covers.
        player.Tick(run, terrain, delta);
        Assert.True(player.IsGrounded);

        player.Tick(jump, terrain, delta);
        Assert.False(player.IsGrounded);
        Assert.True(player.TookOffThisTick);
        Assert.Equal("jump_1", PlayerBody.JumpSoundName);

        // The horizontal velocity is the take-off speed, not the animation's. The airborne integrator
        // has no damping, so it survives the tick unchanged.
        double takeOff = 17.5 * HumanPlayerParameters.UnitsPerTickPerKilometrePerHour;
        double speed = Math.Sqrt((player.Velocity.X * player.Velocity.X) + (player.Velocity.Y * player.Velocity.Y));
        Assert.Equal(takeOff, speed, 1.0);

        // 17.5 km/h is 4.86 m/s, which is all but the run animation's own speed: a running jump keeps
        // the pace you were going.
        Assert.Equal(4.86, takeOff * 30.0 / UnitsPerMetre, 0.01);

        // And it goes the way the step went, at yaw zero that is +Y.
        Assert.True(player.Velocity.Y > 0f);
        Assert.Equal(0f, player.Velocity.X, 1e-3f);

        // Releasing the keys mid-air changes nothing: the momentum carries.
        int ticks = 0;
        while (!player.IsGrounded && ticks < 300)
        {
            player.Tick(default, terrain, default);
            ticks++;
        }

        Assert.True(player.Position.Y - start.Y > 1.0 * UnitsPerMetre, "the jump should carry forward");
    }

    [RequiresGameDataFact]
    public void AStandingJumpGoesStraightUp()
    {
        (TerrainData terrain, PlayerBody player) = OnLevelOne();
        Vector3d start = player.Position;

        player.Tick(new PlayerCommand(0f, 0f, 0f, 0f, false, false, Jump: true), terrain, default);

        Assert.Equal(0f, player.Velocity.X);
        Assert.Equal(0f, player.Velocity.Y);
        Assert.True(player.Velocity.Z > 0f);

        while (!player.IsGrounded)
        {
            player.Tick(default, terrain, default);
        }

        Assert.Equal(start.X, player.Position.X, 1e-6);
        Assert.Equal(start.Y, player.Position.Y, 1e-6);
    }

    [RequiresGameDataFact]
    public void GravityDoesNotBiteOnTheTickTheJumpLeavesTheGround()
    {
        // The jump entry (0x40BA70) installs the airborne per-tick in the state slot and returns - it
        // does not call it, unlike every transition inside 0x40BBD0, which sets the slot and invokes it
        // in the same breath. So the launch tick is the take-off velocity clean.
        (TerrainData terrain, PlayerBody player) = OnLevelOne();

        player.Tick(new PlayerCommand(0f, 0f, 0f, 0f, false, false, Jump: true), terrain, default);

        Assert.False(player.IsGrounded);
        Assert.Equal(player.JumpSpeed, player.Velocity.Z, 1e-3);

        // And on the next tick it does.
        player.Tick(default, terrain, default);
        Assert.Equal(player.JumpSpeed - HumanMotion.GravityPerTick, player.Velocity.Z, 1e-3);
    }

    [RequiresGameDataFact]
    public void HoldingADirectionThroughAJumpLeansItWithoutSteeringIt()
    {
        // Air control accumulates into the velocity rather than replacing it, so it is an acceleration:
        // it bends the arc by something over a metre across a jump but cannot turn it round.
        (TerrainData terrain, PlayerBody player) = OnLevelOne();
        player.Yaw = 0f;

        Vector3d start = player.Position;
        player.Tick(new PlayerCommand(0f, 0f, 0f, 0f, false, false, Jump: true), terrain, default);

        // Straight up, then lean forward for the whole flight.
        PlayerCommand lean = new(1f, 0f, 0f, 0f, false, false, Jump: false);
        int ticks = 0;
        while (!player.IsGrounded && ticks < 300)
        {
            player.Tick(lean, terrain, default);
            ticks++;
        }

        double travelled = (player.Position.Y - start.Y) / UnitsPerMetre;
        Assert.InRange(travelled, 0.5, 4.0);

        // The same jump with nothing held goes nowhere, which is what makes the above air control and
        // not the take-off carry.
        (TerrainData still, PlayerBody idle) = OnLevelOne();
        idle.Yaw = 0f;
        Vector3d from = idle.Position;
        idle.Tick(new PlayerCommand(0f, 0f, 0f, 0f, false, false, Jump: true), still, default);

        ticks = 0;
        while (!idle.IsGrounded && ticks < 300)
        {
            idle.Tick(default, still, default);
            ticks++;
        }

        Assert.Equal(from.Y, idle.Position.Y, 1e-6);
    }

    [RequiresGameDataFact]
    public void AJumpTakesTheInheritedVelocityWithItAndThenConsumesIt()
    {
        // Human+1832, added on all three axes at take-off and zeroed by 0x4632D0. It is zero for a body
        // standing on terrain, so this drives it by hand to show the term is carried rather than dropped.
        (TerrainData terrain, PlayerBody player) = OnLevelOne();
        player.Yaw = 0f;
        player.InheritedVelocity = new Vector3f(0f, 500f, 200f);

        player.Tick(new PlayerCommand(0f, 0f, 0f, 0f, false, false, Jump: true), terrain, default);

        Assert.Equal(500f, player.Velocity.Y, 1e-3f);
        Assert.Equal(player.JumpSpeed + 200.0, player.Velocity.Z, 1e-3);

        // Consumed, not carried: the next jump does not get it again.
        Assert.Equal(default, player.InheritedVelocity);
    }

    [RequiresGameDataFact]
    public void AHighFallCrossesTheTerrainSurfaceInsteadOfTunnellingBelowIt()
    {
        (TerrainData terrain, PlayerBody player) = OnLevelOne();
        Vector3d ground = player.Position;
        HumanFallImpact? published = null;
        HumanHealth displayedHealth = new(
            HumanHitZoneTable.Player,
            RetailPlayerParameters.Value.MaximumHealth);
        player.UseHealth(displayedHealth);
        player.SetFallImpactSink(impact => published = impact);

        // Fifty metres makes the final 30 Hz step much larger than the endpoint probe's 0.4 m origin.
        // Before the continuous terrain move test, that endpoint began below the triangle and the body
        // continued falling through the map forever.
        player.Position = new Vector3d(ground.X, ground.Y, ground.Z + (50.0 * UnitsPerMetre));
        player.Tick(default, terrain, default);
        Assert.False(player.IsGrounded);

        int ticks = 0;
        while (!player.IsGrounded && ticks < 300)
        {
            player.Tick(default, terrain, default);
            ticks++;
        }

        Assert.True(player.IsGrounded, "the falling body never recovered the terrain surface");
        Assert.Equal(ground.Z, player.Position.Z, 1.0);
        Assert.True(player.Position.Z >= ground.Z - 1.0, "the body settled beneath the terrain");

        // Retail's airborne state observes the ground flag and resolves the impact on the following
        // tick, after applying one last gravity step.
        player.Tick(default, terrain, default);

        Assert.NotNull(player.LastFallImpact);
        Assert.Equal("player_fall_3", player.LastFallImpact.Value.SoundName);
        Assert.Same(displayedHealth, player.Health);
        Assert.True(displayedHealth.IsDead);
        Assert.True(player.DamageFlash.IsActive);
        Assert.Equal(player.LastFallImpact, published);
    }

    [RequiresGameDataFact]
    public void TheAnimationAndTheVelocityBothMoveTheBodyAndAreAdded()
    {
        // Correction to an earlier reading. The two were implemented as either/or - animation on the
        // ground, velocity in the air - on the reasoning that applying both would make a jump travel at
        // twice its speed. The move-and-collide at 0x4614B0 opens by summing them:
        //
        //     v2 = velocity.x + rootDelta.x;   // +1636 and +1312
        //
        // so a jump does carry both, and the airborne animation's own travel is part of the arc.
        (TerrainData terrain, PlayerBody player) = OnLevelOne();
        player.Yaw = 0f;

        player.Tick(new PlayerCommand(0f, 0f, 0f, 0f, false, false, Jump: true), terrain, default);
        Assert.False(player.IsGrounded);

        Vector3d before = player.Position;
        float velocityY = player.Velocity.Y;

        const float delta = 5000f;
        player.Tick(default, terrain, new Vector3f(0f, delta, 0f));

        // At yaw 0 the body faces +Y, so the whole scaled delta lands on Y beside the velocity.
        double step = delta * HumanMotion.DefaultDeltaTranslationScale;
        Assert.Equal(before.Y + velocityY + step, player.Position.Y, 1.0);
    }

    private static (TerrainData Terrain, PlayerBody Player) OnLevelOne()
    {
        (TerrainData terrain, double x, double y, double z) = RetailLevelOneGround.Find();

        PlayerBody player = new()
        {
            Position = new Vector3d(x, y, z),
        };

        Assert.True(player.PlaceOnGround(terrain), "the chosen sample had no ground under it");
        return (terrain, player);
    }
}
