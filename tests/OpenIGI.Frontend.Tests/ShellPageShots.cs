using OpenIGI.Core.Configuration;
using OpenIGI.Core.Configuration.Presets;
using OpenIGI.Core.Titles;
using OpenIGI.Frontend.Saves;
using OpenIGI.Frontend.Screens;
using OpenIGI.Game.Config;
using OpenIGI.Input;
using OpenIGI.UI;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Screens;
using OpenIGI.UI.Skia;
using OpenIGI.UI.Text;
using SkiaSharp;
using Xunit;

namespace OpenIGI.Frontend.Tests;

/// <summary>
/// Draws every shell page to a PNG at a monitor's, a laptop's and a phone's size, so a page can be looked at without
/// starting the game. Not a test of anything: it runs only when <c>OPENIGI_SHELL_SHOTS</c> names a folder.
/// </summary>
/// <remarks>
/// <c>OPENIGI_SHELL_PAGES</c> narrows it to a comma-separated list of page names, and <c>OPENIGI_SHELL_GROUND</c> names a
/// picture to stand behind the pages in place of the drawn dusk -- a screenshot of a level, to judge them over one.
/// </remarks>
[Collection(ShellTestHarness.CollectionName)]
public sealed class ShellPageShots
{
    // As seen: a monitor, a small laptop, a 4:3 screen, and a phone held sideways.
    private static readonly (int Width, int Height, float Density)[] Sizes =
        [(1920, 1080, 1f), (1366, 768, 1f), (1024, 768, 1f), (844, 390, 1f)];

    private static readonly (string Name, Func<ShellContext, Screen> Create, bool InGame)[] Pages =
    [
        ("01-main-menu", static context => new MainMenuScreen(context), false),
        ("02-campaign", static context => new CampaignScreen(context), false),
        ("03-multiplayer-browse", static context => new MultiplayerScreen(context), false),
        ("04-multiplayer-host", static context => new MultiplayerScreen(context) { StartOnHost = true }, false),
        ("05-settings-graphics", static context => new SettingsScreen(context) { StartCategory = "graphics" }, false),
        ("06-settings-display", static context => new SettingsScreen(context) { StartCategory = "display" }, false),
        ("07-settings-audio", static context => new SettingsScreen(context) { StartCategory = "audio" }, false),
        ("08-settings-controls", static context => new SettingsScreen(context) { StartCategory = "controls" }, false),
        ("09-settings-gameplay", static context => new SettingsScreen(context) { StartCategory = "gameplay" }, false),
        ("10-profile", static context => new ProfilesScreen(context), false),
        ("10-profile-waiting", static context => new ProfilesScreen(context), false),
        ("10-profile-signed-in", static context => new ProfilesScreen(context), false),
        ("11-extras", static context => new ExtrasScreen(context), false),
        ("12-pause", static context => new PauseScreen(context), true),
        ("13-maps", static context => new MapsScreen(context), false),
    ];

    // The account each profile page is drawn with: signed out, waiting on Discord, or signed in.
    private static FakeAccount AccountFor(string page) => page switch
    {
        _ when page.EndsWith("signed-in", StringComparison.Ordinal) => new FakeAccount().SignedInAs("Sh"),
        _ when page.EndsWith("waiting", StringComparison.Ordinal) => new FakeAccount
        {
            Status = AccountStatus.WaitingForBrowser,
            SignInUrl = "https://master.openigi.com/v1/auth/discord/start?code=NSTG5TWS",
        },
        _ => new FakeAccount(),
    };

