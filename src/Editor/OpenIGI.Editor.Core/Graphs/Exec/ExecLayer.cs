namespace OpenIGI.Editor.Core.Graphs.Exec;

/// <summary>What an action node of a mission graph does.</summary>
/// <remarks>
/// The game has no events: every task re-reads its conditions each tick. These nodes give the editor Unreal-style "when this
/// happens, do that" wiring, and <see cref="ExecLayerCompiler"/> lowers each into the tasks retail missions use for the same
/// thing, so a mission built with them still runs in the original game.
/// </remarks>
public enum ExecNodeKind
{
    /// <summary>Starts a chain the first tick its condition holds; lowered to an <c>EditVariable</c> latch.</summary>
    OnBecomesTrue,

    /// <summary>Waits before going on; lowered to a <c>LevelTimer</c> that runs while the chain does.</summary>
    Delay,

    /// <summary>Goes one way or the other by a condition read once, when the chain reaches it; lowered to two latches.</summary>
    Branch,

    /// <summary>Calls a native for what it does; lowered into a generated container's run-at-start text.</summary>
    Call,

    /// <summary>Makes a task's condition pin hold from this point on: show a message, complete an objective, win.</summary>
    Drive,

    /// <summary>Runs a project macro in place: expanded into its nodes, with its parameters filled in, before lowering.</summary>
    Macro,

    /// <summary>Inside a macro, where the chain that runs it comes in.</summary>
    MacroEntry,

    /// <summary>Inside a macro, where the chain goes on to whatever follows the macro.</summary>
    MacroExit,
}

/// <summary>One action node.</summary>
/// <param name="Id">Its identity, stable across saves; the tasks it lowers to are kept by it.</param>
/// <param name="Kind">What it does.</param>
public sealed record ExecNode(string Id, ExecNodeKind Kind)
{
    /// <summary>The exec output of every node but a branch.</summary>
    public const string Then = "then";

    /// <summary>A branch's output taken when its condition holds.</summary>
    public const string True = "true";

    /// <summary>A branch's output taken when it does not.</summary>
    public const string False = "false";

    /// <summary>Gets the condition, as level-expression text, for an event or a branch.</summary>
    public string Condition { get; init; } = string.Empty;

    /// <summary>Gets how long a delay waits, in seconds.</summary>
    public double Seconds { get; init; }

    /// <summary>Gets the call, as level-expression text (<c>Game_DisableMusic()</c>), for a call node.</summary>
    public string Call { get; init; } = string.Empty;

    /// <summary>Gets the task a drive node drives, by its graph key.</summary>
    public GraphNodeKey? Target { get; init; }

    /// <summary>Gets the condition pin a drive node drives (<c>Send</c>, <c>Complete</c>).</summary>
    public string TargetPin { get; init; } = string.Empty;

    /// <summary>Gets the macro a macro node runs, by name.</summary>
    public string Macro { get; init; } = string.Empty;

    /// <summary>Gets the values a macro node gives its macro's parameters, by name.</summary>
    public IReadOnlyDictionary<string, string> Arguments { get; init; } = NoArguments;

    /// <summary>Gets the arguments of a node that gives none, one shared instance so equal nodes compare equal.</summary>
    public static IReadOnlyDictionary<string, string> NoArguments { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>An exec wire: from one node's output to the node that runs next. Several wires out of one output run in order.</summary>
/// <param name="From">The node it leaves.</param>
/// <param name="Pin">The output it leaves by.</param>
/// <param name="To">The node it runs.</param>
public sealed record ExecWire(string From, string Pin, string To);

/// <summary>The action nodes of a mission graph: kept in its sidecar, compiled into the mission as tasks.</summary>
public sealed class ExecLayer
{
    /// <summary>Gets the nodes.</summary>
    public List<ExecNode> Nodes { get; } = [];

    /// <summary>Gets the exec wires, in the order they run.</summary>
    public List<ExecWire> Wires { get; } = [];

    /// <summary>Gets the task ids each node was last lowered to, so lowering again reuses them and references stay valid.</summary>
    public Dictionary<string, List<int>> Generated { get; } = new(StringComparer.Ordinal);

    /// <summary>The nodes a node's output runs, in order.</summary>
    /// <param name="node">The node.</param>
    /// <param name="pin">The output.</param>
    /// <returns>The nodes.</returns>
    public IEnumerable<ExecNode> Next(ExecNode node, string pin)
    {
        ArgumentNullException.ThrowIfNull(node);
        foreach (ExecWire wire in Wires.Where(w => w.From == node.Id && w.Pin == pin))
        {
            if (Nodes.Find(n => n.Id == wire.To) is { } next)
            {
                yield return next;
            }
        }
    }
}
