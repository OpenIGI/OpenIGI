using OpenIGI.Core.Numerics;

namespace OpenIGI.Scripting.Expressions;

/// <summary>
/// Evaluates a parsed expression.
/// </summary>
/// <remarks>
/// <para>
/// Transcribed from <c>Expr_EvalInt</c> (<c>0x4B9120</c>). Four of its behaviours are observable and
/// are reproduced deliberately rather than tidied:
/// </para>
/// <list type="number">
/// <item><description>
/// <b>Everything is a 32-bit integer.</b> The tokenizer parses numbers as <c>double</c>, but the
/// evaluator truncates each literal toward zero on the way in and every operator works on
/// <see cref="int"/>. There is no floating-point arithmetic in this language at all.
/// </description></item>
/// <item><description>
/// <b>Binary operands evaluate right-to-left.</b> The original computes its right operand first and
/// then its left, for every arithmetic, bitwise and comparison operator. With a call on each side —
/// which retail menu scripts do have — the calls happen in that order, so this is not free to
/// "correct".
/// </description></item>
/// <item><description>
/// <b>The comma is the exception</b>: it evaluates left first, then right, and yields the right.
/// </description></item>
/// <item><description>
/// <b>Division by zero yields zero</b> and raises the script error flag rather than faulting. The
/// original defines this case, so it is not a divergence.
/// </description></item>
/// </list>
/// <para>
/// Shifts take only the low byte of their right operand and <c>&gt;&gt;</c> is arithmetic, both
/// straight from the original's use of a <c>char</c> shift count on a signed operand.
/// </para>
/// </remarks>
public sealed class ScriptExpressionEvaluator
{
    private readonly IScriptExpressionContext _context;

    /// <summary>Initializes a new instance of the <see cref="ScriptExpressionEvaluator"/> class.</summary>
    /// <param name="context">The names the expression may reach.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    public ScriptExpressionEvaluator(IScriptExpressionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <summary>Gets a value indicating whether an error was raised during the last evaluation.</summary>
    /// <remarks>
    /// The original sets a global flag for a division by zero and reports unknown identifiers to its
    /// console, then carries on with a zero result rather than aborting the frame.
    /// </remarks>
    public bool HasError { get; private set; }

    /// <summary>Gets the message describing the last error, or an empty string.</summary>
    public string ErrorMessage { get; private set; } = string.Empty;

    /// <summary>Evaluates an expression.</summary>
    /// <param name="expression">The expression to evaluate.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="expression"/> is <see langword="null"/>.</exception>
    public int Evaluate(ScriptExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        HasError = false;
        ErrorMessage = string.Empty;
        return Eval(expression);
    }

    /// <summary>Evaluates an expression as a condition.</summary>
    /// <param name="expression">The expression to evaluate.</param>
    /// <returns><see langword="true"/> when the result is non-zero.</returns>
    public bool EvaluateCondition(ScriptExpression expression) => Evaluate(expression) != 0;

    private int Eval(ScriptExpression? node)
    {
        if (node is null)
        {
            return 0;
        }

        return node.Kind switch
        {
            ScriptExpressionKind.Constant => node.Constant,

            // Literals are truncated toward zero, which is the project's float-to-int rule everywhere.
            ScriptExpressionKind.Number => IgiMath.TruncToInt32(node.Number),

            // A string has no integer value; the original's integer path returns zero for one.
            ScriptExpressionKind.String => 0,

            ScriptExpressionKind.Group => Eval(node.Left),
            ScriptExpressionKind.Identifier => EvalIdentifier(node),
            ScriptExpressionKind.Conditional => Eval(node.Left) != 0 ? Eval(node.Right) : Eval(node.Extra),
            ScriptExpressionKind.Operator => EvalOperator(node),
            _ => 0,
        };
    }

    private int EvalIdentifier(ScriptExpression node)
    {
        if (node.IsCall)
        {
            int[] arguments = new int[node.Arguments.Count];
            for (int i = 0; i < arguments.Length; i++)
            {
                arguments[i] = Eval(node.Arguments[i]);
            }

            if (_context.TryInvoke(node.Text, arguments, out int result))
            {
                return result;
            }

            return Fail($"Unknown identifier '{node.Text}' in expression, line {node.Line}");
        }

        if (_context.TryGetValue(node.Text, out int value))
        {
            return value;
        }

        return Fail($"Unknown identifier '{node.Text}' in expression, line {node.Line}");
    }

    private int EvalOperator(ScriptExpression node)
    {
        // Unary operators take their operand from the right child, as they do in the original.
        switch (node.Operator)
        {
            case ScriptOperator.UnaryPlus:
                return Eval(node.Right);
            case ScriptOperator.UnaryMinus:
                return -Eval(node.Right);
            case ScriptOperator.BitwiseNot:
                return ~Eval(node.Right);
            case ScriptOperator.Not:
                return Eval(node.Right) == 0 ? 1 : 0;

            // Short-circuiting operators must not pre-evaluate, so they come before the general path.
            case ScriptOperator.LogicalAnd:
                return Eval(node.Left) != 0 && Eval(node.Right) != 0 ? 1 : 0;
            case ScriptOperator.LogicalOr:
                return Eval(node.Left) != 0 || Eval(node.Right) != 0 ? 1 : 0;

            // The comma is the one operator that evaluates left first.
            case ScriptOperator.Comma:
                Eval(node.Left);
                return Eval(node.Right);

            case ScriptOperator.Assign:
                return EvalAssignment(node);
        }

        // Everything else: right operand first, then left. Deliberate — see the remarks.
        int right = Eval(node.Right);
        int left = Eval(node.Left);

        return node.Operator switch
        {
            ScriptOperator.Add => left + right,
            ScriptOperator.Subtract => left - right,
            ScriptOperator.Multiply => left * right,
            ScriptOperator.Divide => right != 0 ? left / right : Fail("Division by zero in expression"),
            ScriptOperator.BitwiseOr => left | right,
            ScriptOperator.BitwiseXor => left ^ right,
            ScriptOperator.BitwiseAnd => left & right,

            // The original's shift count is a char, so only the low byte reaches the shift.
            ScriptOperator.ShiftLeft => left << (byte)right,
            ScriptOperator.ShiftRight => left >> (byte)right,

            ScriptOperator.Equal => left == right ? 1 : 0,
            ScriptOperator.NotEqual => left != right ? 1 : 0,
            ScriptOperator.Less => left < right ? 1 : 0,
            ScriptOperator.Greater => left > right ? 1 : 0,
            ScriptOperator.LessOrEqual => left <= right ? 1 : 0,
            ScriptOperator.GreaterOrEqual => left >= right ? 1 : 0,
            _ => 0,
        };
    }

    private int EvalAssignment(ScriptExpression node)
    {
        ScriptExpression? target = node.Left;
        if (target is null || !target.IsAssignable)
        {
            return Fail($"Expression: Cannot assign to constant, line {node.Line}");
        }

        int value = Eval(node.Right);
        if (!_context.TrySetValue(target.Text, value))
        {
            return Fail($"Unknown identifier '{target.Text}' in expression, line {node.Line}");
        }

        return value;
    }

    private int Fail(string message)
    {
        HasError = true;
        if (ErrorMessage.Length == 0)
        {
            ErrorMessage = message;
        }

        return 0;
    }
}
