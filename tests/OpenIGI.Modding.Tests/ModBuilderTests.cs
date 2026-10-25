using OpenIGI.Core.Configuration;
using OpenIGI.Core.Content;
using OpenIGI.Core.Titles;
using OpenIGI.Game.Events;
using OpenIGI.Game.Igi2;
using OpenIGI.Game.Igi2.Systems;
using OpenIGI.Modding.Presentation;
using OpenIGI.Modding.Testing;
using OpenIGI.Presentation;
using Xunit;

namespace OpenIGI.Modding.Tests;

/// <summary>A mod's one Configure, and what each of the builder's groups makes of what it says.</summary>
public sealed class ModBuilderTests
{
    [Fact]
    public void A_mods_settings_are_named_after_it_and_declared_once_however_often_it_is_configured()
    {
        Settings mod = new();
        ModHarness harness = ModHarness.Of(mod, "my-look.2");

        harness.Start(new Igi2Runtime());
        harness.Start(new Igi2Runtime());

        Assert.Equal("mod_my_look_2_vignette", mod.Vignette!.Name);
        Assert.Same(mod.Vignette, CVarRegistry.Default.TryGet("mod_my_look_2_vignette", out ICVar? found) ? found : null);
        Assert.True(mod.Vignette.Flags.HasFlag(CVarOptions.Archive));
        Assert.Equal(0.5f, mod.Amount!.Value);
    }

    [Fact]
    public void A_setting_is_named_in_lowercase_and_declared_as_one_kind()
    {
        Assert.Throws<ArgumentException>(() => ModHarness.Of(new Declares(static b => b.Settings.Bool("Loud Steps", false, "")), "naming").Start(new Igi2Runtime()));

        ModHarness.Of(new Declares(static b => b.Settings.Bool("twice", false, "")), "kinds").Start(new Igi2Runtime());
        Assert.Throws<InvalidOperationException>(() =>
            ModHarness.Of(new Declares(static b => b.Settings.Float("twice", 0f, 0f, 1f, "")), "kinds").Start(new Igi2Runtime()));
    }

    [Fact]
    public void A_mods_level_systems_land_where_it_puts_them_and_its_events_reach_its_handler()
    {
        int loaded = 0;
        ModHarness harness = ModHarness.Of(new Declares(b =>
        {
            b.LevelSystems<Igi2LevelBuild>()
                .InsertBefore("Two", "Before", static _ => { })
                .InsertAfter("Two", "After", static _ => { })
                .Replace("Three", static _ => { })
                .Remove("One")
                .Add("Last", static _ => { });
            b.Events.On<LevelLoaded>(level => loaded += level.MissionId);
        }), "places");

        ModSession session = harness.Start(new Igi2Runtime());
        session.Raise(new LevelLoaded(GameTitle.Igi2, 3));

        Assert.Equal(["Before", "Two", "After", "Three", "Last"], session.LevelSystems<Igi2LevelBuild>("One", "Two", "Three"));
        Assert.Equal(3, loaded);
    }

    [Fact]
    public void What_a_mod_draws_is_added_to_its_games_presenters_and_no_others()
    {
        PresenterRegistry? drawnInto = null;
        ModHarness harness = ModHarness.Of(new Declares(b => b.Presenters(presenters => drawnInto = presenters)), "draws", titles: [GameTitle.Igi2.Id]);
        harness.Extend(new Igi2Runtime());

        PresenterRegistry presenters = new();
        Assert.Equal(0, PresentationMods.Apply(harness.Set, GameTitle.Igi1, presenters));
        Assert.Null(drawnInto);
        Assert.Equal(1, PresentationMods.Apply(harness.Set, GameTitle.Igi2, presenters));
        Assert.Same(presenters, drawnInto);
    }

    [Fact]
    public void A_mods_file_is_the_players_copy_beside_it_when_there_is_one()
    {
        string directory = Directory.CreateTempSubdirectory("openigi-mod-config-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, "table.json"), """{ "name": "beside", "count": 2 }""");
            Table? read = null;
            ModHarness.Of(new Declares(b => read = b.ReadConfig("table.json", TableJson.Default.Table)), "config", directory: directory)
                .Start(new Igi2Runtime());

            Assert.Equal(new Table("beside", 2), read);
            Assert.Throws<ModLoadException>(() =>
                ModHarness.Of(new Declares(b => b.ReadConfig("absent.json", TableJson.Default.Table)), "absent", directory: directory).Start(new Igi2Runtime()));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class Settings : IOpenIgiMod
    {
        public BoolCVar? Vignette { get; private set; }

        public NumericCVar<float>? Amount { get; private set; }

        public void Configure(IModBuilder builder)
        {
            Vignette = builder.Settings.Bool("vignette", true, "Darkens the edges.");
            Amount = builder.Settings.Float("vignette_amount", 0.5f, 0f, 1f, "How dark the edges go.");
        }
    }

    private sealed class Declares(Action<IModBuilder> configure) : IOpenIgiMod
    {
        public void Configure(IModBuilder builder) => configure(builder);
    }
}

/// <summary>A file of a mod's own, for the tests.</summary>
/// <param name="Name">A name.</param>
/// <param name="Count">A number.</param>
public sealed record Table(string Name, int Count);

/// <summary>The file's generated JSON shape, as a mod's own would have one.</summary>
[System.Text.Json.Serialization.JsonSourceGenerationOptions(PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.CamelCase)]
[System.Text.Json.Serialization.JsonSerializable(typeof(Table))]
internal sealed partial class TableJson : System.Text.Json.Serialization.JsonSerializerContext;
