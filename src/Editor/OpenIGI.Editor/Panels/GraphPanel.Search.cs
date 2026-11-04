using System.Globalization;
using OpenIGI.Editor.Core.Graphs;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Scripting.Vocabulary;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Panels;

/// <content>The node search: what can go into a pin a wire was dropped from, and the task types a new node can be.</content>
internal sealed partial class GraphPanel
{
    // Operators as the search offers them: the name searched, and the text put in (each "0" an empty operand to wire).
    private static readonly (string Name, string Text, ScriptTypeKind Gives)[] Operators =
    [
        ("And (&&)", "0 && 0", ScriptTypeKind.Bool),
        ("Or (||)", "0 || 0", ScriptTypeKind.Bool),
        ("Not (!)", "!0", ScriptTypeKind.Bool),
        ("Equals (==)", "0 == 0", ScriptTypeKind.Bool),
        ("Not equal (!=)", "0 != 0", ScriptTypeKind.Bool),
        ("Greater (>)", "0 > 0", ScriptTypeKind.Bool),
        ("Less (<)", "0 < 0", ScriptTypeKind.Bool),
        ("Add (+)", "0 + 0", ScriptTypeKind.Real),
        ("Subtract (-)", "0 - 0", ScriptTypeKind.Real),
        ("Multiply (*)", "0 * 0", ScriptTypeKind.Real),
        ("Seconds (n * GAME_FREQUENCY)", "1 * GAME_FREQUENCY", ScriptTypeKind.Int),
        ("Then (,)", "0, 0", ScriptTypeKind.Any),
    ];

    // A wire dropped on empty canvas: everything that fits the pin it came from.
    private void OpenSearchFromPin(string nodeId, string pinId, bool isOutput, UiVec2 at)
    {
        if (_ai is not null && AiAdd(at))
        {
            return;
        }

        if (Graph is not { } graph || Root is not { } root)
        {
            return;
        }

        GraphPinRef pin = GraphScope.Resolve(nodeId, pinId);
        List<PaletteEntry> entries = isOutput ? InputsAccepting(graph, pin) : SourcesFor(graph, pin);
        if (entries.Count == 0)
        {
            _notify("Nothing in this mission fits that pin.", false);
            return;
        }

        CommandPalette.Open(root, entries, isOutput ? "Wire into…" : "Search fields, natives, constants and operators");
    }

    private List<PaletteEntry> SourcesFor(MissionGraph graph, GraphPinRef input)
    {
        ScriptType wanted = InputType(graph, input);
        List<PaletteEntry> entries = [];
        if (OwnerOf(graph, input) is not { } owner)
        {
            return entries;
        }

        foreach ((string name, string text, ScriptTypeKind gives) in Operators.Where(o => new ScriptType(o.Gives).ConnectsTo(wanted)))
        {
            entries.Add(new PaletteEntry(name, () => Insert(graph, input, owner, text.Contains(' ', StringComparison.Ordinal) ? $"({text})" : text))
            {
                Detail = "Operator",
                Icon = KitIcon.Script,
            });
        }

        foreach (TaskGraphNode task in graph.TaskNodes.Where(static t => t.Task.Id >= 0 && t.IsShown))
        {
            foreach (TaskPin field in task.Definition.Outputs.Where(f => f.Type.ConnectsTo(wanted)))
            {
                entries.Add(new PaletteEntry($"{TaskPresentation.Name(task.Task)} · {field.Name}", () => Link(task.Key.Value, field.Name, input.Node.Value, input.Pin))
                {
                    Detail = $"{task.Task.TypeName} #{task.Task.Id}",
                    Icon = TaskPresentation.Icon(task.Task),
                });
            }
        }

        ScriptVocabulary vocabulary = _host.Vocabulary;
        bool action = wanted.Kind == ScriptTypeKind.Exec;
        foreach (NativeSignature native in vocabulary.CallableFrom(ScriptContexts.Mission))
        {
            bool fits = action ? native.Effect == NativeEffect.Action : native.Returns is { } returns && returns.ConnectsTo(wanted);
            if (fits)
            {
                string call = $"{native.Name}({string.Join(", ", native.Parameters.Take(native.MinimumArity).Select(static _ => "0"))})";
                entries.Add(new PaletteEntry(native.Name, () => Insert(graph, input, owner, call)) { Detail = native.Doc ?? "Native", Icon = KitIcon.Script });
            }
        }

        foreach (ConstantFamily family in vocabulary.Families.Values)
        {
            foreach ((string name, int value) in family.Values)
            {
                if (ScriptType.EnumOf(family.Name).ConnectsTo(wanted) || wanted.Kind is ScriptTypeKind.Bool or ScriptTypeKind.Int or ScriptTypeKind.Real)
                {
                    entries.Add(new PaletteEntry(name, () => Insert(graph, input, owner, name))
                    {
                        Detail = string.Create(CultureInfo.InvariantCulture, $"{family.Name} = {value}"),
                    });
                }
            }
        }

        return entries;
    }

