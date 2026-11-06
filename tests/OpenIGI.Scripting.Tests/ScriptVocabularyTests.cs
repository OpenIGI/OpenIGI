using OpenIGI.Scripting.Qsc;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Vocabulary;
using Xunit;

namespace OpenIGI.Scripting.Tests;

/// <summary>The typed vocabulary: which pins connect, and which calls the checker refuses.</summary>
public sealed class ScriptVocabularyTests
{
    private static readonly ScriptVocabulary Vocabulary = new ScriptVocabulary()
        .Add([
            new NativeSignature("PlaySound", [new("Sound", ScriptType.Text), new("Flags", ScriptType.EnumOf("Flag"))], null, NativeEffect.Action, ScriptContexts.Ai),
            new NativeSignature("SetPriority", [new("Event", ScriptType.EnumOf("Event"))], null, NativeEffect.Action, ScriptContexts.Ai),
            new NativeSignature("Distance", [], ScriptType.Real, NativeEffect.Pure, ScriptContexts.Ai),
            new NativeSignature("Define", [new("Name", ScriptType.Text), new("Value", ScriptType.Int)], null, NativeEffect.Action, ScriptContexts.Config) { IsVariadic = true },
        ])
        .Add(new ConstantFamily("Flag", [new("FLAG_NONE", 0), new("FLAG_PUSHABLE", 2)]))
        .Add(new ConstantFamily("Event", [new("EVENT_IDLE", 0), new("EVENT_ALARM", 1)]));

    /// <summary>Calls that fit, including a bare number for a constant family, raise nothing.</summary>
    [Fact]
    public void Fitting_calls_pass()
    {
        Assert.Empty(Check("""PlaySound("alarm", FLAG_PUSHABLE); PlaySound("x", 0); SetPriority(EVENT_ALARM); Define("a", 1, 2, 3);"""));
    }

    /// <summary>Wrong counts, text against number, and a constant of the wrong family are each reported once.</summary>
    [Fact]
    public void Misfits_are_reported()
    {
        IReadOnlyList<ScriptCallProblem> problems = Check("""PlaySound(5, 0); PlaySound("x"); SetPriority(FLAG_NONE); SetPriority(Distance(), 1);""");

        Assert.Equal(
            [("PlaySound", 0), ("PlaySound", -1), ("SetPriority", 0), ("SetPriority", -1)],
            problems.Select(static p => (p.Native, p.Argument)));
    }

    /// <summary>Pins refuse what the game would misread and accept what the VM mixes freely.</summary>
    [Fact]
    public void Pin_connection_rules()
    {
        Assert.True(ScriptType.Int.ConnectsTo(ScriptType.Real));
        Assert.True(ScriptType.TaskRef.ConnectsTo(ScriptType.Int));
        Assert.True(ScriptType.EnumOf("Event").ConnectsTo(ScriptType.Int));
        Assert.False(ScriptType.EnumOf("Event").ConnectsTo(ScriptType.EnumOf("Flag")));
        Assert.False(ScriptType.Text.ConnectsTo(ScriptType.Int));
        Assert.False(ScriptType.Vector3.ConnectsTo(ScriptType.Real));
        Assert.False(ScriptType.Exec.ConnectsTo(ScriptType.Bool));
        Assert.True(ScriptType.Any.ConnectsTo(ScriptType.Text));
    }

    /// <summary>A name declared twice with different pins is a table mistake, caught when the vocabulary is built.</summary>
    [Fact]
    public void Conflicting_declarations_throw()
    {
        ScriptVocabulary vocabulary = new();
        NativeSignature one = new("F", [new("A", ScriptType.Int)], null, NativeEffect.Action, ScriptContexts.Ai);
        vocabulary.Add([one, one with { Doc = "same shape" }]);
        Assert.Throws<InvalidOperationException>(() =>
            vocabulary.Add([new NativeSignature("F", [new("A", ScriptType.Text)], null, NativeEffect.Action, ScriptContexts.Ai)]));
    }

    private static IReadOnlyList<ScriptCallProblem> Check(string source) =>
        ScriptCallChecker.Check(QscParser.ParseScript(source, QvmProgram.IgiVersion), Vocabulary);
}
