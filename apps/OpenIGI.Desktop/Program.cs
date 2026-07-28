using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Desktop;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Config;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Game.Menus;
using OpenIGI.Game.World;
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

string? gamePath = options.GamePath ?? @"S:\I'm Going In\pc";
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

    // The UI is authored against the original's 640x480 back buffer and is centred, NEVER SCALED, so
    // the projection is the back buffer's own size and MenuLayout.ToScreen does the centring. Passing
    // the authored size here instead scales 640x480 up to fill the window AND then scales the centring
    // offset with it, which puts the menu in the bottom-right corner of anything but a 640x480 window.
    Resize(window.Width, window.Height);
    window.Resized += (_, e) => Resize(e.Width, e.Height);

    void Resize(int width, int height)
    {
        device.SetViewport(new Viewport(0, 0, width, height));
        device.Set2DProjection(width, height);
    }

    // Entry labels are keys into the language archive, so it has to be registered before the menu can
    // resolve any of them.
    ResourceRegistry resources = new(fileSystem, logger);

    // menusystem.res holds the menu's own labels; missions.res holds the mission names and briefings,
    // because a descriptor's "Mission 1 Name" is a key into it rather than the title itself.
    foreach (string archive in new[] { MenuStringTable.GetArchiveName(), MenuStringTable.GetMissionArchiveName() })
    {
        if (fileSystem.Exists(archive))
        {
            resources.Register(archive, fileSystem.ReadAllBytes(archive));
        }
    }

    // Settings load from the retail config.qvm, and from our own file in preference to it when one
    // exists. Nothing here ever writes into the original installation.
    GameConfig configuration = LoadConfiguration(fileSystem, logger);
    DesktopConfigHost configHost = new(device, window)
    {
        ApplyResolution = (w, h) =>
        {
            window.Resize(w, h);
            logger.Information("Config", $"Resolution set to {w}x{h}.");
        },
    };

    ConfigNatives configNatives = new(configuration, configHost);

    MenuStringTable strings = new(resources);
    configHost.Strings = strings;
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

    // The mission table is built the way the original builds it: walk LOCAL:missions and run every
    // mission.qvm, each of which publishes one descriptor through DefineMission. It has to happen
    // before the Select Mission screen asks for the list.
    MissionRegistry missions = new(logger);
    missions.Register(loop.Natives);
    GameMissionState missionState = new();
    missionState.Register(loop.Natives);

    try
    {
        missions.Scan(fileSystem, resources, loop.Natives);
        configHost.Missions = missions;
    }
    catch (EngineFatalException ex)
    {
        logger.Warning("Mission", $"The mission scan failed, so no missions are selectable: {ex.Message}");
    }

    menuHost.StartGame = () =>
    {
        int id = missionState.PendingMissionId;
        if (!missions.TryGet(id, out MissionDescriptor? chosen))
        {
            logger.Warning("Mission", $"Asked to start mission {id}, which is not registered.");
            return;
        }

        logger.Information("Mission", $"Starting {chosen}.");
        LevelTask levelRoot = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        LevelLoader levelLoader = new(levelRoot, loop.TaskCommands, loop.Natives, resources, logger);
        MissionLoader missionLoader = new(missions, fileSystem, resources, levelLoader, logger);
        LoadedMission loaded = missionLoader.Load(id);
        logger.Information("Mission", $"Loaded {loaded}.");
    };

    menuHost.LoadMainMenu = () =>
    {
        MenuSystem menu = MenuSystem.Load(
            fileSystem.ReadAllBytes("LOCAL:menusystem/mainmenu.qvm"),
            loop,
            logger);

        // The mission briefing images are in their own archive beside the menu's.
        renderer = new MenuRenderer(
            device,
            fileSystem,
            menu.Manager.ResourcePath,
            strings,
            ["LOCAL:menusystem/missionsprites.res"]);

        // -screen is a development shortcut, not a game feature: the script decides where the menu
        // opens, and this overwrites that one parameter before the manager reads it.
        if (options.StartScreen > 0)
        {
            menu.Manager.CurrentMenuScreen = options.StartScreen;
        }

        menu.Manager.Configuration = configNatives;
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

    SaveConfiguration(gamePath, configuration, logger);
    renderer?.Dispose();
    logger.Information(
        "Desktop",
        $"Shutting down after {loop.Clock.TickCount} ticks and {loop.Clock.FrameCount} frames.");
}

return 0;


static GameConfig LoadConfiguration(VirtualFileSystem fileSystem, IIgiLogger logger)
{
    // Ours wins when it is there, so a setting changed in OpenIGI survives even though the retail
    // file is still the fallback.
    foreach (string path in new[] { ConfigScript.OpenIgiPath, ConfigScript.RetailPath })
    {
        if (!fileSystem.Exists(path))
        {
            continue;
        }

        try
        {
            return ConfigScript.Load(fileSystem.ReadAllBytes(path), logger);
        }
        catch (Exception ex) when (ex is IOException or QvmFormatException)
        {
            logger.Warning("Config", $"Could not read {path}: {ex.Message}");
        }
    }

    logger.Information("Config", "No configuration found; starting from the defaults.");
    return GameConfig.CreateDefault();
}

static void SaveConfiguration(string gamePath, GameConfig configuration, IIgiLogger logger)
{
    try
    {
        // Written next to the retail file rather than over it, so an original installation is never
        // modified. The format is identical, so copying it across would work.
        string path = Path.Combine(gamePath, "openigi-config.qvm");
        File.WriteAllBytes(path, ConfigScriptWriter.Write(configuration));
        logger.Information("Config", $"Saved {configuration.Profiles.Count} profile(s) to {path}.");
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        logger.Warning("Config", $"Could not save the configuration: {ex.Message}");
    }
}
