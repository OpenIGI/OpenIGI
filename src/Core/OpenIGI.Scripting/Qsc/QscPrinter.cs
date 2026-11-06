using System.Globalization;
using System.Text;

namespace OpenIGI.Scripting.Qsc;

/// <summary>
/// Writes a QSC syntax tree as source text that <see cref="QscParser"/> reads back into the same tree.
/// </summary>
/// <remarks>
/// <para>
/// The layout follows the retail sources: one statement per line, and a call whose arguments include
/// further calls — a <c>Task_New</c> holding its children — puts each such argument on its own, deeper
/// indented line, so an object tree reads as a tree. Control flow uses Allman braces, and an
/// <c>else</c> whose only statement is an <c>if</c> prints as <c>else if</c>.
/// </para>
/// <para>
/// Reals print in the shortest form that reads back as the same 32-bit value, always with a decimal
/// point or exponent, because <c>0</c> and <c>0.0</c> are different literals (an integer push and a
/// float push). Parentheses appear only where precedence needs them.
/// </para>
/// </remarks>
public static class QscPrinter
{
    private const char Indent = '\t';

    /// <summary>Prints a whole script.</summary>
    /// <param name="script">The script.</param>
    /// <returns>The source text, ending with a newline.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="script"/> is <see langword="null"/>.</exception>
    public static string Print(QscScript script)
    {
        ArgumentNullException.ThrowIfNull(script);
        StringBuilder text = new();
        PrintStatements(text, script.Statements, 0);
        return text.ToString();
    }

    /// <summary>Prints one expression on a single logical line (nested calls may still break lines).</summary>
    /// <param name="expression">The expression.</param>
    /// <returns>The source text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="expression"/> is <see langword="null"/>.</exception>
    public static string Print(QscExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        StringBuilder text = new();
        PrintExpression(text, expression, 0, 0);
        return text.ToString();
    }

