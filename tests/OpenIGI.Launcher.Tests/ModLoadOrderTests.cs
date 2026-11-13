using OpenIGI.Core.Content;
using OpenIGI.Launcher.Services;
using Xunit;

namespace OpenIGI.Launcher.Tests;

/// <summary>A game's mod order: what is in it, where a new mod goes, and the profile the game is started with.</summary>
public sealed class ModLoadOrderTests
{
    [Fact]
    public void A_new_mod_goes_at_the_end_switched_on_and_a_known_one_keeps_its_place()
    {
        List<ContentOrderEntry> order = [new("first", true), new("second", false)];

        Assert.True(ModLoadOrder.AddInstalled(order, "third"));
        Assert.False(ModLoadOrder.AddInstalled(order, "first"));

        Assert.Equal([new("first", true), new("second", false), new("third", true)], order);
    }

    [Fact]
    public void Reconciling_drops_what_is_gone_only_when_asked_and_adds_what_is_new()
    {
        List<ContentOrderEntry> order = [new("gone", true), new("kept", false)];

        Assert.True(ModLoadOrder.Reconcile(order, ["kept", "new"], prune: false));
        Assert.Equal(["gone", "kept", "new"], order.Select(static e => e.Id));

        Assert.True(ModLoadOrder.Reconcile(order, ["kept", "new"], prune: true));
        Assert.Equal([new("kept", false), new("new", true)], order);
        Assert.False(ModLoadOrder.Reconcile(order, ["kept", "new"], prune: true));
    }

    [Fact]
    public void The_profile_names_every_games_playable_mods_in_order_and_a_game_played_without_mods_has_them_off()
    {
        Dictionary<string, List<ContentOrderEntry>> orders = new(StringComparer.Ordinal)
        {
            ["igi1"] = [new("soldiers", true), new("broken", true), new("textures", false)],
            ["igi2"] = [new("soldiers", true)],
        };

        ModProfile profile = ModLoadOrder.ToProfile(
            orders,
            static (game, id) => id == "broken" ? null : $"/mods/{id}/files",
            off: ["igi2"]);

        Assert.Equal(
            [new ModProfileEntry("soldiers", "/mods/soldiers/files", true), new ModProfileEntry("textures", "/mods/textures/files", false)],
            profile.For("igi1"));
        Assert.Equal([new ModProfileEntry("soldiers", "/mods/soldiers/files", false)], profile.For("igi2"));
        Assert.Equal(profile.For("igi1"), ModProfile.Parse(ModProfile.Serialize(profile)).For("igi1"));
    }
}
