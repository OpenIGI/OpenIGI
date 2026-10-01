using OpenIGI.Core.Numerics;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Scripting.Expressions;

/// <summary>
/// Evaluates a parsed expression, in whichever of the language's three types the caller asks for.
/// </summary>
/// <remarks>
/// <para>
/// Transcribed from the original's three walks over the same tree — <c>Expr_EvalInt</c>
/// (<c>0x4B9120</c>), <c>Expr_EvalFloat</c> (<c>0x4B9660</c>) and <c>Expr_EvalString</c>
/// (<c>0x4B8CE0</c>). Which one runs is decided by the consumer, never by the expression: an
/// assignment picks by its target's declared type, and a native picks per argument. The same source
/// therefore has three different values depending on who asks, and <c>this.vValue</c> reading
/// <c>0.75</c> as a float and <c>0</c> as an integer is correct behaviour rather than a rounding bug.
/// </para>
/// <para>
/// Behaviours that are observable and are reproduced deliberately rather than tidied:
/// </para>
/// <list type="number">
/// <item><description>
/// <b>Binary operands evaluate right-to-left</b> in the integer walk, for every arithmetic, bitwise
/// and comparison operator. With a call on each side — which retail menu scripts do have — the calls
/// happen in that order, so this is not free to "correct". The float walk keeps that order except for
/// <c>-</c>, which it alone evaluates left-first.
/// </description></item>
/// <item><description>
/// <b>Division tests the divisor first and gives up before evaluating the numerator</b>, in both
/// numeric walks. <c>f() / 0</c> never calls <c>f</c>.
/// </description></item>
/// <item><description>
/// <b>An assignment evaluates its right-hand side twice.</b> <c>Expr_Assign</c> evaluates it to store
/// it, and then the caller evaluates the same node again to produce the assignment's own value —
/// which is how the walks get a result in <em>their</em> type from a store made in the target's. So
/// <c>this.nUserData = Config_GetFlags()</c> calls the native twice, and that is not a transcription
/// slip.
/// </description></item>
/// <item><description>
/// <b>The comma evaluates left first</b>, then right, and yields the right. In the float walk it runs
/// the <em>integer</em> evaluator on both sides.
/// </description></item>
/// <item><description>
/// <b>Division by zero yields zero</b> and raises the script error flag rather than faulting.
/// </description></item>
/// <item><description>
/// <b>The float walk has no bitwise or shift operators at all</b> — no case in the switch — so each
/// one falls through to zero there.
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

    /// <summary>Evaluates an expression as an integer, clearing the error state first.</summary>
    /// <param name="expression">The expression to evaluate.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="expression"/> is <see langword="null"/>.</exception>
    public int Evaluate(ScriptExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        HasError = false;
        ErrorMessage = string.Empty;
        return EvalInt(expression);
    }

    /// <summary>Evaluates an expression as a <see cref="double"/>, clearing the error state first.</summary>
    /// <param name="expression">The expression to evaluate.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="expression"/> is <see langword="null"/>.</exception>
    public double EvaluateReal(ScriptExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        HasError = false;
        ErrorMessage = string.Empty;
        return EvalReal(expression);
    }

    /// <summary>Evaluates an expression as text, clearing the error state first.</summary>
    /// <param name="expression">The expression to evaluate.</param>
    /// <returns>The result, or an empty string.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="expression"/> is <see langword="null"/>.</exception>
    public string EvaluateText(ScriptExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        HasError = false;
        ErrorMessage = string.Empty;
        return EvalText(expression);
    }

    /// <summary>Evaluates an expression as a condition.</summary>
    /// <param name="expression">The expression to evaluate.</param>
    /// <returns><see langword="true"/> when the result is non-zero.</returns>
    public bool EvaluateCondition(ScriptExpression expression) => Evaluate(expression) != 0;

    /// <summary>Evaluates a sub-expression as an integer without disturbing the error state.</summary>
    /// <param name="expression">The expression to evaluate.</param>
    /// <returns>The result.</returns>
    /// <remarks>This is what a native's <c>Arg_GetInt</c> reaches, mid-evaluation.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="expression"/> is <see langword="null"/>.</exception>
    public int EvaluateInt32(ScriptExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return EvalInt(expression);
    }

    // ---- the integer walk: Expr_EvalInt (0x4B9120) ----

    private int EvalInt(ScriptExpression? node)
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

            ScriptExpressionKind.Group => EvalInt(node.Left),
            ScriptExpressionKind.Identifier => Resolve(node).ToInt32(),
            ScriptExpressionKind.Conditional => EvalInt(node.Left) != 0 ? EvalInt(node.Right) : EvalInt(node.Extra),
            ScriptExpressionKind.Operator => EvalIntOperator(node),
            _ => 0,
        };
    }

    private int EvalIntOperator(ScriptExpression node)
    {
        // Unary operators take their operand from the right child, as they do in the original.
        switch (node.Operator)
        {
            case ScriptOperator.UnaryPlus:
                return EvalInt(node.Right);
            case ScriptOperator.UnaryMinus:
                return -EvalInt(node.Right);
            case ScriptOperator.BitwiseNot:
                return ~EvalInt(node.Right);
            case ScriptOperator.Not:
                return EvalInt(node.Right) == 0 ? 1 : 0;

            // Short-circuiting operators must not pre-evaluate, so they come before the general path.
            case ScriptOperator.LogicalAnd:
                return EvalInt(node.Left) != 0 && EvalInt(node.Right) != 0 ? 1 : 0;
            case ScriptOperator.LogicalOr:
                return EvalInt(node.Left) != 0 || EvalInt(node.Right) != 0 ? 1 : 0;

            // The comma is the one operator that evaluates left first.
            case ScriptOperator.Comma:
                EvalInt(node.Left);
                return EvalInt(node.Right);

            // The assignment stores, and then the caller evaluates the right-hand side AGAIN for its
            // own result rather than reusing what was stored. A call there therefore runs twice.
            case ScriptOperator.Assign:
                return Assign(node) ? EvalInt(node.Right) : 0;

            // Division tests the divisor before it touches the numerator, so a zero divisor means the
            // left operand is never evaluated at all — a call there does not run.
            case ScriptOperator.Divide:
            {
                int divisor = EvalInt(node.Right);
                return divisor != 0 ? EvalInt(node.Left) / divisor : FailInt("Division by zero in expression");
            }
        }

        // Everything else: right operand first, then left. Deliberate — see the remarks.
        int right = EvalInt(node.Right);
        int left = EvalInt(node.Left);

        return node.Operator switch
        {
            ScriptOperator.Add => left + right,
            ScriptOperator.Subtract => left - right,
            ScriptOperator.Multiply => left * right,
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

    // ---- the float walk: Expr_EvalFloat (0x4B9660) ----

    private double EvalReal(ScriptExpression? node)
    {
        if (node is null)
        {
            return 0.0;
        }

        return node.Kind switch
        {
            ScriptExpressionKind.Constant => node.Constant,
            ScriptExpressionKind.Number => node.Number,
            ScriptExpressionKind.String => 0.0,
            ScriptExpressionKind.Group => EvalReal(node.Left),
            ScriptExpressionKind.Identifier => Resolve(node).ToDouble(),

            // The condition stays in float and is truncated to test it.
            ScriptExpressionKind.Conditional =>
                IgiMath.TruncToInt32(EvalReal(node.Left)) != 0 ? EvalReal(node.Right) : EvalReal(node.Extra),

            ScriptExpressionKind.Operator => EvalRealOperator(node),
            _ => 0.0,
        };
    }

    private double EvalRealOperator(ScriptExpression node)
    {
        switch (node.Operator)
        {
            case ScriptOperator.UnaryPlus:
                return EvalReal(node.Right);
            case ScriptOperator.UnaryMinus:
                return -EvalReal(node.Right);
            case ScriptOperator.Not:
                return EvalReal(node.Right) == 0.0 ? 1.0 : 0.0;

            case ScriptOperator.LogicalAnd:
                return EvalReal(node.Left) != 0.0 && EvalReal(node.Right) != 0.0 ? 1.0 : 0.0;
            case ScriptOperator.LogicalOr:
                return EvalReal(node.Left) != 0.0 || EvalReal(node.Right) != 0.0 ? 1.0 : 0.0;

            // The float walk hands the comma to the integer walk, both sides.
            case ScriptOperator.Comma:
                EvalInt(node.Left);
                return EvalInt(node.Right);

            case ScriptOperator.Assign:
                return Assign(node) ? EvalReal(node.Right) : 0.0;

            // Subtraction is the one operator the float walk evaluates left-first.
            case ScriptOperator.Subtract:
            {
                double leftFirst = EvalReal(node.Left);
                return leftFirst - EvalReal(node.Right);
            }

            // As in the integer walk, a zero divisor means the left operand never runs.
            case ScriptOperator.Divide:
            {
                double divisor = EvalReal(node.Right);
                return divisor != 0.0 ? EvalReal(node.Left) / divisor : FailReal("Division by zero in expression");
            }
        }

        double right = EvalReal(node.Right);
        double left = EvalReal(node.Left);

        return node.Operator switch
        {
            ScriptOperator.Add => left + right,
            ScriptOperator.Multiply => left * right,
            ScriptOperator.Equal => left == right ? 1.0 : 0.0,
            ScriptOperator.NotEqual => left != right ? 1.0 : 0.0,
            ScriptOperator.Less => left < right ? 1.0 : 0.0,
            ScriptOperator.Greater => left > right ? 1.0 : 0.0,
            ScriptOperator.LessOrEqual => left <= right ? 1.0 : 0.0,
            ScriptOperator.GreaterOrEqual => left >= right ? 1.0 : 0.0,

            // The bitwise and shift operators have no case at all in the original's float switch, so
            // they fall through to zero. Nothing retail reaches this, but it is defined.
            _ => 0.0,
        };
    }

    // ---- the string walk: Expr_EvalString (0x4B8CE0) ----

    private string EvalText(ScriptExpression? node)
    {
        if (node is null)
        {
            return string.Empty;
        }

        switch (node.Kind)
        {
            case ScriptExpressionKind.String:
                return node.Text;

            case ScriptExpressionKind.Group:
                return EvalText(node.Left);

            case ScriptExpressionKind.Identifier:
                return Resolve(node).ToText();

            // A number in a string expression is an error, where a string in an integer expression is
            // silently zero. The asymmetry is the original's.
            case ScriptExpressionKind.Number:
            case ScriptExpressionKind.Constant:
                return FailText($"Numeric member in string expression. Line {node.Line}.");

            case ScriptExpressionKind.Operator:
                switch (node.Operator)
                {
                    case ScriptOperator.Add:
                        return EvalText(node.Left) + EvalText(node.Right);

                    case ScriptOperator.Comma:
                        EvalText(node.Left);
                        return EvalText(node.Right);

                    case ScriptOperator.Assign:
                        return Assign(node) ? EvalText(node.Right) : string.Empty;
                }

                break;
        }

        return FailText($"Syntax error in string expression, line {node.Line}");
    }

    // ---- shared: name resolution and assignment ----

    private QvmValue Resolve(ScriptExpression node)
    {
        if (node.IsCall && _context.TryInvoke(node.Text, new ScriptArguments(this, node.Arguments), out QvmValue result))
        {
            return result;
        }

        if (!node.IsCall && _context.TryGetValue(node.Text, out QvmValue value))
        {
            return value;
        }

        Fail($"Unknown identifier '{node.Text}' in expression, line {node.Line}");
        return QvmValue.FromInt32(0);
    }

    /// <summary>Assigns, running the evaluator the <em>target's</em> declared type names.</summary>
    /// <remarks>This is <c>Expr_Assign</c> (<c>0x4B9040</c>), reached identically from all three walks.</remarks>
    private bool Assign(ScriptExpression node)
    {
        ScriptExpression? target = node.Left;
        if (target is null || !target.IsAssignable)
        {
            Fail($"Expression: Cannot assign to constant, line {node.Line}");
            return false;
        }

        QvmValue value = _context.GetValueKind(target.Text) switch
        {
            QvmValueKind.Float => QvmValue.FromDouble(EvalReal(node.Right)),
            QvmValueKind.String => QvmValue.FromString(EvalText(node.Right)),
            _ => QvmValue.FromInt32(EvalInt(node.Right)),
        };

        if (_context.TrySetValue(target.Text, value))
        {
            return true;
        }

        Fail($"Unknown identifier '{target.Text}' in expression, line {node.Line}");
        return false;
    }

    private int FailInt(string message)
    {
        Fail(message);
        return 0;
    }

    private double FailReal(string message)
    {
        Fail(message);
        return 0.0;
    }

    private string FailText(string message)
    {
        Fail(message);
        return string.Empty;
    }

    private void Fail(string message)
    {
        HasError = true;
        if (ErrorMessage.Length == 0)
        {
            ErrorMessage = message;
        }
    }
}
