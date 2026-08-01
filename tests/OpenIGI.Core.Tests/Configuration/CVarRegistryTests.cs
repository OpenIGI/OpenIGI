using OpenIGI.Core.Configuration;
using Xunit;

namespace OpenIGI.Core.Tests.Configuration;

/// <summary>
/// Pins the registry's contract: unique names, ordered enumeration and the diff the settings writer needs.
/// </summary>
public sealed class CVarRegistryTests
{
    [Fact]
    public void Add_RejectsADuplicateName()
    {
        CVarRegistry registry = new();
        CVar.Int("r_test_dup", 1, registry: registry);

        // The source generator reports this at build time; the runtime check is the backstop for
        // variables registered dynamically by a mod.
        Assert.Throws<ArgumentException>(() => CVar.Int("R_TEST_DUP", 2, registry: registry));
    }

    [Fact]
    public void TryGet_IgnoresCase()
    {
        CVarRegistry registry = new();
        CVar.Bool("r_test_case", true, registry: registry);

        Assert.True(registry.TryGet("R_Test_Case", out ICVar? found));
        Assert.Equal("r_test_case", found.Name);
    }

    [Fact]
    public void Search_MatchesAnywhereInTheNameAndSkipsHidden()
    {
        CVarRegistry registry = new();
        CVar.Int("r_fog_quality", 1, registry: registry);
        CVar.Int("r_shadow_quality", 1, registry: registry);
        CVar.Int("r_secret", 1, flags: CVarOptions.Hidden, registry: registry);

        List<string> names = [.. registry.Search("quality").Select(variable => variable.Name)];

        Assert.Equal(["r_fog_quality", "r_shadow_quality"], names);
        Assert.Empty(registry.Search("secret"));
        Assert.Single(registry.Search("secret", includeHidden: true));
    }

    [Fact]
    public void Modified_ReturnsOnlyArchivedVariablesThatDifferFromTheirDefault()
    {
        CVarRegistry registry = new();
        NumericCVar<int> archived = CVar.Int("r_test_archived", 1, 0, 8, CVarOptions.Archive, registry: registry);
        NumericCVar<int> transient = CVar.Int("r_test_transient", 1, 0, 8, registry: registry);
        CVar.Int("r_test_untouched", 1, 0, 8, CVarOptions.Archive, registry: registry);

        archived.Value = 5;
        transient.Value = 5;

        List<string> names = [.. registry.Modified(CVarOptions.Archive).Select(variable => variable.Name)];

        // The settings file is a diff, not a snapshot: a default that is later re-tuned should reach
        // every player who never overrode it.
        Assert.Equal(["r_test_archived"], names);
    }

    [Fact]
    public void AnyChanged_ReportsChangesToEveryRegisteredVariable()
    {
        CVarRegistry registry = new();
        NumericCVar<int> first = CVar.Int("r_test_a", 0, registry: registry);
        NumericCVar<int> second = CVar.Int("r_test_b", 0, registry: registry);
        List<string> seen = [];
        registry.AnyChanged += (_, e) => seen.Add(e.Variable.Name);

        first.Value = 1;
        second.Value = 2;

        Assert.Equal(["r_test_a", "r_test_b"], seen);
    }

    [Fact]
    public void ResetAll_RestoresEveryDefault()
    {
        CVarRegistry registry = new();
        NumericCVar<int> number = CVar.Int("r_test_number", 3, 0, 9, registry: registry);
        BoolCVar flag = CVar.Bool("r_test_bool", false, registry: registry);
        number.Value = 8;
        flag.Value = true;

        registry.ResetAll();

        Assert.Equal(3, number.Value);
        Assert.False(flag.Value);
    }
}
