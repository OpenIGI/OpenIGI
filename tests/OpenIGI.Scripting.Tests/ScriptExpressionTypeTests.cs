using OpenIGI.Scripting.Expressions;
using OpenIGI.Scripting.Runtime;
using Xunit;

namespace OpenIGI.Scripting.Tests;

/// <summary>
/// The three evaluators, and who picks between them.
/// </summary>
/// <remarks>
/// The language has one tree and three walks over it — <c>Expr_EvalInt</c> (<c>0x4B9120</c>),
/// <c>Expr_EvalFloat</c> (<c>0x4B9660</c>) and <c>Expr_EvalString</c> (<c>0x4B8CE0</c>) — and the
/// consumer chooses. The retail slider expressions do not work under any single one of them, so the
/// choosing is the behaviour under test here as much as the arithmetic is.
/// </remarks>
public sealed class ScriptExpressionTypeTests
{
    [Fact]
    public void TheSameSourceHasADifferentValueInEachType()
    {
        TypedContext context = new();
        context.Reals["this.vValue"] = 0.75;

        ScriptExpressionEvaluator evaluator = new(context);
        ScriptExpression expression = Parse("this.vValue");

        Assert.Equal(0.75, evaluator.EvaluateReal(expression));

        // The very same text under the integer walk truncates to nothing, which is why
        // Config_SoundOptionsSetMusicVolume has to ask for a float.
        Assert.Equal(0, evaluator.Evaluate(expression));
    }

    [Fact]
    public void TheFloatWalkKeepsFractionsThatTheIntegerWalkTruncates()
    {
        TypedContext context = new();
        context.Reals["this.vValue"] = 0.5;

        ScriptExpressionEvaluator evaluator = new(context);
        ScriptExpression expression = Parse("this.vValue + 0.5");

        // ingamemenu.qvm's gamma slider: Config_GraphicOptionsSetGamma(this.vValue + 0.5).
        Assert.Equal(1.0, evaluator.EvaluateReal(expression));
        Assert.Equal(0, evaluator.Evaluate(expression));
    }

    [Fact]
    public void AnAssignmentPicksItsEvaluatorFromTheTargetsDeclaredType()
    {
        TypedContext context = new();
        context.Reals["SlideBar_950.vValue"] = 0;
        context.Integers["SlideBar_950.nValue"] = 0;

        ScriptExpressionEvaluator evaluator = new(context);

        // One source, two targets, two results: 1/2 is 0.5 into a float and 0 into an integer.
        evaluator.Evaluate(Parse("SlideBar_950.vValue = 1 / 2"));
        evaluator.Evaluate(Parse("SlideBar_950.nValue = 1 / 2"));

        Assert.Equal(0.5, context.Reals["SlideBar_950.vValue"]);
        Assert.Equal(0, context.Integers["SlideBar_950.nValue"]);
    }

    [Fact]
    public void AnIntegerTargetInsideAFloatExpressionStillTruncates()
    {
        TypedContext context = new();
        context.Integers["ControlsMenu_954.nOffset"] = 0;

        ScriptExpressionEvaluator evaluator = new(context);

        // Expr_Assign is reached identically from all three walks and always dispatches on the target.
        double result = evaluator.EvaluateReal(Parse("ControlsMenu_954.nOffset = 7 / 2"));

        Assert.Equal(3, context.Integers["ControlsMenu_954.nOffset"]);
        Assert.Equal(3.0, result);
    }

    [Fact]
    public void TheFloatWalkHasNoBitwiseOperators()
    {
        TypedContext context = new();
        ScriptExpressionEvaluator evaluator = new(context);

        // No case in the original's switch, so each falls through to zero — unlike the integer walk.
        Assert.Equal(0.0, evaluator.EvaluateReal(Parse("6 & 3")));
        Assert.Equal(0.0, evaluator.EvaluateReal(Parse("6 | 3")));
        Assert.Equal(0.0, evaluator.EvaluateReal(Parse("6 << 1")));
        Assert.Equal(2, evaluator.Evaluate(Parse("6 & 3")));
    }

    [Fact]
    public void SubtractionIsTheOneOperatorTheFloatWalkEvaluatesLeftFirst()
    {
        TypedContext context = new();
        ScriptExpressionEvaluator evaluator = new(context);

        evaluator.EvaluateReal(Parse("f(1) + f(2)"));
        Assert.Equal([2, 1], context.CallOrder);

        context.CallOrder.Clear();
        evaluator.EvaluateReal(Parse("f(1) - f(2)"));
        Assert.Equal([1, 2], context.CallOrder);
    }

    [Fact]
    public void DivisionGivesUpBeforeEvaluatingItsNumerator()
    {
        TypedContext context = new();
        ScriptExpressionEvaluator evaluator = new(context);

        // "f" answers 0, so the divisor is zero and the left operand is abandoned unevaluated.
        evaluator.Evaluate(Parse("f(1) / f(2)"));
        Assert.Equal([2], context.CallOrder);

        context.CallOrder.Clear();
        evaluator.EvaluateReal(Parse("f(1) / f(2)"));
        Assert.Equal([2], context.CallOrder);
    }

