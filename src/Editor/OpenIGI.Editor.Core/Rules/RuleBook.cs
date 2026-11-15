using System.Globalization;
using OpenIGI.Editor.Core.Graphs.Exec;

namespace OpenIGI.Editor.Core.Rules;

/// <summary>A mission's rules, read from and written to its action layer: each event's chain is one rule.</summary>
/// <remarks>
/// <para>
/// The layer is the one place a mission's authored logic is kept, so a rule made in the list shows in the graph as its
/// chain of nodes, and a chain wired in the graph shows in the list as a rule. A step is a node and keeps the node's id, so
/// the tasks a step was compiled to stay its own through every edit.
/// </para>
/// <para>
/// A rule's steps hang off whatever the rule last waited for: the steps before its first wait off the event, each wait off
/// the one before it, the steps after a wait off that wait. That is the one shape written here, and the shape read back; a
/// chain wired otherwise is listed as <see cref="Rule.IsFreeform"/>.
/// </para>
/// </remarks>
public static class RuleBook
{
    /// <summary>Reads a layer's rules, in the order their events stand in it.</summary>
    /// <param name="layer">The layer.</param>
    /// <returns>The rules.</returns>
    public static IReadOnlyList<Rule> Read(ExecLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        Dictionary<string, int> runBy = [];
        foreach (ExecWire wire in layer.Wires)
        {
            runBy[wire.To] = runBy.GetValueOrDefault(wire.To) + 1;
        }

        List<Rule> rules = [];
        foreach (ExecNode root in layer.Nodes.Where(static n => n.Kind == ExecNodeKind.OnBecomesTrue))
        {
            List<RuleStep>? steps = [];
            if (!TryReadSteps(layer, root, ExecNode.Then, runBy, [root.Id], steps))
            {
                steps = null;
            }

            rules.Add(new Rule(root.Id, root.Condition, steps ?? [])
            {
                Name = root.Name,
                Group = root.Group,
                IsEnabled = !root.IsDisabled,
                Conditions = root.Guards,
                IsFreeform = steps is null,
            });
        }

        return rules;
    }

    /// <summary>The layer with a rule added at the end, or put in the place of the rule of the same id.</summary>
    /// <param name="layer">The layer, left as it is.</param>
    /// <param name="rule">The rule.</param>
    /// <returns>The layer's state with the rule in it.</returns>
    /// <exception cref="ArgumentException">The rule is freeform, or two of its steps share an id, or one has the id of a node outside the rule.</exception>
    public static ExecLayerState With(ExecLayer layer, Rule rule)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(rule);
        if (rule.IsFreeform)
        {
            throw new ArgumentException("A rule wired in the graph is edited there.", nameof(rule));
        }

        HashSet<string> old = Reach(layer, rule.Id);
        ExecNode? before = layer.Nodes.Find(n => n.Id == rule.Id);
        List<ExecNode> nodes = [];
        List<ExecWire> wires = [];
        nodes.Add(new ExecNode(rule.Id, ExecNodeKind.OnBecomesTrue)
        {
            Condition = rule.When,
            Guards = rule.Conditions.Count == 0 ? ExecNode.NoGuards : [.. rule.Conditions],
            Name = rule.Name,
            Group = rule.Group,
            IsDisabled = !rule.IsEnabled,
            Scope = before?.Scope,
        });
        Emit(rule.Id, ExecNode.Then, rule.Steps, before?.Scope, nodes, wires);

        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (ExecNode node in nodes)
        {
            if (!ids.Add(node.Id) || (!old.Contains(node.Id) && layer.Nodes.Exists(n => n.Id == node.Id)))
            {
                throw new ArgumentException($"The step id '{node.Id}' is used twice.", nameof(rule));
            }
        }

