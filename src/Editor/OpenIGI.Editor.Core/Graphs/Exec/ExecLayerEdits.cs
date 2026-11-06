using System.Text.RegularExpressions;
using OpenIGI.Editor.Core.Commands;

namespace OpenIGI.Editor.Core.Graphs.Exec;

/// <summary>An action layer's nodes and wires as they stand at a moment, to restore.</summary>
/// <param name="Nodes">The nodes.</param>
/// <param name="Wires">The wires.</param>
public sealed record ExecLayerState(IReadOnlyList<ExecNode> Nodes, IReadOnlyList<ExecWire> Wires)
{
    /// <summary>Takes a layer's state.</summary>
    /// <param name="layer">The layer.</param>
    /// <returns>Its nodes and wires, copied.</returns>
    public static ExecLayerState Of(ExecLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        return new ExecLayerState([.. layer.Nodes], [.. layer.Wires]);
    }

    /// <summary>Puts the state back into a layer.</summary>
    /// <param name="layer">The layer.</param>
    public void Into(ExecLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        layer.Nodes.Clear();
        layer.Nodes.AddRange(Nodes);
        layer.Wires.Clear();
        layer.Wires.AddRange(Wires);
    }
}

/// <summary>An edit of a graph's action nodes, as one undoable step: the layer before and after.</summary>
/// <param name="name">What the step is called in the history.</param>
/// <param name="layout">The layout the layer belongs to, told of the change.</param>
/// <param name="before">The layer before.</param>
/// <param name="after">The layer after.</param>
public sealed class SetActionsCommand(string name, GraphLayout layout, ExecLayerState before, ExecLayerState after) : IEditorCommand
{
    /// <inheritdoc/>
    public string Name => name;

    /// <inheritdoc/>
    public void Apply()
    {
        after.Into(layout.Actions);
        layout.NotifyChanged();
    }

    /// <inheritdoc/>
    public void Undo()
    {
        before.Into(layout.Actions);
        layout.NotifyChanged();
    }
}

/// <summary>Turning action nodes into a project macro, and a macro node back into the nodes it stands for.</summary>
public static partial class ExecMacroEdits
{
    /// <summary>The id a collapsed macro's entry node takes in its body.</summary>
    public const string EntryId = "entry";

    /// <summary>The id a collapsed macro's exit node takes in its body.</summary>
    public const string ExitId = "exit";

    /// <summary>
    /// Collapses action nodes into a macro: the nodes and the wires among them become its body, run from an entry by what
    /// ran them from outside and going on through an exit to what they ran outside; in the layer they become one macro node.
    /// </summary>
    /// <param name="layer">The layer, left as it is.</param>
    /// <param name="ids">The nodes to collapse.</param>
    /// <param name="name">The macro's name.</param>
    /// <param name="useId">The new macro node's id.</param>
    /// <returns>The macro, and the layer's state with the macro node in the nodes' place; null when no node can go in a macro.</returns>
    /// <remarks>
    /// An event (<see cref="ExecNodeKind.OnBecomesTrue"/>) starts a chain of its own and has no way in, so it stays out of a
    /// macro. Every <c>$Name</c> the nodes' text holds is one of the macro's parameters, filled in by the macro node.
    /// </remarks>
    public static (ExecMacro Macro, ExecLayerState Layer)? Collapse(ExecLayer layer, IReadOnlyCollection<string> ids, string name, string useId)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(ids);
        HashSet<string> inside = [.. layer.Nodes.Where(n => ids.Contains(n.Id) && n.Kind is not (ExecNodeKind.OnBecomesTrue or ExecNodeKind.MacroEntry or ExecNodeKind.MacroExit)).Select(static n => n.Id)];
        if (inside.Count == 0)
        {
            return null;
        }

        ExecLayer body = new();
        body.Nodes.Add(new ExecNode(EntryId, ExecNodeKind.MacroEntry));
        body.Nodes.AddRange(layer.Nodes.Where(n => inside.Contains(n.Id)).Select(static n => n with { Scope = null }));
        List<ExecWire> into = [.. layer.Wires.Where(w => !inside.Contains(w.From) && inside.Contains(w.To))];
        List<ExecWire> outOf = [.. layer.Wires.Where(w => inside.Contains(w.From) && !inside.Contains(w.To))];
        body.Wires.AddRange(layer.Wires.Where(w => inside.Contains(w.From) && inside.Contains(w.To)));

