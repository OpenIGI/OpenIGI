using OpenIGI.Core.Configuration;
using Xunit;

namespace OpenIGI.Core.Tests.Configuration;

/// <summary>
/// Pins how a line of console input is resolved into a variable read, a variable write or a command.
/// </summary>
public sealed class ConsoleSystemTests
{
    private readonly CVarRegistry _variables = new();
    private readonly ConsoleSystem _console;
    private readonly RecordingConsoleOutput _output = new();

    /// <summary>Initialises a new instance of the <see cref="ConsoleSystemTests"/> class.</summary>
    public ConsoleSystemTests()
    {
        _console = new ConsoleSystem(_variables);
        _console.AddOutput(_output);
        BuiltInConsoleCommands.Register(_console);
    }

    [Fact]
    public void NamingAVariableWithoutAValuePrintsIt()
    {
        CVar.Int("r_test_quality", 3, 0, 4, help: "Test quality", registry: _variables);

        Assert.True(_console.Execute("r_test_quality"));

        // The line names the value, its range and its help, so one glance answers "what is this?".
        Assert.True(_output.Contains("r_test_quality = 3"));
        Assert.True(_output.Contains("[0 .. 4]"));
        Assert.True(_output.Contains("Test quality"));
    }

    [Fact]
    public void NamingAVariableWithAValueAssignsIt()
    {
        NumericCVar<int> quality = CVar.Int("r_test_quality", 3, 0, 4, registry: _variables);

        Assert.True(_console.Execute("r_test_quality 1"));

        Assert.Equal(1, quality.Value);
    }

    [Fact]
    public void AnUnknownNameIsReportedRatherThanIgnored()
    {
        Assert.False(_console.Execute("r_nonsense 1"));

        Assert.Contains(_output.Errors, line => line.Contains("Unknown command or variable", StringComparison.Ordinal));
    }

    [Fact]
    public void ARangeFailureLeavesTheValueAloneAndExplainsItself()
    {
        NumericCVar<float> density = CVar.Float("r_test_density", 0.5f, 0f, 1f, registry: _variables);

        Assert.False(_console.Execute("r_test_density thick"));

        Assert.Equal(0.5f, density.Value);
        Assert.Contains(_output.Errors, line => line.Contains("0 .. 1", StringComparison.Ordinal));
    }

    [Fact]
    public void AReadOnlyVariableRefusesAssignmentButStillReads()
    {
        CVar.Text("r_renderer", "OpenGL 3.3", CVarOptions.ReadOnly, registry: _variables);

        Assert.False(_console.Execute("r_renderer Vulkan"));
        Assert.True(_console.Execute("r_renderer"));

        Assert.Contains(_output.Errors, line => line.Contains("read-only", StringComparison.Ordinal));
    }

    [Fact]
    public void ACheatVariableNeedsCheatsEnabled()
    {
        NumericCVar<int> godMode = CVar.Int("g_test_god", 0, 0, 1, CVarOptions.Cheat, registry: _variables);

        Assert.False(_console.Execute("g_test_god 1"));
        Assert.Equal(0, godMode.Value);

        _console.CheatsEnabled = true;
        Assert.True(_console.Execute("g_test_god 1"));
        Assert.Equal(1, godMode.Value);
    }

    [Fact]
    public void ARestartOnlyVariableSaysSo()
    {
        CVar.Int("r_test_msaa", 4, 0, 8, CVarOptions.RequiresRestart, registry: _variables);

        Assert.True(_console.Execute("r_test_msaa 8"));

        Assert.Contains(_output.Lines, line => line.Severity == ConsoleSeverity.Notice);
    }

    [Fact]
    public void SeveralStatementsOnOneLineAllRun()
    {
        NumericCVar<int> first = CVar.Int("r_test_a", 0, 0, 9, registry: _variables);
        NumericCVar<int> second = CVar.Int("r_test_b", 0, 0, 9, registry: _variables);

        Assert.True(_console.Execute("r_test_a 4; r_test_b 7"));

        Assert.Equal(4, first.Value);
        Assert.Equal(7, second.Value);
    }

    [Fact]
    public void AnAliasExpandsAndCanTakeArguments()
    {
        NumericCVar<int> quality = CVar.Int("r_fog_quality", 0, 0, 4, registry: _variables);

        _console.Execute("alias fog r_fog_quality");
        Assert.True(_console.Execute("fog 3"));

        Assert.Equal(3, quality.Value);
    }

    [Fact]
    public void AnAliasThatCallsItselfIsStoppedRatherThanOverflowingTheStack()
    {
        _console.Execute("alias loop \"loop\"");

        Assert.False(_console.Execute("loop"));
        Assert.Contains(_output.Errors, line => line.Contains("loop", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ABoundKeyRunsItsCommand()
    {
        NumericCVar<int> fog = CVar.Int("r_fog", 1, 0, 1, registry: _variables);

        _console.Execute("bind f7 \"r_fog 0\"");
        Assert.True(_console.ExecuteBind("f7"));

        Assert.Equal(0, fog.Value);
        Assert.False(_console.ExecuteBind("f8"));
    }

    [Fact]
    public void ToggleFlipsASwitchAndCyclesAList()
    {
        BoolCVar flag = CVar.Bool("r_test_flag", false, registry: _variables);
        EnumCVar<ConsoleSeverity> choice = CVar.Choice("r_test_choice", ConsoleSeverity.Output, registry: _variables);

        _console.Execute("toggle r_test_flag");
        Assert.True(flag.Value);
        _console.Execute("toggle r_test_flag");
        Assert.False(flag.Value);

        _console.Execute("toggle r_test_choice output warning error");
        Assert.Equal(ConsoleSeverity.Warning, choice.Value);
        _console.Execute("toggle r_test_choice output warning error");
        Assert.Equal(ConsoleSeverity.Error, choice.Value);
        _console.Execute("toggle r_test_choice output warning error");
        Assert.Equal(ConsoleSeverity.Output, choice.Value);
    }

    [Fact]
    public void CompletionOffersCommandsVariablesAndAliasesAndTheirCommonPrefix()
    {
        CVar.Int("r_fog_quality", 1, registry: _variables);
        CVar.Int("r_fog_density", 1, registry: _variables);
        CVar.Int("r_shadows", 1, registry: _variables);

        ConsoleCompletion completion = _console.Complete("r_fog");

        Assert.Equal(["r_fog_density", "r_fog_quality"], completion.Candidates);
        Assert.Equal("r_fog_", completion.CommonPrefix);
        Assert.False(completion.IsUnambiguous);
    }

    [Fact]
    public void CompletionStopsOnceAValueIsBeingTyped()
    {
        CVar.Int("r_fog_quality", 1, registry: _variables);

        Assert.False(_console.Complete("r_fog_quality 3").HasCandidates);
    }

    [Fact]
    public void HiddenVariablesAreNotOffered()
    {
        CVar.Int("r_secret", 1, flags: CVarOptions.Hidden, registry: _variables);

        Assert.False(_console.Complete("r_sec").HasCandidates);
        Assert.True(_console.Execute("r_secret"));
    }
}
