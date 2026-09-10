using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Expressions;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>Retail-script lifecycle coverage for Mission 3's authored outro.</summary>
public sealed class LevelThreeOutroFidelityTests
{
    [RequiresGameDataFact]
    public void ExtractionAreaStartsScene700BeforeLevelFlowCompletes()
    {
        (GameLoop loop, LevelTask root) = LoadLevelThree();
        int projectileCleanupCalls = 0;
        CutsceneRuntime runtime = new(
            root,
            loop.Variables,
            native: (name, _) =>
            {
                if (!string.Equals(name, "Game_CutsceneDelete", StringComparison.Ordinal))
                {
                    return null;
                }

                projectileCleanupCalls++;
                return QvmValue.FromInt32(1);
            });
        MissionObjectiveRuntime objectives = new(root, loop.Variables, native: runtime.TryInvokeNative);

        // Leave the intro, then reproduce the real final-objective path: objectives 1 and 2
        // are already complete and the player enters authored extraction AreaActivate_739.
        // Do not write Objective3/3068 directly -- its EditVariable task must raise the gate.
        runtime.RequestBreak();
        Step(objectives, runtime);
        Assert.True(loop.Variables.TryWrite("EditVariable_3071.nValue", QvmValue.FromInt32(1)));
        Assert.True(loop.Variables.TryWrite("EditVariable_3069.nValue", QvmValue.FromInt32(1)));

        Vector3d extractionCentre = Position(Assert.IsType<DeclaredTask>(root.Tree.FromId(739)));
        Step(objectives, runtime, extractionCentre);

        Assert.True(loop.Variables.TryRead("EditVariable_3068.nValue", out QvmValue objective3));
        Assert.Equal(1, objective3.ToInt32());
        CutSceneTask scene = Assert.IsType<CutSceneTask>(root.Tree.FromId(700));
        Assert.Equal(700, runtime.ActiveScene?.Id);
        Assert.True(scene.IsRun);
        Assert.False(scene.IsFinished);
        Assert.False(objectives.IsComplete);
        Assert.Equal(1, projectileCleanupCalls);
    }

    [RequiresGameDataFact]
    public void Scene700KeepsItsAuthoredHangarAndTakeoffTimeline()
    {
        (GameLoop loop, LevelTask root) = LoadLevelThree();
        List<string> natives = [];
        List<CutsceneSoundEvent> sounds = [];
        CutsceneRuntime runtime = new(
            root,
            loop.Variables,
            native: InvokeNative,
            soundChanged: sounds.Add);
        MissionObjectiveRuntime objectives = new(root, loop.Variables, native: runtime.TryInvokeNative);

        runtime.RequestBreak();
        Step(objectives, runtime);
        natives.Clear();
        Assert.True(loop.Variables.TryWrite("EditVariable_3068.nValue", QvmValue.FromInt32(1)));

        Step(objectives, runtime);

        CutSceneTask scene = Assert.IsType<CutSceneTask>(root.Tree.FromId(700));
        ConditionalContainerTask hangar = Assert.IsType<ConditionalContainerTask>(root.Tree.FromId(3445));
        ConditionalContainerTask staticPlane = Assert.IsType<ConditionalContainerTask>(root.Tree.FromId(3446));
        DeclaredTask takeoffPlane = Assert.IsType<DeclaredTask>(root.Tree.FromId(601));
        Assert.Equal(700, runtime.ActiveScene?.Id);
        Assert.True(scene.IsRun);
        Assert.False(scene.IsFinished);
        Assert.True(ConditionalContainerGate.IsTaskActive(hangar));
        Assert.False(ConditionalContainerGate.IsTaskActive(staticPlane));
        Assert.False(objectives.IsComplete);
        Assert.Equal(
            ["MenuManager_SetEnabled", "Game_DisableMusic", "Game_CutsceneDelete"],
            natives.Take(3));
        Assert.Contains(sounds, sound => sound.Name == "m3o_pl_tax" && sound.Playing);

        Vector3d authoredPosition = Position(takeoffPlane);
        while (scene.Ticks <= 8 * CutsceneRuntime.Frequency)
        {
            Assert.False(
                runtime.TryGetDrawTransform(takeoffPlane, out _, out _),
                "plane 601's AnimTask must not publish a dynamic pose through scene tick 240");
            Step(objectives, runtime);
            Assert.False(scene.IsFinished);
        }

        bool moved = false;
        while (scene.Ticks < 12 * CutsceneRuntime.Frequency)
        {
            Step(objectives, runtime);
            Assert.True(runtime.TryGetDrawTransform(takeoffPlane, out Vector3d position, out _));
            moved |= position != authoredPosition;
        }

        Assert.True(moved, "AnimTask 3333 must first move plane 601 strictly after scene tick 240");
        Step(objectives, runtime);
        Assert.Contains(sounds, sound => sound.Name == "m3o_pl_behind" && sound.Playing);
        Assert.False(objectives.IsComplete);

        int budget = 2_000;
        while (!scene.IsFinished && budget-- > 0)
        {
            Step(objectives, runtime);
            if (!scene.IsFinished)
            {
                Assert.False(objectives.IsComplete);
            }
        }

        Assert.True(scene.IsFinished);
        Assert.True(scene.IsRun, "the completion-boundary frame retains the final authored camera");
        Assert.True(objectives.IsComplete);

        QvmValue? InvokeNative(string name, ScriptArguments _)
        {
            natives.Add(name);
            return QvmValue.FromInt32(1);
        }
    }

