using OpenIGI.Input;
using OpenIGI.Launcher.Pages;
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

    /// <summary>
    /// Draws the first-run guide's two steps over the home page, and a tab switch caught mid-flight, into the same
    /// folder: <c>onboarding-&lt;step&gt;-&lt;size&gt;.png</c> and <c>switch-&lt;frame&gt;.png</c>.
    /// </summary>
    /// <remarks>The guide is never finished here, so the player's settings are left as they are.</remarks>
    [Fact]
    public void Draw_the_first_run_guide_and_a_tab_switch()
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

        foreach ((int width, int height) in new[] { (1280, 720), (1920, 1080) })
        {
            for (int step = 0; step < 2; step++)
            {
                ScreenStack stack = new() { PointerSuppressed = true };
                stack.Push(new LauncherScreen(page, rows, model, window, LauncherBackdrop.LoadPicture(images)), animate: false);
                OnboardingScreen guide = new(page, rows, model, window);
                stack.Push(guide, animate: false);
                LayoutContext context = LauncherApp.Layout(width, height, 1f);
                InputState input = new();

                // Long enough for the search to come back and the step's slide to settle.
                for (int frame = 0; frame < 120; frame++)
                {
                    if (frame == 60 && step == 1)
                    {
                        guide.ShowStep(1);
                    }

                    stack.Update(context, input, 1f / 30f);
                    input.BeginFrame();
                    if (frame == 30)
                    {
                        Thread.Sleep(1500);
                    }
                }

                Save(stack, context, width, height, images, fonts, Path.Combine(folder, $"onboarding-{step + 1}-{width}x{height}.png"));
            }
        }

        // Home to Settings, drawn every other frame of the glide and the slide.
        {
            LauncherScreen screen = new(page, rows, model, window, LauncherBackdrop.LoadPicture(images));
            ScreenStack stack = new() { PointerSuppressed = true };
            stack.Push(screen, animate: false);
            LayoutContext context = LauncherApp.Layout(1600, 900, 1f);
            InputState input = new();
            for (int frame = 0; frame < 60; frame++)
            {
                stack.Update(context, input, 1f / 60f);
                input.BeginFrame();
            }

            // Drawn once on Home first, as the window would have: the underline glides from where it was drawn.
            Save(stack, context, 1600, 900, images, fonts, Path.Combine(folder, "switch-before.png"));
            screen.ShowSection(4, refocus: false);
            for (int frame = 0; frame <= 24; frame++)
            {
                stack.Update(context, input, 1f / 60f);
                input.BeginFrame();
                if (frame % 4 == 0)
                {
                    Save(stack, context, 1600, 900, images, fonts, Path.Combine(folder, $"switch-{frame:00}.png"));
                }
            }
        }
    }

    private static void Save(ScreenStack stack, LayoutContext context, int width, int height, SkiaImageStore images, SkiaFontCache fonts, string path)
    {
        using SKSurface surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.Clear(new SKColor(10, 12, 13));
        using (SkiaUiRenderer renderer = new(surface.Canvas, images, fonts))
        {
            stack.Draw(new UiPainter(renderer, context));
            renderer.Flush();
        }

        using SKImage image = surface.Snapshot();
        using SKData png = image.Encode(SKEncodedImageFormat.Png, 90);
        using FileStream file = File.Create(path);
        png.SaveTo(file);
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
