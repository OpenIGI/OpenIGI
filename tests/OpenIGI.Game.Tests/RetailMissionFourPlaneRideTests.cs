using System.Collections;
using System.Reflection;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Mission 4 insertion ride: the airframe travels authored-forward, staged friendlies ride it,
/// and the body rests on terrain instead of sinking forever.
/// </summary>
/// <remarks>
/// QVM dump of <c>missions/location0/level4/objects.qvm</c> has no <c>Plane</c> type — transport is
/// Heli 356 / pad 800. Mission 3 still ships Plane tasks, so the Plane callback registered by r2
/// at <c>0x4330F0</c> and beginning at <c>0x433690</c> is pinned there. Friendlies live under
/// <c>Friendly Forces</c>, <c>Guys running for heli</c>, and <c>Harrison &amp; Men in Heli</c>;
/// container 300 + <c>AnimTask_850.nTick</c> later activates Harrison 400–405 for combat.
/// </remarks>
public sealed class RetailMissionFourPlaneRideTests
{
    [RequiresGameDataFact]
    public void MissionThreePilotKeepsItsAuthoredCabinOffsetWhenHelicopterParks()
    {
        (CutsceneRuntime runtime, LevelTask root, _, _) = LoadMission(3);
        DeclaredTask heli = Assert.IsType<DeclaredTask>(root.Tree.FromId(603));
        DeclaredTask pilot = Assert.Single(heli.Children.OfType<DeclaredTask>(),
            child => child.TryGetValue("Model", out object? model) && Equals(model, "715_01_1"));
        Assert.True(heli.TryGetValue("Position", out object? heliPosition));
        Assert.True(pilot.TryGetValue("Position", out object? pilotPosition));
        double authoredDistance = (Assert.IsType<Vector3d>(pilotPosition)
            - Assert.IsType<Vector3d>(heliPosition)).Length / WorldScene.UnitsPerMetre;
        Assert.True(runtime.TryGetDrawTransform(heli, out Vector3d heliDraw, out _));
        Assert.True(runtime.TryGetDrawTransform(pilot, out Vector3d pilotDraw, out _));
        Assert.InRange(Math.Abs((pilotDraw - heliDraw).Length - authoredDistance), 0d, 0.00001d);
    }

    [RequiresGameDataFact]
    public void MissionThreePlanesMoveAlongAuthoredForwardAfterTickZero()
    {
        (CutsceneRuntime runtime, LevelTask root, _, _) = LoadMission(3);
        ForceAnimations(runtime);

        int planes = 0;
        foreach (GameTask task in Walk(root))
        {
            if (task is not DeclaredTask || task.Type.Name != "Plane")
            {
                continue;
            }

            if (!runtime.TryGetDrawTransform(task, out Vector3d origin, out Matrix3f authored))
            {
                continue;
            }

            Vector3f forward = authored.Row2;
            WritePlaneHold(runtime, task.Id, thrust: 0.8f);

            for (int i = 0; i < 12; i++)
            {
                runtime.Tick();
                runtime.FinishExpressionTick();
            }

            Assert.True(runtime.TryGetDrawTransform(task, out Vector3d later, out _));
            Vector3d delta = later - origin;
            double along = (delta.X * forward.X) + (delta.Y * forward.Y) + (delta.Z * forward.Z);
            double against = -along;
            Assert.True(
                along > 1.0 && along > against,
                $"plane #{task.Id} did not travel authored +Y (Row2). along={along:R} delta=({delta.X:R},{delta.Y:R},{delta.Z:R}) fwd=({forward.X:R},{forward.Y:R},{forward.Z:R})");
            Assert.True(
                runtime.TryGetDynamicVelocity(task, out Vector3d velocity) && velocity.Length > 0.01d,
                $"plane #{task.Id} must publish its integrated velocity while moving; velocity={velocity}");
            planes++;
        }

        Assert.True(planes >= 1, "mission 3 must still ship a Plane so the receiver can be pinned");
    }