        // In the old rule's place, so the rules keep their order; a new rule goes last.
        int at = before is null ? layer.Nodes.Count : layer.Nodes.IndexOf(before);
        List<ExecNode> allNodes = [.. layer.Nodes.Take(at).Where(n => !old.Contains(n.Id)), .. nodes, .. layer.Nodes.Skip(at).Where(n => !old.Contains(n.Id))];
        List<ExecWire> allWires = [.. layer.Wires.Where(w => !old.Contains(w.From) && !old.Contains(w.To)), .. wires];
        return new ExecLayerState(allNodes, allWires);
    }

    /// <summary>The layer without a rule: its event and everything only it runs.</summary>
    /// <param name="layer">The layer, left as it is.</param>
    /// <param name="ruleId">The rule.</param>
    /// <returns>The layer's state without it.</returns>
    public static ExecLayerState Without(ExecLayer layer, string ruleId)
    {
        ArgumentNullException.ThrowIfNull(layer);
        HashSet<string> gone = Reach(layer, ruleId);
        return new ExecLayerState(
            [.. layer.Nodes.Where(n => !gone.Contains(n.Id))],
            [.. layer.Wires.Where(w => !gone.Contains(w.From) && !gone.Contains(w.To))]);
    }

    /// <summary>The layer with a rule moved to another place among the rules.</summary>
    /// <param name="layer">The layer, left as it is.</param>
    /// <param name="ruleId">The rule.</param>
    /// <param name="index">Its new place among the rules, from zero.</param>
    /// <returns>The layer's state with the rules in the new order.</returns>
    /// <remarks>
    /// Order is not only how the list reads: rules are compiled in it, and of two rules that start on the same tick the
    /// earlier one's steps run first.
    /// </remarks>
    public static ExecLayerState Moved(ExecLayer layer, string ruleId, int index)
    {
        ArgumentNullException.ThrowIfNull(layer);
        List<ExecNode> roots = [.. layer.Nodes.Where(static n => n.Kind == ExecNodeKind.OnBecomesTrue)];
        if (roots.Find(n => n.Id == ruleId) is not { } moving)
        {
            return ExecLayerState.Of(layer);
        }

        roots.Remove(moving);
        roots.Insert(Math.Clamp(index, 0, roots.Count), moving);

        // Each rule's nodes go with its event, in the order they stood; whatever no rule runs comes after them all.
        List<ExecNode> nodes = [];
        HashSet<string> placed = new(StringComparer.Ordinal);
        foreach (ExecNode root in roots)
        {
            HashSet<string> own = Reach(layer, root.Id);
            nodes.AddRange(layer.Nodes.Where(n => (n.Id == root.Id || (own.Contains(n.Id) && n.Kind != ExecNodeKind.OnBecomesTrue)) && placed.Add(n.Id)));
        }

        nodes.AddRange(layer.Nodes.Where(n => !placed.Contains(n.Id)));
        return new ExecLayerState(nodes, [.. layer.Wires]);
    }

    /// <summary>An id no node of the layer has, for a new rule or step.</summary>
    /// <param name="layer">The layer.</param>
    /// <param name="taken">Ids already given out for steps not yet in the layer.</param>
    /// <returns>The id.</returns>
    public static string NewId(ExecLayer layer, IReadOnlyCollection<string>? taken = null)
    {
        ArgumentNullException.ThrowIfNull(layer);
        for (int number = layer.Nodes.Count + 1; ; number++)
        {
            string id = string.Create(CultureInfo.InvariantCulture, $"r{number}");
            if (!layer.Nodes.Exists(n => n.Id == id) && taken?.Contains(id) != true)
            {
                return id;
            }
        }
    }

    // The steps an output runs, with what its one wait runs after them; false when the chain is not a list of steps.
    private static bool TryReadSteps(ExecLayer layer, ExecNode owner, string pin, Dictionary<string, int> runBy, HashSet<string> seen, List<RuleStep> steps)
    {
        ExecNode? wait = null;
        foreach (ExecNode next in layer.Next(owner, pin))
        {
            if (runBy.GetValueOrDefault(next.Id) != 1 || !seen.Add(next.Id))
            {
                return false;
            }

            switch (next.Kind)
            {
                case ExecNodeKind.Call:
                    steps.Add(new CallStep(next.Id, next.Call));
                    break;

                case ExecNodeKind.Drive when next.Target is { } target:
                    steps.Add(new DriveStep(next.Id, target, next.TargetPin));
                    break;

                case ExecNodeKind.Macro:
                    steps.Add(new MacroStep(next.Id, next.Macro, next.Arguments));
                    break;

                case ExecNodeKind.Branch:
                    List<RuleStep> then = [];
                    List<RuleStep> otherwise = [];
                    if (!TryReadSteps(layer, next, ExecNode.True, runBy, seen, then) || !TryReadSteps(layer, next, ExecNode.False, runBy, seen, otherwise))
                    {
                        return false;
                    }

                    steps.Add(new IfStep(next.Id, next.Condition, then, otherwise));
                    continue;

                case ExecNodeKind.Delay when wait is null:
                    wait = next;
                    continue;

                default:
                    return false;
            }

            // A call or a drive that something is wired after: a chain made in the graph, which runs in another order.
            if (layer.Next(next, ExecNode.Then).Any())
            {
                return false;
            }
        }

        if (wait is null)
        {
            return true;
        }

        steps.Add(new WaitStep(wait.Id, wait.Seconds));
        return TryReadSteps(layer, wait, ExecNode.Then, runBy, seen, steps);
    }

    private static void Emit(string owner, string pin, IReadOnlyList<RuleStep> steps, string? scope, List<ExecNode> nodes, List<ExecWire> wires)
    {
        foreach (RuleStep step in steps)
        {
            wires.Add(new ExecWire(owner, pin, step.Id));
            switch (step)
            {
                case CallStep call:
                    nodes.Add(new ExecNode(call.Id, ExecNodeKind.Call) { Call = call.Call, Scope = scope });
                    break;

                case DriveStep drive:
                    nodes.Add(new ExecNode(drive.Id, ExecNodeKind.Drive) { Target = drive.Target, TargetPin = drive.Pin, Scope = scope });
                    break;

                case MacroStep macro:
                    nodes.Add(new ExecNode(macro.Id, ExecNodeKind.Macro) { Macro = macro.Macro, Arguments = macro.Arguments, Scope = scope });
                    break;

                case WaitStep wait:
                    nodes.Add(new ExecNode(wait.Id, ExecNodeKind.Delay) { Seconds = wait.Seconds, Scope = scope });
                    owner = wait.Id;
                    pin = ExecNode.Then;
                    break;

                case IfStep block:
                    nodes.Add(new ExecNode(block.Id, ExecNodeKind.Branch) { Condition = block.Condition, Scope = scope });
                    Emit(block.Id, ExecNode.True, block.Then, scope, nodes, wires);
                    Emit(block.Id, ExecNode.False, block.Otherwise, scope, nodes, wires);
                    break;

                default:
                    throw new ArgumentException($"A {step.GetType().Name} cannot be written into a layer.", nameof(steps));
            }
        }
    }

    // A rule's event and every node its chain reaches.
    private static HashSet<string> Reach(ExecLayer layer, string ruleId)
    {
        HashSet<string> reached = new(StringComparer.Ordinal);
        if (!layer.Nodes.Exists(n => n.Id == ruleId))
        {
            return reached;
        }

        Stack<string> pending = new([ruleId]);
        while (pending.TryPop(out string? id))
        {
            if (reached.Add(id))
            {
                foreach (ExecWire wire in layer.Wires.Where(w => w.From == id))
                {
                    pending.Push(wire.To);
                }
            }
        }

        return reached;
    }
}
