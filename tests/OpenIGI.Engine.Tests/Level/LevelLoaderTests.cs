using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Tests.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Engine.Tests.Level;

/// <summary>
/// Building and tearing down a level's subtree, and the level clock that comes out of it.
/// </summary>
public sealed class LevelLoaderTests
{
    [Fact]
    public void ALevelScriptBuildsItsTreeUnderTheLevelRoot()
    {
        Fixture fixture = new();
        fixture.WriteScript(
            "LOCAL:level1/objects.qvm",
            QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(7),
                QvmExpression.Text("Container"),
                QvmExpression.Text("group"),
                QvmExpression.Call(
                    "Task_New",
                    QvmExpression.Int(-1),
                    QvmExpression.Text("Static"),
                    QvmExpression.Text("scenery"))));

        fixture.Loader.LoadMission("LOCAL:level1");

        GameTask group = Assert.Single(fixture.LevelRoot.Children);
        Assert.Equal("Container", group.Type.Name);
        Assert.Equal(7, group.Id);
        Assert.Same(group, fixture.Tree.FromId(7));
        Assert.Equal("Static", Assert.Single(group.Children).Type.Name);
    }

    [Fact]
    public void LoadingRestartsTheLevelClockWithTruncatedMilliseconds()
    {
        Fixture fixture = new();
        fixture.WriteEmptyScript("LOCAL:level1/objects.qvm");

        fixture.Loader.LoadMission("LOCAL:level1");

        Assert.Equal(30, fixture.LevelRoot.TickRate);

        // 1000 / 30 in integer arithmetic. This is the increment the level adds to its own clock every
        // tick, which is why level time runs about 1 % behind the frame schedule.
        Assert.Equal(33, fixture.LevelRoot.MillisecondsPerTick);
        Assert.Equal(0, fixture.LevelRoot.ElapsedMilliseconds);
        Assert.Equal(0, fixture.LevelRoot.TickCount);
    }

    [Fact]
    public void TheLevelClockAdvancesByWholeMillisecondsAndFallsBehindWallClock()
    {
        Fixture fixture = new();
        fixture.WriteEmptyScript("LOCAL:level1/objects.qvm");
        fixture.Loader.LoadMission("LOCAL:level1");

        for (int i = 0; i < 30; i++)
        {
            fixture.LevelRoot.Receive(TaskMessage.Update);
        }

        // A second of simulation reports 990 ms, not 1000. Every timer expressed in level time inherits
        // that deficit, so it has to be reproduced rather than rounded away.
        Assert.Equal(30, fixture.LevelRoot.TickCount);
        Assert.Equal(990, fixture.LevelRoot.ElapsedMilliseconds);
    }

    [Fact]
    public void ALevelTaskForwardsTheTickBeforeAdvancingItsClock()
    {
        Fixture fixture = new();
        fixture.WriteEmptyScript("LOCAL:level1/objects.qvm");
        fixture.Loader.LoadMission("LOCAL:level1");

        int elapsedSeenByChild = -1;
        var child = (WalkerTask)fixture.Tree.New(fixture.LevelRoot, fixture.WalkerType);
        child.OnUpdated = () => elapsedSeenByChild = fixture.LevelRoot.ElapsedMilliseconds;

        fixture.LevelRoot.Receive(TaskMessage.Update);

        // Children update first, so a task reading level time during its own update sees the previous
        // tick's value.
        Assert.Equal(0, elapsedSeenByChild);
        Assert.Equal(33, fixture.LevelRoot.ElapsedMilliseconds);
    }

    [Fact]
    public void PostLoadReachesEveryTaskInTheSubtreeIncludingOnesThatNeverTick()
    {
        Fixture fixture = new();
        fixture.Types.Register<LifecycleProbeTask>("Probe");
        fixture.WriteScript(
            "LOCAL:level1/objects.qvm",
            QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(-1),
                QvmExpression.Text("Static"),
                QvmExpression.Text("frozen"),
                QvmExpression.Call(
                    "Task_New",
                    QvmExpression.Int(-1),
                    QvmExpression.Text("Probe"),
                    QvmExpression.Text("deep"))));

        fixture.Loader.LoadMission("LOCAL:level1");

        // PostLoad is a real recursive broadcast, unlike the per-tick update: it reaches a task under a
        // Static, which the tick never would.
        GameTask frozen = Assert.Single(fixture.LevelRoot.Children);
        var probe = (LifecycleProbeTask)Assert.Single(frozen.Children);
        Assert.Equal(1, probe.PostLoadCount);
    }

    [Fact]
    public void UnloadingEmptiesTheLevelRootAndReleasesEveryScriptId()
    {
        Fixture fixture = new();
        fixture.WriteScript(
            "LOCAL:level1/objects.qvm",
            QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(11),
                QvmExpression.Text("Container"),
                QvmExpression.Text("group")));

        fixture.Loader.LoadMission("LOCAL:level1");
        Assert.NotNull(fixture.Tree.FromId(11));

        fixture.Loader.Unload();

        Assert.Empty(fixture.LevelRoot.Children);
        Assert.Null(fixture.Tree.FromId(11));

        // The level root is a fixture of the tree, not part of the level.
        Assert.Same(fixture.LevelRoot, fixture.Tree.Root.FirstChild);
    }

    [Fact]
    public void ReloadingReusesTheSameScriptIdsWithoutColliding()
    {
        Fixture fixture = new();
        fixture.WriteScript(
            "LOCAL:level1/objects.qvm",
            QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(11),
                QvmExpression.Text("Container"),
                QvmExpression.Text("group")));

        fixture.Loader.LoadMission("LOCAL:level1");
        GameTask first = fixture.LevelRoot.FirstChild!;

        fixture.Loader.Unload();
        fixture.Loader.LoadMission("LOCAL:level1");

        GameTask second = Assert.Single(fixture.LevelRoot.Children);
        Assert.NotSame(first, second);
        Assert.Equal(11, second.Id);
        Assert.Same(second, fixture.Tree.FromId(11));
    }

    [Fact]
    public void LoadingWithoutUnloadingReleasesTheIdsButKeepsTheOldTasks()
    {
        Fixture fixture = new();
        fixture.WriteScript(
            "LOCAL:level1/objects.qvm",
            QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(11),
                QvmExpression.Text("Container"),
                QvmExpression.Text("group")));

        fixture.Loader.LoadMission("LOCAL:level1");
        fixture.Loader.LoadMission("LOCAL:level1");

        // Level_Load releases id cells; it does not delete tasks. Only Level_Unload does that, which is
        // why the mission load sequence calls both.
        Assert.Equal(2, fixture.LevelRoot.ChildCount);
        Assert.Equal(11, fixture.LevelRoot.LastChild!.Id);
        Assert.Equal(GameTask.NoId, fixture.LevelRoot.FirstChild!.Id);
    }

    [Fact]
    public void AMissingScriptIsFatalWithTheOriginalWording()
    {
        Fixture fixture = new();

        EngineFatalException error = Assert.Throws<EngineFatalException>(
            () => fixture.Loader.LoadMission("LOCAL:nosuchlevel"));

        Assert.Contains("Level_Load(): Couldn't load script:", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ALoadRestartsTheTaskCommandStateItLeftBehind()
    {
        Fixture fixture = new();
        fixture.WriteEmptyScript("LOCAL:level1/objects.qvm");
        fixture.Commands.CreateAsFirstChild = true;

        fixture.Loader.LoadMission("LOCAL:level1");

        Assert.False(fixture.Commands.CreateAsFirstChild);
        Assert.Null(fixture.Commands.FirstCreatedTask);
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Loop = new GameLoop(new ManualTimeSource());
            Types = Loop.Types;
            Tree = Loop.Tasks;
            Commands = Loop.TaskCommands;
            WalkerType = Types.Register("Walker", () => new WalkerTask());

            LevelRoot = (LevelTask)Tree.New(Tree.Root, Types.Get(EngineTaskTypes.Level));

            // A real device, on an empty directory: every script in these tests is registered in memory,
            // so the only lookup that reaches the file system is the one that is meant to come up empty.
            var fileSystem = new VirtualFileSystem();
            fileSystem.RegisterDevice(new LocalFileDevice(Directory.CreateTempSubdirectory("openigi-level").FullName));
            Resources = new ResourceRegistry(fileSystem);
            Loader = new LevelLoader(LevelRoot, Commands, Loop.Natives, Resources);
        }

        public GameLoop Loop { get; }

        public TaskTypeRegistry Types { get; }

        public TaskTree Tree { get; }

        public TaskScriptCommands Commands { get; }

        public TaskType WalkerType { get; }

        public LevelTask LevelRoot { get; }

        public ResourceRegistry Resources { get; }

        public LevelLoader Loader { get; }

        public void WriteScript(string path, params QvmExpression[] statements) =>
            Resources.Register(path, QvmExpression.BuildProgram(statements));

        public void WriteEmptyScript(string path) => WriteScript(path);
    }
}
