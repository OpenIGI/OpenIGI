using OpenIGI.Scripting.Qsc;

namespace OpenIGI.Editor.Core.Missions;

/// <summary>One part of a condition as the condition editor shows it: a group of parts, one test, or an expression kept as written.</summary>
public abstract class ConditionNode
{
    /// <summary>Gets or sets whether the part is negated (<c>!</c>).</summary>
    public bool Negated { get; set; }
}

/// <summary>Parts joined by one operator: all of them (<c>&amp;&amp;</c>) or any of them (<c>||</c>).</summary>
public sealed class ConditionGroup : ConditionNode
{
    /// <summary>Gets or sets whether any part is enough (<c>||</c>) rather than every part (<c>&amp;&amp;</c>).</summary>
    public bool Any { get; set; }

    /// <summary>Gets the parts.</summary>
    public List<ConditionNode> Items { get; } = [];
}

/// <summary>One test of a task's field: <c>CutScene_1200.isRun</c>, or compared, <c>AnimTask_850.nTick &lt; 2320</c>.</summary>
public sealed class ConditionTerm : ConditionNode
{
    /// <summary>Gets or sets the task the field belongs to, as scripts name it (<c>CutScene_1200</c>, or <c>this</c>).</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Gets or sets the field (<c>isRun</c>).</summary>
    public string Field { get; set; } = string.Empty;

    /// <summary>Gets or sets the comparison, or <see langword="null"/> for the field's truth alone.</summary>
    public QscBinaryOperator? Compare { get; set; }

    /// <summary>Gets or sets what the field is compared with, when it is compared.</summary>
    public QscExpression? Value { get; set; }

    /// <summary>Gets the symbol the term reads, <c>Subject.Field</c>.</summary>
    public string Symbol => $"{Subject}.{Field}";
}

/// <summary>An expression the editor cannot take apart into tests (a call, arithmetic), kept exactly as written.</summary>
/// <param name="expression">The expression.</param>
public sealed class ConditionCode(QscExpression expression) : ConditionNode
{
    /// <summary>Gets or sets the expression.</summary>
    public QscExpression Expression { get; set; } = expression;
}

/// <summary>
/// A <c>Condition</c> parameter's expression (<c>CutScene_1200.isRun &amp;&amp; !CutScene_1200.isFinished</c>) taken apart into
/// groups and tests a person can read and change, and put back together.
/// </summary>
/// <remarks>
/// A chain of one logical operator becomes one group; <c>!</c> marks the part it applies to; a task's field, bare or compared
/// with a value, becomes a test. Anything else stays an expression of its own. Printed back, the condition means what it
/// meant: the same tree, parenthesised where the grouping needs it.
/// </remarks>
public static class ConditionModel
{
    private static readonly HashSet<QscBinaryOperator> Comparisons =
    [
        QscBinaryOperator.Equal, QscBinaryOperator.NotEqual, QscBinaryOperator.Less, QscBinaryOperator.LessOrEqual,
        QscBinaryOperator.Greater, QscBinaryOperator.GreaterOrEqual,
    ];

    /// <summary>Gets the comparisons a test can make, in the order an editor offers them.</summary>
    public static IReadOnlyList<QscBinaryOperator> ComparisonOperators { get; } =
    [
        QscBinaryOperator.Equal, QscBinaryOperator.NotEqual, QscBinaryOperator.Less, QscBinaryOperator.LessOrEqual,
        QscBinaryOperator.Greater, QscBinaryOperator.GreaterOrEqual,
    ];

    /// <summary>Takes a condition apart.</summary>
    /// <param name="text">The condition's text; blank for none.</param>
    /// <param name="root">Receives the condition as a group (one that holds a single part when there is no operator).</param>
    /// <param name="error">Receives why the text is not an expression, when it is not.</param>
    /// <returns>Whether the text parsed.</returns>
    public static bool TryParse(string text, out ConditionGroup root, out string? error)
    {
        root = new ConditionGroup();
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        try
        {
            ConditionNode node = Read(QscParser.ParseExpression(text));
            if (node is ConditionGroup group && !group.Negated)
            {
                root = group;
            }
            else
            {
                root.Items.Add(node);
            }

            return true;
        }
        catch (QscParseException parse)
        {
            error = parse.Message;
            return false;
        }
    }

    /// <summary>Puts a condition back together as text.</summary>
    /// <param name="root">The condition.</param>
    /// <returns>The expression's text, empty when the condition holds nothing.</returns>
    public static string Print(ConditionGroup root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return Write(root) is { } expression ? QscPrinter.Print(expression) : string.Empty;
    }

