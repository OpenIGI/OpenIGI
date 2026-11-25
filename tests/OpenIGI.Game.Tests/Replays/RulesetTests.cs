using OpenIGI.Core.Configuration;
using OpenIGI.Core.Configuration.Presets;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Player;
using OpenIGI.Game.Player.Aim;
using OpenIGI.Game.Replays;
using Xunit;

namespace OpenIGI.Game.Tests.Replays;

/// <summary>
/// A run's rules are what its settings make: the original's every setting as the Vanilla experience has it, the remaster's as
/// the Enhanced one does, and a cheat or a setting changed on its own outside both. Read from recorded settings, never the
/// game's own, which other tests may be changing.
/// </summary>
public sealed class RulesetTests
{
    public RulesetTests()
    {
        // The containers register their variables as they are first touched.
        _ = GameplayCVars.GrenadeCook;
        _ = ModernAimCVars.Enabled;
        _ = AdsTuningCVars.Tune;
        _ = PlayerCheatCVars.InfiniteHealth;
        _ = CoopCVars.FriendlyFire;
        _ = NetCVars.TimeLimit;
    }

    [Fact]
    public void The_vanilla_experience_plays_by_the_classic_rules()
    {
        (Ruleset rules, string why) = Ruleset.Of(SettingsOf(ExperiencePreset.Vanilla), CVarRegistry.Default, ExperiencePresetRegistry.Default);
        Assert.True(rules == Ruleset.Classic, why);
    }

    [Fact]
    public void The_enhanced_experience_plays_by_the_modern_rules()
    {
        (Ruleset rules, string why) = Ruleset.Of(SettingsOf(ExperiencePreset.Enhanced), CVarRegistry.Default, ExperiencePresetRegistry.Default);
        Assert.True(rules == Ruleset.Modern, why);
    }

    [Fact]
    public void A_game_is_held_only_to_the_settings_its_single_player_reads()
    {
        // I.G.I. 2's movement changed, and a server's time limit: neither is read in Project I.G.I.'s single player.
        List<(string Name, string Value)> settings = [.. SettingsOf(ExperiencePreset.Vanilla)];
        Set(settings, GameplayCVars.Igi2Movement.Name, "igi1");
        Set(settings, NetCVars.TimeLimit.Name, "25");

        (Ruleset igi1, string why) = Ruleset.Of(settings, CVarRegistry.Default, ExperiencePresetRegistry.Default, "igi1");
        Assert.True(igi1 == Ruleset.Classic, why);

        // I.G.I. 2 reads its movement: changed, its run is no longer the original's.
        (Ruleset igi2, string reason) = Ruleset.Of(settings, CVarRegistry.Default, ExperiencePresetRegistry.Default, "igi2");
        Assert.Equal(Ruleset.Unranked, igi2);
        Assert.Contains(GameplayCVars.Igi2Movement.Name, reason, StringComparison.Ordinal);
        Assert.DoesNotContain(NetCVars.TimeLimit.Name, reason, StringComparison.Ordinal);

        static void Set(List<(string Name, string Value)> list, string name, string value)
        {
            int at = list.FindIndex(s => s.Name == name);
            if (at >= 0)
            {
                list[at] = (name, value);
            }
            else
            {
                list.Add((name, value));
            }
        }
    }

    [Fact]
    public void A_cheat_leaves_both_rules()
    {
        List<(string Name, string Value)> settings = [.. SettingsOf(ExperiencePreset.Vanilla)];
        int at = settings.FindIndex(static s => s.Name == PlayerCheatCVars.InfiniteHealth.Name);
        settings[at] = (PlayerCheatCVars.InfiniteHealth.Name, "1");

        (Ruleset rules, string why) = Ruleset.Of(settings, CVarRegistry.Default, ExperiencePresetRegistry.Default);
        Assert.Equal(Ruleset.Unranked, rules);
        Assert.Contains(PlayerCheatCVars.InfiniteHealth.Name, why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_setting_the_game_does_not_know_leaves_both_rules()
    {
        List<(string Name, string Value)> settings = [.. SettingsOf(ExperiencePreset.Enhanced), ("g_made_up", "1")];
        (Ruleset rules, string why) = Ruleset.Of(settings, CVarRegistry.Default, ExperiencePresetRegistry.Default);
        Assert.Equal(Ruleset.Unranked, rules);
        Assert.Contains("g_made_up", why, StringComparison.Ordinal);
    }

    [Fact]
    public void How_the_dead_are_drawn_is_no_part_of_either_rules()
    {
        // The Vanilla experience turns the ragdolls off, but they only draw a death: a classic run may have them on.
        Assert.Equal(CVarOptions.None, GameplayCVars.Ragdoll.Flags & CVarOptions.Simulation);
        List<(string Name, string Value)> settings = SettingsOf(ExperiencePreset.Vanilla);
        Assert.DoesNotContain(settings, static s => s.Name.StartsWith("g_ragdoll", StringComparison.Ordinal));
        (Ruleset rules, string why) = Ruleset.Of(settings, CVarRegistry.Default, ExperiencePresetRegistry.Default);
        Assert.True(rules == Ruleset.Classic, why);
    }

    [Fact]
    public void A_ruleset_is_found_by_its_id()
    {
        Assert.Equal(Ruleset.Classic, Ruleset.FromId("classic"));
        Assert.Equal(Ruleset.Modern, Ruleset.FromId("modern"));
        Assert.Equal(Ruleset.Unranked, Ruleset.FromId("anything"));
        Assert.Equal(Ruleset.Unranked, Ruleset.FromId(null));
    }

    // Every simulation setting at its default, then as an experience sets it: what a run under that experience records.
    private static List<(string Name, string Value)> SettingsOf(ExperiencePreset preset)
    {
        Dictionary<string, string> values = CVarRegistry.Default.All
            .Where(static v => (v.Flags & CVarOptions.Simulation) != 0)
            .ToDictionary(static v => v.Name, static v => v.DefaultText, StringComparer.Ordinal);
        foreach ((ICVar variable, string value) in ExperiencePresetRegistry.Default.ValuesOf(preset))
        {
            if (values.ContainsKey(variable.Name))
            {
                values[variable.Name] = value;
            }
        }

        return [.. values.OrderBy(static p => p.Key, StringComparer.Ordinal).Select(static p => (p.Key, p.Value))];
    }
}
