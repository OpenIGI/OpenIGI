using System.Globalization;
using OpenIGI.Editor.Core.Graphs;
using OpenIGI.Editor.Core.Graphs.Exec;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Panels;

/// <summary>
/// Action nodes and macros on a mission graph: added from the palette, wired by their exec pins, their values typed in,
/// collapsed into a project macro and expanded back, and compiled into the mission's tasks when it is saved. Every edit is
/// one undoable step on the graph's layout.
/// </summary>
internal sealed partial class GraphPanel
{
    private static readonly ExecNodeKind[] Addable = [ExecNodeKind.OnBecomesTrue, ExecNodeKind.Delay, ExecNodeKind.Branch, ExecNodeKind.Call, ExecNodeKind.Drive];

    private MacroLibrary? _macroLibrary;

    private MacroLibrary Macros => _macroLibrary ??= new MacroLibrary(_workspace.Project);

    private ExecLayer Actions => _workspace.Logic.Layout.Actions;

    // The palette's action entries: each kind, then the project's macros.
    private IEnumerable<PaletteEntry> ActionEntries(UiVec2 at)
    {
        foreach (ExecNodeKind kind in Addable)
        {
            yield return new PaletteEntry(MissionCanvasSource.TitleOf(kind), () => AddAction(new ExecNode(NewActionId(), kind), at)) { Detail = "Action" };
        }

        foreach (ExecMacro macro in Macros.Load().Values.OrderBy(static m => m.Name, StringComparer.Ordinal))
        {
            yield return new PaletteEntry(macro.Name, () => AddAction(new ExecNode(NewActionId(), ExecNodeKind.Macro)
            {
                Macro = macro.Name,
                Arguments = macro.Parameters.ToDictionary(static p => p, static _ => string.Empty, StringComparer.Ordinal),
            }, at))
            { Detail = "Macro" };
        }
    }

    private void AddAction(ExecNode node, UiVec2 at)
    {
        ExecLayerState before = ExecLayerState.Of(Actions);
        _workspace.Logic.Layout.Positions[new GraphNodeKey(MissionCanvasSource.CanvasIdOf(node.Id))] = new GraphPoint(at.X, at.Y);
        ExecLayerState after = new([.. before.Nodes, node with { Scope = OpenScopeKey }], before.Wires);
        _workspace.Execute(new SetActionsCommand($"Add {MissionCanvasSource.TitleOf(node.Kind)}", _workspace.Logic.Layout, before, after));
    }

    private string NewActionId()
    {
        HashSet<string> used = [.. Actions.Nodes.Select(static n => n.Id)];
        int n = used.Count + 1;
        while (used.Contains(string.Create(CultureInfo.InvariantCulture, $"a{n}")))
        {
            n++;
        }

        return string.Create(CultureInfo.InvariantCulture, $"a{n}");
    }

    // A wire dropped from an action's exec output onto another's input, or from a drive onto a condition pin.
    private bool ActionLink(string fromNode, string fromPin, string toNode, string toPin)
    {
        if (MissionCanvasSource.ActionIdOf(fromNode) is not { } from)
        {
            return MissionCanvasSource.ActionIdOf(toNode) is not null; // Nothing else wires into an action.
        }

        ExecLayerState before = ExecLayerState.Of(Actions);
        if (fromPin == MissionCanvasSource.DrivesPin)
        {
            ExecNode drive = before.Nodes.First(n => n.Id == from);
            ExecNode[] nodes = [.. before.Nodes.Select(n => n.Id == from ? drive with { Target = new GraphNodeKey(toNode), TargetPin = toPin } : n)];
            _workspace.Execute(new SetActionsCommand("Make a condition hold", _workspace.Logic.Layout, before, new ExecLayerState(nodes, before.Wires)));
            return true;
        }

        if (MissionCanvasSource.ActionIdOf(toNode) is not { } to || toPin != MissionCanvasSource.ExecIn || to == from)
        {
            _notify("An action's exec output runs another action: wire it to that action's exec input.", true);
            return true;
        }

        ExecWire wire = new(from, fromPin, to);
        if (!before.Wires.Contains(wire))
        {
            _workspace.Execute(new SetActionsCommand("Wire actions", _workspace.Logic.Layout, before, new ExecLayerState(before.Nodes, [.. before.Wires, wire])));
        }

        return true;
    }

