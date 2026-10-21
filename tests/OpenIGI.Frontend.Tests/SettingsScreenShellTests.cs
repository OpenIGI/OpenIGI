using OpenIGI.Core.Configuration;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Titles;
using OpenIGI.Frontend.Saves;
using OpenIGI.Frontend.Screens;
using OpenIGI.Frontend.Settings;
using OpenIGI.Frontend;
using OpenIGI.Game.Config;
using OpenIGI.Game.Player;
using OpenIGI.Input;
using OpenIGI.Core.Configuration.Presets;
using OpenIGI.Rendering;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Screens;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;
using OpenIGI.UI;
using System.Linq;
using Xunit;

namespace OpenIGI.Frontend.Tests;

/// <summary>Covers the settings section: its one graphics page, its rows' tooltips, and saving on the way out.</summary>
[Collection(ShellTestHarness.CollectionName)]
public sealed class SettingsScreenShellTests : ShellTestHarness
{
    /// <summary>
    /// Settings open on one graphics page: the preset, then a row per subject, and no second half.
    /// </summary>
    /// <remarks>
    /// This used to assert the opposite — that a Basic/Advanced switch was present and reading Basic. The
    /// Advanced half listed every graphics variable as its own row, which meant the menu offered a card
    /// atlas density and a probe gather cadence as sliders. It is gone; the console is the advanced
    /// interface. The pin now is that the switch does NOT come back, because the easiest way to undo this
    /// is to add "just one" raw row.
    /// </remarks>
    [Fact]
    public void Settings_open_on_one_graphics_page_with_no_advanced_half()
    {
        string dir = Path.Combine(Path.GetTempPath(), "openigi-shell-tests", Guid.NewGuid().ToString("N"));
        try
        {
            (Shell shell, _) = BuildShell(dir);
            Pump(shell, 0.1f);
            Find<Button>(shell.Stack.Top!, b => b.Text == "Settings")!.Activate();
            Pump(shell, 0.6f);

            var screen = Assert.IsType<SettingsScreen>(shell.Stack.Top);
            TabStrip tabs = screen.Root!.Descendants().OfType<TabStrip>().First();

            Assert.Equal(["Graphics", "Display", "Audio", "Controls", "Gameplay"], tabs.Tabs);
            Assert.Equal(0, tabs.SelectedIndex);

            Assert.Null(Find<Button>(screen, b => b.Text == "Basic"));
            Assert.Null(Find<Button>(screen, b => b.Text == "Advanced"));

            // The preset leads: it is the one control that answers the whole question at once. It is also
            // the only row this harness can show, its registry being empty -- what the page makes of a
            // populated one is QualityBindingTests' business.
            Assert.NotNull(screen.Root!.Descendants().OfType<Dropdown>().FirstOrDefault(d => d.Label == "Preset"));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void Every_settings_row_carries_something_to_explain_itself_with()
    {
        string dir = Path.Combine(Path.GetTempPath(), "openigi-shell-tests", Guid.NewGuid().ToString("N"));
        try
        {
            (Shell shell, _) = BuildShell(dir);
            Pump(shell, 0.1f);
            Find<Button>(shell.Stack.Top!, b => b.Text == "Settings")!.Activate();
            Pump(shell, 0.6f);

            // Every bound row on every tab, not just the visible one: a tooltip that only exists on the
            // page someone happened to test is the kind that is missing everywhere else.
            List<Control> rows = [.. shell.Stack.Top!.Root!.Descendants().OfType<Control>()
                .Where(c => c is Slider or Toggle or Dropdown or KeyBindingRow)];

            Assert.NotEmpty(rows);
            Assert.All(rows, row => Assert.NotEmpty(row.Tooltip));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void Leaving_settings_persists_both_settings_and_profiles()
    {
        // Issue #230: rebinds mutate the live profile, but SettingsScreen.OnExit only saved engine
        // settings — the profile (GOInRemap bindings, sensitivity, invert) was silently lost.
        string dir = Path.Combine(Path.GetTempPath(), "openigi-shell-tests", Guid.NewGuid().ToString("N"));
        try
        {
            (Shell shell, FakeHost host) = BuildShell(dir);
            Assert.True(shell.OpenOn("settings"));
            Pump(shell, 0.6f);
            var screen = Assert.IsType<SettingsScreen>(shell.Stack.Top);
            int settings = host.SaveSettingsCalls;
            int profiles = host.SaveProfilesCalls;
            screen.BeginExit();
            Assert.Equal(settings + 1, host.SaveSettingsCalls);
            Assert.Equal(profiles + 1, host.SaveProfilesCalls);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void Leaving_settings_through_a_top_tab_reset_persists_both_settings_and_profiles()
    {
        // Issue #230 follow-up: ScreenStack.Reset cleared without exit hooks, so leaving Settings
        // via the top tabs (Campaign/Profile/Extras/Home) skipped the profile save entirely.
        string dir = Path.Combine(Path.GetTempPath(), "openigi-shell-tests", Guid.NewGuid().ToString("N"));
        try
        {
            (Shell shell, FakeHost host) = BuildShell(dir);
            Assert.True(shell.OpenOn("settings"));
            Pump(shell, 0.6f);
            Assert.IsType<SettingsScreen>(shell.Stack.Top);
            int settings = host.SaveSettingsCalls;
            int profiles = host.SaveProfilesCalls;
            Assert.True(shell.OpenOn("campaign"));
            Pump(shell, 0.6f);
            Assert.Equal(settings + 1, host.SaveSettingsCalls);
            Assert.Equal(profiles + 1, host.SaveProfilesCalls);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