    [RequiresGameDataFact]
    public void MissionFourHeliThreeFiveSixRidersAreQvmChildrenAndStaged()
    {
        (CutsceneRuntime runtime, LevelTask root, TerrainData? _, ObjectCollisionIndex _) = LoadMission(4);
        GameTask heli = Assert.IsAssignableFrom<GameTask>(root.Tree.FromId(356));
        List<HumanSoldierTask> cabin = [.. heli.Children.OfType<HumanSoldierTask>()];
        Assert.True(cabin.Count >= 5, "Heli 356 authors cabin Humans 1234–1238 as QVM children");
        Assert.All(cabin, human =>
        {
            Assert.True(CinematicActor.IsStaged(human), $"cabin human mind={human.Children.OfType<HumanAiTask>().FirstOrDefault()?.Id} must be staged via Heli ancestor");
            Assert.True(
                runtime.TryGetAttachedDrawTransform(human, out _, out _),
                "QVM parent attach must bind NoId cabin Humans to Heli 356");
        });
        HashSet<int> minds = [.. cabin.Select(h => h.Children.OfType<HumanAiTask>().First().Id)];
        Assert.Contains(1234, minds);
        Assert.Contains(1238, minds);
    }

    [RequiresGameDataFact]
    public void MissionFourStagedFriendliesRideTheMovingTransport()
    {
        (CutsceneRuntime runtime, LevelTask root, TerrainData? _, ObjectCollisionIndex _) = LoadMission(4);
        if (root.Tree.FromId(3902) is ConditionalContainerTask transport)
        {
            transport.Condition = "1";
        }

        runtime.Tick();
        runtime.FinishExpressionTick();

        List<HumanSoldierTask> riders = [];
        foreach (GameTask task in Walk(root))
        {
            if (task is HumanSoldierTask human && CinematicActor.IsStaged(human))
            {
                riders.Add(human);
            }
        }

        Assert.True(riders.Count > 0, "M4 QVM Friendly Forces / heli riders must be staged");

        Dictionary<HumanSoldierTask, Vector3d> before = [];
        foreach (HumanSoldierTask rider in riders)
        {
            if (!runtime.TryGetAttachedDrawTransform(rider, out Vector3d pos, out _))
            {
                continue;
            }

            before[rider] = pos;
        }

        Assert.True(before.Count > 0, "staged M4 friendlies must parent to the vehicle at tick 0 (QVM child or staged sibling)");

        GameTask? mover = null;
        Vector3d moverBefore = default;
        foreach (int id in new[] { 356, 800 })
        {
            if (root.Tree.FromId(id) is GameTask heli
                && runtime.TryGetDrawTransform(heli, out Vector3d heliPos, out _))
            {
                mover = heli;
                moverBefore = heliPos;
                break;
            }
        }

        Assert.NotNull(mover);
        riders = [.. riders.Where(rider => HasAncestor(rider, mover!))];
        before = before
            .Where(pair => HasAncestor(pair.Key, mover!))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        Assert.True(before.Count > 0, "QVM children of the moving heli must be attached");

        for (int tick = 0; tick < 90; tick++)
        {
            runtime.Tick();
            runtime.FinishExpressionTick();
        }

        Assert.True(runtime.TryGetDrawTransform(mover!, out Vector3d moverAfter, out _));
        Vector3d moverDelta = moverAfter - moverBefore;
        if (moverDelta.Length < 0.5)
        {
            return;
        }

        int tracked = 0;
        foreach ((HumanSoldierTask rider, Vector3d start) in before)
        {
            Assert.True(runtime.TryGetAttachedDrawTransform(rider, out Vector3d later, out _));
            Vector3d delta = later - start;
            double follow = (delta.X * moverDelta.X) + (delta.Y * moverDelta.Y) + (delta.Z * moverDelta.Z);
            Assert.True(
                follow > 0.0,
                $"friendly mind={rider.Children.OfType<HumanAiTask>().FirstOrDefault()?.Id} stayed put while transport moved {moverDelta}");
            tracked++;
        }

        Assert.True(tracked > 0);
    }

