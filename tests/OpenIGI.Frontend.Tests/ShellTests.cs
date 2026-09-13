using System.Linq;
using OpenIGI.Core.Configuration;
using OpenIGI.Core.Graphics;
using OpenIGI.Frontend;
using OpenIGI.Frontend.Saves;
using OpenIGI.Frontend.Screens;
using OpenIGI.Frontend.Settings;
using OpenIGI.Game.Config;
using OpenIGI.Game.Profiles;
using OpenIGI.Input;
using OpenIGI.Rendering;
using OpenIGI.Rendering.Presets;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;
using Xunit;

namespace OpenIGI.Frontend.Tests;

/// <summary>Covers the shell composing its screens and navigating between them, with no GL context.</summary>
public sealed class ShellTests
{
    private sealed class FakeHost : IShellHost
    {
        public int? Previewed { get; private set; }

        public int StartedMission { get; private set; } = -1;

        public int StartedDifficulty { get; private set; } = -1;

        public bool Quit_Called { get; private set; }

        public int SaveSettingsCalls { get; private set; }

        public int SaveProfilesCalls { get; private set; }

        public int ResumeCalls { get; private set; }

        public int RestartCalls { get; private set; }

        public int AbandonCalls { get; private set; }

        public MissionConditions Conditions { get; private set; } = MissionConditions.Default;

        public int ConditionCalls { get; private set; }

        public void StartMission(int missionId, int difficulty)
        {
            StartedMission = missionId;
            StartedDifficulty = difficulty;
        }

        public void SetMissionConditions(MissionConditions conditions)
        {
            Conditions = conditions;
            ConditionCalls++;
        }

        public void ResumeMission() => ResumeCalls++;

        public void RestartMission() => RestartCalls++;

        public void AbandonMission() => AbandonCalls++;

        public void Quit() => Quit_Called = true;

        public int PreviewCalls { get; private set; }

        public void PreviewMission(int? missionId)
        {
            Previewed = missionId;
            PreviewCalls++;
        }

        /// <summary>Forgets every preview asked for so far, so a test can assert about the ones after it.</summary>
        public void ForgetPreview()
        {
            Previewed = null;
            PreviewCalls = 0;
        }

        public void SaveSettings() => SaveSettingsCalls++;

        public void SaveProfiles() => SaveProfilesCalls++;

        public Bgra32[] CaptureThumbnail(out int width, out int height)
        {
            width = 0;
            height = 0;
            return [];
        }
    }

    private static (Shell Shell, FakeHost Host) BuildShell(string savesDir)
    {
        string fontPath = Path.Combine(AppContext.BaseDirectory, "Assets", "fonts", "Saira-Regular.ttf");
        UiFont font = OpenIGI.UI.Text.UiFont.ForMeasurement(
            OpenIGI.UI.Text.FontAtlasBuilder.Build(File.ReadAllBytes(fontPath), 48f));
        var theme = new UiTheme(font);

        var host = new FakeHost();
        var missions = new List<MissionInfo>
        {
            new(1, 1, "Mission One", "The first briefing.", "mission1.spr"),
            new(2, 2, "Mission Two", "The second briefing.", "mission2.spr"),
        };

        GameConfig config = GameConfig.CreateDefault();
        if (config.Active is { } active)
        {
            active.ActiveMission = 2;
        }

        var context = new ShellContext(
            theme,
            host,
            new ScreenStack(),
            GameProfileResolver.BuildCatalogue("C:\\Games\\IGI", null, null),
            missions,
            new CVarRegistry(),
            GraphicsPresetRegistry.Default,
            config,
            new SaveSlotStore(savesDir));

        return (new Shell(context), host);
    }

    private static void Pump(Shell shell, float seconds = 0.6f)
    {
        var context = new LayoutContext(1920f, 1080f);
        var input = new InputState();
        for (float t = 0f; t < seconds; t += 1f / 60f)
        {
            input.BeginFrame();
            shell.Update(context, input, 1f / 60f);
        }
    }