    private List<PaletteEntry> InputsAccepting(MissionGraph graph, GraphPinRef output)
    {
        ScriptType given = OutputType(graph, output);
        List<PaletteEntry> entries = [];
        foreach (TaskGraphNode task in graph.TaskNodes.Where(static t => t.IsShown && !t.IsGenerated))
        {
            foreach (TaskPin pin in task.Definition.Inputs.Where(p => p.IsLinkable && given.ConnectsTo(p.Type)))
            {
                entries.Add(new PaletteEntry($"{TaskPresentation.Name(task.Task)} · {pin.Name}", () => Link(output.Node.Value, output.Pin, task.Key.Value, pin.Name))
                {
                    Detail = task.Task.TypeName,
                    Icon = TaskPresentation.Icon(task.Task),
                });
            }
        }

        return entries;
    }

    // Puts new text into an operand, as one command on its parameter.
    private void Insert(MissionGraph graph, GraphPinRef input, (GraphNodeKey Task, string Parameter) owner, string text)
    {
        GraphPinRef snippet = graph.AddSnippet(text, owner);
        Rewrite(graph, input, snippet, "Insert");
    }

    // A double click or right click on empty canvas: a task of any type the level declares, placed there.
    private void OpenAddTask(UiVec2 at)
    {
        if (_ai is not null && AiAdd(at))
        {
            return;
        }

        if (Root is not { } root || _workspace.Logic.Catalogue is not { } catalogue || _workspace.Document is not { } document)
        {
            return;
        }

        PaletteEntry[] entries = [.. ActionEntries(at), .. catalogue.Definitions.Values
            .Where(static d => !d.IsContainer || d.Inputs.Count > 0)
            .OrderByDescending(static d => d.IsLogic)
            .ThenBy(static d => d.TypeName, StringComparer.Ordinal)
            .Select(d => new PaletteEntry(d.TypeName, () => AddTask(document, d.TypeName, at)) { Detail = d.IsLogic ? "Logic" : "Object" })];
        CommandPalette.Open(root, entries, "Add a task");
    }

    private void AddTask(MissionDocument document, string type, UiVec2 at)
    {
        HashSet<int> used = [.. document.EnumerateTasks().Select(static t => t.Id)];
        int id = Enumerable.Range(1, 4095).FirstOrDefault(i => !used.Contains(i), -1);
        if (id < 0)
        {
            _notify("Every task id is taken.", true);
            return;
        }

        MissionTask task = new(type, string.Empty, id);
        if (!document.ApplyDeclaration(task))
        {
            _notify($"This level does not declare {type}.", true);
            return;
        }

        // Into the open graph's container, so it is drawn where it was put.
        MissionTask? root = _source?.Scope.Container is { } container && Graph?.Nodes.GetValueOrDefault(container) is TaskGraphNode holder
            ? holder.Task
            : document.RootTasks.FirstOrDefault();
        _workspace.Logic.Layout.Positions[new GraphNodeKey($"t{id}")] = new GraphPoint(at.X, at.Y);
        _workspace.Execute(new InsertTaskCommand(document, task, new TaskLocation(root, root?.Children.Count ?? 0)));
        _workspace.Selection.Set([task]);
    }
}
