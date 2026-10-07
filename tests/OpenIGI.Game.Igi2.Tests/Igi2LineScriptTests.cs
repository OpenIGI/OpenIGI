using OpenIGI.Game.Igi2.Scripting;
using OpenIGI.Scripting.Runtime;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Script text a task carries, evaluated as one expression the way <c>Script_ParseLineInt32</c> does.</summary>
public sealed class Igi2LineScriptTests
{
    /// <summary>A line loses everything from <c>//</c>, unless the <c>//</c> is inside quotes.</summary>
    [Fact]
    public void Comments_are_cut_outside_strings_only()
    {
        Assert.Equal("Add(1) \n\"a//b\" \n\n", Igi2LineScript.StripComments("Add(1) // one\r\n\"a//b\" // two\n// three"));
        Assert.Equal("\"a\\\"//b\"\n", Igi2LineScript.StripComments("\"a\\\"//b\""));
    }

    /// <summary>The parser stops at the first <c>;</c>, so a second call after one never runs.</summary>
    [Fact]
    public void Only_the_first_statement_runs()
    {
        (NativeRegistry natives, Igi2ScriptFunctions functions, List<string> added) = Recorder();

        Igi2LineScript.Evaluate("//Normal Guard\nAdd(\"A\");\n\n//Tough Guard\nAdd(\"B\");", natives, functions);

        Assert.Equal(["A"], added);
    }

    /// <summary>Calls joined by <c>,</c> are one expression, and all run in order.</summary>
    [Fact]
    public void Calls_joined_by_commas_all_run()
    {
        (NativeRegistry natives, Igi2ScriptFunctions functions, List<string> added) = Recorder();

        Igi2LineScript.Evaluate("Add(\"A\"),\nAdd(\"B\"),\nAdd(\"C\");", natives, functions);

        Assert.Equal(["A", "B", "C"], added);
    }

    /// <summary>Names resolve to the namespace's constants, and the value of the expression comes back.</summary>
    [Fact]
    public void Constants_resolve_and_the_value_returns()
    {
        NativeRegistry natives = new();
        Igi2ScriptFunctions functions = new();
        natives.RegisterConstant("SEVEN", 7);
        functions.Register(natives, "Sum", static call => QvmValue.FromInt32(call.GetInt32(0) + call.GetInt32(1)), 2);

        Assert.Equal(9, Igi2LineScript.Evaluate("Sum(SEVEN,\n// skipped\n2)", natives, functions));
        Assert.Equal(0, Igi2LineScript.Evaluate("// nothing but a comment", natives, functions));
        Assert.Equal(0, Igi2LineScript.Evaluate("Sum(1, ", natives, functions));
    }

    /// <summary>The object bound to a native's symbol is the call's context, as <c>Script_SetSymbolContext</c> arranges.</summary>
    [Fact]
    public void A_bound_context_reaches_the_call()
    {
        NativeRegistry natives = new();
        Igi2ScriptFunctions functions = new();
        object? seen = null;
        functions.Register(natives, "Probe", call =>
        {
            seen = call.Context;
            return QvmValue.Zero;
        }, 0);
        object owner = new();
        natives.SetSymbolContext("Probe", owner);

        Igi2LineScript.Evaluate("Probe()", natives, functions);

        Assert.Same(owner, seen);
    }

    private static (NativeRegistry Natives, Igi2ScriptFunctions Functions, List<string> Added) Recorder()
    {
        NativeRegistry natives = new();
        Igi2ScriptFunctions functions = new();
        List<string> added = [];
        functions.Register(natives, "Add", call =>
        {
            added.Add(call.GetText(0));
            return QvmValue.FromInt32(1);
        }, 1);
        return (natives, functions, added);
    }
}
