namespace OpenIGI.Editor.Core.Graphs.Exec;

/// <summary>A project macro: a chain of action nodes, with one way in and one way out, reused by name.</summary>
/// <param name="Name">Its name, which macro nodes run it by.</param>
/// <param name="Parameters">Its parameters' names; a node's text says <c>$Name</c> where one is filled in.</param>
/// <param name="Body">Its nodes and wires, with one <see cref="ExecNodeKind.MacroEntry"/> and at most one <see cref="ExecNodeKind.MacroExit"/>.</param>
/// <remarks>
/// The game has no functions, so a macro is expanded where it is used: every use lowers into tasks of its own. A retail-target
/// mission can therefore use macros freely; the original game only ever sees the tasks.
/// </remarks>
public sealed record ExecMacro(string Name, IReadOnlyList<string> Parameters, ExecLayer Body);

/// <summary>Expands the macro nodes of a layer into the nodes they stand for, before lowering.</summary>
/// <remarks>
/// A use's nodes are copied with ids prefixed by the use's id (<c>alarm1/wait</c>), so lowering the same use again keeps the
/// same generated tasks. Wires into the use go to what its entry runs; what its exit runs is what follows the use. A macro may
/// use another; one that uses itself, however deep, is refused.
/// </remarks>
public static class ExecMacroExpander
{
    /// <summary>Expands every macro node of a layer.</summary>
    /// <param name="layer">The layer.</param>
    /// <param name="macros">The project's macros, by name.</param>
    /// <returns>A layer with no macro nodes; the same layer when it had none. Its generated ids are the layer's own.</returns>
    /// <exception cref="ExecLayerException">A macro is missing, malformed, or uses itself.</exception>
    public static ExecLayer Expand(ExecLayer layer, IReadOnlyDictionary<string, ExecMacro> macros)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(macros);
        if (layer.Nodes.TrueForAll(static n => n.Kind != ExecNodeKind.Macro))
        {
            return layer;
        }

        ExecLayer flat = new();
        foreach ((string key, List<int> ids) in layer.Generated)
        {
            flat.Generated[key] = ids;
        }

        Inline(layer, macros, flat, prefix: string.Empty, arguments: ExecNode.NoArguments, active: []);
        return flat;
    }

    // Copies a layer's nodes and wires into "flat", prefixing ids and filling parameters, expanding macro nodes as it goes.
    // A use is wired as its macro: what runs into it reaches its entry's successors, and its exit's inputs run what follows it.
    private static void Inline(
        ExecLayer source,
        IReadOnlyDictionary<string, ExecMacro> macros,
        ExecLayer flat,
        string prefix,
        IReadOnlyDictionary<string, string> arguments,
        HashSet<string> active)
    {
        Dictionary<string, (List<string> Entries, List<(string From, string Pin)> Exits)> uses = new(StringComparer.Ordinal);
        foreach (ExecNode node in source.Nodes)
        {
            string id = prefix + node.Id;
            if (node.Kind != ExecNodeKind.Macro)
            {
                if (node.Kind is not (ExecNodeKind.MacroEntry or ExecNodeKind.MacroExit))
                {
                    flat.Nodes.Add(Fill(node, id, arguments));
                }

                continue;
            }

            if (!macros.TryGetValue(node.Macro, out ExecMacro? macro))
            {
                throw new ExecLayerException($"The macro '{node.Macro}' is not in this project.");
            }

            if (!active.Add(macro.Name))
            {
                throw new ExecLayerException($"The macro '{macro.Name}' uses itself.");
            }

            Dictionary<string, string> filled = macro.Parameters.ToDictionary(
                static p => p,
                p => node.Arguments.TryGetValue(p, out string? value) ? Fill(value, arguments) : string.Empty,
                StringComparer.Ordinal);
            string inner = id + "/";
            Inline(macro.Body, macros, flat, inner, filled, active);
            active.Remove(macro.Name);

            ExecNode entry = macro.Body.Nodes.SingleOrDefault(static n => n.Kind == ExecNodeKind.MacroEntry)
                ?? throw new ExecLayerException($"The macro '{macro.Name}' has no entry.");
            ExecNode? exit = macro.Body.Nodes.SingleOrDefault(static n => n.Kind == ExecNodeKind.MacroExit);
            uses[node.Id] = (
                [.. macro.Body.Wires.Where(w => w.From == entry.Id).Select(w => inner + w.To)],
                exit is null ? [] : [.. macro.Body.Wires.Where(w => w.To == exit.Id).Select(w => (inner + w.From, w.Pin))]);
        }

        foreach (ExecWire wire in source.Wires)
        {
            if (source.Nodes.Find(n => n.Id == wire.From) is { Kind: ExecNodeKind.MacroEntry or ExecNodeKind.MacroExit }
                || source.Nodes.Find(n => n.Id == wire.To) is { Kind: ExecNodeKind.MacroEntry or ExecNodeKind.MacroExit })
            {
                continue; // The use's own wiring stands for these.
            }

            IEnumerable<(string From, string Pin)> froms = uses.TryGetValue(wire.From, out var fromUse)
                ? fromUse.Exits
                : [(prefix + wire.From, wire.Pin)];
            IEnumerable<string> tos = uses.TryGetValue(wire.To, out var toUse) ? toUse.Entries : [prefix + wire.To];
            foreach ((string from, string pin) in froms)
            {
                foreach (string to in tos)
                {
                    flat.Wires.Add(new ExecWire(from, pin, to));
                }
            }
        }
    }

    private static ExecNode Fill(ExecNode node, string id, IReadOnlyDictionary<string, string> arguments) => node with
    {
        Id = id,
        Condition = Fill(node.Condition, arguments),
        Call = Fill(node.Call, arguments),
        TargetPin = Fill(node.TargetPin, arguments),
        Target = node.Target is { } target ? new GraphNodeKey(Fill(target.Value, arguments)) : null,
    };

    private static string Fill(string text, IReadOnlyDictionary<string, string> arguments)
    {
        foreach ((string name, string value) in arguments.OrderByDescending(static a => a.Key.Length))
        {
            text = text.Replace("$" + name, value, StringComparison.Ordinal);
        }

        return text;
    }
}
