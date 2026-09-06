using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Runtime;
using OpenIGI.Core.Numerics;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Regression coverage for the Mission 4 intro helicopter. Its flight programme exhausts around tick
/// 400 leaving the airframe banked with thrust latched on; the anti-sink clamp must keep it riding the
/// terrain instead of crossing below the surface and diving to the bottom of the level.
/// </summary>
public sealed class RetailMissionFourHelicopterTests
{
    [RequiresGameDataFact]
    public void MissionFourIntroHelicopterNeverSinksBelowTerrain()
    {
        VirtualFileSystem fs = new();
        fs.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fs);
        GameLoop loop = new(new ManualTimeSource());
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        loop.Types.Register<CutSceneTask>(CutSceneTask.TypeName);
        loop.Types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fs, resources, loop.Natives);
        MagicObjectRegistry magic = new();
        magic.Register(loop.Natives);
        magic.Load(resources, loop.Natives);
        VehiclePhysicsRegistry vehiclePhysics = new();
        vehiclePhysics.Load(resources);
        LoadedMission mission = new MissionLoader(
            missions, fs, resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources),
            terrainLoader: new TerrainLoader(resources, loop.Natives)).Load(4);
        WorldScene scene = WorldScene.Build(mission, root, magic);
        new ConditionalContainerGate(loop.Variables).Prepare(root);

        TerrainData terrain = Assert.IsAssignableFrom<TerrainData>(mission.Terrain);
        Assert.True(terrain.IsUsable, "Mission 4 terrain must be usable for the helicopter to rest on.");

        ObjectCollisionIndex solids = ObjectCollisionIndex.Build(scene.Placements);
        CutsceneRuntime runtime = new(root, loop.Variables, terrain: terrain, vehiclePhysics: vehiclePhysics)
        {
            CollisionIndex = solids,
        };

        // The transport Bell (356) only flies once its container opens (the authored trigger is the
        // player entering area 156, which a unit test cannot reach). Force it open so the flight
        // programme runs; the parked Bell (800) is gated by a mutually exclusive container, so it is
        // exercised separately in MissionFourParkedBellRestsOnTerrainNaturally.
        Assert.IsType<ConditionalContainerTask>(root.Tree.FromId(3902)).Condition = "1";
        runtime.Tick();
        runtime.FinishExpressionTick();

        GameTask heli356 = Assert.IsAssignableFrom<GameTask>(root.Tree.FromId(356));

        double lowestZ = double.MaxValue;
        double lowestTerrainBelow = double.MaxValue;
        int transformSamples = 0;
        int terrainSamples = 0;
        for (int tick = 0; tick < 20_000; tick++)
        {
            runtime.Tick();
            runtime.FinishExpressionTick();

            // Sample the whole flight. The old behaviour crossed below the surface around tick 1240
            // and never recovered, ending at -15 km; the fix keeps the body within about 1.4 m of the
            // surface for the entire run. Every scheduled transform lookup is required to succeed.
            if (tick % 100 == 0)
            {
                Assert.True(
                    runtime.TryGetDrawTransform(heli356, out Vector3d position, out _),
                    $"heli 356 must expose a draw transform at tick {tick}");
                transformSamples++;
                if (!TerrainProbe.TryGetGroundHeight(
                        terrain, position.X, position.Y, 45_000.0, 42_000.0, out double terrainHeight))
                {
                    continue; // no terrain at this XY: nothing to sink through, skip the sample
                }

                terrainSamples++;
                Assert.True(
                    position.Z >= terrainHeight - 1.0,
                    $"heli 356 sank below the terrain at tick {tick}: z={position.Z:F1}m terrain={terrainHeight:F1}m");
                lowestZ = Math.Min(lowestZ, position.Z);
                lowestTerrainBelow = Math.Min(lowestTerrainBelow, terrainHeight);
            }
        }

        Assert.Equal(200, transformSamples);
        Assert.True(terrainSamples > 0, "no terrain samples were collected for heli 356");

        // Never dive to the bottom of the level (the pre-fix flight reached -15 km), and never end up
        // meaningfully below the surface it is supposed to ride.
        Assert.True(lowestZ > 42_000.0, $"heli 356 dived to {lowestZ:F1}m");
        Assert.True(
            lowestZ >= lowestTerrainBelow - 1.0,
            $"heli 356 flew {lowestTerrainBelow - lowestZ:F1}m below the terrain");
    }

    /// <summary>
    /// The stationary Bell (task 800) is gated by a container (305) that is mutually exclusive with the
    /// transport Bell's (3902), so it can only be exercised on the natural flow: it appears once the
    /// level timer passes the authored threshold (45 s) and then sits on its pad. This locks in that it
    /// rests on the pad rather than sinking through to the bottom of the level like the pre-fix flight
    /// did, and that the anti-sink clamp keeps it riding the surface for the whole run.
    /// </summary>
    [RequiresGameDataFact]
    public void MissionFourParkedBellRestsOnTerrainNaturally()
    {
        VirtualFileSystem fs = new();
        fs.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fs);
        GameLoop loop = new(new ManualTimeSource());
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        loop.Types.Register<CutSceneTask>(CutSceneTask.TypeName);
        loop.Types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fs, resources, loop.Natives);
        MagicObjectRegistry magic = new();
        magic.Register(loop.Natives);
        magic.Load(resources, loop.Natives);
        VehiclePhysicsRegistry vehiclePhysics = new();
        vehiclePhysics.Load(resources);
        LoadedMission mission = new MissionLoader(
            missions, fs, resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources),
            terrainLoader: new TerrainLoader(resources, loop.Natives)).Load(4);
        WorldScene scene = WorldScene.Build(mission, root, magic);
        new ConditionalContainerGate(loop.Variables).Prepare(root);

        TerrainData terrain = Assert.IsAssignableFrom<TerrainData>(mission.Terrain);
        ObjectCollisionIndex solids = ObjectCollisionIndex.Build(scene.Placements);
        CutsceneRuntime runtime = new(root, loop.Variables, terrain: terrain, vehiclePhysics: vehiclePhysics)
        {
            CollisionIndex = solids,
        };

        GameTask heli800 = Assert.IsAssignableFrom<GameTask>(root.Tree.FromId(800));
        ConfigureMissionFourGetawayLanding(runtime, root);
        double lowestZ = double.MaxValue;
        double lowestTerrainBelow = double.MaxValue;
        bool appeared = false;
        int transformSamples = 0;
        int supportSamples = 0;
        for (int tick = 0; tick < 20_000; tick++)
        {
            runtime.Tick();
            runtime.FinishExpressionTick();

            // Sample the parked Bell whenever it is visible. It appears around tick 3080 once the level
            // timer passes the authored threshold, then must ride its pad instead of sinking.
            if (tick % 100 != 0)
            {
                continue;
            }

            bool hasTransform = runtime.TryGetDrawTransform(heli800, out Vector3d position, out _);
            if (!appeared && !hasTransform)
            {
                continue; // the authored conditional container has not opened yet
            }

            Assert.True(hasTransform, $"parked Bell 800 lost its draw transform at tick {tick}");
            appeared = true;
            transformSamples++;
            if (TerrainProbe.TryGetGroundHeight(
                    terrain, position.X, position.Y, 45_000.0, 42_000.0, out double terrainHeight))
            {
                supportSamples++;
                lowestZ = Math.Min(lowestZ, position.Z);
                lowestTerrainBelow = Math.Min(lowestTerrainBelow, terrainHeight);
            }
            else
            {
                // The terrain half is sparse at the pad, so fall back to the collision-index
                // ray cast to confirm the Bell is resting on the surface rather than sinking.
                if (runtime.CollisionIndex is { } index)
                {
                    WorldRayCast caster = index.Caster();
                    if (caster(
                        new Vector3d(position.X, position.Y, position.Z + 4096.0),
                        new Vector3d(position.X, position.Y, position.Z - 262144.0),
                        out WorldRayHit hit))
                    {
                        double hitMetres = hit.Position.Z / WorldScene.UnitsPerMetre;
                        if (hitMetres >= 42_000.0)
                        {
                            supportSamples++;
                            lowestZ = Math.Min(lowestZ, position.Z);
                            lowestTerrainBelow = Math.Min(lowestTerrainBelow, hitMetres);
                        }
                    }
                }
            }
        }
        Assert.True(appeared, "parked Bell 800 never appeared on the natural flow");
        Assert.True(transformSamples > 0, "parked Bell 800 produced no scheduled draw-transform samples");
        Assert.True(supportSamples > 0, "parked Bell 800 produced no terrain or support samples");
        Assert.NotEqual(double.MaxValue, lowestZ);
        Assert.NotEqual(double.MaxValue, lowestTerrainBelow);
        Assert.True(lowestZ > 42_000.0, $"parked Bell 800 sank to {lowestZ:F1}m");
        Assert.True(
            lowestZ >= lowestTerrainBelow - 1.0,
            $"parked Bell 800 sat {lowestTerrainBelow - lowestZ:F1}m below the terrain");
        Assert.Contains(runtime.Diagnostics, line => line.Contains("heli-park-rest", StringComparison.Ordinal));
        System.Console.WriteLine("  park diagnostics: " + string.Join(" | ", runtime.Diagnostics.Take(8)));
    }

    /// <summary>
    /// Locks in that the transport helicopter (task 356) flies <b>horizontally</b> toward the deep
    /// end once its container (3902) opens, rather than straight up. Z is the up axis in this engine,
    /// so a "flies directly upward" regression shows as a large Z gain with little horizontal travel.
    /// This test forces the container open (the authored trigger is the player entering area 156,
    /// which a unit test cannot reach) and verifies the flight programme's horizontal progress vastly
    /// outpaces its climb.
    /// </summary>
    [RequiresGameDataFact]
    public void MissionFourTransportHelicopterFliesHorizontallyNotUpward()
    {
        VirtualFileSystem fs = new();
        fs.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fs);
        GameLoop loop = new(new ManualTimeSource());
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        loop.Types.Register<CutSceneTask>(CutSceneTask.TypeName);
        loop.Types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fs, resources, loop.Natives);
        MagicObjectRegistry magic = new();
        magic.Register(loop.Natives);
        magic.Load(resources, loop.Natives);
        VehiclePhysicsRegistry vehiclePhysics = new();
        vehiclePhysics.Load(resources);
        LoadedMission mission = new MissionLoader(
            missions, fs, resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources),
            terrainLoader: new TerrainLoader(resources, loop.Natives)).Load(4);
        WorldScene scene = WorldScene.Build(mission, root, magic);
        new ConditionalContainerGate(loop.Variables).Prepare(root);

        TerrainData terrain = Assert.IsAssignableFrom<TerrainData>(mission.Terrain);
        ObjectCollisionIndex solids = ObjectCollisionIndex.Build(scene.Placements);
        CutsceneRuntime runtime = new(root, loop.Variables, terrain: terrain, vehiclePhysics: vehiclePhysics)
        {
            CollisionIndex = solids,
        };

        // Force the transport heli's container open as if the player had entered area 156.
        Assert.IsType<ConditionalContainerTask>(root.Tree.FromId(3902)).Condition = "1";
        runtime.Tick();
        runtime.FinishExpressionTick();

        GameTask heli356 = Assert.IsAssignableFrom<GameTask>(root.Tree.FromId(356));
        Assert.True(runtime.TryGetDrawTransform(heli356, out Vector3d start, out _),
            "heli 356 must have a draw transform once its container opens");
        double startX = start.X, startY = start.Y, startZ = start.Z;

        for (int tick = 0; tick < 3000; tick++)
        {
            runtime.Tick();
            runtime.FinishExpressionTick();
        }

        Assert.True(runtime.TryGetDrawTransform(heli356, out Vector3d end, out _),
            "heli 356 must still have a draw transform after the flight");
        double horizontal = Math.Sqrt(
            ((end.X - startX) * (end.X - startX)) + ((end.Y - startY) * (end.Y - startY)));
        double vertical = Math.Abs(end.Z - startZ);

        Assert.True(horizontal > 1500.0,
            $"heli 356 barely moved horizontally over 3000 ticks: {horizontal:F1}m");
        // The extract corridor climbs the hillside (~700 m of terrain). Fail only if the
        // airframe is going to the sky instead of along that corridor.
        Assert.True(vertical < horizontal * 0.5,
            $"heli 356 climbed {vertical:F1}m vs {horizontal:F1}m horizontal — path is vertical");
    }

    [RequiresGameDataFact]
    public void MissionFourTransportBellContinuesAfterItsTerrainContact()
    {
        VirtualFileSystem fs = new();
        fs.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fs);
        GameLoop loop = new(new ManualTimeSource());
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        loop.Types.Register<CutSceneTask>(CutSceneTask.TypeName);
        loop.Types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fs, resources, loop.Natives);
        MagicObjectRegistry magic = new();
        magic.Register(loop.Natives);
        magic.Load(resources, loop.Natives);
        VehiclePhysicsRegistry vehiclePhysics = new();
        vehiclePhysics.Load(resources);
        LoadedMission mission = new MissionLoader(
            missions, fs, resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources),
            terrainLoader: new TerrainLoader(resources, loop.Natives)).Load(4);
        WorldScene scene = WorldScene.Build(mission, root, magic);
        new ConditionalContainerGate(loop.Variables).Prepare(root);
        ObjectCollisionIndex solids = ObjectCollisionIndex.Build(scene.Placements);
        CutsceneRuntime runtime = new(
            root, loop.Variables, terrain: mission.Terrain, vehiclePhysics: vehiclePhysics)
        {
            CollisionIndex = solids,
        };
        ConfigureMissionFourGetawayLanding(runtime, root);

        Assert.IsType<ConditionalContainerTask>(root.Tree.FromId(3902)).Condition = "1";
        runtime.Tick();
        runtime.FinishExpressionTick();
        GameTask transport = Assert.IsAssignableFrom<GameTask>(root.Tree.FromId(356));
        Assert.True(runtime.TryGetDrawTransform(transport, out Vector3d start, out _));

        for (int tick = 0; tick < 3_000; tick++)
        {
            runtime.Tick();
            runtime.FinishExpressionTick();
        }

        Assert.Contains(
            runtime.Diagnostics,
            line => line.Contains("ground-clamp id=356", StringComparison.Ordinal));
        Assert.DoesNotContain(
            runtime.Diagnostics,
            line => line.Contains("heli-terminal-latch id=356", StringComparison.Ordinal));
        Assert.True(runtime.TryGetDrawTransform(transport, out Vector3d end, out _));
        double horizontal = Math.Sqrt(
            ((end.X - start.X) * (end.X - start.X)) + ((end.Y - start.Y) * (end.Y - start.Y)));
        Assert.True(
            horizontal > 1_500.0,
            $"an unrelated transport Bell froze after terrain contact: horizontal={horizontal:F1}m");
    }

    [RequiresGameDataFact]
    public void MissionFourCollisionHoldsThePadUntilCyclic()
    {
        VirtualFileSystem fs = new();
        fs.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fs);
        GameLoop loop = new(new ManualTimeSource());
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
            missions, fs, resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources),
            terrainLoader: new TerrainLoader(resources, loop.Natives)).Load(4);
        WorldScene scene = WorldScene.Build(mission, root, magic);
        new ConditionalContainerGate(loop.Variables).Prepare(root);
        ObjectCollisionIndex solids = ObjectCollisionIndex.Build(scene.Placements);
        CutsceneRuntime runtime = new(root, loop.Variables, terrain: mission.Terrain, vehiclePhysics: vehiclePhysics)
        {
            CollisionIndex = solids,
        };
        Assert.IsType<ConditionalContainerTask>(root.Tree.FromId(3902)).Condition = "1";
        runtime.Tick();
        runtime.FinishExpressionTick();
        GameTask heli356 = Assert.IsAssignableFrom<GameTask>(root.Tree.FromId(356));
        Assert.True(runtime.TryGetDrawTransform(heli356, out Vector3d start, out _));

        // Wait(93) then collective 1.2, still no cyclic. Collision must keep skids on the pad.
        for (int tick = 0; tick < 120; tick++)
        {
            runtime.Tick();
            runtime.FinishExpressionTick();
        }

        Assert.True(runtime.TryGetDrawTransform(heli356, out Vector3d parked, out _));
        double parkedClimb = Math.Abs(parked.Z - start.Z);
        double parkedSlide = Math.Sqrt(
            ((parked.X - start.X) * (parked.X - start.X))
            + ((parked.Y - start.Y) * (parked.Y - start.Y)));
        Assert.True(parkedClimb < 8.0, $"heli 356 left the pad vertically before cyclic: climb={parkedClimb:F1}m");
        Assert.True(parkedSlide < 8.0, $"heli 356 slid off the pad before cyclic: slide={parkedSlide:F1}m");
    }

    [RequiresGameDataFact]
    public void MissionFourGetawayBellFollowsQvmFlightWithHarrisonInside()
    {
        VirtualFileSystem fs = new();
        fs.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fs);
        GameLoop loop = new(new ManualTimeSource());
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        loop.Types.Register<CutSceneTask>(CutSceneTask.TypeName);
        loop.Types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);
        PlayerTaskTypes.RegisterAll(loop.Types);
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fs, resources, loop.Natives);
        MagicObjectRegistry magic = new();
        magic.Register(loop.Natives);
        magic.Load(resources, loop.Natives);
        VehiclePhysicsRegistry vehiclePhysics = new();
        vehiclePhysics.Load(resources);
        LoadedMission mission = new MissionLoader(
            missions, fs, resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources),
            terrainLoader: new TerrainLoader(resources, loop.Natives)).Load(4);
        WorldScene scene = WorldScene.Build(mission, root, magic);
        new ConditionalContainerGate(loop.Variables).Prepare(root);
        CutsceneRuntime runtime = new(
            root, loop.Variables, terrain: mission.Terrain, vehiclePhysics: vehiclePhysics)
        {
            CollisionIndex = ObjectCollisionIndex.Build(scene.Placements),
        };

        ConfigureMissionFourGetawayLanding(runtime, root);
        GameTask heli800 = Assert.IsAssignableFrom<GameTask>(root.Tree.FromId(800));
        // CC 305 opens at LevelTimer_760 > 45 s. GAME_FREQUENCY is 60, so ~2700 ticks
        // plus the opening seconds of AnimTask 850's authored collective phase.
        for (int tick = 0; tick < 3_200; tick++)
        {
            runtime.Tick();
            runtime.FinishExpressionTick();
        }

        Assert.True(runtime.TryGetDrawTransform(heli800, out Vector3d parked, out _),
            "heli 800 must appear after the 45 s gate");
        Vector3d origin = ((DeclaredTask)heli800).TryGetValue("Position", out object? pos) && pos is Vector3d authored
            ? authored / WorldScene.UnitsPerMetre
            : parked;
        double climb = parked.Z - origin.Z;
        double slide = Math.Sqrt(
            ((parked.X - origin.X) * (parked.X - origin.X))
            + ((parked.Y - origin.Y) * (parked.Y - origin.Y)));
        Assert.True(climb < 8.0, $"heli 800 left the base pad vertically: climb={climb:F1}m pos={parked}");
        Assert.True(slide < 8.0, $"heli 800 slid off the base pad: slide={slide:F1}m pos={parked}");

        List<HumanSoldierTask> cabin = [];
        foreach (GameTask task in WalkTree(root))
        {
            if (task is HumanSoldierTask human
                && task.Parent is ConditionalContainerTask { Id: 309 })
            {
                cabin.Add(human);
            }
        }

        Assert.True(cabin.Count >= 5, "CC 309 authors Harrison & Men in Heli");
        int riding = 0;
        foreach (HumanSoldierTask human in cabin)
        {
            Assert.True(runtime.TryGetAttachedDrawTransform(human, out Vector3d rider, out _));
            double away = (rider - parked).Length;
            Assert.True(away < 8.0, $"Harrison cabin soldier={human.Id} is {away:F1}m from heli 800 ({rider} vs {parked})");
            riding++;
        }

        Assert.True(riding >= 5, "Harrison's men must ride heli 800 on the base pad");

        // AnimTask 850 next applies the cyclic pulses from OBJECTS.QVM. The Bell must leave the pad
        // along that authored flight path; a pad-position clamp would make this assertion stay at zero.
        for (int tick = 0; tick < 1_000; tick++)
        {
            runtime.Tick();
            runtime.FinishExpressionTick();
        }

        Assert.True(runtime.TryGetDrawTransform(heli800, out Vector3d inFlight, out _),
            "heli 800 must keep a draw transform during its QVM flight");
        double horizontalFlight = Math.Sqrt(
            ((inFlight.X - origin.X) * (inFlight.X - origin.X))
            + ((inFlight.Y - origin.Y) * (inFlight.Y - origin.Y)));
        double verticalFlight = Math.Abs(inFlight.Z - origin.Z);
        Assert.True(horizontalFlight > 100.0,
            $"heli 800 did not follow its cyclic flight path: horizontal={horizontalFlight:F1}m");
        Assert.True(horizontalFlight > verticalFlight,
            $"heli 800 flight became vertical: horizontal={horizontalFlight:F1}m vertical={verticalFlight:F1}m");
    }

    [RequiresGameDataFact]
    public void MissionFourGetawayBellStopsAtFirstTerrainLanding()
    {
        VirtualFileSystem fs = new();
        fs.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fs);
        GameLoop loop = new(new ManualTimeSource());
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        loop.Types.Register<CutSceneTask>(CutSceneTask.TypeName);
        loop.Types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fs, resources, loop.Natives);
        MagicObjectRegistry magic = new();
        magic.Register(loop.Natives);
        magic.Load(resources, loop.Natives);
        VehiclePhysicsRegistry vehiclePhysics = new();
        vehiclePhysics.Load(resources);
        LoadedMission mission = new MissionLoader(
            missions, fs, resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources),
            terrainLoader: new TerrainLoader(resources, loop.Natives)).Load(4);
        WorldScene scene = WorldScene.Build(mission, root, magic);
        new ConditionalContainerGate(loop.Variables).Prepare(root);
        ObjectCollisionIndex solids = ObjectCollisionIndex.Build(scene.Placements);
        CutsceneRuntime runtime = new(
            root, loop.Variables, terrain: mission.Terrain, vehiclePhysics: vehiclePhysics)
        {
            CollisionIndex = solids,
        };

        GameTask heli800 = Assert.IsAssignableFrom<GameTask>(root.Tree.FromId(800));
        ConfigureMissionFourGetawayLanding(runtime, root);
        TerrainData terrain = Assert.IsAssignableFrom<TerrainData>(mission.Terrain);
        double upm = WorldScene.UnitsPerMetre;
        bool TryGetWarehouseSupport(Vector3d position, out WorldTraceHit support)
        {
            WorldTraceRequest trace = new(
                new Vector3d(position.X * upm, position.Y * upm, (position.Z + 4096.0) * upm),
                new Vector3d(position.X * upm, position.Y * upm, (position.Z - 262144.0) * upm),
                WorldTraceMask.StaticObjects | WorldTraceMask.DynamicObjects,
                IgnoredOwner: heli800);
            return solids.TryTrace(trace, out support)
                && support.Owner?.Id == 2006
                && support.Label?.Contains("LARGEWAREHOUSE", StringComparison.Ordinal) == true;
        }

        // CC 305 opens after the authored level-timer gate. The terminal touchdown is the first
        // terrain contact on the way to the warehouse; the Bell must stop there instead of carrying
        // its horizontal velocity onto the elevated warehouse mesh.
        bool touchedTerrain = false;
        Vector3d touchdown = default;
        Vector3d origin = default;
        bool haveOrigin = false;
        int diagnosticsSeen = runtime.Diagnostics.Count;
        for (int tick = 0; tick < 8_000; tick++)
        {
            runtime.Tick();
            runtime.FinishExpressionTick();
            if (!runtime.TryGetDrawTransform(heli800, out Vector3d position, out _))
            {
                continue;
            }

            if (!haveOrigin)
            {
                origin = position;
                haveOrigin = true;
            }

            // The terrain mesh is sparse at this landing route and a draw-transform sample can skip
            // the exact contact frame. Use the runtime's terrain-only terminal-latch diagnostic as
            // the authoritative touchdown event, then use the sampled position for the drift check.
            bool terminalLandingThisTick = runtime.Diagnostics
                .Skip(diagnosticsSeen)
                .Any(line =>
                    line.Contains("heli-terminal-latch id=800", StringComparison.Ordinal)
                    && line.Contains("source=terrain", StringComparison.Ordinal));
            diagnosticsSeen = runtime.Diagnostics.Count;
            if (terminalLandingThisTick)
            {
                touchedTerrain = true;
                touchdown = position;
                break;
            }
        }
        Assert.True(touchedTerrain, "Bell 800 never reached its first terrain landing contact");
        double touchdownHorizontal = Math.Sqrt(
            ((touchdown.X - origin.X) * (touchdown.X - origin.X))
            + ((touchdown.Y - origin.Y) * (touchdown.Y - origin.Y)));
        Assert.True(touchdownHorizontal > 100.0,
            $"Bell 800 latched before following its authored flight path: horizontal={touchdownHorizontal:F1}m");

        for (int tick = 0; tick < 500; tick++)
        {
            runtime.Tick();
            runtime.FinishExpressionTick();
        }
        Assert.True(runtime.TryGetDrawTransform(heli800, out Vector3d settled, out _));
        double horizontalDrift = Math.Sqrt(
            ((settled.X - touchdown.X) * (settled.X - touchdown.X))
            + ((settled.Y - touchdown.Y) * (settled.Y - touchdown.Y)));
        Assert.True(
            TerrainProbe.TryGetGroundHeight(
                terrain, settled.X, settled.Y, 45_000.0, 42_000.0, out double settledTerrain),
            "Bell 800 touchdown must remain over usable terrain");
        Assert.False(TryGetWarehouseSupport(settled, out _), "Bell 800 climbed on to the warehouse roof");
        Assert.True(horizontalDrift < 2.0,
            $"Bell 800 drifted {horizontalDrift:F2}m after terrain touchdown: {touchdown} -> {settled}");
        Assert.True(Math.Abs(settled.Z - touchdown.Z) < 1.0,
            $"Bell 800 did not remain at touchdown: {touchdown} -> {settled}");
        Assert.InRange(settled.Z - settledTerrain, 1.0, 2.5);
        Assert.Contains(
            runtime.Diagnostics,
            line => line.Contains("heli-terminal-latch id=800", StringComparison.Ordinal));

        double minimumBellVisualZ = double.PositiveInfinity;
        foreach (WorldPlacement placement in scene.Placements.Where(p => p.Owner.Id == heli800.Id))
        {
            if (!runtime.TryGetPlacementDrawTransform(placement, out Vector3d drawPosition, out Matrix3f drawOrientation))
            {
                continue;
            }

            double placementMinimumZ = double.PositiveInfinity;
            foreach (MefVertex vertex in placement.Model.Vertices)
            {
                Vector3f localMetres = vertex.Position / (float)upm;
                Vector3f worldOffset = drawOrientation.Transform(localMetres);
                placementMinimumZ = Math.Min(placementMinimumZ, drawPosition.Z + worldOffset.Z);
            }

            if (placement.Instance.Name.StartsWith("709_", StringComparison.Ordinal))
            {
                minimumBellVisualZ = Math.Min(minimumBellVisualZ, placementMinimumZ);
            }
        }

        Assert.False(double.IsPositiveInfinity(minimumBellVisualZ), "Bell 800 has no rendered body geometry");
        Assert.InRange(minimumBellVisualZ - settledTerrain, -0.5, 0.5);
    }

    [RequiresGameDataFact]
    public void MissionFourAfterCyclicGoesForwardNotStraightUp()
    {
        VirtualFileSystem fs = new();
        fs.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fs);
        GameLoop loop = new(new ManualTimeSource());
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        loop.Types.Register<CutSceneTask>(CutSceneTask.TypeName);
        loop.Types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fs, resources, loop.Natives);
        MagicObjectRegistry magic = new();
        magic.Register(loop.Natives);
        magic.Load(resources, loop.Natives);
        VehiclePhysicsRegistry vehiclePhysics = new();
        vehiclePhysics.Load(resources);
        LoadedMission mission = new MissionLoader(
            missions, fs, resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources),
            terrainLoader: new TerrainLoader(resources, loop.Natives)).Load(4);
        WorldScene scene = WorldScene.Build(mission, root, magic);
        new ConditionalContainerGate(loop.Variables).Prepare(root);
        CutsceneRuntime runtime = new(
            root, loop.Variables, terrain: mission.Terrain, vehiclePhysics: vehiclePhysics)
        {
            CollisionIndex = ObjectCollisionIndex.Build(scene.Placements),
        };

        Assert.IsType<ConditionalContainerTask>(root.Tree.FromId(3902)).Condition = "1";
        runtime.Tick();
        runtime.FinishExpressionTick();
        GameTask heli356 = Assert.IsAssignableFrom<GameTask>(root.Tree.FromId(356));
        Assert.True(runtime.TryGetDrawTransform(heli356, out Vector3d start, out _));

        CutSceneTask intro = WalkTree(root).OfType<CutSceneTask>().First(cut => cut.Id == 1000);

        // The authored intro intentionally changes coordinate scale: the opening exterior is a
        // world-space view, while the HQ cabin shots are authored near the origin. The native
        // evaluator consumes those values directly; do not reject the legitimate local-space
        // segment as if it were the old magnitude-relocation bug.
        bool sawWorldSpaceShot = false;
        bool sawLocalSpaceShot = false;
        for (int tick = 0; tick < 2000; tick++)
        {
            runtime.Tick();
            runtime.FinishExpressionTick();
            if (runtime.Frame is not { } frame)
            {
                continue;
            }

            Vector3d cam = frame.Camera.Position;
            double fromOrigin = cam.Length;
            Assert.True(double.IsFinite(fromOrigin), $"intro camera became non-finite at tick {tick}");
            sawWorldSpaceShot |= fromOrigin > 1_000.0;
            sawLocalSpaceShot |= fromOrigin < 100.0;
        }

        Assert.True(sawWorldSpaceShot, "intro never published its authored exterior camera");
        Assert.True(sawLocalSpaceShot, "intro never published its authored HQ camera");

        Assert.True(runtime.TryGetDrawTransform(heli356, out Vector3d mid, out _));
        double horizontal = Math.Sqrt(
            ((mid.X - start.X) * (mid.X - start.X)) + ((mid.Y - start.Y) * (mid.Y - start.Y)));
        double vertical = mid.Z - start.Z;
        Assert.True(
            horizontal > 80.0 && horizontal > Math.Abs(vertical) * 1.5,
            $"heli 356 still going up, not along its path: horiz={horizontal:F1}m vert={vertical:F1}m");
    }

    [RequiresGameDataFact]
    public void MissionFourAirframePathLogMatchesQvmGates()
    {
        string log = Path.Combine(Path.GetTempPath(), $"m4-airframe-{Guid.NewGuid():N}.log");
        string? previous = CutsceneRuntime.AirframePathLog;
        CutsceneRuntime.AirframePathLog = log;
        try
        {
            VirtualFileSystem fs = new();
            fs.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
            ResourceRegistry resources = new(fs);
            GameLoop loop = new(new ManualTimeSource());
            LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
            loop.Types.Register<CutSceneTask>(CutSceneTask.TypeName);
            loop.Types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);
            MissionRegistry missions = new();
            missions.Register(loop.Natives);
            missions.Scan(fs, resources, loop.Natives);
            MagicObjectRegistry magic = new();
            magic.Register(loop.Natives);
            magic.Load(resources, loop.Natives);
            VehiclePhysicsRegistry vehiclePhysics = new();
            vehiclePhysics.Load(resources);
            LoadedMission mission = new MissionLoader(
                missions, fs, resources,
                new LevelLoader(root, loop.TaskCommands, loop.Natives, resources),
                terrainLoader: new TerrainLoader(resources, loop.Natives)).Load(4);
            WorldScene scene = WorldScene.Build(mission, root, magic);
            new ConditionalContainerGate(loop.Variables).Prepare(root);
            CutsceneRuntime runtime = new(
                root, loop.Variables, terrain: mission.Terrain, vehiclePhysics: vehiclePhysics)
            {
                CollisionIndex = ObjectCollisionIndex.Build(scene.Placements),
            };

            ConfigureMissionFourGetawayLanding(runtime, root);
            // Same edge Escape uses (LevelFlow_GetBreakCutSceneKey). Mission 1 covers
            // the native; here we only need the Heli tick to keep running after break.
            runtime.RequestBreak();

            // Natural flow first: CC 305 opens 800. 3902 is mutually exclusive with 305.
            for (int tick = 0; tick < 3_200; tick++)
            {
                runtime.Tick();
                runtime.FinishExpressionTick();
            }

            if (root.Tree.FromId(3902) is ConditionalContainerTask extract)
            {
                extract.Condition = "1";
            }

            for (int tick = 0; tick < 1_400; tick++)
            {
                runtime.Tick();
                runtime.FinishExpressionTick();
            }

            Assert.True(File.Exists(log), "shipped Heli tick must write OPENIGI_AIRFRAME_PATH_LOG");
            string[] lines = File.ReadAllLines(log);
            Assert.True(lines.Length > 0, "airframe path log is empty");
            Assert.All(lines, line => Assert.Contains("[AIRFRAME-PATH]", line));
            Assert.DoesNotContain(lines, line => line.Contains("type=Plane", StringComparison.Ordinal));

            bool saw356 = false;
            bool saw800 = false;
            foreach (string line in lines)
            {
                Assert.True(
                    line.Contains("id=356", StringComparison.Ordinal)
                    || line.Contains("id=800", StringComparison.Ordinal),
                    $"path log named an airframe QVM does not author: {line}");
                if (line.Contains("id=800", StringComparison.Ordinal) && TryReadDeltas(line, out double h8, out double v8))
                {
                    saw800 = true;
                    if (h8 > 100.0)
                    {
                        Assert.True(
                            h8 > Math.Abs(v8),
                            $"heli 800 QVM flight is vertical-dominant: {line}");
                    }
                }

                if (line.Contains("id=356", StringComparison.Ordinal) && TryReadDeltas(line, out double h3, out double v3))
                {
                    saw356 = true;
                    if (h3 > 20.0)
                    {
                        Assert.True(
                            h3 > Math.Abs(v3),
                            $"heli 356 post-cyclic path is vertical-dominant: {line}");
                    }
                }
            }

            Assert.True(saw356, "forcing CC 3902 must tick Heli 356 into the path log");
            Assert.True(saw800, "after the 45 s gate Heli 800 must tick into the path log");
        }
        finally
        {
            CutsceneRuntime.AirframePathLog = previous;
            if (File.Exists(log))
            {
                File.Delete(log);
            }
        }
    }

    private static bool TryReadDeltas(string line, out double dHoriz, out double dVert)
    {
        dHoriz = dVert = 0;
        return TryReadTagged(line, "dHoriz=", out dHoriz) && TryReadTagged(line, "dVert=", out dVert);
    }

    private static bool TryReadTagged(string line, string tag, out double value)
    {
        value = 0;
        int at = line.IndexOf(tag, StringComparison.Ordinal);
        if (at < 0)
        {
            return false;
        }

        int start = at + tag.Length;
        int end = start;
        while (end < line.Length && line[end] is not ' ')
        {
            end++;
        }

        return double.TryParse(
            line.AsSpan(start, end - start),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out value);
    }

    private static IEnumerable<GameTask> WalkTree(GameTask root)
    {
        yield return root;
        foreach (GameTask child in root.Children)
        {
            foreach (GameTask nested in WalkTree(child))
            {
                yield return nested;
            }
        }
    }

    [Fact]
    public void ParkedHeliRestDoesNotHardCodeTaskEightHundred()
    {
        string source = File.ReadAllText(
            Path.Combine(RepoSrc(), "OpenIGI.Game", "World", "CutsceneRuntime.cs"));
        Assert.False(
            System.Text.RegularExpressions.Regex.IsMatch(source, @"\b800\b"),
            "generic CutsceneRuntime must not contain Mission 4's task-800 identifier");
        Assert.Contains("heli-park-rest", source);
        Assert.Contains("TerminalLandingOnTerrain", source);
        Assert.Contains("ResolveEditCameraPosition", source);
    }

    private static void ConfigureMissionFourGetawayLanding(CutsceneRuntime runtime, LevelTask root)
    {
        GameTask getaway = Assert.IsAssignableFrom<GameTask>(root.Tree.FromId(800));
        runtime.EnableTerminalTerrainLanding(getaway);
    }

    private static string RepoSrc()
    {
        string dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            string src = Path.Combine(dir, "src");
            if (Directory.Exists(Path.Combine(src, "OpenIGI.Game")))
            {
                return src;
            }

            dir = Directory.GetParent(dir)?.FullName ?? "";
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src"));
    }
}
