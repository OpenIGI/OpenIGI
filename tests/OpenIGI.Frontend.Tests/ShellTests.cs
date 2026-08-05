using System.Linq;
using OpenIGI.Core.Configuration;
using OpenIGI.Core.Graphics;
using OpenIGI.Frontend;
using OpenIGI.Frontend.Saves;
using OpenIGI.Frontend.Screens;
using OpenIGI.Game.Config;
using OpenIGI.Game.Profiles;
using OpenIGI.Input;
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

        public int ResumeCalls { get; private set; }

        public int RestartCalls { get; private set; }

        public int AbandonCalls { get; private set; }

        public void StartMission(int missionId, int difficulty)
        {
            StartedMission = missionId;
            StartedDifficulty = difficulty;
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

    [Fact]
    public void Settings_open_on_the_basic_graphics_page_with_advanced_a_tab_away()
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

            // Advanced is not a category beside Audio and Controls; it is the other half of Graphics.
            Assert.Equal(["Graphics", "Display", "Audio", "Controls", "Gameplay"], tabs.Tabs);
            Assert.Equal(0, tabs.SelectedIndex);

            Button basic = Find<Button>(screen, b => b.Text == "Basic")!;
            Button advanced = Find<Button>(screen, b => b.Text == "Advanced")!;
            Assert.True(basic.Selected);
            Assert.False(advanced.Selected);

            // The preset is on the basic half — it is the one control that answers the whole question.
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

    // Escape, the way the shell reads it -- one frame with the key down against a frame without.
    private static void Back(Shell shell)
    {
        var input = new InputState();
        input.BeginFrame();
        input.Keyboard.SetKey(KeyCode.Escape, true);
        shell.Update(new LayoutContext(1920f, 1080f), input, 1f / 60f);
    }
}
