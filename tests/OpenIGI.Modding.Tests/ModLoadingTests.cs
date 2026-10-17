using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Titles;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Titles;
using OpenIGI.Game.Igi2;
using OpenIGI.Game.Igi2.Systems;
using OpenIGI.Game.Level;
using OpenIGI.Modding.Loader;
using OpenIGI.Scripting.Runtime;
using Xunit;

namespace OpenIGI.Modding.Tests;

/// <summary>A mod installed in a directory is found, loaded and configured into the games it is for.</summary>
public sealed class ModLoadingTests : IDisposable
{
    private readonly string _mods = Path.Combine(Path.GetTempPath(), "openigi-mods-" + Guid.NewGuid().ToString("N"));

    public ModLoadingTests() => Directory.CreateDirectory(_mods);

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
    public void The_sample_mod_loads_through_the_real_loader()
    {
        InstallSample("sample", """{ "id": "sample", "name": "Sample", "version": "1.0", "apiVersion": 1, "assembly": "SampleMod.dll" }""");

        ModSet mods = ModLoader.Load(_mods);

        InstalledMod mod = Assert.Single(mods.Mods);
        Assert.Equal("sample", mod.Manifest.Id);
        Assert.Empty(mod.Manifest.Titles);
        Assert.NotNull(mod.Code);
        Assert.Equal("SampleMod.Sample", mod.Code.GetType().FullName);
    }

    [Fact]
    public void A_configured_game_registers_the_mods_modules_after_its_own_and_edits_its_level_systems()
    {
        InstallSample("sample", """{ "id": "sample", "name": "Sample", "version": "1.0", "apiVersion": 1, "assembly": "SampleMod.dll" }""");
        IGameTitleRuntime modded = ModLoader.Load(_mods).Extend(new Igi2Runtime());

        Assert.Equal(new Igi2Runtime().Title, modded.Title);
        Assert.Equal(new Igi2Runtime().Modules, modded.Modules.Take(new Igi2Runtime().Modules.Count));

        // Only the mod's modules: the game's own need its tables.
        NativeRegistry natives = new();
        ServiceCatalog services = new();
        ContentRegistrationContext registration = new(
            new TaskTypeRegistry(), natives, new ScriptVarRegistry(natives), services, NullLogger.Instance);
        foreach (IContentModule module in modded.Modules.Skip(new Igi2Runtime().Modules.Count))
        {
            module.Register(registration);
        }

        Assert.True(natives.TryResolve("SampleMod_Answer", out _));
        Assert.True(services.TryGet(out LevelSystemEdits? edits));
        LevelSystems<Igi2LevelBuild> systems = edits.Apply(Igi2LevelSystems.Create());
        Assert.Equal([Igi2LevelSystems.Flow, "SampleMod.Greeting", Igi2LevelSystems.Ambience], systems.Take(3).Select(static s => s.Name));
    }

    [Fact]
    public void A_mod_for_another_game_leaves_this_one_as_it_ships()
    {
        InstallSample("sample", """{ "id": "sample", "name": "Sample", "version": "1.0", "apiVersion": 1, "titles": ["igi1"], "assembly": "SampleMod.dll" }""");

        ModSet mods = ModLoader.Load(_mods);

        Igi2Runtime runtime = new();

        Assert.Same(runtime, mods.Extend(runtime));
        Assert.True(mods.Mods[0].Manifest.AppliesTo(GameTitle.Igi1));
        Assert.False(mods.Mods[0].Manifest.AppliesTo(GameTitle.Igi2));
    }

    [Fact]
    public void A_mod_that_cannot_load_is_left_out_and_the_rest_are_kept()
    {
        InstallSample("a-good", """{ "id": "good", "name": "Good", "version": "1", "apiVersion": 1, "assembly": "SampleMod.dll" }""");
        Write("b-old-api", """{ "id": "old", "name": "Old", "version": "1", "apiVersion": 0 }""");
        Write("c-no-assembly", """{ "id": "missing", "name": "Missing", "version": "1", "apiVersion": 1, "assembly": "Nothing.dll" }""");
        Write("d-not-json", "not a manifest");
        Write("e-duplicate", """{ "id": "GOOD", "name": "Again", "version": "1", "apiVersion": 1 }""");
        Directory.CreateDirectory(Path.Combine(_mods, "f-no-manifest"));

        ModSet mods = ModLoader.Load(_mods);

        Assert.Equal(["good"], mods.Mods.Select(static m => m.Manifest.Id));
    }

    [Fact]
    public void A_content_only_mod_has_no_code_and_changes_no_runtime()
    {
        Write("textures", """{ "id": "textures", "name": "Textures", "version": "2", "apiVersion": 1, "affectsSimulation": false }""");

        ModSet mods = ModLoader.Load(_mods);

        InstalledMod mod = Assert.Single(mods.Mods);
        Assert.Null(mod.Code);
        Assert.False(mod.Manifest.AffectsSimulation);
        Igi2Runtime runtime = new();
        Assert.Same(runtime, mods.Extend(runtime));
    }

    [Fact]
    public void No_mods_directory_is_no_mods()
    {
        Assert.Empty(ModLoader.Load(Path.Combine(_mods, "absent")).Mods);
    }

    private void InstallSample(string directory, string manifest)
    {
        string target = Write(directory, manifest);
        string built = Path.Combine(AppContext.BaseDirectory, "SampleMod");
        foreach (string file in Directory.EnumerateFiles(built))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }
    }

    private string Write(string directory, string manifest)
    {
        string target = Path.Combine(_mods, directory);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, ModManifest.FileName), manifest);
        return target;
    }
}
