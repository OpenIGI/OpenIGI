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

/// <summary>Retail-script lifecycle coverage for Mission 3's authored outro.</summary>
public sealed class LevelThreeOutroFidelityTests
{
    [RequiresGameDataFact]
    public void ProjectileCleanupKeepsScene700AliveUntilItsAuthoredFinalKnot()
    {
        (GameLoop loop, LevelTask root) = LoadLevelThree();
        int projectileCleanupCalls = 0;
        CutsceneRuntime runtime = new(
            root,
            loop.Variables,
            clearTransientProjectiles: () => projectileCleanupCalls++);
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
    }

    private static void Step(MissionObjectiveRuntime objectives, CutsceneRuntime runtime)
    {
        objectives.TickLevelState();
        runtime.Tick();
        objectives.TickAfterLevelState();
        runtime.FinishExpressionTick();
    }

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
