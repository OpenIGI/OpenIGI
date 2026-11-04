using System.Globalization;
using System.Text;
using OpenIGI.Scripting.Expressions;
using OpenIGI.Scripting.Vocabulary;

namespace OpenIGI.Editor.Core.Graphs;

/// <summary>What an expression node shows: a name for what it does, a line saying what it is, and a name for each pin.</summary>
/// <param name="Title">Its name: <c>Multiply</c>, <c>Get Current Event Type</c>, <c>GAME_FREQUENCY</c>.</param>
/// <param name="Subtitle">What it is, as written: <c>A * B</c>, <c>AIFunction · Native</c>, <c>Constant · Time = 30</c>.</param>
/// <param name="Inputs">A name for each input pin, in order.</param>
/// <param name="Output">A name for the output pin.</param>
/// <param name="Doc">A sentence on what it does, for its tooltip.</param>
public sealed record ExpressionNodeLabel(string Title, string Subtitle, IReadOnlyList<string> Inputs, string Output, string Doc);

/// <summary>Names expression nodes the way a node editor does: by what they do, with every pin named.</summary>
public static class ExpressionNodeText
{
    /// <summary>Describes a node.</summary>
    /// <param name="node">The node.</param>
    /// <param name="vocabulary">The title's vocabulary, for a constant's family and value; optional.</param>
    /// <returns>What it shows.</returns>
    public static ExpressionNodeLabel Describe(ExpressionGraphNode node, ScriptVocabulary? vocabulary = null)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.Kind switch
        {
            ExpressionNodeKind.Operator => Operator(node),
            ExpressionNodeKind.Conditional => new("Select", "condition ? a : b", ["Condition", "If True", "If False"], "Result", "The second input while the condition holds, the third otherwise."),
            ExpressionNodeKind.Number => new("Number", "Literal", [], node.Text, $"The number {node.Text}."),
            ExpressionNodeKind.Text => new("Text", "Literal", [], node.Text, "A piece of text."),
            ExpressionNodeKind.Constant => Constant(node, vocabulary),
            ExpressionNodeKind.Global => new(node.Text, $"Global · {node.OutputType}", [], "Value", $"The game's {node.Text}, read every tick."),
            ExpressionNodeKind.Call => Call(node),
            ExpressionNodeKind.UnresolvedField => new(node.Text, "Missing task", [], "Value", $"{node.Text} names a task this mission does not have, so it reads nothing."),
            ExpressionNodeKind.UnknownName => new(node.Text, "Unknown name", [], "Value", $"{node.Text} is not a name the game knows."),
            _ => new("Unreadable expression", "Kept as written", [], "Value", "The game cannot read this text, so it treats the expression as empty."),
        };
    }

    /// <summary>Turns a native's name into words: <c>AIFunction_GetCurrentEventType</c> is <c>Get Current Event Type</c>.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The words, and the prefix before the first underscore (empty when there is none).</returns>
    public static (string Words, string Prefix) Humanise(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        int underscore = name.IndexOf('_', StringComparison.Ordinal);
        string prefix = underscore > 0 ? name[..underscore] : string.Empty;
        string rest = underscore > 0 ? name[(underscore + 1)..] : name;
        StringBuilder words = new(rest.Length + 8);
        for (int i = 0; i < rest.Length; i++)
        {
            char c = rest[i];
            if (c == '_')
            {
                words.Append(' ');
                continue;
            }

            // A word starts at a capital after a small letter (GetCurrent), or before a small one after capitals (AIAction).
            bool starts = i > 0 && char.IsUpper(c)
                && (char.IsLower(rest[i - 1]) || (i + 1 < rest.Length && char.IsLower(rest[i + 1]) && char.IsUpper(rest[i - 1])));
            if (starts && words.Length > 0 && words[^1] != ' ')
            {
                words.Append(' ');
            }

            words.Append(i == 0 ? char.ToUpperInvariant(c) : c);
        }

        return (words.ToString(), prefix);
    }

    /// <summary>A native's category as it reads on a node: its path's parts joined by an arrow, <c>AI › Actions</c>.</summary>
    /// <param name="category">The category path, as the vocabulary keeps it (<c>AI|Actions</c>).</param>
    /// <returns>The words.</returns>
    public static string Category(string category)
    {
        ArgumentNullException.ThrowIfNull(category);
        return string.Join(" › ", category.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static ExpressionNodeLabel Operator(ExpressionGraphNode node)
    {
        string symbol = ScriptOperatorInfo.GetSymbol(node.Operator);
        (string title, string doc) = node.Operator switch
        {
            ScriptOperator.Add => ("Add", "A plus B."),
            ScriptOperator.Subtract => ("Subtract", "A minus B."),
            ScriptOperator.Multiply => ("Multiply", "A times B."),
            ScriptOperator.Divide => ("Divide", "A divided by B."),
            ScriptOperator.UnaryMinus => ("Negate", "The value with its sign flipped."),
            ScriptOperator.UnaryPlus => ("Plus", "The value, unchanged."),
            ScriptOperator.BitwiseOr => ("Bitwise OR", "The bits set in A or B."),
            ScriptOperator.BitwiseXor => ("Bitwise XOR", "The bits set in A or B but not both."),
            ScriptOperator.BitwiseAnd => ("Bitwise AND", "The bits set in both A and B."),
            ScriptOperator.ShiftLeft => ("Shift Left", "A's bits moved B places up."),
            ScriptOperator.ShiftRight => ("Shift Right", "A's bits moved B places down."),
            ScriptOperator.BitwiseNot => ("Bitwise NOT", "Every bit of the value flipped."),
            ScriptOperator.Equal => ("Equal", "True when A equals B."),
            ScriptOperator.NotEqual => ("Not Equal", "True when A differs from B."),
            ScriptOperator.Less => ("Less", "True when A is below B."),
            ScriptOperator.Greater => ("Greater", "True when A is above B."),
            ScriptOperator.LessOrEqual => ("Less or Equal", "True when A is at most B."),
            ScriptOperator.GreaterOrEqual => ("Greater or Equal", "True when A is at least B."),
            ScriptOperator.Not => ("NOT", "True when the value is false."),
            ScriptOperator.LogicalOr => ("OR", "True when either input is; the second is not read when the first holds."),
            ScriptOperator.LogicalAnd => ("AND", "True when both inputs are; the second is not read when the first fails."),
            ScriptOperator.Assign => ("Set", "Writes the value into the target, then gives it."),
            ScriptOperator.Comma => ("Then", "Does the first, then the second, and gives the second."),
            _ => (symbol, "An operator."),
        };

        IReadOnlyList<string> inputs = node.Operator switch
        {
            ScriptOperator.Assign => ["Target", "Value"],
            ScriptOperator.Comma => ["First", "Then"],
            _ when node.Inputs.Count == 1 => ["Value"],
            _ => ["A", "B"],
        };
        string written = node.Inputs.Count == 1 ? $"{symbol} value" : $"A {symbol} B";
        return new(title, written, inputs, "Result", doc);
    }

    private static ExpressionNodeLabel Constant(ExpressionGraphNode node, ScriptVocabulary? vocabulary)
    {
        if (vocabulary is not null && vocabulary.TryGetConstant(node.Text, out string? family, out int value))
        {
            return new(node.Text, string.Create(CultureInfo.InvariantCulture, $"Constant · {family} = {value}"), [], "Value", $"The game's {family} constant {node.Text}, which is {value.ToString(CultureInfo.InvariantCulture)}.");
        }

        return new(node.Text, "Constant", [], "Value", $"The game's constant {node.Text}.");
    }

    private static ExpressionNodeLabel Call(ExpressionGraphNode node)
    {
        (string words, string prefix) = Humanise(node.Text);
        string category = node.Native?.Category is { Length: > 0 } c ? Category(c) : "Native";
        string subtitle = prefix.Length > 0 ? $"{prefix} · {category}" : category;
        IReadOnlyList<string> inputs = [.. node.Inputs.Select(static (p, i) => p.Name is { Length: > 0 } name ? name : $"Argument {i + 1}")];
        string output = node.Native is { Returns: null } ? "Done" : "Result";
        return new(words, subtitle, inputs, output, node.Native?.Doc ?? $"Calls {node.Text}.");
    }
}
