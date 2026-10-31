using System.Globalization;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Physics;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Game.Tests;

/// <summary>A thrown grenade flying as the original's physics object of its type, in the world the cars step in.</summary>
[Collection(RetailMissionWorld.Name)]
public sealed class RetailGrenadeBodyTests(ITestOutputHelper output)
{
    [RequiresGameDataFact]
    public void The_grenade_types_are_read_from_the_physics_scripts()
    {
        RetailMissionData data = RetailMissionData.Get(1);
        Assert.True(data.Vehicles.Types.TryFind("135_01_1", out PhysicsObjectType frag));
        Assert.Equal("Grenade", frag.TaskTypeName);
        Assert.Equal(0.3f, frag.Mass);
        Assert.Equal(new Vector3f(0.1f, 0.1f, 0.15f), frag.Size);
        Assert.Equal(5, frag.ContactPoints.Count);
        Assert.All(frag.ContactPoints, static point => Assert.Equal(0.3f, point.Bounce));
        Assert.Equal(3f, frag.RestSpeed);
        Assert.Equal(0.016f, frag.RestAngularVelocitySquared);
        Assert.True(data.Vehicles.Types.TryFind("137_01_1", out PhysicsObjectType flashbang));
        Assert.Equal("Grenade", flashbang.TaskTypeName);
    }

    [RequiresGameDataFact]
    public void A_thrown_grenade_lands_bounces_rests_and_goes_off_after_its_fuse()
    {
        RetailMissionData data = RetailMissionData.Get(1);
        RetailCarWorld world = new(data.TerrainRay, data.Objects);
        Assert.True(data.Vehicles.Types.TryFind("135_01_1", out PhysicsObjectType frag));
        MefModel? model = data.Mission.TryGetModel("135_01_1", out MefModel? found) ? found : null;
        GrenadeSimulation grenades = new()
        {
            World = () => world,
            Bodies = (name, at, velocity, frame) => RetailPhysicsObjectBody.Create(frag, model, at, frame, velocity),
        };

        // From the player's spawn, at chest height, lobbed forward at the shipped fifteen metres a second.
        Assert.True(PlayerSpawn.TryFindGameplay(data.Root, out PlayerSpawnPoint spawn));
        Vector3d from = new(spawn.Position.X, spawn.Position.Y, spawn.Position.Z + (1.5 * 4096.0));
        Vector3f direction = new Vector3f(MathF.Sin(-spawn.Gamma), MathF.Cos(-spawn.Gamma), 0.5f).Normalized();
        Assert.True(grenades.Spawn(from, direction, 15f, GrenadeExplosionType.Normal, fuseTicks: 120));
        LiveGrenade grenade = Assert.Single(grenades.Grenades);
        Assert.NotNull(grenade.Body);

        int bounces = 0;
        int wentOff = -1;
        for (int tick = 1; tick <= 200 && wentOff < 0; tick++)
        {
            grenades.Tick(null);
            bounces += grenades.Bounces.Count;
            if (grenades.Detonations.Count > 0)
            {
                wentOff = tick;
            }
        }

        // 0x476AC0 moves it while it has fuse, a tick for each, and sets it off the tick after.
        Assert.Equal(121, wentOff);
        Assert.InRange(bounces, 1, 10);
        GrenadeDetonation blast = Assert.Single(grenades.Detonations);

        // It came to rest on the ground: straight down from a metre above, the ground is within a hand's breadth.
        Vector3d above = new(blast.Position.X, blast.Position.Y, blast.Position.Z + 4096.0);
        Span<RetailRayResult> hit = new RetailRayResult[1];
        world.CastRays(null!, [above], [new Vector3d(above.X, above.Y, above.Z - 8192.0)], hit);
        Assert.True(hit[0].Hit);
        double ground = above.Z - hit[0].Distance;
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"thrown from {from.Z / 4096:F2} m, went off at tick {wentOff} after {bounces} bounce(s), {(blast.Position.Z - ground) / 4096:F3} m over the ground, {(blast.Position - from).Length / 4096:F1} m away"));
        Assert.InRange(blast.Position.Z - ground, -0.05 * 4096.0, 0.3 * 4096.0);
        Assert.True(grenade.Velocity.Length < 41f, "it rests");
    }
}
