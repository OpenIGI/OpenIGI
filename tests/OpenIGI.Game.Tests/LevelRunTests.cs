using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Level;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>The shared level run: what runs a task, in what order, and the title's say over the rest.</summary>
public sealed class LevelRunTests
{
    [Fact]
    public void A_typed_task_runs_itself_and_the_title_runs_the_rest()
    {
        (TaskTree tree, TaskType container, TaskType ticked) = Build();
        var typed = (Ticking)tree.New(tree.Root, ticked);
        GameTask plain = tree.New(tree.Root, container);
        RecordingRun run = new(tree.Root);

        run.RunChildren(tree.Root);

        Assert.Equal(["typed", "untyped"], run.Log);
        Assert.Equal(1, typed.Runs);
        Assert.Same(plain, run.Untyped.Single());
    }

    [Fact]
    public void A_run_that_only_passes_over_a_level_runs_no_typed_tasks_handler_but_walks_beneath_it()
    {
        (TaskTree tree, TaskType _, TaskType ticked) = Build();
        var parent = (Ticking)tree.New(tree.Root, ticked);
        var child = (Ticking)tree.New(parent, ticked);
        RecordingRun run = new(tree.Root) { OwnsLevel = false };

        run.RunChildren(tree.Root);

        Assert.Equal((0, 0), (parent.Runs, child.Runs));
        Assert.Empty(run.Log);
    }

    [Fact]
    public void A_run_passes_over_the_children_its_title_skips()
    {
        (TaskTree tree, TaskType _, TaskType ticked) = Build();
        var kept = (Ticking)tree.New(tree.Root, ticked);
        var skipped = (Ticking)tree.New(tree.Root, ticked);

        new RecordingRun(tree.Root) { Skipped = skipped }.RunChildren(tree.Root);

        Assert.Equal((1, 0), (kept.Runs, skipped.Runs));
    }

    private static (TaskTree Tree, TaskType Container, TaskType Ticked) Build()
    {
        TaskTypeRegistry types = new();
        TaskType container = types.Register<Plain>("Container");
        TaskType ticked = types.Register<Ticking>("Ticking");
        return (new TaskTree(types), container, ticked);
    }

    private sealed class Plain : GameTask
    {
    }

    private sealed class Ticking : TickedTask
    {
        public int Runs { get; private set; }

        public override void Run(LevelRun level)
        {
            Runs++;
            (level as RecordingRun)?.Log.Add("typed");
        }
    }

    private sealed class RecordingRun(GameTask root) : LevelRun(root)
    {
        public List<string> Log { get; } = [];

        public List<GameTask> Untyped { get; } = [];

        public GameTask? Skipped { get; init; }

        public override OpenIGI.Scripting.Expressions.LevelExpression Compile(string? source, GameTask owner) => throw new NotSupportedException();

        public override double Evaluate(OpenIGI.Scripting.Expressions.LevelExpression expression) => throw new NotSupportedException();

        public override double EvaluateLine(string source) => throw new NotSupportedException();

        protected override void RunUntyped(GameTask task)
        {
            Log.Add("untyped");
            Untyped.Add(task);
        }

        protected override bool Skips(GameTask child) => ReferenceEquals(child, Skipped) || base.Skips(child);
    }
}
