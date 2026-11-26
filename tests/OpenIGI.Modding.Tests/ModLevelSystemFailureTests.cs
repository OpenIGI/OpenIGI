using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Titles;
using OpenIGI.Game.Igi2;
using OpenIGI.Game.Level;
using OpenIGI.Modding.Testing;
using Xunit;

namespace OpenIGI.Modding.Tests;

/// <summary>
/// A mod's level system fails on its own: the level is brought up without it and the failure is logged. A mod built against
/// another version of the game threw a TypeLoadException from its system as a mission loaded, and that ended the process.
/// </summary>
public sealed class ModLevelSystemFailureTests
{
    [Fact]
    public void A_mod_built_for_another_version_is_left_out_of_the_level_and_the_level_still_comes_up()
    {
        ModHarness harness = ModHarness.Of(new Systems(static b => b.LevelSystems<TestBuild>()
            .InsertBefore("Host", "Stale", static _ => throw new TypeLoadException("Could not load type 'OpenIGI.Game.World.GunPickupRegistry'."))
            .Add("Fine", static level => level.Ran.Add("Fine"))), "stale");
        ModSession session = harness.Start(new Igi2Runtime());
        Assert.True(session.Services.TryGet(out LevelSystemEdits? edits));

        RecordingLogger log = new();
        TestBuild build = Build(log);
        edits.Apply(new LevelSystems<TestBuild>().Add(new LevelSystem<TestBuild>("Host", static level => level.Ran.Add("Host")))).BuildAll(build);

        Assert.Equal(["Host", "Fine"], build.Ran);
        LogRecord error = Assert.Single(log.Records, static r => r.Level == LogLevel.Error);
        Assert.Contains("'stale'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'Stale'", error.Message, StringComparison.Ordinal);
        Assert.Contains("another version of OpenIGI", error.Message, StringComparison.Ordinal);
        Assert.IsType<TypeLoadException>(error.Exception);
    }

    [Fact]
    public void A_mods_own_failure_is_logged_without_the_version_hint()
    {
        ModHarness harness = ModHarness.Of(new Systems(static b => b.LevelSystems<TestBuild>()
            .Replace("Host", static _ => throw new InvalidOperationException("the mod's own bug"))), "buggy");
        ModSession session = harness.Start(new Igi2Runtime());
        Assert.True(session.Services.TryGet(out LevelSystemEdits? edits));

        RecordingLogger log = new();
        TestBuild build = Build(log);
        edits.Apply(new LevelSystems<TestBuild>().Add(new LevelSystem<TestBuild>("Host", static level => level.Ran.Add("Host")))).BuildAll(build);

        Assert.Empty(build.Ran);
        LogRecord error = Assert.Single(log.Records, static r => r.Level == LogLevel.Error);
        Assert.Contains("the mod's own bug", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("another version", error.Message, StringComparison.Ordinal);
    }

    private static TestBuild Build(IOpenIgiLogger logger)
    {
        TaskTypeRegistry types = new();
        types.Register<Plain>("Container");
        TaskTree tree = new(types);
        return new TestBuild(new TestRun(tree.Root), new ServiceCatalog()) { Logger = logger };
    }

    private sealed class Systems(Action<IModBuilder> configure) : IOpenIgiMod
    {
        public void Configure(IModBuilder builder) => configure(builder);
    }

    private sealed class RecordingLogger : IOpenIgiLogger
    {
        public List<LogRecord> Records { get; } = [];

        public bool IsEnabled(LogLevel level, string category) => true;

        public void Log(in LogRecord record) => Records.Add(record);
    }

    private sealed class Plain : GameTask
    {
    }

    private sealed class TestRun(GameTask root) : LevelRun(root)
    {
        public override OpenIGI.Scripting.Expressions.LevelExpression Compile(string? source, GameTask owner) => throw new NotSupportedException();

        public override double Evaluate(OpenIGI.Scripting.Expressions.LevelExpression expression) => throw new NotSupportedException();

        public override double EvaluateLine(string source) => throw new NotSupportedException();

        protected override void RunUntyped(GameTask task)
        {
        }
    }

    private sealed class TestBuild(LevelRun run, IServiceCatalog services) : LevelBuild(run, services)
    {
        public List<string> Ran { get; } = [];
    }
}
