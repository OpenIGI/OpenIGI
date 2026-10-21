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
using OpenIGI.Core.Numerics;
using OpenIGI.TestSupport;
using Xunit;
using Xunit.Abstractions;
using System.Globalization;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Game.Tests;

/// <summary>Dumps Mission 4's Plane/Heli tasks and whether the cutscene runtime drives them below terrain.</summary>
public sealed class DiagMission4Plane
{
    private readonly ITestOutputHelper _output;

    public DiagMission4Plane(ITestOutputHelper output) => _output = output;

    [RequiresGameDataFact]
    public void DumpRideAtStart()
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
        CutsceneRuntime runtime = new(root, loop.Variables, terrain: mission.Terrain, vehiclePhysics: vehiclePhysics)
        {
            CollisionIndex = ObjectCollisionIndex.Build(scene.Placements),
        };
        runtime.Tick();
        runtime.FinishExpressionTick();

        _output.WriteLine("=== M4 NATURAL START (no force 3902) ===");
        int humans = 0;
        foreach (GameTask task in Walk(root))
        {
            string? typeName = task.Type?.Name;
            if (typeName is "HumanPlayer" or "HumanSoldier" or "Human")
            {
                humans++;
                Vector3d hp = default;
                if (task is HumanTask humanBase)
                {
                    hp = humanBase.Position;
                }
                else if (task is DeclaredTask hd && hd.TryGetValue("Position", out object? hpos) && hpos is Vector3d hv)
                {
                    hp = hv;
                }

                int mind = task.Children.OfType<HumanAiTask>().FirstOrDefault()?.Id
                    ?? (task is DeclaredTask da && da.TryGetValue("ID", out object? idv) && idv is int ii ? ii : -2);
                bool attached = runtime.TryGetAttachedDrawTransform(task, out Vector3d ap, out _);
                _output.WriteLine(
                    $"  {typeName} id={task.Id} mind={mind} parent={Name(task.Parent)} staged={CinematicActor.IsStaged(task)} attached={attached} "
                    + $"world=({hp.X / WorldScene.UnitsPerMetre:F1},{hp.Y / WorldScene.UnitsPerMetre:F1},{hp.Z / WorldScene.UnitsPerMetre:F1})"
                    + (attached ? $" ride=({ap.X:F1},{ap.Y:F1},{ap.Z:F1})" : ""));
            }

            if (typeName == "AnimTask" && task is DeclaredTask anim)
            {
                anim.TryGetValue("TargetID", out object? tid);
                anim.TryGetValue("Target ID", out object? tid2);
                _output.WriteLine($"  ANIM id={task.Id} parent={Name(task.Parent)} target={tid ?? tid2}");
            }

            if (task is DeclaredTask declared && typeName is "Plane" or "Heli")
            {
                declared.TryGetValue("Position", out object? pos);
                declared.TryGetValue("Orientation", out object? ori);
                declared.TryGetValue("Original Thrust", out object? thrust);
                declared.TryGetValue("Collision detection", out object? col);
                declared.TryGetValue("Model", out object? model);
                bool drawn = runtime.TryGetDrawTransform(task, out Vector3d draw, out Matrix3f drawOri);
                Vector3f fwd = drawOri.Row2;
                _output.WriteLine(
                    $"  {typeName} id={task.Id} parent={Name(task.Parent)} model={model} thrust={thrust} col={col}");
                _output.WriteLine($"    authored={pos} ori={ori}");
                if (drawn)
                {
                    _output.WriteLine(
                        $"    draw=({draw.X:F1},{draw.Y:F1},{draw.Z:F1}) fwd=({fwd.X:F2},{fwd.Y:F2},{fwd.Z:F2})");
                }
                else
                {
                    _output.WriteLine("    draw=NONE (container closed?)");
                }

                foreach (GameTask child in task.Children)
                {
                    if (child is HumanSoldierTask cabin)
                    {
                        bool attached = runtime.TryGetAttachedDrawTransform(cabin, out Vector3d ap, out _);
                        int mind = cabin.Children.OfType<HumanAiTask>().FirstOrDefault()?.Id ?? -1;
                        _output.WriteLine(
                            $"    CABIN soldier={cabin.Id} mind={mind} staged={CinematicActor.IsStaged(cabin)} attached={attached} "
                            + (attached ? $"at=({ap.X:F1},{ap.Y:F1},{ap.Z:F1})" : $"world=({cabin.Position.X / WorldScene.UnitsPerMetre:F1},{cabin.Position.Y / WorldScene.UnitsPerMetre:F1},{cabin.Position.Z / WorldScene.UnitsPerMetre:F1})"));
                    }
                }
            }

            if (task is HumanSoldierTask human)
            {
                int mind = human.Children.OfType<HumanAiTask>().FirstOrDefault()?.Id ?? -1;
                if (mind is >= 400 and <= 505 or >= 1234 and <= 1238 or >= 540 and <= 544
                    || CinematicActor.IsStaged(human))
                {
                    bool attached = runtime.TryGetAttachedDrawTransform(human, out Vector3d ap, out _);
                    _output.WriteLine(
                        $"  HUMAN soldier={human.Id} mind={mind} parent={Name(human.Parent)} staged={CinematicActor.IsStaged(human)} attached={attached} "
                        + $"world=({human.Position.X / WorldScene.UnitsPerMetre:F1},{human.Position.Y / WorldScene.UnitsPerMetre:F1},{human.Position.Z / WorldScene.UnitsPerMetre:F1})"
                        + (attached ? $" ride=({ap.X:F1},{ap.Y:F1},{ap.Z:F1})" : ""));
                }
            }

            if (task is ConditionalContainerTask cc && (cc.Id is 300 or 305 or 3902 || (cc.Label ?? "").Contains("Heli", StringComparison.OrdinalIgnoreCase) || (cc.Label ?? "").Contains("Friendly", StringComparison.OrdinalIgnoreCase) || (cc.Label ?? "").Contains("Harrison", StringComparison.OrdinalIgnoreCase)))
            {
                _output.WriteLine($"  CC id={cc.Id} label='{cc.Label}' cond='{cc.Condition}' run={cc.IsRun}");
            }
        }

