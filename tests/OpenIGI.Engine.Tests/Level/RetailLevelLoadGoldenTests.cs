using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Engine.Tests.Level;

/// <summary>
/// Drives every retail level through the real load path and then ticks it.
/// </summary>
/// <remarks>
/// <para>
/// <c>RetailObjectsGoldenTests</c> proves the tree comes out the right shape. This proves the rest of
/// the sequence holds up on real data: the level root really does end up owning it, the ids resolve,
/// the <c>PostLoad</c> broadcast reaches the whole thing, and a few hundred ticks through the arbiter
/// neither throw nor drift.
/// </para>
/// <para>
/// Skipped unless <c>IGI_GAME_PATH</c> points at an original installation.
/// </para>
/// </remarks>
public sealed class RetailLevelLoadGoldenTests
{
    [RequiresGameDataFact]
    public void EveryRetailLevelLoadsUnderTheLevelRootAndUnloadsCleanly()
    {
        IReadOnlyList<string> scripts = GameData.EnumerateFiles("objects.qvm");
        Assert.NotEmpty(scripts);

        foreach (string script in scripts)
        {
            Harness harness = new(script);

            harness.Loader.LoadScript(ScriptName);

            int loaded = Count(harness.LevelRoot);
            Assert.True(loaded > 100, $"{script} produced only {loaded} tasks.");
            Assert.Equal(30, harness.LevelRoot.TickRate);
            Assert.Equal(33, harness.LevelRoot.MillisecondsPerTick);

            harness.Loader.Unload();

            Assert.Empty(harness.LevelRoot.Children);
            Assert.Equal(GameTask.NoId, harness.LevelRoot.Id);

            // The tree's own count comes back to the two fixtures that outlive a level: the flow root
            // and the level root. Anything left over would be a task the unload failed to reach.
            Assert.Equal(2, harness.Loop.Tasks.Count);
        }
    }

    [RequiresGameDataFact]
    public void ARetailLevelTicksThroughTheArbiterWithoutDrifting()
    {
        Harness harness = new(GameData.Resolve("missions/location0/level1/objects.qvm"));
        harness.Loader.LoadScript(ScriptName);

        for (int frame = 0; frame < 2_000; frame++)
        {
            harness.Loop.RunFrame();
            harness.Time.Advance(1);
        }

        // Every arbiter tick is one level tick, and level time is exactly 33 ms per tick — the whole
        // point of the level keeping its own truncated clock rather than reading wall time.
        Assert.True(harness.Loop.Clock.TickCount > 50);
        Assert.Equal(harness.Loop.Clock.TickCount, harness.LevelRoot.TickCount);
        Assert.Equal(harness.LevelRoot.TickCount * 33, harness.LevelRoot.ElapsedMilliseconds);

        // Roughly 2000 ms of wall clock at 30 Hz. The bound is loose because the exact count depends on
        // where the opening render alternation lands, but a drifting schedule misses it by a mile.
        Assert.InRange(harness.Loop.Clock.TickCount, 55, 62);
    }

    private const string ScriptName = "LOCAL:level/objects.qvm";

    private static int Count(GameTask task)
    {
        int total = 0;
        foreach (GameTask child in task.Children)
        {
            total += 1 + Count(child);
        }

        return total;
    }

    private sealed class Harness
    {
        public Harness(string scriptPath)
        {
            Time = new ManualTimeSource(1_000);
            Loop = new GameLoop(Time);

            // Level data spawns Container, Static and Dynamic without declaring them, and reads TRUE and
            // FALSE. Everything else it declares itself at the top of the file.
            Loop.Natives.RegisterConstant("TRUE", 1);
            Loop.Natives.RegisterConstant("FALSE", 0);

            LevelRoot = (LevelTask)Loop.Tasks.New(Loop.Tasks.Root, Loop.Types.Get(EngineTaskTypes.Level));

            Resources = new ResourceRegistry(new VirtualFileSystem());
            Resources.Register(ScriptName, File.ReadAllBytes(scriptPath));
            Loader = new LevelLoader(LevelRoot, Loop.TaskCommands, Loop.Natives, Resources);
        }

        public ManualTimeSource Time { get; }

        public GameLoop Loop { get; }

        public LevelTask LevelRoot { get; }

        public ResourceRegistry Resources { get; }

        public LevelLoader Loader { get; }
    }
}
