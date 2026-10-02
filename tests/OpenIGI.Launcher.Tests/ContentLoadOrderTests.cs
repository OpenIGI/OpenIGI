using OpenIGI.Core.Content;
using OpenIGI.Launcher.Services;
using Xunit;

namespace OpenIGI.Launcher.Tests;

/// <summary>Pins the per-game content load order: what is in it, where new packs go, and the migration.</summary>
public sealed class ContentLoadOrderTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("openigi-load-order").FullName;

    [Fact]
    public void The_old_switches_become_an_order_with_local_on_top_and_their_on_off_kept()
    {
        List<ContentOrderEntry> order = [];
        Dictionary<string, bool> legacy = new() { ["alpha"] = false, ["beta"] = true };

        Assert.True(ContentLoadOrder.Reconcile(order, ["alpha", "beta", "gamma"], legacy, prune: true));

        Assert.Equal(
            [new(ContentLayerSpec.LocalId, true), new("alpha", false), new("beta", true), new("gamma", true)],
            order);
        Assert.False(ContentLoadOrder.Reconcile(order, ["alpha", "beta", "gamma"], legacy, prune: true));
    }

    [Fact]
    public void A_new_pack_goes_directly_below_local_even_when_local_was_moved()
    {
        List<ContentOrderEntry> order = [new("old", true), new(ContentLayerSpec.LocalId, true), new("older", true)];

        Assert.True(ContentLoadOrder.AddInstalled(order, "new"));
        Assert.False(ContentLoadOrder.AddInstalled(order, "new"));

        Assert.Equal(["old", ContentLayerSpec.LocalId, "new", "older"], order.Select(static e => e.Id));
    }

    [Fact]
    public void Moving_and_switching_change_only_that_entry()
    {
        List<ContentOrderEntry> order = [new(ContentLayerSpec.LocalId, true), new("a", true), new("b", true)];

        Assert.True(ContentLoadOrder.Move(order, "b", 0));
        Assert.Equal(["b", ContentLayerSpec.LocalId, "a"], order.Select(static e => e.Id));
        Assert.False(ContentLoadOrder.Move(order, "b", 0));
        Assert.True(ContentLoadOrder.Move(order, "b", 99));
        Assert.Equal(["local", "a", "b"], order.Select(static e => e.Id));

        Assert.True(ContentLoadOrder.SetEnabled(order, ContentLayerSpec.LocalId, false));
        Assert.False(ContentLoadOrder.SetEnabled(order, ContentLayerSpec.LocalId, false));
        Assert.False(order[0].Enabled);
        Assert.True(order[1].Enabled);
    }

    [Fact]
    public void A_removed_pack_leaves_the_order_but_not_while_the_library_is_unreachable()
    {
        List<ContentOrderEntry> order = [new(ContentLayerSpec.LocalId, true), new("gone", false), new("kept", true)];

        Assert.False(ContentLoadOrder.Reconcile(order, ["kept"], null, prune: false));
        Assert.Equal(3, order.Count);

        Assert.True(ContentLoadOrder.Reconcile(order, ["kept"], null, prune: true));
        Assert.Equal([ContentLayerSpec.LocalId, "kept"], order.Select(static e => e.Id));
    }

    [Fact]
    public void Each_game_keeps_its_own_order_through_the_settings_file()
    {
        string path = Path.Combine(_root, "settings.json");
        LauncherSettings settings = new();
        settings.ContentOrder["igi1"] = [new("hd", true), new(ContentLayerSpec.LocalId, false)];
        settings.ContentOrder["igi2"] = [new(ContentLayerSpec.LocalId, true)];
        settings.Save(path);

        LauncherSettings read = LauncherSettings.Load(path);

        Assert.Equal(settings.ContentOrder["igi1"], read.ContentOrder["igi1"]);
        Assert.Equal(settings.ContentOrder["igi2"], read.ContentOrder["igi2"]);
    }

    [Fact]
    public void A_settings_file_from_before_load_orders_still_reads()
    {
        string path = Path.Combine(_root, "settings.json");
        File.WriteAllText(path, """{ "library": "x", "content": { "hd-textures": false } }""");

        LauncherSettings read = LauncherSettings.Load(path);

        Assert.False(read.Content["hd-textures"]);
        Assert.Empty(read.ContentOrder);
    }

    [Fact]
    public void Conflicts_count_what_each_enabled_layer_wins_and_loses()
    {
        IReadOnlyDictionary<string, ContentConflict> conflicts = ContentConflicts.Compute(
        [
            new("local", "Local", true, ["common/a.tex"]),
            new("hd", "HD Textures", true, ["common/a.tex", "common/b.tex", "common\\c.tex"]),
            new("off", "Switched off", false, ["common/b.tex"]),
            new("low", "Low", true, ["COMMON/B.TEX", "common/c.tex", "common/d.tex"]),
        ]);

        Assert.Equal([("HD Textures", 1)], conflicts["local"].Covers);
        Assert.Equal([("Local", 1)], conflicts["hd"].CoveredBy);
        Assert.Equal([("Low", 2)], conflicts["hd"].Covers);
        Assert.Same(ContentConflict.None, conflicts["off"]);
        Assert.Equal(2, conflicts["low"].CoveredFiles);
        Assert.Empty(conflicts["low"].Covers);
    }

    [Fact]
    public void The_profile_lists_every_game_in_order_and_leaves_out_what_cannot_load()
    {
        Dictionary<string, List<ContentOrderEntry>> orders = new()
        {
            ["igi1"] = [new(ContentLayerSpec.LocalId, true), new("hd", false), new("damaged", true), new("igi2-only", true)],
            ["igi2"] = [new("igi2-only", true), new(ContentLayerSpec.LocalId, false)],
        };

        ContentProfile profile = ContentLoadOrder.ToProfile(orders, id => id switch
        {
            "hd" => new ContentLoadOrder.PackLayer("HD Textures", "igi1", "C:/lib/content/hd/files"),
            "igi2-only" => new ContentLoadOrder.PackLayer("Sequel Pack", "igi2", "C:/lib/content/igi2-only/files"),
            _ => null,
        });

        Assert.Equal(
            [ContentLayerSpec.Local(), new("hd", "HD Textures", ContentLayerKind.Pack, "C:/lib/content/hd/files", false)],
            profile.For("igi1"));
        Assert.Equal(["igi2-only", ContentLayerSpec.LocalId], profile.For("igi2").Select(static l => l.Id));
        Assert.False(profile.For("igi2")[1].Enabled);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temporary directory is not worth failing a test run over.
        }
    }
}
