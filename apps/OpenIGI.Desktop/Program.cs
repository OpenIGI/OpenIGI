using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Desktop;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Menus;
using OpenIGI.Input;
using OpenIGI.Platform;
using OpenIGI.Platform.Desktop;
using OpenIGI.Rendering;
using OpenIGI.Scripting.Expressions;
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
    device.Set2DProjection(MenuLayout.AuthoredWidth, MenuLayout.AuthoredHeight);
    window.Resized += (_, e) =>
    {
        device.SetViewport(new Viewport(0, 0, e.Width, e.Height));
        device.Set2DProjection(MenuLayout.AuthoredWidth, MenuLayout.AuthoredHeight);
    };

    // The menu is data: run the script and draw the tree it builds.
    MenuSystem menu = MenuSystem.Load(fileSystem.ReadAllBytes("LOCAL:menusystem/mainmenu.qvm"), logger);
    MenuScreenTask? screen = menu.GetInitialScreen() ?? (menu.Screens.Count > 0 ? menu.Screens[0] : null);

    // Entry labels are keys into the language archive, so it has to be registered before the
    // renderer can resolve any of them.
    ResourceRegistry resources = new(fileSystem, logger);
    string languageArchive = MenuStringTable.GetArchiveName();
    if (fileSystem.Exists(languageArchive))
    {
        resources.Register(languageArchive, fileSystem.ReadAllBytes(languageArchive));
    }

    MenuStringTable strings = new(resources);
    using MenuRenderer renderer = new(device, fileSystem, menu.Manager.ResourcePath, strings);
    SpriteBatch batch = new(device);

    MenuPointer pointer = new();
    MenuNavigator navigator = new();
    navigator.Start(menu.Manager.CurrentMenuScreen);

    MenuScriptContext menuContext = new(navigator, logger);
    ScriptExpressionEvaluator evaluator = new(menuContext);

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
                if (window.Input.Keyboard.WasPressed(KeyCode.Escape))
                {
                    running = false;
                }

                // Advance the fade, and pick up the screen the navigator moved to.
                if (navigator.Tick() || screen is null)
                {
                    // A popup is a DialogWindow layered over the screen, not a screen of its own, so
                    // the screen underneath stays current while one is up.
                    screen = menu.FindScreen(navigator.CurrentScreen) ?? screen;
                }

                if (navigator.IsLeaving)
                {
                    logger.Information("Menu", $"Leaving the menus: {navigator.State}.");
                    running = false;
                }

                if (screen is not null && navigator.State == MenuManagerState.Active && menuContext.IsEnabled)
                {
                    pointer.Update(
                        (int)window.Input.Mouse.X,
                        (int)window.Input.Mouse.Y,
                        window.Width,
                        window.Height,
                        window.Input.Mouse.IsDown(MouseButton.Left));

                    if (screen.HitTestSubtree(pointer) is MenuTextTask chosen
                        && chosen.OnClickScript.Length > 0)
                    {
                        logger.Information("Menu", $"'{chosen.TextResource}' -> {chosen.OnClickScript}");
                        evaluator.Evaluate(ScriptExpressionParser.Parse(chosen.OnClickScript, MenuConstants));
                        if (evaluator.HasError)
                        {
                            logger.Warning("Menu", evaluator.ErrorMessage);
                        }
                    }
                }

                clock.CompleteTick();
                break;

            case FrameAction.Render:
                // Present first, then open the new frame: the order DisplaySwapBuffers uses.
                device.SwapBuffers();
                device.Clear(Bgra32.Black);
                if (screen is not null)
                {
                    renderer.Draw(batch, screen, MenuLayout.AuthoredWidth, MenuLayout.AuthoredHeight);
                }

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

static int? MenuConstants(string name) => name switch
{
    "TRUE" => 1,
    "FALSE" => 0,
    "MENUMANAGER_IDLE" => (int)MenuManagerState.Idle,
    "MENUMANAGER_QUIT" => (int)MenuManagerState.Quit,
    "MENUMANAGER_QUITTOMAINMENU" => (int)MenuManagerState.QuitToMainMenu,
    "MENUMANAGER_RESTARTLEVEL" => (int)MenuManagerState.RestartLevel,
    "MENUMANAGER_STARTGAME" => (int)MenuManagerState.StartGame,
    "TEXT_BLACK" => 0,
    "TEXT_WHITE" => 1,
    "TEXT_GREEN" => 2,
    "TEXT_ORANGE" => 3,
    _ => null,
};
