using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Desktop;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Platform;
using OpenIGI.Platform.Desktop;
using OpenIGI.Rendering;
using OpenIGI.Rendering.OpenGL;
using Silk.NET.OpenGL;

DesktopOptions options;
try
{
    options = DesktopOptions.Parse(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine();
    Console.Error.WriteLine(DesktopOptions.Usage);
    return 2;
}

IIgiLogger logger = new ConsoleLogger();

string? gamePath = options.GamePath;
if (string.IsNullOrWhiteSpace(gamePath) || !Directory.Exists(gamePath))
{
    Console.Error.WriteLine(
        gamePath is null
            ? $"No original installation configured. Pass -game <dir> or set {DesktopOptions.GamePathVariable}."
            : $"'{gamePath}' does not exist. Pass -game <dir> or set {DesktopOptions.GamePathVariable}.");
    return 2;
}

VirtualFileSystem fileSystem = new(logger);
fileSystem.RegisterDevice(new LocalFileDevice(gamePath));

IPlatformHost host = new DesktopPlatformHost();
WindowSettings settings = new(
    Title: "OpenIGI",
    Width: options.Width,
    Height: options.Height,
    Fullscreen: options.Fullscreen,
    CursorVisible: false,
    VSync: options.VSync);

SilkGameWindow window;
try
{
    window = (SilkGameWindow)host.CreateWindow(settings);
}
catch (PlatformException ex)
{
    logger.Error("Desktop", ex.Message, ex);
    return 3;
}

using (window)
{
    GL gl = GL.GetApi(window.GLContext);

    using GLGraphicsDevice device = new(
        gl,
        new DisplayMode(window.Width, window.Height, 32, 0),
        window.SwapBuffers,
        logger);

    // The UI is authored against the original's 640x480 back buffer; the device stretches it to the
    // window, which is what the original's windowed present does with its own back buffer.
    device.SetViewport(new Viewport(0, 0, window.Width, window.Height));
    device.Set2DProjection(PlaceholderMainMenu.AuthoredWidth, PlaceholderMainMenu.AuthoredHeight);
    window.Resized += (_, e) =>
    {
        device.SetViewport(new Viewport(0, 0, e.Width, e.Height));
        device.Set2DProjection(PlaceholderMainMenu.AuthoredWidth, PlaceholderMainMenu.AuthoredHeight);
    };

    using MenuAssets assets = MenuAssets.Load(fileSystem, device);
    PlaceholderMainMenu menu = new(assets);
    SpriteBatch batch = new(device);

    GameClock clock = new(new SystemTimeSource());
    bool running = true;
    window.CloseRequested += (_, _) => running = false;

    window.Show();
    logger.Information("Desktop", $"{host.Name} up on {device.Capabilities.Renderer}.");

    while (running && !window.IsClosing)
    {
        window.PumpEvents();

        switch (clock.Decide(clock.Now))
        {
            case FrameAction.Tick:
                menu.Update(window.Input);
                clock.CompleteTick();
                if (menu.QuitRequested)
                {
                    running = false;
                }

                break;

            case FrameAction.Render:
                // Present first, then open the new frame: the order DisplaySwapBuffers uses.
                device.SwapBuffers();
                device.Clear(Bgra32.Black);
                menu.Draw(batch);
                clock.CompleteRender();

                if (options.ExitAfterFrames > 0 && clock.FrameCount >= options.ExitAfterFrames)
                {
                    if (options.ScreenshotPath is { } screenshotPath)
                    {
                        Bgra32[] capture = new Bgra32[window.Width * window.Height];
                        device.CaptureFrame(window.Width, window.Height, capture);
                        BitmapWriter.Write(screenshotPath, window.Width, window.Height, capture);
                        logger.Information("Desktop", $"Wrote {window.Width}x{window.Height} capture to {screenshotPath}.");
                    }

                    running = false;
                }

                break;

            default:
                break;
        }
    }

    logger.Information(
        "Desktop",
        $"Shutting down after {clock.TickCount} ticks and {clock.FrameCount} frames.");
}

return 0;
