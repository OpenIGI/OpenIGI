using OpenIGI.Core.Configuration;
using OpenIGI.Core.Configuration.Presets;
using OpenIGI.Core.Titles;
using OpenIGI.Frontend.Saves;
using OpenIGI.Frontend.Screens;
using OpenIGI.Game.Config;
using OpenIGI.Game.Player;
using OpenIGI.Input;
using OpenIGI.UI;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Screens;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;
using Xunit;

namespace OpenIGI.Frontend.Tests;

/// <summary>Covers the shell composing its screens and navigating between them, with no GL context.</summary>
[Collection(ShellTestHarness.CollectionName)]
public sealed class ShellTests : ShellTestHarness
{
    /// <summary>The game selector offers the other title, and asks the app for it by id.</summary>
    /// <remarks>
    /// <para>
    /// The shell's whole part in a title switch: name the game the player picked. It cannot perform one
    /// — the loop, the registry and every renderer it is being drawn out of are what a switch replaces —
    /// so what is asserted here is the request, not the result.
    /// </para>
    /// <para>
    /// Picking the game already running is deliberately NOT a request. It is how the player gets back to
    /// the landing page from a section, and tearing a session down to rebuild it identically would cost
    /// a second and change nothing they can see.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_game_selector_asks_the_app_for_the_other_title()
    {
        var host = new FakeHost();

        // The one running is current; asking for it again is a navigation, not a switch -- which is why
        // the screen short-circuits it before ever reaching this seam.
        Assert.True(host.SwitchTitle(GameTitle.Igi1.Id));
        Assert.Equal(GameTitle.Igi1.Id, host.SwitchedTo);

        Assert.True(host.SwitchTitle(GameTitle.Igi2.Id));
        Assert.Equal(GameTitle.Igi2.Id, host.SwitchedTo);

        // A refusal is the app's to make -- not installed, or already running -- and the shell has to
        // cope with being told no rather than assuming the game changed under it.
        host.SwitchAccepted = false;
        Assert.False(host.SwitchTitle(GameTitle.Igi2.Id));
    }