    [RequiresGameDataFact]
    public void MissionThreePlanesRestOnTerrainInsteadOfSinking()
    {
        (CutsceneRuntime runtime, LevelTask root, TerrainData? terrain, ObjectCollisionIndex _) = LoadMission(3);
        Assert.NotNull(terrain);
        Assert.True(terrain!.IsUsable);
        ForceAnimations(runtime);

        GameTask[] planes = Walk(root)
            .Where(task => task is DeclaredTask
                && task.Type.Name == "Plane"
                && runtime.TryGetDrawTransform(task, out _, out _))
            .ToArray();
        Assert.True(planes.Length > 0, "mission 3 must drive a Plane through CutsceneRuntime");

        foreach (GameTask plane in planes)
        {
            WritePlaneHold(runtime, plane.Id, thrust: 0.8f);
        }

        Dictionary<int, double> previousZ = [];
        Dictionary<int, int> fallingTicks = [];
        int nearGroundSamples = 0;
        for (int tick = 0; tick < 8_000; tick++)
        {
            runtime.Tick();
            runtime.FinishExpressionTick();
            if (tick % 20 != 0)
            {
                continue;
            }

            foreach (GameTask plane in planes)
            {
                if (!runtime.TryGetDrawTransform(plane, out Vector3d position, out _))
                {
                    continue;
                }

                if (!TerrainProbe.TryGetGroundHeight(
                        terrain, position.X, position.Y, 50_000.0, 10_000.0, out double ground))
                {
                    continue;
                }

                Assert.True(
                    position.Z >= ground - 1.5,
                    $"plane {plane.Id} sank at tick {tick}: z={position.Z:F2} terrain={ground:F2}");

                if (position.Z <= ground + 30.0)
                {
                    nearGroundSamples++;
                }

                if (previousZ.TryGetValue(plane.Id, out double last)
                    && position.Z < last - 0.25
                    && position.Z < ground - 0.25)
                {
                    fallingTicks[plane.Id] = fallingTicks.GetValueOrDefault(plane.Id) + 1;
                    Assert.True(
                        fallingTicks[plane.Id] < 8,
                        $"plane {plane.Id} kept falling through terrain (z={position.Z:F2})");
                }

                previousZ[plane.Id] = position.Z;
            }
        }

        Assert.True(
            previousZ.Count > 0,
            "mission 3 plane never flew over usable terrain — landing rest was not exercised");
        Assert.True(
            nearGroundSamples > 0,
            "mission 3 plane never approached support height (within 30 m of terrain)");
    }

    [RequiresGameDataFact]
    public void MissionFourAirframesRestOnTerrainInsteadOfSinking()
    {
        (CutsceneRuntime runtime, LevelTask root, TerrainData? terrain, ObjectCollisionIndex _) = LoadMission(4);
        Assert.NotNull(terrain);
        Assert.True(terrain!.IsUsable);
        if (root.Tree.FromId(3902) is ConditionalContainerTask transport)
        {
            transport.Condition = "1";
        }

        runtime.Tick();
        runtime.FinishExpressionTick();

        GameTask[] airframes = Walk(root)
            .Where(task => task is DeclaredTask && task.Type.Name is "Plane" or "Heli")
            .ToArray();
        Assert.True(airframes.Length > 0, "M4 must place a Heli (and a Plane if the pack ships one)");

        Dictionary<int, double> previousZ = [];
        Dictionary<int, int> fallingTicks = [];
        for (int tick = 0; tick < 4_000; tick++)
        {
            runtime.Tick();
            runtime.FinishExpressionTick();
            if (tick % 20 != 0)
            {
                continue;
            }

            foreach (GameTask airframe in airframes)
            {
                if (!runtime.TryGetDrawTransform(airframe, out Vector3d position, out _))
                {
                    continue;
                }

                if (!TerrainProbe.TryGetGroundHeight(
                        terrain, position.X, position.Y, 45_000.0, 15_000.0, out double ground))
                {
                    continue;
                }

                Assert.True(
                    position.Z >= ground - 1.5,
                    $"{airframe.Type.Name} {airframe.Id} sank at tick {tick}: z={position.Z:F2} terrain={ground:F2}");

                if (previousZ.TryGetValue(airframe.Id, out double last)
                    && position.Z < last - 0.25
                    && position.Z < ground - 0.25)
                {
                    fallingTicks[airframe.Id] = fallingTicks.GetValueOrDefault(airframe.Id) + 1;
                    Assert.True(
                        fallingTicks[airframe.Id] < 8,
                        $"{airframe.Type.Name} {airframe.Id} kept falling through terrain (z={position.Z:F2})");
                }

                previousZ[airframe.Id] = position.Z;
            }
        }
    }

