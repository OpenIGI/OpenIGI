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

/// <summary>A missile flying as the original's physics object of its type, in the world the cars step in.</summary>
[Collection(RetailMissionWorld.Name)]
public sealed class RetailMissileBodyTests(ITestOutputHelper output)
{
    [RequiresGameDataFact]
    public void The_missile_types_are_read_from_the_physics_scripts()
    {
        RetailMissionData data = RetailMissionData.Get(1);
        Assert.True(data.Vehicles.Types.TryFind("110_02_1", out PhysicsObjectType rpg));
        Assert.Equal("Missile", rpg.TaskTypeName);
        MissileTypeDefinition missile = Assert.IsType<MissileTypeDefinition>(MissileTypeDefinition.Of<X87>(rpg));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"RPG-18: guidance {missile.Guidance}, smoke {missile.Smoke}, fuel {missile.Fuel}, burn {missile.Burn}, thrust {missile.Vehicle.MaxThrust}, wings {missile.Vehicle.Wings.Count}"));
        Assert.Equal((int)MissileType.MissileDirect, missile.Guidance);
        Assert.Equal(4, missile.Vehicle.Wings.Count);
        Assert.Equal(200f, missile.Vehicle.MaxThrust);

        Assert.True(data.Vehicles.Types.TryFind("700_10_1", out PhysicsObjectType hind));
        Assert.Equal((int)MissileType.HindHelicopter, MissileTypeDefinition.Of<X87>(hind)!.Guidance);
    }

    [RequiresGameDataFact]
    public void A_fired_missile_speeds_up_under_its_motor_and_goes_off_where_it_strikes()
    {
        RetailMissionData data = RetailMissionData.Get(1);
        RetailCarWorld world = new(data.TerrainRay, data.Objects);
        Assert.True(data.Vehicles.Types.TryFind("110_02_1", out PhysicsObjectType type));
        MissileTypeDefinition missile = MissileTypeDefinition.Of<X87>(type)!;
        MefModel? model = data.Mission.TryGetModel("110_02_1", out MefModel? found) ? found : null;
        MissileSimulation missiles = new()
        {
            World = () => world,
            Bodies = (name, at, frame, speed, target) =>
                RetailMissileBody.Fire(type, missile, model, at, frame, speed, target, null, new MissileRandom().Random),
        };

        // From the player's spawn, at shoulder height, a little down at the ground forty metres off, at the launcher's five metres a
        // second.
        Assert.True(PlayerSpawn.TryFindGameplay(data.Root, out PlayerSpawnPoint spawn));
        Vector3d from = new(spawn.Position.X, spawn.Position.Y, spawn.Position.Z + (1.5 * 4096.0));
        Vector3f direction = new Vector3f(MathF.Sin(-spawn.Gamma), MathF.Cos(-spawn.Gamma), -0.04f).Normalized();
        Assert.True(missiles.Spawn(from, from + new Vector3d(direction.X * 409600.0, direction.Y * 409600.0, direction.Z * 409600.0),
            MissileType.MissileDirect, 5f, direction, "110_02_1", launcherMetresPerSecond: 5f));
        LiveMissile flying = Assert.Single(missiles.Missiles);
        Assert.NotNull(flying.Body);
        float launch = flying.Velocity.Length;

        int wentOff = -1;
        float fastest = 0f;
        for (int tick = 1; tick <= 300 && wentOff < 0; tick++)
        {
            missiles.TickWithMissileContext(null, null);
            fastest = Math.Max(fastest, flying.Velocity.Length);
            if (missiles.Detonations.Count > 0)
            {
                wentOff = tick;
            }
        }

        MissileDetonation blast = Assert.Single(missiles.Detonations);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"left at {launch / 136.53f:F1} m/s, reached {fastest / 136.53f:F1} m/s, went off at tick {wentOff}, {(blast.Position - from).Length / 4096:F1} m away"));
        Assert.True(fastest > launch * 5f, "its motor pushes it far past the launcher's speed");
        Assert.InRange(wentOff, 2, 300);
        Assert.Equal(MissileExplosion.RadiusUnits, blast.RadiusUnits);
    }
}
