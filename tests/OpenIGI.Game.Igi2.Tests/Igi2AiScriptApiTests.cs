using OpenIGI.Game.Igi2.Ai;
using OpenIGI.Game.Igi2.Ai.Runtime;
using OpenIGI.Scripting.Qsc;
using OpenIGI.Scripting.Runtime;
using OpenIGI.Scripting.Vocabulary;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>I.G.I. 2's AI vocabulary against the natives the port registers and every AI script the game ships.</summary>
public sealed class Igi2AiScriptApiTests
{
    /// <summary>
    /// Every soldier and squad native the runtime registers has a signature, and every signature is registered: a native added
    /// without one fails here.
    /// </summary>
    [Fact]
    public void Every_registered_native_has_a_signature()
    {
        NativeRegistry natives = new();
        Igi2AiWorld world = new();
        new Igi2HumanAiNatives(world).Register(natives);
        new Igi2AiSquadNatives(world).Register(natives);

        string[] registered = [.. natives.Names
            .Where(name => natives.TryResolve(name, out QvmSymbol? symbol) && symbol.Kind == QvmSymbolKind.Function)
            .Order(StringComparer.Ordinal)];
        string[] declared = [.. Igi2AiScriptApi.Signatures.Select(static s => s.Name).Order(StringComparer.Ordinal)];

        Assert.Equal(registered, declared);
        Assert.Equal(115, declared.Length);
    }

    /// <summary>Every constant a family holds is one the AI module registers, and no two families share one.</summary>
    [Fact]
    public void Families_cover_the_constants_once()
    {
        ScriptVocabulary vocabulary = Vocabulary();
        int total = Igi2AiConstants.Values.Count + Igi2AiConstants.SquadTypes.Count;
        Assert.Equal(total, vocabulary.Families.Values.Sum(static f => f.Values.Count));
        Assert.True(vocabulary.TryGetConstant("AIType_Offensive", out string? family, out int value));
        Assert.Equal((Igi2AiScriptApi.SquadTypeFamily, 1), (family, value));
    }

    // The seven calls the shipped scripts get wrong, each against the arity the retail registrar declares. Zero-argument
    // natives never look at the count, so an extra argument is ignored and the call still works (PassEventOnToSquad,
    // AISquad_DefaultHandler). LookAtNode and AISquad_Patrol test the count and do nothing, so those two calls are dead in the
    // shipped game. AISquad_MoveToNode with none reads an argument that is not there. The editor reads all seven verbatim and
    // never writes a call like them.
    private static readonly string[] KnownRetailMisfits =
    [
        @"MISSIONS\location2\level1\ai\514.qvm: AIAction_LookAtNode takes 2 argument(s), not 1.",
        @"MISSIONS\location2\LEVEL2\ai\550.qvm: AIFunction_PassEventOnToSquad takes 0 argument(s), not 1.",
        @"MISSIONS\location2\LEVEL2\ai\Squad_320.qvm: AISquad_MoveToNode takes 1 argument(s), not 0.",
        @"MISSIONS\location2\LEVEL2\ai\Squad_320.qvm: AISquad_DefaultHandler takes 0 argument(s), not 1.",
        @"MISSIONS\location2\LEVEL4\ai\523.qvm: AIFunction_PassEventOnToSquad takes 0 argument(s), not 1.",
        @"MISSIONS\location2\LEVEL6\ai\519.qvm: AIFunction_PassEventOnToSquad takes 0 argument(s), not 1.",
        @"MISSIONS\location3\level3\ai\Squad_713.qvm: AISquad_Patrol takes 1 argument(s), not 2.",
    ];

    /// <summary>
    /// Every call every shipped soldier and squad script makes fits its signature (count, and what each argument is), apart
    /// from the seven <see cref="KnownRetailMisfits"/> the shipped data gets wrong.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void Every_retail_call_fits_its_signature()
    {
        ScriptVocabulary vocabulary = Vocabulary();
        string root = GameDataSource.Igi2.RequireRoot();
        string[] scripts =
        [
            .. Directory.EnumerateFiles(root, "*.qvm", SearchOption.AllDirectories)
                .Where(static path => string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), "ai", StringComparison.OrdinalIgnoreCase)),
        ];

        List<string> problems = [];
        int calls = 0;
        foreach (string path in scripts)
        {
            QscScript script = QscDecompiler.Decompile(File.ReadAllBytes(path));
            calls += CountCalls(script, vocabulary);
            problems.AddRange(ScriptCallChecker.Check(script, vocabulary).Select(p => $"{Path.GetRelativePath(root, path)}: {p.Message}"));
        }

        Assert.True(scripts.Length > 1000, $"only found {scripts.Length} AI scripts");
        Assert.True(calls > 8000, $"only {calls} AI calls");
        string[] distinct = [.. problems
            .Select(static p => p.Replace('/', '\\'))
            .Distinct(StringComparer.Ordinal)
            .Except(KnownRetailMisfits, StringComparer.Ordinal)];
        Assert.True(distinct.Length == 0, $"{distinct.Length} misfits:\n" + string.Join("\n", distinct.Take(60)));
    }

    private static ScriptVocabulary Vocabulary()
    {
        ScriptVocabulary vocabulary = new ScriptVocabulary().Add(Igi2AiScriptApi.Signatures);
        foreach (ConstantFamily family in Igi2AiScriptApi.Families)
        {
            vocabulary.Add(family);
        }

        return vocabulary;
    }

    private static int CountCalls(QscScript script, ScriptVocabulary vocabulary)
    {
        int count = 0;
        Stack<QscNode> pending = new(script.Statements);
        while (pending.Count > 0)
        {
            switch (pending.Pop())
            {
                case QscExpressionStatement statement:
                    pending.Push(statement.Expression);
                    break;
                case QscIf branch:
                    pending.Push(branch.Condition);
                    branch.Then.Concat(branch.Else ?? []).ToList().ForEach(pending.Push);
                    break;
                case QscWhile loop:
                    pending.Push(loop.Condition);
                    loop.Body.ToList().ForEach(pending.Push);
                    break;
                case QscCall call:
                    count += vocabulary.Natives.ContainsKey(call.Name) ? 1 : 0;
                    call.Arguments.ToList().ForEach(pending.Push);
                    break;
                case QscBinary binary:
                    pending.Push(binary.Left);
                    pending.Push(binary.Right);
                    break;
                case QscUnary unary:
                    pending.Push(unary.Operand);
                    break;
            }
        }

        return count;
    }
}
