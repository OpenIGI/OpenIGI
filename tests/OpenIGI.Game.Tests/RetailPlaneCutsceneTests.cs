using System.Collections;
using System.Reflection;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Regression coverage for the retail Plane callback (IGI.exe registration 0x4330F0, channel
/// receiver 0x4337E0 and physics routine 0x433B50). Retail dispatches every
/// AnimTask twice at tick zero: the receiver restores the authored pose, zeroes motion and control,
/// and performs no physics until the first positive tick. Afterwards each tick applies the scripted
/// control channels exactly once.
/// </summary>
public sealed class RetailPlaneCutsceneTests
{
    [RequiresGameDataFact]
    public void MissionThreePlanesResetOnTickZeroAndApplyControlsOncePerTick()
    {
        VirtualFileSystem fs = new();
        fs.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fs);
        GameLoop loop = new(new ManualTimeSource());
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fs, resources, loop.Natives);
        MagicObjectRegistry magic = new();
        magic.Register(loop.Natives);
        magic.Load(resources, loop.Natives);
        VehiclePhysicsRegistry vehiclePhysics = new();
        vehiclePhysics.Load(resources);

        GameLoop mloop = new(new ManualTimeSource());
        LevelTask mroot = (LevelTask)mloop.Tasks.New(mloop.Tasks.Root, mloop.Types.Get(EngineTaskTypes.Level));
        mloop.Types.Register<CutSceneTask>(CutSceneTask.TypeName);
        LoadedMission mission = new MissionLoader(
            missions,
            fs,
            resources,
            new LevelLoader(mroot, mloop.TaskCommands, mloop.Natives, resources),
            terrainLoader: new TerrainLoader(resources, mloop.Natives)).Load(3);
        WorldScene scene = WorldScene.Build(mission, mroot, magic);
        ObjectCollisionIndex solids = ObjectCollisionIndex.Build(scene.Placements);
        CutsceneRuntime runtime = new(mroot, mloop.Variables, terrain: mission.Terrain, vehiclePhysics: vehiclePhysics)
        {
            CollisionIndex = solids,
        };

        // Force every AnimTask to run so the plane programmes publish deterministically.
        FieldInfo animField = typeof(CutsceneRuntime).GetField("_animations", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var animations = (IList)animField.GetValue(runtime)!;
        foreach (object state in animations)
        {
            Type t = state.GetType();
            t.GetField("<Run>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(state, "");
            t.GetField("<InitialRun>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(state, true);
        }

        // Map task ids to type names so the plane-targeted AnimTasks are identifiable.
        Dictionary<int, string> typeNames = new();
        foreach (GameTask task in Walk(mroot))
        {
            if (task is DeclaredTask d)
            {
                typeNames[d.Id] = d.Type.Name;
            }
        }

        FieldInfo transformsField = typeof(CutsceneRuntime).GetField("_transforms", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var transforms = (IDictionary)transformsField.GetValue(runtime)!;

        // Pass 1: right after construction (before any Tick), every plane must sit exactly at its
        // authored pose with zeroed motion and control. Retail's tick-zero reset restores the body
        // and no physics runs until the first positive tick; a duplicated apply block (regression
        // from the ground-rest commit) rotated and moved the airframe during the construction
        // dispatches.
        int planesChecked = 0;
        HashSet<int> seenPlanes = new();
        foreach (object state in animations)
        {
            Type t = state.GetType();
            int target = (int)(t.GetProperty("TargetId")!.GetValue(state)!);
            if (!typeNames.TryGetValue(target, out string? ownerType) || !ownerType.Contains("Plane"))
            {
                continue;
            }

            if (!seenPlanes.Add(target) || transforms[target] is not { } transform)
            {
                continue;
            }

            Type st = transform.GetType();
            Vector3d authoredPosition = (Vector3d)st.GetProperty("Position")!.GetValue(transform)!;
            Matrix3f authoredOrientation = (Matrix3f)st.GetProperty("Orientation")!.GetValue(transform)!;
            AssertPlanePose(transform, authoredPosition, authoredOrientation);
        }

        // Pass 2: per-plane single-apply reference on a held, known control packet.
        foreach (object state in animations)
        {
            Type t = state.GetType();
            int target = (int)(t.GetProperty("TargetId")!.GetValue(state)!);
            if (!typeNames.TryGetValue(target, out string? ownerType) || !ownerType.Contains("Plane"))
            {
                continue;
            }

            if (!seenPlanes.Contains(target) || transforms[target] is not { } transform)
            {
                continue;
            }

            Type st = transform.GetType();

            // Exhaust the authored programme so its entries stop overwriting the channel block on
            // every forward seek (SeekTo replays entries while the counter is in range). Then the
            // test owns the channels and can assert the plane handler's smoothing exactly.
            AnimDataProgram program = (AnimDataProgram)t.GetProperty("Program")!.GetValue(state)!;
            int budget = program.TotalTicks() + 256;
            int tick;
            for (tick = 1; !program.HasFinished && tick <= budget; tick++)
            {
                runtime.Tick();
                runtime.FinishExpressionTick();
            }

            Assert.True(program.HasFinished, $"plane #{target} programme must finish within {budget} ticks (stopped at {tick})");

            // A known held packet: controls (0.5, -0.3, 0.2), thrust 0.8. Plane_Physics retains
            // each SU-27 target by 0.9 and applies its 0.995 low-speed multiplier once per tick.
            // The duplicated apply block would square that response in one scheduler slot.
            program.WriteChannel(0, 0.5f);
            program.WriteChannel(1, -0.3f);
            program.WriteChannel(2, 0.2f);
            program.WriteChannel(3, 0.8f);

            // Reference model for the held packet, seeded from the receiver's actual control and
            // heading the moment before the first held tick (the authored programme's final values
            // are not zero). The 0.995 multiplier is the speed-zero term in 0x433B50; this focused
            // test owns a synthetic packet rather than claiming a full retail flight trace.
            Vector3f expectedControl = (Vector3f)st.GetProperty("Control")!.GetValue(transform)!;
            float expectedHeading = (float)st.GetProperty("Heading")!.GetValue(transform)!;
            Vector3f packet = new(0.5f, -0.3f, 0.2f);
            for (int held = 0; held < 6; held++)
            {
                runtime.Tick();
                runtime.FinishExpressionTick();

                expectedControl = new Vector3f(
                    ((0.9f * expectedControl.X) + (0.1f * packet.X)) * 0.995f,
                    ((0.9f * expectedControl.Y) + (0.1f * packet.Y)) * 0.995f,
                    ((0.9f * expectedControl.Z) + (0.1f * packet.Z)) * 0.995f);
                expectedHeading += expectedControl.Z * 0.004f;

                Vector3f actualControl = (Vector3f)st.GetProperty("Control")!.GetValue(transform)!;
                float actualHeading = (float)st.GetProperty("Heading")!.GetValue(transform)!;
                Assert.Equal(expectedControl.X, actualControl.X, 5);
                Assert.Equal(expectedControl.Y, actualControl.Y, 5);
                Assert.Equal(expectedControl.Z, actualControl.Z, 5);
                Assert.Equal(expectedHeading, actualHeading, 5);
            }

            planesChecked++;
        }

        Assert.True(planesChecked >= 2, $"mission 3 must drive at least two planes, checked {planesChecked}");
    }

    private static void AssertPlanePose(object transform, Vector3d authoredPosition, Matrix3f authoredOrientation)
    {
        Type st = transform.GetType();
        Vector3d position = (Vector3d)st.GetProperty("Position")!.GetValue(transform)!;
        Matrix3f orientation = (Matrix3f)st.GetProperty("Orientation")!.GetValue(transform)!;
        Vector3f control = (Vector3f)st.GetProperty("Control")!.GetValue(transform)!;
        float heading = (float)st.GetProperty("Heading")!.GetValue(transform)!;

        Assert.Equal(authoredPosition.X, position.X, 5);
        Assert.Equal(authoredPosition.Y, position.Y, 5);
        Assert.Equal(authoredPosition.Z, position.Z, 5);
        Assert.Equal(authoredOrientation.M11, orientation.M11, 5);
        Assert.Equal(authoredOrientation.M12, orientation.M12, 5);
        Assert.Equal(authoredOrientation.M13, orientation.M13, 5);
        Assert.Equal(authoredOrientation.M21, orientation.M21, 5);
        Assert.Equal(authoredOrientation.M22, orientation.M22, 5);
        Assert.Equal(authoredOrientation.M23, orientation.M23, 5);
        Assert.Equal(authoredOrientation.M31, orientation.M31, 5);
        Assert.Equal(authoredOrientation.M32, orientation.M32, 5);
        Assert.Equal(authoredOrientation.M33, orientation.M33, 5);
        Assert.Equal(0f, control.X, 5);
        Assert.Equal(0f, control.Y, 5);
        Assert.Equal(0f, control.Z, 5);
        Assert.Equal(0f, heading, 5);
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
