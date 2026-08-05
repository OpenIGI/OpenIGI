using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Tests.Tasks;
using OpenIGI.Engine.Vfs;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Engine.Tests.Flow;

/// <summary>
/// The M4 acceptance run: boot with no window, no renderer and no audio, walk the flow states through
/// to a loaded level, and tick it.
/// </summary>
/// <remarks>
/// Everything here is the real engine kernel — the same <see cref="GameLoop"/>, arbiter, task trees and
/// level loader a rendering build uses. Only the flow host's screens and the render scheduler are
/// stubbed, which is exactly the seam that lets the port be tested at all before there are pixels.
/// </remarks>
public sealed class HeadlessBootTests
{
    [Fact]
    public void ABootedLoopIsIdleAtThirtyHertzWithBothTreesEmpty()
    {
        GameLoop loop = new(new ManualTimeSource());

        Assert.Equal(30, loop.Clock.TickRate);
        Assert.Equal(0, loop.Clock.TickCount);
        Assert.Equal(0, loop.Clock.CatchUpCount);
        Assert.Equal(GameLoop.FlowTypeName, loop.Flow.Type.Name);
        Assert.Empty(loop.Tasks.Root.Children);
        Assert.Empty(loop.SystemTasks.Root.Children);
        Assert.True(loop.IsRunning);
    }

    [Fact]
    public void TheScriptNamespaceCarriesTheFlowCommandAndItsConstants()
    {
        Headless game = new();

        Assert.True(game.Loop.Natives.Contains(FlowScriptCommands.RequestEvent));
        Assert.True(game.Loop.Natives.Contains(TaskScriptCommands.TaskNew));
        Assert.True(game.Loop.Natives.Contains(TaskScriptCommands.TaskDeclareParameters));

        foreach ((string name, FlowEvent value) in FlowScriptCommands.Constants)
        {
            Assert.True(game.Loop.Natives.TryResolve(name, out QvmSymbol? symbol), $"'{name}' is not registered.");
            Assert.Equal((int)value, symbol.Read().ToInt32());
        }

        // Events 6, 7 and 8 have no script constant in the original, and inventing one would let level
        // data reach a state it cannot reach in the retail game.
        Assert.False(game.Loop.Natives.Contains("FLOW_EVENT_ENDGAME"));
    }

    [Fact]
    public void AScriptCanDriveTheStateMachineThroughFlowRequestEvent()
    {
        Headless game = new();

        // The constant is what a real script names here; its value is asserted separately, above.
        game.RunScript(QvmExpression.Call(
            FlowScriptCommands.RequestEvent,
            QvmExpression.Int((int)FlowEvent.MainMenu)));

        Assert.Equal(FlowEvent.MainMenu, game.Loop.Flow.PendingEvent);

        game.Loop.RunFrame();

        Assert.Equal(FlowEvent.MainMenu, game.Loop.Flow.CurrentEvent);
        Assert.Contains("mainmenu", game.Calls);
    }

    [Fact]
    public void BootingIntoTheIntroReachesTheMainMenuAndThenALoadedLevel()
    {
        Headless game = new();
        game.WriteLevel(
            QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(3),
                QvmExpression.Text("Container"),
                QvmExpression.Text("world"),
                QvmExpression.Call(
                    "Task_New",
                    QvmExpression.Int(4),
                    QvmExpression.Text("Dynamic"),
                    QvmExpression.Text("movers"))));

        game.Loop.Boot(showIntro: true);
        game.Loop.RunFrame();
        Assert.Equal(FlowEvent.Intro, game.Loop.Flow.CurrentEvent);

        game.Loop.Flow.Request(FlowEvent.MainMenu);
        game.Loop.RunFrame();
        Assert.Equal(FlowEvent.MainMenu, game.Loop.Flow.CurrentEvent);

        game.Loop.Flow.Request(FlowEvent.Game);
        game.Loop.RunFrame();
        Assert.Equal(FlowEvent.Game, game.Loop.Flow.CurrentEvent);

        game.Loop.Flow.Request(FlowEvent.RestartGame);
        game.Loop.RunFrame();

        Assert.Equal(["intro", "mainmenu", "begingame", "loadlevel"], game.Calls);
        Assert.Equal(FlowEvent.Game, game.Loop.Flow.CurrentEvent);

