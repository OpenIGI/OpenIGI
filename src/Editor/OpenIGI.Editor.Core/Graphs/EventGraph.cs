using System.Collections.ObjectModel;
using System.Globalization;
using OpenIGI.Scripting.Qsc;
using OpenIGI.Scripting.Vocabulary;

namespace OpenIGI.Editor.Core.Graphs;

/// <summary>What an exec node of an event graph does.</summary>
public enum EventStatementKind
{
    /// <summary>Where the script starts: one exec output.</summary>
    Start,

    /// <summary>A native called for what it does (<c>AIAction_Patrol(5, 0, 1);</c>).</summary>
    Call,

    /// <summary>Any other expression evaluated for its effect, such as an assignment.</summary>
    Expression,

    /// <summary><c>if</c>, with or without <c>else</c>.</summary>
    Branch,

    /// <summary><c>while</c>.</summary>
    Loop,
}

/// <summary>An exec node: one statement of the script.</summary>
/// <param name="key">Its identity.</param>
/// <param name="kind">What it does.</param>
/// <param name="text">The native's name for a call; empty otherwise.</param>
public sealed class EventStatementNode(GraphNodeKey key, EventStatementKind kind, string text) : GraphNode(key)
{
    /// <summary>The exec input every statement but the start has.</summary>
    public const string In = "in";

    /// <summary>The exec output to the statement that follows.</summary>
    public const string Next = "next";

    /// <summary>A branch's exec output taken when its condition holds; a loop's body.</summary>
    public const string True = "true";

    /// <summary>A branch's exec output taken when it does not.</summary>
    public const string False = "false";

    /// <summary>A branch's or loop's condition input.</summary>
    public const string Condition = "condition";

    /// <summary>Gets what it does.</summary>
    public EventStatementKind Kind { get; } = kind;

    /// <summary>Gets the native's name, for a call.</summary>
    public string Text { get; } = text;

    /// <summary>Gets the native's signature, for a call the vocabulary knows.</summary>
    public NativeSignature? Native { get; init; }

    /// <summary>Gets how many arguments a call passes.</summary>
    public int ArgumentCount { get; init; }

    /// <summary>Gets a value indicating whether a branch has an <c>else</c>, even an empty one, which still compiles to a jump.</summary>
    public bool HasElse { get; init; }

    /// <summary>Gets the event a branch tests for (<c>AIEVENT_IDLE</c>), when its condition is <c>GetCurrentEventType() == X</c>.</summary>
    /// <remarks>The editor draws such a branch as an event node; it is still the branch, so it writes back unchanged.</remarks>
    public string? Event { get; init; }

    /// <inheritdoc/>
    public override string Title => Kind switch
    {
        EventStatementKind.Start => "Start",
        EventStatementKind.Call => Text,
        EventStatementKind.Branch when Event is { } name => "On " + name,
        EventStatementKind.Branch => "Branch",
        EventStatementKind.Loop => "While",
        _ => "Do",
    };
}

/// <summary>What a value node of an event graph is.</summary>
public enum EventValueKind
{
    /// <summary>An integer literal.</summary>
    Integer,

    /// <summary>A real literal.</summary>
    Real,

    /// <summary>A string literal.</summary>
    Text,

    /// <summary>A name: a constant, variable or field.</summary>
    Symbol,

    /// <summary>A native called for its value.</summary>
    Call,

    /// <summary>A unary operator.</summary>
    Unary,

    /// <summary>A binary operator.</summary>
    Binary,

    /// <summary>An assignment, whose value is the value assigned.</summary>
    Assignment,
}

/// <summary>A value node: one expression of the script, its operands its inputs.</summary>
/// <param name="key">Its identity.</param>
/// <param name="kind">What it is.</param>
/// <param name="inputs">How many operands it takes.</param>
public sealed class EventValueNode(GraphNodeKey key, EventValueKind kind, int inputs) : GraphNode(key)
{
    /// <summary>Gets what it is.</summary>
    public EventValueKind Kind { get; } = kind;

    /// <summary>Gets how many operands it takes.</summary>
    public int InputCount { get; } = inputs;

    /// <summary>Gets the name or text, for a symbol, call or string.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Gets the value, for an integer.</summary>
    public int Integer { get; init; }

    /// <summary>Gets the value, for a real.</summary>
    public float Real { get; init; }

    /// <summary>Gets the operator, for a unary node.</summary>
    public QscUnaryOperator Unary { get; init; }

    /// <summary>Gets the operator, for a binary node.</summary>
    public QscBinaryOperator Binary { get; init; }

    /// <summary>Gets the native's signature, for a call the vocabulary knows.</summary>
    public NativeSignature? Native { get; init; }

