using OpenIGI.Game.Physics;
using OpenIGI.Engine.Flow;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.World;
using OpenIGI.Game.Player;
using OpenIGI.TestSupport;
using OpenIGI.Formats.World;
using OpenIGI.Scripting.Runtime;
using Xunit;
using Xunit.Abstractions;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.World;
using static OpenIGI.Game.Tests.RetailMissionFixture;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The cutscene helicopters against retail: their physics records, the rigid-body trace, the rotor
/// presentation, and what rides on them.
/// </summary>
public sealed class RetailCutsceneHelicopterTests
{
    [RequiresGameDataFact]
    public void RetailHelicopterScriptsPublishControlAndRotorRecords()
    {
        VirtualFileSystem fs = new(); fs.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fs);
        VehiclePhysicsRegistry physics = new();

        Assert.True(physics.Load(resources) >= 6);
        Assert.True(physics.TryGetHelicopter("709_01_1", out HelicopterPhysicsDefinition helicopter));
        Assert.Equal(3_000f, helicopter.Mass);
        Assert.Equal(new Vector3f(2.2f, 7.8f, 3f), helicopter.Dimensions);
        Assert.Equal(new Vector3f(50f, 50f, 50f), helicopter.Torque);
        Assert.Equal(new Vector3f(0.4f, 0.4f, 0.4f), helicopter.Smoothing);
        Assert.Equal(0.027f, helicopter.HighCollectiveStep);
        Assert.Equal(0.003f, helicopter.LowCollectiveStep);
        Assert.Equal(200f, helicopter.MaximumSpeed);
        Assert.Equal(3, helicopter.Wings.Count);
        Assert.Equal(new Wing(20f, Vector3f.Zero, Vector3f.UnitX), helicopter.Wings[0]);
        Assert.Equal(new Wing(3f, Vector3f.Zero, Vector3f.UnitY), helicopter.Wings[1]);
        Assert.Equal(new Wing(50f, Vector3f.Zero, Vector3f.UnitZ), helicopter.Wings[2]);

        Assert.True(physics.TryGetRotor("711_02_1", out RotorPhysicsDefinition mainBlade));
        Assert.False(mainBlade.ProducesLift);
        Assert.False(mainBlade.IsTailRotor);
        Assert.Equal(17, mainBlade.BladeSamples);
        Assert.Equal(0.07f, mainBlade.PhaseStep);
        Assert.Equal(1.19f, mainBlade.PhasePerCollectiveTick, precision: 5);
        Assert.True(physics.TryGetRotor("712_01_1", out RotorPhysicsDefinition tailRotor));
        Assert.True(tailRotor.IsTailRotor);
        Assert.True(physics.TryGetCar("622_01_1", out CarPhysicsDefinition truck));
        Assert.Equal(3_000f, truck.Mass);
        Assert.Equal(new Vector3f(2.2f, 7.8f, 3f), truck.Dimensions);
        // The TYPE record declares four wheel bodies (argument 9) as 24-byte entries from argument 10.
        Assert.Equal(4, truck.BodyCount);
        Assert.Equal(4, truck.Bodies.Count);
        Assert.Equal(new PhysicsContactPointType(0, 0, 5, 0f, 0.95f, 0.05f), truck.Bodies[0]);
        Assert.Equal(new PhysicsContactPointType(1, 0, 5, 0f, 0.95f, 0.05f), truck.Bodies[1]);

        // Retention is record +24/+32 and the engine force +28 -- arguments 42, 44 and 43, which sit
        // in the tail after the wheel bodies, not in the header. Cars carry no aerodynamics.
        Assert.Equal(0.95f, truck.SteeringRetention);
        Assert.Equal(0.95f, truck.ThrottleRetention);
        Assert.Equal(5_000f, truck.EngineForce);
        Assert.Equal(0, truck.AerodynamicSurfaceCount);