    /// <summary>Gets the symbols a condition reads, in order, each once.</summary>
    /// <param name="root">The condition.</param>
    /// <returns>The symbols (<c>CutScene_1200.isRun</c>), from tests and from expressions kept as written.</returns>
    public static IReadOnlyList<string> Symbols(ConditionNode root)
    {
        List<string> found = [];
        Collect(root, found);
        return [.. found.Distinct(StringComparer.Ordinal)];
    }

    /// <summary>Gets a comparison as it is written.</summary>
    /// <param name="comparison">The comparison.</param>
    /// <returns>Its operator text.</returns>
    public static string Text(QscBinaryOperator comparison) => comparison switch
    {
        QscBinaryOperator.Equal => "==",
        QscBinaryOperator.NotEqual => "!=",
        QscBinaryOperator.Less => "<",
        QscBinaryOperator.LessOrEqual => "<=",
        QscBinaryOperator.Greater => ">",
        QscBinaryOperator.GreaterOrEqual => ">=",
        _ => comparison.ToString(),
    };

    private static ConditionNode Read(QscExpression expression)
    {
        switch (expression)
        {
            case QscUnary { Operator: QscUnaryOperator.LogicalNot } not:
            {
                ConditionNode inner = Read(not.Operand);
                inner.Negated = !inner.Negated;
                return inner;
            }

            case QscBinary { Operator: QscBinaryOperator.LogicalAnd or QscBinaryOperator.LogicalOr } logical:
            {
                ConditionGroup group = new() { Any = logical.Operator == QscBinaryOperator.LogicalOr };
                Flatten(logical, logical.Operator, group.Items);
                return group;
            }

            case QscBinary binary when Comparisons.Contains(binary.Operator) && Reference(binary.Left) is { } reference:
                return new ConditionTerm { Subject = reference.Subject, Field = reference.Field, Compare = binary.Operator, Value = binary.Right };

            case QscSymbol symbol when Reference(symbol) is { } bare:
                return new ConditionTerm { Subject = bare.Subject, Field = bare.Field };

            default:
                return new ConditionCode(expression);
        }
    }

    // A chain of one logical operator, as one list: (a && b) && c is a, b, c.
    private static void Flatten(QscExpression expression, QscBinaryOperator chain, List<ConditionNode> into)
    {
        if (expression is QscBinary binary && binary.Operator == chain)
        {
            Flatten(binary.Left, chain, into);
            Flatten(binary.Right, chain, into);
        }
        else
        {
            into.Add(Read(expression));
        }
    }

    private static (string Subject, string Field)? Reference(QscExpression expression)
    {
        if (expression is not QscSymbol symbol)
        {
            return null;
        }

        int dot = symbol.Name.LastIndexOf('.');
        return dot > 0 && dot < symbol.Name.Length - 1 ? (symbol.Name[..dot], symbol.Name[(dot + 1)..]) : null;
    }

    private static QscExpression? Write(ConditionNode node)
    {
        QscExpression? written = node switch
        {
            ConditionGroup group => WriteGroup(group),
            ConditionTerm term when string.IsNullOrEmpty(term.Subject) || string.IsNullOrEmpty(term.Field) => null,
            ConditionTerm { Compare: { } compare, Value: { } value } term => new QscBinary(compare, new QscSymbol(term.Symbol), value),
            ConditionTerm term => new QscSymbol(term.Symbol),
            ConditionCode code => code.Expression,
            _ => null,
        };

        return written is not null && node.Negated ? new QscUnary(QscUnaryOperator.LogicalNot, written) : written;
    }

    private static QscExpression? WriteGroup(ConditionGroup group)
    {
        QscExpression? result = null;
        QscBinaryOperator join = group.Any ? QscBinaryOperator.LogicalOr : QscBinaryOperator.LogicalAnd;
        foreach (ConditionNode item in group.Items)
        {
            if (Write(item) is { } part)
            {
                result = result is null ? part : new QscBinary(join, result, part);
            }
        }

        return result;
    }

    private static void Collect(ConditionNode node, List<string> into)
    {
        switch (node)
        {
            case ConditionGroup group:
                foreach (ConditionNode item in group.Items)
                {
                    Collect(item, into);
                }

                break;

            case ConditionTerm term:
                into.Add(term.Symbol);
                break;

            case ConditionCode code:
                CollectExpression(code.Expression, into);
                break;
        }
    }

    private static void CollectExpression(QscExpression expression, List<string> into)
    {
        switch (expression)
        {
            case QscSymbol symbol when symbol.Name.Contains('.'):
                into.Add(symbol.Name);
                break;

            case QscUnary unary:
                CollectExpression(unary.Operand, into);
                break;

            case QscBinary binary:
                CollectExpression(binary.Left, into);
                CollectExpression(binary.Right, into);
                break;

            case QscCall call:
                foreach (QscExpression argument in call.Arguments)
                {
                    CollectExpression(argument, into);
                }

                break;
        }
    }
}
