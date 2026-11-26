using OpenIGI.Core.Configuration;
using OpenIGI.Frontend.Screens;
using OpenIGI.UI;
using OpenIGI.UI.Widgets;
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

            Assert.Equal(["Graphics", "Display", "Audio", "Controls", "Gameplay", "Interface"], tabs.Tabs);
            Assert.Equal(0, tabs.SelectedIndex);

            Assert.Null(Find<Button>(screen, b => b.Text == "Basic"));
            Assert.Null(Find<Button>(screen, b => b.Text == "Advanced"));

            // The preset leads: it is the one control that answers the whole question at once. It is also
            // the only row this harness can show, its registry being empty -- what the page makes of a
            // populated one is QualityBindingTests' business.
            Assert.NotNull(screen.Root.Descendants().OfType<TabStrip>().FirstOrDefault(strip => strip.Tabs.Contains("Custom")));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    /// <summary>
    /// A panel's finer rows wait behind its expander, which opens them in place and is remembered; and a row that follows
    /// another setting stays out of the way, expander and all, until that setting is on.
    /// </summary>
    [Fact]
    public void Advanced_rows_wait_behind_the_expander_and_follow_their_setting()
    {
        string dir = Path.Combine(Path.GetTempPath(), "openigi-shell-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var cvars = new CVarRegistry();
            BoolCVar ragdoll = CVar.Bool("g_ragdoll", false, CVarOptions.Archive, "Ragdolls.", cvars);
            CVar.Float("g_ragdoll_force", 1f, 0f, 8f, CVarOptions.Archive, "Force.", cvars);
            StringCVar expanded = CVar.Text("ui_settings_expanded", string.Empty, CVarOptions.Archive | CVarOptions.Hidden, "Open panels.", cvars);

            (Shell shell, _) = BuildShell(dir, cvars: cvars);
            Assert.True(shell.OpenOn("settings"));
            Pump(shell, 0.6f);
            var screen = Assert.IsType<SettingsScreen>(shell.Stack.Top);
            TabStrip tabs = screen.Root!.Descendants().OfType<TabStrip>().First();
            tabs.SelectedIndex = tabs.Tabs.ToList().IndexOf("Gameplay");
            Pump(shell, 0.1f);

            Slider force = Assert.IsType<Slider>(Find<Slider>(screen, s => s.Label == "Ragdoll Knock-Back"));
            ExpanderRow expander = Assert.IsType<ExpanderRow>(Find<ExpanderRow>(screen, _ => true));

            // Ragdolls off: the knock-back means nothing, so neither it nor an expander with only it behind is shown.
            Assert.Equal(Visibility.Collapsed, force.Visibility);
            Assert.Equal(Visibility.Collapsed, expander.Visibility);

            ragdoll.Value = true;
            Assert.Equal(Visibility.Visible, expander.Visibility);
            Assert.Equal("Advanced (1)", expander.Caption);
            Assert.Equal(Visibility.Collapsed, force.Visibility);

            expander.Activate();
            Assert.Equal(Visibility.Visible, force.Visibility);
            Assert.Equal("Gameplay/Realism", expanded.Value);

            expander.Activate();
            Assert.Equal(Visibility.Collapsed, force.Visibility);
            Assert.Equal(string.Empty, expanded.Value);
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