        // The CAR tail (0x42DB00) declares SIX wheels, each read as int, int, float, int, float,
        // float. Both steered, driven entries are the front pair; the two negative body indices have
        // no collision body and only draw.
        Assert.Equal(6, truck.Wheels.Count);
        Assert.Equal(
            new CarWheelTypeDefinition(true, true, 45f, 1, 0.3f * (float)WorldScene.UnitsPerMetre, 0.5f),
            truck.Wheels[0]);
        Assert.Equal(
            new CarWheelTypeDefinition(false, false, 0f, 0, 0.3f * (float)WorldScene.UnitsPerMetre, 0.5f),
            truck.Wheels[1]);
        Assert.Equal(-10, truck.Wheels[2].ContactPoint);
        Assert.False(truck.Wheels[2].HasContactPoint);
        Assert.Equal(
            new CarWheelTypeDefinition(true, true, 45f, 2, 0.3f * (float)WorldScene.UnitsPerMetre, 0.5f),
            truck.Wheels[3]);
        Assert.Equal(-13, truck.Wheels[4].ContactPoint);
        Assert.Equal(3, truck.Wheels[5].ContactPoint);
        Assert.Equal(2, truck.DrivenWheelCount);
        Assert.Equal(45f * (MathF.PI / 180f), truck.Wheels[0].MaxAngleRadians, precision: 5);

        // The native Car parser receives the complete 95-value cutscene-truck record.  Keep the
        // unassigned tail available by index while its wheel/contact layout is being translated;
        // reducing it to the seven header fields would silently discard the data used by 0x42D1D0.
        Assert.Equal(95, truck.ArgumentCount);
        Assert.Equal(QvmValueKind.Integer, truck.ArgumentAt(0).Kind);
        Assert.True(physics.Types.TryFind("622_01_1", out PhysicsObjectType truckType));
        Assert.Equal("Car", truckType.TaskTypeName);
        Assert.Equal(truckType.TaskType, truck.ArgumentAt(0).Integer); // TASKTYPE_CAR
        Assert.Equal("622_01_1", truck.ArgumentAt(8).Text);
        Assert.Equal(30, truck.ArgumentAt(34).Integer); // cutscene truck steering-point count
        Assert.Equal(0.001f, truck.ArgumentAt(35).ToSingle());
        Assert.Equal(90, truck.ArgumentAt(36).Integer);
        Assert.Equal(45f, truck.ArgumentAt(41).ToSingle());

