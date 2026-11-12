using OpenIGI.Core.Configuration;
using OpenIGI.Core.Configuration.Presets;
using OpenIGI.Core.Titles;
using OpenIGI.Formats.Textures;
using OpenIGI.Frontend.Saves;
using OpenIGI.Frontend.Screens;
using OpenIGI.Game.Config;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.Input;
using OpenIGI.UI;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Screens;
using OpenIGI.UI.Skia;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;
using SkiaSharp;
using Xunit;

namespace OpenIGI.Frontend.Tests;

/// <summary>
/// Draws the multiplayer page into PNGs -- browsing a list of games, browsing none, hosting, joining and failing to -- for checking its look by
/// eye, without a window and without starting the game.
/// </summary>
/// <remarks>
/// Opt-in: set <c>OPENIGI_SHELL_SHOTS</c> to a folder and run this test; it writes <c>multiplayer-&lt;state&gt;-&lt;width&gt;x&lt;height&gt;.png</c>
/// there. Without the variable it does nothing. The level behind the menu is not there to blur, so the page stands on a
/// plain dusk-coloured ground.
/// </remarks>
[Collection(ShellTestHarness.CollectionName)]
public sealed class MultiplayerScreenshots
{
    private static readonly (int Width, int Height)[] Sizes = [(1920, 1080), (1366, 768)];

    [Fact]
    public void Draw_the_multiplayer_page()
    {
        string? folder = Environment.GetEnvironmentVariable("OPENIGI_SHELL_SHOTS");
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        Directory.CreateDirectory(folder);
        using IgiTypography typography = IgiTypography.Load(LoadFont);
        using SkiaImageStore images = new();
        using SkiaFontCache fonts = new();
        Dictionary<int, MissionPicture?> pictures = [];

        foreach ((int width, int height) in Sizes)
        {
            foreach (string state in new[] { "browse", "empty", "host", "switch", "glide", "connecting", "failed" })
            {
                var rooms = new FakeRooms
                {
                    Listing = state == "empty" ? [] : Games(),
                    Status = state switch
                    {
                        "connecting" => new CoopStatus(CoopPhase.Connecting),
                        "failed" => new CoopStatus(CoopPhase.Failed, Error: "the server did not answer"),
                        _ => default,
                    },
                };
                var host = new FakeHost
                {
                    Coop = rooms,
                    Party = state == "empty" ? new FakeParty() : new FakeParty { Party = Squad() },
                    Pictures = mission => Picture(images, pictures, mission),
                };
                GameConfig config = GameConfig.CreateDefault();
                host.Profile = config.Active;
                var context = new ShellContext(
                    typography.CreateTheme(),
                    host,
                    new ScreenStack { PointerSuppressed = true },
                    GameProfileResolver.BuildCatalogue(GameTitle.Igi1, "C:\\Games\\IGI", null, null),
                    GameTitle.Igi1.Id,
                    Missions(),
                    new CVarRegistry(),
                    GraphicsPresetRegistry.Default,
                    config,
                    new SaveSlotStore(Path.Combine(folder, "saves")));
                var shell = new Shell(context);
                context.Stack.Reset(new MultiplayerScreen(context), animate: false);

                var layout = new LayoutContext(width, height);
                var input = new InputState();
                for (int frame = 0; frame < 90; frame++)
                {
                    input.BeginFrame();
                    if (frame == 30 && state is "host" or "switch")
                    {
                        input.Keyboard.SetKey(KeyCode.Tab, true);
                    }
                    else if (frame == 31)
                    {
                        input.Keyboard.SetKey(KeyCode.Tab, false);
                    }

                    // E steps to the next section; two frames on, the header's underline is caught between the two tabs.
                    if (state == "glide")
                    {
                        input.Keyboard.SetKey(KeyCode.E, frame == 30);
                        if (frame == 33)
                        {
                            break;
                        }
                    }

                    shell.Update(layout, input, 1f / 30f);

                    // The frame the tab is switched on is drawn as it stands: the tab just shown must already be in place.
                    if (frame == 30 && state == "switch")
                    {
                        break;
                    }
                }

                using SKSurface surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
                DrawGround(surface.Canvas, width, height);
                using (SkiaUiRenderer renderer = new(surface.Canvas, images, fonts))
                {
                    var painter = new UiPainter(renderer, layout);
                    shell.DrawBackdrop(painter);
                    shell.Draw(painter);
                    renderer.Flush();
                }

                using SKImage image = surface.Snapshot();
                using SKData png = image.Encode(SKEncodedImageFormat.Png, 90);
                using FileStream file = File.Create(Path.Combine(folder, $"multiplayer-{state}-{width}x{height}.png"));
                png.SaveTo(file);
            }
        }
    }

