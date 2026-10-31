using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The steep slope below mission 9's cliff, where a player who walked off the top went through the ground and fell under the
/// level: the slope's triangle belongs to the octree leaf above, hanging below that leaf's cell, and a ground query begun at
/// the feet started in the cell beneath and never met it.
/// </summary>
public sealed class MissionNineSlopeGroundTests
{
    // A place on the slope, in world units: where a player stood when it went under.
    private const double SlopeX = 22812389;
    private const double SlopeY = -50767536;
    private const double SlopeZ = 174696636;

    [RequiresGameDataFact]
    public void A_ground_drop_begun_at_the_feet_meets_the_slope_hanging_from_the_leaf_above()
    {
        TerrainData terrain = MissionNine();
        double x = SlopeX + (2.4 * WorldScene.UnitsPerMetre);
        Assert.True(TerrainHeightQuery.TryGetGround(terrain, x, SlopeY, SlopeZ - (200 * WorldScene.UnitsPerMetre), 400 * WorldScene.UnitsPerMetre, out TerrainGround surface));

        // From just over the surface, where a body's ground probe is: the drop starts four metres up, in the leaf the
        // triangle belongs to.
        Assert.True(terrain.TryDrop(x, SlopeY, surface.Height + 1638.4, HumanGroundQuery.GroundDropUnits, upward: false, out TerrainGround dropped));
        Assert.Equal(surface.Height, dropped.Height, 1);

        // And from under it, by the drop's way back up.
        Assert.True(terrain.TryDrop(x, SlopeY, surface.Height - (3 * WorldScene.UnitsPerMetre), HumanGroundQuery.GroundDropUnits, upward: true, out TerrainGround raised));
        Assert.Equal(surface.Height, raised.Height, 1);
    }

    [RequiresGameDataTheory]
    [InlineData(2.0f)]
    [InlineData(5.5f)]
    public void A_body_walking_off_the_slope_every_way_stays_on_it(float metresPerSecond)
    {
        TerrainData terrain = MissionNine();
        const double m = WorldScene.UnitsPerMetre;
        for (int direction = 0; direction < 16; direction++)
        {
            PlayerBody player = new() { Position = new Vector3d(SlopeX, SlopeY, SlopeZ) };
            Assert.True(player.PlaceOnGround(terrain));
            player.Yaw = direction * MathF.Tau / 16f;
            Vector3f step = new(0f, (float)(m * metresPerSecond / 30.0 / HumanMotion.DefaultDeltaTranslationScale), 0f);
            for (int tick = 0; tick < 150; tick++)
            {
                player.Tick(default, terrain, step);
                Assert.True(
                    TerrainHeightQuery.TryGetGround(terrain, player.Position.X, player.Position.Y, player.Position.Z - (300 * m), 600 * m, out TerrainGround surface),
                    $"heading {direction * 22.5f}: no ground at all under the body on tick {tick}");
                Assert.True(
                    player.Position.Z >= surface.Height - (0.5 * m),
                    $"heading {direction * 22.5f}: the body went {(surface.Height - player.Position.Z) / m:0.00} m under the slope on tick {tick}");
            }
        }
    }

    private static TerrainData MissionNine()
    {
        (TerrainData terrain, _, _) = RetailTerrainHarness.Create().LoadMission(9);
        return terrain;
    }
}