        // The Level 3 outro plane (702_02_1) is backed by the SU27 definition.  The
        // common parser consumes arguments 0..11; Plane_Physics (0x433B50) reads its
        // task-specific config from argument 12 onward.  In particular, the three
        // control retentions and the two thrust steps are not rendering guesses.
        Assert.True(physics.TryGetPlane("702_02_1", out PlanePhysicsDefinition su27));
        Assert.Equal(68, su27.ArgumentCount);
        Assert.Equal(QvmValueKind.Integer, su27.ArgumentAt(0).Kind);
        Assert.Equal(10_000f, su27.ArgumentAt(2).ToSingle());
        Assert.Equal(5f, su27.ArgumentAt(3).ToSingle());
        Assert.Equal(16f, su27.ArgumentAt(4).ToSingle());
        Assert.Equal(2.5f, su27.ArgumentAt(5).ToSingle());
        Assert.Equal("702_02_1", su27.ArgumentAt(8).Text);
        Assert.Equal(new Vector3f(0.9f, 0.9f, 0.9f), su27.ControlRetention);
        Assert.Equal(0.054f, su27.HighThrustStep, precision: 5);
        Assert.Equal(0.006f, su27.LowThrustStep, precision: 5);
    }

    [RequiresGameDataFact]
    public void MissionOneHumanDetachesWhenItsMovingOwnerIsUnregistered()
    {
        RetailMissionFixture fixture = RetailMissionFixture.Open();
        GameLoop loop = fixture.Loop;
        LevelTask root = fixture.Root;
        _ = fixture.Load(1);
        new ConditionalContainerGate(loop.Variables).Prepare(root);
        CutsceneRuntime runtime = new(root, loop.Variables);
        HumanSoldierTask passenger = Assert.IsType<HumanSoldierTask>(root.Tree.FromId(1505));
        GameTask helicopter = Assert.IsType<DeclaredTask>(root.Tree.FromId(1502));

        runtime.Tick();
        Assert.True(ConditionalContainerGate.IsTaskActive(helicopter));
        Assert.True(runtime.TryAttachActorToTask(
            passenger,
            helicopter,
            passenger.Position,
            Matrix3f.FromEngineEulerAngles(0f, 0f, passenger.Gamma)));
        Assert.True(runtime.TryGetAttachedDrawTransform(passenger, out _, out _));

        // ConditionalContainer_RunHandler unregisters the moving subtree before the next Human ground
        // pass (the tick after its condition fails, 0x4874E0). The native 0x463550 helper clears the parent
        // only for this inactive-owner case.
        Assert.True(loop.Variables.TryWrite("ConditionalContainer_4002.isRun", QvmValue.Zero));
        ((ConditionalContainerTask)root.Tree.FromId(4002)!).IsActive = false;
        Assert.False(ConditionalContainerGate.IsTaskActive(helicopter));
        Assert.True(runtime.TryDetachInactiveAttachment(passenger));
        Assert.False(runtime.TryGetAttachedDrawTransform(passenger, out _, out _));
        Assert.False(runtime.TryDetachInactiveAttachment(passenger));
    }

    [RequiresGameDataFact]
    public void MissionOneVehicleAttachmentsKeepTheirAuthoredOffsetsWhileMoving()
    {
        RetailMissionFixture fixture = RetailMissionFixture.Open();
        GameLoop loop = fixture.Loop;
        LevelTask root = fixture.Root;
        ResourceRegistry resources = fixture.Resources;
        MagicObjectRegistry magic = fixture.LoadMagic();
        LoadedMission loaded = fixture.Load(1);
        WorldScene world = WorldScene.Build(loaded, root, magic);
        VehiclePhysicsRegistry physics = new(); physics.Load(resources);
        new ConditionalContainerGate(loop.Variables).Prepare(root);
        CutsceneRuntime runtime = new(root, loop.Variables, vehiclePhysics: physics);
        WorldPlacement[] helicopter = world.Placements.Where(placement => placement.Owner.Id == 1502).ToArray();
        Assert.True(helicopter.Length >= 5, $"expected the helicopter and its attachments, got {helicopter.Length}");
        Assert.Equal(4, helicopter.Count(placement => placement.IsRotor));
        for (int tick = 0; tick < 360; tick++)
        {
            runtime.Tick();
        }

        Vector3d[] moved = new Vector3d[helicopter.Length];
        for (int i = 0; i < helicopter.Length; i++)
        {
            WorldPlacement placement = helicopter[i];
            Assert.True(runtime.TryGetPlacementDrawTransform(
                placement.Owner, placement.Position, placement.Orientation, out moved[i], out _));
        }
        double largestSeparation = 0d;
        for (int i = 0; i < helicopter.Length; i++)
        {
            for (int j = i + 1; j < helicopter.Length; j++)
            {
                double authored = Distance(helicopter[i].Position, helicopter[j].Position);
                double animated = Distance(moved[i], moved[j]);
                Assert.InRange(Math.Abs(animated - authored), 0d, 0.0001d);
                largestSeparation = Math.Max(largestSeparation, animated);
            }
        }
        Assert.True(largestSeparation > 1d, $"helicopter attachments collapsed to {largestSeparation:F3} m");

        static double Distance(Vector3d a, Vector3d b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        }
    }

    [RequiresGameDataFact]
    public void ParkedMissionThreeHelicopterKeepsItsAuthoredRotorPresentationMoving()
    {
        RetailMissionFixture fixture = RetailMissionFixture.Open();
        GameLoop loop = fixture.Loop;
        LevelTask root = fixture.Root;
        ResourceRegistry resources = fixture.Resources;
        MagicObjectRegistry magic = fixture.LoadMagic();
        LoadedMission mission = fixture.Load(3);
        WorldScene world = WorldScene.Build(mission, root, magic);
        VehiclePhysicsRegistry physics = new(); physics.Load(resources);
        new ConditionalContainerGate(loop.Variables).Prepare(root);
        CutsceneRuntime runtime = new(root, loop.Variables, vehiclePhysics: physics, terrain: mission.Terrain);
        WorldPlacement[] rotors = world.Placements.Where(
            placement => placement.Owner.Id == 3339
                && placement.Instance.Name == "711_02_1").ToArray();
        Assert.Equal(2, rotors.Length);

        runtime.Tick();
        RotorDrawState[] before = rotors.Select(rotor =>
        {
            Assert.True(runtime.TryGetRotorDrawState(rotor, out RotorDrawState state));
            return state;
        }).ToArray();
        runtime.Tick();
        for (int i = 0; i < rotors.Length; i++)
        {
            Assert.True(runtime.TryGetRotorDrawState(rotors[i], out RotorDrawState after));
            Assert.NotEqual(before[i].GetOrientation(0), after.GetOrientation(0));
            Assert.Equal(before[i].Position, after.Position);
            Assert.Equal(before[i].SampleCount, after.SampleCount);
            Assert.True(after.SampleCount >= 2);
        }
    }

    [RequiresGameDataFact]
    public void MissionOneRotorDiscKeepsItsFlapInsideTheAuthoredRange()
    {
        RetailMissionFixture fixture = RetailMissionFixture.Open();
        GameLoop loop = fixture.Loop;
        LevelTask root = fixture.Root;
        ResourceRegistry resources = fixture.Resources;
        MagicObjectRegistry magic = fixture.LoadMagic();
        LoadedMission mission = fixture.Load(1);
        WorldScene world = WorldScene.Build(mission, root, magic);
        VehiclePhysicsRegistry physics = new(); physics.Load(resources);
        Assert.True(physics.TryGetRotor("711_02_1", out RotorPhysicsDefinition mainBlade));
        new ConditionalContainerGate(loop.Variables).Prepare(root);
        CutsceneRuntime runtime = new(root, loop.Variables, vehiclePhysics: physics, terrain: mission.Terrain);
        WorldPlacement[] rotors = world.Placements.Where(
            placement => placement.Owner.Id == 1502 && placement.Instance.Name == "711_02_1").ToArray();
        Assert.NotEmpty(rotors);

        // RotorPrimitive::Draw flaps the blade with the airframe's AXIAL speed measured against
        // 0.4 of the model's maximum speed, so a flying Bell stays inside the collective's own
        // -(1 - collective) .. +0.1 band: the flap is a trim, not a switch. Reading the sideways
        // axis, or dividing by the live collective, saturated it at the authored five degrees on
        // any movement and inverted it whenever the drift changed sign -- the blur samples then
        // read as an umbrella and a reversed umbrella instead of a flat disc.
        float lowest = float.PositiveInfinity;
        float highest = float.NegativeInfinity;
        for (int tick = 0; tick < 600; tick++)
        {
            runtime.Tick();
            foreach (WorldPlacement rotor in rotors)
            {
                Assert.True(runtime.TryGetRotorDrawState(rotor, out RotorDrawState state));
                lowest = Math.Min(lowest, state.Tilt);
                highest = Math.Max(highest, state.Tilt);
            }
        }

        Assert.True(
            lowest > -mainBlade.MaximumTilt,
            $"the rotor disc saturated its downward flap at {lowest:F5} rad");
        // The collective band tops out at +0.1 * MaximumTilt. The axial trim is allowed a further
        // hundredth of the authored deflection on top of it, which this flight uses less than a
        // tenth of; the broken term reached the whole +MaximumTilt.
        Assert.True(
            highest <= 0.11f * mainBlade.MaximumTilt,
            $"the rotor disc flapped upward to {highest:F5} rad, past the collective band");
    }

    [RequiresGameDataFact]
    public void MissionOneHelicopterMatchesRetailRigidBodyTrace()
    {
        RetailMissionFixture fixture = RetailMissionFixture.Open();
        GameLoop loop = fixture.Loop;
        LevelTask root = fixture.Root;
        ResourceRegistry resources = fixture.Resources;
        _ = fixture.Load(1);
        VehiclePhysicsRegistry physics = new(); physics.Load(resources);
        new ConditionalContainerGate(loop.Variables).Prepare(root);
        CutsceneRuntime runtime = new(root, loop.Variables, vehiclePhysics: physics);
        GameTask helicopter = Assert.IsAssignableFrom<GameTask>(root.Tree.FromId(1502));
        HumanSoldierTask helicopterJones = Assert.IsType<HumanSoldierTask>(root.Tree.FromId(1505));
        Assert.True(runtime.TryAttachActorToTask(
            helicopterJones,
            helicopter,
            helicopterJones.Position,
            Matrix3f.FromEngineEulerAngles(0f, 0f, helicopterJones.Gamma)));

        for (int tick = 0; tick < 29; tick++)
        {
            runtime.Tick();
        }
        Assert.True(runtime.TryGetDrawTransform(
            helicopter,
            out Vector3d previousPosition,
            out Matrix3f previousOrientation));
        Assert.True(runtime.TryGetAttachedDrawTransform(
            helicopterJones,
            out Vector3d previousJonesPosition,
            out _));
        Vector3d jonesLocalPosition = previousOrientation.Transform(
            previousJonesPosition - previousPosition);
        runtime.Tick();

        Assert.True(runtime.TryGetDrawTransform(helicopter, out Vector3d position, out Matrix3f orientation));
        Vector3d expected = new(22_681_159.833 / WorldScene.UnitsPerMetre,
            -60_962_730.119 / WorldScene.UnitsPerMetre,
            174_938_815.877 / WorldScene.UnitsPerMetre);
        Assert.True((position - expected).Length <= 0.02d,
            $"retail={expected}, runtime={position}, delta={(position - expected).Length:R}, matrix={orientation}, velocity={(position - previousPosition) * WorldScene.UnitsPerMetre}");
        Assert.InRange(MathF.Abs(orientation.M23 - 0.342750251f), 0f, 0.0002f);
        Assert.InRange(MathF.Abs(orientation.M33 - 0.939382612f), 0f, 0.0002f);

        Assert.True(runtime.TryGetAttachedDrawTransform(
            helicopterJones,
            out Vector3d jonesPosition,
            out _));
        Vector3d expectedJonesPosition = position
            + orientation.Transposed().Transform(jonesLocalPosition);
        Assert.True((jonesPosition - expectedJonesPosition).Length <= 0.0001d,
            $"helicopter Jones must use the current parent slot: expected={expectedJonesPosition}, " +
            $"runtime={jonesPosition}, delta={(jonesPosition - expectedJonesPosition).Length:R}");
    }

    /// <summary>
    /// Mission 4's parked Bell rests on its pad at the authored pose instead of sinking through the
    /// terrain while its collective is below hover, then leaves the pad along the authored path
    /// once cyclic pitches the disc. Retail's Heli collision flag grounds the airframe; excess
    /// collective alone must not launch it up the world Z axis.
    /// </summary>
    [RequiresGameDataFact]
    public void MissionFourParkedHelicopterRestsOnTerrainThenLiftsOff()
    {
        RetailMissionFixture fixture = RetailMissionFixture.Open();
        GameLoop loop = fixture.Loop;
        LevelTask root = fixture.Root;
        ResourceRegistry resources = fixture.Resources;
        LoadedMission mission = fixture.LoadWithTerrain(4);
        MagicObjectRegistry magic = fixture.LoadMagic();
        WorldScene scene = WorldScene.Build(mission, root, magic);
        VehiclePhysicsRegistry physics = new(); physics.Load(resources);
        new ConditionalContainerGate(loop.Variables).Prepare(root);
        CutsceneRuntime runtime = new(root, loop.Variables, vehiclePhysics: physics, terrain: mission.Terrain);

        // The parked Bell rests low (skids on its pad) while the airborne getaway Bell flies far above
        // it; this ceiling separates the two 709_01_1 instances. It is the same parked-helicopter
        // Z threshold the suite uses elsewhere to pick the parked copy.
        const double ParkedBellZCeiling = 42_800d;
        WorldPlacement parkedBell = Assert.Single(scene.Placements, placement =>
            placement.Instance.Name == "709_01_1" && placement.Position.Z < ParkedBellZCeiling);
        GameTask helicopter = parkedBell.Owner;

        // The transport Bell rests on the pad inside a gated container whose authored condition needs
        // the player to enter area 156 - something a headless runtime test cannot supply. Force the
        // container active so the flight programme actually runs, matching how the intro activates it.
        if (root.Tree.FromId(3902) is ConditionalContainerTask { } transportBell)
        {
            transportBell.Condition = "1";
        }

        // Tick until the Bell exposes its rest pose on the pad, then measure its take-off profile.
        Vector3d parked = default;
        int activatedTick = -1;
        for (int t = 0; t < 20_000; t++)
        {
            runtime.Tick();
            if (runtime.TryGetDrawTransform(helicopter, out parked, out _))
            {
                activatedTick = t;
                break;
            }
        }
        Assert.True(activatedTick >= 0, "parked Bell never exposed a draw transform");
        double pad = parked.Z; // authored rest pose: skids on the pad, origin just above it
        TerrainData terrain = Assert.IsAssignableFrom<TerrainData>(mission.Terrain);
        Assert.True(TerrainProbe.TryGetGroundHeight(
                terrain, parked.X, parked.Y, 45_000.0, 42_000.0, out double terrainAtPad),
            "terrain lookup found no ground under the parked Bell");
        Assert.True(terrainAtPad <= pad && pad - terrainAtPad < 8d,
            $"parked Bell must sit on the pad: pad={pad:R}, terrain={terrainAtPad:R}");

        double lowest = double.MaxValue;
        double farthest = 0d;
        for (int tick = 0; tick < 600; tick++)
        {
            runtime.Tick();
            if (tick % 9 == 0)
            {
                Assert.True(runtime.TryGetDrawTransform(helicopter, out Vector3d position, out _));
                lowest = Math.Min(lowest, position.Z);
                double slide = Math.Sqrt(
                    ((position.X - parked.X) * (position.X - parked.X))
                    + ((position.Y - parked.Y) * (position.Y - parked.Y)));
                farthest = Math.Max(farthest, slide);
                Assert.True(TerrainProbe.TryGetGroundHeight(
                        terrain, position.X, position.Y, 45_000.0, 42_000.0, out double ground),
                    $"terrain lookup found no ground at tick={tick}");
                Assert.True(position.Z >= ground - 0.05d,
                    $"helicopter must not sink below the terrain: tick={tick}, z={position.Z:R}, terrain={ground:R}");
            }
        }

        // Collision holds the skids through the pre-cyclic collective ramp. Lift-off is
        // cyclic converting that lift into the authored path, not a vertical launch.
        Assert.True(lowest >= pad - 0.1d,
            $"parked Bell must stay on the pad until lift-off: lowest={lowest:R}, pad={pad:R}");
        Assert.True(farthest > 20d,
            $"parked Bell must leave the pad along its path after cyclic: slide={farthest:R}");
    }
}
