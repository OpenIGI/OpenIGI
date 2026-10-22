using OpenIGI.Frontend.Screens;
using OpenIGI.Game.Player;
using OpenIGI.Input;
using OpenIGI.Rendering;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;
using Xunit;

namespace OpenIGI.Frontend.Tests;

/// <summary>Covers the campaign section: its mission rows, their preview, the unlock gates and the run's conditions.</summary>
[Collection(ShellTestHarness.CollectionName)]
public sealed class CampaignShellTests : ShellTestHarness
{
    [Fact]
    public void Activating_campaign_lists_the_missions_as_rows_bound_to_the_mission_data()
    {
        string dir = Path.Combine(Path.GetTempPath(), "openigi-shell-tests", Guid.NewGuid().ToString("N"));
        try
        {
            (Shell shell, FakeHost host) = BuildShell(dir);
            Pump(shell, 0.1f);

            Button campaign = Find<Button>(shell.Stack.Top!, b => b.Text == "Campaign")!;
            campaign.Activate();
            Pump(shell, 0.6f);

            var screen = Assert.IsType<CampaignScreen>(shell.Stack.Top);
            List<ListCard> rows = [.. screen.Root!.Descendants().OfType<ListCard>()];
            Assert.Equal(2, rows.Count);
            Assert.Equal("01", rows[0].Badge);
            Assert.Equal("Mission One", rows[0].Title);
            Assert.Equal("Mission Two", rows[1].Title);

            // Every row carries its progress, which is what tells a locked mission from an available one.
            Assert.All(rows, row => Assert.NotEmpty(row.Tag));

            // Entering the campaign previews the mission the profile is up to behind the menu.
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

    [Fact]
    public void A_mods_campaign_is_listed_apart_and_every_one_of_its_missions_is_open()
    {
        string dir = Path.Combine(Path.GetTempPath(), "openigi-shell-tests", Guid.NewGuid().ToString("N"));
        try
        {
            (Shell shell, FakeHost host) = BuildShell(
                dir,
                activeMission: 1,
                extraMissions:
                [
                    new(901, 1, "Nightfall One", "A mod's first.", "nf1.spr", "Nightfall"),
                    new(902, 2, "Nightfall Two", "A mod's second.", "nf2.spr", "Nightfall"),
                ]);
            Pump(shell, 0.1f);
            Find<Button>(shell.Stack.Top!, b => b.Text == "Campaign")!.Activate();
            Pump(shell, 0.6f);

            var screen = Assert.IsType<CampaignScreen>(shell.Stack.Top);
            OptionCycler campaigns = Assert.Single(screen.Root!.Descendants().OfType<OptionCycler>(), static c => c.Label == "CAMPAIGN");
            Assert.Equal(["The game", "Nightfall"], campaigns.Options);
            Assert.Equal(["Mission One", "Mission Two"], screen.Root!.Descendants().OfType<ListCard>().Select(static r => r.Title));

            campaigns.Step(1);
            Pump(shell, 0.6f);

            List<ListCard> rows = [.. shell.Stack.Top!.Root!.Descendants().OfType<ListCard>()];
            Assert.Equal(["Nightfall One", "Nightfall Two"], rows.Select(static r => r.Title));
            Assert.All(rows, static row => Assert.True(row.IsEnabled));
            Assert.Equal(901, host.Previewed);
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
    public void Clicking_a_mission_row_selects_it_and_previews_that_mission()
    {
        string dir = Path.Combine(Path.GetTempPath(), "openigi-shell-tests", Guid.NewGuid().ToString("N"));
        try
        {
            (Shell shell, FakeHost host) = BuildShell(dir);
            Pump(shell, 0.1f);
            Find<Button>(shell.Stack.Top!, b => b.Text == "Campaign")!.Activate();
            Pump(shell, 0.6f);

            var screen = (CampaignScreen)shell.Stack.Top!;
            ListCard second = screen.Root!.Descendants().OfType<ListCard>().ElementAt(1);
            UiVec2 centre = second.Bounds.Center;

            var input = new InputState();
            input.BeginFrame();
            input.Mouse.SetPosition(centre.X, centre.Y);
            input.Mouse.AddMotion(4f, 4f);
            input.Mouse.SetButton(MouseButton.Left, true);
            shell.Update(new LayoutContext(1920f, 1080f), input, 1f / 60f);

            Assert.True(second.Selected);
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

    [Fact]
    public void Campaign_refreshes_locked_missions_when_unlock_cheat_changes()
    {
        string dir = Path.Combine(Path.GetTempPath(), "openigi-shell-tests", Guid.NewGuid().ToString("N"));
        bool previousDeveloperMode = PlayerCheatCVars.DeveloperMode.Value;
        bool previousCheatsEnabled = PlayerCheatCVars.CheatsEnabled.Value;
        bool previousUnlockAllLevels = PlayerCheatCVars.UnlockAllLevels.Value;
        try
        {
            PlayerCheatCVars.DeveloperMode.Value = false;
            PlayerCheatCVars.CheatsEnabled.Value = false;
            PlayerCheatCVars.UnlockAllLevels.Value = false;

            (Shell shell, _) = BuildShell(dir, activeMission: 1);
            Pump(shell, 0.1f);
            Find<Button>(shell.Stack.Top!, b => b.Text == "Campaign")!.Activate();
            Pump(shell, 0.6f);

            var rows = shell.Stack.Top!.Root!.Descendants().OfType<ListCard>().ToList();
            Assert.False(rows[1].IsEnabled);

            // Unlocking alone is not enough: all three availability gates are part of the effective
            // option, and each change must refresh the active rail.
            PlayerCheatCVars.UnlockAllLevels.Value = true;
            Assert.False(rows[1].IsEnabled);
            PlayerCheatCVars.DeveloperMode.Value = true;
            Assert.False(rows[1].IsEnabled);
            PlayerCheatCVars.CheatsEnabled.Value = true;
            Assert.True(rows[1].IsEnabled);

            PlayerCheatCVars.UnlockAllLevels.Value = false;
            Assert.False(rows[1].IsEnabled);

            // Shell tab navigation uses Reset; the old campaign must no longer react after it leaves.
            shell.OpenOn("extras");
            PlayerCheatCVars.UnlockAllLevels.Value = true;
            Assert.False(rows[1].IsEnabled);
        }
        finally
        {
            PlayerCheatCVars.UnlockAllLevels.Value = previousUnlockAllLevels;
            PlayerCheatCVars.CheatsEnabled.Value = previousCheatsEnabled;
            PlayerCheatCVars.DeveloperMode.Value = previousDeveloperMode;
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void Play_starts_the_selected_mission_at_the_chosen_difficulty()
    {
        string dir = Path.Combine(Path.GetTempPath(), "openigi-shell-tests", Guid.NewGuid().ToString("N"));
        try
        {
            (Shell shell, FakeHost host) = BuildShell(dir);
            Pump(shell, 0.1f);
            Find<Button>(shell.Stack.Top!, b => b.Text == "Campaign")!.Activate();
            Pump(shell, 0.6f);

            var campaign = (CampaignScreen)shell.Stack.Top!;
            Find<PrimaryAction>(campaign, p => p.Title == "PLAY")!.Activate();

            Assert.Equal(2, host.StartedMission); // the profile's frontier is selected by default
            Assert.True(host.StartedDifficulty >= 0);
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
    /// The campaign's hour and weather are a way to play the run, so they are told to the app as they are
    /// chosen and they survive the screen being rebuilt.
    /// </summary>
    /// <remarks>
    /// The second half is the half that would break silently. This panel is built fresh every time the
    /// section is opened, so a choice held on the screen rather than in the context would show "Default"
    /// back to a player three missions into a night run -- and the run would carry on at night while the
    /// menu said it would not, which is worse than either behaviour on its own.
    /// </remarks>
    [Fact]
    public void The_campaign_chooses_the_hour_and_the_weather_the_run_is_played_in()
    {
        string dir = Path.Combine(Path.GetTempPath(), "openigi-shell-tests", Guid.NewGuid().ToString("N"));
        try
        {
            (Shell shell, FakeHost host) = BuildShell(dir);
            Pump(shell, 0.1f);
            Find<Button>(shell.Stack.Top!, b => b.Text == "Campaign")!.Activate();
            Pump(shell, 0.6f);

            var campaign = (CampaignScreen)shell.Stack.Top!;
            OptionCycler time = Find<OptionCycler>(campaign, o => o.Label == "TIME")!;
            OptionCycler weather = Find<OptionCycler>(campaign, o => o.Label == "WEATHER")!;

            Assert.Equal(MissionTimeOfDay.Default, (MissionTimeOfDay)time.SelectedIndex);
            Assert.Equal(MissionWeather.Default, (MissionWeather)weather.SelectedIndex);

            time.SelectedIndex = (int)MissionTimeOfDay.Night;
            weather.SelectedIndex = (int)MissionWeather.Rain;

            Assert.Equal(new MissionConditions(MissionTimeOfDay.Night, MissionWeather.Rain), host.Conditions);

            // Away and back: the section is rebuilt from scratch and still shows the run being played.
            shell.OpenOn("settings");
            Pump(shell, 0.6f);
            shell.OpenOn("campaign");
            Pump(shell, 0.6f);

            var reopened = (CampaignScreen)shell.Stack.Top!;
            Assert.Equal(
                MissionTimeOfDay.Night,
                (MissionTimeOfDay)Find<OptionCycler>(reopened, o => o.Label == "TIME")!.SelectedIndex);
            Assert.Equal(
                MissionWeather.Rain,
                (MissionWeather)Find<OptionCycler>(reopened, o => o.Label == "WEATHER")!.SelectedIndex);
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
