using OpenIGI.Scripting.Expressions;
using OpenIGI.Scripting.Runtime;
using Xunit;

namespace OpenIGI.Scripting.Tests;

public sealed class LevelExpressionTests
{
    private static readonly Func<string, int?> NoConstants = _ => null;

    [Theory]
    [InlineData(LevelExpressionDialect.AsWritten)]
    [InlineData(LevelExpressionDialect.Rewritten)]
    public void This_names_the_owner_in_both_dialects(LevelExpressionDialect dialect)
    {
        VariableContext context = new();
        context.Values["Door_4.isOpen"] = QvmValue.FromInt32(7);

        LevelExpression expression = new("this.isOpen + 1", "Door_4", NoConstants, dialect);

        Assert.Equal(8.0, expression.Evaluate(context));
    }

    [Theory]
    [InlineData(LevelExpressionDialect.AsWritten)]
    [InlineData(LevelExpressionDialect.Rewritten)]
    public void An_exists_probe_reads_true_without_a_variable(LevelExpressionDialect dialect)
    {
        LevelExpression expression = new("Door_9.isExists", "Task_1", NoConstants, dialect);

        Assert.Equal(1.0, expression.Evaluate(new VariableContext()));
    }

    [Theory]
    [InlineData(LevelExpressionDialect.AsWritten)]
    [InlineData(LevelExpressionDialect.Rewritten)]
    public void An_assignment_through_this_writes_the_owners_variable(LevelExpressionDialect dialect)
    {
        VariableContext context = new();
        context.Values["Task_1.isRun"] = QvmValue.FromInt32(0);

        _ = new LevelExpression("this.isRun = 1", "Task_1", NoConstants, dialect).EvaluateInt32(context);

        Assert.Equal(1, context.Values["Task_1.isRun"].Integer);
    }

    [Fact]
    public void Only_the_rewrite_compiles_the_text()
    {
        LevelExpression asWritten = new("this.a  +  2 // note", "T", NoConstants, LevelExpressionDialect.AsWritten);
        LevelExpression rewritten = new("this.a  +  2 // note", "T", NoConstants, LevelExpressionDialect.Rewritten);

        Assert.Equal("this.a  +  2 // note", asWritten.Compiled);
        Assert.Equal("T.a+2", rewritten.Compiled);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("(((")]
    public void Nothing_to_run_reads_as_nought(string? source)
    {
        LevelExpression expression = new(source, "T", NoConstants, LevelExpressionDialect.Rewritten);

        Assert.True(expression.IsEmpty);
        Assert.Equal(0.0, expression.Evaluate(new VariableContext()));
    }

    [Fact]
    public void Constants_fold_at_the_parse()
    {
        LevelExpression expression = new("GAME_FREQUENCY * 2", null, name => name == "GAME_FREQUENCY" ? 30 : null, LevelExpressionDialect.AsWritten);

        Assert.Equal(60, expression.EvaluateInt32(new VariableContext()));
    }

    private sealed class VariableContext : IScriptExpressionContext
    {
        public Dictionary<string, QvmValue> Values { get; } = new(StringComparer.Ordinal);

        public bool TryGetValue(string name, out QvmValue value) => Values.TryGetValue(name, out value);

        public QvmValueKind GetValueKind(string name) =>
            Values.TryGetValue(name, out QvmValue value) ? value.Kind : QvmValueKind.None;

        public bool TrySetValue(string name, QvmValue value)
        {
            if (!Values.ContainsKey(name))
            {
                return false;
            }

            Values[name] = value;
            return true;
        }

        public bool TryInvoke(string name, ScriptArguments arguments, out QvmValue result)
        {
            result = QvmValue.Zero;
            return false;
        }
    }
}
