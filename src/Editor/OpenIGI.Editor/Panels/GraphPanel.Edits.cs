using OpenIGI.Editor.Core.Commands;
using OpenIGI.Editor.Core.Graphs;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Scripting.Qsc;
using OpenIGI.Scripting.Vocabulary;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Panels;

/// <content>What the canvas's gestures do to the mission: each one a command on the document or the layout.</content>
internal sealed partial class GraphPanel
{
    private void Move(IReadOnlyCollection<string> ids, UiVec2 delta)
    {
        GraphLayout layout = OpenLayout;
        Dictionary<GraphNodeKey, (GraphPoint, GraphPoint)> moves = [];
        foreach (string id in ids)
        {
            // A reference keeps a place of its own in each graph that draws it.
            GraphNodeKey key = _ai is null && _source is { } source ? source.Scope.LayoutKey(new GraphNodeKey(id)) : new GraphNodeKey(id);
            GraphPoint from = layout.Positions.GetValueOrDefault(key);
            moves[key] = (from, new GraphPoint(from.X + delta.X, from.Y + delta.Y));
        }

        _workspace.Execute(new MoveNodesCommand(layout, moves));
    }

    // The layout of the graph open now: an AI script's own, or the mission's.
    private GraphLayout OpenLayout => _aiOpen?.Layout ?? _workspace.Logic.Layout;

    // The scope a comment made now belongs to: the open event, or the open mission graph.
    private string? OpenScopeKey => _ai is not null ? _aiScope?.Key : _source?.Scope.Key;

    // A frame's title drags what it holds; a comment's drags itself and what it covers.
    private void MoveFrame(string id, UiVec2 delta)
    {
        GraphLayout layout = OpenLayout;
        INodeCanvasSource source = _canvas.Source;
        CanvasFrame? frame = source.Frames.FirstOrDefault(f => f.Id == id);
        if (frame is null)
        {
            return;
        }

        string[] inside = [.. source.Nodes.Where(n => Contains(frame.Bounds, NodeCanvas.SizeOf(n))).Select(static n => n.Id)];
        if (inside.Length > 0)
        {
            Move(inside, delta);
        }

        if (frame.IsComment && layout.Comments.Find(c => c.Id == id) is { } comment)
        {
            GraphRect moved = comment.Bounds with { X = comment.Bounds.X + delta.X, Y = comment.Bounds.Y + delta.Y };
            _workspace.Execute(new SetCommentCommand(layout, comment, comment with { Bounds = moved }));
        }

        static bool Contains(UiRect outer, UiRect inner) =>
            inner.X >= outer.X && inner.Y >= outer.Y && inner.Right <= outer.Right && inner.Bottom <= outer.Bottom;
    }

    private void CommentSelection()
    {
        if (OpenScopeKey is not { } scope || _canvas.Selected.Count == 0)
        {
            _notify("Select the nodes to comment first.", false);
            return;
        }

        UiRect[] boxes = [.. _canvas.Source.Nodes.Where(n => _canvas.Selected.Contains(n.Id)).Select(NodeCanvas.SizeOf)];
        GraphRect bounds = new(boxes.Min(static b => b.X) - 30, boxes.Min(static b => b.Y) - 50, 0, 0);
        bounds = bounds with { Width = boxes.Max(static b => b.Right) + 30 - bounds.X, Height = boxes.Max(static b => b.Bottom) + 30 - bounds.Y };
        GraphComment comment = new(Guid.NewGuid().ToString("N"), "Comment", bounds, 0x4A6FA5) { Scope = scope };
        _workspace.Execute(new SetCommentCommand(OpenLayout, null, comment));
    }

    private void Link(string fromNode, string fromPin, string toNode, string toPin)
    {
        if (_ai is not null && AiLink(fromNode, fromPin, toNode, toPin))
        {
            return;
        }

        if (_ai is null && ActionLink(fromNode, fromPin, toNode, toPin))
        {
            return;
        }

        if (Graph is not { } graph || _workspace.Document is not { } document)
        {
            return;
        }

        GraphPinRef source = new(new GraphNodeKey(fromNode), fromPin);
        GraphPinRef input = GraphScope.Resolve(toNode, toPin);
        ScriptType wanted = InputType(graph, input);
        ScriptType given = OutputType(graph, source);
        if (!given.ConnectsTo(wanted))
        {
            _notify($"A {given} cannot go into a pin that wants a {wanted}.", true);
            return;
        }

        if (graph.Nodes[source.Node] is TaskGraphNode { Task.Id: < 0 } nameless)
        {
            _notify($"{nameless.Task} has no id, so nothing can refer to it; give it one in the Inspector first.", true);
            return;
        }

        if (wanted.Kind == ScriptTypeKind.TaskRef && graph.Nodes[input.Node] is TaskGraphNode target)
        {
            int id = ((TaskGraphNode)graph.Nodes[source.Node]).Task.Id;
            _workspace.Execute(SetParameterCommand.Numbers(document, target.Task, toPin, [id]));
            return;
        }

        if (fromPin == MissionGraph.Self)
        {
            _notify("A task itself is not a value: wire one of its fields instead.", true);
            return;
        }

        Rewrite(graph, input, source, "Wire");
    }

