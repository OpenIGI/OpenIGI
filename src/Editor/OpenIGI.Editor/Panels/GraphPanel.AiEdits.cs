using OpenIGI.Editor.Core.Graphs;
using OpenIGI.Scripting.Qsc;
using OpenIGI.Scripting.Vocabulary;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Panels;

/// <content>
/// Editing AI graphs: wiring, unwiring, deleting, adding a native, and writing a value onto a pin. Each is one command in the
/// mission's history, so it undoes with everything else; saving the mission compiles every edited script into the project.
/// </content>
internal sealed partial class GraphPanel
{
    private bool AiLink(string fromNode, string fromPin, string toNode, string toPin)
    {
        if (_aiOpen is not { } script)
        {
            return false;
        }

        GraphPinRef from = new(new GraphNodeKey(fromNode), fromPin);
        GraphPinRef to = new(new GraphNodeKey(toNode), toPin);
        if (EventGraphEdits.Link(script.Graph, from, to, () => AiChanged(script)) is not { } command)
        {
            _notify("An exec pin wires only to an exec pin, and a value only to a value.", true);
            return true;
        }

        _workspace.Execute(command);
        return true;
    }

    private bool AiUnlink(string nodeId, string pinId)
    {
        if (_aiOpen is { } script && EventGraphEdits.Unlink(script.Graph, new GraphPinRef(new GraphNodeKey(nodeId), pinId), () => AiChanged(script)) is { } command)
        {
            _workspace.Execute(command);
        }

        return true;
    }

    private bool AiDelete(IReadOnlyCollection<string> ids)
    {
        if (_aiOpen is { } script && EventGraphEdits.Delete(script.Graph, ids.Select(static id => new GraphNodeKey(id)), () => AiChanged(script)) is { } command)
        {
            _workspace.Execute(command);
        }

        return true;
    }

    // A native the AI can call, placed where asked; wired by dragging from the exec pin before it.
    private static readonly (string Title, QscBinaryOperator Op)[] BinaryOperators =
    [
        ("Equal", QscBinaryOperator.Equal), ("Not Equal", QscBinaryOperator.NotEqual), ("Less", QscBinaryOperator.Less),
        ("Less or Equal", QscBinaryOperator.LessOrEqual), ("Greater", QscBinaryOperator.Greater), ("Greater or Equal", QscBinaryOperator.GreaterOrEqual),
        ("AND", QscBinaryOperator.LogicalAnd), ("OR", QscBinaryOperator.LogicalOr), ("Add", QscBinaryOperator.Add),
        ("Subtract", QscBinaryOperator.Subtract), ("Multiply", QscBinaryOperator.Multiply), ("Divide", QscBinaryOperator.Divide),
    ];

    // What can be added to an AI graph, placed where asked: flow (Branch, While), actions, queries and operators.
    private bool AiAdd(UiVec2 at)
    {
        if (_aiOpen is not { } script || _aiScope is not { } scope || Root is not { } root)
        {
            return true;
        }

        void Place((EventGraphEditCommand Command, GraphNodeKey Key) added)
        {
            script.Layout.Positions[added.Key] = new GraphPoint(at.X, at.Y);
            _workspace.Execute(added.Command);
        }

        Action changed = () => AiChanged(script);
        List<PaletteEntry> entries =
        [
            new("Branch", () => Place(EventGraphEdits.AddFlow(script.Graph, EventStatementKind.Branch, scope, changed))) { Detail = "Flow · if, else" },
            new("While", () => Place(EventGraphEdits.AddFlow(script.Graph, EventStatementKind.Loop, scope, changed))) { Detail = "Flow · loop while a condition holds" },
            new("NOT", () => Place(EventGraphEdits.AddValue(script.Graph, new EventValueNode(default, EventValueKind.Unary, 1) { Unary = QscUnaryOperator.LogicalNot }, scope, changed))) { Detail = "Logic · !value" },
        ];
        foreach ((string title, QscBinaryOperator op) in BinaryOperators)
        {
            EventValueNode value = new(default, EventValueKind.Binary, 2) { Binary = op };
            entries.Add(new PaletteEntry(title, () => Place(EventGraphEdits.AddValue(script.Graph, value, scope, changed))) { Detail = "Operator" });
        }

        foreach (NativeSignature native in _host.Vocabulary.Natives.Values.Where(static n => n.Contexts.HasFlag(ScriptContexts.Ai)).OrderBy(static n => n.Name, StringComparer.Ordinal))
        {
            string words = ExpressionNodeText.Humanise(native.Name).Words;
            entries.Add(native.Effect == NativeEffect.Action
                ? new PaletteEntry(words, () => Place(EventGraphEdits.AddCall(script.Graph, native, scope, changed))) { Detail = $"Action · {native.Name}" }
                : new PaletteEntry(words, () => Place(EventGraphEdits.AddValue(script.Graph, new EventValueNode(default, EventValueKind.Call, native.Parameters.Count) { Text = native.Name, Native = native }, scope, changed))) { Detail = $"Query · {native.Name}" });
        }

        CommandPalette.Open(root, entries, "Add to the AI graph: Branch, While, an action, a query or an operator");
        return true;
    }