    [RequiresGameDataFact]
    public void ProjectileCleanupKeepsScene700AliveUntilItsAuthoredFinalKnot()
    {
        (GameLoop loop, LevelTask root) = LoadLevelThree();
        int projectileCleanupCalls = 0;
        CutsceneRuntime runtime = new(
            root,
            loop.Variables,
            native: InvokeNative);
        MissionObjectiveRuntime objectives = new(root, loop.Variables, native: runtime.TryInvokeNative);

        // Leave the opening cinematic through the same native break edge used by the desktop host,
        // then satisfy Objective3's authored outro gate.
        runtime.RequestBreak();
        objectives.TickLevelState();
        runtime.Tick();
        runtime.FinishExpressionTick();
        Assert.True(loop.Variables.TryWrite("EditVariable_3068.nValue", QvmValue.FromInt32(1)));

        Step(objectives, runtime);

        CutSceneTask scene = Assert.IsType<CutSceneTask>(root.Tree.FromId(700));
        Assert.Equal(700, runtime.ActiveScene?.Id);
        Assert.True(scene.IsRun);
        Assert.False(scene.IsFinished);
        Assert.Equal(1, projectileCleanupCalls);

        // ConditionalContainer_3445 owns the hangar plane for the first 13 seconds and invokes
        // Game_CutsceneDelete as it enters. Retail's native clears projectile tasks; it does not
        // finish scene 700 and thereby satisfy the mission-complete expression on tick one.
        for (int tick = 1; tick < 13 * CutsceneRuntime.Frequency; tick++)
        {
            Step(objectives, runtime);
            Assert.Equal(700, runtime.ActiveScene?.Id);
            Assert.True(scene.IsRun);
            Assert.False(scene.IsFinished);
        }

        Assert.Equal(1, projectileCleanupCalls);

        int remainingBudget = 2_000;
        while (!scene.IsFinished && remainingBudget-- > 0)
        {
            Step(objectives, runtime);
        }

        Assert.True(scene.IsFinished, "scene 700 must finish at its authored 52.5-second final knot");
        Assert.True(scene.IsRun, "retail retains the completion-boundary camera until mission flow advances");
        Assert.Equal(1, projectileCleanupCalls);

        QvmValue? InvokeNative(string name, ScriptArguments _)
        {
            if (!string.Equals(name, "Game_CutsceneDelete", StringComparison.Ordinal))
            {
                return null;
            }

            projectileCleanupCalls++;
            return QvmValue.FromInt32(1);
        }
    }

    private static void Step(
        MissionObjectiveRuntime objectives,
        CutsceneRuntime runtime,
        Vector3d? playerPosition = null)
    {
        objectives.TickLevelState(playerPosition);
        runtime.Tick();
        objectives.TickAfterLevelState();
        runtime.FinishExpressionTick();
    }

    private static Vector3d Position(DeclaredTask task) =>
        task.TryGetValue("Position", out object? value) && value is Vector3d position ? position : default;

    private static (GameLoop Loop, LevelTask Root) LoadLevelThree()
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
        _ = new MissionLoader(
            missions,
            fs,
            resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(3);
        new ConditionalContainerGate(loop.Variables).Prepare(root);
        return (loop, root);
    }
}