        _output.WriteLine($"  human-count={humans}");
    }

    private static string Name(GameTask? task) =>
        task is null ? "none" : $"{task.Type?.Name}#{task.Id}";

    [RequiresGameDataFact]
    public void Dump()
    {
        VirtualFileSystem fs = new();
        fs.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fs);
        GameLoop loop = new(new ManualTimeSource());
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        loop.Types.Register<CutSceneTask>(CutSceneTask.TypeName);
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
        ConditionalContainerGate gate = new(loop.Variables);
        int dormant = gate.Prepare(root);

        _output.WriteLine($"=== Mission 4: dormant containers {dormant}, terrain usable={mission.Terrain?.IsUsable} ===");

        foreach (GameTask task in Walk(root))
        {
            if (task is not DeclaredTask d2 || task.Type.Name != "AnimTask")
            {
                continue;
            }

            int tgt = d2.TryGetValue("TargetID", out object? tv) && tv is int ti
                ? ti
                : d2.TryGetValue("Target ID", out object? tv2) && tv2 is int ti2 ? ti2 : -1;
            if (tgt is not (356 or 800))
            {
                continue;
            }

            _output.WriteLine($"  ANIM id={task.Id} target={tgt}");
            string? mdl = null;
            if (task.Parent is DeclaredTask pd)
            {
                mdl = pd.TryGetValue(WorldScene.ModelParameter, out object? mv) ? mv as string : null;
                if (mdl is null && pd.TryGetValue("Model", out object? mv2))
                {
                    mdl = mv2 as string;
                }
            }
            _output.WriteLine($"    model={(mdl ?? "?")} thrust-authored="
                + (task.Parent is DeclaredTask pd2 && pd2.TryGetValue("Original Thrust", out object? ot) ? ot : "?"));
            if (d2.TryGetValue("AnimData", out object? ad) && ad is IReadOnlyList<AnimDataEntry> entries)
            {
                _output.WriteLine(
                    $"    entries={entries.Count}");
                _output.WriteLine(
                    "    " + string.Join(" ", entries.Select(e => $"{e.Code}:{e.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}")));
            }
        }

        ObjectCollisionIndex solids = ObjectCollisionIndex.Build(scene.Placements);
        _output.WriteLine($"  collision index entries={solids.Count}");
        if (mission.Terrain is { } t2)
        {
            foreach ((double mx, double my) in new[] { (27474.0, -13615.0), (27532.0, -13513.0), (27889.0, -12755.0), (28156.0, -12112.0), (28675.0, -10766.0), (29235.0, -9022.0), (29494.0, -7295.0), (29164.0, -5697.0), (28871.0, -4434.0), (28590.0, -3182.0) })
            {
                double found = double.NaN;
                for (double probeZ = 47000.0; probeZ >= 15000.0; probeZ -= 4.0)
                {
                    if (TerrainHeightQuery.TryGetGround(
                            t2, mx * WorldScene.UnitsPerMetre, my * WorldScene.UnitsPerMetre,
                            probeZ * WorldScene.UnitsPerMetre, out TerrainGround tg))
                    {
                        found = tg.Height / WorldScene.UnitsPerMetre;
                        break;
                    }
                }
                _output.WriteLine($"  terrain@{mx:F0},{my:F0} -> {(double.IsNaN(found) ? "NO TERRAIN" : found.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "m")}");
            }
        }

        CutsceneRuntime runtime = new(root, loop.Variables, terrain: mission.Terrain, vehiclePhysics: vehiclePhysics)
        {
            CollisionIndex = solids,
        };
        _output.WriteLine($"  cutscene playing={runtime.IsPlaying} frame={runtime.Frame is not null}");

        foreach (GameTask task in Walk(root))
        {
            if (task is not DeclaredTask declared)
            {
                continue;
            }

            string? typeName = task.Type.Name;
            if (typeName is not ("Plane" or "Heli" or "Train" or "Car"))
            {
                continue;
            }

            // Report the authored pose and, for a heli, whether the terrain query can see it from there.
            if (typeName is "Heli" && runtime.TryGetDrawTransform(task, out Vector3d authored, out _))
            {
                double groundZ = double.NaN;
                double heightAbove = double.NaN;
                TerrainData? missionTerrain = mission.Terrain;
                bool terrainReady = missionTerrain is { IsUsable: true };
                bool nearGround = false;
                TerrainGround ag = default;
                if (terrainReady && TerrainHeightQuery.TryGetGround(
                        missionTerrain!, authored.X * WorldScene.UnitsPerMetre,
                        authored.Y * WorldScene.UnitsPerMetre, authored.Z * WorldScene.UnitsPerMetre,
                        out ag))
                {
                    nearGround = true;
                }
                if (nearGround)
                {
                    groundZ = ag.Height / WorldScene.UnitsPerMetre;
                    heightAbove = authored.Z - ag.Height / WorldScene.UnitsPerMetre;
                }
                string heightText = double.IsNaN(heightAbove)
                    ? "n/a" : heightAbove.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "m";
                _output.WriteLine(
                    $"  VEH id={task.Id} type={typeName} authoredZ={authored.Z.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}m "
                    + $"groundZ={groundZ.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}m "
                    + $"heightAbove={heightText}"
                    + (nearGround ? " (terrain visible)" : "  *** NO TERRAIN VISIBLE AT TICK-0 -> NaN rest offset ***"));
            }
            else
            {
                _output.WriteLine($"  VEH id={task.Id} type={typeName}");
            }
        }

        _output.WriteLine($"  cutscene playing={runtime.IsPlaying} frame={runtime.Frame is not null}");

        double lowestPlaneZ = double.MaxValue;
        double groundUnderPlane = double.MaxValue;
        int planeTicks = 0;
        // Heli landing probe: track each heli's position vs terrain during the cutscene.
        Dictionary<int, double> heliLowest = new();
        Dictionary<int, double> heliGround = new();
        List<(int Tick, double X, double Y)> heli356Path = new();
        // Run the full mission-flow window (45s heli gate = 1350 ticks; 66s landing = 1980 ticks;
        // extra margin). The runtime ticks timers/animations/containers each tick regardless of
        // whether a cutscene scene is active.
        int[] traceTicks = [0, 435, 1000, 1350, 1980, 3000, 5000, 10000, 15000, 19999];
        // Collect the Plane/Heli tasks once; the task tree does not change during the tick loop, so
        // re-walking it 20,000 times (allocating a Stack and visiting every task each time) is wasted work.
        GameTask[] aircraft = Walk(root)
            .Where(t => t is DeclaredTask && t.Type.Name is "Plane" or "Heli")
            .ToArray();
        for (int tick = 0; tick < 20_000; tick++)
        {
            runtime.Tick();
            runtime.FinishExpressionTick();

            if (tick >= 428 && tick <= 445 || tick % 50 == 0)
            {
                ProbeRuntime(_output, runtime, root, tick);
            }
            if (tick % 100 == 0 && root.Tree.FromId(356) is GameTask hp356
                && runtime.TryGetDrawTransform(hp356, out Vector3d hp36, out _))
            {
                heli356Path.Add((tick, hp36.X, hp36.Y));
            }
            if (Array.IndexOf(traceTicks, tick) >= 0)
            {
                if (root.Tree.FromId(800) is GameTask heli800Task
                    && runtime.TryGetDrawTransform(heli800Task, out Vector3d h8, out _))
                {
                    double gz = double.NaN;
                    if (mission.Terrain is { IsUsable: true } && TerrainHeightQuery.TryGetGround(
                            mission.Terrain, h8.X * WorldScene.UnitsPerMetre,
                            h8.Y * WorldScene.UnitsPerMetre, h8.Z * WorldScene.UnitsPerMetre, out TerrainGround tg))
                    {
                        gz = tg.Height / WorldScene.UnitsPerMetre;
                    }
                    double upm8 = WorldScene.UnitsPerMetre;
                    Vector3d top8 = new(h8.X * upm8, h8.Y * upm8, (h8.Z + 4096.0) * upm8);
                    Vector3d bot8 = new(h8.X * upm8, h8.Y * upm8, (h8.Z - 262144.0) * upm8);
                    double cast8 = double.NaN;
                    if (solids.TryCast(top8, bot8, out WorldRayHit c8))
                    {
                        cast8 = c8.Position.Z / upm8;
                    }
                    _output.WriteLine(
                        $"    heli800 tick={tick} z={h8.Z.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}m "
                        + $"x={h8.X.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)} "
                        + $"y={h8.Y.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)} "
                        + $"ground={gz.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}m "
                        + $"cast={cast8.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}m");
                }
                if (root.Tree.FromId(356) is GameTask heli356Task
                    && runtime.TryGetDrawTransform(heli356Task, out Vector3d h36, out _))
                {
                    double gz2 = double.NaN;
                    if (mission.Terrain is { IsUsable: true } && TerrainHeightQuery.TryGetGround(
                            mission.Terrain, h36.X * WorldScene.UnitsPerMetre,
                            h36.Y * WorldScene.UnitsPerMetre, h36.Z * WorldScene.UnitsPerMetre, out TerrainGround tg2))
                    {
                        gz2 = tg2.Height / WorldScene.UnitsPerMetre;
                    }
                    double upm = WorldScene.UnitsPerMetre;
                    Vector3d castTop = new(h36.X * upm, h36.Y * upm, (h36.Z + 4096.0) * upm);
                    Vector3d castBottom = new(h36.X * upm, h36.Y * upm, (h36.Z - 262144.0) * upm);
                    double castHit = double.NaN;
                    if (solids.TryCast(castTop, castBottom, out WorldRayHit chit))
                    {
                        castHit = chit.Position.Z / upm;
                    }
                    double deepGround = double.NaN;
                    if (mission.Terrain is { IsUsable: true } && TerrainHeightQuery.TryGetGround(
                            mission.Terrain, h36.X * upm, h36.Y * upm, 42900.0 * upm, out TerrainGround dtg))
                    {
                        deepGround = dtg.Height / upm;
                    }
                    _output.WriteLine(
                        $"    heli356 tick={tick} z={h36.Z.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}m "
                        + $"x={h36.X.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)} "
                        + $"y={h36.Y.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)} "
                        + $"ground={gz2.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}m "
                        + $"cast={castHit.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}m "
                        + $"deep={deepGround.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}m");
                }
            }

            foreach (GameTask task in aircraft)
            {
                if (!runtime.TryGetDrawTransform(task, out Vector3d pos, out _))
                {
                    continue;
                }

                if (task.Type.Name == "Plane")
                {
                    planeTicks++;
                    lowestPlaneZ = Math.Min(lowestPlaneZ, pos.Z);
                    if (mission.Terrain is { IsUsable: true }
                        && TerrainHeightQuery.TryGetGround(
                            mission.Terrain, pos.X * WorldScene.UnitsPerMetre,
                            pos.Y * WorldScene.UnitsPerMetre, pos.Z * WorldScene.UnitsPerMetre,
                            out TerrainGround g))
                    {
                        groundUnderPlane = Math.Min(groundUnderPlane, g.Height / WorldScene.UnitsPerMetre);
                    }
                }
                else
                {
                    heliLowest[task.Id] = Math.Min(
                        heliLowest.TryGetValue(task.Id, out double hz) ? hz : double.MaxValue, pos.Z);
                    if (mission.Terrain is { IsUsable: true }
                        && TerrainHeightQuery.TryGetGround(
                            mission.Terrain, pos.X * WorldScene.UnitsPerMetre,
                            pos.Y * WorldScene.UnitsPerMetre, pos.Z * WorldScene.UnitsPerMetre,
                            out TerrainGround hg))
                    {
                        heliGround[task.Id] = Math.Min(
                            heliGround.TryGetValue(task.Id, out double hgz) ? hgz : double.MaxValue,
                            hg.Height / WorldScene.UnitsPerMetre);
                    }
                }
            }
        }

        _output.WriteLine(
            $"  planeTicks={planeTicks} lowestPlaneZ={lowestPlaneZ:F2}m lowestGroundUnder={groundUnderPlane:F2}m"
            + (planeTicks > 0 && lowestPlaneZ < groundUnderPlane
                ? "  *** PLANE WENT BELOW TERRAIN ***"
                : "  plane stayed above terrain"));
        foreach (KeyValuePair<int, double> pair in heliLowest.OrderBy(p => p.Key))
        {
            double ground = heliGround.TryGetValue(pair.Key, out double g) ? g : double.NaN;
            _output.WriteLine(
                $"  HELI id={pair.Key} lowestZ={pair.Value:F2}m lowestGround={ground:F2}m"
                + (double.IsNaN(ground) || pair.Value < ground
                    ? "  *** HELI BELOW TERRAIN ***"
                    : "  heli above ground"));
        }

        // True terrain profile along heli 356's flight: full-coverage sweep (4m steps) under each
        // recorded path point. A coarser sweep reports phantom gaps because the terrain query only
        // searches 4m below its query z.
        if (mission.Terrain is { IsUsable: true } && heli356Path.Count > 0)
        {
            int unknown = 0;
            foreach ((int tick, double px, double py) in heli356Path)
            {
                double terrain = double.NaN;
                for (double probeZ = 44500.0; probeZ >= 42000.0; probeZ -= 4.0)
                {
                    if (TerrainHeightQuery.TryGetGround(
                            mission.Terrain, px * WorldScene.UnitsPerMetre,
                            py * WorldScene.UnitsPerMetre, probeZ * WorldScene.UnitsPerMetre,
                            out TerrainGround tg))
                    {
                        terrain = tg.Height / WorldScene.UnitsPerMetre;
                        break;
                    }
                }
                if (double.IsNaN(terrain))
                {
                    unknown++;
                }
                _output.WriteLine(
                    $"    pathTerrain tick={tick} x={px.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)} "
                    + $"y={py.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)} -> "
                    + (double.IsNaN(terrain) ? "NO TERRAIN" : terrain.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "m"));
            }
            _output.WriteLine($"  heli356 pathTerrain unknown={unknown}/{heli356Path.Count}");
        }

        // Report each heli's final position and the terrain height at that XY regardless of the body's Z.
        foreach (GameTask task in Walk(root))
        {
            if (task is not DeclaredTask declared || task.Type.Name is not "Heli")
            {
                continue;
            }
            if (runtime.TryGetDrawTransform(task, out Vector3d fin, out _)
                && mission.Terrain is { IsUsable: true })
            {
                // Query from well above so the downward search finds the surface even if the heli sank.
                double probeX = fin.X * WorldScene.UnitsPerMetre;
                double probeY = fin.Y * WorldScene.UnitsPerMetre;
                double probeZ = (fin.Z + 2000) * WorldScene.UnitsPerMetre;
                bool hasGround = TerrainHeightQuery.TryGetGround(
                    mission.Terrain, probeX, probeY, probeZ, out TerrainGround fg);
                if (hasGround)
                {
                    double groundZ = fg.Height / WorldScene.UnitsPerMetre;
                    _output.WriteLine(
                        $"  HELI final id={task.Id} finalZ={fin.Z.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}m "
                        + $"terrainAtXY={groundZ.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}m "
                        + $"delta={((fin.Z - groundZ)).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}m");
                }
                else
                {
                    _output.WriteLine(
                        $"  HELI final id={task.Id} finalZ={fin.Z.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}m -> NO TERRAIN AT THAT XY");
                }
            }
            else
            {
                _output.WriteLine($"  HELI final id={task.Id} -> no draw transform");
            }
        }
    }

    private static IEnumerable<GameTask> Walk(GameTask root)
    {
        Stack<GameTask> stack = new();
        stack.Push(root);
        while (stack.Count > 0)
        {
            GameTask task = stack.Pop();
            yield return task;
            foreach (GameTask child in task.Children)
            {
                stack.Push(child);
            }
        }
    }

    /// <summary>Dumps the runtime's animation and transform state for heli 356.</summary>
    private static void ProbeRuntime(ITestOutputHelper output, CutsceneRuntime runtime, GameTask root, int tick)
    {
        try
        {
            foreach (CutsceneAnimationState animation in runtime.AnimationsForTesting)
            {
                if (animation.TargetId != 356)
                {
                    continue;
                }

                AnimDataProgram program = animation.Program;
                float[] channels = program.Channels.ToArray();
                System.Text.StringBuilder sb = new();
                sb.Append(CultureInfo.InvariantCulture, $"  PROBE356 tick={tick} animTick={program.Tick} pc={program.ProgramCounter} runExpr={animation.Run} initialRun={animation.InitialRun}");
                if (channels is { Length: >= 5 })
                {
                    sb.Append(CultureInfo.InvariantCulture, $" ch0={channels[0]:R} ch1={channels[1]:R} ch2={channels[2]:R} ch3={channels[3]:R} ch4={channels[4]:R}");
                }

                foreach ((GameTask task, CutsceneTransformState state) in runtime.TaskTransformsForTesting)
                {
                    if (task.Id != 356)
                    {
                        continue;
                    }

                    sb.Append(CultureInfo.InvariantCulture, $" pos={state.Position} vel={state.Velocity} thrust={state.Thrust} targetThrust={state.TargetThrust} originalThrust={state.OriginalThrust} control={state.Control} physicsTicks={state.PhysicsTicks} orient={state.Orientation}");
                }

                output.WriteLine(sb.ToString());
            }
        }
        catch (Exception ex)
        {
            output.WriteLine($"  PROBE356 tick={tick} EXCEPTION {ex.GetType().Name}: {ex.Message}");
        }
    }
}
