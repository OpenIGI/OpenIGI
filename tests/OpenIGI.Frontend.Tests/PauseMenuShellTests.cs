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

/// <summary>Covers the shell opened over a running mission: the pause menu, and the sections reached from it.</summary>
[Collection(ShellTestHarness.CollectionName)]
public sealed class PauseMenuShellTests : ShellTestHarness
{
    [Fact]
    public void The_pause_menu_opens_on_its_own_page_and_asks_for_no_preview()
    {
        string dir = Path.Combine(Path.GetTempPath(), "openigi-shell-tests", Guid.NewGuid().ToString("N"));
        try
        {
            (Shell shell, FakeHost host) = BuildShell(dir);
            Pump(shell, 0.1f);
            host.ForgetPreview();

            shell.OpenPauseMenu();
            Pump(shell, 0.6f);

            Assert.IsType<PauseScreen>(shell.Stack.Top);

            // The level being played is already the backdrop, so nothing is streamed in behind the menu.
            Assert.Equal(0, host.PreviewCalls);
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
    public void The_campaign_reached_from_the_pause_menu_previews_nothing()
    {
        string dir = Path.Combine(Path.GetTempPath(), "openigi-shell-tests", Guid.NewGuid().ToString("N"));
        try
        {
            (Shell shell, FakeHost host) = BuildShell(dir);
            Pump(shell, 0.1f);
            shell.OpenPauseMenu();
            Pump(shell, 0.6f);
            host.ForgetPreview();

            Find<Button>(shell.Stack.Top!, b => b.Text == "Campaign")!.Activate();
            Pump(shell, 0.6f);

            // The rail is still a rail -- the missions are listed and selectable -- but selecting one must
            // not tear the running mission's level out from under the menu.
            var campaign = Assert.IsType<CampaignScreen>(shell.Stack.Top);
            campaign.Root!.Descendants().OfType<ListCard>().ElementAt(1).Activate();
            Assert.Equal(1, campaign.SelectedMission);
            Assert.Equal(0, host.PreviewCalls);
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
    public void Back_from_a_section_inside_a_mission_returns_to_the_pause_menu()
    {
        string dir = Path.Combine(Path.GetTempPath(), "openigi-shell-tests", Guid.NewGuid().ToString("N"));
        try
        {
            (Shell shell, _) = BuildShell(dir);
            Pump(shell, 0.1f);
            shell.OpenPauseMenu();
            Pump(shell, 0.6f);

            Find<Button>(shell.Stack.Top!, b => b.Text == "Extras")!.Activate();
            Pump(shell, 0.6f);
            Assert.IsType<ExtrasScreen>(shell.Stack.Top);

            Back(shell);
            Pump(shell, 0.6f);

            // Not the landing page: inside a mission the page every section hangs off is the pause menu.
            Assert.IsType<PauseScreen>(shell.Stack.Top);
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
    public void Back_on_the_pause_menu_resumes_the_mission()
    {
        string dir = Path.Combine(Path.GetTempPath(), "openigi-shell-tests", Guid.NewGuid().ToString("N"));
        try
        {
            (Shell shell, FakeHost host) = BuildShell(dir);
            Pump(shell, 0.1f);
            shell.OpenPauseMenu();
            Pump(shell, 0.6f);

            Back(shell);
            Assert.Equal(1, host.ResumeCalls);

            // And so does the button, for a pointer that never reaches for Escape.
            Find<PrimaryAction>(shell.Stack.Top!, p => p.Title == "RESUME")!.Activate();
            Assert.Equal(2, host.ResumeCalls);
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
    public void The_pause_menu_can_restart_or_abandon_the_mission()
    {
        string dir = Path.Combine(Path.GetTempPath(), "openigi-shell-tests", Guid.NewGuid().ToString("N"));
        try
        {
            (Shell shell, FakeHost host) = BuildShell(dir);
            Pump(shell, 0.1f);
            shell.OpenPauseMenu();
            Pump(shell, 0.6f);

            Find<Button>(shell.Stack.Top!, b => b.Text == "RESTART MISSION")!.Activate();
            Find<Button>(shell.Stack.Top!, b => b.Text == "ABANDON MISSION")!.Activate();

            Assert.Equal(1, host.RestartCalls);
            Assert.Equal(1, host.AbandonCalls);
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
    public void Leaving_the_pause_menu_puts_the_showcase_back()
    {
        string dir = Path.Combine(Path.GetTempPath(), "openigi-shell-tests", Guid.NewGuid().ToString("N"));
        try
        {
            (Shell shell, FakeHost host) = BuildShell(dir);
            Pump(shell, 0.1f);
            shell.OpenPauseMenu();
            Pump(shell, 0.6f);

            // What the app does once the mission is released: the shell is over the showcase again, so the
            // sections stream a backdrop behind themselves like they always did.
            shell.ClosePauseMenu();
            host.ForgetPreview();
            shell.OpenOn("campaign");
            Pump(shell, 0.6f);

            Assert.IsType<CampaignScreen>(shell.Stack.Top);
            Assert.Equal(2, host.Previewed);
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
