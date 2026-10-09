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
/// Mission 8's airstrike: lasing the SAM launcher (<c>GenericTBA_181</c>) calls in the Su27 that destroys it.
/// </summary>
/// <remarks>
/// <para>
/// The chain is the level's own. <c>ConditionalContainer_454</c> ("Su27") opens on
/// <c>GenericTBA_181.isFinished || this.isRun</c>. It holds <c>Plane_806</c>, whose <c>AnimTask_856</c> runs
/// while that container runs. The launcher (<c>ExplodeObject_2500</c>) goes off on
/// <c>AnimTask_856.nTick &gt; 404</c>.
/// </para>
/// <para>
/// Probed on retail (issue #64). The player picked up the designator (<c>GenericPickup_870</c>) and held Use
/// at the launcher. The task finished 180 ticks later, and the container opened and the AnimTask took its first
/// step by the next tick. The launcher exploded at nTick 405. The port's plane matches retail's at matching
/// AnimTask steps: step 30 at (39634777, 145102718, 175708573) and step 390 at (44060487, 146334607, 175549645).
/// </para>
/// </remarks>
public sealed class MissionEightAirstrikeTests
{
    [RequiresGameDataFact]
    public void LasingTheLauncherSendsTheSu27AndItsStrikeGateOpensAtTick405()
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
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(8);

        // The interaction registry owns this export in the game; here the test stands in for the designator.
        bool lased = false;
        loop.Variables.BindBool(root.Tree.FromId(181)!, "isFinished", () => lased, _ => { });
        new ConditionalContainerGate(loop.Variables).Prepare(root);
        CutsceneRuntime runtime = new(root, loop.Variables);
        MissionObjectiveRuntime objectives = new(root, loop.Variables, native: runtime.TryInvokeNative);
        LevelExpressionRuntime expressions = new(loop.Variables);
        ConditionalContainerTask su27 = Assert.IsType<ConditionalContainerTask>(root.Tree.FromId(454));
        DeclaredTask launcher = Assert.IsType<DeclaredTask>(root.Tree.FromId(2500));
        Assert.True(launcher.TryGetValue("Explosion expression", out object? strike));

        void Tick()
        {
            objectives.TickLevelState();
            runtime.Tick();
            objectives.TickAfterLevelState();
        }

        for (int tick = 0; tick < 60; tick++)
        {
            Tick();
        }

        Assert.False(su27.IsRun);
        Assert.Equal(0, Read("AnimTask_856.nTick"));

        lased = true;
        Tick();
        Assert.True(su27.IsRun);
        int first = Read("AnimTask_856.nTick");
        Assert.InRange(first, 0, 1);

        // One step a tick from there; the launcher's gate is false through 404 and true at 405.
        while (Read("AnimTask_856.nTick") < 404)
        {
            Tick();
        }

        Assert.False(expressions.EvaluateCondition((string)strike!, emptyValue: false, launcher));
        Tick();
        Assert.Equal(405, Read("AnimTask_856.nTick"));
        Assert.True(expressions.EvaluateCondition((string)strike!, emptyValue: false, launcher));

        int Read(string name)
        {
            Assert.True(loop.Variables.TryRead(name, out QvmValue value), name);
            return value.ToInt32();
        }
    }
}
