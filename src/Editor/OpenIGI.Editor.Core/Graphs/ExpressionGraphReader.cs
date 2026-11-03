using System.Globalization;
using OpenIGI.Scripting.Expressions;
using OpenIGI.Scripting.Vocabulary;

namespace OpenIGI.Editor.Core.Graphs;

/// <summary>What reading one level expression made: its nodes, the wires between them, and the pin that carries its value.</summary>
/// <param name="Nodes">The expression's nodes.</param>
/// <param name="Links">Its wires, field reads included (from a task's output pin).</param>
/// <param name="Root">The pin that carries the whole expression's value, or <see langword="null"/> when it is empty.</param>
public sealed record ExpressionReadResult(IReadOnlyList<ExpressionGraphNode> Nodes, IReadOnlyList<GraphLink> Links, GraphPinRef? Root);

/// <summary>Turns a level expression into graph nodes, reading it with the game's own parser.</summary>
/// <remarks>
/// The text goes through <see cref="LevelExpression.Compile(string, string?, LevelExpressionDialect, ILevelTaskTable?)"/>
/// first, exactly as the game prepares it (whitespace, quotes, <c>this.</c>, I.G.I. 2's comments), then
/// <see cref="ScriptExpressionParser"/>. A field read such as <c>Door_12.isOpen</c> is not a node: it is a wire from task 12's
/// <c>isOpen</c> pin, which is what makes the graph a graph. Text the parser rejects becomes one raw node holding it verbatim,
/// because the game reads it as empty and the editor must not lose it.
/// </remarks>
public sealed class ExpressionGraphReader
{
    private readonly ScriptVocabulary _vocabulary;
    private readonly LevelExpressionDialect _dialect;
    private readonly Func<int, TaskGraphNode?> _taskById;
    private List<ExpressionGraphNode> _nodes = [];
    private List<GraphLink> _links = [];
    private (GraphNodeKey Task, string Parameter) _owner;
    private string _prefix = string.Empty;

    /// <summary>Initializes a new instance of the <see cref="ExpressionGraphReader"/> class.</summary>
    /// <param name="vocabulary">The title's vocabulary: natives, constants and globals.</param>
    /// <param name="dialect">Which game's expression rules apply.</param>
    /// <param name="taskById">The mission's task node for an id, or <see langword="null"/> when it has none.</param>
    public ExpressionGraphReader(ScriptVocabulary vocabulary, LevelExpressionDialect dialect, Func<int, TaskGraphNode?> taskById)
    {
        ArgumentNullException.ThrowIfNull(vocabulary);
        ArgumentNullException.ThrowIfNull(taskById);
        _vocabulary = vocabulary;
        _dialect = dialect;
        _taskById = taskById;
    }

    /// <summary>Reads one parameter's expression.</summary>
    /// <param name="text">The parameter's text, as level data holds it.</param>
    /// <param name="owner">The task whose parameter it is.</param>
    /// <param name="parameter">The parameter's name.</param>
    /// <returns>The nodes and wires.</returns>
    public ExpressionReadResult Read(string text, TaskGraphNode owner, string parameter)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(parameter);
        _nodes = [];
        _links = [];
        _owner = (owner.Key, parameter);
        _prefix = $"{owner.Key.Value}/{parameter}/";

        string compiled = LevelExpression.Compile(text, $"{owner.Task.TypeName}_{owner.Task.Id}", _dialect);
        if (ScriptExpressionParser.IsBlank(compiled))
        {
            return new ExpressionReadResult([], [], null);
        }

        GraphPinRef root;
        try
        {
            root = Walk(ScriptExpressionParser.Parse(compiled), "r");
        }
        catch (ScriptExpressionException)
        {
            _nodes.Clear();
            _links.Clear();
            root = Add("r", ExpressionNodeKind.Raw, text, ScriptType.Any, []);
        }

