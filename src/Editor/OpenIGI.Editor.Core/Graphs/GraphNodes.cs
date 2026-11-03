using OpenIGI.Editor.Core.Missions;
using OpenIGI.Scripting.Expressions;
using OpenIGI.Scripting.Vocabulary;

namespace OpenIGI.Editor.Core.Graphs;

/// <summary>A node's identity in a graph, stable across rebuilds so a saved layout finds its node again.</summary>
/// <param name="Value">The key: <c>t12</c> for task 12, <c>p0.3</c> for an id-less task by its place in the tree, and
/// <c>t12/Condition/r.0</c> for an expression node by its place in a parameter's expression.</param>
public readonly record struct GraphNodeKey(string Value)
{
    /// <inheritdoc/>
    public override string ToString() => Value;
}

/// <summary>One end of a link: a node and one of its pins.</summary>
/// <param name="Node">The node.</param>
/// <param name="Pin">The pin's name: a parameter or field name on a task node, an operand index or <see cref="GraphNode.Output"/> on an expression node.</param>
public readonly record struct GraphPinRef(GraphNodeKey Node, string Pin);

/// <summary>A wire from an output pin to an input pin.</summary>
/// <param name="From">The output.</param>
/// <param name="To">The input.</param>
public sealed record GraphLink(GraphPinRef From, GraphPinRef To);

/// <summary>A node of a mission graph.</summary>
/// <param name="key">Its identity.</param>
public abstract class GraphNode(GraphNodeKey key)
{
    /// <summary>The name of an expression node's one output pin.</summary>
    public const string Output = "out";

    /// <summary>Gets its identity.</summary>
    public GraphNodeKey Key { get; } = key;

    /// <summary>Gets or sets the frame it sits in: the key of the container task that holds it, or <see langword="null"/>.</summary>
    public GraphNodeKey? Frame { get; set; }

    /// <summary>Gets the title drawn on the node.</summary>
    public abstract string Title { get; }
}

/// <summary>A task of the mission: its parameters are its inputs, its exported fields its outputs.</summary>
/// <param name="key">Its identity.</param>
/// <param name="task">The task.</param>
/// <param name="definition">Its type's node definition.</param>
public sealed class TaskGraphNode(GraphNodeKey key, MissionTask task, TaskNodeDefinition definition) : GraphNode(key)
{
    /// <summary>Gets the task.</summary>
    public MissionTask Task { get; } = task;

    /// <summary>Gets its type's node definition.</summary>
    public TaskNodeDefinition Definition { get; } = definition;

    /// <summary>Gets or sets a value indicating whether the graph shows it by default: logic, a frame, or linked to something.</summary>
    public bool IsShown { get; set; }

    /// <inheritdoc/>
    public override string Title => Task.Label.Length > 0 ? $"{Task.TypeName} '{Task.Label}'" : Task.ToString();
}

/// <summary>What an expression node computes.</summary>
public enum ExpressionNodeKind
{
    /// <summary>A number typed in place.</summary>
    Number,

    /// <summary>Text typed in place.</summary>
    Text,

    /// <summary>A named constant, such as <c>GAME_FREQUENCY</c> or <c>AIEVENT_IDLE</c>.</summary>
    Constant,

    /// <summary>A global variable, such as <c>NetMission_eCurrentObjective</c>.</summary>
    Global,

    /// <summary>A task field the mission has no task for: it reads nothing.</summary>
    UnresolvedField,

    /// <summary>A name the vocabulary does not know.</summary>
    UnknownName,

    /// <summary>An operator: logic, comparison, arithmetic or assignment.</summary>
    Operator,

    /// <summary><c>condition ? a : b</c>.</summary>
    Conditional,

    /// <summary>A call to a native.</summary>
    Call,

    /// <summary>Text the game's parser rejects, which the game reads as empty: kept verbatim.</summary>
    Raw,
}

/// <summary>A node of a level expression: its operands are its inputs, its value its one output.</summary>
/// <param name="key">Its identity.</param>
/// <param name="kind">What it computes.</param>
/// <param name="text">Its name, operator symbol, literal or raw text.</param>
/// <param name="output">The type of its value.</param>
/// <param name="inputs">Its operand pins, in order.</param>
public sealed class ExpressionGraphNode(
    GraphNodeKey key,
    ExpressionNodeKind kind,
    string text,
    ScriptType output,
    IReadOnlyList<NativeParameter> inputs) : GraphNode(key)
{
    /// <summary>Gets what it computes.</summary>
    public ExpressionNodeKind Kind { get; } = kind;

    /// <summary>Gets its name, operator symbol, literal or raw text.</summary>
    public string Text { get; } = text;

    /// <summary>Gets the type of its value.</summary>
    public ScriptType OutputType { get; } = output;

    /// <summary>Gets its operand pins, named <c>0</c>, <c>1</c>... for operators and by parameter for calls.</summary>
    public IReadOnlyList<NativeParameter> Inputs { get; } = inputs;

    /// <summary>Gets the operator, for an <see cref="ExpressionNodeKind.Operator"/> node.</summary>
    public ScriptOperator Operator { get; init; }

    /// <summary>Gets the native's signature, for a <see cref="ExpressionNodeKind.Call"/> node the vocabulary knows.</summary>
    public NativeSignature? Native { get; init; }

    /// <summary>Gets the number, for a <see cref="ExpressionNodeKind.Number"/> node.</summary>
    public double Number { get; init; }

    /// <summary>Gets the task and parameter whose text this node is part of.</summary>
    public (GraphNodeKey Task, string Parameter) Owner { get; init; }

    /// <inheritdoc/>
    public override string Title => Kind switch
    {
        ExpressionNodeKind.Call => Text + "()",
        ExpressionNodeKind.Raw => "Unreadable expression",
        _ => Text,
    };
}
