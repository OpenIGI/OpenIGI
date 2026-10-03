using OpenIGI.Input;
using OpenIGI.Launcher.Services;
using OpenIGI.UI;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Screens;
using OpenIGI.UI.Skia;
using OpenIGI.UI.Widgets;
using SkiaSharp;
using Xunit;

namespace OpenIGI.Launcher.Tests;

/// <summary>
/// Draws every launcher page at a range of window sizes into PNGs, for checking the layout by eye, without a
/// window and without starting the launcher.
/// </summary>
/// <remarks>
/// Opt-in: set <c>OPENIGI_LAUNCHER_SHOTS</c> to a folder and run this test. It reads the player's real launcher
/// settings, installs and content (read only) so the pages show what they show at home, and writes
/// <c>&lt;section&gt;-&lt;width&gt;x&lt;height&gt;.png</c> for each page and size. Without the variable it does nothing.
/// </remarks>
public sealed class LauncherScreenshots
{
    private static readonly string[] Sections = ["home", "content", "mods", "logs", "settings"];

    // The minimum window, a small laptop, the design size, a large monitor, and a narrow tall window.
    private static readonly (int Width, int Height)[] Sizes = [(1280, 720), (1366, 768), (1600, 900), (1920, 1080), (2560, 1440), (1280, 1000)];

    [Fact]
    public void Draw_every_page_at_every_size()
    {
        string? folder = Environment.GetEnvironmentVariable("OPENIGI_LAUNCHER_SHOTS");
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        Directory.CreateDirectory(folder);
        using LauncherLog log = LauncherLog.Start(Path.Combine(folder, "log"));
        using LauncherModel model = new(log);
        using IgiTypography typography = LauncherFonts.Load();
        using SkiaImageStore images = new();
        using SkiaFontCache fonts = new();
        (UiTheme page, UiTheme rows) = LauncherApp.CreateThemes(typography);
        PictureWindow window = new(images);

        foreach ((int width, int height) in Sizes)
        {
            for (int section = 0; section < Sections.Length; section++)
            {
                LauncherScreen screen = new(page, rows, model, window, LauncherBackdrop.LoadPicture(images));
                ScreenStack stack = new() { PointerSuppressed = true };
                stack.Push(screen, animate: false);
                LayoutContext context = LauncherApp.Layout(width, height, 1f);
                InputState input = new();

                // A few frames for the tree to build and the entrance to finish, then the section, then settle.
                for (int frame = 0; frame < 90; frame++)
                {
                    if (frame == 2)
                    {
                        screen.ShowSection(section, refocus: false);
                    }

                    stack.Update(context, input, 1f / 30f);
                    input.BeginFrame();
                }

                using SKSurface surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
                surface.Canvas.Clear(new SKColor(10, 12, 13));
                using (SkiaUiRenderer renderer = new(surface.Canvas, images, fonts))
                {
                    stack.Draw(new UiPainter(renderer, context));
                    renderer.Flush();
                }

                using SKImage image = surface.Snapshot();
                using SKData png = image.Encode(SKEncodedImageFormat.Png, 90);
                using FileStream file = File.Create(Path.Combine(folder, $"{Sections[section]}-{width}x{height}.png"));
                png.SaveTo(file);
            }
        }
    }

    // Only what the pages ask of the window while they are drawn: pictures. Nothing here is clicked.
    private sealed class PictureWindow(SkiaImageStore images) : ILauncherWindow
    {
        private readonly Dictionary<string, BackdropPicture?> _pictures = new(StringComparer.Ordinal);

        public string ClipboardText { get; set; } = string.Empty;

        public void Minimize()
        {
        }

        public void ToggleMaximize()
        {
        }

        public void Close()
        {
        }

        public void Exit()
        {
        }

        public Task<string?> PickFolder(string title, string? initial) => Task.FromResult<string?>(null);

        public BackdropPicture? Picture(string path)
        {
            if (!_pictures.TryGetValue(path, out BackdropPicture? picture))
            {
                SKImage? image = File.Exists(path) ? SKImage.FromEncodedData(path) : null;
                picture = image is null ? null : new BackdropPicture(images.Add(image), image.Width / (float)image.Height);
                _pictures[path] = picture;
            }

            return picture;
        }
    }
}
