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

namespace OpenIGI.Game.Tests;

public sealed class RetailCutsceneRuntimeTests
{
    private readonly ITestOutputHelper _output;

    public RetailCutsceneRuntimeTests(ITestOutputHelper output)
    {
        _output = output;
    }

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
        Assert.Equal(3, helicopter.Aerodynamics.Count);
        Assert.Equal(new AerodynamicSurfaceDefinition(20f, Vector3f.UnitX, Vector3f.Zero),
            helicopter.Aerodynamics[0]);
        Assert.Equal(new AerodynamicSurfaceDefinition(3f, Vector3f.UnitY, Vector3f.Zero),
            helicopter.Aerodynamics[1]);
        Assert.Equal(new AerodynamicSurfaceDefinition(50f, Vector3f.UnitZ, Vector3f.Zero),
            helicopter.Aerodynamics[2]);

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
        Assert.Equal(new CarBodyDefinition(0, 0, 5, 0f, 0.95f, 0.05f), truck.Bodies[0]);
        Assert.Equal(new CarBodyDefinition(1, 0, 5, 0f, 0.95f, 0.05f), truck.Bodies[1]);

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
            new CarWheelDefinition(true, true, 45f, 1, 0.3f * (float)WorldScene.UnitsPerMetre, 0.5f),
            truck.Wheels[0]);
        Assert.Equal(
            new CarWheelDefinition(false, false, 0f, 0, 0.3f * (float)WorldScene.UnitsPerMetre, 0.5f),
            truck.Wheels[1]);
        Assert.Equal(-10, truck.Wheels[2].BodyIndex);
        Assert.False(truck.Wheels[2].HasBody);
        Assert.Equal(
            new CarWheelDefinition(true, true, 45f, 2, 0.3f * (float)WorldScene.UnitsPerMetre, 0.5f),
            truck.Wheels[3]);
        Assert.Equal(-13, truck.Wheels[4].BodyIndex);
        Assert.Equal(3, truck.Wheels[5].BodyIndex);
        Assert.Equal(2, truck.DrivenWheelCount);
        Assert.Equal(45f * (MathF.PI / 180f), truck.Wheels[0].MaximumSteerRadians, precision: 5);

        // The native Car parser receives the complete 95-value cutscene-truck record.  Keep the
        // unassigned tail available by index while its wheel/contact layout is being translated;
        // reducing it to the seven header fields would silently discard the data used by 0x42D1D0.
        Assert.Equal(95, truck.ArgumentCount);
        Assert.Equal(QvmValueKind.Integer, truck.ArgumentAt(0).Kind);
        Assert.Equal(1, truck.ArgumentAt(0).Integer); // TASKTYPE_CAR
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
    public void MissionOneAttachedHumanConsumesItsAnimationRootMotion()
    {
        VirtualFileSystem fs = new(); fs.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fs); GameLoop loop = new(new ManualTimeSource());
        RegisterCutsceneTypes(loop.Types);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new(); missions.Register(loop.Natives); missions.Scan(fs, resources, loop.Natives);
        _ = new MissionLoader(missions, fs, resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(1);
        new ConditionalContainerGate(loop.Variables).Prepare(root);
        CutsceneRuntime runtime = new(root, loop.Variables);
        HumanSoldierTask jones = Assert.IsType<HumanSoldierTask>(root.Tree.FromId(2021));

        Assert.True(runtime.TryGetAttachedDrawTransform(jones, out Vector3d before, out Matrix3f orientation));
        Vector3f rootDelta = new(160f, 640f, 80f);
        Assert.True(runtime.AdvanceAttachedActor(jones, rootDelta));
        Assert.True(runtime.TryGetAttachedDrawTransform(jones, out Vector3d after, out _));

        Vector3d expected = orientation.Transposed().Transform(new Vector3d(
            rootDelta.X,
            rootDelta.Y,
            rootDelta.Z)) / WorldScene.UnitsPerMetre;
        Assert.InRange(Math.Abs((after.X - before.X) - expected.X), 0d, 0.000001d);
        Assert.InRange(Math.Abs((after.Y - before.Y) - expected.Y), 0d, 0.000001d);
        Assert.InRange(Math.Abs((after.Z - before.Z) - expected.Z), 0d, 0.000001d);

        Assert.True(runtime.AdvanceAttachedActor(jones, Vector3f.Zero));
        Assert.True(runtime.TryGetAttachedDrawTransform(jones, out Vector3d stationary, out _));
        Assert.InRange((stationary - after).Length, 0d, 0.000001d);
    }

    [RequiresGameDataFact]
    public void MissionOneHumanDetachesWhenItsMovingOwnerIsUnregistered()
    {
        VirtualFileSystem fs = new(); fs.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fs); GameLoop loop = new(new ManualTimeSource());
        RegisterCutsceneTypes(loop.Types);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new(); missions.Register(loop.Natives); missions.Scan(fs, resources, loop.Natives);
        _ = new MissionLoader(missions, fs, resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(1);
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
        VirtualFileSystem fs = new(); fs.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fs); GameLoop loop = new(new ManualTimeSource());
        RegisterCutsceneTypes(loop.Types);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new(); missions.Register(loop.Natives); missions.Scan(fs, resources, loop.Natives);
        MagicObjectRegistry magic = new(); magic.Register(loop.Natives); magic.Load(resources, loop.Natives);
        LoadedMission loaded = new MissionLoader(missions, fs, resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(1);
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
        VirtualFileSystem fs = new(); fs.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fs); GameLoop loop = new(new ManualTimeSource());
        RegisterCutsceneTypes(loop.Types);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new(); missions.Register(loop.Natives); missions.Scan(fs, resources, loop.Natives);
        MagicObjectRegistry magic = new(); magic.Register(loop.Natives); magic.Load(resources, loop.Natives);
        LoadedMission mission = new MissionLoader(
            missions, fs, resources, new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(3);
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
        VirtualFileSystem fs = new(); fs.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fs); GameLoop loop = new(new ManualTimeSource());
        RegisterCutsceneTypes(loop.Types);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new(); missions.Register(loop.Natives); missions.Scan(fs, resources, loop.Natives);
        MagicObjectRegistry magic = new(); magic.Register(loop.Natives); magic.Load(resources, loop.Natives);
        LoadedMission mission = new MissionLoader(
            missions, fs, resources, new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(1);
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
        VirtualFileSystem fs = new(); fs.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fs); GameLoop loop = new(new ManualTimeSource());
        RegisterCutsceneTypes(loop.Types);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new(); missions.Register(loop.Natives); missions.Scan(fs, resources, loop.Natives);
        _ = new MissionLoader(missions, fs, resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(1);
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

    [RequiresGameDataFact]
    public void MissionOneConditionalSoundEdgesKeepTheirRetailKinds()
    {
        VirtualFileSystem fs = new(); fs.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fs); GameLoop loop = new(new ManualTimeSource());
        RegisterCutsceneTypes(loop.Types);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new(); missions.Register(loop.Natives); missions.Scan(fs, resources, loop.Natives);
        _ = new MissionLoader(missions, fs, resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(1);
        new ConditionalContainerGate(loop.Variables).Prepare(root);
        DeclaredTask dialogue = Assert.IsType<DeclaredTask>(root.Tree.FromId(3998));
        Assert.True(dialogue.TryGetValue("Relative to microphone", out object? relative));
        Assert.False(Assert.IsType<bool>(relative));
        List<CutsceneSoundEvent> edges = [];
        CutsceneRuntime runtime = new(root, loop.Variables, soundChanged: edges.Add);
        runtime.Tick();
        Assert.Contains(edges, e => e.Name == "m1_heligen01" && e.Playing && !e.Simple && e.Position is null);
        Assert.Contains(edges, e => e.Name == "m1_heliblad02_vol1" && e.Playing && !e.Simple && e.Position is null);
        Assert.Contains(edges, e => e.Name == "_cut01_01" && e.Playing && e.Simple && e.Position is null);

        for (int tick = 0; tick < 400; tick++)
        {
            runtime.Tick();
        }

        Assert.Contains(edges, e => e.Name == "m1_heligen01" && !e.Playing);
        Assert.DoesNotContain(edges, e => e.Name == "_cut01_01" && !e.Playing);
        Assert.Single(edges, e => e.Name == "_cut01_01");
    }

    [RequiresGameDataTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    public void EveryRetailMissionCanRunItsOpeningCutsceneChain(int missionId)
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        RegisterCutsceneTypes(loop.Types);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fileSystem, resources, loop.Natives);
        _ = new MissionLoader(
            missions,
            fileSystem,
            resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(missionId);

        new ConditionalContainerGate(loop.Variables).Prepare(root);
        CutsceneRuntime runtime = new(root, loop.Variables);
        MissionObjectiveRuntime objectives = new(root, loop.Variables, native: runtime.TryInvokeNative);

        Assert.NotEmpty(Walk(root).OfType<CutSceneTask>());
        Assert.True(runtime.IsPlaying);
        Assert.NotNull(runtime.Frame);
        for (int tick = 0; tick < 20_000 && runtime.IsPlaying; tick++)
        {
            objectives.TickLevelState();
            runtime.Tick();
            objectives.TickAfterLevelState();
            runtime.FinishExpressionTick();
        }

        Assert.False(runtime.IsPlaying);
    }

    [RequiresGameDataFact]
    public void MissionFiveIntroShowsAuthoredBinocularOverlayOnlyDuringScene3501()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        RegisterCutsceneTypes(loop.Types);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fileSystem, resources, loop.Natives);
        _ = new MissionLoader(
            missions,
            fileSystem,
            resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(5);

        new ConditionalContainerGate(loop.Variables).Prepare(root);
        CutsceneRuntime runtime = new(root, loop.Variables);

        // LEVEL5/objects.qsc authors the holder as
        // CutScene_3500.isFinished && !CutScene_3501.isFinished && ConditionalContainer_1000.isRun.
        // The boundary ticks below are the frames produced by the retail 30 Hz completion contract.
        CutsceneFrame opening = Assert.IsType<CutsceneFrame>(runtime.Frame);
        Assert.Equal(3500, opening.Scene.Id);
        Assert.Equal(0, opening.Scene.Ticks);
        Assert.False(opening.BinocularOverlayVisible);

        CutsceneFrame lastEstablishingFrame = opening;
        int lastEstablishingTick = opening.Scene.Ticks;
        for (int tick = 0; tick < 1_000 && runtime.ActiveScene?.Id == 3500; tick++)
        {
            lastEstablishingFrame = Assert.IsType<CutsceneFrame>(runtime.Frame);
            lastEstablishingTick = lastEstablishingFrame.Scene.Ticks;
            runtime.Tick();
        }

        Assert.NotEqual(3500, runtime.ActiveScene?.Id);
        Assert.Equal(107, lastEstablishingTick);
        Assert.False(lastEstablishingFrame.BinocularOverlayVisible);

        CutsceneFrame firstBinocularFrame = Assert.IsType<CutsceneFrame>(runtime.Frame);
        Assert.Equal(3501, firstBinocularFrame.Scene.Id);
        Assert.Equal(1, firstBinocularFrame.Scene.Ticks);
        Assert.True(firstBinocularFrame.BinocularOverlayVisible);

        CutsceneFrame lastBinocularFrame = firstBinocularFrame;
        int lastBinocularTick = firstBinocularFrame.Scene.Ticks;
        for (int tick = 0; tick < 1_000 && runtime.ActiveScene?.Id == 3501; tick++)
        {
            lastBinocularFrame = Assert.IsType<CutsceneFrame>(runtime.Frame);
            lastBinocularTick = lastBinocularFrame.Scene.Ticks;
            Assert.True(lastBinocularFrame.BinocularOverlayVisible);
            runtime.Tick();
        }

        Assert.NotEqual(3501, runtime.ActiveScene?.Id);
        Assert.Equal(744, lastBinocularTick);
        CutsceneFrame firstClosingFrame = Assert.IsType<CutsceneFrame>(runtime.Frame);
        Assert.Equal(3502, firstClosingFrame.Scene.Id);
        Assert.Equal(1, firstClosingFrame.Scene.Ticks);
        Assert.False(firstClosingFrame.BinocularOverlayVisible);
    }

    [RequiresGameDataFact]
    public void MissionOneStartsItsAuthoredIntroAndAdvancesItsCamera()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        RegisterCutsceneTypes(loop.Types);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));

        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fileSystem, resources, loop.Natives);
        LevelLoader levels = new(root, loop.TaskCommands, loop.Natives, resources);
        _ = new MissionLoader(missions, fileSystem, resources, levels).Load(1);

        new ConditionalContainerGate(loop.Variables).Prepare(root);
        CutsceneRuntime runtime = new(root, loop.Variables);

        List<CutSceneTask> scenes = Walk(root).OfType<CutSceneTask>().ToList();
        Assert.True(runtime.ActiveScene is not null,
            string.Join(" | ", scenes.Select(scene => $"{scene.Id}: initial={scene.InitialRun} run='{scene.Run}' active={scene.IsRun} finished={scene.IsFinished} parentActive={ConditionalContainerGate.IsTaskActive(scene)}")));
        CutSceneTask scene = runtime.ActiveScene!;
        CutsceneFrame opening = Assert.IsType<CutsceneFrame>(runtime.Frame);
        int before = scene.Ticks;

        // Mission 1's first EditCamera is linked to heli 1502, but retail caches the helicopter's
        // authored transform while parsing the shot. With live == cached at tick zero, linking must
        // leave the authored world pose unchanged. This guards the exact opening view from regressing
        // into the below-terrain/sliver rendering that a reversed link transform produced.
        Assert.InRange(opening.Camera.Position.X, (22_664_462d / WorldScene.UnitsPerMetre) - 0.001,
            (22_664_462d / WorldScene.UnitsPerMetre) + 0.001);
        Assert.InRange(opening.Camera.Position.Y, (-60_994_444d / WorldScene.UnitsPerMetre) - 0.001,
            (-60_994_444d / WorldScene.UnitsPerMetre) + 0.001);
        Assert.InRange(opening.Camera.Position.Z, (174_919_170d / WorldScene.UnitsPerMetre) - 0.001,
            (174_919_170d / WorldScene.UnitsPerMetre) + 0.001);
        Assert.Equal(1f, opening.FieldOfView);
        Assert.InRange(opening.Camera.Orientation.Row2.Z, 0.97f, 0.99f);
        Assert.InRange(opening.Camera.Orientation.Row3.Y, 0.97f, 0.99f);
        Assert.InRange(opening.Camera.Orientation.Row3.Z, -0.22f, -0.19f);

        runtime.Tick();

        Assert.True(runtime.IsPlaying);
        CutsceneFrame next = Assert.IsType<CutsceneFrame>(runtime.Frame);
        Assert.InRange((next.Camera.Position - opening.Camera.Position).Length, 0d, 10d);
        Assert.InRange(next.Camera.Orientation.Row2.Z, 0.97f, 0.99f);
        Assert.InRange(next.Camera.Orientation.Row3.Y, 0.97f, 0.99f);
        Assert.True(scene.Ticks > before);
        Assert.Equal(scene, opening.Scene);
        Assert.InRange(opening.ViewportHeightFactor, 0f, 1f);

        for (int tick = 0; tick < 12_000 && runtime.IsPlaying; tick++)
        {
            runtime.Tick();
        }

        Assert.False(runtime.IsPlaying);
        ConditionalContainerTask gameplay = Walk(root).OfType<ConditionalContainerTask>()
            .First(container => container.Id == 1495);
        Assert.True(gameplay.IsRun);
    }

    [RequiresGameDataFact]
    public void MissionOneSecondLinkedShotMatchesRetailCameraTrace()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        RegisterCutsceneTypes(loop.Types);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fileSystem, resources, loop.Natives);
        _ = new MissionLoader(missions, fileSystem, resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(1);
        VehiclePhysicsRegistry physics = new();
        physics.Load(resources);
        new ConditionalContainerGate(loop.Variables).Prepare(root);
        CutsceneRuntime runtime = new(root, loop.Variables, vehiclePhysics: physics);
        // The retail camera recorded at scene tick N is the one set on the level's (N+1)th tick: a running
        // scene sets its camera from the tick it is on and counts on afterwards, so its first tick shows time
        // nought. One runtime tick more than the scene tick, then.
        for (int tick = 0; tick < 191; tick++)
        {
            runtime.Tick();
        }

        CutsceneFrame frame = Assert.IsType<CutsceneFrame>(runtime.Frame);
        Vector3d expected = new(
            22_784_745.511 / WorldScene.UnitsPerMetre,
            -60_294_163.283 / WorldScene.UnitsPerMetre,
            174_759_830.653 / WorldScene.UnitsPerMetre);
        // The rigid body is float-state and the linked camera compounds its tiny attitude difference
        // over 190 steps. Keep the complete shot within one vehicle length while the tick-30 force/pose
        // test above pins the integrator itself to centimetres. 6.5 m with the camera on the retail tick: it
        // read 5.5 while the port's camera ran a tick ahead of the level, the drift growing a metre a tick.
        Assert.True((frame.Camera.Position - expected).Length <= 7d,
            $"retail={expected}, runtime={frame.Camera.Position}, delta={(frame.Camera.Position - expected).Length:F6}m");
        Assert.Equal(1.00868344f, frame.FieldOfView, precision: 5);
    }

    [RequiresGameDataFact]
    public void MissionOneTrainLandingShotMatchesRetailCameraTrace()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        RegisterCutsceneTypes(loop.Types);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fileSystem, resources, loop.Natives);
        _ = new MissionLoader(missions, fileSystem, resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(1);
        VehiclePhysicsRegistry physics = new();
        physics.Load(resources);
        new ConditionalContainerGate(loop.Variables).Prepare(root);
        CutsceneRuntime runtime = new(root, loop.Variables, vehiclePhysics: physics);

        // The retail camera recorded at scene tick N is the one set on the level's (N+1)th tick: a running
        // scene sets its camera from the tick it is on and counts on afterwards, so its first tick shows time
        // nought. One runtime tick more than the scene tick, then.
        for (int tick = 0; tick < 562; tick++)
        {
            runtime.Tick();
        }

        CutsceneFrame frame = Assert.IsType<CutsceneFrame>(runtime.Frame);
        Assert.Equal(1201, frame.Scene.Id);
        Vector3d expected = new(
            22_189_401.574 / WorldScene.UnitsPerMetre,
            -57_661_741.075 / WorldScene.UnitsPerMetre,
            174_525_506.995 / WorldScene.UnitsPerMetre);
        Assert.True((frame.Camera.Position - expected).Length <= 0.25d,
            $"retail={expected}, runtime={frame.Camera.Position}, delta={(frame.Camera.Position - expected).Length:F6}m");
        Assert.Equal(1.09990954f, frame.FieldOfView, precision: 5);

        HumanSoldierTask jones = Assert.IsType<HumanSoldierTask>(root.Tree.FromId(1666));
        GameTask trainCar = Assert.IsType<DeclaredTask>(root.Tree.FromId(1888));
        Assert.True(runtime.TryAttachActorToTask(
            jones,
            trainCar,
            new Vector3d(22_205_644.0, -57_659_132.0, 174_528_880.0),
            Matrix3f.FromEngineEulerAngles(0f, 0f, jones.Gamma)));
        Assert.True(runtime.TryGetAttachedDrawTransform(jones, out _, out _));
    }

    [RequiresGameDataFact]
    public void MissionOneLandingTrainStopsAtRetailTickSixtySeven()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        RegisterCutsceneTypes(loop.Types);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fileSystem, resources, loop.Natives);
        MagicObjectRegistry magic = new();
        magic.Register(loop.Natives);
        magic.Load(resources, loop.Natives);
        LoadedMission loaded = new MissionLoader(missions, fileSystem, resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(1);
        WorldScene world = WorldScene.Build(loaded, root, magic);
        float Length(string name) => loaded.TryGetModel(name, out MefModel? model) && model is not null
            ? WorldScene.TrainCouplingDistance(model)
            : 0f;
        float Pivot(string name) => loaded.TryGetModel(name, out MefModel? model) && model is not null
            ? WorldScene.TrainCouplingPivotFraction(model)
            : 0f;
        new ConditionalContainerGate(loop.Variables).Prepare(root);
        CutsceneRuntime runtime = new(root, loop.Variables, modelLength: Length, modelPivot: Pivot);

        ObjectCollisionIndex solids = ObjectCollisionIndex.Build(world.Placements);
        foreach (WorldPlacement placement in world.Placements)
        {
            if (runtime.TryGetPlacementDrawTransform(
                placement.Owner,
                placement.Position,
                placement.Orientation,
                out Vector3d placementPosition,
                out Matrix3f placementOrientation))
            {
                solids.UpdateTransform(placement, placementPosition, placementOrientation);
            }
        }

        // The live renderer must not bind a staged actor to dormant geometry. The exact
        // scene-1200 mesh has no active dynamic owner at Jones's authored point; searching
        // inactive owners would select landing car1888 and mask the live attachment bug.
        Assert.False(solids.TryFindContainingDynamicOwner(
            new Vector3d(22_205_644.0, -57_659_132.0, 174_528_880.0),
            includeInactiveOwners: false,
            out _));

        GameTask freightCar = Assert.IsType<DeclaredTask>(root.Tree.FromId(1888));
        Assert.True(runtime.TryGetDrawTransform(freightCar, out Vector3d initialFreightPosition, out _));
        Vector3d retailInitialFreight = new(
            22_198_857.489 / WorldScene.UnitsPerMetre,
            -57_650_405.618 / WorldScene.UnitsPerMetre,
            174_504_640.000 / WorldScene.UnitsPerMetre);
        Assert.True((initialFreightPosition - retailInitialFreight).Length <= 0.01d,
            $"retail initial freight={retailInitialFreight}, runtime={initialFreightPosition}, delta={(initialFreightPosition - retailInitialFreight).Length:R}");

        int ticks = 0;
        while (runtime.ActiveScene?.Id != 1202 && ticks++ < 2_000)
        {
            runtime.Tick();
        }

        Assert.Equal(1202, runtime.ActiveScene?.Id);
        HumanSoldierTask wagonJones = Assert.IsType<HumanSoldierTask>(root.Tree.FromId(2021));
        GameTask wagonCar = Assert.IsType<DeclaredTask>(root.Tree.FromId(2333));
        Assert.True(runtime.TryAttachActorToTask(
            wagonJones,
            wagonCar,
            wagonJones.Position,
            Matrix3f.FromEngineEulerAngles(0f, 0f, wagonJones.Gamma)));
        CutSceneTask landingScene = Assert.IsType<CutSceneTask>(root.Tree.FromId(1201));
        Assert.True(landingScene.IsRun);
        runtime.Tick();
        Assert.False(landingScene.IsRun);
        GameTask landingTrain = Assert.IsType<DeclaredTask>(root.Tree.FromId(1507));
        Assert.True(runtime.TryGetDrawTransform(landingTrain, out Vector3d position, out _));
        Vector3d retail = new(
            22_347_714.470 / WorldScene.UnitsPerMetre,
            -57_326_513.211 / WorldScene.UnitsPerMetre,
            174_504_640.000 / WorldScene.UnitsPerMetre);
        Assert.True((position - retail).Length <= 0.01d,
            $"retail={retail}, runtime={position}, delta={(position - retail).Length:R}");

        Assert.True(runtime.TryGetDrawTransform(freightCar, out Vector3d freightPosition, out _));
        Vector3d retailFreight = new(
            22_232_274.372 / WorldScene.UnitsPerMetre,
            -57_577_695.359 / WorldScene.UnitsPerMetre,
            174_504_640.000 / WorldScene.UnitsPerMetre);
        Assert.True((freightPosition - retailFreight).Length <= 0.01d,
            $"retail freight={retailFreight}, runtime={freightPosition}, delta={(freightPosition - retailFreight).Length:R}");

        // The retail sample is taken when CutScene_1202's update begins with clock 121 -- after the trains ahead
        // of it in the tree (ConditionalContainer_4005) have already moved that frame. The same state here is
        // the end of the tick that brings the scene to 122 (the original; the walk runs the
        // containers before their scenes, so the train's first step comes a tick after the scene starts).
        while (runtime.ActiveScene?.Ticks < 122)
        {
            runtime.Tick();
        }

        Assert.True(runtime.TryGetAttachedDrawTransform(
            wagonJones,
            out Vector3d wagonJonesPosition,
            out _));
        Vector3d retailWagonJones = new(
            22_577_215.109 / WorldScene.UnitsPerMetre,
            -56_832_708.629 / WorldScene.UnitsPerMetre,
            174_511_902.344 / WorldScene.UnitsPerMetre);
        Assert.True((wagonJonesPosition - retailWagonJones).Length <= 0.01d,
            $"retail wagon Jones={retailWagonJones}, runtime={wagonJonesPosition}, delta={(wagonJonesPosition - retailWagonJones).Length:R}");

    }

    /// <summary>Landing-scene Jones (1666) rides wagon 1888 from load through the jump-off, and nothing else.</summary>
    /// <remarks>
    /// He is an AI human standing on 1888's own graph, so the AI branch of the ground query (<c>0x462390</c>) parents
    /// him to the wagon on his first pass -- no ray, no contact -- and never detaches him while that graph is his.
    /// The probe reads parent 1888 on every record of his from load through scene 1203, at the offset below on the
    /// wagon, with 1888 where its consist stands. Modelling the parent as a downward-ray contact that drops an
    /// unregistered owner latched train 1999 instead once the landing consist unregistered.
    /// </remarks>
    [RequiresGameDataFact]
    public void MissionOneLandingJonesRidesWagon1888FromLoadThroughTheJumpOff()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        RegisterCutsceneTypes(loop.Types);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fileSystem, resources, loop.Natives);
        LoadedMission loaded = new MissionLoader(missions, fileSystem, resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(1);
        float Length(string name) => loaded.TryGetModel(name, out MefModel? model) && model is not null
            ? WorldScene.TrainCouplingDistance(model)
            : 0f;
        float Pivot(string name) => loaded.TryGetModel(name, out MefModel? model) && model is not null
            ? WorldScene.TrainCouplingPivotFraction(model)
            : 0f;
        new ConditionalContainerGate(loop.Variables).Prepare(root);
        CutsceneRuntime runtime = new(root, loop.Variables, modelLength: Length, modelPivot: Pivot);
        MissionObjectiveRuntime objectives = new(root, loop.Variables, native: runtime.TryInvokeNative);
        HumanSoldierTask jones = Assert.IsType<HumanSoldierTask>(root.Tree.FromId(1666));
        GameTask car1888 = Assert.IsType<DeclaredTask>(root.Tree.FromId(1888));
        GameTask introContainer = Assert.IsAssignableFrom<GameTask>(root.Tree.FromId(4001));

        // The wagon where its consist stands at load (the probe's Human_SetParent packet), and Jones on it.
        Assert.True(runtime.TryGetDrawTransform(car1888, out Vector3d wagonAtLoad, out _));
        Vector3d retailWagon = new Vector3d(22_198_857.489, -57_650_405.618, 174_504_640.000) / WorldScene.UnitsPerMetre;
        Assert.True((wagonAtLoad - retailWagon).Length <= 0.001d,
            $"retail={retailWagon}, port={wagonAtLoad}, delta={(wagonAtLoad - retailWagon).Length:F6}m");
        Assert.True(runtime.TryAttachActorToTask(jones, car1888, jones.Position, Matrix3f.FromEngineEulerAngles(0f, 0f, jones.Gamma)));

        Vector3d retailLocal = new(-5_095.0, -9_810.5, 24_239.9);
        Assert.True((LocalOnWagon() - retailLocal).Length <= 2d, $"retail local={retailLocal}, port={LocalOnWagon()}");

        int wrongParentTicks = 0;
        double maxDrift = 0d;
        bool sawScene1203 = false;
        int tick = 0;
        while (tick++ < 6_000 && ConditionalContainerGate.IsTaskActive(introContainer))
        {
            objectives.TickLevelState();
            runtime.Tick();
            objectives.TickAfterLevelState();
            runtime.FinishExpressionTick();

            if (!runtime.TryGetAttachmentParent(jones, out GameTask? parent) || !ReferenceEquals(parent, car1888))
            {
                wrongParentTicks++;
            }
            else
            {
                maxDrift = Math.Max(maxDrift, (LocalOnWagon() - retailLocal).Length);
            }

            sawScene1203 |= runtime.ActiveScene?.Id == 1203;
        }

        // No clip plays here (the jumps are the renderer's root motion), so the offset holds while the consist moves.
        Assert.Equal(0, wrongParentTicks);
        Assert.True(maxDrift <= 2d, $"Jones drifted {maxDrift:F1} units on wagon 1888");
        Assert.True(sawScene1203, "scene 1203 was never reached");
        Assert.False(ConditionalContainerGate.IsTaskActive(introContainer));

        // Human_SetParent's local offset: the wagon's transposed frame applied to Jones less the wagon (0x463310).
        Vector3d LocalOnWagon()
        {
            Assert.True(runtime.TryGetDrawTransform(jones, out Vector3d jonesMetres, out _));
            Assert.True(runtime.TryGetDrawTransform(car1888, out Vector3d wagonMetres, out Matrix3f wagonFrame));
            return wagonFrame.Transform((jonesMetres - wagonMetres) * WorldScene.UnitsPerMetre);
        }
    }

    [RequiresGameDataFact]
    public void MissionOneEscapeNativeLeavesTheIntroContainer()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        RegisterCutsceneTypes(loop.Types);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fileSystem, resources, loop.Natives);
        LevelLoader levels = new(root, loop.TaskCommands, loop.Natives, resources);
        _ = new MissionLoader(missions, fileSystem, resources, levels).Load(1);

        new ConditionalContainerGate(loop.Variables).Prepare(root);
        CutsceneRuntime runtime = new(root, loop.Variables);
        MissionObjectiveRuntime objectives = new(root, loop.Variables, native: runtime.TryInvokeNative);
        ConditionalContainerTask intro = Walk(root).OfType<ConditionalContainerTask>()
            .First(container => container.Label.Contains("Intro cutscene", StringComparison.OrdinalIgnoreCase));

        runtime.RequestBreak();
        objectives.TickLevelState();
        runtime.Tick();
        runtime.FinishExpressionTick();
        Assert.False(intro.IsRun);

        // The intro container's falling edge still runs its children once (0x4874E0); its scenes leave the
        // world on the next tick.
        objectives.TickLevelState();
        runtime.Tick();
        runtime.FinishExpressionTick();
        Assert.False(runtime.IsPlaying);
    }

    [RequiresGameDataFact]
    public void MissionOneOutroTakesCameraControlAndCompletesLevelFlow()
    {
        VirtualFileSystem fs = new();
        fs.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fs);
        GameLoop loop = new(new ManualTimeSource());
        RegisterCutsceneTypes(loop.Types);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fs, resources, loop.Natives);
        _ = new MissionLoader(missions, fs, resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(1);

        new ConditionalContainerGate(loop.Variables).Prepare(root);
        CutsceneRuntime runtime = new(root, loop.Variables);
        MissionObjectiveRuntime objectives = new(root, loop.Variables, native: runtime.TryInvokeNative);

        // Leave the intro through the same native edge the Escape key uses, then trigger the authored
        // mission-success variable that registers ConditionalContainer 4007.
        runtime.RequestBreak();
        Step(objectives, runtime);
        Step(objectives, runtime);
        Assert.False(runtime.IsPlaying);
        Assert.True(loop.Variables.TryWrite("EditVariable_199.nValue", QvmValue.FromInt32(1)));
        Step(objectives, runtime);

        Assert.Equal(1204, runtime.ActiveScene?.Id);
        Assert.NotNull(runtime.Frame);
        Assert.True(Walk(root).OfType<ConditionalContainerTask>().Single(c => c.Id == 4007).IsRun);
        Assert.False(Walk(root).OfType<ConditionalContainerTask>().Single(c => c.Id == 1495).IsRun);

        for (int tick = 0; tick < 20_000 && runtime.IsPlaying; tick++)
        {
            Step(objectives, runtime);
        }

        Assert.False(runtime.IsPlaying);
        Assert.True(Walk(root).OfType<CutSceneTask>().Single(scene => scene.Id == 1204).IsFinished);
        Assert.True(objectives.IsComplete);

        static void Step(MissionObjectiveRuntime objectives, CutsceneRuntime runtime)
        {
            objectives.TickLevelState();
            runtime.Tick();
            objectives.TickAfterLevelState();
            runtime.FinishExpressionTick();
        }
    }

    [RequiresGameDataFact]
    public void MissionOneOutroTruckRouteProbe()
    {
        TruckRouteResult closedThenOpen = RunMissionOneTruckRoute(gateStartsOpen: false);
        TruckRouteResult alreadyOpen = RunMissionOneTruckRoute(gateStartsOpen: true);

        Vector3d retailStart = new(
            24_956_676.0 / WorldScene.UnitsPerMetre,
            -56_086_240.0 / WorldScene.UnitsPerMetre,
            174_414_610.0 / WorldScene.UnitsPerMetre);
        Assert.InRange((closedThenOpen.InitialPosition - retailStart).Length, 0d, 0.001d);
        Assert.InRange((alreadyOpen.InitialPosition - retailStart).Length, 0d, 0.001d);

        Assert.True(closedThenOpen.OpenCommandIssued,
            "the authored gate-open moment was not reached");
        Assert.True(closedThenOpen.Gate.State.IsFullyOpen,
            "the authored gate never reached the live fully-open state");
        Assert.Equal(closedThenOpen.Gate.Position, alreadyOpen.Gate.Position);

        AssertAuthoredRightExit(closedThenOpen);
        AssertAuthoredRightExit(alreadyOpen);
    }

    /// <summary>
    /// Mission 2 reuses the Mission 1 truck's model for a different inbound route. The obstacle
    /// probe is now taken from the car's own authored dimensions rather than from a table keyed on
    /// the Mission 1 task, so every car sweeps for obstacles and no level-specific route survives.
    /// </summary>
    [RequiresGameDataFact]
    public void MissionTwoTruckSharingTheModelSweepsWithItsOwnDimensions()
    {
        VirtualFileSystem fs = new();
        fs.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fs);
        GameLoop loop = new(new ManualTimeSource());
        RegisterCutsceneTypes(loop.Types);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fs, resources, loop.Natives);
        _ = new MissionLoader(missions, fs, resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(2);

        new ConditionalContainerGate(loop.Variables).Prepare(root);
        int liveRouteProbes = 0;
        CutsceneRuntime runtime = new(
            root,
            loop.Variables,
            vehicleObstacleHit: (_, _, _) =>
            {
                liveRouteProbes++;
                return null;
            },
            vehicleObstacleReleased: _ => false);
        DeclaredTask truck = Assert.IsType<DeclaredTask>(root.Tree.FromId(778));
        Assert.Equal("Car which Jones drives into base", truck.Label);
        Assert.True(truck.TryGetValue(WorldScene.ModelParameter, out object? model));
        Assert.Equal("622_01_1", model);
        Assert.True(runtime.TryGetDrawTransform(truck, out Vector3d before, out _));

        for (int tick = 0; tick < 60; tick++)
        {
            runtime.Tick();
        }

        Assert.True(runtime.TryGetDrawTransform(truck, out Vector3d after, out _));
        Assert.True((after - before).Length > 0.1d, "the Mission 2 truck did not run its authored cutscene movement");

        // The probe is generic: it runs for this truck too, driven by its own body length, and is
        // no longer gated on the Mission 1 label and model.
        Assert.True(liveRouteProbes > 0, "the Mission 2 truck never swept for obstacles");
    }


    [RequiresGameDataFact]
    public void MissionOneJosefCameraFollowsItsFreightCar()
    {
        VirtualFileSystem fs = new(); fs.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fs); GameLoop loop = new(new ManualTimeSource());
        RegisterCutsceneTypes(loop.Types);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new(); missions.Register(loop.Natives); missions.Scan(fs, resources, loop.Natives);
        LoadedMission loaded = new MissionLoader(missions, fs, resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(1);
        float Length(string name)
        {
            if (!loaded.TryGetModel(name, out MefModel? model) || model is null)
            {
                return 0f;
            }
            return WorldScene.TrainCouplingDistance(model);
        }
        float Pivot(string name)
        {
            return loaded.TryGetModel(name, out MefModel? model) && model is not null
                ? WorldScene.TrainCouplingPivotFraction(model)
                : 0f;
        }
        new ConditionalContainerGate(loop.Variables).Prepare(root);
        CutsceneRuntime runtime = new(root, loop.Variables, modelLength: Length, modelPivot: Pivot);
        MissionObjectiveRuntime objectives = new(root, loop.Variables, native: runtime.TryInvokeNative);
        // The retail camera recorded at scene tick N is the one set on the level's (N+1)th tick: a running
        // scene sets its camera from the tick it is on and counts on afterwards, so its first tick shows time
        // nought. One runtime tick more than the scene tick, then.
        for (int i = 0; i < 783; i++)
        {
            objectives.TickLevelState();
            runtime.Tick();
            objectives.TickAfterLevelState();
            runtime.FinishExpressionTick();
        }

        DeclaredTask car = Walk(root).OfType<DeclaredTask>().First(t => t.Id == 2333);
        DeclaredTask train = Walk(root).OfType<DeclaredTask>().First(t => t.Id == 1999);
        Assert.True(runtime.TryGetDrawTransform(car, out var carPosition, out _));
        Assert.True(runtime.TryGetDrawTransform(train, out var trainPosition, out var trainOrientation));
        CutsceneFrame frame = Assert.IsType<CutsceneFrame>(runtime.Frame);
        Assert.Equal(1202, runtime.ActiveScene?.Id);
        Assert.Equal(3, frame.ShotIndex);
        Assert.InRange((frame.Camera.Position - carPosition).Length, 0d, 30d);
        // The exact CutScene completion contract keeps the post-finish boundary frame.
        // Runtime tick 782 is therefore the same retail camera stamp 940 used by this trace.
        Vector3d retailCamera = new(
            22_633_025.324 / WorldScene.UnitsPerMetre,
            -56_719_312.220 / WorldScene.UnitsPerMetre,
            174_513_097.977 / WorldScene.UnitsPerMetre);
        Assert.True((frame.Camera.Position - retailCamera).Length <= 1d,
            $"retail={retailCamera}, runtime={frame.Camera.Position}, delta={(frame.Camera.Position - retailCamera).Length:R}, fov={frame.FieldOfView:R}, car={carPosition}, train={trainPosition}, trainM={trainOrientation}");
        Assert.Equal(0.991028547f, frame.FieldOfView, precision: 5);
    }

    /// <summary>A car sits on the chord between its couplers on the rail's polyline, where the original puts it.</summary>
    /// <remarks>
    /// The intro consist's first car, 607_01_1 behind lead 2099, stands at the foot of the hill into the valley.
    /// Evaluating the curve at its pivot put it 0.57 m below the original there, and Jones (3031), welded to it at
    /// that moment, was drawn 0.64 m high for the rest of the ride -- inside the crate he leans on. The retail pose
    /// is the probe's <c>Human_SetParent</c> (<c>0x463310</c>) packet for 3031.
    /// </remarks>
    [RequiresGameDataFact]
    public void MissionOneIntroWagonStandsOnItsCouplerChord()
    {
        VirtualFileSystem fs = new(); fs.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fs); GameLoop loop = new(new ManualTimeSource());
        RegisterCutsceneTypes(loop.Types);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new(); missions.Register(loop.Natives); missions.Scan(fs, resources, loop.Natives);
        MagicObjectRegistry magic = new(); magic.Register(loop.Natives); magic.Load(resources, loop.Natives);
        LoadedMission loaded = new MissionLoader(missions, fs, resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(1);
        WorldScene world = WorldScene.Build(loaded, root, magic);
        float Length(string name) => loaded.TryGetModel(name, out MefModel? model) && model is not null
            ? WorldScene.TrainCouplingDistance(model)
            : 0f;
        float Pivot(string name) => loaded.TryGetModel(name, out MefModel? model) && model is not null
            ? WorldScene.TrainCouplingPivotFraction(model)
            : 0f;
        new ConditionalContainerGate(loop.Variables).Prepare(root);
        CutsceneRuntime runtime = new(root, loop.Variables, modelLength: Length, modelPivot: Pivot);

        DeclaredTask wagon = IntroWagonUnderJones(root, runtime);
        Vector3d retail = new Vector3d(24_319_753.157, -55_777_038.666, 174_420_202.524) / WorldScene.UnitsPerMetre;
        float[] retailOrientation =
        [
            0.982984423f, 0.0016115799f, 0.1836818f,
            -0.00158416002f, 0.999998688f, -0.000296018319f,
            -0.183682039f, 1.50080468e-11f, 0.982985675f,
        ];

        WorldPlacement placement = world.Placements.First(placement => ReferenceEquals(placement.Owner, wagon));
        Assert.True((placement.Position - retail).Length <= 0.001d,
            $"retail={retail}, port={placement.Position}, delta={(placement.Position - retail).Length:F6}m");
        Matrix3f m = placement.Orientation;
        float[] port = [m.M11, m.M12, m.M13, m.M21, m.M22, m.M23, m.M31, m.M32, m.M33];
        for (int i = 0; i < 9; i++)
        {
            Assert.True(MathF.Abs(port[i] - retailOrientation[i]) <= 1e-5f, $"M[{i}]: retail={retailOrientation[i]}, port={port[i]}");
        }

        Assert.True(runtime.TryGetDrawTransform(wagon, out Vector3d live, out _));
        Assert.InRange((live - placement.Position).Length, 0d, 0.0001d);
    }

    /// <summary>A Human attached to a pitched parent leans the way the original's conjugated frame leans him.</summary>
    /// <remarks>
    /// Human_SetParent (<c>0x463310</c>) keeps <c>H0 * P0^T</c> and <c>0x4610C0</c> rebuilds the Human as <c>P * that</c>
    /// every tick. Attached to wagon 607_01_1 while it is pitched 10.6 degrees down the hill, Jones (3031) leans 21
    /// degrees at once, which the probe reads at scene 1. Composing parent-relative (<c>P * P0^T * H0</c>) kept him
    /// upright there and leaning the wrong way on the flat, which turned his climb-down clip's root steps.
    /// </remarks>
    [RequiresGameDataFact]
    public void MissionOneIntroJonesLeansWithTheWagonHeIsAttachedTo()
    {
        VirtualFileSystem fs = new(); fs.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fs); GameLoop loop = new(new ManualTimeSource());
        RegisterCutsceneTypes(loop.Types);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new(); missions.Register(loop.Natives); missions.Scan(fs, resources, loop.Natives);
        LoadedMission loaded = new MissionLoader(missions, fs, resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(1);
        float Length(string name) => loaded.TryGetModel(name, out MefModel? model) && model is not null
            ? WorldScene.TrainCouplingDistance(model)
            : 0f;
        float Pivot(string name) => loaded.TryGetModel(name, out MefModel? model) && model is not null
            ? WorldScene.TrainCouplingPivotFraction(model)
            : 0f;
        new ConditionalContainerGate(loop.Variables).Prepare(root);
        CutsceneRuntime runtime = new(root, loop.Variables, modelLength: Length, modelPivot: Pivot);

        HumanSoldierTask jones = Assert.IsType<HumanSoldierTask>(root.Tree.FromId(3031));
        DeclaredTask wagon = IntroWagonUnderJones(root, runtime);
        Assert.True(runtime.TryAttachActorToTask(jones, wagon, jones.Position, Matrix3f.FromEngineEulerAngles(0f, 0f, jones.Gamma)));
        Assert.True(runtime.TryGetAttachedDrawTransform(jones, out _, out Matrix3f leaning));

        float[] retail =
        [
            -0.91030f, 0.20949f, 0.35702f,
            -0.20970f, -0.97700f, 0.03859f,
            0.35689f, -0.03974f, 0.93329f,
        ];
        float[] port = [leaning.M11, leaning.M12, leaning.M13, leaning.M21, leaning.M22, leaning.M23, leaning.M31, leaning.M32, leaning.M33];
        for (int i = 0; i < 9; i++)
        {
            Assert.True(MathF.Abs(port[i] - retail[i]) <= 1e-4f, $"M[{i}]: retail={retail[i]}, port={port[i]}");
        }
    }

    /// <summary>Mission 10's two parked consists stand where the original settles them, car by car.</summary>
    /// <remarks>
    /// The probe's <c>train</c> hook set reads each car's committed pose at Train::UpdateConsist's world commit
    /// (<c>0x42A66B</c> for a lead, <c>0x42AAB3</c> behind it) once every car has initialised. Its rail (870) is
    /// automatically oriented (<c>0x500EF0</c>) and climbs and turns under the second consist, so it exercises the
    /// polyline walk, the chord frames and the waypoint frames together.
    /// </remarks>
    [RequiresGameDataFact]
    public void MissionTenConsistsStandWhereTheOriginalSettlesThem()
    {
        VirtualFileSystem fs = new(); fs.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fs); GameLoop loop = new(new ManualTimeSource());
        RegisterCutsceneTypes(loop.Types);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new(); missions.Register(loop.Natives); missions.Scan(fs, resources, loop.Natives);
        MagicObjectRegistry magic = new(); magic.Register(loop.Natives); magic.Load(resources, loop.Natives);
        LoadedMission loaded = new MissionLoader(missions, fs, resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(10);
        WorldScene world = WorldScene.Build(loaded, root, magic);

        (int Lead, string Model, Vector3d Position, float[] Orientation)[] retail =
        [
            (666, "605_01_1", new(6_962_140.833, 152_980_859.470, 174_503_575.711), [0.725996f, -0.687540f, -0.014760f, 0.687398f, 0.726146f, -0.013975f, 0.020326f, 0f, 0.999793f]),
            (666, "613_01_1", new(6_915_451.728, 152_930_135.191, 174_502_796.289), [0.625440f, -0.780270f, -0.001426f, 0.780268f, 0.625442f, -0.001779f, 0.002280f, 0f, 0.999997f]),
            (666, "608_01_1", new(6_874_934.224, 152_874_249.336, 174_503_017.088), [0.547329f, -0.836903f, 0.004728f, 0.836872f, 0.547350f, 0.007230f, -0.008639f, 0f, 0.999962f]),
            (666, "606_01_1", new(6_838_411.555, 152_815_603.859, 174_503_803.981), [0.509719f, -0.860310f, 0.007196f, 0.860225f, 0.509769f, 0.012145f, -0.014117f, 0f, 0.999900f]),
            (666, "607_01_1", new(6_803_872.076, 152_755_758.847, 174_504_912.865), [0.489879f, -0.871745f, 0.008800f, 0.871605f, 0.489958f, 0.015657f, -0.017961f, 0f, 0.999838f]),
            (666, "609_01_1", new(6_769_986.940, 152_695_539.566, 174_506_216.651), [0.490724f, -0.871260f, 0.009700f, 0.871090f, 0.490820f, 0.017219f, -0.019763f, 0f, 0.999804f]),
            (300, "605_01_1", new(9_127_879.197, 152_980_582.059, 174_438_968.451), [0.057859f, 0.998295f, 0.007579f, -0.989839f, 0.058353f, -0.129666f, -0.129887f, 0f, 0.991528f]),
            (300, "606_01_1", new(9_110_593.178, 153_184_865.032, 174_469_449.411), [0.127097f, 0.991686f, 0.020109f, -0.979501f, 0.128678f, -0.154977f, -0.156276f, 0f, 0.987713f]),
            (300, "609_01_1", new(9_081_494.642, 153_316_715.159, 174_496_749.894), [0.332743f, 0.940300f, 0.071530f, -0.919299f, 0.340344f, -0.197622f, -0.210169f, 0f, 0.977664f]),
        ];

        foreach ((int lead, string model, Vector3d position, float[] orientation) in retail)
        {
            WorldPlacement car = world.Placements.Single(placement =>
                placement.Owner is DeclaredTask { Type.Name: "Train" } train
                && (train.Id == lead || train.Parent?.Id == lead)
                && string.Equals(placement.Instance.Name, model, StringComparison.OrdinalIgnoreCase));
            Vector3d expected = position / WorldScene.UnitsPerMetre;
            Assert.True((car.Position - expected).Length <= 0.002d,
                $"{lead}/{model}: retail={expected}, port={car.Position}, delta={(car.Position - expected).Length:F6}m");
            Matrix3f m = car.Orientation;
            float[] port = [m.M11, m.M12, m.M13, m.M21, m.M22, m.M23, m.M31, m.M32, m.M33];
            for (int i = 0; i < 9; i++)
            {
                Assert.True(MathF.Abs(port[i] - orientation[i]) <= 2e-5f, $"{lead}/{model} M[{i}]: retail={orientation[i]}, port={port[i]}");
            }
        }
    }

    [RequiresGameDataFact]
    public void MissionOneFreightCarSceneMatchesItsRuntimeRailPose()
    {
        VirtualFileSystem fs = new(); fs.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fs); GameLoop loop = new(new ManualTimeSource());
        RegisterCutsceneTypes(loop.Types);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new(); missions.Register(loop.Natives); missions.Scan(fs, resources, loop.Natives);
        MagicObjectRegistry magic = new(); magic.Register(loop.Natives); magic.Load(resources, loop.Natives);
        LoadedMission loaded = new MissionLoader(missions, fs, resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(1);
        WorldScene world = WorldScene.Build(loaded, root, magic);
        float Length(string name)
        {
            if (!loaded.TryGetModel(name, out MefModel? model) || model is null)
            {
                return 0f;
            }
            return WorldScene.TrainCouplingDistance(model);
        }
        float Pivot(string name)
        {
            return loaded.TryGetModel(name, out MefModel? model) && model is not null
                ? WorldScene.TrainCouplingPivotFraction(model)
                : 0f;
        }
        new ConditionalContainerGate(loop.Variables).Prepare(root);
        CutsceneRuntime runtime = new(root, loop.Variables, modelLength: Length, modelPivot: Pivot);
        DeclaredTask car = Walk(root).OfType<DeclaredTask>().First(task => task.Id == 2333);
        Assert.True(runtime.TryGetDrawTransform(car, out Vector3d initialCarPosition, out _));
        WorldPlacement carPlacement = world.Placements.First(placement => ReferenceEquals(placement.Owner, car));
        Assert.InRange((carPlacement.Position - initialCarPosition).Length, 0d, 0.0001d);

        MissionObjectiveRuntime objectives = new(root, loop.Variables, native: runtime.TryInvokeNative);
        for (int tick = 0; tick < 2250; tick++)
        {
            objectives.TickLevelState();
            runtime.Tick();
            objectives.TickAfterLevelState();
            runtime.FinishExpressionTick();
        }

        CutsceneFrame frame = Assert.IsType<CutsceneFrame>(runtime.Frame);
        Assert.Equal(18, frame.ShotIndex);
        Assert.True(runtime.TryGetDrawTransform(car, out Vector3d carPosition, out Matrix3f carOrientation));
        Vector3d cameraLocal = carOrientation.Transform(frame.Camera.Position - carPosition);
        Assert.True(loaded.TryGetModel("609_01_1", out MefModel? carModel));
        float minX = float.MaxValue;
        float maxX = float.MinValue;
        foreach (MefVertex vertex in carModel!.Vertices)
        {
            minX = Math.Min(minX, vertex.Position.X);
            maxX = Math.Max(maxX, vertex.Position.X);
        }
        minX /= (float)WorldScene.UnitsPerMetre;
        maxX /= (float)WorldScene.UnitsPerMetre;
        Assert.InRange(cameraLocal.X, minX, maxX);
    }

    [RequiresGameDataFact]
    public void CampaignCutsceneNativeSurfaceStaysWithinTheImplementedSet()
    {
        HashSet<string> names = new(StringComparer.Ordinal);
        for (int missionId = 1; missionId <= 14; missionId++)
        {
            VirtualFileSystem fs = new(); fs.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
            ResourceRegistry resources = new(fs); GameLoop loop = new(new ManualTimeSource());
            RegisterCutsceneTypes(loop.Types);
            LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
            MissionRegistry missions = new(); missions.Register(loop.Natives); missions.Scan(fs, resources, loop.Natives);
            _ = new MissionLoader(missions, fs, resources,
                new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(missionId);
            foreach (GameTask task in Walk(root))
            {
                if (!IsCinematic(task))
                {
                    continue;
                }
                IEnumerable<string> texts = task switch
                {
                    CutSceneTask scene => [scene.Run, scene.Reset, scene.TimeDelta,
                        scene.StartExpression, scene.StopExpression],
                    ConditionalContainerTask container => [container.Condition, container.RunAtStart, container.RunAtStop],
                    DeclaredTask declared => declared.Type.Parameters
                        .Select((_, i) => declared.GetValue(i)).OfType<string>(),
                    _ => [],
                };
                foreach (string text in texts)
                {
                    foreach (System.Text.RegularExpressions.Match match in
                        System.Text.RegularExpressions.Regex.Matches(text, @"\b([A-Za-z_][A-Za-z0-9_]*)\s*\("))
                    {
                        names.Add(match.Groups[1].Value);
                    }
                }
            }
        }
        Assert.Equal(
            ["Game_CutsceneDelete", "Game_DisableMusic", "Game_EnableMusic", "MenuManager_SetEnabled"],
            names.Order(StringComparer.Ordinal));

        static bool IsCinematic(GameTask task)
        {
            for (GameTask? current = task; current is not null; current = current.Parent)
            {
                if (current is CutSceneTask)
                {
                    return true;
                }
                if (current is ConditionalContainerTask container
                    && (container.Label.Contains("cutscene", StringComparison.OrdinalIgnoreCase)
                        || container.Label.Contains("cut scene", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>The 607_01_1 wagon seated Jones (3031) is on, behind lead 2099: the level has a dozen such cars.</summary>
    private static DeclaredTask IntroWagonUnderJones(GameTask root, CutsceneRuntime runtime)
    {
        HumanSoldierTask jones = Assert.IsType<HumanSoldierTask>(root.Tree.FromId(3031));
        Vector3d seat = jones.Position / WorldScene.UnitsPerMetre;
        return Walk(root).OfType<DeclaredTask>()
            .Where(task => task.Type.Name == "Train" && task.TryGetValue("Model", out object? model) && model is "607_01_1")
            .MinBy(task => runtime.TryGetDrawTransform(task, out Vector3d at, out _)
                ? ((at.X - seat.X) * (at.X - seat.X)) + ((at.Y - seat.Y) * (at.Y - seat.Y))
                : double.MaxValue)!;
    }

    private static IEnumerable<GameTask> Walk(GameTask root)
    {
        foreach (GameTask child in root.Children)
        {
            yield return child;
            foreach (GameTask nested in Walk(child))
            {
                yield return nested;
            }
        }
    }

    private static TruckRouteResult RunMissionOneTruckRoute(bool gateStartsOpen)
    {
        VirtualFileSystem fs = new();
        fs.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fs);
        GameLoop loop = new(new ManualTimeSource());
        RegisterCutsceneTypes(loop.Types);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fs, resources, loop.Natives);
        MagicObjectRegistry magic = new();
        magic.Register(loop.Natives);
        magic.Load(resources, loop.Natives);
        VehiclePhysicsRegistry physics = new();
        physics.Load(resources);
        LoadedMission mission = new MissionLoader(missions, fs, resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(1);
        WorldScene world = WorldScene.Build(mission, root, magic);
        DoorRegistry doors = DoorRegistry.Build(root);
        ObjectCollisionIndex solids = ObjectCollisionIndex.Build(world.Placements);

        new ConditionalContainerGate(loop.Variables).Prepare(root);
        DeclaredTask truck = Assert.IsType<DeclaredTask>(root.Tree.FromId(1099));
        Assert.Equal("Truck which Jones will steal", truck.Label);
        Assert.True(truck.TryGetValue(WorldScene.ModelParameter, out object? model));
        Assert.Equal("622_01_1", model);

        PlacedDoor gate = Assert.Single(doors.Doors, door => door.Task.Id == 401);
        Assert.Equal(new Vector3d(24_979_312.0, -56_202_112.0, 174_413_120.0), gate.Position);
        if (gateStartsOpen)
        {
            gate.State.CommandOpen();
            for (int tick = 0; tick < 300 && !gate.State.IsFullyOpen; tick++)
            {
                doors.Tick();
                RefreshDoorCollision(solids, doors);
            }
            Assert.True(gate.State.IsFullyOpen, "the authored gate did not reach its open state");
        }

        bool openCommandIssued = false;
        CutsceneRuntime runtime = new(
            root,
            loop.Variables,
            vehiclePhysics: physics,
            vehicleObstacleHit: (task, origin, end) =>
                Contact(solids, task, origin, end),
            vehicleObstacleReleased: obstacle => doors.Doors
                .FirstOrDefault(door => ReferenceEquals(door.Task, obstacle))?.State.IsFullyOpen == true);
        MissionObjectiveRuntime objectives = new(root, loop.Variables, native: runtime.TryInvokeNative);
        RefreshCutsceneCollision(solids, world, runtime);
        RefreshDoorCollision(solids, doors);
        AssertGateCollisionState(solids, gate, expected: !gateStartsOpen);

        runtime.RequestBreak();
        Step(objectives, runtime, solids, world, doors, gate, ref openCommandIssued);
        Assert.True(loop.Variables.TryWrite("EditVariable_199.nValue", QvmValue.FromInt32(1)));
        Step(objectives, runtime, solids, world, doors, gate, ref openCommandIssued);
        Assert.Equal(1204, runtime.ActiveScene?.Id);
        Assert.True(runtime.TryGetDrawTransform(truck, out Vector3d initialPosition, out Matrix3f initialOrientation));

        for (int tick = 0; tick < 600 && runtime.ActiveScene is not null; tick++)
        {
            Step(objectives, runtime, solids, world, doors, gate, ref openCommandIssued);
        }

        Assert.True(runtime.TryGetDrawTransform(truck, out Vector3d finalPosition, out Matrix3f finalOrientation));
        AssertGateCollisionState(solids, gate, expected: false);
        return new TruckRouteResult(
            initialPosition,
            initialOrientation.Column2,
            finalPosition,
            finalOrientation.Column2,
            gate,
            openCommandIssued);

        static (GameTask Owner, WorldRayHit Hit, string Label)? Contact(
            ObjectCollisionIndex solids,
            GameTask ignoredOwner,
            Vector3d origin,
            Vector3d end)
        {
            if (!solids.TryCastIgnoring(ignoredOwner, origin, end, out WorldRayHit hit, out GameTask? owner, out string label)
                || owner is null)
            {
                return null;
            }

            return (owner, hit, label);
        }

        static void Step(
            MissionObjectiveRuntime objectives,
            CutsceneRuntime runtime,
            ObjectCollisionIndex solids,
            WorldScene world,
            DoorRegistry doors,
            PlacedDoor gate,
            ref bool openCommandIssued)
        {
            objectives.TickLevelState();
            runtime.Tick();
            RefreshCutsceneCollision(solids, world, runtime);
            if (runtime.ActiveScene?.Ticks >= 11 * CutsceneRuntime.Frequency
                && !gate.State.IsFullyOpen
                && !openCommandIssued)
            {
                gate.State.CommandOpen();
                openCommandIssued = true;
            }
            doors.Tick();
            RefreshDoorCollision(solids, doors);
            objectives.TickAfterLevelState();
            runtime.FinishExpressionTick();
        }
    }

    private static void RefreshCutsceneCollision(ObjectCollisionIndex solids, WorldScene world, CutsceneRuntime runtime)
    {
        foreach (WorldPlacement placement in world.Placements)
        {
            if (runtime.TryGetPlacementDrawTransform(
                placement.Owner,
                placement.Position,
                placement.Orientation,
                out Vector3d position,
                out Matrix3f orientation))
            {
                solids.UpdateTransform(placement, position, orientation);
            }
        }
    }

    private static void RefreshDoorCollision(ObjectCollisionIndex solids, DoorRegistry doors)
    {
        foreach (PlacedDoor door in doors.Doors)
        {
            solids.SetPassable(door.Task, door.State.IsFullyOpen);
            solids.UpdateTransform(door.Task, door.DrawPosition, door.DrawOrientation);
        }
    }

    private static void AssertGateCollisionState(
        ObjectCollisionIndex solids,
        PlacedDoor gate,
        bool expected)
    {
        Vector3d centre = gate.Position;
        bool hitGate = false;
        for (double x = -8d; x <= 8d && !hitGate; x += 1d)
        {
            for (double y = -8d; y <= 8d && !hitGate; y += 1d)
            {
                for (double z = 0.25d; z <= 4d; z += 0.5d)
                {
                    Vector3d point = centre + new Vector3d(x, y, z) * WorldScene.UnitsPerMetre;
                    if (solids.IsPointInTaskVolume(point, gate.Task))
                    {
                        hitGate = true;
                        break;
                    }
                }
            }
        }

        Assert.Equal(expected, hitGate);
    }

    private static void AssertAuthoredRightExit(TruckRouteResult route)
    {
        float turnCross = (route.InitialForward.X * route.FinalForward.Y)
            - (route.InitialForward.Y * route.FinalForward.X);
        double travelled = (route.FinalPosition - route.InitialPosition).Length;
        Assert.True(travelled > 10d,
            $"truck did not leave its authored garage route: start={route.InitialPosition}, end={route.FinalPosition}");
        Assert.True(turnCross > 0f && route.FinalForward.X > 0f,
            $"truck did not take the authored right exit: initial={route.InitialForward}, final={route.FinalForward}, cross={turnCross}");
    }

    private sealed record TruckRouteResult(
        Vector3d InitialPosition,
        Vector3f InitialForward,
        Vector3d FinalPosition,
        Vector3f FinalForward,
        PlacedDoor Gate,
        bool OpenCommandIssued);

    /// <summary>
    /// Mission 4's parked Bell rests on its pad at the authored pose instead of sinking through the
    /// terrain while its collective is below hover, then leaves the pad along the authored path
    /// once cyclic pitches the disc. Retail's Heli collision flag grounds the airframe; excess
    /// collective alone must not launch it up the world Z axis.
    /// </summary>
    [RequiresGameDataFact]
    public void MissionFourParkedHelicopterRestsOnTerrainThenLiftsOff()
    {
        VirtualFileSystem fs = new(); fs.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fs); GameLoop loop = new(new ManualTimeSource());
        RegisterCutsceneTypes(loop.Types);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new(); missions.Register(loop.Natives); missions.Scan(fs, resources, loop.Natives);
        LoadedMission mission = new MissionLoader(
            missions,
            fs,
            resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources),
            terrainLoader: new TerrainLoader(resources, loop.Natives)).Load(4);
        MagicObjectRegistry magic = new(); magic.Register(loop.Natives); magic.Load(resources, loop.Natives);
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



    private static void RegisterCutsceneTypes(TaskTypeRegistry types)
    {
        PlayerTaskTypes.RegisterAll(types);
        types.Register<CutSceneTask>(CutSceneTask.TypeName);
        types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);
    }
}
