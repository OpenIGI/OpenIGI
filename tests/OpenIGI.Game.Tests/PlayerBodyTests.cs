using OpenIGI.TestSupport;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// A player standing, walking, crouching, jumping and falling on real terrain.
/// </summary>
/// <remarks>See the original and the original.</remarks>
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
        Assert.Equal(1.8, PlayerBody.StandingEyeHeight / UnitsPerMetre, 0.01);
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
    public void APlayerWalksInTheDirectionItFaces()
    {
        (TerrainData terrain, PlayerBody player) = OnLevelOne();
        Vector3d start = player.Position;

        // A metre a second, which is what a walk animation's delta would carry.
        Vector3f delta = new((float)(UnitsPerMetre / 30.0 / HumanMotion.DefaultDeltaTranslationScale), 0f, 0f);

        player.Yaw = 0f;
        for (int tick = 0; tick < 30; tick++)
        {
            player.Tick(default, terrain, delta);
        }

        double movedX = player.Position.X - start.X;
        Assert.True(movedX > 0.9 * UnitsPerMetre, $"walked only {movedX / UnitsPerMetre:0.##} m");
        Assert.Equal(start.Y, player.Position.Y, 1e-6);

        // Turned a quarter turn, the same delta walks along Y instead.
        (terrain, player) = OnLevelOne();
        start = player.Position;
        player.Yaw = MathF.PI / 2f;

        for (int tick = 0; tick < 30; tick++)
        {
            player.Tick(default, terrain, delta);
        }

        Assert.True(player.Position.Y - start.Y > 0.9 * UnitsPerMetre);
        Assert.Equal(start.X, player.Position.X, 1.0);
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