    // Alt-click on an action's pin: its wires go; on a condition pin a drive makes hold, the drive lets go.
    private bool ActionUnlink(string nodeId, string pinId)
    {
        ExecLayerState before = ExecLayerState.Of(Actions);
        if (MissionCanvasSource.ActionIdOf(nodeId) is { } id)
        {
            ExecWire[] wires = [.. before.Wires.Where(w => !(pinId == MissionCanvasSource.ExecIn ? w.To == id : w.From == id && w.Pin == pinId))];
            ExecNode[] nodes = [.. before.Nodes.Select(n => n.Id == id && pinId == MissionCanvasSource.DrivesPin ? n with { Target = null, TargetPin = string.Empty } : n)];
            _workspace.Execute(new SetActionsCommand("Unwire action", _workspace.Logic.Layout, before, new ExecLayerState(nodes, wires)));
            return true;
        }

        if (before.Nodes.Any(n => n.Target?.Value == nodeId && n.TargetPin == pinId))
        {
            ExecNode[] nodes = [.. before.Nodes.Select(n => n.Target?.Value == nodeId && n.TargetPin == pinId ? n with { Target = null, TargetPin = string.Empty } : n)];
            _workspace.Execute(new SetActionsCommand("Unwire action", _workspace.Logic.Layout, before, new ExecLayerState(nodes, before.Wires)));
        }

        return false;
    }

    // Deletes the selected actions and their wires; returns what is left for the tasks' own delete.
    private IReadOnlyCollection<string> ActionDelete(IReadOnlyCollection<string> ids)
    {
        HashSet<string> gone = [.. ids.Select(MissionCanvasSource.ActionIdOf).OfType<string>()];
        if (gone.Count > 0)
        {
            ExecLayerState before = ExecLayerState.Of(Actions);
            ExecLayerState after = new([.. before.Nodes.Where(n => !gone.Contains(n.Id))], [.. before.Wires.Where(w => !gone.Contains(w.From) && !gone.Contains(w.To))]);
            _workspace.Execute(new SetActionsCommand(gone.Count == 1 ? "Delete action" : "Delete actions", _workspace.Logic.Layout, before, after));
        }

        return [.. ids.Where(static i => MissionCanvasSource.ActionIdOf(i) is null)];
    }

    // A click on an action's value: the text, seconds or argument typed in.
    private bool ActionValue(string nodeId, string pinId)
    {
        if (MissionCanvasSource.ActionIdOf(nodeId) is not { } id || Root is not { } root || Actions.Nodes.Find(n => n.Id == id) is not { } node)
        {
            return false;
        }

        string current;
        Func<string, ExecNode?> set;
        if (pinId == MissionCanvasSource.ConditionPin)
        {
            (current, set) = (node.Condition, text => node with { Condition = text });
        }
        else if (pinId == MissionCanvasSource.CallPin)
        {
            (current, set) = (node.Call, text => node with { Call = text });
        }
        else if (pinId == MissionCanvasSource.SecondsPin)
        {
            (current, set) = (node.Seconds.ToString(CultureInfo.InvariantCulture),
                text => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double s) && s >= 0 ? node with { Seconds = s } : null);
        }
        else if (pinId.StartsWith(MissionCanvasSource.ArgumentPrefix, StringComparison.Ordinal))
        {
            string parameter = pinId[MissionCanvasSource.ArgumentPrefix.Length..];
            (current, set) = (node.Arguments.GetValueOrDefault(parameter, string.Empty),
                text => node with { Arguments = new Dictionary<string, string>(node.Arguments, StringComparer.Ordinal) { [parameter] = text } });
        }
        else
        {
            return false;
        }

