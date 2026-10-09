using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Igi2.Audio;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Scripting;
using OpenIGI.Game.Player;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// The level's tick (<c>Flow_RunChildren</c>, <c>igi2 0x401A50</c>) over mission 1: its containers, its cutscenes' clocks, the
/// intro they make up, and the break key that ends it.
/// </summary>
public sealed class RetailIgi2LevelFlowTests
{
    /// <summary>The intro, <c>ConditionalContainer_3532</c>.</summary>
    private const int Intro = 3532;

    /// <summary>What holds the player and the AI, <c>ConditionalContainer_3076</c>: "Player and Game AI disabled during cutscenes".</summary>
    private const int PlayerAndAi = 3076;

    /// <summary>
    /// The compile rewrites a word at a time: whitespace goes, <c>this.</c> takes the owner's name, a word naming
    /// <c>.isExists</c> is one, <c>//</c> cuts the line, and quoted strings and numbers stay as they are.
    /// </summary>
    [Fact]
    public void An_expression_compiles_as_the_level_compiles_it()
    {
        Assert.Equal(
            "ConditionalContainer_7.isRun&&1||2",
            Igi2LevelExpression.Compile("this.isRun  &&  Door_5.isExists // a note\n|| 2", "ConditionalContainer_7"));
        Assert.Equal("SoundGenerator_PlaySound(\"a b // c\");", Igi2LevelExpression.Compile("SoundGenerator_PlaySound (\"a b // c\");", "X_1"));
        Assert.Equal("CutScene_3064.nTick>7.76*GAME_FREQUENCY", Igi2LevelExpression.Compile("CutScene_3064.nTick > 7.76*GAME_FREQUENCY", "X_1"));
        Assert.Equal(string.Empty, Igi2LevelExpression.Compile("  // nothing\n", "X_1"));
    }

    /// <summary>
    /// Played through, the intro is thirteen cutscenes one after another, each ended when its time passes its cameras'; the
    /// player and the AI run on the first tick, before the intro starts, and not again until it has ended. While it plays the
    /// menu is shut and the music off, and both come back when it stops.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void Mission1_plays_its_intro_with_the_player_held()
    {
        Mission1 m = Mission1.Load();
        Igi2ConditionalContainerTask player = m.Container(PlayerAndAi);
        Igi2ConditionalContainerTask intro = m.Container(Intro);
        Igi2CutSceneTask flyBy = m.CutScene(3049);
        Assert.Equal((1, 6.5f), (flyBy.Cameras().Cameras.Count, flyBy.Cameras().Total));
        Assert.Equal(18.949999f, m.CutScene(3090).Cameras().Total);

        List<int> playerTicks = [];
        int flyByEnd = 0;
        bool menuShutDuringIntro = true;
        for (int tick = 1; tick <= 3700; tick++)
        {
            m.Level.Tick();
            if (player.IsRun)
            {
                playerTicks.Add(tick);
            }

            if (flyByEnd == 0 && flyBy.IsFinished)
            {
                flyByEnd = tick;
            }

            if (tick > 1 && intro.IsRun)
            {
                menuShutDuringIntro &= !m.Flow.IsMenuEnabled && !m.Flow.IsMusicEnabled && m.Level.IsCutsceneRunning;
            }
        }

        Assert.Equal(197, flyByEnd);
        Assert.Equal(1, playerTicks[0]);
        Assert.Equal(3664, playerTicks[1]);
        Assert.Equal(3700 - 3664 + 2, playerTicks.Count);
        Assert.All(m.Tree.OfType<Igi2CutSceneTask>(), static c => Assert.True(c.IsFinished, c.Name));
        Assert.True(menuShutDuringIntro);
        Assert.False(intro.IsRun);
        Assert.True(m.Flow.IsMenuEnabled);
        Assert.True(m.Flow.IsMusicEnabled);
        Assert.False(m.Level.IsCutsceneRunning);
    }

    /// <summary>
    /// Escape held from the start ends the intro as the level's break key does: its <c>EditVariable</c> counts the key once,
    /// the intro stops the next tick, and the player runs again the tick after.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_break_key_ends_the_intro()
    {
        Mission1 m = Mission1.Load();
        Igi2ConditionalContainerTask player = m.Container(PlayerAndAi);
        Igi2ConditionalContainerTask intro = m.Container(Intro);
        Igi2EditVariableTask breakKey = m.Tree.OfType<Igi2EditVariableTask>().Single(static v => v.Id == 2112);
        m.Flow.IsBreakKeyDown = true;

        m.Level.Tick();
        Assert.Equal((true, true, 1), (player.IsRun, intro.IsRun, breakKey.Value));
        Assert.False(m.Flow.IsMenuEnabled);

        m.Level.Tick();
        Assert.Equal((false, false, 1), (player.IsRun, intro.IsRun, breakKey.Value));
        Assert.True(m.Flow.IsMenuEnabled);

        m.Level.Tick();
        Assert.True(player.IsRun);
    }

