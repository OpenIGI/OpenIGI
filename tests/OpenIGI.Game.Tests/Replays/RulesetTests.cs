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
