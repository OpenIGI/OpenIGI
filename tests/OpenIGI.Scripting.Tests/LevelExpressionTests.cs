using OpenIGI.Scripting.Expressions;
using OpenIGI.Scripting.Runtime;
using Xunit;

namespace OpenIGI.Scripting.Tests;

public sealed class LevelExpressionTests
{
    private static readonly Func<string, int?> NoConstants = _ => null;

    [Theory]
    [InlineData(LevelExpressionDialect.Igi1)]
    [InlineData(LevelExpressionDialect.Igi2)]
    public void This_names_the_owner_in_both_dialects(LevelExpressionDialect dialect)
    {
        VariableContext context = new();
        context.Values["Door_4.isOpen"] = QvmValue.FromInt32(7);

        LevelExpression expression = new("this.isOpen + 1", "Door_4", NoConstants, dialect);

        Assert.Equal(8.0, expression.Evaluate(context));
    }

    [Theory]
    [InlineData(LevelExpressionDialect.Igi1)]
    [InlineData(LevelExpressionDialect.Igi2)]
    public void An_exists_probe_reads_true_without_a_variable(LevelExpressionDialect dialect)
    {
        LevelExpression expression = new("Door_9.isExists", "Task_1", NoConstants, dialect);

        Assert.Equal(1.0, expression.Evaluate(new VariableContext()));
    }

    [Theory]
    [InlineData(LevelExpressionDialect.Igi1)]
    [InlineData(LevelExpressionDialect.Igi2)]
    public void An_assignment_through_this_writes_the_owners_variable(LevelExpressionDialect dialect)
    {
        VariableContext context = new();
        context.Values["Task_1.isRun"] = QvmValue.FromInt32(0);

        _ = new LevelExpression("this.isRun = 1", "Task_1", NoConstants, dialect).EvaluateInt32(context);

        Assert.Equal(1, context.Values["Task_1.isRun"].Integer);
    }

    [Fact]
    public void Only_igi2_cuts_a_comment()
    {
        LevelExpression igi1 = new("this.a  +  2 // note", "T", NoConstants, LevelExpressionDialect.Igi1);
        LevelExpression igi2 = new("this.a  +  2 // note", "T", NoConstants, LevelExpressionDialect.Igi2);

        Assert.Equal("T.a+2//note", igi1.Compiled);
        Assert.Equal("T.a+2", igi2.Compiled);
    }

    [Fact]
    public void Only_igi1_unwraps_a_quoted_string()
    {
        const string Source = """Say ("a\tb\"c\x41")""";

        Assert.Equal("Say(a\tb\"cA)", LevelExpression.Compile(Source, null, LevelExpressionDialect.Igi1));
        Assert.Equal("""Say("a\tb\"c\x41")""", LevelExpression.Compile(Source, null, LevelExpressionDialect.Igi2));
    }

    [Fact]
    public void Igi1_writes_nought_for_a_word_naming_no_task()
    {
        TaskTable tasks = new();
        tasks.Tasks[4] = "door";

        Assert.Equal("1&&0", LevelExpression.Compile("Door_4.isExists && Door_9.isExists", null, LevelExpressionDialect.Igi1, tasks));
        Assert.Equal("0+0", LevelExpression.Compile("Thing_5000.x + Big_99999999999.x", null, LevelExpressionDialect.Igi1, tasks));
        Assert.Equal("1&&1", LevelExpression.Compile("Door_4.isExists && Door_9.isExists", null, LevelExpressionDialect.Igi2, tasks));
    }

    [Fact]
    public void Numbers_and_words_without_an_id_are_left_alone()
    {
        TaskTable tasks = new();

        Assert.Equal("12.5+GAME_FREQUENCY+this", LevelExpression.Compile("12.5 + GAME_FREQUENCY + this", null, LevelExpressionDialect.Igi1, tasks));
    }

    [Fact]
    public void A_task_dying_compiles_the_text_again()
    {
        TaskTable tasks = new();
        tasks.Tasks[4] = "door";
        LevelExpression expression = new("Door_4.isExists", "T", NoConstants, LevelExpressionDialect.Igi1, tasks);
        Assert.Equal(1, expression.EvaluateInt32(new VariableContext()));

        tasks.Dead.Add("door");
        tasks.Tasks.Remove(4);

        Assert.Equal(0, expression.EvaluateInt32(new VariableContext()));
        Assert.Equal("0", expression.Compiled);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("(((")]
    public void Nothing_to_run_reads_as_nought(string? source)
    {
        LevelExpression expression = new(source, "T", NoConstants, LevelExpressionDialect.Igi2);

        Assert.True(expression.IsEmpty);
        Assert.Equal(0.0, expression.Evaluate(new VariableContext()));
    }

    [Fact]
    public void Constants_fold_at_the_parse()
    {
        LevelExpression expression = new("GAME_FREQUENCY * 2", null, name => name == "GAME_FREQUENCY" ? 30 : null, LevelExpressionDialect.Igi1);

        Assert.Equal(60, expression.EvaluateInt32(new VariableContext()));
    }

    private sealed class TaskTable : ILevelTaskTable
    {
        public Dictionary<int, object> Tasks { get; } = [];

        public HashSet<object> Dead { get; } = [];

        public object? FindTask(int id) => Tasks.GetValueOrDefault(id);

        public bool IsLive(object task) => !Dead.Contains(task);
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