    /// <summary>A task bound to its node runs in the tree's place for it, only while every container above it runs.</summary>
    [RequiresTitleDataFact("igi2")]
    public void The_player_runs_where_the_level_places_it()
    {
        Mission1 m = Mission1.Load();
        Assert.True(PlayerSpawn.TryFindGameplay(m.Root, out _));
        GameTask human = PlayerSpawn.FindGameplayTask(m.Root)!;
        Assert.Equal(PlayerAndAi, human.Parent!.Parent!.Id);
        int runs = 0;
        int delayed = 0;
        m.Level.Bind(human, () =>
        {
            runs++;
            m.Level.Delay(() => delayed++);
        });

        m.Flow.IsBreakKeyDown = true;
        for (int tick = 0; tick < 5; tick++)
        {
            m.Level.Tick();
        }

        Assert.Equal(4, runs);
        Assert.Equal(4, delayed);
        Assert.Equal(5, m.Level.Ticks);
    }

    /// <summary>
    /// The generator in the shed (<c>SoundGenerator_3510</c>, under a building and the object it hangs off) waits on its
    /// switch: pressed, its first step starts the generator's start-up and moves the sequence on, the next starts the loop and
    /// stops the sequence for good.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_generator_sounds_its_sequence_when_its_switch_is_pressed()
    {
        Mission1 m = Mission1.Load();
        int pressed = 0;
        m.Level.Natives.RegisterVariable("Switch_3429.isPressed", new QvmIntegerVariable(() => pressed, value => pressed = value, sizeof(byte)));
        Igi2SoundGeneratorTask generator = m.Tree.OfType<Igi2SoundGeneratorTask>().Single(static g => g.Id == 3510);
        generator.Sounds = new Igi2Sounds(Mission1Definitions(), null);
        Igi2SequenceCommandTask driver = m.Tree.OfType<Igi2SequenceCommandTask>().Single(static c => c.Id == 3509);

        m.Level.Tick();
        m.Level.Tick();
        Assert.Equal((false, string.Empty), (generator.IsPlaying, generator.Sound));

        pressed = 1;
        m.Level.Tick();
        Assert.Equal((true, "11_gen_start", 3664), (generator.IsPlaying, generator.Sound, driver.Current!.Id));

        m.Level.Tick();
        Assert.Equal((true, "11_gen_loop", true), (generator.IsPlaying, generator.Sound, driver.IsStopped));

        m.Level.Tick();
        Assert.Equal("11_gen_loop", generator.Sound);
    }

    private static Igi2SoundDefinitions Mission1Definitions()
    {
        string data = GameDataSource.Igi2.RequireRoot();
        VirtualFileSystem files = new();
        files.RegisterDevice(new LocalFileDevice(data));
        files.SetAlias(VirtualFileSystem.MissionAlias, "LOCAL:missions/location1/level1/");
        Igi2SoundDefinitions definitions = new();
        definitions.Load(new ResourceRegistry(files), ["LOCAL:common/sounds", "LOCAL:missions/location1/common/sounds", "MISSION:sounds"]);
        return definitions;
    }

    private sealed record Mission1(GameTask Root, Igi2LevelRun Level, Igi2FlowState Flow, IReadOnlyList<GameTask> Tree)
    {
        public static Mission1 Load()
        {
            Igi2Level level = Igi2Level.Find("location1", "level1");
            RetailIgi2LevelLoadTests.Harness harness = new(Path.Combine(level.Directory, "objects.qvm"), loadTables: true);
            harness.Loader.LoadScript(RetailIgi2LevelLoadTests.Harness.ScriptName);
            Igi2FlowState flow = harness.Session.Services.Require<Igi2FlowState>();
            flow.Reset();
            Igi2LevelRun run = new(harness.LevelRoot, harness.Loop.Natives, harness.Session.Services.Require<Igi2ScriptFunctions>());
            return new Mission1(harness.LevelRoot, run, flow, [.. All(harness.LevelRoot)]);
        }

        public Igi2ConditionalContainerTask Container(int id) => Tree.OfType<Igi2ConditionalContainerTask>().Single(c => c.Id == id);

        public Igi2CutSceneTask CutScene(int id) => Tree.OfType<Igi2CutSceneTask>().Single(c => c.Id == id);

        private static IEnumerable<GameTask> All(GameTask task)
        {
            yield return task;
            foreach (GameTask child in task.Children)
            {
                foreach (GameTask inner in All(child))
                {
                    yield return inner;
                }
            }
        }
    }
}