    private void Unlink(string nodeId, string pinId)
    {
        if (_ai is not null && AiUnlink(nodeId, pinId))
        {
            return;
        }

        if (_ai is null && ActionUnlink(nodeId, pinId))
        {
            return;
        }

        if (Graph is not { } graph || _workspace.Document is not { } document)
        {
            return;
        }

        GraphNodeKey key = GraphScope.Resolve(nodeId, pinId).Node;
        GraphPinRef pin = GraphScope.Resolve(nodeId, pinId);
        if (graph.Links.Any(l => l.To == pin))
        {
            if (graph.Nodes[key] is TaskGraphNode task && InputType(graph, pin).Kind == ScriptTypeKind.TaskRef)
            {
                _workspace.Execute(SetParameterCommand.Numbers(document, task.Task, pinId, [-1]));
            }
            else
            {
                Rewrite(graph, pin, null, "Unwire");
            }

            return;
        }

        // An output: unplug every input it feeds.
        GraphPinRef[] fed = [.. graph.Links.Where(l => l.From == pin).Select(static l => l.To)];
        UnplugAll(graph, fed, "Unwire");
    }

    private void Delete(IReadOnlyCollection<string> ids)
    {
        if (_ai is not null && AiDelete(ids))
        {
            return;
        }

        if (_ai is null)
        {
            ids = ActionDelete(ids);
        }

        if (_ai is null && ids.Count == 0)
        {
            return;
        }

        if (Graph is not { } graph || _workspace.Document is not { } document)
        {
            return;
        }

        List<IEditorCommand> steps = [];
        List<GraphPinRef> unplug = [];
        foreach (string id in ids)
        {
            switch (graph.Nodes.GetValueOrDefault(new GraphNodeKey(id)))
            {
                case TaskGraphNode task:
                    steps.Add(new RemoveTaskCommand(document, task.Task));
                    break;

                case ExpressionGraphNode expression:
                    unplug.AddRange(graph.Links.Where(l => l.From.Node == expression.Key).Select(static l => l.To));
                    break;
            }
        }

        if (unplug.Count > 0)
        {
            UnplugAll(graph, unplug, "Delete");
        }

        if (steps.Count > 0)
        {
            _workspace.Execute(new CompositeCommand(steps.Count == 1 ? "Delete task" : "Delete tasks", steps));
        }
    }

    // Plugs a source (or nothing) into an expression input, then writes the owning parameter's text as one command.
    private void Rewrite(MissionGraph graph, GraphPinRef input, GraphPinRef? source, string name)
    {
        if (OwnerOf(graph, input) is not { } owner)
        {
            _notify("Only a condition, an action or a number pin takes a wire.", true);
            return;
        }

        graph.Relink(input, source);
        Write(graph, [owner], name);
    }

    private void UnplugAll(MissionGraph graph, IEnumerable<GraphPinRef> inputs, string name)
    {
        HashSet<(GraphNodeKey, string)> owners = [];
        foreach (GraphPinRef input in inputs)
        {
            if (OwnerOf(graph, input) is { } owner)
            {
                graph.Relink(input, null);
                owners.Add(owner);
            }
        }

        Write(graph, owners, name);
    }

    private void Write(MissionGraph graph, IEnumerable<(GraphNodeKey Task, string Parameter)> owners, string name)
    {
        if (_workspace.Document is not { } document)
        {
            return;
        }

        IEditorCommand[] steps = [.. owners.Select(owner => (IEditorCommand)new SetParameterCommand(
            document,
            ((TaskGraphNode)graph.Nodes[owner.Task]).Task,
            owner.Parameter,
            [new QscStringLiteral(ExpressionGraphWriter.Write(graph, owner.Task, owner.Parameter))]))];
        if (steps.Length > 0)
        {
            _workspace.Execute(steps.Length == 1 ? steps[0] : new CompositeCommand(name, steps));
        }
        else
        {
            _workspace.Logic.Invalidate();
        }
    }

    // The task parameter an input belongs to: the task's own pin, or the expression the operand is part of.
    private static (GraphNodeKey Task, string Parameter)? OwnerOf(MissionGraph graph, GraphPinRef input) => graph.Nodes.GetValueOrDefault(input.Node) switch
    {
        TaskGraphNode task when task.Definition.Inputs.FirstOrDefault(p => p.Name == input.Pin) is { Role: { } role } && TaskPinRoles.IsExpression(role) =>
            (task.Key, input.Pin),
        ExpressionGraphNode expression => expression.Owner,
        _ => null,
    };

    private static ScriptType InputType(MissionGraph graph, GraphPinRef input) => graph.Nodes.GetValueOrDefault(input.Node) switch
    {
        TaskGraphNode task => task.Definition.Inputs.FirstOrDefault(p => p.Name == input.Pin)?.Type ?? ScriptType.Any,
        ExpressionGraphNode expression when int.TryParse(input.Pin, out int index) && index < expression.Inputs.Count => expression.Inputs[index].Type,
        _ => ScriptType.Any,
    };

    private static ScriptType OutputType(MissionGraph graph, GraphPinRef output) => graph.Nodes.GetValueOrDefault(output.Node) switch
    {
        TaskGraphNode task when output.Pin == MissionGraph.Self => ScriptType.TaskOf(task.Task.TypeName),
        TaskGraphNode task => task.Definition.Outputs.FirstOrDefault(p => p.Name == output.Pin)?.Type ?? ScriptType.Any,
        ExpressionGraphNode expression => expression.OutputType,
        _ => ScriptType.Any,
    };
}
