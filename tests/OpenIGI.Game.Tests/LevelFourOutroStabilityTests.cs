using OpenIGI.Core.Diagnostics;
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
using OpenIGI.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Game.Tests;

/// <summary>Locks the Mission 4 outro gate against cutscene re-entry.</summary>
/// <remarks>
/// Retail fires <c>ConditionalContainer_3902</c> from <c>AreaActivate_156.nActive</c> with no
/// <c>|| this.isRun</c> latch, and completes the mission when <c>CutScene_2222</c> finishes. The
/// desktop host feeds the cutscene camera as the area occupant while a cutscene plays, so the
/// moment the outro camera leaves the tiny extraction volume the gate drops, the cutscene aborts,
/// and the occupant snaps back to the player body still standing inside it: the outro refires in a
/// visible game/cutscene flicker instead of playing once through to completion.
/// </remarks>
public sealed class LevelFourOutroStabilityTests
{
    private readonly ITestOutputHelper _output;

    public LevelFourOutroStabilityTests(ITestOutputHelper output) => _output = output;

    [RequiresGameDataFact]
    public void OutroGateSurvivesItsOwnCameraLeavingTheExtractionVolume()
    {
        (GameLoop loop, LevelTask root, MissionObjectiveRuntime objectives, CutsceneRuntime runtime) = LoadLevel();
        _ = root;

        // The player walks into the extraction volume alive, exactly as the authored gate expects.
        Vector3d inside156 = new(112565344.0, -55798528.0, 175291040.0);
        Vector3d farAway = new(112865840.0, -54000000.0, 176238000.0);

        int outroStarts = 0;
        int outroStops = 0;
        bool completed = false;
        bool gate3902 = false;

        for (int tick = 0; tick < 30 * 60; tick++)
        {
            // Mirror the desktop host: the cutscene camera drives activation once the outro owns
            // the frame, while the stationary player body remains an alternate projection of the
            // same occupant. Before this fix the host substituted the camera for the body, so the
            // gate below flapped with it.
            Vector3d? camera = gate3902 ? CameraOf(runtime) : null;
            Vector3d? primary = camera ?? inside156;
            Vector3d? extra = camera is not null ? inside156 : null;
            objectives.TickLevelState(primary, 0, extra, playerAlive: true);
            runtime.Tick();
            objectives.TickAfterLevelState();

            if (ReadBool(loop, "ConditionalContainer_3902.isRun", out bool run3902))
            {
                if (run3902 && !gate3902)
                {
                    gate3902 = true;
                }
                else if (!run3902 && gate3902)
                {
                    gate3902 = false;
                    _output.WriteLine($"tick={tick} 3902 DROPPED while the outro camera was away");
                }
            }

            if (ReadBool(loop, "CutScene_2222.isRun", out bool run2222))
            {
                if (run2222)
                {
                    Assert.Equal(2222, runtime.ActiveScene?.Id);
                    Assert.NotNull(runtime.Frame);
                    Assert.NotEqual(
                        4,
                        runtime.Frame!.ShotIndex);
                }

                if (run2222 && outroStarts == outroStops)
                {
                    outroStarts++;
                    _output.WriteLine($"tick={tick} 2222 started (#{outroStarts})");
                }
                else if (!run2222 && outroStarts > outroStops)
                {
                    outroStops++;
                    _output.WriteLine($"tick={tick} 2222 STOPPED before finishing (#{outroStops})");
                }
            }

            if (ReadBool(loop, "CutScene_2222.isFinished", out bool finished2222) && finished2222)
            {
                completed = true;
                _output.WriteLine($"tick={tick} 2222 finished");
                break;
            }

            _ = farAway;
        }

        _output.WriteLine($"starts={outroStarts} stops={outroStops} completed={completed}");
        Assert.True(outroStarts >= 1, "walking into area 156 alive must start the outro");
        Assert.Equal(0, outroStops);
        Assert.True(completed, "the outro must play once through to completion");
    }

    [RequiresGameDataFact]
    public void FriendlyTriggeredOutroSurvivesGameplaySquadUnregistration()
    {
        (GameLoop loop, LevelTask root, MissionObjectiveRuntime objectives, CutsceneRuntime runtime) = LoadLevel();
        Vector3d inside156 = new(112565344.0, -55798528.0, 175291040.0);
        Vector3d playerOutside = new(112865840.0, -54000000.0, 176238000.0);

        // Leave the intro first, then expose Harrison's gameplay subtree exactly as the mission does.
        runtime.RequestBreak();
        for (int tick = 0; tick < 10 && runtime.ActiveScene?.Id == 1000; tick++)
        {
            objectives.TickLevelState(playerOutside, 0);
            runtime.Tick();
            objectives.TickAfterLevelState();
            runtime.FinishExpressionTick();
        }
        Assert.NotEqual(1000, runtime.ActiveScene?.Id);
        Assert.True(loop.Variables.TryWrite(
            "ConditionalContainer_3900.isRun",
            OpenIGI.Scripting.Runtime.QvmValue.FromInt32(0)));
        Assert.True(loop.Variables.TryWrite(
            "ConditionalContainer_3901.isRun",
            OpenIGI.Scripting.Runtime.QvmValue.FromInt32(1)));
        Assert.True(loop.Variables.TryWrite(
            "ConditionalContainer_300.isRun",
            OpenIGI.Scripting.Runtime.QvmValue.FromInt32(1)));
        HumanSoldierTask harrison = Assert.IsType<HumanSoldierTask>(root.Tree.FromId(400));
        Assert.Equal("022_01_1", harrison.SoldierModel);
        Assert.True(ConditionalContainerGate.IsTaskActive(harrison));

        int starts = 0;
        int stops = 0;
        bool wasRunning = false;
        bool completed = false;
        for (int tick = 0; tick < 30 * 60; tick++)
        {
            // Harrison—not the player—is the sole team-0 occupant that starts the outro. Container
            // 3901 is disabled as soon as 3902 opens, so the trigger body becomes unregistered on the
            // following tick. The running cutscene must retain ownership despite that self-removal.
            objectives.SetSoldierOccupants(
            [
                new HumanOccupant(400, 0, inside156, IsDead: false),
            ]);
            objectives.TickLevelState(playerOutside, 0);
            runtime.Tick();
            objectives.TickAfterLevelState();
            runtime.FinishExpressionTick();

            bool finished = ReadBool(loop, "CutScene_2222.isFinished", out bool sceneFinished)
                && sceneFinished;
            bool running = ReadBool(loop, "CutScene_2222.isRun", out bool sceneRunning)
                && sceneRunning;
            if (running)
            {
                Assert.Equal(2222, runtime.ActiveScene?.Id);
                Assert.NotNull(runtime.Frame);
            }
            if (running && !wasRunning)
            {
                starts++;
            }
            else if (!running && wasRunning && !finished)
            {
                stops++;
            }
            wasRunning = running;

            if (finished)
            {
                completed = true;
                break;
            }
        }

        Assert.Equal(1, starts);
        Assert.Equal(0, stops);
        Assert.True(completed, "a friendly-triggered Mission 4 outro must play once through to completion");
    }

