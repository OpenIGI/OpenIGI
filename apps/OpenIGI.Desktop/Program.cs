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
using OpenIGI.Game.Player;
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

DesktopPlatformHost platform = new();
WindowSettings settings = new(
    Title: "OpenIGI",
    Width: options.Width,
    Height: options.Height,
    Mode: options.Mode,
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
    ReportDisplay(window, options, logger);

    GL gl = GL.GetApi(window.GLContext);

    using GLGraphicsDevice device = new(
        gl,
        new DisplayMode(window.Width, window.Height, 32, window.RefreshRate),
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
            if (window.Mode == WindowMode.BorderlessFullscreen)
            {
                logger.Information(
                    "Config",
                    $"Resolution {w}x{h} was chosen but the window is borderless fullscreen, whose size "
                        + "is the desktop's. Run with -window or -fullscreen for it to take effect.");
                return;
            }

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
    DesktopRenderScheduler scheduler = new(device);
    GameLoop loop = new(host: menuHost, renderer: scheduler, logger: logger);

    // The arbiter renders at most once per simulation tick, so the picture is 30 Hz however fast the
    // monitor is. Lifting the latch presents every vsync instead and leaves the tick schedule alone.
    loop.Clock.RenderEveryFrame = options.UncapFrameRate;

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

    // Without these the level's own HumanPlayer task is never built, and the spawn the mission
    // declares cannot be found - the tree loads perfectly well without it and simply has no player.
    PlayerTaskTypes.RegisterAll(loop.Types);
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
    TerrainRenderer? terrain = null;
    SkyRenderer? sky = null;
    FreeCamera? camera = null;

    // The walking player. F1 swaps between it and the free camera: the camera is how a level is
    // inspected, the player is how it is stood in. Both draw through the same Camera3D.
    PlayerCamera? player = null;
    TerrainData? playerTerrain = null;
    bool playerMode = false;

    // The camera's projection, in one place: the terrain traversal needs the same field of view the
    // projection matrix is built from, or it would cull against a frustum the picture does not have.
    // The far plane is shared with the sky, which sizes its dome from it.
    const float verticalFieldOfView = 1.2f;
    const float farPlane = 4000f;

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
        TerrainLoader terrainLoader = new(resources, loop.Natives, logger);
        MissionLoader missionLoader = new(missions, fileSystem, resources, levelLoader, logger, terrainLoader);
        LoadedMission loaded = missionLoader.Load(id);

        scene = WorldScene.Build(loaded, levelRoot, magic, logger);
        world = new WorldRenderer(device, resources, logger);

        // A level whose terrain files are not live data — level 14 is exactly that — draws its objects
        // over nothing rather than following offsets into whatever the geometry blob happens to hold.
        // The surface comes from the level's own TextureModifier tasks, so it needs the loaded tree.
        terrain?.Dispose();
        terrain = loaded.Terrain is { IsUsable: true } usable
            ? new TerrainRenderer(device, TerrainSurface.Build(usable, levelRoot, logger), logger)
            : null;

        // The sky is script data on the level's task tree; its fog colour is also what the frame
        // clears to, so the horizon meets sky-coloured backing rather than black.
        sky?.Dispose();
        sky = new SkyRenderer(device, fileSystem, levelRoot, farPlane, logger);
        if (sky.HasSky)
        {
            scheduler.ClearColour = new OpenIGI.Core.Graphics.Bgra32(
                (byte)Math.Clamp((int)(sky.FogColour.Z * 255f), 0, 255),
                (byte)Math.Clamp((int)(sky.FogColour.Y * 255f), 0, 255),
                (byte)Math.Clamp((int)(sky.FogColour.X * 255f), 0, 255),
                255);
        }

        // Open on the middle of what was placed, backed off and looking down slightly, so the first
        // frame shows the level rather than the inside of whichever object happens to be at the origin.
        // -campos overrides that with an exact spot, which is how a bug at a known position is looked at.
        camera = options.CameraPosition is { } at
            ? new FreeCamera(new Vector3d(at.X, at.Y, at.Z), options.CameraYaw, options.CameraPitch)
            : CreateCamera(scene, options.CameraYaw, options.CameraPitch, options.CameraHeight);

        // The player stands where the camera looks from, dropped onto whatever is under it. A level
        // whose terrain is not live data has nothing to stand on, so the player stays unavailable and
        // F1 does nothing.
        playerTerrain = loaded.Terrain is { IsUsable: true } standable ? standable : null;
        player = null;
        playerMode = false;

        if (playerTerrain is not null)
        {
            PlayerCamera candidate = new();

            // The level places its own player: every mission's objects.qvm declares a HumanPlayer task
            // with a Position and a Gamma. Standing there is standing where the game starts you.
            bool placed = PlayerSpawn.TryFind(levelRoot, out PlayerSpawnPoint spawn)
                ? candidate.PlaceAt(playerTerrain, spawn)
                : candidate.Place(playerTerrain, camera.Position, options.CameraYaw);

            if (placed)
            {
                player = candidate;

                // Walking is what starting a mission means. The free camera is the debugging tool, so
                // it is the thing you switch to rather than the thing you start in.
                playerMode = !options.StartFlying;
                logger.Information(
                    "Player",
                    playerMode
                        ? $"Standing at {candidate.Position}; F1 for the free camera."
                        : $"Free camera; F1 to stand at {candidate.Position}.");
            }
            else
            {
                logger.Warning(
                    "Player",
                    "Nothing to stand on at the level's spawn, so this mission opens in the free camera.");
            }
        }

        // The menus are done; without dropping the renderer the last screen would keep drawing over
        // the world.
        renderer = null;
        logger.Information("Mission", $"Loaded {loaded}.");
    }

    menuHost.StartGame = () => StartMission(missionState.PendingMissionId);

    static FreeCamera CreateCamera(WorldScene scene, float yaw, float pitch, float height)
    {
        if (scene.Placements.Count == 0)
        {
            return new FreeCamera(default, yaw, pitch);
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
            new Vector3d(centreX, centreY - 100.0, ground + 25.0 + height),
            yaw,
            pitch);
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

    int lastCameraMilliseconds = loop.TimeSource.Milliseconds;

    while (running && loop.IsRunning && !window.IsClosing)
    {
        window.PumpEvents();

        // The camera is advanced on every pass of the loop, not only on the passes that render. The
        // pump clears the mouse's motion accumulator, and the loop pumps several times per rendered
        // frame — so consuming it only on a render threw most of the mouse movement away and made the
        // view lurch. It also runs on real elapsed time: a fixed step tied flying speed to the frame
        // rate, which is not what a person moving a mouse expects.
        // F1 swaps between flying and walking. The player takes the camera's position when it is
        // switched on, so the swap happens where you were looking.
        if (window.Input.Keyboard.WasPressed(OpenIGI.Input.KeyCode.F1) && player is not null && camera is not null)
        {
            playerMode = !playerMode;
            if (playerMode)
            {
                if (!player.Place(playerTerrain!, camera.Position, camera.Yaw))
                {
                    playerMode = false;
                    logger.Warning("Player", "Nothing to stand on here; staying with the free camera.");
                }
            }
            else
            {
                camera.MoveTo(player.Camera.Position, player.Body.Yaw, player.Body.Pitch);
            }

            logger.Information("Player", playerMode ? "Walking." : "Flying.");
        }

        if (camera is not null)
        {
            int nowMilliseconds = loop.TimeSource.Milliseconds;
            float elapsed = Math.Clamp((nowMilliseconds - lastCameraMilliseconds) / 1000f, 0f, 0.1f);
            lastCameraMilliseconds = nowMilliseconds;

            float rawX = window.Input.Mouse.DeltaX;
            float rawY = window.Input.Mouse.DeltaY;
            bool right = window.Input.Mouse.IsDown(OpenIGI.Input.MouseButton.Right);

            if (playerMode && player is not null)
            {
                // Looking is per frame so the view is as smooth as the display allows; the body itself
                // moves on the fixed tick below, so it steps identically whatever the frame rate.
                player.Look(window.Input, elapsed);
                window.CursorLocked = player.IsCapturingMouse;
            }
            else
            {
                camera.Update(window.Input, elapsed);

                // The cursor is locked away while a mouse button is held, the way a scene editor's is,
                // so the view keeps turning however far the mouse is dragged. It comes back on release.
                window.CursorLocked = camera.IsCapturingMouse;
            }

            if (options.LogCamera && (right || rawX != 0f || rawY != 0f))
            {
                logger.Information(
                    "Camera",
                    $"rmb={(right ? 1 : 0)} raw=({rawX:0.##},{rawY:0.##}) pos=({camera.Position.X:0.##},"
                        + $"{camera.Position.Y:0.##},{camera.Position.Z:0.##}) fwd={camera.Camera.Orientation.Row3} "
                        + $"right={camera.Camera.Orientation.Row1} dt={elapsed:0.####}");
            }
        }

        FrameOutcome outcome = loop.RunFrame();

        // The body advances on the simulation tick, never on elapsed time - that is what keeps its
        // motion the game's own rather than the display's.
        if (outcome is FrameOutcome.Tick or FrameOutcome.Render
            && playerMode && player is not null && playerTerrain is not null)
        {
            player.Tick(window.Input, playerTerrain);
        }

        if (outcome != FrameOutcome.Render)
        {
            continue;
        }

        if (scene is not null && world is not null && camera is not null)
        {
            float aspectRatio = (float)window.Width / Math.Max(1, window.Height);
            Matrix4f projection = Matrix4f.CreatePerspective(
                verticalFieldOfView,
                aspectRatio,
                near: 0.2f,
                far: farPlane);

            // The sky is behind everything and writes no depth, so it goes first; then terrain, which
            // is opaque and covers most of the screen, so drawing it before the objects gives the
            // depth test the most to reject.
            Camera3D eye = playerMode && player is not null ? player.Camera : camera.Camera;

            sky?.Draw(eye, projection, loop.TimeSource.Milliseconds / 1000f);
            terrain?.Draw(eye, projection, verticalFieldOfView, aspectRatio, options.TerrainViewDistance);
            world.Draw(scene, eye, projection);

            if (!reportedWorld)
            {
                reportedWorld = true;
                logger.Information(
                    "World",
                    $"Drew {world.LastDrawCount} sub-mesh(es), {world.LastLightmappedCount} lightmapped, {world.LastTriangleCount} triangles from {camera.Position}.");

                if (terrain is not null)
                {
                    logger.Information(
                        "Terrain",
                        $"Drew {terrain.LastChunkCount} chunk(s) in {terrain.LastPassCount} material pass(es), "
                            + $"{terrain.LastTriangleCount} triangles, from {terrain.LastVisitedCount} "
                            + $"octree node(s) visited, over {terrain.TileCount} ground tile(s).");
                }
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
    sky?.Dispose();
    terrain?.Dispose();
    world?.Dispose();
    renderer?.Dispose();
    logger.Information(
        "Desktop",
        $"Shutting down after {loop.Clock.TickCount} ticks and {loop.Clock.FrameCount} frames.");
}

return 0;


// What the window actually ended up as, which is not always what was asked for: a borderless window is
// composited by the desktop, so it presents at the desktop's refresh rate and cannot raise it. Saying
// so is the difference between "165 Hz was requested" and "60 Hz is what you are seeing".
static void ReportDisplay(SilkGameWindow window, DesktopOptions options, IIgiLogger logger)
{
    DesktopDisplay display = window.Display;
    string rate = window.RefreshRate > 0 ? $"{window.RefreshRate} Hz" : "an unreported refresh rate";

    logger.Information(
        "Desktop",
        $"{window.Mode} at {window.Width}x{window.Height} @ {rate}"
            + (display.MaxRefreshRate > 0 ? $" (monitor tops out at {display.MaxRefreshRate} Hz here)" : string.Empty)
            + (options.VSync ? ", vsync on." : ", vsync off."));

    logger.Information(
        "Desktop",
        $"Backend reports {window.BackendState}; monitor origin ({display.Origin.X},{display.Origin.Y}), "
            + $"desktop {display.DesktopSize.X}x{display.DesktopSize.Y} @ {display.DesktopRefreshRate} Hz, "
            + $"work area {display.WorkArea.Size.X}x{display.WorkArea.Size.Y}.");

    if (window.Mode == WindowMode.BorderlessFullscreen
        && display.MaxRefreshRate > 0
        && display.DesktopRefreshRate > 0
        && display.DesktopRefreshRate < display.MaxRefreshRate)
    {
        logger.Warning(
            "Desktop",
            $"The monitor supports {display.MaxRefreshRate} Hz at {display.DesktopSize.X}x{display.DesktopSize.Y} "
                + $"but the desktop is set to {display.DesktopRefreshRate} Hz. A borderless window presents "
                + "through the compositor and cannot exceed that -- raise it in Windows display settings, "
                + "or run with -fullscreen to have the display mode set for you.");
    }
    else if (display.IsBelowMaxRefreshRate)
    {
        logger.Warning(
            "Desktop",
            $"Opened at {display.RefreshRate} Hz although the monitor reports {display.MaxRefreshRate} Hz "
                + $"at {display.Size.X}x{display.Size.Y}.");
    }

    if (!options.UncapFrameRate)
    {
        logger.Information(
            "Desktop",
            "Rendering is latched to one frame per 30 Hz simulation tick, as the original is. "
                + "Pass -uncapped to present on every vsync instead.");
    }
}

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
