using OpenIGI.Core.Configuration;
using OpenIGI.Core.Configuration.Presets;
using OpenIGI.Core.Titles;
using OpenIGI.Frontend.Saves;
using OpenIGI.Frontend.Screens;
using OpenIGI.Game.Config;
using OpenIGI.Input;
using OpenIGI.Releases.Accounts;
using OpenIGI.UI;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Kit.Leaderboards;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Screens;
using OpenIGI.UI.Skia;
using OpenIGI.UI.Text;
using SkiaSharp;
using Xunit;

namespace OpenIGI.Frontend.Tests;

/// <summary>
/// Draws the Leaderboard page into PNGs -- the board, a record, a board being read, an empty one, one that can't be read, all
/// time -- and the landing page with its way in, for checking their look by eye, without a window and without the servers.
/// </summary>
/// <remarks>
/// Opt-in: set <c>OPENIGI_SHELL_SHOTS</c> to a folder and run this test; it writes <c>leaderboard-&lt;state&gt;-&lt;width&gt;x&lt;height&gt;.png</c>
/// there. Without the variable it does nothing.
/// </remarks>
[Collection(ShellTestHarness.CollectionName)]
public sealed class LeaderboardScreenshots
{
    // A desktop, a laptop, and a phone held sideways: the menu's three arrangements.
    private static readonly (int Width, int Height, float Density)[] Sizes = [(1920, 1080, 1f), (1366, 768, 1f), (2532, 1170, 3f)];

    private static readonly string[] States =
    [
        "board", "record", "loading", "empty", "failed", "alltime", "signedout", "notranked", "home", "missions", "missions-all", "missions-empty",
    ];

    [Fact]
    public void Draw_the_leaderboard_page()
    {
        string? folder = Environment.GetEnvironmentVariable("OPENIGI_SHELL_SHOTS");
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        Directory.CreateDirectory(folder);
        using IgiTypography typography = IgiTypography.Load(MultiplayerScreenshots.LoadFont);
        using SkiaImageStore images = new();
        using SkiaFontCache fonts = new();

        foreach ((int width, int height, float density) in Sizes)
        {
            string only = Environment.GetEnvironmentVariable("OPENIGI_SHELL_PAGES") ?? string.Empty;
            foreach (string state in States.Where(state => only.Length == 0 || only.Split(',').Contains(state)))
            {
                var reader = new FakeBoardReader
                {
                    Hold = state == "loading",
                    Fail = state == "failed",
                    RowCount = state is "empty" or "missions-empty" ? 0 : 24,
                    Unranked = state is "notranked" or "signedout",
                    LatestCount = 10,
                };
                var boards = new FakeLeaderboard(reader) { SignInHint = state == "signedout" ? "Sign in with Discord on the Profile page to be on the board under your name." : null };
                if (state is "board" or "notranked" or "signedout")
                {
                    // The panel beside the board reads the selected row's record once the keys rest on it: read the first's now.
                    boards.Feed.Peek("900000000000000000");
                }

                if (state == "record")
                {
                    boards.Feed.OpenRecord("900000000000000000");
                }
                else if (state is "missions" or "missions-empty")
                {
                    boards.Missions.ShowMission(1);
                }
                else if (state == "alltime")
                {
                    boards.Feed.ShowPeriod(LeaderboardSeasons.AllTime);
                    boards.Feed.ShowMetric(LeaderboardMetric.Of("kd"));
                }

                var host = new FakeHost { Leaderboard = boards, Replays = new ShellPageShots.FakeReplays() };
                GameConfig config = GameConfig.CreateDefault();
                host.Profile = config.Active;
                var context = new ShellContext(
                    typography.CreateTheme(),
                    host,
                    new ScreenStack { PointerSuppressed = true },
                    GameProfileResolver.BuildCatalogue(GameTitle.Igi1, "C:\\Games\\IGI", null, null),
                    GameTitle.Igi1.Id,
                    [new(1, 1, "Trainyard", "The first briefing.", "mission1.spr"), new(2, 2, "SAM Base", "The second briefing.", "mission2.spr")],
                    new CVarRegistry(),
                    GraphicsPresetRegistry.Default,
                    config,
                    new SaveSlotStore(Path.Combine(folder, "saves")));
                var shell = new Shell(context);
                context.Stack.Reset(
                    state == "home" ? new MainMenuScreen(context) : new LeaderboardScreen(context) { StartOnMissions = state.StartsWith("missions", StringComparison.Ordinal) },
                    animate: false);

                var layout = new LayoutContext(width, height, userScale: OpenIGI.Core.Graphics.UiScale.MenuBoost(width, height, density));
                UiFont.MinimumPixelHeight = OpenIGI.Core.Graphics.UiScale.MenuSmallestTextPixels * MathF.Max(1f, density);
                var input = new InputState();
                for (int frame = 0; frame < 90; frame++)
                {
                    input.BeginFrame();
                    shell.Update(layout, input, 1f / 30f);
                }

                using SKSurface surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
                MultiplayerScreenshots.DrawGround(surface.Canvas, width, height);
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
                using FileStream file = File.Create(Path.Combine(folder, $"leaderboard-{state}-{width}x{height}.png"));
                png.SaveTo(file);
            }
        }
    }
}
