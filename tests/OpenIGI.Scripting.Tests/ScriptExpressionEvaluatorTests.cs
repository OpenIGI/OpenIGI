using OpenIGI.Scripting.Expressions;
using Xunit;

namespace OpenIGI.Scripting.Tests;

public sealed class ScriptExpressionEvaluatorTests
{
    [Theory]
    [InlineData("1 + 2 * 3", 7)]
    [InlineData("(1 + 2) * 3", 9)]
    [InlineData("10 / 3", 3)]
    [InlineData("-5 / 2", -2)]
    [InlineData("1 << 4", 16)]
    [InlineData("-16 >> 2", -4)]
    [InlineData("0xF0 | 0x0F", 255)]
    [InlineData("0xFF & 0x0F", 15)]
    [InlineData("~0", -1)]
    [InlineData("!0", 1)]
    [InlineData("!5", 0)]
    [InlineData("3 < 4", 1)]
    [InlineData("3 >= 4", 0)]
    [InlineData("1 && 0", 0)]
    [InlineData("0 || 3", 1)]
    [InlineData("1 ? 10 : 20", 10)]
    [InlineData("0 ? 10 : 20", 20)]
    public void ArithmeticIsIntegerThroughout(string source, int expected) =>
        Assert.Equal(expected, Evaluate(source, new RecordingContext()));

    [Theory]
    [InlineData("2.9", 2)]
    [InlineData("-2.9", -2)]
    [InlineData("1.5 + 1.5", 2)]
    public void NumericLiteralsTruncateTowardZeroBeforeAnyArithmetic(string source, int expected)
    {
        // The tokenizer parses doubles but the evaluator is integer-only, so 1.5 + 1.5 is 1 + 1, not 3.
        Assert.Equal(expected, Evaluate(source, new RecordingContext()));
    }

    [Fact]
    public void BinaryOperandsEvaluateRightToLeft()
    {
        // Not a tidy-up candidate: the original computes its right operand first for every arithmetic,
        // bitwise and comparison operator, and retail menu scripts put calls on both sides.
        RecordingContext context = new();
        Evaluate("f(1) + f(2)", context);

        Assert.Equal([2, 1], context.CallOrder);
    }

    [Fact]
    public void TheCommaIsTheExceptionAndEvaluatesLeftFirst()
    {
        RecordingContext context = new();
        context.Returns["f"] = 9;

        int result = Evaluate("f(1), f(2)", context);

        Assert.Equal([1, 2], context.CallOrder);
        Assert.Equal(9, result);
    }

    [Fact]
    public void LogicalOperatorsShortCircuit()
    {
        RecordingContext context = new();
        context.Returns["f"] = 1;

        Assert.Equal(0, Evaluate("0 && f(1)", context));
        Assert.Empty(context.CallOrder);

        Assert.Equal(1, Evaluate("1 || f(2)", context));
        Assert.Empty(context.CallOrder);

        Assert.Equal(1, Evaluate("0 || f(3)", context));
        Assert.Equal([3], context.CallOrder);
    }

    [Fact]
    public void ThereIsNoModuloOperator()
    {
        // The tokenizer has no '%' case at all, so it falls through to the word scanner. Whatever the
        // result, it is not arithmetic — worth pinning so nobody "restores" a modulo that never existed.
        Assert.Throws<ScriptExpressionException>(() => ScriptExpressionParser.Parse("7 % 2"));
    }

    [Fact]
    public void TernaryEvaluatesOnlyTheTakenBranch()
    {
        RecordingContext context = new();
        Evaluate("1 ? f(1) : f(2)", context);

        Assert.Equal([1], context.CallOrder);
    }

    [Fact]
    public void DivisionByZeroYieldsZeroAndRaisesTheErrorFlag()
    {
        // The original defines this: it sets a global error flag and returns zero rather than faulting.
        RecordingContext context = new();
        ScriptExpressionEvaluator evaluator = new(context);

        Assert.Equal(0, evaluator.Evaluate(ScriptExpressionParser.Parse("10 / 0")));
        Assert.True(evaluator.HasError);
    }

    [Fact]
    public void ShiftCountsUseOnlyTheLowByte()
    {
        // The original's shift count is a char.
        Assert.Equal(2, Evaluate("1 << 257", new RecordingContext()));
    }

    [Fact]
    public void NamesAreReadThroughTheContext()
    {
        RecordingContext context = new();
        context.Values["this.nUserData"] = 42;

        Assert.Equal(42, Evaluate("this.nUserData", context));
        Assert.Equal(43, Evaluate("this.nUserData + 1", context));
    }