    // Something for the glass to stand on: a dusk sky over dark hills, about as bright as a level behind the menu.
    private static void DrawGround(SKCanvas canvas, int width, int height)
    {
        using var sky = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(0f, 0f),
                new SKPoint(0f, height),
                [new SKColor(96, 104, 112), new SKColor(150, 128, 104), new SKColor(52, 58, 50)],
                [0f, 0.55f, 1f],
                SKShaderTileMode.Clamp),
        };
        canvas.DrawRect(0f, 0f, width, height, sky);
        using var hill = new SKPaint { Color = new SKColor(38, 44, 40), IsAntialias = true };
        using var path = new SKPath();
        path.MoveTo(0f, height * 0.62f);
        path.CubicTo(width * 0.25f, height * 0.42f, width * 0.45f, height * 0.7f, width * 0.62f, height * 0.55f);
        path.CubicTo(width * 0.78f, height * 0.42f, width * 0.9f, height * 0.6f, width, height * 0.5f);
        path.LineTo(width, height);
        path.LineTo(0f, height);
        path.Close();
        canvas.DrawPath(path, hill);
    }

    // A mission's shipped picture, read by the game's own JPEG reader, or none when the mission has no file.
    private static MissionPicture? Picture(SkiaImageStore images, Dictionary<int, MissionPicture?> known, int mission)
    {
        if (!known.TryGetValue(mission, out MissionPicture? picture))
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Assets", "missions", GameTitle.Igi1.Id, $"{mission}.jpg");
            if (File.Exists(path))
            {
                TextureImage image = JpegImage.Read(File.ReadAllBytes(path));
                picture = new MissionPicture(images.CreateImage(image.Width, image.Height, image.GetLevel(0)), image.Width, image.Height);
            }

            known[mission] = picture;
        }

        return picture;
    }

    private static UiFont LoadFont(IgiFontRequest request)
    {
        byte[] data = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "fonts", request.FileName));
        return UiFont.ForMeasurement(FontAtlasBuilder.Build(data, request.PixelHeight), data);
    }

    private static List<MissionInfo> Missions() =>
    [
        new(1, 1, "Trainyard", "The first briefing.", "mission1.spr"),
        new(2, 2, "SAM Base", "The second briefing.", "mission2.spr"),
        new(3, 3, "Military Airbase", "The third briefing.", "mission3.spr"),
        new(4, 4, "GOD", "The fourth briefing.", "mission4.spr"),
        new(5, 5, "Radar Base", "The fifth briefing.", "mission5.spr"),
    ];

    private static List<CoopRoomInfo> Games() =>
    [
        new("EU1", "OpenIGI Official · Frankfurt #1", 1, 11, 16, false) { Mode = GameModes.TeamDeathmatch, PingMs = 38, Region = "eu", Dedicated = true, Favourite = true },
        new("EU2", "OpenIGI Official · Frankfurt #2", 3, 16, 16, false) { Mode = GameModes.SearchAndDestroy, PingMs = 41, Region = "eu", Dedicated = true },
        new("K7QX2M", "Anya's room", 2, 2, 4, false) { Mode = GameModes.Coop, Region = string.Empty },
        new("IN1", "Mumbai Regulars", 5, 7, 16, true) { Mode = GameModes.FreeForAll, PingMs = 121, Region = "in", Dedicated = true, Recent = true },
        new("LAN1", "Living room", 1, 1, 4, false) { Mode = GameModes.Coop, PingMs = 2, Lan = true },
        new("US1", "East Coast Elimination with a very long server name that will not fit", 4, 9, 12, false) { Mode = GameModes.Elimination, PingMs = 164, Region = "us", Dedicated = true },
        new("OLD1", "Night Shift (old build)", 2, 3, 16, false) { Mode = GameModes.TeamDeathmatch, PingMs = 88, Region = "eu", Dedicated = true, Compatible = false },
        new("P4T9ZA", "Jones and friends", 3, 3, 4, true) { Mode = GameModes.Coop },
        new("EU3", "Covert Ops 24/7", 5, 0, 16, false) { Mode = GameModes.SearchAndDestroy, PingMs = 57, Region = "eu", Dedicated = true },
    ];

    private static PartyView Squad() => new(
        "R3D7KQ",
        "Jones",
        true,
        [new(1, "Jones", true, true, "in the menus"), new(2, "Anya", false, false, "in the menus"), new(3, "Priboi", false, false, "in the menus")],
        null);

    private sealed class FakeRooms : ICoopRooms
    {
        public CoopStatus Status { get; init; }

        public IReadOnlyList<CoopRoomInfo>? Listing { get; init; }

        public string? ListingError => null;

        public IReadOnlyList<string> Players => [];

        public bool CanRetryFromCheckpoint => false;

        public bool CanHostServer => true;

        public string? LastPlayed => "Mumbai Regulars";

        public DateTimeOffset? LastPlayedAt => DateTimeOffset.UtcNow.AddMinutes(-42);

        public void Host(int missionId, int difficulty, bool listed, string mode = "coop")
        {
        }

        public void Join(string code)
        {
        }

        public void Kick(string name)
        {
        }

        public void RetryFromCheckpoint()
        {
        }

        public void Leave()
        {
        }

        public void RefreshListing()
        {
        }

        public void PreviewLook(string? skinId)
        {
        }

        public void TurnLook(float radians)
        {
        }
    }

    private sealed class FakeParty : IPartyPanel
    {
        public PartyView? Party { get; init; }

        public string? Failure => null;

        public string? OrderError => null;

        public void Create()
        {
        }

        public void Join(string code)
        {
        }

        public void Leave()
        {
        }

        public void Kick(int member)
        {
        }

        public void Promote(int member)
        {
        }

        public string? CopyInvite() => null;
    }
}
