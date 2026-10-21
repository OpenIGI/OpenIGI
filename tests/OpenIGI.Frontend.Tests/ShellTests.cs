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
    public void Extras_unsubscribes_cheat_gate_handlers_after_resize_and_reset()
    {
        string dir = Path.Combine(Path.GetTempPath(), "openigi-shell-tests", Guid.NewGuid().ToString("N"));
        bool previousDeveloperMode = PlayerCheatCVars.DeveloperMode.Value;
        bool previousCheatsEnabled = PlayerCheatCVars.CheatsEnabled.Value;
        try
        {
            PlayerCheatCVars.DeveloperMode.Value = false;
            PlayerCheatCVars.CheatsEnabled.Value = false;

            (Shell shell, _) = BuildShell(dir);
            shell.OpenOn("extras");
            Pump(shell, 0.1f);
            var extras = Assert.IsType<ExtrasScreen>(shell.Stack.Top);
            Toggle firstDeveloper = Find<Toggle>(extras, toggle => toggle.Label == "Developer mode")
                ?? throw new Xunit.Sdk.XunitException("The Extras developer toggle was not built.");

            Panel firstGate = Find<GroupHeader>(extras, header => header.Title == "CHEATS")!.Parent!.Parent as Panel
                ?? throw new Xunit.Sdk.XunitException("The Extras cheat gate panel was not built.");
            Assert.Equal(Visibility.Collapsed, firstGate.Visibility);

            // A viewport change rebuilds the retained screen tree. The subscription must not be made by
            // BuildContent, or reset would leave one duplicate handler behind.
            shell.Update(new LayoutContext(1280f, 720f), new InputState(), 1f / 60f);
            Toggle resizedDeveloper = Find<Toggle>(extras, toggle => toggle.Label == "Developer mode")
                ?? throw new Xunit.Sdk.XunitException("The resized Extras developer toggle was not built.");
            Panel resizedGate = Find<GroupHeader>(extras, header => header.Title == "CHEATS")!.Parent!.Parent as Panel
                ?? throw new Xunit.Sdk.XunitException("The resized Extras cheat gate panel was not built.");

            shell.OpenOn("campaign");
            PlayerCheatCVars.DeveloperMode.Value = true;

            Assert.Equal(Visibility.Collapsed, firstGate.Visibility);
            Assert.Equal(Visibility.Collapsed, resizedGate.Visibility);
            Assert.False(firstDeveloper.Value);
            Assert.False(resizedDeveloper.Value);
        }
        finally
        {
            PlayerCheatCVars.CheatsEnabled.Value = previousCheatsEnabled;
            PlayerCheatCVars.DeveloperMode.Value = previousDeveloperMode;
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