    /// <summary>The current entry is the title running, not the first one listed.</summary>
    /// <remarks>
    /// The list is in a fixed order and either game can be the one running, so the two are only the same
    /// while the first entry is the only takeable one. Selecting the second and then opening the list
    /// would otherwise show the first as current, which reads as the switch having failed.
    /// </remarks>
    [Fact]
    public void The_current_entry_is_the_title_that_is_running()
    {
        var games = GameProfileResolver.BuildCatalogue(GameTitle.Igi2, "C:/games/igi2", null, null);

        var context = new ShellContext(
            new UiTheme(UiFont.ForMeasurement(FontAtlasBuilder.Build(
                File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "fonts", "Barlow-Regular.ttf")),
                48f))),
            new FakeHost(),
            new ScreenStack(),
            games,
            GameTitle.Igi2.Id,
            [],
            new CVarRegistry(),
            GraphicsPresetRegistry.Default,
            GameConfig.CreateDefault(),
            new SaveSlotStore(Path.Combine(Path.GetTempPath(), "openigi-shell-title-index")));

        Assert.Equal(GameTitle.Igi2.Id, context.ActiveTitleId);
        Assert.NotEqual(context.Games[0].Id, context.ActiveTitleId);
    }

    [Fact]
    public void The_shell_opens_on_the_main_menu()
    {
        string dir = Path.Combine(Path.GetTempPath(), "openigi-shell-tests", Guid.NewGuid().ToString("N"));
        try
        {
            (Shell shell, _) = BuildShell(dir);
            Pump(shell, 0.1f); // build + first layout
            Assert.IsType<MainMenuScreen>(shell.Stack.Top);
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
    public void A_mouse_click_activates_the_control_under_the_pointer()
    {
        string dir = Path.Combine(Path.GetTempPath(), "openigi-shell-tests", Guid.NewGuid().ToString("N"));
        try
        {
            (Shell shell, _) = BuildShell(dir);
            Pump(shell, 0.1f); // build + lay out the top bar

            Button campaign = Find<Button>(shell.Stack.Top!, b => b.Text == "Campaign")!;
            UiVec2 centre = campaign.Bounds.Center;

            var input = new InputState();
            input.BeginFrame();
            input.Mouse.SetPosition(centre.X, centre.Y);
            input.Mouse.AddMotion(4f, 4f);                 // a move, so the pointer hover-focuses
            input.Mouse.SetButton(MouseButton.Left, true); // and a click
            shell.Update(new LayoutContext(1920f, 1080f), input, 1f / 60f);

            Pump(shell, 0.6f);
            Assert.IsType<CampaignScreen>(shell.Stack.Top);
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
    public void Quit_reaches_the_host()
    {
        string dir = Path.Combine(Path.GetTempPath(), "openigi-shell-tests", Guid.NewGuid().ToString("N"));
        try
        {
            (Shell shell, FakeHost host) = BuildShell(dir);
            Pump(shell, 0.1f);
            Find<Button>(shell.Stack.Top!, b => b.Text == "Quit")!.Activate();
            Assert.True(host.Quit_Called);
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
    public void The_cheats_are_one_group_in_gameplay_settings_behind_enable_cheats_and_nowhere_in_extras()
    {
        string dir = Path.Combine(Path.GetTempPath(), "openigi-shell-tests", Guid.NewGuid().ToString("N"));
        bool previousCheatsEnabled = PlayerCheatCVars.CheatsEnabled.Value;
        bool previousInfiniteAmmo = PlayerCheatCVars.InfiniteAmmo.Value;
        try
        {
            PlayerCheatCVars.CheatsEnabled.Value = false;
            PlayerCheatCVars.InfiniteAmmo.Value = false;

            // A registry of the cheats alone: the shared one is filled from other tests' threads as this one reads it.
            var cvars = new Core.Configuration.CVarRegistry();
            foreach (Core.Configuration.ICVar cheat in (Core.Configuration.ICVar[])[
                PlayerCheatCVars.CheatsEnabled, PlayerCheatCVars.InfiniteHealth, PlayerCheatCVars.InfiniteAmmo, PlayerCheatCVars.NoReload,
                PlayerCheatCVars.NoRecoil, PlayerCheatCVars.AllWeapons, PlayerCheatCVars.UnlockAllLevels, PlayerCheatCVars.Speed,
                PlayerCheatCVars.Jump, PlayerCheatCVars.HealthScale])
            {
                cvars.Add(cheat);
            }

            (Shell shell, _) = BuildShell(dir, cvars: cvars);
            Assert.True(shell.OpenOn("settings"));
            Pump(shell, 0.6f);
            List<Control> rows = [.. shell.Stack.Top!.Root!.Descendants().OfType<Control>().Where(c => c is Slider or Toggle or Dropdown)];

            // Each cheat once, in the Cheats group, its gate first: never loose among the general rows doing nothing.
            Toggle enable = Assert.Single(rows.OfType<Toggle>(), toggle => toggle.Label == "Enable Cheats");
            Toggle ammo = Assert.Single(rows.OfType<Toggle>(), toggle => toggle.Label == "Infinite Ammo");
            Assert.Equal(9, rows.Count(row => row.Tooltip.Contains("(cheat)", StringComparison.Ordinal)));
            Assert.True(rows.IndexOf(enable) < rows.IndexOf(ammo));

            // Switched on there, a cheat is in force.
            ammo.Value = true;
            Assert.False(PlayerCheatOptions.Current.InfiniteAmmo);
            enable.Value = true;
            Assert.True(PlayerCheatOptions.Current.InfiniteAmmo);

            shell.OpenOn("extras");
            Pump(shell, 0.3f);
            Assert.Null(Find<GroupHeader>(shell.Stack.Top!, header => header.Title == "CHEATS"));
            Assert.Null(Find<Toggle>(shell.Stack.Top!, toggle => toggle.Label is "Enable cheats" or "Infinite ammo"));
        }
        finally
        {
            PlayerCheatCVars.CheatsEnabled.Value = previousCheatsEnabled;
            PlayerCheatCVars.InfiniteAmmo.Value = previousInfiniteAmmo;
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

}
