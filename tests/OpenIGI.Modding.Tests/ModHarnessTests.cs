using OpenIGI.Core.Titles;
using OpenIGI.Game.Events;
using OpenIGI.Game.Igi1;
using OpenIGI.Game.Igi2;
using OpenIGI.Game.Igi2.Systems;
using OpenIGI.Modding.Testing;
using Xunit;

namespace OpenIGI.Modding.Tests;

/// <summary>The harness a mod's tests are written with, holding a mod as the game configures it.</summary>
public sealed class ModHarnessTests : IDisposable
{
    private readonly string _mods = Path.Combine(Path.GetTempPath(), "openigi-harness-" + Guid.NewGuid().ToString("N"));

    public ModHarnessTests() => Directory.CreateDirectory(_mods);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_mods, recursive: true);
        }
        catch (IOException)
        {
            // The sample's assembly stays mapped while its load context lives; the temp folder is the system's to clear.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void A_package_loaded_through_the_harness_registers_edits_and_listens_as_a_session_has_it()
    {
        // The sample's package as the SDK builds it, its manifest the one written from its project.
        string package = Path.Combine(AppContext.BaseDirectory, "SampleMod");
        foreach (string file in Directory.EnumerateFiles(package, "*", SearchOption.AllDirectories))
        {
            string to = Path.Combine(_mods, "sample", Path.GetRelativePath(package, file));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(file, to);
        }

        ModHarness harness = ModHarness.Load(_mods, "sample");
        ModSession session = harness.Start(new Igi2Runtime());

        Assert.True(session.Loop.Natives.TryResolve("SampleMod_Answer", out _));
        Assert.Equal(
            [Igi2LevelSystems.Flow, "SampleMod.Greeting", Igi2LevelSystems.Ambience],
            session.LevelSystems<Igi2LevelBuild>(Igi2LevelSystems.Flow, Igi2LevelSystems.Ambience));

        session.Raise(new LevelLoaded(GameTitle.Igi2, 1));
        object code = harness.Mod.Code!;
        Assert.Equal(1, code.GetType().GetProperty("LevelsLoaded")!.GetValue(code));
    }

    [Fact]
    public void A_mod_made_by_the_test_is_configured_only_into_the_titles_it_is_for()
    {
        Constant mod = new();
        ModHarness harness = ModHarness.Of(mod, "constant", titles: [GameTitle.Igi1.Id]);

        Assert.True(harness.Start(new Igi1Runtime()).Loop.Natives.TryResolve(Constant.Name, out _));
        Assert.Equal(new Igi2Runtime().Modules.Count, harness.Extend(new Igi2Runtime()).Modules.Count);

        // A host with no edits of the mod's keeps its systems as it lists them.
        Assert.Equal(["One", "Two"], harness.Start(new Igi1Runtime()).LevelSystems<Igi2LevelBuild>("One", "Two"));
    }

    private sealed class Constant : IOpenIgiMod
    {
        public const string Name = "Harness_Constant";

        public void Configure(IModBuilder builder) => builder.AddConstant(Name, 7);
    }
}