        return new ExpressionReadResult(_nodes, _links, root);
    }

    /// <summary>The task id a field read names: the number after the last underscore of its subject.</summary>
    /// <param name="name">The read, such as <c>Door_12.isOpen</c>.</param>
    /// <param name="id">Receives the id.</param>
    /// <param name="field">Receives the field's name.</param>
    /// <returns><see langword="true"/> when the name is a field read of a numbered task.</returns>
    /// <remarks>The game resolves the task by this id alone; the type name before it is not checked.</remarks>
    public static bool TryParseFieldRead(string name, out int id, out string field)
    {
        ArgumentNullException.ThrowIfNull(name);
        id = -1;
        field = string.Empty;
        int dot = name.LastIndexOf('.');
        if (dot <= 0 || dot == name.Length - 1)
        {
            return false;
        }

        string subject = name[..dot];
        int bar = subject.LastIndexOf('_');
        if (bar < 0 || !int.TryParse(subject.AsSpan(bar + 1), NumberStyles.None, CultureInfo.InvariantCulture, out id))
        {
            return false;
        }

        field = name[(dot + 1)..];
        return true;
    }

    private GraphPinRef Walk(ScriptExpression expression, string path)
    {
        switch (expression.Kind)
        {
            case ScriptExpressionKind.Group when expression.Left is { } inner:
                return Walk(inner, path);

            case ScriptExpressionKind.Number:
                return Add(path, ExpressionNodeKind.Number, expression.ToString(), ScriptType.Real, [], number: expression.Number);

            case ScriptExpressionKind.String:
                return Add(path, ExpressionNodeKind.Text, expression.Text, ScriptType.Text, []);

            case ScriptExpressionKind.Constant:
                return Add(path, ExpressionNodeKind.Constant, expression.ToString(), ScriptType.Int, []);

            case ScriptExpressionKind.Conditional:
                return Operands(path, ExpressionNodeKind.Conditional, "?:", ScriptType.Any, [expression.Left, expression.Right, expression.Extra]);

            case ScriptExpressionKind.Operator:
                ScriptOperator op = expression.Operator;
                ScriptType type = ScriptOperatorInfo.IsBoolean(op) ? ScriptType.Bool
                    : op is ScriptOperator.Assign or ScriptOperator.Comma ? ScriptType.Any : ScriptType.Real;
                ScriptExpression?[] operands = ScriptOperatorInfo.IsUnary(op) ? [expression.Right] : [expression.Left, expression.Right];
                return Operands(path, ExpressionNodeKind.Operator, ScriptOperatorInfo.GetSymbol(op), type, operands, op);

            case ScriptExpressionKind.Identifier when expression.IsCall:
                return Call(path, expression.Text, expression.Arguments);

            case ScriptExpressionKind.Identifier:
                return Name(path, expression.Text);

            default:
                return Add(path, ExpressionNodeKind.UnknownName, expression.ToString(), ScriptType.Any, []);
        }
    }

    private GraphPinRef Name(string path, string name)
    {
        if (TryParseFieldRead(name, out int id, out string field))
        {
            return _taskById(id) is { } task
                ? new GraphPinRef(task.Key, field)
                : Add(path, ExpressionNodeKind.UnresolvedField, name, ScriptType.Any, []);
        }

        if (_vocabulary.TryGetConstant(name, out string? family, out _))
        {
            return Add(path, ExpressionNodeKind.Constant, name, ScriptType.EnumOf(family), []);
        }

        if (_vocabulary.Globals.TryGetValue(name, out NativeParameter? global))
        {
            return Add(path, ExpressionNodeKind.Global, name, global.Type, []);
        }

        // A native named without brackets is called all the same (HumanPlayer_isDead).
        return _vocabulary.TryGetNative(name, out _)
            ? Call(path, name, [])
            : Add(path, ExpressionNodeKind.UnknownName, name, ScriptType.Any, []);
    }

    private GraphPinRef Call(string path, string name, IReadOnlyList<ScriptExpression> arguments)
    {
        _vocabulary.TryGetNative(name, out NativeSignature? native);
        NativeParameter[] inputs = new NativeParameter[arguments.Count];
        for (int i = 0; i < inputs.Length; i++)
        {
            inputs[i] = native is { Arity: > 0 } known
                ? known.Parameters[Math.Min(i, known.Arity - 1)]
                : new NativeParameter(i.ToString(CultureInfo.InvariantCulture), ScriptType.Any);
        }

        GraphPinRef call = Add(path, ExpressionNodeKind.Call, name, native?.Returns ?? ScriptType.Any, inputs, native: native);
        for (int i = 0; i < arguments.Count; i++)
        {
            Wire(Walk(arguments[i], $"{path}.{i}"), call.Node, i);
        }

        return call;
    }

    private GraphPinRef Operands(
        string path,
        ExpressionNodeKind kind,
        string text,
        ScriptType type,
        ScriptExpression?[] operands,
        ScriptOperator op = ScriptOperator.None)
    {
        NativeParameter[] inputs = [.. operands.Select(static (_, i) => new NativeParameter(i.ToString(CultureInfo.InvariantCulture), ScriptType.Any))];
        GraphPinRef node = Add(path, kind, text, type, inputs, op);
        for (int i = 0; i < operands.Length; i++)
        {
            if (operands[i] is { } operand)
            {
                Wire(Walk(operand, $"{path}.{i}"), node.Node, i);
            }
        }

        return node;
    }

    private void Wire(GraphPinRef from, GraphNodeKey to, int input) =>
        _links.Add(new GraphLink(from, new GraphPinRef(to, input.ToString(CultureInfo.InvariantCulture))));

    private GraphPinRef Add(
        string path,
        ExpressionNodeKind kind,
        string text,
        ScriptType type,
        NativeParameter[] inputs,
        ScriptOperator op = ScriptOperator.None,
        NativeSignature? native = null,
        double number = 0)
    {
        ExpressionGraphNode node = new(new GraphNodeKey(_prefix + path), kind, text, type, inputs)
        {
            Operator = op,
            Native = native,
            Number = number,
            Owner = _owner,
        };
        _nodes.Add(node);
        return new GraphPinRef(node.Key, GraphNode.Output);
    }
}