    /// <summary>Formats a real so that it reads back as exactly the same value and is never taken for an integer.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The literal's text.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is not finite: QSC has no such literal.</exception>
    public static string FormatReal(float value)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "QSC has no literal for a value that is not finite.");
        }

        string text = value.ToString("R", CultureInfo.InvariantCulture);
        return text.Contains('.', StringComparison.Ordinal) || text.Contains('E', StringComparison.Ordinal)
            ? text
            : text + ".0";
    }

    /// <summary>Formats a string literal with its quotes and escapes.</summary>
    /// <param name="value">The text.</param>
    /// <returns>The literal's source text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static string FormatString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        StringBuilder text = new(value.Length + 2);
        text.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '"':
                    text.Append("\\\"");
                    break;
                case '\\':
                    text.Append("\\\\");
                    break;
                case '\n':
                    text.Append("\\n");
                    break;
                case '\r':
                    text.Append("\\r");
                    break;
                case '\t':
                    text.Append("\\t");
                    break;
                case < ' ':
                    text.Append(CultureInfo.InvariantCulture, $"\\x{(int)c:X2}");
                    break;
                default:
                    text.Append(c);
                    break;
            }
        }

        return text.Append('"').ToString();
    }

    internal static int Precedence(QscBinaryOperator op) => op switch
    {
        QscBinaryOperator.LogicalOr => 2,
        QscBinaryOperator.LogicalAnd => 3,
        QscBinaryOperator.BitwiseOr => 4,
        QscBinaryOperator.BitwiseXor => 5,
        QscBinaryOperator.BitwiseAnd => 6,
        QscBinaryOperator.Equal or QscBinaryOperator.NotEqual => 7,
        QscBinaryOperator.Less or QscBinaryOperator.LessOrEqual or QscBinaryOperator.Greater or QscBinaryOperator.GreaterOrEqual => 8,
        QscBinaryOperator.ShiftLeft or QscBinaryOperator.ShiftRight => 9,
        QscBinaryOperator.Add or QscBinaryOperator.Subtract => 10,
        QscBinaryOperator.Multiply or QscBinaryOperator.Divide => 11,
        _ => throw new ArgumentOutOfRangeException(nameof(op), op, null),
    };

    internal static string Symbol(QscBinaryOperator op) => op switch
    {
        QscBinaryOperator.Add => "+",
        QscBinaryOperator.Subtract => "-",
        QscBinaryOperator.Multiply => "*",
        QscBinaryOperator.Divide => "/",
        QscBinaryOperator.ShiftLeft => "<<",
        QscBinaryOperator.ShiftRight => ">>",
        QscBinaryOperator.BitwiseAnd => "&",
        QscBinaryOperator.BitwiseOr => "|",
        QscBinaryOperator.BitwiseXor => "^",
        QscBinaryOperator.LogicalAnd => "&&",
        QscBinaryOperator.LogicalOr => "||",
        QscBinaryOperator.Equal => "==",
        QscBinaryOperator.NotEqual => "!=",
        QscBinaryOperator.Less => "<",
        QscBinaryOperator.LessOrEqual => "<=",
        QscBinaryOperator.Greater => ">",
        QscBinaryOperator.GreaterOrEqual => ">=",
        _ => throw new ArgumentOutOfRangeException(nameof(op), op, null),
    };

    internal static char Symbol(QscUnaryOperator op) => op switch
    {
        QscUnaryOperator.Plus => '+',
        QscUnaryOperator.Minus => '-',
        QscUnaryOperator.BitwiseNot => '~',
        QscUnaryOperator.LogicalNot => '!',
        _ => throw new ArgumentOutOfRangeException(nameof(op), op, null),
    };

    private const int AssignmentPrecedence = 1;
    private const int UnaryPrecedence = 12;
    private const int PrimaryPrecedence = 13;

    private static void PrintStatements(StringBuilder text, IEnumerable<QscStatement> statements, int depth)
    {
        foreach (QscStatement statement in statements)
        {
            PrintStatement(text, statement, depth);
        }
    }

    private static void PrintStatement(StringBuilder text, QscStatement statement, int depth)
    {
        switch (statement)
        {
            case QscExpressionStatement expression:
                AppendIndent(text, depth);
                PrintExpression(text, expression.Expression, 0, depth);
                text.Append(";\n");
                break;

            case QscIf conditional:
                AppendIndent(text, depth);
                PrintIf(text, conditional, depth);
                break;

            case QscWhile loop:
                AppendIndent(text, depth);
                text.Append("while (");
                PrintExpression(text, loop.Condition, 0, depth);
                text.Append(")\n");
                PrintBlock(text, loop.Body, depth);
                break;

            default:
                throw new ArgumentException($"Unknown statement node {statement.GetType().Name}.", nameof(statement));
        }
    }

    private static void PrintIf(StringBuilder text, QscIf conditional, int depth)
    {
        text.Append("if (");
        PrintExpression(text, conditional.Condition, 0, depth);
        text.Append(")\n");
        PrintBlock(text, conditional.Then, depth);

        if (conditional.Else is not { } otherwise)
        {
            return;
        }

        AppendIndent(text, depth);
        if (otherwise is [QscIf chained])
        {
            text.Append("else ");
            PrintIf(text, chained, depth);
            return;
        }

        text.Append("else\n");
        PrintBlock(text, otherwise, depth);
    }

    private static void PrintBlock(StringBuilder text, IEnumerable<QscStatement> statements, int depth)
    {
        AppendIndent(text, depth);
        text.Append("{\n");
        PrintStatements(text, statements, depth + 1);
        AppendIndent(text, depth);
        text.Append("}\n");
    }

    private static void PrintExpression(StringBuilder text, QscExpression expression, int required, int depth)
    {
        int own = PrecedenceOf(expression);
        bool parenthesise = own < required;
        if (parenthesise)
        {
            text.Append('(');
        }

        switch (expression)
        {
            case QscIntLiteral literal:
                text.Append(literal.Value.ToString(CultureInfo.InvariantCulture));
                break;
            case QscRealLiteral literal:
                text.Append(FormatReal(literal.Value));
                break;
            case QscStringLiteral literal:
                text.Append(FormatString(literal.Value));
                break;
            case QscSymbol symbol:
                text.Append(symbol.Name);
                break;
            case QscCall call:
                PrintCall(text, call, depth);
                break;
            case QscUnary unary:
                text.Append(Symbol(unary.Operator));

                // A nested unary or a negative literal is parenthesised so "- -1" never lexes as one token.
                PrintExpression(text, unary.Operand, unary.Operand is QscUnary ? PrimaryPrecedence + 1 : UnaryPrecedence, depth);
                break;
            case QscBinary binary:
            {
                int precedence = Precedence(binary.Operator);
                PrintExpression(text, binary.Left, precedence, depth);
                text.Append(' ').Append(Symbol(binary.Operator)).Append(' ');
                PrintExpression(text, binary.Right, precedence + 1, depth);
                break;
            }

            case QscAssignment assignment:
                PrintExpression(text, assignment.Target, UnaryPrecedence, depth);
                text.Append(" = ");
                PrintExpression(text, assignment.Value, AssignmentPrecedence, depth);
                break;
            default:
                throw new ArgumentException($"Unknown expression node {expression.GetType().Name}.", nameof(expression));
        }

        if (parenthesise)
        {
            text.Append(')');
        }
    }

    private static void PrintCall(StringBuilder text, QscCall call, int depth)
    {
        text.Append(call.Name).Append('(');
        for (int i = 0; i < call.Arguments.Count; i++)
        {
            QscExpression argument = call.Arguments[i];
            if (i > 0)
            {
                text.Append(',');
            }

            if (BreaksLine(argument))
            {
                text.Append('\n');
                AppendIndent(text, depth + 1);
                PrintExpression(text, argument, 0, depth + 1);
            }
            else
            {
                if (i > 0)
                {
                    text.Append(' ');
                }

                PrintExpression(text, argument, 0, depth);
            }
        }

        text.Append(')');
    }

    // A call that itself holds arguments is a child in a tree (Task_New inside Task_New) and gets its own line.
    private static bool BreaksLine(QscExpression argument) => argument is QscCall { Arguments.Count: > 0 };

    private static int PrecedenceOf(QscExpression expression) => expression switch
    {
        QscAssignment => AssignmentPrecedence,
        QscBinary binary => Precedence(binary.Operator),
        QscUnary => UnaryPrecedence,

        // A negative literal can only come from a decompiled push of a negative immediate; it is wrapped so
        // that it never merges with a preceding operator.
        QscIntLiteral { Value: < 0 } or QscRealLiteral { Value: < 0 } => UnaryPrecedence,
        _ => PrimaryPrecedence,
    };

    private static void AppendIndent(StringBuilder text, int depth)
    {
        for (int i = 0; i < depth; i++)
        {
            text.Append(Indent);
        }
    }
}
