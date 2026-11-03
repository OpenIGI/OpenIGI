using System.Globalization;
using System.Text;
using OpenIGI.Scripting.Expressions;

namespace OpenIGI.Editor.Core.Graphs;

/// <summary>Writes the text of a level expression from its graph: what the game's parser reads back as the same tree.</summary>
/// <remarks>
/// <para>
/// Brackets are written only where the operators' precedence and associativity need them, using the parser's own table
/// (<see cref="ScriptOperatorInfo"/>), so text the graph writes is the shortest the game reads the same way. A field read is
/// written <c>Type_Id.field</c> from the task the wire comes from: the game finds the task by the id alone, so a retail read
/// whose type name disagrees with its task is written with the task's real type, the same read.
/// </para>
/// <para>
/// A raw node is written back verbatim, so text the game cannot parse stays exactly as level data had it.
/// </para>
/// </remarks>
public static class ExpressionGraphWriter
{
    // Looser than any operator: an operand at this level never needs brackets.
    private const int Atom = 0;

    /// <summary>Writes the expression that feeds a task's parameter.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="task">The task.</param>
    /// <param name="parameter">The parameter.</param>
    /// <returns>The text, or an empty string when nothing feeds the parameter.</returns>
    public static string Write(MissionGraph graph, GraphNodeKey task, string parameter)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(parameter);
        return graph.ExpressionRoots.TryGetValue((task, parameter), out GraphPinRef root) ? Write(graph, root) : string.Empty;
    }

    /// <summary>Writes the expression an output pin carries.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="output">The output pin.</param>
    /// <returns>The text.</returns>
    public static string Write(MissionGraph graph, GraphPinRef output)
    {
        ArgumentNullException.ThrowIfNull(graph);
        StringBuilder text = new();
        Append(graph, output, text);
        return text.ToString();
    }

    private static void Append(MissionGraph graph, GraphPinRef output, StringBuilder text)
    {
        switch (graph.Nodes[output.Node])
        {
            case TaskGraphNode task:
                text.Append(CultureInfo.InvariantCulture, $"{task.Task.TypeName}_{task.Task.Id}.{output.Pin}");
                break;

            case ExpressionGraphNode node:
                AppendNode(graph, node, text);
                break;
        }
    }

    private static void AppendNode(MissionGraph graph, ExpressionGraphNode node, StringBuilder text)
    {
        switch (node.Kind)
        {
            case ExpressionNodeKind.Number:
                // The short spelling, unless it loses the value (a tiny real), then the round-trip one with its exponent.
                bool exact = double.TryParse(node.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double shown) && shown == node.Number;
                text.Append(exact ? node.Text : node.Number.ToString("R", CultureInfo.InvariantCulture));
                break;

            case ExpressionNodeKind.Text:
                text.Append('"').Append(node.Text.Replace("\"", "\\\"", StringComparison.Ordinal)).Append('"');
                break;

            case ExpressionNodeKind.Call:
                text.Append(node.Text);
                if (!node.IsBare)
                {
                    text.Append('(');
                    for (int i = 0; i < node.Inputs.Count; i++)
                    {
                        if (i > 0)
                        {
                            text.Append(", ");
                        }

                        // An argument is its own expression: a comma inside it would split the call, so it is bracketed.
                        Operand(graph, node, i, ScriptOperatorInfo.GetPrecedence(ScriptOperator.Comma), text);
                    }

                    text.Append(')');
                }

                break;

            case ExpressionNodeKind.Operator when ScriptOperatorInfo.IsUnary(node.Operator):
                text.Append(node.Text);
                Operand(graph, node, 0, ScriptOperatorInfo.GetPrecedence(node.Operator), text, unary: true);
                break;

            case ExpressionNodeKind.Operator:
                int precedence = ScriptOperatorInfo.GetPrecedence(node.Operator);
                bool left = ScriptOperatorInfo.IsLeftAssociative(node.Operator);
                Operand(graph, node, 0, left ? precedence + 1 : precedence, text);
                text.Append(node.Operator == ScriptOperator.Comma ? ", " : $" {node.Text} ");
                Operand(graph, node, 1, left ? precedence : precedence + 1, text);
                break;

            case ExpressionNodeKind.Conditional:
                int question = ScriptOperatorInfo.GetPrecedence(ScriptOperator.Question);
                Operand(graph, node, 0, question, text);
                text.Append(" ? ");
                Operand(graph, node, 1, question + 1, text);
                text.Append(" : ");
                Operand(graph, node, 2, question + 1, text);
                break;

            default:
                text.Append(node.Text);
                break;
        }
    }

    // One operand, bracketed when it binds looser than the place it stands in allows: an operand whose precedence is at or
    // past "limit" is wrapped. A unary operator's operand is wrapped whenever it is an operator, which also keeps "- -x" from
    // reading as a decrement.
    private static void Operand(MissionGraph graph, ExpressionGraphNode node, int index, int limit, StringBuilder text, bool unary = false)
    {
        if (graph.SourceOf(new GraphPinRef(node.Key, index.ToString(CultureInfo.InvariantCulture))) is not { } source)
        {
            text.Append('0');
            return;
        }

        int own = PrecedenceOf(graph, source);
        bool wrap = unary ? own != Atom : own >= limit && own != Atom;
        if (wrap)
        {
            text.Append('(');
        }

        Append(graph, source, text);
        if (wrap)
        {
            text.Append(')');
        }
    }

    private static int PrecedenceOf(MissionGraph graph, GraphPinRef output) => graph.Nodes[output.Node] switch
    {
        ExpressionGraphNode { Kind: ExpressionNodeKind.Operator } node => ScriptOperatorInfo.GetPrecedence(node.Operator),
        ExpressionGraphNode { Kind: ExpressionNodeKind.Conditional } => ScriptOperatorInfo.GetPrecedence(ScriptOperator.Question),
        _ => Atom,
    };
}
