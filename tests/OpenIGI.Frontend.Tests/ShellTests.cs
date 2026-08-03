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

        public void StartMission(int missionId, int difficulty)
        {
            StartedMission = missionId;
            StartedDifficulty = difficulty;
        }

        public void Quit() => Quit_Called = true;

        public void PreviewMission(int? missionId) => Previewed = missionId;

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

        var context = new ShellContext(
            theme,
            host,
            new ScreenStack(),
            GameProfileResolver.BuildCatalogue("C:\\Games\\IGI", null, null),
            missions,
            new CVarRegistry(),
            GraphicsPresetRegistry.Default,
            GameConfig.CreateDefault(),
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
    public void Activating_campaign_pushes_the_mission_carousel_bound_to_the_missions()
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
            Carousel carousel = Find<Carousel>(screen, _ => true)!;
            Assert.Equal(2, carousel.Items.Count);
            Assert.Contains("Mission One", carousel.Items[0]);

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
            Find<Button>(campaign, b => b.Text == "Play")!.Activate();

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
}
