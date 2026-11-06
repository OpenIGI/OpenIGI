using System.Globalization;
using OpenIGI.Editor.Core.Graphs;
using OpenIGI.Scripting.Vocabulary;
using OpenIGI.UI.Editor.Widgets;

namespace OpenIGI.Editor.Panels;

/// <content>
/// The selected node's details, shown in the Inspector as a node editor's Details panel: what it is and does, and an editor for every
/// input it takes a value on (a tick box for true or false, a list for a constant, a number field, a text box). A mission task
/// is edited in the Inspector, which the selection already follows.
/// </content>
internal sealed partial class GraphPanel
{
    private string? _detailsFor;

    // Hands the Inspector the one selected node's details, again whenever the selection or the graph changes.
    private void RebuildDetails()
    {
        string? id = _canvas.Selected.Count == 1 ? _canvas.Selected.First() : null;
        string key = $"{id}|{_ai?.GetHashCode()}|{_source?.GetHashCode()}";
        if (key == _detailsFor)
        {
            return;
        }

        _detailsFor = key;
        if (id is null)
        {
            _workspace.ShowDetails(null);
            return;
        }

        GraphNodeKey node = new(id);
        if (_ai is not null && _aiOpen is { } script && _aiScope is { } scope && script.Graph.Nodes.ContainsKey(node))
        {
            _workspace.ShowDetails(new NodeDetails(key, grid => AiDetails(grid, script, scope, node)));
        }
        else if (Graph is { } graph && graph.Nodes.GetValueOrDefault(node) is { } found)
        {
            _workspace.ShowDetails(new NodeDetails(key, grid => MissionDetails(grid, graph, found)));
        }
        else
        {
            _workspace.ShowDetails(null);
        }
    }

    private void AiDetails(PropertyGrid grid, AiScript script, EventScope scope, GraphNodeKey key)
    {
        EventNodeView view = EventGraphView.Describe(script.Graph, scope, key);
        grid.Section(view.Title);
        grid.Text("Is", () => view.Subtitle, null);
        grid.Text("Does", () => view.Doc, null, view.Doc);
        List<EventPinView> inputs = [.. view.Inputs.Where(static p => !p.IsExec)];
        if (inputs.Count == 0)
        {
            return;
        }

        grid.Section("Inputs");
        foreach (EventPinView pin in inputs)
        {
            GraphPinRef input = new(key, pin.Id);
            string Current() => EventGraphView.Describe(script.Graph, scope, key).Inputs.FirstOrDefault(p => p.Id == pin.Id)?.Value ?? string.Empty;
            if (pin.Value is null && script.Graph.Links.Any(l => l.To == input))
            {
                string from = script.Graph.Links.First(l => l.To == input).From.Node is var source && script.Graph.Nodes.ContainsKey(source)
                    ? EventGraphView.Describe(script.Graph, scope, source).Title
                    : "a value";
                grid.Text(pin.Label, () => $"Wired from {from}", null, "Unwire it (Alt-click the pin) to give it a value here.");
                continue;
            }

            ScriptType type = PinType(script.Graph, input);
            ValueEditor(grid, pin.Label, type, Current, text => SetAiValue(script, input, text));
        }
    }

    private void MissionDetails(PropertyGrid grid, MissionGraph graph, GraphNode node)
    {
        if (node is TaskGraphNode task)
        {
            grid.Section(GraphScopes.Title(task));
            grid.Text("Is", () => task.Task.Id >= 0 ? $"{task.Task.TypeName} #{task.Task.Id}" : task.Task.TypeName, null);
            grid.Text("Settings", () => "In the Inspector, which follows the selection", null);
            return;
        }

        if (node is not ExpressionGraphNode expression || _source is not { } source)
        {
            return;
        }

        ExpressionNodeLabel label = ExpressionNodeText.Describe(expression, _host.Vocabulary);
        grid.Section(label.Title);
        grid.Text("Is", () => label.Subtitle, null);
        grid.Text("Does", () => label.Doc, null, label.Doc);
        if (expression.Inputs.Count == 0)
        {
            return;
        }

        grid.Section("Inputs");
        for (int i = 0; i < expression.Inputs.Count; i++)
        {
            GraphPinRef input = new(expression.Key, i.ToString(CultureInfo.InvariantCulture));
            string name = label.Inputs.ElementAtOrDefault(i) ?? input.Pin;
            if (source.Scope.Inline.TryGetValue(input, out _) || graph.SourceOf(input) is null)
            {
                string Current() => _source?.Scope.Inline.TryGetValue(input, out (GraphNodeKey Node, string Text) value) == true ? value.Text : string.Empty;
                ValueEditor(grid, name, expression.Inputs[i].Type, Current, text => Insert(graph, input, expression.Owner, text));
            }
            else
            {
                grid.Text(name, () => "Wired", null, "Unwire it (Alt-click the pin) to give it a value here.");
            }
        }
    }