    [Fact]
    public void Draw_every_page()
    {
        string? folder = Environment.GetEnvironmentVariable("OPENIGI_SHELL_SHOTS");
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        string[] only = (Environment.GetEnvironmentVariable("OPENIGI_SHELL_PAGES") ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string? groundPath = Environment.GetEnvironmentVariable("OPENIGI_SHELL_GROUND");
        using SKBitmap? ground = !string.IsNullOrWhiteSpace(groundPath) && File.Exists(groundPath) ? SKBitmap.Decode(groundPath) : null;

        Directory.CreateDirectory(folder);
        using IgiTypography typography = IgiTypography.Load(MultiplayerScreenshots.LoadFont);
        using SkiaImageStore images = new();
        using SkiaFontCache fonts = new();
        Dictionary<int, MissionPicture?> pictures = [];

        foreach ((int width, int height, float density) in Sizes)
        {
            foreach ((string name, Func<ShellContext, Screen> create, bool inGame) in Pages)
            {
                if (only.Length > 0 && !only.Any(page => name.Contains(page, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var host = new FakeHost
                {
                    Coop = new MultiplayerScreenshots.FakeRooms { Listing = MultiplayerScreenshots.Games() },
                    Party = new MultiplayerScreenshots.FakeParty { Party = MultiplayerScreenshots.Squad() },
                    Pictures = mission => MultiplayerScreenshots.Picture(images, pictures, mission),
                    Maps = new FakeMaps(),
                    Account = AccountFor(name),
                };
                GameConfig config = GameConfig.CreateDefault();
                if (config.Active is { } active)
                {
                    active.ActiveMission = 9;
                }

                host.Profile = config.Active;
                var context = new ShellContext(
                    typography.CreateTheme(),
                    host,
                    new ScreenStack { PointerSuppressed = true },
                    GameProfileResolver.BuildCatalogue(GameTitle.Igi1, "C:\\Games\\IGI", null, null),
                    GameTitle.Igi1.Id,
                    Campaign(),
                    Settings(),
                    GraphicsPresetRegistry.Default,
                    config,
                    new SaveSlotStore(Path.Combine(folder, "saves")))
                {
                    InGame = inGame,
                };
                var shell = new Shell(context);
                context.Stack.Reset(create(context), animate: false);

                var layout = new LayoutContext(width, height, userScale: OpenIGI.Core.Graphics.UiScale.MenuBoost(width, height, density));
                UiFont.MinimumPixelHeight = OpenIGI.Core.Graphics.UiScale.MenuSmallestTextPixels * MathF.Max(1f, density);
                var input = new InputState();
                for (int frame = 0; frame < 90; frame++)
                {
                    input.BeginFrame();
                    shell.Update(layout, input, 1f / 30f);
                }

                using SKSurface surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
                if (ground is not null)
                {
                    using SKImage picture = SKImage.FromBitmap(ground);
                    surface.Canvas.DrawImage(picture, new SKRect(0f, 0f, width, height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
                }
                else
                {
                    MultiplayerScreenshots.DrawGround(surface.Canvas, width, height);
                }

                using (SkiaUiRenderer renderer = new(surface.Canvas, images, fonts))
                {
                    var painter = new UiPainter(renderer, layout);
                    shell.DrawBackdrop(painter);
                    shell.Draw(painter);
                    renderer.Flush();
                }

                UiFont.MinimumPixelHeight = 0f;
                using SKImage image = surface.Snapshot();
                using SKData png = image.Encode(SKEncodedImageFormat.Png, 90);
                string sizeFolder = Path.Combine(folder, $"{width}x{height}");
                Directory.CreateDirectory(sizeFolder);
                using FileStream file = File.Create(Path.Combine(sizeFolder, $"{name}.png"));
                png.SaveTo(file);
            }
        }
    }

    // The game's own settings, so the settings page has its rows.
    private static CVarRegistry Settings()
    {
        OpenIGI.Rendering.Generated.CVarRegistrations.RegisterAll();
        OpenIGI.Game.Generated.CVarRegistrations.RegisterAll();
        return CVarRegistry.Default;
    }

    // The game's fourteen, as the campaign page lists them.
    private static List<MissionInfo> Campaign()
    {
        string[] names =
        [
            "Trainyard", "SAM Base", "Military Airbase", "GOD", "Radar Base", "Get Priboi", "Border Crossing",
            "Re-Supply", "Missile Trainyard", "Defend Priboi", "Eagle's Nest I", "Eagle's Nest II", "Nuclear Infiltration", "Finding the Bomb",
        ];
        return [.. names.Select(static (name, index) => new MissionInfo(index + 1, index + 1, name, "Get in unseen, find what was taken, and get out again.", $"mission{index + 1}.spr"))];
    }

    /// <summary>A handful of community maps, one installed and one with a newer version out, for the Maps page's picture.</summary>
    private sealed class FakeMaps : IShellMaps
    {
        public IReadOnlyList<ShellMap> Maps() =>
        [
            new("night-raid", "Night Raid", "The trainyard by night: the train is in, the guards are tired, and the warhead is in the office.", "Kestrel", 3, 4.6, 41, 1280, 2_025_116, 3, Online: true),
            new("sam-sweep", "SAM Sweep", "Every launcher on the ridge, before the convoy comes through at dawn.", "Volkov", 2, 4.1, 17, 640, 5_400_000, 1, Online: true),
            new("airbase-silent", "Silent Airbase", "No alarms. Not one. The hangar codes are with the duty officer.", "Ana", 1, 3.8, 9, 312, 3_100_000, null, Online: true),
            new("border-run", "Border Run", "Two guard posts, one road, no night vision.", "Teo", 1, 0, 0, 44, 1_900_000, null, Online: true),
        ];

        public Task<string?> RefreshAsync() => Task.FromResult<string?>(null);

        public Task<string?> InstallAsync(string id) => Task.FromResult<string?>(null);

        public void Play(string id, int difficulty)
        {
        }

        public void Remove(string id)
        {
        }

        public Task<string?> RateAsync(string id, int stars) => Task.FromResult<string?>(null);

        public Task<string?> ReportAsync(string id, string reason) => Task.FromResult<string?>(null);
    }
}