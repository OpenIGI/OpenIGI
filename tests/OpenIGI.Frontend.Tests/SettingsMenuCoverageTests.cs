using OpenIGI.Core.Configuration;
using OpenIGI.Frontend.Settings;
using Xunit;

namespace OpenIGI.Frontend.Tests;

/// <summary>
/// Keeps the written-out settings pages honest: every player-facing variable is on one or declared console-only, no
/// developer's switch reaches a page, and a row that follows another setting follows one the same page shows.
/// </summary>
/// <remarks>
/// The Audio and Gameplay pages used to be gathered from their variables by name, which showed whatever was registered --
/// including a weapon-sight tuning tool's debug switches. Written out, the risk turns round: a new variable can fall
/// through the gap, shown nowhere and recorded nowhere as left out. These checks are what make recording it compulsory.
/// </remarks>
[Collection(GraphicsSettingsRegistries.Name)]
public sealed class SettingsMenuCoverageTests
{
    private static readonly string[] Prefixes = ["snd_", "g_", "hud_", "ui_"];

    /// <summary>Registers every variable the pages can show, as the game does before it reads its settings.</summary>
    public SettingsMenuCoverageTests()
    {
        OpenIGI.Rendering.Generated.CVarRegistrations.RegisterAll();
        OpenIGI.Game.Generated.CVarRegistrations.RegisterAll();
        OpenIGI.Audio.Generated.CVarRegistrations.RegisterAll();
    }

    /// <summary>Every visible sound, gameplay and interface variable is on a page or declared console-only.</summary>
    [Fact]
    public void Every_player_variable_is_on_a_page_or_declared_console_only()
    {
        HashSet<string> accounted = [.. SettingsMenus.Rows.Select(row => row.Variable), .. SettingsMenus.ConsoleOnly];

        string[] adrift =
        [
            .. CVarRegistry.Default.All
                .Where(variable => Prefixes.Any(prefix => variable.Name.StartsWith(prefix, StringComparison.Ordinal)))
                .Where(variable => !variable.Flags.HasFlag(CVarOptions.Hidden))
                .Where(variable => !variable.Flags.HasFlag(CVarOptions.Developer))
                .Select(variable => variable.Name)
                .Where(name => !accounted.Contains(name))
                .Order(StringComparer.Ordinal),
        ];

        Assert.True(
            adrift.Length == 0,
            "These settings are neither on a settings page nor listed in their page's ConsoleOnly with a reason: "
                + string.Join(", ", adrift));
    }

    /// <summary>Every variable a page declares console-only is registered, and none is also shown.</summary>
    [Fact]
    public void Every_console_only_variable_exists_and_is_not_shown()
    {
        HashSet<string> shown = [.. SettingsMenus.Rows.Select(row => row.Variable)];
        foreach (string name in SettingsMenus.ConsoleOnly)
        {
            Assert.True(CVarRegistry.Default.TryGet(name, out ICVar? cvar) && cvar is not null, $"{name} is declared console-only but is not registered.");
            Assert.DoesNotContain(name, shown);
        }
    }

    /// <summary>No developer's variable is offered to players, but the cheats' own gate.</summary>
    [Fact]
    public void No_developer_variable_is_on_a_page()
    {
        foreach (SettingRow row in SettingsMenus.Rows)
        {
            if (row.Variable == GameplayMenu.CheatsEnabled || !CVarRegistry.Default.TryGet(row.Variable, out ICVar? cvar) || cvar is null)
            {
                continue;
            }

            Assert.False(cvar.Flags.HasFlag(CVarOptions.Developer), $"{row.Caption} ({row.Variable}) is a developer's switch.");
        }
    }

    /// <summary>A row that follows another setting follows one its own page shows, so a player can always turn it on.</summary>
    [Fact]
    public void Every_followed_setting_is_on_the_same_page()
    {
        foreach ((string page, IReadOnlyList<SettingsPanel> panels) in SettingsMenus.Pages)
        {
            HashSet<string> onPage =
            [
                .. panels.SelectMany(panel => panel.Rows).Select(row => row.Variable),
                .. panels.SelectMany(panel => panel.Groups).SelectMany(group => group.Variables),
            ];

            IEnumerable<(string Caption, RowCondition Condition)> followers =
                panels.SelectMany(panel => panel.Rows).Where(row => row.ShownWhen is not null).Select(row => (row.Caption, row.ShownWhen!))
                    .Concat(panels.SelectMany(panel => panel.Groups).Where(group => group.ShownWhen is not null).Select(group => (group.Caption, group.ShownWhen!)));

            foreach ((string caption, RowCondition condition) in followers)
            {
                Assert.True(
                    onPage.Contains(condition.Variable),
                    $"{page}: {caption} follows {condition.Variable}, which the page does not show.");
            }
        }
    }

    /// <summary>A row's chosen value sets a setting the same page shows, to a value that setting takes, from a value the row offers.</summary>
    [Fact]
    public void Every_chosen_value_sets_a_setting_on_the_same_page()
    {
        foreach ((string page, IReadOnlyList<SettingsPanel> panels) in SettingsMenus.Pages)
        {
            HashSet<string> onPage = [.. panels.SelectMany(panel => panel.Rows).Select(row => row.Variable)];
            foreach (SettingRow row in panels.SelectMany(panel => panel.Rows).Where(row => row.OnChosen is not null))
            {
                RowEffect effect = row.OnChosen!;
                Assert.True(onPage.Contains(effect.Variable), $"{page}: {row.Caption} sets {effect.Variable}, which the page does not show.");
                Assert.True(CVarRegistry.Default.TryGet(row.Variable, out ICVar? source) && source is not null, $"{row.Variable} is not registered.");
                Assert.True(CVarRegistry.Default.TryGet(effect.Variable, out ICVar? target) && target is not null, $"{effect.Variable} is not registered.");
                Assert.True(row.Choices is null || row.Choices.ContainsKey(effect.Value), $"{page}: {row.Caption} offers no '{effect.Value}'.");
                string kept = target.ValueText;
                try
                {
                    Assert.True(target.TrySetText(effect.Sets, out string? reason), $"{effect.Variable} refuses '{effect.Sets}': {reason}");
                }
                finally
                {
                    target.TrySetText(kept, out _);
                }
            }
        }
    }

    /// <summary>Choosing Project I.G.I.'s movement for I.G.I. 2 turns its weapon weight off, once, as the choice is made.</summary>
    [Fact]
    public void Choosing_project_igis_movement_turns_its_weapon_weight_off()
    {
        RowEffect effect = SettingsMenus.Rows.Single(row => row.Variable == "g_igi2_movement").OnChosen!;

        Assert.Equal(("g_igi2_igi1_weight", "0"), (effect.Variable, effect.Sets));
        Assert.True(effect.Fires("igi2", "igi1"));
        Assert.False(effect.Fires("igi1", "igi1"));
        Assert.False(effect.Fires("igi1", "igi2"));
    }

    /// <summary>What reads as on: anything but empty, nought or a choice named off.</summary>
    [Theory]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("0.0", false)]
    [InlineData("0.25", true)]
    [InlineData("off", false)]
    [InlineData("Off", false)]
    [InlineData("quality", true)]
    [InlineData("", false)]
    public void A_condition_reads_on_and_off(string value, bool on)
    {
        Assert.Equal(on, RowCondition.IsOn(value));
        Assert.Equal(on, new RowCondition("x").Shows(value));
        Assert.Equal(!on, new RowCondition("x", WhileOff: true).Shows(value));
    }
}
