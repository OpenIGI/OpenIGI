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
/// Mission 1's intro against retail: its camera trace, the landing train, the wagons and the Joneses
/// attached to them, and the escape out of it.
/// </summary>
public sealed class RetailCutsceneMissionOneIntroTests
{
    [RequiresGameDataFact]
    public void MissionOneAttachedHumanConsumesItsAnimationRootMotion()
    {
        RetailMissionFixture fixture = RetailMissionFixture.Open();
        GameLoop loop = fixture.Loop;
        LevelTask root = fixture.Root;
        _ = fixture.Load(1);
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
    public void MissionOneConditionalSoundEdgesKeepTheirRetailKinds()
    {
        RetailMissionFixture fixture = RetailMissionFixture.Open();
        GameLoop loop = fixture.Loop;
        LevelTask root = fixture.Root;
        _ = fixture.Load(1);
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

    [RequiresGameDataFact]
    public void MissionOneStartsItsAuthoredIntroAndAdvancesItsCamera()
    {
        RetailMissionFixture fixture = RetailMissionFixture.Open();
        GameLoop loop = fixture.Loop;
        LevelTask root = fixture.Root;
        _ = fixture.Load(1);

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
        RetailMissionFixture fixture = RetailMissionFixture.Open();
        GameLoop loop = fixture.Loop;
        LevelTask root = fixture.Root;
        ResourceRegistry resources = fixture.Resources;
        _ = fixture.Load(1);
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
        RetailMissionFixture fixture = RetailMissionFixture.Open();
        GameLoop loop = fixture.Loop;
        LevelTask root = fixture.Root;
        ResourceRegistry resources = fixture.Resources;
        _ = fixture.Load(1);
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
        RetailMissionFixture fixture = RetailMissionFixture.Open();
        GameLoop loop = fixture.Loop;
        LevelTask root = fixture.Root;
        MagicObjectRegistry magic = fixture.LoadMagic();
        LoadedMission loaded = fixture.Load(1);
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
        // the end of the tick that brings the scene to 122 (the walk runs the containers before their scenes,
        // so the train's first step comes a tick after the scene starts).
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
        RetailMissionFixture fixture = RetailMissionFixture.Open();
        GameLoop loop = fixture.Loop;
        LevelTask root = fixture.Root;
        LoadedMission loaded = fixture.Load(1);
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
        RetailMissionFixture fixture = RetailMissionFixture.Open();
        GameLoop loop = fixture.Loop;
        LevelTask root = fixture.Root;
        _ = fixture.Load(1);

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
    public void MissionOneJosefCameraFollowsItsFreightCar()
    {
        RetailMissionFixture fixture = RetailMissionFixture.Open();
        GameLoop loop = fixture.Loop;
        LevelTask root = fixture.Root;
        LoadedMission loaded = fixture.Load(1);
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
        RetailMissionFixture fixture = RetailMissionFixture.Open();
        GameLoop loop = fixture.Loop;
        LevelTask root = fixture.Root;
        MagicObjectRegistry magic = fixture.LoadMagic();
        LoadedMission loaded = fixture.Load(1);
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
        RetailMissionFixture fixture = RetailMissionFixture.Open();
        GameLoop loop = fixture.Loop;
        LevelTask root = fixture.Root;
        LoadedMission loaded = fixture.Load(1);
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

    [RequiresGameDataFact]
    public void MissionOneFreightCarSceneMatchesItsRuntimeRailPose()
    {
        RetailMissionFixture fixture = RetailMissionFixture.Open();
        GameLoop loop = fixture.Loop;
        LevelTask root = fixture.Root;
        MagicObjectRegistry magic = fixture.LoadMagic();
        LoadedMission loaded = fixture.Load(1);
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
}