    // The editor a value takes: a tick box for true or false, a list for a family of constants, a number, or text.
    private void ValueEditor(PropertyGrid grid, string label, ScriptType type, Func<string> current, Action<string> set)
    {
        string now = current();
        bool isBool = type.Kind == ScriptTypeKind.Bool || now is "TRUE" or "FALSE";
        if (isBool)
        {
            // Written as the script wrote it: TRUE/FALSE where it used names, 1/0 where it used numbers.
            bool names = now is "TRUE" or "FALSE" or "";
            grid.Bool(label, () => current() is "TRUE" or "1", value => set(names ? (value ? "TRUE" : "FALSE") : (value ? "1" : "0")));
            return;
        }

        if (type.Family is { } family && _host.Vocabulary.Families.TryGetValue(family, out ConstantFamily? constants))
        {
            // The family's constants offered, with their values; anything else the script can write still taken.
            ComboItem[] options = [.. constants.Values.Select(static c => new ComboItem(c.Key, c.Value.ToString(CultureInfo.InvariantCulture)))];
            grid.Combo(label, current, set, () => options);
            return;
        }

        if (type.Kind is ScriptTypeKind.Int or ScriptTypeKind.Real
            || double.TryParse(now, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            bool whole = type.Kind == ScriptTypeKind.Int || (now.Length > 0 && !now.Contains('.', StringComparison.Ordinal));
            NumberField field = grid.Number(
                label,
                () => double.TryParse(current(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0,
                (value, final) =>
                {
                    if (final)
                    {
                        set(whole ? Math.Round(value).ToString(CultureInfo.InvariantCulture) : value.ToString("0.###", CultureInfo.InvariantCulture));
                    }
                });
            field.Decimals = whole ? 0 : 3;
            return;
        }

        grid.Text(label, current, set);
    }

    // A click on a node's inline value: a tick box flips, any other value opens its picker.
    private void ValueClicked(string nodeId, string pinId)
    {
        if (_ai is null && ActionValue(nodeId, pinId))
        {
            return;
        }

        if (_canvas.Source.Nodes.FirstOrDefault(n => n.Id == nodeId)?.Inputs.FirstOrDefault(p => p.Id == pinId) is not { } pin)
        {
            return;
        }

        string flipped = pin.Value is "TRUE" or "FALSE" ? (pin.IsOn ? "FALSE" : "TRUE") : (pin.IsOn ? "0" : "1");
        if (_ai is not null && _aiOpen is { } script)
        {
            GraphPinRef input = new(new GraphNodeKey(nodeId), pinId);
            if (pin.IsToggle)
            {
                SetAiValue(script, input, flipped);
            }
            else
            {
                AiEditValue(nodeId, NodeCanvas.PinAt(_canvas.Source.Nodes.First(n => n.Id == nodeId), pinId, output: false));
            }

            return;
        }

        if (Graph is not { } graph)
        {
            return;
        }

        GraphPinRef real = GraphScope.Resolve(nodeId, pinId);
        if (OwnerOf(graph, real) is not { } owner)
        {
            return;
        }

        if (pin.IsToggle)
        {
            Insert(graph, real, owner, flipped);
            return;
        }

        if (Root is not { } root)
        {
            return;
        }

        CommandPalette palette = CommandPalette.Open(root, [new PaletteEntry("TRUE", () => Insert(graph, real, owner, "TRUE")), new PaletteEntry("FALSE", () => Insert(graph, real, owner, "FALSE"))], $"Value for {pin.Label}: a number, \"text\" or a name");
        palette.Typed = text => EventGraphEdits.IsValue(text) ? new PaletteEntry($"Set to {text}", () => Insert(graph, real, owner, text)) : null;
        palette.Query = pin.Value ?? string.Empty;
    }

    private void SetAiValue(AiScript script, GraphPinRef input, string text)
    {
        if (EventGraphEdits.SetValue(script.Graph, input, text, () => AiChanged(script)) is { } command)
        {
            _workspace.Execute(command);
        }
        else
        {
            _notify($"'{text}' is not a value: a number, \"text\" or a name.", true);
        }
    }
}
