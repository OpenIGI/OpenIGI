using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
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

    // The magic-object table is what turns a level's few hundred scripted objects into its full
    // contents: every model's attachments name an entry here. Loading it is a startup step, like the
    // mission scan, because a level's own scripts never mention it.
    MagicObjectRegistry magic = new(logger);
    magic.Register(loop.Natives);

    try
    {
        missions.Scan(fileSystem, resources, loop.Natives);
        magic.Load(resources, loop.Natives);
        configHost.Missions = missions;
    }
    catch (EngineFatalException ex)
    {
        logger.Warning("Mission", $"The mission scan failed, so no missions are selectable: {ex.Message}");
    }

    bool reportedWorld = false;
    WorldScene? scene = null;
    WorldRenderer? world = null;
    FreeCamera? camera = null;

    void StartMission(int id)
    {
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

        scene = WorldScene.Build(loaded, levelRoot, magic, logger);
        world = new WorldRenderer(device, resources, logger);

        // Open on the middle of what was placed, backed off and looking down slightly, so the first
        // frame shows the level rather than the inside of whichever object happens to be at the origin.
        camera = CreateCamera(scene);

        // The menus are done; without dropping the renderer the last screen would keep drawing over
        // the world.
        renderer = null;
        logger.Information("Mission", $"Loaded {loaded}.");
    }

    menuHost.StartGame = () => StartMission(missionState.PendingMissionId);

    static FreeCamera CreateCamera(WorldScene scene)
    {
        if (scene.Placements.Count == 0)
        {
            return new FreeCamera(default);
        }

        // Medians, not a bounding box or a mean: a level is a dense cluster with a few far-flung
        // outliers, so a box centre lands in the empty part and a mean gets dragged towards whatever is
        // furthest away. The median height is the ground, because most objects sit on it.
        double[] xs = new double[scene.Placements.Count];
        double[] ys = new double[scene.Placements.Count];
        double[] zs = new double[scene.Placements.Count];

        for (int i = 0; i < scene.Placements.Count; i++)
        {
            xs[i] = scene.Placements[i].Position.X;
            ys[i] = scene.Placements[i].Position.Y;
            zs[i] = scene.Placements[i].Position.Z;
        }

        Array.Sort(xs);
        Array.Sort(ys);
        Array.Sort(zs);

        double centreX = xs[xs.Length / 2];
        double centreY = ys[ys.Length / 2];
        double ground = zs[zs.Length / 2];

        // Standing back a hundred metres, twenty-five up, looking north and slightly down.
        return new FreeCamera(
            new Vector3d(centreX, centreY - 100.0, ground + 25.0),
            yaw: 0f,
            pitch: -0.12f);
    }

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

        // Game_SetMission belongs to the game, not the menu system, and a menu expression is evaluated
        // by the menu's own evaluator rather than by the script VM — so registering it with the VM is
        // not enough to make the Select Mission screen's OK button work.
        menu.Manager.ExternalNatives = missionState.TryInvokeFromMenu;
        menu.Manager.Initialize(menu, menuHost, logger);
    };

    // -mission is a development shortcut: it skips the menus and drops straight into a level, which is
    // how the fly-through gets captured without anyone clicking OK.
    if (options.StartMission > 0)
    {
        StartMission(options.StartMission);
    }
    else
    {
        loop.Flow.Request(FlowEvent.MainMenu);
    }

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

        if (scene is not null && world is not null && camera is not null)
        {
            camera.Update(window.Input, 1f / 30f, mouseLook: false);
            world.Draw(
                scene,
                camera.Camera,
                Matrix4f.CreatePerspective(
                    verticalFieldOfView: 1.2f,
                    aspectRatio: (float)window.Width / Math.Max(1, window.Height),
                    near: 0.2f,
                    far: 4000f));

            if (!reportedWorld)
            {
                reportedWorld = true;
                logger.Information(
                    "World",
                    $"Drew {world.LastDrawCount} sub-mesh(es), {world.LastLightmappedCount} lightmapped, {world.LastTriangleCount} triangles from {camera.Position}.");
            }
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