    // A double click on an input of an AI node: a value for it, typed or picked from the constants its type takes.
    private void AiEditValue(string nodeId, UiVec2 at)
    {
        if (_aiOpen is not { } script || _ai is null || Root is not { } root
            || _ai.Nodes.FirstOrDefault(n => n.Id == nodeId) is not { } node)
        {
            return;
        }

        CanvasPin? pin = node.Inputs
            .Where(static p => !p.IsExec)
            .MinBy(p => MathF.Abs(NodeCanvas.PinAt(node, p.Id, output: false).Y - at.Y));
        if (pin is null || MathF.Abs(NodeCanvas.PinAt(node, pin.Id, output: false).Y - at.Y) > NodeCanvas.RowHeight)
        {
            return;
        }

        GraphPinRef input = new(new GraphNodeKey(nodeId), pin.Id);
        void Set(string text)
        {
            if (EventGraphEdits.SetValue(script.Graph, input, text, () => AiChanged(script)) is { } command)
            {
                _workspace.Execute(command);
            }
        }

        // The constants of the pin's family first (the events for an event pin, the flags for a flag pin), then TRUE/FALSE.
        ScriptType type = PinType(script.Graph, input);
        IEnumerable<string> constants = type.Family is { } family && _host.Vocabulary.Families.TryGetValue(family, out ConstantFamily? values)
            ? values.Values.Select(static v => v.Key)
            : ["TRUE", "FALSE"];
        PaletteEntry[] entries = [.. constants.Select(c => new PaletteEntry(c, () => Set(c)))];
        CommandPalette palette = CommandPalette.Open(root, entries, $"Value for {pin.Label}: a number, \"text\" or a name");
        palette.Typed = text => EventGraphEdits.IsValue(text) ? new PaletteEntry($"Set to {text}", () => Set(text)) : null;
        palette.Query = pin.Value ?? string.Empty;
    }

    // The type an AI node's input wants: the native's parameter for a call, else anything.
    private static ScriptType PinType(EventGraph graph, GraphPinRef input) =>
        graph.Nodes.GetValueOrDefault(input.Node) switch
        {
            EventStatementNode { Native: { } native } when int.TryParse(input.Pin, out int i) && i < native.Parameters.Count => native.Parameters[i].Type,
            EventValueNode { Native: { } native } when int.TryParse(input.Pin, out int i) && i < native.Parameters.Count => native.Parameters[i].Type,
            _ => ScriptType.Any,
        };

    // After an edit or its undo: the script is split again, marked unsaved, and drawn again.
    private void AiChanged(AiScript script)
    {
        script.IsDirty = true;
        script.Scopes = EventGraphView.Scopes(script.Graph);
        _listedScopes = null;
        _stale = true;
    }

    // Saving the mission: each edited script written back from its graph and compiled into the project, and each layout
    // (moved nodes, comments) saved beside it, moved onto the keys the script reads back with.
    private void SaveAiScripts()
    {
        foreach (AiScript script in _aiScripts.Values.Where(static s => s.IsDirty || s.IsLayoutDirty))
        {
            string gamePath = script.Path[LocalDevice.Length..];
            try
            {
                QscScript written = EventGraphWriter.Write(script.Graph);
                GraphLayout layout = script.Layout;
                if (script.IsDirty)
                {
                    byte[] image = QscCompiler.Compile(written);
                    _workspace.WriteToProject(gamePath, image);
                    EventGraph read = EventGraph.Read(QscDecompiler.Decompile(image), _host.Vocabulary);
                    (layout, int left) = Remapped(script, read);
                    if (left > 0)
                    {
                        _notify($"{script.Name}: {left} node{(left == 1 ? string.Empty : "s")} nothing runs (not wired into an event) were left out.", false);
                    }

                    script.IsDirty = false;
                }

                layout.TextHashes.Clear();
                layout.TextHashes[AiScriptHashKey] = GraphLayout.HashOf(QscPrinter.Print(written));
                _workspace.Project.WriteGraph(gamePath, layout.Write());
                script.IsLayoutDirty = false;
            }
            catch (EventGraphWriteException error)
            {
                _notify($"{script.Name} was not saved: {error.Message}", true);
            }
            catch (IOException error)
            {
                _notify($"{script.Name} could not be written: {error.Message}", true);
            }
        }
    }

    // The layout moved onto the keys of the graph read back from the saved script, and how many nodes it no longer holds.
    private static (GraphLayout Layout, int Left) Remapped(AiScript script, EventGraph read)
    {
        IReadOnlyDictionary<GraphNodeKey, GraphNodeKey> map = EventGraphKeys.Map(script.Graph, read);
        GraphLayout layout = new();
        foreach ((GraphNodeKey key, GraphPoint at) in script.Layout.Positions)
        {
            if (map.TryGetValue(key, out GraphNodeKey moved))
            {
                layout.Positions[moved] = at;
            }
        }

        foreach (GraphNodeKey key in script.Layout.Collapsed.Where(map.ContainsKey))
        {
            layout.Collapsed.Add(map[key]);
        }

        // A comment belongs to an event by the event's key, which may have moved too.
        foreach (GraphComment comment in script.Layout.Comments)
        {
            string? scope = comment.Scope is { } key && map.TryGetValue(new GraphNodeKey(key), out GraphNodeKey moved) ? moved.Value : comment.Scope;
            layout.Comments.Add(comment with { Scope = scope });
        }

        int left = script.Graph.Nodes.Keys.Count(k => !map.ContainsKey(k) && !EventGraphView.IsInlined(script.Graph.Nodes[k]));
        return (layout, left);
    }
}