        GameTask world = Assert.Single(game.LevelRoot.Children);
        Assert.Equal("Container", world.Type.Name);
        Assert.Same(world, game.Loop.Tasks.FromId(3));
        Assert.Equal("Dynamic", Assert.Single(world.Children).Type.Name);
    }

    [Fact]
    public void ALoadCostsTheScheduleNothingBecauseItIsExcludedTime()
    {
        Headless game = new();
        game.WriteLevel();
        game.LoadDelayMilliseconds = 5_000;

        game.Loop.Flow.Request(FlowEvent.RestartGame);
        game.Loop.RunFrame();

        // The five seconds the load took are excluded, and the tick rate reset that follows zeroes the
        // counters anyway — so the frame after a load is a first tick, not the start of a catch-up burst.
        Assert.Equal(0, game.Loop.Clock.TickCount);
        Assert.Equal(0, game.Loop.Clock.CatchUpCount);

        game.Time.Advance(1);
        Assert.Equal(FrameOutcome.Tick, game.Loop.RunFrame());
        Assert.Equal(FrameOutcome.Render, game.Loop.RunFrame());
    }

    [Fact]
    public void TheLoadedLevelTicksOnceForEveryTickTheArbiterRuns()
    {
        Headless game = new();
        game.WriteLevel();
        game.Loop.Flow.Request(FlowEvent.RestartGame);
        game.Loop.RunFrame();

        for (int frame = 0; frame < 400; frame++)
        {
            game.Loop.RunFrame();
            game.Time.Advance(1);
        }

        Assert.True(game.Loop.Clock.TickCount > 0);
        Assert.Equal(game.Loop.Clock.TickCount, game.LevelRoot.TickCount);
        Assert.Equal(game.LevelRoot.TickCount * 33, game.LevelRoot.ElapsedMilliseconds);
    }

    [Fact]
    public void APausedLevelFreezesItsSubtreeAndItsClockWhileTheArbiterKeepsItsSchedule()
    {
        Headless game = new();
        game.WriteLevel();
        game.Loop.Flow.Request(FlowEvent.RestartGame);
        game.Loop.RunFrame();

        game.Loop.Types.Register<WalkerTask>("Walker");
        var world = (WalkerTask)game.Loop.Tasks.New(game.LevelRoot, game.Loop.Types.Get("Walker"));
        world.Visited = [];

        Run(game, frames: 200);
        int visitsBeforePause = world.Visited.Count;
        int levelTicksBeforePause = game.LevelRoot.TickCount;
        int levelMsBeforePause = game.LevelRoot.ElapsedMilliseconds;
        int arbiterTicksBeforePause = game.Loop.Clock.TickCount;
        Assert.True(visitsBeforePause > 0);

        game.LevelRoot.IsPaused = true;
        Run(game, frames: 200);

        // The arbiter is deliberately left on its schedule — ticks keep arriving — but nothing under the
        // level sees them and the level's own clock stands still. That pairing is the whole point: a menu
        // held open for a minute is not followed by a minute of catch-up.
        Assert.True(game.Loop.Clock.TickCount > arbiterTicksBeforePause);
        Assert.Equal(visitsBeforePause, world.Visited.Count);
        Assert.Equal(levelTicksBeforePause, game.LevelRoot.TickCount);
        Assert.Equal(levelMsBeforePause, game.LevelRoot.ElapsedMilliseconds);

        game.LevelRoot.IsPaused = false;
        Run(game, frames: 200);

        Assert.True(world.Visited.Count > visitsBeforePause);
        Assert.True(game.LevelRoot.TickCount > levelTicksBeforePause);
    }

    private static void Run(Headless game, int frames)
    {
        for (int frame = 0; frame < frames; frame++)
        {
            game.Loop.RunFrame();
            game.Time.Advance(1);
        }
    }

    [Fact]
    public void EndingTheGameLeavesTheLevelRootEmptyAndTheMenuUp()
    {
        Headless game = new();
        game.WriteLevel(QvmExpression.Call(
            "Task_New",
            QvmExpression.Int(9),
            QvmExpression.Text("Container"),
            QvmExpression.Text("world")));

        game.Loop.Flow.Request(FlowEvent.RestartGame);
        game.Loop.RunFrame();
        Assert.NotEmpty(game.LevelRoot.Children);

        game.Loop.Flow.Request(FlowEvent.EndGame);
        game.Loop.RunFrame();

        Assert.Equal(FlowEvent.MainMenu, game.Loop.Flow.CurrentEvent);
        Assert.Empty(game.LevelRoot.Children);
        Assert.Null(game.Loop.Tasks.FromId(9));
    }

    /// <summary>A complete game with no platform under it: real kernel, stubbed screens.</summary>
    private sealed class Headless : IFlowHost
    {
        private const string LevelScript = "LOCAL:missions/level1/objects.qvm";

        public Headless()
        {
            Time = new ManualTimeSource(1_000);
            Loop = new GameLoop(Time, this);

            var fileSystem = new VirtualFileSystem();
            fileSystem.RegisterDevice(new LocalFileDevice(Directory.CreateTempSubdirectory("openigi-boot").FullName));
            Resources = new ResourceRegistry(fileSystem);

            LevelRoot = (LevelTask)Loop.Tasks.New(Loop.Tasks.Root, Loop.Types.Get(EngineTaskTypes.Level));
            Loader = new LevelLoader(LevelRoot, Loop.TaskCommands, Loop.Natives, Resources);
        }

        public ManualTimeSource Time { get; }

        public GameLoop Loop { get; }

        public ResourceRegistry Resources { get; }

        public LevelTask LevelRoot { get; }

        public LevelLoader Loader { get; }

        public List<string> Calls { get; } = [];

        public int LoadDelayMilliseconds { get; set; }

        public void WriteLevel(params QvmExpression[] statements) =>
            Resources.Register(LevelScript, QvmExpression.BuildProgram(statements));

        public void RunScript(params QvmExpression[] statements)
        {
            QvmProgram program = QvmProgram.Read(QvmExpression.BuildProgram(statements));
            QvmError error = new QvmInterpreter(program, Loop.Natives).Run();
            Assert.Equal(QvmError.None, error);
        }

        public void ShowIntroScreens() => Calls.Add("intro");

        public void ShowMainMenu() => Calls.Add("mainmenu");

        public void ShowOutroScreens() => Calls.Add("outro");

        public void ShowAttractScreens() => Calls.Add("attract");

        public void BeginGame(FlowTask flow) => Calls.Add("begingame");

        public void LoadLevel()
        {
            Calls.Add("loadlevel");

            // Level_LoadAll wraps the whole sequence in the exclusion stopwatch. Doing the same here is
            // what makes the load invisible to the frame schedule.
            using (Loop.Clock.ExcludeTime())
            {
                Time.Advance(LoadDelayMilliseconds);
                Loader.Unload();
                Loader.LoadMission("LOCAL:missions/level1");
            }
        }

        public void EndGame() => Loader.Unload();
    }
}