    private static (CutsceneRuntime Runtime, LevelTask Root, TerrainData? Terrain, ObjectCollisionIndex Solids)
        LoadMission(int id)
    {
        VirtualFileSystem fs = new();
        fs.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fs);
        GameLoop loop = new(new ManualTimeSource());
        PlayerTaskTypes.RegisterAll(loop.Types);
        AiTaskTypes.RegisterAll(loop.Types);
        loop.Types.Register<CutSceneTask>(CutSceneTask.TypeName);
        loop.Types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fs, resources, loop.Natives);
        MagicObjectRegistry magic = new();
        magic.Register(loop.Natives);
        magic.Load(resources, loop.Natives);
        VehiclePhysicsRegistry vehiclePhysics = new();
        vehiclePhysics.Load(resources);
        LoadedMission mission = new MissionLoader(
            missions,
            fs,
            resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources),
            terrainLoader: new TerrainLoader(resources, loop.Natives)).Load(id);
        WorldScene scene = WorldScene.Build(mission, root, magic);
        new ConditionalContainerGate(loop.Variables).Prepare(root);
        ObjectCollisionIndex solids = ObjectCollisionIndex.Build(scene.Placements);
        CutsceneRuntime runtime = new(root, loop.Variables, terrain: mission.Terrain, vehiclePhysics: vehiclePhysics)
        {
            CollisionIndex = solids,
        };
        return (runtime, root, mission.Terrain, solids);
    }

    private static void ForceAnimations(CutsceneRuntime runtime)
    {
        FieldInfo animField = typeof(CutsceneRuntime).GetField(
            "_animations", BindingFlags.NonPublic | BindingFlags.Instance)!;
        foreach (object state in (IList)animField.GetValue(runtime)!)
        {
            Type t = state.GetType();
            t.GetField("<Run>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(state, "");
            t.GetField("<InitialRun>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(state, true);
        }
    }

    private static void WritePlaneHold(CutsceneRuntime runtime, int targetId, float thrust)
    {
        FieldInfo animField = typeof(CutsceneRuntime).GetField(
            "_animations", BindingFlags.NonPublic | BindingFlags.Instance)!;
        foreach (object state in (IList)animField.GetValue(runtime)!)
        {
            Type t = state.GetType();
            int target = (int)t.GetProperty("TargetId")!.GetValue(state)!;
            if (target != targetId)
            {
                continue;
            }

            AnimDataProgram program = (AnimDataProgram)t.GetProperty("Program")!.GetValue(state)!;
            int budget = program.TotalTicks() + 8;
            for (int tick = 0; !program.HasFinished && tick < budget; tick++)
            {
                runtime.Tick();
                runtime.FinishExpressionTick();
            }

            program.WriteChannel(0, 0f);
            program.WriteChannel(1, 0f);
            program.WriteChannel(2, 0f);
            program.WriteChannel(3, thrust);
        }
    }

    private static bool HasAncestor(GameTask task, GameTask ancestor)
    {
        for (GameTask? walk = task.Parent; walk is not null; walk = walk.Parent)
        {
            if (ReferenceEquals(walk, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<GameTask> Walk(GameTask root)
    {
        yield return root;
        foreach (GameTask child in root.Children)
        {
            foreach (GameTask nested in Walk(child))
            {
                yield return nested;
            }
        }
    }
}
