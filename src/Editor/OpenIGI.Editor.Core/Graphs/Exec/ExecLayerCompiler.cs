using System.Globalization;
using OpenIGI.Editor.Core.Commands;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Scripting.Qsc;

namespace OpenIGI.Editor.Core.Graphs.Exec;

/// <summary>Why an action layer cannot be lowered into a mission.</summary>
/// <param name="message">What is wrong.</param>
public sealed class ExecLayerException(string message) : Exception(message);

/// <summary>What lowering an action layer makes: the tasks to add, and the condition pins to drive.</summary>
/// <param name="Tasks">The generated tasks, each with its parameters set, for the <c>Blueprint</c> container.</param>
/// <param name="Drives">Each driven pin's new text, by task and pin.</param>
/// <param name="Generated">The ids each node lowered to, to keep in the layer.</param>
public sealed record LoweredLayer(
    IReadOnlyList<MissionTask> Tasks,
    IReadOnlyDictionary<(MissionTask Task, string Pin), string> Drives,
    IReadOnlyDictionary<string, List<int>> Generated);

/// <summary>Lowers a mission's action nodes into the tasks retail missions use for the same thing.</summary>
/// <remarks>
/// <para>
/// A chain runs in stages, each a condition that holds from the moment the chain reaches it. An event's stage is its latch:
/// an <c>EditVariable</c> whose <c>Add</c> is <c>nValue == 0 &amp;&amp; (condition)</c>, so it counts to one the first tick the
/// condition holds and never again, exactly the idiom retail levels use. A delay's stage is a <c>LevelTimer</c> that runs while
/// the stage before it holds, and the next stage is its tick count reaching the delay. A branch reads its condition once, on
/// two latches that each rule the other out, so it never changes its mind afterwards.
/// </para>
/// <para>
/// A stage's calls are joined by commas, the one operator the game evaluates on both sides in order, into the run-at-start
/// text of a generated <c>ConditionalContainer</c> whose condition is the stage. A drive writes the stage into the driven pin;
/// several stages driving one pin are joined with <c>||</c>. Lowering again reuses each node's ids, so nothing that refers to
/// a generated task is broken by an edit elsewhere.
/// </para>
/// </remarks>
public static class ExecLayerCompiler
{
    /// <summary>The label of the container the generated tasks live in.</summary>
    public const string ContainerLabel = "Blueprint";

    private const string EditVariable = "EditVariable";
    private const string LevelTimer = "LevelTimer";
    private const string Conditional = "ConditionalContainer";

    /// <summary>Lowers a layer.</summary>
    /// <param name="layer">The action nodes.</param>
    /// <param name="document">The mission, whose declarations the generated tasks use and whose ids they avoid.</param>
    /// <param name="graph">The mission's graph, to find driven tasks.</param>
    /// <returns>The tasks and drives.</returns>
    /// <exception cref="ExecLayerException">The level does not declare a type lowering needs, or the id table is full.</exception>
    public static LoweredLayer Lower(ExecLayer layer, MissionDocument document, MissionGraph graph)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(graph);
        Lowering lowering = new(layer, document, graph);
        foreach (ExecNode root in layer.Nodes.Where(static n => n.Kind == ExecNodeKind.OnBecomesTrue))
        {
            MissionTask latch = lowering.Latch(root, id => $"EditVariable_{id}.nValue == 0 && ({root.Condition})");
            lowering.Chain(root, ExecNode.Then, Stage(latch));
        }

