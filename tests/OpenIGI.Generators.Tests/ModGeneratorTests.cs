using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace OpenIGI.Generators.Tests;

/// <summary>A mod's class marked [OpenIgiMod] gets its Configure written from its attributes, or a clear error.</summary>
public sealed class ModGeneratorTests
{
    private const string Header = """
        using OpenIGI.Engine.Tasks;
        using OpenIGI.Game.Events;
        using OpenIGI.Game.Hosting;
        using OpenIGI.Modding;

        namespace MyMod;

        """;

    [Fact]
    public void A_mod_class_gets_a_configure_that_says_what_its_attributes_say()
    {
        (string generated, ImmutableArray<Diagnostic> diagnostics) = Run(Header + """
            [ReplacesTask("HumanSoldier", Title = "igi1")]
            public sealed class Soldier : GameTask;

            [OpenIgiMod]
            public sealed partial class Mod
            {
                [ScriptConstant("MyMod_Answer")]
                public const int Answer = 42;

                [AddsLevelSystem<MissionLevelBuild>(After = "Player")]
                private void Arrive(MissionLevelBuild build) { }

                [OnGameEvent]
                private void Killed(in HumanKilled killed) { }

                [OnModLoaded(Title = "igi2")]
                private static void Loaded(IModContext context) { }
            }
            """);

        Assert.Empty(diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("[assembly: global::OpenIGI.Modding.OpenIgiMod(typeof(global::MyMod.Mod))]", generated);
        Assert.Contains("builder.ReplaceTask(\"HumanSoldier\", static () => new global::MyMod.Soldier());", generated);
        Assert.Contains("builder.AddConstant(\"MyMod_Answer\", Answer);", generated);
        Assert.Contains("systems.InsertAfter(\"Player\", new global::OpenIGI.Game.Level.LevelSystem<global::OpenIGI.Game.Hosting.MissionLevelBuild>(\"Mod.Arrive\", Arrive))", generated);
        Assert.Contains("builder.Listen<global::OpenIGI.Game.Events.HumanKilled>(", generated);
        Assert.Contains("builder.OnLoaded(Loaded);", generated);
        Assert.Contains("\"igi1\"", generated);
        Assert.Contains("\"igi2\"", generated);
        Assert.True(
            generated.IndexOf("ReplaceTask", StringComparison.Ordinal) < generated.IndexOf("AddConstant", StringComparison.Ordinal)
            && generated.IndexOf("AddConstant", StringComparison.Ordinal) < generated.IndexOf("EditLevelSystems", StringComparison.Ordinal),
            "Tasks, then constants, then the members in declaration order.");
    }

    [Fact]
    public void A_mod_class_that_is_not_partial_is_an_error()
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Run(Header + "[OpenIgiMod] public sealed class Mod;");

        Assert.Contains(diagnostics, static d => d.Id == "OIGI101");
    }

    [Fact]
    public void A_level_system_placed_twice_or_not_at_all_is_an_error()
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Run(Header + """
            [OpenIgiMod]
            public sealed partial class Mod
            {
                [AddsLevelSystem<MissionLevelBuild>(Before = "A", After = "B")]
                private static void Twice(MissionLevelBuild build) { }

                [AddsLevelSystem<MissionLevelBuild>]
                private static void Nowhere(MissionLevelBuild build) { }
            }
            """);

        Assert.Equal(2, diagnostics.Count(static d => d.Id == "OIGI104"));
    }

    [Fact]
    public void Members_of_the_wrong_shape_are_errors()
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Run(Header + """
            [AddsTask("Thing")]
            public abstract class Thing : GameTask;

            [OpenIgiMod]
            public sealed partial class Mod
            {
                [ScriptConstant("Text")]
                public const string Text = "no";

                [AddsLevelSystem<MissionLevelBuild>(After = "Player")]
                private static int WrongReturn(MissionLevelBuild build) => 0;

                [OnGameEvent]
                private static void NotAnEvent(int value) { }

                [OnModLoaded]
                private static void NoContext() { }
            }
            """);

        Assert.Contains(diagnostics, static d => d.Id == "OIGI103");
        Assert.Contains(diagnostics, static d => d.Id == "OIGI105");
        Assert.Contains(diagnostics, static d => d.Id == "OIGI106");
        Assert.Contains(diagnostics, static d => d.Id == "OIGI107");
        Assert.Contains(diagnostics, static d => d.Id == "OIGI108");
    }

    [Fact]
    public void A_mod_class_writing_its_own_configure_is_told_where_additions_go()
    {
        (_, ImmutableArray<Diagnostic> diagnostics) = Run(Header + """
            [OpenIgiMod]
            public sealed partial class Mod
            {
                public void Configure(IModBuilder builder) { }
            }
            """);

        Assert.Contains(diagnostics, static d => d.Id == "OIGI102");
    }

    // Runs the generator over one file, against the modding API and the assemblies it names.
    private static (string Generated, ImmutableArray<Diagnostic> Diagnostics) Run(string source)
    {
        // The runtime's own assemblies, as the test host trusts them, and the game's beside the tests.
        string[] platform = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        IEnumerable<MetadataReference> references = platform
            .Where(static path => Path.GetFileName(path).StartsWith("System", StringComparison.Ordinal) || Path.GetFileName(path) is "netstandard.dll" or "mscorlib.dll")
            .Concat(Directory.EnumerateFiles(AppContext.BaseDirectory, "OpenIGI.*.dll"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(static path => MetadataReference.CreateFromFile(path));
        CSharpParseOptions options = new(LanguageVersion.Preview);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "MyMod",
            [CSharpSyntaxTree.ParseText(source, options)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create([new ModGenerator().AsSourceGenerator()], parseOptions: options)
            .RunGeneratorsAndUpdateCompilation(compilation, out Compilation built, out _);
        GeneratorDriverRunResult result = driver.GetRunResult();
        string generated = string.Concat(result.GeneratedTrees.Select(static tree => tree.ToString()));

        // What the generator wrote compiles with what it was written for: only the generator's own errors may remain.
        IEnumerable<Diagnostic> compiled = result.Diagnostics.IsEmpty ? built.GetDiagnostics() : [];
        return (generated, [.. result.Diagnostics, .. compiled.Where(static d => d.Severity == DiagnosticSeverity.Error)]);
    }
}
