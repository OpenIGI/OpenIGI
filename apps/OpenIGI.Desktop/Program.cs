using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Desktop;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Menus;
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

IPlatformHost platform = new DesktopPlatformHost();
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
    window = (SilkGameWindow)platform.CreateWindow(settings);
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

    // The UI is authored against the original's 640x480 back buffer and is centred, never scaled.
    device.SetViewport(new Viewport(0, 0, window.Width, window.Height));
    device.Set2DProjection(MenuLayout.AuthoredWidth, MenuLayout.AuthoredHeight);
    window.Resized += (_, e) =>
    {
        device.SetViewport(new Viewport(0, 0, e.Width, e.Height));
        device.Set2DProjection(MenuLayout.AuthoredWidth, MenuLayout.AuthoredHeight);
    };

    // Entry labels are keys into the language archive, so it has to be registered before the menu can
    // resolve any of them.
    ResourceRegistry resources = new(fileSystem, logger);
    string languageArchive = MenuStringTable.GetArchiveName();
    if (fileSystem.Exists(languageArchive))
    {
        resources.Register(languageArchive, fileSystem.ReadAllBytes(languageArchive));
    }

    MenuStringTable strings = new(resources);
    SpriteBatch batch = new(device);
    MenuRenderer? renderer = null;
    bool running = true;

    DesktopMenuHost menuHost = new(
        window.Input,
        batch,
        () => renderer,
        () => window.Width,
        () => window.Height,
        () => running = false,
        logger);

    // The flow state machine owns the frame, and the menu is a task beneath it — so the frame walk
    // updates and renders the menu rather than this loop doing either by hand.
    GameLoop loop = new(host: menuHost, renderer: new DesktopRenderScheduler(device), logger: logger);

    menuHost.LoadMainMenu = () =>
    {
        MenuSystem menu = MenuSystem.Load(
            fileSystem.ReadAllBytes("LOCAL:menusystem/mainmenu.qvm"),
            loop,
            logger);

        renderer = new MenuRenderer(device, fileSystem, menu.Manager.ResourcePath, strings);

        // -screen is a development shortcut, not a game feature: the script decides where the menu
        // opens, and this overwrites that one parameter before the manager reads it.
        if (options.StartScreen > 0)
        {
            menu.Manager.CurrentMenuScreen = options.StartScreen;
        }

        menu.Manager.Initialize(menu, menuHost, logger);
    };

    loop.Flow.Request(FlowEvent.MainMenu);

    window.CloseRequested += (_, _) => running = false;
    window.Show();
    logger.Information("Desktop", $"{platform.Name} up on {device.Capabilities.Renderer}.");

    while (running && loop.IsRunning && !window.IsClosing)
    {
        window.PumpEvents();

        if (loop.RunFrame() != FrameOutcome.Render)
        {
            continue;
        }

        if (options.ExitAfterFrames > 0 && loop.Clock.FrameCount >= options.ExitAfterFrames)
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
    }

    renderer?.Dispose();
    logger.Information(
        "Desktop",
        $"Shutting down after {loop.Clock.TickCount} ticks and {loop.Clock.FrameCount} frames.");
}

return 0;