    /// <inheritdoc/>
    public override string Title => Kind switch
    {
        EventValueKind.Integer => Integer.ToString(CultureInfo.InvariantCulture),
        EventValueKind.Real => Real.ToString("R", CultureInfo.InvariantCulture),
        EventValueKind.Text => $"\"{Text}\"",
        EventValueKind.Call => Text + "()",
        EventValueKind.Unary => Unary.ToString(),
        EventValueKind.Binary => Binary.ToString(),
        EventValueKind.Assignment => "Set",
        _ => Text,
    };
}

/// <summary>
/// An AI event script as a graph: statements are exec nodes wired in order, their operands value nodes. Built from the
/// script's syntax tree and written back to the same tree.
/// </summary>
/// <remarks>
/// Every soldier and squad script of both games is an <c>if</c>/<c>else</c> chain on
/// <c>AIFunction_GetCurrentEventType()</c>; the graph keeps that chain as branches (marking each with the event it tests),
/// keeps an empty <c>else</c> apart from none, and keeps the script's header, so a script read and written back compiles to
/// the image it came from.
/// </remarks>
public sealed class EventGraph
{
    /// <summary>The native whose result the event branches test.</summary>
    public const string EventTypeNative = "AIFunction_GetCurrentEventType";

    private static readonly ReadOnlyCollection<QscStatement> NoStatements = new([]);

    private readonly Dictionary<GraphNodeKey, GraphNode> _nodes = [];
    private readonly List<GraphLink> _links = [];
    private readonly ScriptVocabulary? _vocabulary;

    private EventGraph(QscScript header, ScriptVocabulary? vocabulary)
    {
        Header = header;
        _vocabulary = vocabulary;
    }

    /// <summary>The start node's key.</summary>
    public static GraphNodeKey StartKey { get; } = new("start");

    /// <summary>Gets the script the graph was read from, for its header: version, stack, locals and file info.</summary>
    public QscScript Header { get; }

    /// <summary>Gets every node, by key.</summary>
    public IReadOnlyDictionary<GraphNodeKey, GraphNode> Nodes => _nodes;

    /// <summary>Gets every wire: exec wires from an exec output to <see cref="EventStatementNode.In"/>, value wires into operands.</summary>
    public IReadOnlyList<GraphLink> Links => _links;

    /// <summary>Reads a script into a graph.</summary>
    /// <param name="script">The script's syntax tree.</param>
    /// <param name="vocabulary">The title's vocabulary, for natives' signatures; optional.</param>
    /// <returns>The graph.</returns>
    public static EventGraph Read(QscScript script, ScriptVocabulary? vocabulary = null)
    {
        ArgumentNullException.ThrowIfNull(script);
        EventGraph graph = new(script, vocabulary);
        graph._nodes[StartKey] = new EventStatementNode(StartKey, EventStatementKind.Start, string.Empty);
        graph.ReadList(script.Statements, new GraphPinRef(StartKey, EventStatementNode.Next), "s", top: true);
        return graph;
    }

    /// <summary>Adds a node; the graph editor's commands are built on this and its siblings.</summary>
    /// <param name="node">The node.</param>
    public void AddNode(GraphNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        _nodes.Add(node.Key, node);
    }

    /// <summary>Removes a node and every wire touching it.</summary>
    /// <param name="key">The node's key.</param>
    /// <returns>The wires that were removed with it, for undo.</returns>
    public IReadOnlyList<GraphLink> RemoveNode(GraphNodeKey key)
    {
        GraphLink[] touching = [.. _links.Where(l => l.From.Node == key || l.To.Node == key)];
        _links.RemoveAll(l => l.From.Node == key || l.To.Node == key);
        _nodes.Remove(key);
        return touching;
    }

    /// <summary>Adds a wire.</summary>
    /// <param name="link">The wire.</param>
    public void AddLink(GraphLink link)
    {
        ArgumentNullException.ThrowIfNull(link);
        _links.Add(link);
    }

    /// <summary>Removes a wire.</summary>
    /// <param name="link">The wire.</param>
    /// <returns><see langword="true"/> when it was there.</returns>
    public bool RemoveLink(GraphLink link) => _links.Remove(link);

    private void ReadList(ReadOnlyCollection<QscStatement> statements, GraphPinRef from, string path, bool top)
    {
        GraphPinRef exec = from;
        for (int i = 0; i < statements.Count; i++)
        {
            GraphNodeKey key = new(string.Create(CultureInfo.InvariantCulture, $"{path}{i}"));
            ReadStatement(statements[i], key, top);
            _links.Add(new GraphLink(exec, new GraphPinRef(key, EventStatementNode.In)));
            exec = new GraphPinRef(key, EventStatementNode.Next);
        }
    }

