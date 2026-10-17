using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Titles;
using OpenIGI.Game.Level;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>A title's level systems run in order, and are edited by name.</summary>
public sealed class LevelSystemsTests
{
    [Fact]
    public void Systems_run_in_the_order_they_are_listed_and_edited()
    {
        TestBuild build = Build();
        LevelSystems<TestBuild> systems = new LevelSystems<TestBuild>()
            .Add(Log("a"))
            .Add(Log("c"))
            .InsertBefore("c", Log("b"))
            .InsertAfter("c", Log("d"))
            .Replace("a", Log("first"));
        Assert.True(systems.Remove("d"));
        Assert.False(systems.Remove("d"));

        systems.BuildAll(build);

        Assert.Equal(["first", "b", "c"], build.Ran);
    }

    [Fact]
    public void A_name_is_listed_once_and_an_unknown_one_is_refused()
    {
        LevelSystems<TestBuild> systems = new LevelSystems<TestBuild>().Add(Log("a"));
        Assert.Throws<ArgumentException>(() => systems.Add(Log("a")));
        Assert.Throws<KeyNotFoundException>(() => systems.InsertAfter("missing", Log("b")));
    }

    [Fact]
    public void A_system_written_for_any_title_runs_in_a_titles_list()
    {
        TestBuild build = Build();
        List<string> seen = [];
        ILevelSystem<LevelBuild> anyTitle = new LevelSystem<LevelBuild>("any", b => seen.Add(b.Root.Name));

        new LevelSystems<TestBuild>().Add(anyTitle).BuildAll(build);

        Assert.Single(seen);
    }

    private static LevelSystem<TestBuild> Log(string name) => new(name, b => b.Ran.Add(name));

    private static TestBuild Build()
    {
        TaskTypeRegistry types = new();
        types.Register<Plain>("Container");
        TaskTree tree = new(types);
        return new TestBuild(new TestRun(tree.Root), new ServiceCatalog());
    }

    private sealed class Plain : GameTask
    {
    }

    private sealed class TestRun(GameTask root) : LevelRun(root)
    {
        protected override void RunUntyped(GameTask task)
        {
        }
    }

    private sealed class TestBuild(LevelRun run, IServiceCatalog services) : LevelBuild(run, services)
    {
        public List<string> Ran { get; } = [];
    }
}
