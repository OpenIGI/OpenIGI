using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Level;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The conditional container's run handler both titles share (<c>0x4874E0</c>, <c>igi2 0x520EF0</c>), under rules a title's
/// run gives it.
/// </summary>
public sealed class ConditionalContainerRunTests
{
    [Theory]
    [InlineData(true, "run start|run|run stop|out")]
    [InlineData(false, "run start|run|out stop|")]
    public void The_children_run_once_more_on_the_falling_tick_only_in_the_first_game(bool once, string expected)
    {
        (ConditionalContainerTask container, Rules run) = Build(once);

        List<string> ticks = [];
        foreach (bool holds in new[] { true, true, false, false })
        {
            run.Holds = holds;
            run.Log.Clear();
            run.Run(container);
            ticks.Add(string.Join(' ', run.Log));
        }

        Assert.Equal(expected, string.Join('|', ticks));
        Assert.False(container.IsActive);
        Assert.False(container.IsRun);
    }

    [Fact]
    public void An_empty_condition_leaves_is_run_as_it_was()
    {
        (ConditionalContainerTask container, Rules run) = Build(once: false);
        run.Holds = true;
        run.Run(container);

        run.Holds = null;
        run.Log.Clear();
        run.Run(container);

        Assert.True(container.IsRun);
        Assert.Equal(["run"], run.Log);
    }

    [Fact]
    public void Children_brought_back_are_put_in_the_world_before_they_run()
    {
        (ConditionalContainerTask container, Rules run) = Build(once: false);
        run.Holds = false;
        run.Run(container);

        run.Holds = true;
        run.Log.Clear();
        run.Run(container);

        Assert.Equal(["in", "run", "start"], run.Log);
        Assert.True(container.IsActive);
    }

    private static (ConditionalContainerTask Container, Rules Run) Build(bool once)
    {
        TaskTypeRegistry types = new();
        TaskType containerType = types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);
        TaskType childType = types.Register<Child>("Child");
        TaskTree tree = new(types);
        var container = (ConditionalContainerTask)tree.New(tree.Root, containerType);
        container.RunAtStart = "start";
        container.RunAtStop = "stop";
        tree.New(container, childType);
        return (container, new Rules(tree.Root, once));
    }

    private sealed class Child : TickedTask
    {
        public override void Run(LevelRun level) => ((Rules)level).Log.Add("run");
    }

    private sealed class Rules(GameTask root, bool once) : LevelRun(root), IConditionalContainerRules
    {
        public List<string> Log { get; } = [];

        public bool? Holds { get; set; }

        public bool RunsChildrenTheTickItStops => once;

        public bool? Condition(ConditionalContainerTask container) => Holds;

        public void RunScript(ConditionalContainerTask container, string script) =>
            Log.Add(script);

        public void PutChildrenInWorld(ConditionalContainerTask container) => Log.Add("in");

        public void TakeChildrenOutOfWorld(ConditionalContainerTask container) => Log.Add("out");

        protected override void RunUntyped(GameTask task) => RunChildren(task);
    }
}