    [RequiresGameDataFact]
    public void PlayerDeathStillInterruptsAFriendlyTriggeredOutro()
    {
        (GameLoop loop, LevelTask root, MissionObjectiveRuntime objectives, CutsceneRuntime runtime) = LoadLevel();
        Vector3d inside156 = new(112565344.0, -55798528.0, 175291040.0);
        Vector3d playerOutside = new(112865840.0, -54000000.0, 176238000.0);

        runtime.RequestBreak();
        for (int tick = 0; tick < 10 && runtime.ActiveScene?.Id == 1000; tick++)
        {
            objectives.TickLevelState(playerOutside, 0);
            runtime.Tick();
            objectives.TickAfterLevelState();
            runtime.FinishExpressionTick();
        }
        Assert.NotEqual(1000, runtime.ActiveScene?.Id);
        Assert.True(loop.Variables.TryWrite(
            "ConditionalContainer_3900.isRun",
            OpenIGI.Scripting.Runtime.QvmValue.FromInt32(0)));
        Assert.True(loop.Variables.TryWrite(
            "ConditionalContainer_3901.isRun",
            OpenIGI.Scripting.Runtime.QvmValue.FromInt32(1)));
        Assert.True(loop.Variables.TryWrite(
            "ConditionalContainer_300.isRun",
            OpenIGI.Scripting.Runtime.QvmValue.FromInt32(1)));
        Assert.Equal("022_01_1", Assert.IsType<HumanSoldierTask>(root.Tree.FromId(400)).SoldierModel);

        objectives.SetSoldierOccupants([new HumanOccupant(400, 0, inside156, IsDead: false)]);
        objectives.TickLevelState(playerOutside, 0);
        runtime.Tick();
        objectives.TickAfterLevelState();
        runtime.FinishExpressionTick();
        Assert.True(ReadBool(loop, "ConditionalContainer_3902.isRun", out bool gateRunning) && gateRunning);
        Assert.True(ReadBool(loop, "CutScene_2222.isRun", out bool sceneRunning) && sceneRunning);

        objectives.TickLevelState(playerOutside, 0, extraPlayerPosition: null, playerAlive: false);
        runtime.Tick();
        runtime.FinishExpressionTick();

        Assert.True(ReadBool(loop, "ConditionalContainer_3902.isRun", out bool gateStopped));
        Assert.False(gateStopped);
        Assert.True(ReadBool(loop, "CutScene_2222.isRun", out bool sceneStopped));
        Assert.False(sceneStopped);
    }

    [RequiresGameDataFact]
    public void FollowingMissionIntroOwnsTheCameraAsSoonAsTheLevelIsConstructed()
    {
        (_, _, _, CutsceneRuntime runtime) = LoadLevel(5);

        Assert.True(runtime.IsPlaying);
        Assert.NotNull(runtime.ActiveScene);
        Assert.NotNull(runtime.Frame);
    }

    private static Vector3d? CameraOf(CutsceneRuntime runtime)
    {
        if (runtime.Frame?.Camera.Position is { } cameraMetres)
        {
            return cameraMetres * WorldScene.UnitsPerMetre;
        }

        return null;
    }

    private static bool ReadBool(GameLoop loop, string name, out bool value)
    {
        value = false;
        if (!loop.Variables.TryRead(name, out OpenIGI.Scripting.Runtime.QvmValue read))
        {
            return false;
        }
        try
        {
            value = read.ToInt32() != 0;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static (GameLoop Loop, LevelTask Root, MissionObjectiveRuntime Objectives, CutsceneRuntime Runtime)
        LoadLevel(int missionId = 4)
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
        _ = new MissionLoader(missions, fs, resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(missionId);
        new ConditionalContainerGate(loop.Variables).Prepare(root);
        CutsceneRuntime runtime = new(root, loop.Variables);
        MissionObjectiveRuntime objectives = new(root, loop.Variables, native: runtime.TryInvokeNative);
        return (loop, root, objectives, runtime);
    }
}