        return lowering.Result();
    }

    /// <summary>The lowered layer as one undoable step: generated tasks replaced, driven pins written.</summary>
    /// <param name="document">The mission.</param>
    /// <param name="layer">The layer, whose <see cref="ExecLayer.Generated"/> the step updates.</param>
    /// <param name="lowered">The lowering.</param>
    /// <returns>The step.</returns>
    public static IEditorCommand ToCommand(MissionDocument document, ExecLayer layer, LoweredLayer lowered)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(lowered);
        List<IEditorCommand> steps = [];
        // The new container takes the old one's place, or goes last in the first root task.
        MissionTask? old = document.EnumerateTasks().FirstOrDefault(static t => t.TypeName == "Container" && t.Label == ContainerLabel);
        MissionTask? root = document.RootTasks.FirstOrDefault();
        TaskLocation place = old is not null ? document.Locate(old) : new TaskLocation(root, root?.Children.Count ?? 0);
        if (old is not null)
        {
            steps.Add(new RemoveTaskCommand(document, old));
        }

        MissionTask container = new("Container", ContainerLabel);
        foreach (MissionTask task in lowered.Tasks)
        {
            container.AddChild(task);
        }

        steps.Add(new InsertTaskCommand(document, container, place));
        foreach (((MissionTask task, string pin), string text) in lowered.Drives)
        {
            steps.Add(new SetParameterCommand(document, task, pin, [new QscStringLiteral(text)]));
        }

        Dictionary<string, List<int>> before = new(layer.Generated);
        steps.Add(new DelegateCommand(
            "Keep generated ids",
            () => Replace(layer.Generated, lowered.Generated),
            () => Replace(layer.Generated, before)));
        return new CompositeCommand("Compile actions", steps);
    }

    private static string Stage(MissionTask latch) => $"EditVariable_{latch.Id}.nValue";

    private static void Replace(Dictionary<string, List<int>> target, IReadOnlyDictionary<string, List<int>> source)
    {
        target.Clear();
        foreach ((string key, List<int> ids) in source)
        {
            target[key] = [.. ids];
        }
    }

    private sealed class Lowering(ExecLayer layer, MissionDocument document, MissionGraph graph)
    {
        private const int MaxId = 4095;

        private readonly List<MissionTask> _tasks = [];
        private readonly Dictionary<(MissionTask, string), List<string>> _drives = [];
        private readonly Dictionary<string, List<int>> _generated = new(StringComparer.Ordinal);
        private readonly HashSet<int> _used = Used(document, layer);
        private readonly Dictionary<string, int> _cursor = new(StringComparer.Ordinal);

        public void Chain(ExecNode from, string pin, string stage)
        {
            List<string> calls = [];
            foreach (ExecNode next in layer.Next(from, pin))
            {
                switch (next.Kind)
                {
                    case ExecNodeKind.Call:
                        calls.Add(next.Call);
                        Chain(next, ExecNode.Then, stage);
                        break;

                    case ExecNodeKind.Drive when next.Target is { } key && graph.Nodes.GetValueOrDefault(key) is TaskGraphNode target:
                        Drive(target.Task, next.TargetPin, stage);
                        Chain(next, ExecNode.Then, stage);
                        break;

                    case ExecNodeKind.Delay:
                        MissionTask timer = Generate(next, LevelTimer);
                        Set(timer, "On", stage);
                        Set(timer, "Initial run", ScriptValues.Bool(false));
                        string ticks = (next.Seconds * 30.0).ToString("0.###", CultureInfo.InvariantCulture);
                        // The timer counts the tick its stage starts on, so "past N" is N ticks after that tick.
                        Chain(next, ExecNode.Then, $"LevelTimer_{timer.Id}.nTick > {ticks}");
                        break;

                    case ExecNodeKind.Branch:
                        MissionTask yes = Generate(next, EditVariable);
                        MissionTask no = Generate(next, EditVariable);
                        Set(yes, "Add", $"EditVariable_{yes.Id}.nValue == 0 && !EditVariable_{no.Id}.nValue && ({stage}) && ({next.Condition})");
                        Set(no, "Add", $"EditVariable_{no.Id}.nValue == 0 && !EditVariable_{yes.Id}.nValue && ({stage}) && !({next.Condition})");
                        Chain(next, ExecNode.True, Stage(yes));
                        Chain(next, ExecNode.False, Stage(no));
                        break;

                    case ExecNodeKind.OnBecomesTrue:
                        throw new ExecLayerException("An event starts a chain; nothing can run into it.");
                }
            }

            if (calls.Count > 0)
            {
                MissionTask container = Generate(from, Conditional, $"{from.Id}:{pin}");
                Set(container, "Condition", stage);
                Set(container, "Run at start", string.Join(", ", calls));
            }
        }

        public MissionTask Latch(ExecNode node, Func<int, string> add)
        {
            MissionTask latch = Generate(node, EditVariable);
            Set(latch, "Add", add(latch.Id));
            return latch;
        }

        public LoweredLayer Result() => new(
            _tasks,
            _drives.ToDictionary(static d => d.Key, static d => string.Join(" || ", d.Value.Select(static s => $"({s})"))),
            _generated);

        private void Drive(MissionTask task, string pin, string stage)
        {
            if (!_drives.TryGetValue((task, pin), out List<string>? stages))
            {
                _drives[(task, pin)] = stages = [];
            }

            stages.Add(stage);
        }

        // A task for a node, its id the node's next kept one or else the next free one from the top of the table down.
        private MissionTask Generate(ExecNode node, string type, string? slot = null)
        {
            string key = slot ?? node.Id;
            int index = _cursor.GetValueOrDefault(key);
            _cursor[key] = index + 1;
            int id = layer.Generated.TryGetValue(key, out List<int>? kept) && index < kept.Count ? kept[index] : Free();
            _used.Add(id);
            if (!_generated.TryGetValue(key, out List<int>? ids))
            {
                _generated[key] = ids = [];
            }

            ids.Add(id);
            MissionTask task = new(type, string.Empty, id);
            if (!document.ApplyDeclaration(task))
            {
                throw new ExecLayerException($"This level does not declare {type}, which an action needs.");
            }

            _tasks.Add(task);
            return task;
        }

        private int Free()
        {
            for (int id = MaxId; id > 0; id--)
            {
                if (!_used.Contains(id))
                {
                    return id;
                }
            }

            throw new ExecLayerException("Every task id is taken.");
        }

        private static void Set(MissionTask task, string parameter, string text) => Set(task, parameter, new QscStringLiteral(text));

        private static void Set(MissionTask task, string parameter, QscExpression value)
        {
            MissionParameter found = task.FindParameter(parameter)
                ?? throw new ExecLayerException($"{task.TypeName} has no {parameter} in this level's declaration.");
            found.Arguments.Clear();
            found.Arguments.Add(value);
        }

        // Ids in the mission, apart from the ones the layer generated last time, which are its to reuse.
        private static HashSet<int> Used(MissionDocument document, ExecLayer layer)
        {
            HashSet<int> kept = [.. layer.Generated.Values.SelectMany(static ids => ids)];
            return [.. document.EnumerateTasks().Select(static t => t.Id).Where(id => id >= 0 && !kept.Contains(id))];
        }
    }
}