        // In by what ran the nodes from outside; with nothing, by the first of them that nothing inside runs.
        IEnumerable<string> starts = into.Count > 0
            ? into.Select(static w => w.To).Distinct()
            : [layer.Nodes.First(n => inside.Contains(n.Id) && !body.Wires.Any(w => w.To == n.Id)).Id];
        body.Wires.InsertRange(0, starts.Select(static to => new ExecWire(EntryId, ExecNode.Then, to)));
        if (outOf.Count > 0)
        {
            body.Nodes.Add(new ExecNode(ExitId, ExecNodeKind.MacroExit));
            body.Wires.AddRange(outOf.Select(static w => (w.From, w.Pin)).Distinct().Select(static w => new ExecWire(w.From, w.Pin, ExitId)));
        }

        string[] parameters = [.. body.Nodes.SelectMany(static n => (string[])[n.Condition, n.Call, n.TargetPin])
            .SelectMany(static text => Parameter().Matches(text).Select(static m => m.Groups[1].Value))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        ExecMacro macro = new(name, parameters, body);

        string? scope = layer.Nodes.First(n => inside.Contains(n.Id)).Scope;
        ExecNode use = new(useId, ExecNodeKind.Macro)
        {
            Macro = name,
            Scope = scope,
            Arguments = parameters.ToDictionary(static p => p, static p => "$" + p, StringComparer.Ordinal),
        };
        List<ExecNode> nodes = [.. layer.Nodes.Where(n => !inside.Contains(n.Id)), use];
        List<ExecWire> wires = [.. layer.Wires.Where(w => !inside.Contains(w.From) && !inside.Contains(w.To))];
        wires.AddRange(into.Select(static w => (w.From, w.Pin)).Distinct().Select(w => new ExecWire(w.From, w.Pin, useId)));
        wires.AddRange(outOf.Select(static w => w.To).Distinct().Select(to => new ExecWire(useId, ExecNode.Then, to)));
        return (macro, new ExecLayerState(nodes, wires));
    }

    /// <summary>
    /// Expands one macro node back into a copy of its macro's nodes, its parameters filled in, wired where the node was.
    /// </summary>
    /// <param name="layer">The layer, left as it is.</param>
    /// <param name="useId">The macro node.</param>
    /// <param name="macro">Its macro.</param>
    /// <returns>The layer's state with the nodes in the macro node's place, and the new nodes' ids; null when it is not a use of the macro.</returns>
    /// <remarks>The copies' ids are the use's id and their own (<c>use/wait</c>), so they never meet the layer's others.</remarks>
    public static (ExecLayerState Layer, IReadOnlyList<string> Added)? Expand(ExecLayer layer, string useId, ExecMacro macro)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(macro);
        if (layer.Nodes.Find(n => n.Id == useId) is not { Kind: ExecNodeKind.Macro } use || use.Macro != macro.Name)
        {
            return null;
        }

        string prefix = useId + "/";
        Dictionary<string, string> filled = macro.Parameters.ToDictionary(static p => p, p => use.Arguments.GetValueOrDefault(p, string.Empty), StringComparer.Ordinal);
        string Fill(string text) => filled.Aggregate(text, static (t, p) => t.Replace("$" + p.Key, p.Value, StringComparison.Ordinal));
        ExecNode[] copies = [.. macro.Body.Nodes.Where(static n => n.Kind is not (ExecNodeKind.MacroEntry or ExecNodeKind.MacroExit))
            .Select(n => n with { Id = prefix + n.Id, Scope = use.Scope, Condition = Fill(n.Condition), Call = Fill(n.Call), TargetPin = Fill(n.TargetPin) })];
        string[] entries = [.. macro.Body.Wires.Where(static w => w.From == EntryId).Select(w => prefix + w.To)];
        (string From, string Pin)[] exits = [.. macro.Body.Wires.Where(static w => w.To == ExitId).Select(w => (prefix + w.From, w.Pin))];

        List<ExecNode> nodes = [.. layer.Nodes.Where(n => n.Id != useId), .. copies];
        List<ExecWire> wires = [.. layer.Wires.Where(w => w.From != useId && w.To != useId)];
        wires.AddRange(macro.Body.Wires.Where(static w => w.From != EntryId && w.To != ExitId).Select(w => new ExecWire(prefix + w.From, w.Pin, prefix + w.To)));
        foreach (ExecWire into in layer.Wires.Where(w => w.To == useId))
        {
            wires.AddRange(entries.Select(to => new ExecWire(into.From, into.Pin, to)));
        }

        foreach (ExecWire after in layer.Wires.Where(w => w.From == useId))
        {
            wires.AddRange(exits.Select(e => new ExecWire(e.From, e.Pin, after.To)));
        }

        return (new ExecLayerState(nodes, wires), [.. copies.Select(static c => c.Id)]);
    }

    [GeneratedRegex(@"\$([A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex Parameter();
}
