using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Igi2.Ai;
using OpenIGI.Game.Igi2.Ai.Runtime;
using OpenIGI.Game.Igi2.Physics;
using OpenIGI.Game.Igi2.Vehicles;
using OpenIGI.Game.Igi2.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Cars and their drivers (car.c, carai.c, wheel.c), on the cars of the campaign.</summary>
public sealed class RetailIgi2CarTests
{
    /// <summary>
    /// A car's physics type reads a vehicle's limits, then its wheels, the lowering of their contact points, whether they are attach
    /// objects, whether it runs on tracks, its engine's sound, where it explodes and what it leaves (<c>CarType_ParsePhysicsObjTypeHandler</c>).
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_car_type_reads_its_wheels_and_its_wreck()
    {
        Igi2Session session = Igi2Session.Start();
        Igi2PhysicsObjectTypes types = session.Require<Igi2PhysicsObjectTypes>();
        Assert.True(types.TryFind("661_01_1", out Igi2PhysicsObjectType apcType));
        Igi2CarType apc = Igi2CarType.Parse(apcType.Parameters);
        Assert.Equal(80000f, apc.Vehicle.MaxThrust);
        Assert.Equal(0.996f, apc.Vehicle.ThrustInertia);
        Assert.Equal(0.92f, apc.Vehicle.DeltaGammaInertia);
        Assert.Empty(apc.Vehicle.Wings);
        Assert.Equal(122880f, apc.Vehicle.ViewconeLength);
        Assert.Equal(4, apc.Wheels.Count);
        Assert.Equal([false, true, true, false], apc.Wheels.Select(static w => w.IsSteering));
        Assert.All(apc.Wheels, static w => Assert.True(w.IsEngine));
        Assert.Equal([0, 1, 2, 3], apc.Wheels.Select(static w => w.ContactPoint));
        Assert.Equal(60f, apc.Wheels[1].MaxAngle);
        Assert.Equal(1638.4f, apc.Wheels[0].MaxSpringDistance);
        Assert.Equal(0.7f, apc.Wheels[0].SpringStiffness);
        Assert.Equal(409.6f, apc.WheelContactLowering);
        Assert.True(apc.IsWheelsAttachObject);
        Assert.False(apc.IsCrawler);
        Assert.Equal("apc_sounds", apc.EngineSound);
        Assert.Equal([4, 5, 6, 7, 8], apc.ExplodeVertices);
        Assert.Equal("665_06_1", apc.DestroyedModel);

        Assert.True(types.TryFind("614_01_1", out Igi2PhysicsObjectType tankType));
        Igi2CarType tank = Igi2CarType.Parse(tankType.Parameters);
        Assert.True(tank.IsCrawler);
        Assert.Equal(16, tank.Wheels.Count);
        Assert.Equal(12, tank.Wheels.Count(static w => w.ContactPoint < 0));
    }

    /// <summary>
    /// Mission 15's armoured car, braked, settles on its four sprung wheels where the level put it, each wheel touching; given full
    /// thrust it drives off along its own forward axis, on the ground.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_car_settles_on_its_wheels_and_drives_forward()
    {
        (_, Igi2CarTask car) = Car(1300);
        Assert.NotNull(car.Body);
        Assert.Equal(4, car.AttachedWheels);
        Assert.NotNull(car.Gun);
        Vector3d start = car.CurrentPosition;
        for (int tick = 1; tick <= 90; tick++)
        {
            car.Drive(tick, [0f, 0f, 0f, 1f], tick);
        }

        Vector3d rest = car.CurrentPosition;
        Assert.All(car.Wheels, static w => Assert.True(w.IsContact));
        Assert.InRange(rest.Z - start.Z, -4096.0, 0.0);
        IReadOnlyList<float> m0 = car.CurrentOrientation;
        for (int tick = 91; tick <= 180; tick++)
        {
            car.Drive(tick, [0f, 1f, 0f, Igi2CarTask.HoldChannel], tick);
        }

        Vector3d d = car.CurrentPosition - rest;
        double forward = (d.X * m0[1]) + (d.Y * m0[4]) + (d.Z * m0[7]);
        Assert.True(forward > 4096.0, $"drove {forward / 4096.0} m forward");
        Assert.InRange(Math.Abs(d.Z), 0.0, 4096.0);
        Assert.True(car.RealThrust > 0.25f && car.RealThrust < 0.35f, $"thrust {car.RealThrust}");
    }

    /// <summary>
    /// Mission 15's armoured car, run with its driver, sets its route's speed of ten and heads along an A* path over its graph for the
    /// route's first node, steering for it.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_driver_drives_its_route()
    {
        (RetailIgi2Mission1 m, Igi2CarTask car) = Car(1300);
        Igi2CarAiTask driver = car.Children.OfType<Igi2CarAiTask>().Single();
        VirtualFileSystem files = new();
        files.RegisterDevice(new LocalFileDevice(GameDataSource.Igi2.RequireRoot()));
        files.SetAlias(VirtualFileSystem.MissionAlias, "LOCAL:missions/location1/level5/");
        Igi2AiWorld ai = new()
        {
            Graphs = Igi2AiGraphs.Build(m.Tree[0], new ResourceRegistry(files), m.Run.Natives, m.Run.Functions),
            PatrolPath = id => m.Tree.FirstOrDefault(t => t.Id == id) as Igi2PatrolPathTask,
        };
        driver.Prepare(ai);
        Assert.Same(car, driver.Car);
        Igi2AiGraphNode node = ai.Graphs!.Graph(driver.GraphId)!.NodeAt(65)!;
        Vector3d start = car.CurrentPosition;
        double before = (node.Position - start).Length;

        // The car's container runs it only once the mission wakes it; here it is run each tick as the level goes on.
        for (int tick = 0; tick < 600; tick++)
        {
            m.Step();
            car.Run(m.Run);
        }

        Assert.Equal(Igi2PatrolCommand.WalkTo, driver.Patrol.Current?.Command);
        Assert.Equal(65, driver.Patrol.Current?.Parameter);
        Assert.Equal(10f, driver.TargetSpeed);
        Assert.Same(node, driver.TargetNode);
        Assert.True((car.CurrentPosition - start).Length > 4.0 * 4096.0, $"travelled {(car.CurrentPosition - start).Length / 4096.0} m");
        Assert.True((node.Position - car.CurrentPosition).Length < before);
        Assert.NotEqual(0f, driver.WantedGamma);
    }

    private static (RetailIgi2Mission1 Mission, Igi2CarTask Car) Car(int id)
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load(collision: true, location: "location1", levelName: "level5");
        Igi2CarTask car = m.Tree.OfType<Igi2CarTask>().Single(c => c.Id == id);
        car.Magic = m.Services!.Require<Igi2MagicObjTypes>();
        car.Bind(m.Collision!.SolidsOf(Igi2CarTask.TypeName, car.Id));
        car.Prepare(
            m.Services!.Require<Igi2PhysicsObjectTypes>(),
            m.Level.ModelFor,
            new Igi2PhysicsWorld(m.Collision!, m.Materials, static owner => !Collision.Igi2ObjectCollision.PassesThrough(owner)),
            m.Services!.Require<Weapons.Igi2HumanWeapons>(),
            null);
        return (m, car);
    }
}
