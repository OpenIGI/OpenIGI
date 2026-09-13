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

/// <summary>Decisive experiment: does the Mission 4 intro camera trip gameplay area gates?</summary>
/// <remarks>
/// The desktop host feeds the cutscene camera as the <c>AreaActivate</c> occupant while a cutscene
/// plays. This test replays the intro exactly that way and records which gates fire: 301/302/303
/// (guard latches), 761 (Josip timer), objective 1 (area 155), and 3902/2222 (the outro).
/// </remarks>
public sealed class LevelFourIntroOccupantTests
{
    private readonly ITestOutputHelper _output;

    public LevelFourIntroOccupantTests(ITestOutputHelper output) => _output = output;

    [RequiresGameDataFact]
    public void IntroCameraMustNotTripTheOutroGate()
    {
        (GameLoop loop, LevelTask root, MissionObjectiveRuntime objectives, CutsceneRuntime runtime) = LoadLevel();

        List<string> transitions = [];
        bool c3902 = false;
        bool scene2222Run = false;
        bool scene2222Finished = false;
        bool obj1Complete = false;
        int ticks = 0;
        int maxTicks = 30 * 120;

        int cameraTicks = 0;
        Vector3d? firstCamera = null;
        Vector3d? lastCamera = null;
        for (int tick = 0; tick < maxTicks; tick++)
        {
            Vector3d? occupant = CameraOccupant(runtime);
            if (occupant is { } cam)
            {
                cameraTicks++;
                firstCamera ??= cam;
                lastCamera = cam;
            }
            objectives.TickLevelState(occupant);
            runtime.Tick();
            objectives.TickAfterLevelState();
            ticks++;

            if (ReadBool(loop, "ConditionalContainer_3902.isRun", out bool run3902) && run3902 && !c3902)
            {
                c3902 = true;
                transitions.Add($"tick={tick} 3902 RUN (outro gate)");
            }
            if (ReadBool(loop, "CutScene_2222.isRun", out bool run2222) && run2222 && !scene2222Run)
            {
                scene2222Run = true;
                transitions.Add($"tick={tick} 2222 RUN (outro cutscene)");
            }
            if (ReadBool(loop, "CutScene_2222.isFinished", out bool fin2222) && fin2222 && !scene2222Finished)
            {
                scene2222Finished = true;
                transitions.Add($"tick={tick} 2222 FINISHED (mission complete)");
            }
            if (ReadBool(loop, "CutScene_1000.isFinished", out bool fin1000) && fin1000)
            {
                transitions.Add($"tick={tick} intro 1000 finished");
                break;
            }
            if (objectives.IsComplete && !obj1Complete)
            {
                obj1Complete = true;
                transitions.Add($"tick={tick} OBJECTIVES COMPLETE");
            }
        }

        foreach (string line in transitions)
        {
            _output.WriteLine(line);
        }
        _output.WriteLine($"ticks={ticks} complete={objectives.IsComplete} cameraTicks={cameraTicks}");
        _output.WriteLine($"firstCamera={firstCamera} lastCamera={lastCamera}");

        Assert.False(c3902, "the intro camera must not fire outro gate 3902");
        Assert.False(scene2222Run, "the outro cutscene must not run during the intro");
    }

    private static Vector3d? CameraOccupant(CutsceneRuntime runtime)
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
        LoadLevel()
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
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(4);
        new ConditionalContainerGate(loop.Variables).Prepare(root);
        CutsceneRuntime runtime = new(root, loop.Variables);
        MissionObjectiveRuntime objectives = new(root, loop.Variables, native: runtime.TryInvokeNative);
        return (loop, root, objectives, runtime);
    }
}