    [Fact]
    public void AssignmentStoresThroughTheContextAndYieldsTheValue()
    {
        RecordingContext context = new();
        context.Values["this.nUserData"] = 0;

        Assert.Equal(7, Evaluate("this.nUserData = 3 + 4", context));
        Assert.Equal(7, context.Values["this.nUserData"]);
    }

    [Fact]
    public void AssignmentToAnUnknownNameReportsRatherThanThrows()
    {
        RecordingContext context = new();
        ScriptExpressionEvaluator evaluator = new(context);

        Assert.Equal(0, evaluator.Evaluate(ScriptExpressionParser.Parse("nope = 1")));
        Assert.True(evaluator.HasError);
        Assert.Contains("Unknown identifier", evaluator.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownNameIsReportedAndContributesZero()
    {
        // The original reports the name to its console and carries on with zero rather than aborting,
        // so `missing + 1` is 1 and the error flag is what tells the host something was wrong.
        RecordingContext context = new();
        ScriptExpressionEvaluator evaluator = new(context);

        Assert.Equal(1, evaluator.Evaluate(ScriptExpressionParser.Parse("missing + 1")));
        Assert.True(evaluator.HasError);
        Assert.Contains("Unknown identifier", evaluator.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void CallArgumentsArriveInSourceOrder()
    {
        RecordingContext context = new();
        Evaluate("g(1, 2, 3)", context);

        Assert.Equal([1, 2, 3], context.LastArguments);
    }

    [Fact]
    public void TheRetailContentControlActionPicksItsBranch()
    {
        RecordingContext context = new();
        context.Returns["Config_IsContentControlPasswordEnabled"] = 0;

        Evaluate(
            "Config_IsContentControlPasswordEnabled() ? MenuManager_ActivatePopupScreen(932) : MenuManager_PushScreen(915)",
            context);

        Assert.Contains("MenuManager_PushScreen", context.CalledNames);
        Assert.DoesNotContain("MenuManager_ActivatePopupScreen", context.CalledNames);
    }

    [Fact]
    public void TheRetailSoundScreenActionRunsBothCalls()
    {
        RecordingContext context = new();
        context.Returns["MenuManager_RequestScreen"] = 1;
        context.Returns["Game_UpdateVolume"] = 1;

        Assert.Equal(
            1,
            Evaluate("MenuManager_RequestScreen(904, FALSE) && Game_UpdateVolume()", context, Constants));

        Assert.Equal(["MenuManager_RequestScreen", "Game_UpdateVolume"], context.CalledNames);
    }

    [Fact]
    public void TheRetailPerformanceFlagsRoundTripThroughATaskProperty()
    {
        RecordingContext context = new();
        context.Values["this.nUserData"] = 0;
        context.Returns["Config_GraphicOptionsGetPerformanceFlags"] = 0x2A;

        Evaluate("this.nUserData = Config_GraphicOptionsGetPerformanceFlags()", context);
        Assert.Equal(0x2A, context.Values["this.nUserData"]);

        Evaluate("Config_GraphicOptionsSetPerformanceFlags(this.nUserData)", context);
        Assert.Equal([0x2A], context.LastArguments);
    }

    private static int? Constants(string name) => name switch
    {
        "TRUE" => 1,
        "FALSE" => 0,
        _ => null,
    };

    private static int Evaluate(string source, RecordingContext context, Func<string, int?>? constants = null) =>
        new ScriptExpressionEvaluator(context).Evaluate(
            ScriptExpressionParser.Parse(source, constants ?? Constants));

    private sealed class RecordingContext : IScriptExpressionContext
    {
        public Dictionary<string, int> Values { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, int> Returns { get; } = new(StringComparer.Ordinal);

        public List<int> CallOrder { get; } = [];

        public List<string> CalledNames { get; } = [];

        public int[] LastArguments { get; private set; } = [];

        public bool TryGetValue(string name, out int value) => Values.TryGetValue(name, out value);

        public bool TrySetValue(string name, int value)
        {
            if (!Values.ContainsKey(name))
            {
                return false;
            }

            Values[name] = value;
            return true;
        }

        public bool TryInvoke(string name, ReadOnlySpan<int> arguments, out int result)
        {
            CalledNames.Add(name);
            LastArguments = arguments.ToArray();

            // "f" records its first argument so evaluation order is observable.
            if (name == "f" && arguments.Length > 0)
            {
                CallOrder.Add(arguments[0]);
            }

            result = Returns.GetValueOrDefault(name);
            return true;
        }
    }
}
