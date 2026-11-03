using OpenIGI.Editor.Core.Missions;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Titles;
using OpenIGI.Game.Igi1;
using OpenIGI.Game.Igi2;
using OpenIGI.Scripting.Expressions;
using OpenIGI.Scripting.Qsc;
using OpenIGI.Scripting.Vocabulary;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// Each title's script vocabulary against every level expression its retail missions hold: every native they call is
/// declared, and every call fits its signature.
/// </summary>
public sealed class MissionScriptVocabularyTests
{
    // The four retail expressions the game's own parser rejects, which it therefore reads as empty: a missing operand
    // ("> * GAME_FREQUENCY"), a stray "|1", and two with a bracket never closed. The editor keeps them verbatim as raw nodes.
    private static readonly string[] KnownRetailSyntaxErrors =
    [
        "Door_1115.Close door expression", "EditVariable_322.Add", "DefineComputerObjective_3943.Objectives Valid",
        "ConditionalContainer_3238.Condition",
    ];

    /// <summary>Project I.G.I.'s missions.</summary>
    [RequiresTitleDataFact("igi1")]
    public void Every_igi1_expression_call_is_declared_and_fits() =>
        AssertFits(new Igi1Runtime(), GameDataSource.Igi1, LevelExpressionDialect.Igi1, minimumCalls: 100);

    /// <summary>I.G.I. 2's missions.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Every_igi2_expression_call_is_declared_and_fits() =>
        AssertFits(new Igi2Runtime(), GameDataSource.Igi2, LevelExpressionDialect.Igi2, minimumCalls: 1500);

    /// <summary>Each title's vocabulary builds without a name declared twice with different shapes.</summary>
    [Fact]
    public void Both_vocabularies_build()
    {
        Assert.True(new Igi1Runtime().CreateScriptVocabulary().Natives.Count >= 61);
        Assert.True(new Igi2Runtime().CreateScriptVocabulary().Natives.Count >= 140);
    }

    /// <summary>Every task type either title registers can be asked for its fields without a level around it.</summary>
    [Fact]
    public void Every_registered_type_exports_without_a_level()
    {
        foreach (IGameTitleRuntime title in new IGameTitleRuntime[] { new Igi1Runtime(), new Igi2Runtime() })
        {
            TaskFieldRecording recording = TitleScriptVocabulary.RecordTaskFields(title.Modules);
            Assert.True(
                recording.Failures.Count == 0,
                $"{title.Title}: " + string.Join('\n', recording.Failures.Select(static f => $"{f.Key}: {f.Value}")));
        }
    }

    private static void AssertFits(IGameTitleRuntime title, GameDataSource source, LevelExpressionDialect dialect, int minimumCalls)
    {
        ScriptVocabulary vocabulary = title.CreateScriptVocabulary();
        List<string> problems = [];
        int calls = 0;
        List<string> unparsed = [];
        SortedSet<string> missingFields = new(StringComparer.Ordinal);
        foreach (string path in source.EnumerateFiles("objects.qvm"))
        {
            MissionDocument document = MissionDocument.Read(File.ReadAllBytes(path));
            foreach (MissionTask task in document.EnumerateTasks())
            {
                foreach ((string text, string parameter) in Expressions(task))
                {
                    // A wholly commented-out I.G.I. 2 expression compiles to nothing, which the game reads as empty.
                    string compiled = LevelExpression.Compile(text, $"{task.TypeName}_{task.Id}", dialect);
                    if (ScriptExpressionParser.IsBlank(compiled))
                    {
                        continue;
                    }

                    ScriptExpression tree;
                    try
                    {
                        tree = ScriptExpressionParser.Parse(compiled);
                    }
                    catch (ScriptExpressionException error)
                    {
                        if (KnownRetailSyntaxErrors.Contains($"{task.TypeName}_{task.Id}.{parameter}"))
                        {
                            continue;
                        }

                        unparsed.Add($"{Path.GetFileName(Path.GetDirectoryName(path))} {task.TypeName}_{task.Id}.{parameter}: {text.Replace("\n", "\\n", StringComparison.Ordinal)}  ({error.Message})");
                        continue;
                    }

                    foreach (ScriptExpression call in Calls(tree))
                    {
                        calls++;
                        if (!vocabulary.Natives.ContainsKey(call.Text))
                        {
                            problems.Add($"{task.TypeName}.{parameter}: undeclared native {call.Text}");
                        }
                    }

                    problems.AddRange(ScriptCallChecker.Check(tree, vocabulary).Select(p => $"{task.TypeName}.{parameter}: {p.Message}"));
                    foreach ((string type, string field) in FieldReads(tree))
                    {
                        if (!vocabulary.TryGetField(type, field, out _))
                        {
                            missingFields.Add($"{type}.{field}");
                        }
                    }
                }
            }
        }

        Assert.True(unparsed.Count == 0, $"{unparsed.Count} unparsed:\n" + string.Join('\n', unparsed));
        Assert.True(calls >= minimumCalls, $"only {calls} calls in retail expressions");
        Assert.True(missingFields.Count == 0, $"{missingFields.Count} fields read but not exported:\n" + string.Join('\n', missingFields));
        string[] distinct = [.. problems.Distinct(StringComparer.Ordinal)];
        Assert.True(distinct.Length == 0, $"{distinct.Length} problems:\n" + string.Join('\n', distinct.Take(60)));
    }

    // Every expression-valued VarString a task carries, with its parameter's name.
    private static IEnumerable<(string Text, string Parameter)> Expressions(MissionTask task)
    {
        foreach (MissionParameter parameter in task.Parameters)
        {
            if (parameter.Declaration.Type == ScriptParamType.VarString
                && parameter.Arguments is [QscStringLiteral { Value: { } text }]
                && !ScriptExpressionParser.IsBlank(text)
                && TaskPinRoles.IsExpression(TaskPinRoles.RoleOf(task.TypeName, parameter.Name)))
            {
                yield return (text, parameter.Name);
            }
        }
    }

    // Every Type_Id.field an expression reads, as the type's name and the field.
    private static IEnumerable<(string Type, string Field)> FieldReads(ScriptExpression expression)
    {
        if (expression is { Kind: ScriptExpressionKind.Identifier, IsCall: false } && expression.Text.LastIndexOf('.') is var dot and > 0)
        {
            string subject = expression.Text[..dot];
            int bar = subject.LastIndexOf('_');
            if (bar > 0 && int.TryParse(subject[(bar + 1)..], out _))
            {
                yield return (subject[..bar], expression.Text[(dot + 1)..]);
            }
        }

        IEnumerable<ScriptExpression> children =
            new[] { expression.Left, expression.Right, expression.Extra }.OfType<ScriptExpression>().Concat(expression.Arguments);
        foreach (ScriptExpression child in children)
        {
            foreach ((string, string) read in FieldReads(child))
            {
                yield return read;
            }
        }
    }

    // A call to a task's field (GenericTBA_1500.isFinishedThisTick(...), one retail typo) is not a native.
    private static IEnumerable<ScriptExpression> Calls(ScriptExpression expression)
    {
        if (expression.IsCall && !expression.Text.Contains('.', StringComparison.Ordinal))
        {
            yield return expression;
        }

        IEnumerable<ScriptExpression> children =
            new[] { expression.Left, expression.Right, expression.Extra }.OfType<ScriptExpression>().Concat(expression.Arguments);
        foreach (ScriptExpression child in children)
        {
            foreach (ScriptExpression call in Calls(child))
            {
                yield return call;
            }
        }
    }
}