    [Fact]
    public void TheFloatWalkHandsTheCommaToTheIntegerWalk()
    {
        TypedContext context = new();
        context.Reals["v"] = 0.75;

        ScriptExpressionEvaluator evaluator = new(context);

        // Both sides go through Expr_EvalInt, so the fraction is lost even though the caller asked
        // for a float.
        Assert.Equal(0.0, evaluator.EvaluateReal(Parse("0, v")));
        Assert.Equal(0.75, evaluator.EvaluateReal(Parse("v")));
    }

    [Fact]
    public void TheStringWalkConcatenatesWithPlusAndRejectsNumbers()
    {
        TypedContext context = new();
        context.Texts["InputBox_963.zData"] = "hunter2";

        ScriptExpressionEvaluator evaluator = new(context);

        Assert.Equal("hunter2", evaluator.EvaluateText(Parse("InputBox_963.zData")));
        Assert.Equal("ab", evaluator.EvaluateText(Parse("\"a\" + \"b\"")));

        Assert.Equal(string.Empty, evaluator.EvaluateText(Parse("1")));
        Assert.True(evaluator.HasError);
        Assert.Contains("Numeric member in string expression", evaluator.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void AStringInAnIntegerExpressionIsSilentlyZero()
    {
        TypedContext context = new();
        ScriptExpressionEvaluator evaluator = new(context);

        // The asymmetry with the previous test is the original's: only the string walk complains.
        Assert.Equal(0, evaluator.Evaluate(Parse("\"a\"")));
        Assert.False(evaluator.HasError);
    }

    [Fact]
    public void AStringTargetGetsTheStringWalk()
    {
        TypedContext context = new();
        context.Texts["InputBox_963.zData"] = string.Empty;

        ScriptExpressionEvaluator evaluator = new(context);
        evaluator.Evaluate(Parse("InputBox_963.zData = \"ab\" + \"cd\""));

        Assert.Equal("abcd", context.Texts["InputBox_963.zData"]);
    }

    [Fact]
    public void ArgumentsAreEvaluatedByTheCalleeSoAnUnreadOneNeverRuns()
    {
        TypedContext context = new();
        ScriptExpressionEvaluator evaluator = new(context);

        // "ignore" reads nothing, so neither f(1) nor f(2) is ever evaluated.
        evaluator.Evaluate(Parse("ignore(f(1), f(2))"));

        Assert.Empty(context.CallOrder);
    }

    [Fact]
    public void ACalleeThatReadsItsArgumentsBackwardsRunsTheirEffectsBackwards()
    {
        TypedContext context = new();
        ScriptExpressionEvaluator evaluator = new(context);

        evaluator.Evaluate(Parse("backwards(f(1), f(2))"));

        Assert.Equal([2, 1], context.CallOrder);
    }

    private static ScriptExpression Parse(string source) => ScriptExpressionParser.Parse(source, _ => null);

    /// <summary>A namespace whose names have real declared types.</summary>
    private sealed class TypedContext : IScriptExpressionContext
    {
        public Dictionary<string, int> Integers { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, double> Reals { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, string> Texts { get; } = new(StringComparer.Ordinal);

        public List<int> CallOrder { get; } = [];

        public bool TryGetValue(string name, out QvmValue value)
        {
            if (Integers.TryGetValue(name, out int integer))
            {
                value = QvmValue.FromInt32(integer);
                return true;
            }

            if (Reals.TryGetValue(name, out double real))
            {
                value = QvmValue.FromDouble(real);
                return true;
            }

            if (Texts.TryGetValue(name, out string? text))
            {
                value = QvmValue.FromString(text);
                return true;
            }

            value = QvmValue.Zero;
            return false;
        }

        public QvmValueKind GetValueKind(string name)
        {
            if (Integers.ContainsKey(name))
            {
                return QvmValueKind.Integer;
            }

            if (Reals.ContainsKey(name))
            {
                return QvmValueKind.Float;
            }

            return Texts.ContainsKey(name) ? QvmValueKind.String : QvmValueKind.None;
        }

        public bool TrySetValue(string name, QvmValue value)
        {
            if (Integers.ContainsKey(name))
            {
                Integers[name] = value.ToInt32();
                return true;
            }

            if (Reals.ContainsKey(name))
            {
                Reals[name] = value.ToDouble();
                return true;
            }

            if (Texts.ContainsKey(name))
            {
                Texts[name] = value.ToText();
                return true;
            }

            return false;
        }

        public bool TryInvoke(string name, ScriptArguments arguments, out QvmValue result)
        {
            result = QvmValue.Zero;

            switch (name)
            {
                case "f":
                    CallOrder.Add(arguments.GetInt32(0));
                    return true;

                // Reads nothing: its arguments are never evaluated.
                case "ignore":
                    return true;

                // Reads argument 1 before argument 0.
                case "backwards":
                    arguments.GetInt32(1);
                    arguments.GetInt32(0);
                    return true;

                default:
                    return true;
            }
        }
    }
}