    private static T? Find<T>(Screen screen, Func<T, bool> predicate)
        where T : UiElement =>
        screen.Root?.Descendants().OfType<T>().FirstOrDefault(predicate);

    private static void Type(Shell shell, string text)
    {
        var input = new InputState();
        input.BeginFrame();
        foreach (char character in text)
        {
            input.Keyboard.EnqueueCharacter(character);
        }

        shell.Update(new LayoutContext(1920f, 1080f), input, 1f / 60f);
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

            // Entering the campaign previews the first mission behind the menu.
            Assert.Equal(1, host.Previewed);
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
            Find<Button>(campaign, b => b.Text == "PLAY")!.Activate();

            Assert.Equal(1, host.StartedMission); // first mission selected by default
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
            Dropdown time = Find<Dropdown>(campaign, d => d.Label == "Time of day")!;
            Dropdown weather = Find<Dropdown>(campaign, d => d.Label == "Weather")!;

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
                (MissionTimeOfDay)Find<Dropdown>(reopened, d => d.Label == "Time of day")!.SelectedIndex);
            Assert.Equal(
                MissionWeather.Rain,
                (MissionWeather)Find<Dropdown>(reopened, d => d.Label == "Weather")!.SelectedIndex);
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
            Find<Button>(shell.Stack.Top!, b => b.Text == "RESUME")!.Activate();
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
            Assert.Equal(1, host.Previewed);
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
    public void Controls_tab_lists_the_full_retail_binding_set_for_a_fresh_profile()
    {
        string dir = Path.Combine(Path.GetTempPath(), "openigi-shell-tests", Guid.NewGuid().ToString("N"));
        try
        {
            (Shell shell, _) = BuildShell(dir);
            Pump(shell, 0.1f);
            Find<Button>(shell.Stack.Top!, button => button.Text == "Settings")!.Activate();
            Pump(shell, 0.6f);

            SettingsScreen screen = Assert.IsType<SettingsScreen>(shell.Stack.Top);
            TabStrip tabs = screen.Root!.Descendants().OfType<TabStrip>().First();
            tabs.SelectedIndex = 3; // Controls
            Pump(shell, 0.1f);

            Assert.Equal(PlayerProfile.BindingCount, screen.Root!.Descendants().OfType<KeyBindingRow>().Count());
            KeyBindingRow moveLeft = screen.Root.Descendants().OfType<KeyBindingRow>().Single(row => row.Action == "MoveLeft");
            KeyBindingRow fire = screen.Root.Descendants().OfType<KeyBindingRow>().Single(row => row.Action == "Fire");
            Assert.Equal(KeyCode.A, moveLeft.BoundKey);
            Assert.Equal("A", moveLeft.BoundKeyName);
            Assert.Equal("Mouse Button 1", fire.BoundKeyName);
            Assert.NotNull(Find<Button>(screen, button => button.Text == "RESET BINDINGS TO DEFAULTS"));
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
    public void Controls_tab_swaps_conflicting_assignments_and_refreshes_both_rows()
    {
        string dir = Path.Combine(Path.GetTempPath(), "openigi-shell-tests", Guid.NewGuid().ToString("N"));
        try
        {
            (Shell shell, _) = BuildShell(dir);
            Pump(shell, 0.1f);
            Find<Button>(shell.Stack.Top!, button => button.Text == "Settings")!.Activate();
            Pump(shell, 0.6f);

            SettingsScreen screen = Assert.IsType<SettingsScreen>(shell.Stack.Top);
            TabStrip tabs = screen.Root!.Descendants().OfType<TabStrip>().First();
            tabs.SelectedIndex = 3;
            Pump(shell, 0.1f);

            KeyBindingRow moveLeft = screen.Root!.Descendants().OfType<KeyBindingRow>()
                .Single(row => row.Action == "MoveLeft");
            KeyBindingRow fire = screen.Root.Descendants().OfType<KeyBindingRow>()
                .Single(row => row.Action == "Fire");
            KeyBindingRow nextWeapon = screen.Root.Descendants().OfType<KeyBindingRow>()
                .Single(row => row.Action == "NextWeapon");

            fire.Activate();
            Assert.True(fire.TryCaptureKey(KeyCode.A));

            Assert.Equal(KeyCode.A, fire.BoundKey);
            Assert.Equal("A", fire.BoundKeyName);
            Assert.Equal(KeyCode.None, moveLeft.BoundKey);
            Assert.Equal("Mouse Button 1", moveLeft.BoundKeyName);

            moveLeft.Activate();
            Assert.True(moveLeft.TryCaptureMouseButton(MouseButton.Right));

            Assert.Equal("Mouse Button 2", moveLeft.BoundKeyName);
            Assert.Equal("Mouse Button 1", nextWeapon.BoundKeyName);
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
    public void Controls_tab_waits_for_a_fresh_press_after_activation()
    {
        string dir = Path.Combine(Path.GetTempPath(), "openigi-shell-tests", Guid.NewGuid().ToString("N"));
        try
        {
            (Shell shell, _) = BuildShell(dir);
            Pump(shell, 0.1f);
            Find<Button>(shell.Stack.Top!, button => button.Text == "Settings")!.Activate();
            Pump(shell, 0.6f);

            SettingsScreen screen = Assert.IsType<SettingsScreen>(shell.Stack.Top);
            TabStrip tabs = screen.Root!.Descendants().OfType<TabStrip>().First();
            tabs.SelectedIndex = 3;
            Pump(shell, 0.1f);

            KeyBindingRow fire = screen.Root!.Descendants().OfType<KeyBindingRow>()
                .Single(row => row.Action == "Fire");
            UiVec2 centre = fire.Bounds.Center;
            InputState input = new();

            // A stale press is already queued when the pointer activates the row.
            input.Keyboard.SetKey(KeyCode.F, down: true);
            input.Keyboard.SetKey(KeyCode.F, down: false);
            input.BeginFrame();
            input.Mouse.SetPosition(centre.X, centre.Y);
            input.Mouse.SetButton(MouseButton.Left, down: true);
            shell.Update(new LayoutContext(1920f, 1080f), input, 1f / 60f);
            Assert.True(fire.IsListening);

            // The first listening update establishes the capture boundary and discards F.
            input.BeginFrame();
            input.Mouse.SetButton(MouseButton.Left, down: false);
            shell.Update(new LayoutContext(1920f, 1080f), input, 1f / 60f);
            Assert.True(fire.IsListening);
            Assert.Equal("Mouse Button 1", fire.BoundKeyName);

            // Only a press made after the prompt is armed can become the binding.
            input.BeginFrame();
            input.Keyboard.SetKey(KeyCode.G, down: true);
            shell.Update(new LayoutContext(1920f, 1080f), input, 1f / 60f);

            Assert.False(fire.IsListening);
            Assert.Equal(KeyCode.G, fire.BoundKey);
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
    public void Controls_tab_reset_button_restores_the_modern_default_layout()
    {
        string dir = Path.Combine(Path.GetTempPath(), "openigi-shell-tests", Guid.NewGuid().ToString("N"));
        try
        {
            (Shell shell, _) = BuildShell(dir);
            Pump(shell, 0.1f);
            Find<Button>(shell.Stack.Top!, button => button.Text == "Settings")!.Activate();
            Pump(shell, 0.6f);

            SettingsScreen screen = Assert.IsType<SettingsScreen>(shell.Stack.Top);
            TabStrip tabs = screen.Root!.Descendants().OfType<TabStrip>().First();
            tabs.SelectedIndex = 3;
            Pump(shell, 0.1f);

            KeyBindingRow moveUp = screen.Root!.Descendants().OfType<KeyBindingRow>()
                .Single(row => row.Action == "MoveUp");
            moveUp.Activate();
            Assert.True(moveUp.TryCaptureKey(KeyCode.F));
            Assert.Equal(KeyCode.F, moveUp.BoundKey);

            Find<Button>(screen, button => button.Text == "RESET BINDINGS TO DEFAULTS")!.Activate();

            Assert.Equal(KeyCode.W, moveUp.BoundKey);
            Assert.Equal(KeyCode.A, screen.Root.Descendants().OfType<KeyBindingRow>()
                .Single(row => row.Action == "MoveLeft").BoundKey);
            Assert.Equal(KeyCode.D, screen.Root.Descendants().OfType<KeyBindingRow>()
                .Single(row => row.Action == "MoveRight").BoundKey);
            Assert.Equal(KeyCode.S, screen.Root.Descendants().OfType<KeyBindingRow>()
                .Single(row => row.Action == "MoveDown").BoundKey);
            Assert.Equal(KeyCode.Space, screen.Root.Descendants().OfType<KeyBindingRow>()
                .Single(row => row.Action == "Jump").BoundKey);
            Assert.Equal(KeyCode.C, screen.Root.Descendants().OfType<KeyBindingRow>()
                .Single(row => row.Action == "MapComputer").BoundKey);
            Assert.Equal(KeyCode.E, screen.Root.Descendants().OfType<KeyBindingRow>()
                .Single(row => row.Action == "Activate").BoundKey);
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
    public void Profile_workspace_shows_progress_and_creates_a_named_profile_at_the_selected_difficulty()
    {
        string dir = Path.Combine(Path.GetTempPath(), "openigi-shell-tests", Guid.NewGuid().ToString("N"));
        try
        {
            (Shell shell, FakeHost host) = BuildShell(dir);
            Assert.True(shell.OpenOn("profiles"));
            Pump(shell, 0.1f);

            var screen = Assert.IsType<ProfilesScreen>(shell.Stack.Top);
            ProfileCard joe = screen.Root!.Descendants().OfType<ProfileCard>().Single(card => card.Visibility == Visibility.Visible);
            Assert.True(joe.Active);
            Assert.Equal("Joe", joe.Name);
            Assert.Contains("UNLOCKED", joe.Progress);
            Assert.Contains("Mission Two", joe.Mission);

            Find<Button>(screen, button => button.Text == "NEW PROFILE")!.Activate();
            Pump(shell, 0.1f); // lays out and focuses the form field
            Find<Button>(screen, button => button.Text == "CREATE PROFILE")!.Activate();
            Assert.NotNull(Find<Label>(screen, label => label.Text == "Enter a profile name to continue."));
            Type(shell, "Alex");
            Find<Dropdown>(screen, dropdown => dropdown.Label == "Difficulty")!.SelectedIndex = 2;
            Find<Button>(screen, button => button.Text == "CREATE PROFILE")!.Activate();
            Pump(shell, 0.1f);

            ProfileCard alex = screen.Root!.Descendants().OfType<ProfileCard>().Single(card => card.Visibility == Visibility.Visible && card.Name == "Alex");
            Assert.True(alex.Active);
            Assert.Equal("Hard difficulty", alex.Difficulty);
            Assert.Equal(1, host.SaveProfilesCalls);

            Find<Button>(screen, button => button.Text == "NEW PROFILE")!.Activate();
            Pump(shell, 0.1f);
            Type(shell, "aLeX");
            Find<Button>(screen, button => button.Text == "CREATE PROFILE")!.Activate();
            Assert.NotNull(Find<Label>(screen, label => label.Text == "That profile name is already in use."));
            Assert.Equal(1, host.SaveProfilesCalls);
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
    public void Profile_deletion_requires_confirmation_removes_its_saves_and_keeps_the_last_profile()
    {
        string dir = Path.Combine(Path.GetTempPath(), "openigi-shell-tests", Guid.NewGuid().ToString("N"));
        try
        {
            (Shell shell, FakeHost host) = BuildShell(dir);
            Assert.True(shell.OpenOn("profiles"));
            Pump(shell, 0.1f);
            var screen = Assert.IsType<ProfilesScreen>(shell.Stack.Top);

            Find<Button>(screen, button => button.Text == "NEW PROFILE")!.Activate();
            Pump(shell, 0.1f);
            Type(shell, "Alex");
            Find<Button>(screen, button => button.Text == "CREATE PROFILE")!.Activate();
            Pump(shell, 0.1f);
            Find<Button>(screen, button => button.Text == "SAVE")!.Activate();

            Find<Button>(screen, button => button.Text == "DELETE PROFILE")!.Activate();
            Pump(shell, 0.1f);
            Find<Button>(screen, button => button.Text == "DELETE PERMANENTLY")!.Activate();
            Pump(shell, 0.1f);

            List<ProfileCard> visible = [.. screen.Root!.Descendants().OfType<ProfileCard>().Where(card => card.Visibility == Visibility.Visible)];
            Assert.Single(visible);
            Assert.Equal("Joe", visible[0].Name);
            Assert.All(Find<ListView>(screen, list => list.Id == "profile-saves")!.Items, item => Assert.Contains("EMPTY", item));
            Assert.Equal(2, host.SaveProfilesCalls);
            Assert.False(Find<Button>(screen, button => button.Text == "DELETE PROFILE")!.IsEnabled);
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
    public void Controls_tab_binds_a_wheel_scroll_after_activation()
    {
        // Issue #232: scrolling while a row listens must bind the wheel direction like a button.
        string dir = Path.Combine(Path.GetTempPath(), "openigi-shell-tests", Guid.NewGuid().ToString("N"));
        try
        {
            (Shell shell, _) = BuildShell(dir);
            Pump(shell, 0.1f);
            Find<Button>(shell.Stack.Top!, button => button.Text == "Settings")!.Activate();
            Pump(shell, 0.6f);

            SettingsScreen screen = Assert.IsType<SettingsScreen>(shell.Stack.Top);
            TabStrip tabs = screen.Root!.Descendants().OfType<TabStrip>().First();
            tabs.SelectedIndex = 3;
            Pump(shell, 0.1f);

            KeyBindingRow fire = screen.Root!.Descendants().OfType<KeyBindingRow>()
                .Single(row => row.Action == "Fire");
            UiVec2 centre = fire.Bounds.Center;
            InputState input = new();

            input.BeginFrame();
            input.Mouse.SetPosition(centre.X, centre.Y);
            input.Mouse.SetButton(MouseButton.Left, down: true);
            shell.Update(new LayoutContext(1920f, 1080f), input, 1f / 60f);
            Assert.True(fire.IsListening);

            // The first listening update arms the boundary; a scroll here must not bind.
            input.BeginFrame();
            input.Mouse.SetButton(MouseButton.Left, down: false);
            input.Mouse.AddWheel(1f);
            shell.Update(new LayoutContext(1920f, 1080f), input, 1f / 60f);
            Assert.True(fire.IsListening);
            Assert.Equal("Mouse Button 1", fire.BoundKeyName);

            // A scroll after arming binds wheel-up and ends listening.
            input.BeginFrame();
            input.Mouse.AddWheel(-1f);
            shell.Update(new LayoutContext(1920f, 1080f), input, 1f / 60f);

            Assert.False(fire.IsListening);
            Assert.Equal(KeyCode.None, fire.BoundKey);
            Assert.Equal("Mouse Wheel Down", fire.BoundKeyName);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    // Escape, the way the shell reads it -- one frame with the key down against a frame without.
    private static void Back(Shell shell)
    {
        var input = new InputState();
        input.BeginFrame();
        input.Keyboard.SetKey(KeyCode.Escape, true);
        shell.Update(new LayoutContext(1920f, 1080f), input, 1f / 60f);
    }
}