        CommandPalette palette = CommandPalette.Open(root, [], pinId == MissionCanvasSource.SecondsPin ? "Seconds to wait" : "Level-expression text, as a task's condition reads");
        palette.Typed = text => set(text.Trim()) is { } changed
            ? new PaletteEntry($"Set to {text.Trim()}", () =>
            {
                ExecLayerState before = ExecLayerState.Of(Actions);
                ExecLayerState after = new([.. before.Nodes.Select(n => n.Id == id ? changed : n)], before.Wires);
                _workspace.Execute(new SetActionsCommand("Edit action", _workspace.Logic.Layout, before, after));
            })
            : null;
        palette.Query = current;
        return true;
    }

    // Collapses the selected actions into a project macro, named in a palette.
    private void CollapseToMacro()
    {
        if (Root is not { } root)
        {
            return;
        }

        string[] ids = [.. _canvas.Selected.Select(MissionCanvasSource.ActionIdOf).OfType<string>()];
        if (ids.Length == 0)
        {
            _notify("Select the action nodes to collapse first (an event stays out: it starts its own chain).", false);
            return;
        }

        CommandPalette palette = CommandPalette.Open(root, [], "The macro's name");
        palette.Typed = text => text.Trim().Length == 0 ? null : new PaletteEntry($"Collapse into macro {text.Trim()}", () => Collapse(ids, text.Trim()));
    }

    private void Collapse(string[] ids, string name)
    {
        string useId = NewActionId();
        if (ExecMacroEdits.Collapse(Actions, ids, name, useId) is not { } collapsed)
        {
            _notify("Nothing selected can go in a macro: an event starts its own chain.", true);
            return;
        }

        try
        {
            Macros.Save(collapsed.Macro);
        }
        catch (IOException error)
        {
            _notify($"The macro could not be saved: {error.Message}", true);
            return;
        }

        // The macro node where the first of the collapsed nodes stood.
        GraphLayout layout = _workspace.Logic.Layout;
        GraphPoint at = ids.Select(i => layout.Positions.GetValueOrDefault(new GraphNodeKey(MissionCanvasSource.CanvasIdOf(i)))).MinBy(static p => p.X + p.Y);
        layout.Positions[new GraphNodeKey(MissionCanvasSource.CanvasIdOf(useId))] = at;
        _workspace.Execute(new SetActionsCommand($"Collapse to macro {name}", layout, ExecLayerState.Of(Actions), collapsed.Layer));
        _notify($"Collapsed into macro {name}, saved with the project; it is in the palette for every mission graph.", false);
    }

    // A double click on a macro node: its nodes back in its place.
    private bool ExpandMacro(string nodeId)
    {
        if (MissionCanvasSource.ActionIdOf(nodeId) is not { } id || Actions.Nodes.Find(n => n.Id == id) is not { Kind: ExecNodeKind.Macro } use)
        {
            return false;
        }

        if (!Macros.Load().TryGetValue(use.Macro, out ExecMacro? macro) || ExecMacroEdits.Expand(Actions, id, macro) is not { } expanded)
        {
            _notify($"The macro {use.Macro} is not in this project.", true);
            return true;
        }

        GraphLayout layout = _workspace.Logic.Layout;
        GraphPoint at = layout.Positions.GetValueOrDefault(new GraphNodeKey(nodeId));
        for (int i = 0; i < expanded.Added.Count; i++)
        {
            layout.Positions[new GraphNodeKey(MissionCanvasSource.CanvasIdOf(expanded.Added[i]))] = new GraphPoint(at.X + (i * 260), at.Y);
        }

        _workspace.Execute(new SetActionsCommand($"Expand macro {use.Macro}", layout, ExecLayerState.Of(Actions), expanded.Layer));
        return true;
    }

    // Before the mission is written: the action nodes, their macros expanded, lowered into the tasks the game runs.
    private void CompileActions()
    {
        if (Actions.Nodes.Count == 0 || _workspace.Document is not { } document || Graph is not { } graph)
        {
            return;
        }

        try
        {
            ExecLayer flat = ExecMacroExpander.Expand(Actions, Macros.Load());
            LoweredLayer lowered = ExecLayerCompiler.Lower(flat, document, graph);
            _workspace.Execute(ExecLayerCompiler.ToCommand(document, Actions, lowered));
        }
        catch (ExecLayerException error)
        {
            _notify($"The action nodes were not compiled: {error.Message}", true);
        }
    }
}