    private EventStatementNode ReadStatement(QscStatement statement, GraphNodeKey key, bool top)
    {
        switch (statement)
        {
            case QscExpressionStatement { Expression: QscCall call }:
            {
                EventStatementNode node = Add(new EventStatementNode(key, EventStatementKind.Call, call.Name)
                {
                    Native = Native(call.Name),
                    ArgumentCount = call.Arguments.Count,
                });
                for (int i = 0; i < call.Arguments.Count; i++)
                {
                    Wire(ReadValue(call.Arguments[i], $"{key.Value}/{i}"), key, i.ToString(CultureInfo.InvariantCulture));
                }

                return node;
            }

            case QscExpressionStatement expression:
            {
                EventStatementNode node = Add(new EventStatementNode(key, EventStatementKind.Expression, string.Empty));
                Wire(ReadValue(expression.Expression, $"{key.Value}/0"), key, "0");
                return node;
            }

            case QscIf branch:
            {
                EventStatementNode node = Add(new EventStatementNode(key, EventStatementKind.Branch, string.Empty)
                {
                    HasElse = branch.Else is not null,
                    Event = top ? EventOf(branch.Condition) : null,
                });
                Wire(ReadValue(branch.Condition, $"{key.Value}/c"), key, EventStatementNode.Condition);
                ReadList(branch.Then, new GraphPinRef(key, EventStatementNode.True), key.Value + ".t", top: false);

                // The else of an event branch is the rest of the dispatch chain, so it is still the top of the script.
                ReadList(branch.Else ?? NoStatements, new GraphPinRef(key, EventStatementNode.False), key.Value + ".e", top && node.Event is not null);
                return node;
            }

            case QscWhile loop:
            {
                EventStatementNode node = Add(new EventStatementNode(key, EventStatementKind.Loop, string.Empty));
                Wire(ReadValue(loop.Condition, $"{key.Value}/c"), key, EventStatementNode.Condition);
                ReadList(loop.Body, new GraphPinRef(key, EventStatementNode.True), key.Value + ".b", top: false);
                return node;
            }

            default:
                throw new ArgumentException($"Unknown statement {statement.GetType().Name}.", nameof(statement));
        }
    }

    private GraphNodeKey ReadValue(QscExpression expression, string path)
    {
        GraphNodeKey key = new(path);
        switch (expression)
        {
            case QscIntLiteral literal:
                Add(new EventValueNode(key, EventValueKind.Integer, 0) { Integer = literal.Value });
                break;

            case QscRealLiteral literal:
                Add(new EventValueNode(key, EventValueKind.Real, 0) { Real = literal.Value });
                break;

            case QscStringLiteral literal:
                Add(new EventValueNode(key, EventValueKind.Text, 0) { Text = literal.Value });
                break;

            case QscSymbol symbol:
                Add(new EventValueNode(key, EventValueKind.Symbol, 0) { Text = symbol.Name });
                break;

            case QscCall call:
                Add(new EventValueNode(key, EventValueKind.Call, call.Arguments.Count) { Text = call.Name, Native = Native(call.Name) });
                for (int i = 0; i < call.Arguments.Count; i++)
                {
                    Wire(ReadValue(call.Arguments[i], $"{path}.{i}"), key, i.ToString(CultureInfo.InvariantCulture));
                }

                break;

            case QscUnary unary:
                Add(new EventValueNode(key, EventValueKind.Unary, 1) { Unary = unary.Operator });
                Wire(ReadValue(unary.Operand, path + ".0"), key, "0");
                break;

            case QscBinary binary:
                Add(new EventValueNode(key, EventValueKind.Binary, 2) { Binary = binary.Operator });
                Wire(ReadValue(binary.Left, path + ".0"), key, "0");
                Wire(ReadValue(binary.Right, path + ".1"), key, "1");
                break;

            case QscAssignment assignment:
                Add(new EventValueNode(key, EventValueKind.Assignment, 2));
                Wire(ReadValue(assignment.Target, path + ".0"), key, "0");
                Wire(ReadValue(assignment.Value, path + ".1"), key, "1");
                break;

            default:
                throw new ArgumentException($"Unknown expression {expression.GetType().Name}.", nameof(expression));
        }

        return key;
    }

    // GetCurrentEventType() == AIEVENT_X, either way round.
    private static string? EventOf(QscExpression condition) => condition switch
    {
        QscBinary { Operator: QscBinaryOperator.Equal, Left: QscCall { Name: EventTypeNative, Arguments.Count: 0 }, Right: QscSymbol name } => name.Name,
        QscBinary { Operator: QscBinaryOperator.Equal, Left: QscSymbol name, Right: QscCall { Name: EventTypeNative, Arguments.Count: 0 } } => name.Name,
        _ => null,
    };

    private NativeSignature? Native(string name) =>
        _vocabulary is not null && _vocabulary.TryGetNative(name, out NativeSignature? native) ? native : null;

    private T Add<T>(T node)
        where T : GraphNode
    {
        _nodes[node.Key] = node;
        return node;
    }

    private void Wire(GraphNodeKey value, GraphNodeKey into, string pin) =>
        _links.Add(new GraphLink(new GraphPinRef(value, GraphNode.Output), new GraphPinRef(into, pin)));
}
