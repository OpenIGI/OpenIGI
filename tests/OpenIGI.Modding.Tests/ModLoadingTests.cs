using OpenIGI.Core.Content;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Titles;
using OpenIGI.Engine.Events;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Titles;
using OpenIGI.Game.Events;
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
        Assert.Equal([Igi2LevelSystems.Services, Igi2LevelSystems.Flow, "SampleMod.Greeting", Igi2LevelSystems.Ambience], systems.Take(4).Select(static s => s.Name));
    }

    [Fact]
    public void A_mod_hears_the_events_it_subscribes_to()
    {
        InstallSample("sample", """{ "id": "sample", "name": "Sample", "version": "1.0", "apiVersion": 1, "assembly": "SampleMod.dll" }""");
        ModSet mods = ModLoader.Load(_mods);
        IGameTitleRuntime modded = mods.Extend(new Igi2Runtime());
        NativeRegistry natives = new();
        ServiceCatalog services = new();
        ContentRegistrationContext registration = new(
            new TaskTypeRegistry(), natives, new ScriptVarRegistry(natives), services, NullLogger.Instance);
        foreach (IContentModule module in modded.Modules.Skip(new Igi2Runtime().Modules.Count))
        {
            module.Register(registration);
        }

        Assert.True(services.TryGet(out GameEvents? events));
        events.Raise(new LevelLoaded(GameTitle.Igi2, 1));
        events.Raise(new LevelLoaded(GameTitle.Igi2, 2));

        object code = Assert.Single(mods.Mods).Code!;
        Assert.Equal(2, code.GetType().GetProperty("LevelsLoaded")!.GetValue(code));
    }

    [Fact]
    public void A_mod_is_given_the_session_once_its_tables_are_loaded()
    {
        LoadedMod loaded = new();
        ModSet mods = new([new InstalledMod(new ModManifest("loaded", "Loaded", "1.0", ModApi.Version, []), "mod-dir", loaded)]);
        IContentModule module = mods.Extend(new Igi2Runtime()).Modules.Single(static m => m.Name == "Mod.loaded");
        Engine.Vfs.VirtualFileSystem fileSystem = new();
        ServiceCatalog services = new();
        services.Provide(new GameEvents());
        NativeRegistry natives = new();
        module.LoadTables(new ContentLoadContext(
            fileSystem, new Engine.Resources.ResourceRegistry(fileSystem), natives, new TaskTree(new TaskTypeRegistry()), services, NullLogger.Instance));

        Assert.NotNull(loaded.Context);
        Assert.Equal("mod-dir", loaded.Context.Directory);
        Assert.Same(fileSystem, loaded.Context.FileSystem);
        Assert.Same(services.Require<GameEvents>(), loaded.Context.Events);
    }

    [Fact]
    public void A_mods_own_task_type_is_registered_under_its_name()
    {
        ModSet mods = new([new InstalledMod(new ModManifest("adds", "Adds", "1.0", ModApi.Version, []), string.Empty, new AddsTaskMod())]);
        TaskTypeRegistry types = new();
        NativeRegistry natives = new();
        ContentRegistrationContext registration = new(types, natives, new ScriptVarRegistry(natives), new ServiceCatalog(), NullLogger.Instance);
        mods.Extend(new Igi2Runtime()).Modules.Single(static m => m.Name == "Mod.adds").Register(registration);

        TaskTree tree = new(types);
        Assert.IsType<StandInSoldier>(tree.New(tree.Root, types.Get("ModSoldier")));
    }

    [Fact]
    public void A_mods_task_replacement_takes_the_games_type_once_the_game_has_registered_it()
    {
        Adapter adapter = new();
        ModSet mods = new([new InstalledMod(new ModManifest("standin", "Stand-in", "1.0", ModApi.Version, []), string.Empty, new StandInMod(adapter))]);
        IGameTitleRuntime modded = mods.Extend(new Igi2Runtime());

        NativeRegistry natives = new();
        TaskTypeRegistry types = new();
        TaskType soldier = types.Register<GameSoldier>("HumanSoldier");
        ContentRegistrationContext registration = new(types, natives, new ScriptVarRegistry(natives), new ServiceCatalog(), NullLogger.Instance);
        foreach (IContentModule module in modded.Modules.Skip(new Igi2Runtime().Modules.Count))
        {
            module.Register(registration);
        }

        Assert.Same(adapter, soldier.ParameterAdapter);
        TaskTree tree = new(types);
        Assert.IsType<StandInSoldier>(tree.New(tree.Root, soldier));
    }

    [Fact]
    public void A_mod_mounts_another_games_installed_data_and_goes_without_when_it_is_absent()
    {
        string installed = Write("igi2-install", "{}");
        File.WriteAllText(Path.Combine(installed, GameTitle.Igi2.SignatureFile), string.Empty);
        File.WriteAllText(Path.Combine(installed, "probe.txt"), "from the other game");
        ModSet mods = new([new InstalledMod(new ModManifest("mount", "Mount", "1.0", ModApi.Version, []), string.Empty, new MountMod())]);
        IContentModule mount = mods.Extend(new Igi2Runtime()).Modules.Single(static m => m.Name == "Mod.mount");

        Engine.Vfs.VirtualFileSystem found = Load(mount, new Locator(installed));
        Engine.Vfs.VirtualFileSystem missing = Load(mount, new Locator(null));

        Assert.True(found.TryGetDevice("OTHER", out Engine.Vfs.IFileDevice? device));
        Assert.True(device.Exists("probe.txt"));
        Assert.False(missing.TryGetDevice("OTHER", out _));

        static Engine.Vfs.VirtualFileSystem Load(IContentModule module, ITitleDataLocator locator)
        {
            Engine.Vfs.VirtualFileSystem fileSystem = new();
            ServiceCatalog services = new();
            services.Provide(locator);
            NativeRegistry natives = new();
            module.LoadTables(new ContentLoadContext(
                fileSystem, new Engine.Resources.ResourceRegistry(fileSystem), natives, new TaskTree(new TaskTypeRegistry()), services, NullLogger.Instance));
            return fileSystem;
        }
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

        // An assembly with no public class that is a mod: there is nothing to make.
        string noMod = Write("g-no-mod-class", """{ "id": "nomod", "name": "No mod", "version": "1", "apiVersion": 1, "assembly": "xunit.assert.dll" }""");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "xunit.assert.dll"), Path.Combine(noMod, "xunit.assert.dll"));

        ModSet mods = ModLoader.Load(_mods);

        Assert.Equal(["good"], mods.Mods.Select(static m => m.Manifest.Id));
    }

    [Fact]
    public void A_content_only_mod_without_data_has_no_code_and_changes_no_runtime()
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
    public void A_mods_campaigns_are_the_runtimes_beside_the_games_own()
    {
        Write("nightfall", """{ "id": "nightfall", "name": "Nightfall", "version": "1", "apiVersion": 1, "campaigns": [ { "name": "Nightfall", "missions": [ 901, 902 ] }, { "name": "", "missions": [ 1 ] } ] }""");

        IGameTitleRuntime modded = ModLoader.Load(_mods).Extend(new Igi2Runtime());

        TitleCampaign campaign = Assert.Single(modded.Campaigns);
        Assert.Equal("Nightfall", campaign.Name);
        Assert.Equal([901, 902], campaign.Missions);
    }

    [Fact]
    public void A_content_only_mods_data_stands_in_for_the_games_files_the_later_mod_first()
    {
        string earlier = Write("earlier", """{ "id": "earlier", "name": "Earlier", "version": "1", "apiVersion": 1 }""");
        string later = Write("later", """{ "id": "later", "name": "Later", "version": "1", "apiVersion": 1 }""");
        Directory.CreateDirectory(Path.Combine(earlier, ModSet.DataDirectoryName));
        Directory.CreateDirectory(Path.Combine(later, ModSet.DataDirectoryName));
        ModSet mods = new(ModLoader.Load(_mods).Mods.OrderBy(static m => m.Manifest.Id == "later"));
        Igi2Runtime runtime = new();

        IGameTitleRuntime modded = mods.Extend(runtime);

        Assert.Equal(runtime.Modules, modded.Modules);
        Assert.Equal(["mod-later", "mod-earlier"], modded.ContentFolders.Select(static f => f.Id));
        Assert.Equal(Path.Combine(later, ModSet.DataDirectoryName), modded.ContentFolders[0].Root);
    }

    [Fact]
    public void No_mods_directory_is_no_mods()
    {
        Assert.Empty(ModLoader.Load(Path.Combine(_mods, "absent")).Mods);
    }

    [Fact]
    public void A_profile_plays_each_games_switched_on_mods_in_its_order_loading_a_shared_one_once()
    {
        string sample = InstallSample("sample", """{ "id": "sample", "name": "Sample", "version": "1.0", "apiVersion": 1, "assembly": "SampleMod.dll" }""");
        string first = Write("zz-first", """{ "id": "first", "version": "1", "apiVersion": 1 }""");
        string off = Write("off", """{ "id": "off", "version": "1", "apiVersion": 1 }""");
        Core.Content.ModProfile profile = Core.Content.ModProfile.Parse(Core.Content.ModProfile.Serialize(new(
            Core.Content.ModProfile.CurrentSchema,
            new Dictionary<string, IReadOnlyList<Core.Content.ModProfileEntry>>
            {
                ["igi1"] = [new("first", first, true), new("sample", sample, true), new("off", off, false)],
                ["igi2"] = [new("sample", sample, true)],
            })));

        IReadOnlyDictionary<string, ModSet> sets = ModLoader.Load(profile, ModEnvironment.Any);

        Assert.Equal(["first", "sample"], sets["igi1"].Mods.Select(static m => m.Manifest.Id));
        Assert.Same(sets["igi1"].Mods[1], Assert.Single(sets["igi2"].Mods));
    }

    [Fact]
    public void A_profile_entry_whose_manifest_names_another_mod_is_left_out()
    {
        string folder = Write("renamed", """{ "id": "renamed", "version": "1", "apiVersion": 1 }""");
        Core.Content.ModProfile profile = new(
            Core.Content.ModProfile.CurrentSchema,
            new Dictionary<string, IReadOnlyList<Core.Content.ModProfileEntry>> { ["igi1"] = [new("listed", folder, true)] });

        Assert.Empty(ModLoader.Load(profile, ModEnvironment.Any)["igi1"].Mods);
    }

    [Fact]
    public void A_mod_reading_a_game_that_is_not_installed_is_left_out()
    {
        Write("a", """{ "id": "reads-igi2", "version": "1", "apiVersion": 1, "requiresTitleData": ["igi2"] }""");
        Write("b", """{ "id": "plain", "version": "1", "apiVersion": 1 }""");

        Assert.Equal(["plain"], ModLoader.Load(_mods, new ModEnvironment(static id => id != "igi2")).Mods.Select(static m => m.Manifest.Id));
        Assert.Equal(2, ModLoader.Load(_mods, ModEnvironment.Any).Mods.Count);
    }

    [Fact]
    public void A_mod_whose_dependency_is_missing_or_too_old_is_left_out_and_so_are_the_mods_that_need_it()
    {
        Write("a-library", """{ "id": "library", "version": "1.5", "apiVersion": 1 }""");
        Write("b-new", """{ "id": "needs-new", "version": "1", "apiVersion": 1, "dependencies": { "library": ">=2.0" } }""");
        Write("c-old", """{ "id": "needs-old", "version": "1", "apiVersion": 1, "dependencies": { "library": "1.0" } }""");
        Write("d-chain", """{ "id": "needs-needs-new", "version": "1", "apiVersion": 1, "dependencies": { "needs-new": "*" } }""");
        Write("e-absent", """{ "id": "needs-absent", "version": "1", "apiVersion": 1, "dependencies": { "absent": "*" } }""");

        Assert.Equal(["library", "needs-old"], ModLoader.Load(_mods).Mods.Select(static m => m.Manifest.Id));
    }

    [Fact]
    public void Of_two_mods_in_conflict_the_later_is_left_out_whichever_names_the_other()
    {
        Write("a", """{ "id": "one", "version": "1", "apiVersion": 1 }""");
        Write("b", """{ "id": "two", "version": "1", "apiVersion": 1, "conflicts": ["one"] }""");
        Write("c", """{ "id": "three", "version": "1", "apiVersion": 1, "conflicts": ["four"] }""");
        Write("d", """{ "id": "four", "version": "1", "apiVersion": 1 }""");

        Assert.Equal(["one", "three"], ModLoader.Load(_mods).Mods.Select(static m => m.Manifest.Id));
    }

    [Fact]
    public void A_mod_is_told_its_own_directory()
    {
        DirectoryMod mod = new();
        ModSet mods = new([new InstalledMod(new ModManifest("dir", "Dir", "1", ModApi.Version, []), _mods, mod)]);

        mods.Extend(new Igi2Runtime());

        Assert.Equal(_mods, mod.Directory);
    }

    [Fact]
    public void The_sample_mods_package_holds_its_own_files_alone_and_loads_as_built()
    {
        string package = Path.Combine(AppContext.BaseDirectory, "SampleMod");

        Assert.Equal(
            [
                "Greyscale.default.compute.dxil",
                "Greyscale.default.reflection.json",
                "Greyscale.default.spv",
                "Greyscale.default.wgsl",
                "SampleMod.deps.json",
                "SampleMod.dll",
                "SampleMod.pdb",
                ModManifest.FileName,
            ],
            Directory.EnumerateFiles(package, "*", SearchOption.AllDirectories).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.True(File.Exists(Path.Combine(package, ModSet.ShadersDirectoryName, "Greyscale.default.spv")));
        CopyDirectory(package, Path.Combine(_mods, "sample"));

        ModSet mods = ModLoader.Load(_mods);
        InstalledMod mod = Assert.Single(mods.Mods);
        Assert.Equal("sample", mod.Manifest.Id);
        Assert.Empty(mod.Manifest.Titles);
        Assert.NotNull(mod.Code);

        // Its pass, with the settings the manifest names, read from the folder the package holds its shaders in.
        TitlePostPass pass = Assert.Single(mods.Extend(new Igi2Runtime()).PostPasses);
        Assert.Equal("sample.Greyscale", pass.Name);
        Assert.Equal("mod_sample_greyscale", pass.Toggle);
        Assert.Equal(["mod_sample_greyscale_amount"], pass.Parameters);
        Assert.Equal(Core.Graphics.PostPassStage.Scene, pass.Stage);
        Assert.True(OpenIGI.Shaders.ExternalShaderModule.Read(pass.ShadersDirectory, pass.Module).Compiles(OpenIGI.Shaders.ShaderTarget.Spirv));
    }

    // The sample's package under another manifest.
    private string InstallSample(string directory, string manifest)
    {
        string target = Path.Combine(_mods, directory);
        CopyDirectory(Path.Combine(AppContext.BaseDirectory, "SampleMod"), target);
        File.WriteAllText(Path.Combine(target, ModManifest.FileName), manifest);
        return target;
    }

    private static void CopyDirectory(string source, string target)
    {
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string to = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(file, to);
        }
    }

    private sealed class DirectoryMod : IOpenIgiMod
    {
        public string? Directory { get; private set; }

        public void Configure(IModBuilder builder) => Directory = builder.Directory;
    }

    private sealed class MountMod : IOpenIgiMod
    {
        public void Configure(IModBuilder builder) => builder.MountTitleData(GameTitle.Igi2, "OTHER:");
    }

    private sealed class Locator(string? root) : ITitleDataLocator
    {
        public bool TryGetDataRoot(GameTitle title, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? dataRoot)
        {
            dataRoot = title == GameTitle.Igi2 ? root : null;
            return dataRoot is not null;
        }
    }

    private sealed class GameSoldier : GameTask
    {
    }

    private sealed class StandInSoldier : GameTask
    {
    }

    private sealed class Adapter : ITaskParameterAdapter
    {
        public void Apply(TaskParameterValues values, GameTask task)
        {
        }
    }

    private sealed class StandInMod(ITaskParameterAdapter adapter) : IOpenIgiMod
    {
        public void Configure(IModBuilder builder) => builder.Tasks.Replace<StandInSoldier>("HumanSoldier", adapter);
    }

    private sealed class LoadedMod : IOpenIgiMod
    {
        public IModContext? Context { get; private set; }

        public void Configure(IModBuilder builder) => builder.OnStarted(context => Context = context);
    }

    private sealed class AddsTaskMod : IOpenIgiMod
    {
        public void Configure(IModBuilder builder) => builder.Tasks.Add<StandInSoldier>("ModSoldier");
    }

    private string Write(string directory, string manifest)
    {
        string target = Path.Combine(_mods, directory);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, ModManifest.FileName), manifest);
        return target;
    }
}
