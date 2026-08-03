using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Frontend;
using OpenIGI.Frontend.Saves;
using OpenIGI.Game.Profiles;
using OpenIGI.Rendering.Presets;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;
using OpenIGI.Formats.Ai;
using OpenIGI.Formats.Textures;
using OpenIGI.Core.Numerics;
using OpenIGI.Desktop;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Config;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Game.Menus;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
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

    // The developer console: the CVar registry, settings.cfg and the on-screen view. Brought up before
    // anything reads a setting, so the saved file and the command line's +statements are already in
    // effect by the time the renderer and the game are built. Everything logged from here on is
    // mirrored into its scrollback, so opening it shows the log rather than an empty panel.
    using DesktopConsole console = new(device, logger);
    console.QuitRequested += (_, _) => running = false;
    console.Start(fileSystem, options.StartupCommands, useSavedSettings: !options.Lockstep);
    logger = console.WrapLogger(logger);

    // -novsync is the older spelling of r_vsync 0. It forces the setting rather than shadowing it, so
    // the console and the flag can never report different things.
    if (!options.VSync)
    {
        RenderCVars.VSync.Value = false;
    }

    // Set before anything uploads a texture: mip generation happens at upload, so a level loaded
    // before this would keep the unfiltered tiles it started with.
    device.SetTextureQuality(RenderCVars.Anisotropy.Value, RenderCVars.GenerateMipmaps.Value);
    RenderCVars.Anisotropy.Changed += (_, _) =>
        device.SetTextureQuality(RenderCVars.Anisotropy.Value, RenderCVars.GenerateMipmaps.Value);

    device.VSyncEnabled = RenderCVars.VSync.Value;
    RenderCVars.VSync.Changed += (_, _) => device.VSyncEnabled = RenderCVars.VSync.Value;

    // Timing is pushed on change rather than read per frame, because switching it off is what discards
    // the history -- the peaks after switching it back on should belong to the run being watched.
    device.SetStageTiming(RenderCVars.PerformanceLog.Value);
    RenderCVars.PerformanceLog.Changed += (_, _) => device.SetStageTiming(RenderCVars.PerformanceLog.Value);

    DesktopMenuHost menuHost = new(
        window.Input,
        batch,
        () => renderer,
        () => window.Width,
        () => window.Height,
        () => running = false,
        logger)
    {
        // The menu and its hit-test have to agree about the magnification -- this is the inverse of
        // what MenuRenderer draws with. See UiScale for why the interface is scaled at all.
        Scale = options.UiScale,
    };

    // The flow state machine owns the frame, and the menu is a task beneath it — so the frame walk
    // updates and renders the menu rather than this loop doing either by hand.
    DesktopRenderScheduler scheduler = new(device);

    // -lockstep replaces the wall clock with a synthetic one advanced a fixed amount per loop pass,
    // so how long a frame took to draw stops deciding how many ticks it covered. Without it a capture
    // is not reproducible -- two identical command lines land on different tick counts and so on
    // different soldier and animation states, which measured at 9% of pixels differing. Everything
    // else in the simulation is already deterministic (IgiRandom is seeded, not time-seeded), so this
    // is the whole of what a reference capture needs.
    ManualTimeSource? lockstepClock = options.Lockstep ? new ManualTimeSource() : null;
    GameLoop loop = new(time: lockstepClock, host: menuHost, renderer: scheduler, logger: logger);

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

    // Without these, HumanAI/PatrolPath/PatrolPathCommand load as untyped DeclaredTasks: the soldier's
    // brain cannot be found, so no soldier gets its own MISSION:AI/<taskid> script, so nothing ever names a
    // patrol path and every guard falls back to the archetype and stands still.
    AiTaskTypes.RegisterAll(loop.Types);

    // A level's CutScene tasks export isRun/isFinished/nTick, which the mission AI scripts read in their
    // idle branch. Without the type registered the symbols never bind and guard.qsc faults every idle tick
    // on CutScene_1200.nTick, cutting its patrol handling short. Its fourteen parameters match the level's
    // Task_DeclareParameters("CutScene", ...) declaration so the registry's verification passes.
    loop.Types.Register<CutSceneTask>(CutSceneTask.TypeName);

    // A level parks its intro and outro cut-scene actors inside ConditionalContainer tasks gated on
    // cut-scene / mission-progress state that is false during play. Without the type registered the port
    // built each as a plain container and rendered its children, so those actors -- including a second
    // David Jones on the player's own model, the outro "Jones drive truck" -- stood in the level at
    // mission start where the original never registers them. Registering the type lets the gate below
    // evaluate each container's condition and hide the subtrees the original leaves unregistered.
    loop.Types.Register<ConditionalContainerTask>(
        ConditionalContainerTask.TypeName);

    // Every soldier's combat numbers - 29 AI types x 3 difficulties out of common/ai/settings.qsc.
    // Loaded before any level, as sub_406DF0 is: the table has to exist before a HumanAI is configured.
    HumanAiConfigRegistry.RegisterTypes(loop.Types);
    HumanAiConfigRegistry aiConfig = new(logger);
    magic.Register(loop.Natives);

    try
    {
        missions.Scan(fileSystem, resources, loop.Natives);
        magic.Load(resources, loop.Natives);
        aiConfig.Load(resources, loop.Natives, loop.Tasks);
        configHost.Missions = missions;
    }
    catch (EngineFatalException ex)
    {
        logger.Warning("Mission", $"The mission scan failed, so no missions are selectable: {ex.Message}");
    }

    bool reportedWorld = false;
    bool reportedShot = false;
    int guardRounds = 0;
    int guardHits = 0;

    // The player's own damage. Its zone table is the player's, whose head is worth 1.0 rather than the
    // soldiers' 100 -- humanplayer.qvm overrides the executable's default (weapons.md 11). The player's
    // own hit handler (0x412440) additionally scales every hit by the profile difficulty: x0.75 easy,
    // x1.0 normal, x1.25 hard -- a scale that applies to the PLAYER only, never to a soldier.
    int playerDifficulty = (int)(options.Difficulty
        ?? (AiDifficulty)Math.Clamp(configuration.Active?.Difficulty ?? 1, 0, 2));

    // The player's maximum health is humanplayer.qvm's ARGUMENT 7 = 3.0, written over Human+600 by the
    // constructor (0x410370 at 0x4104E1) AFTER 0x489C30 set the universal 1.0 -- the player is worth
    // three soldiers. Measured live off the retail game: [phit] max=3.000, dead at exactly 3.0
    // accumulated. The value is refreshed from the parsed script when the mission loads.
    float playerMaximumHealth = 3f;
    HumanHealth playerHealth = new(HumanHitZoneTable.Player, playerMaximumHealth)
    {
        DamageScale = HumanHealth.DifficultyDamageScale(playerDifficulty),
    };
    bool playerDead = false;

    // The red wash the screen takes on a hit -- four corner alphas that decay a forty-fifth a tick
    // (sub_411300) and paint a screen-filling red quad (HumanPlayer_DrawDamageFlash). It survives the
    // player's death, so the corpse's-eye view stays red rather than snapping back to a clean picture.
    HumanDamageFlash damageFlash = new();

    // The camera the game hands the view to when the player dies -- it pulls back off the first-person eye
    // to a third-person shot of the body (HumanCamera death mode). The body it looks at is the player's
    // own third-person rig, which SoldierRenderer loads and poses with the death fall.
    DeathCamera deathCamera = new();
    InteractionCamera interactionCamera = new();
    bool climbActive = false;
    int temporalFrame = 0;

    // The player eye's last two TICK positions, for drawing the walk between them. Orientation is
    // already per-frame (mouse-look runs on every loop pass); it is the POSITION that stepped at
    // 30 Hz, which read as the whole world juddering whenever the player moved.
    Vector3d playerEyePrevTick = default;
    Vector3d playerEyeTick = default;
    bool playerEyeCaptured = false;
    IrradianceVolume? probeField = null;

    // The bake in flight, while it is in flight. Non-null only between the level load starting one and
    // the render thread collecting it; a lockstep capture never sees it, because that path waits.
    IrradianceProbeBake? probeBake = null;

    // When the probe field landed, in the loop's own milliseconds, or -1 for "no fade owed". The field
    // is traced on a worker and arrives several seconds into a level; until it does, every surface is
    // lit by one flat ambient colour, and swapping the two in a single frame relights the entire world
    // at once. That reads as a fault rather than as a load finishing.
    //
    // A lockstep capture never fades: it waits for the field at load, so there is nothing to blend from
    // and nothing time-dependent for a reference image to disagree about.
    long probeArrivedAt = -1;

    // How long the field takes to replace the ambient it stands in for. Long enough that the eye reads
    // it as light settling rather than as a cut, short enough that nobody waits for it -- and well
    // under the several seconds the bake itself takes, so the two do not read as one long event.
    const float ProbeFadeMilliseconds = 600f;

    // How long a load will hold for the lighting before giving up and letting the player in anyway. A
    // level that somehow never finishes tracing must not become a game that never starts.
    const int LightingWaitMilliseconds = 30_000;

    // The level's signed distance fields, and the clipmaps that resample them around the camera. Baked
    // on a worker for the same reason the probe field is; the clipmaps themselves are GL and are built
    // on the render thread once the bake lands.
    SurfaceCacheLighting? cardLighting = null;
    SurfaceCacheLookup? cardLookup = null;
    bool cardLightingDumped = false;

    // How many times the bounce has been re-lit this mission. Zero converges in one shot; each relight
    // after that accumulates over frames. Reset when a mission loads.
    int cardBounceFrame = 0;

    // One screen-space indirect dump per run, to verify the gather reads the G-buffer and reconstructs
    // world positions correctly before it feeds the frame.
    bool screenGiDumped = false;

    // The live screen probe gather's atlases, sized to the frame divided by the probe step and reused
    // across frames. Ping-ponged: each frame reads one pair and writes the other, so the accumulation
    // reprojects onto its own past. giAccum holds the accumulated irradiance the deferred lighting reads;
    // giPosition holds the world position each probe belongs to, for the reprojection's disocclusion test.
    // Grown when the window changes, which drops the history with it.
    TextureHandle[] giAccum = [default, default];
    TextureHandle[] giPosition = [default, default];
    TextureHandle giFiltered = default;
    int giIndirectWidth = 0;
    int giIndirectHeight = 0;
    int giFrame = 0;
    bool giHasHistory = false;
    Matrix3f giPrevWorldToView = Matrix3f.Identity;
    Vector3f giPrevCameraToCache = default;
    SurfaceCacheCapture.Result? capturedCards = null;
    SurfaceCacheInstances? cardInstances = null;
    DistanceFieldBake? sdfBake = null;
    SceneDistanceField? sdfScene = null;
    GlobalDistanceField? sdfClipmaps = null;
    bool sdfValidated = false;
    bool climbSlideArmed = false;
    Random shotRandom = new(20000101);
    int reportedHumanHits = 0;
    WorldScene? scene = null;
    WorldRenderer? world = null;
    TerrainRenderer? terrain = null;
    SkyRenderer? sky = null;
    ShadowCasterPass? shadowPass = null;

    // Authored atmosphere. Levels with no entry fall back to what their own FlatSky task asks for.
    FogPresets fogPresets = FogPresets.Load(
        Path.Combine(AppContext.BaseDirectory, "Assets", "fog-presets.json"),
        logger);

    MaterialLibrary materials = MaterialLibrary.Load(
        Path.Combine(AppContext.BaseDirectory, "Assets", "materials.json"),
        logger);

    // The derived surface detail this game never shipped -- normals, roughness, metal. Built by
    // `ResExtract maps` and pointed at by r_material_pack; an installation that has never run the tool
    // has none, and renders exactly as it did before any of this existed.
    //
    // Constructed once for the whole session rather than per level, because a texture pack is shared
    // across missions and the maps are the textures' own property rather than a level's.
    MaterialDetailPack detailMaps = new(device, RenderCVars.MaterialPack.Value, logger);

    // The mission's authored grade, or the one r_lut_table names instead. Loaded per level rather than
    // per frame -- it is a volume texture upload, while how much of it to apply is a number.
    // A mission with no table of its own is not a fault: it renders ungraded, which is what every
    // mission did before this existed.
    void ApplyColourLookup(int mission)
    {
        string named = RenderCVars.ColourLookupTable.Value.Trim();
        string name = named.Length > 0
            ? named
            : FormattableString.Invariant($"mission{mission}");

        // Anchored to the assets directory and stripped of any path, so r_lut_table cannot be pointed
        // at an arbitrary file on the machine from a settings file or a +statement.
        string file = Path.Combine(
            AppContext.BaseDirectory, "Assets", "luts", Path.GetFileName(name) + ".cube");

        ColourLookupTable? table = ColourLookupTable.Load(file, logger);

        if (table is null && named.Length > 0)
        {
            logger.Warning("Grade", $"No colour table called '{named}' in Assets/luts; the frame is ungraded.");
        }

        device.SetColourLookup(table);
    }

    RenderCVars.ColourLookupTable.Changed += (_, _) => ApplyColourLookup(options.StartMission);

    // The level's own sun, read from its Dirlight task -- section 12.
    // Null until a level is loaded, and for a level that declares none.
    DirectionalLightSettings? sun = null;
    FreeCamera? camera = null;

    // The walking player. F1 swaps between it and the free camera: the camera is how a level is
    // inspected, the player is how it is stood in. Both draw through the same Camera3D.
    PlayerCamera? player = null;
    HeldWeaponRenderer? heldWeapon = null;
    StatusScreenRenderer? hud = null;
    BulletMarkRenderer? marks = null;
    LoadedMission? loadedMissionForWeapon = null;
    ViewModelRenderer? arms = null;
    SoldierRenderer? soldiers = null;
    TerrainData? playerTerrain = null;

    // The material table, shared with the guard-round handler so an AI round's impact can leave the
    // same mark a player's does -- retail rounds are physical and decal whatever they meet (10.4).
    GameMaterialRegistry? worldMaterials = null;
    bool playerMode = false;

    // The debug overlay (F3): the player's state and a crosshair "what am I looking at" readout. The
    // collision index is kept here so the overlay can name the object under the crosshair.
    ObjectCollisionIndex? worldSolids = null;
    bool showDebug = false;

    // The camera's projection, in one place: the terrain traversal needs the same field of view the
    // projection matrix is built from, or it would cull against a frustum the picture does not have.
    // The far plane is shared with the sky, which sizes its dome from it.
    //
    // The field of view is the original's, not a chosen one. HumanView's constructor (0x4643A0) sets
    // its half-tangent to tan(45 degrees) = 1.0, the camera copies it to HumanCamera+96 and submits it
    // to the view (0x4D9870) -- and it is HORIZONTAL, which the terrain traversal's own
    // viewDistance/sqrt(tanX) independently confirms. So the horizontal field of view is 90 degrees and the
    // vertical one follows from the window's aspect, rather than the other way round.
    const float farPlane = 4000f;
    static float VerticalFieldOfView(float aspectRatio) =>
        2f * MathF.Atan(HumanView.DefaultTanX / MathF.Max(aspectRatio, 0.0001f));

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

        // Evaluate the level's ConditionalContainer conditions against the initial playable state, so the
        // scene, spawn and soldier walks below skip the subtrees the original leaves unregistered (the
        // intro/outro cut-scene props and actors). Runs after the tree is built and its variables exported,
        // and before anything walks it.
        new ConditionalContainerGate(loop.Variables, logger).Prepare(levelRoot);

        scene = WorldScene.Build(loaded, levelRoot, magic, logger);
        world = new WorldRenderer(device, resources, logger) { Materials = materials, DetailMaps = detailMaps };

        // The walls. Built here because two consumers want it: the player's collision, and the
        // soldiers' ground probe - a guard's declared Position is only where it starts, and without
        // the settle the retail integrator performs, floor-authored spawns bury the body to the waist.
        ObjectCollisionIndex solids = ObjectCollisionIndex.Build(scene.Placements);
        worldSolids = solids;

        // The soldiers the level places. Each names its own model, skeleton and stand animation
        // (player.md section 15); the sets they index were loaded when the packs opened. They are not
        // WorldScene placements - a human task is a typed task, and the original draws it through the
        // bone path, not the static one.
        soldiers?.Dispose();
        soldiers = new SoldierRenderer(device, resources, logger) { DetailMaps = detailMaps };
        soldiers.Load(
            loaded,
            levelRoot,
            loaded.Terrain is { IsUsable: true } standing ? standing : null,
            solids.Caster());

        // The level's nav graphs. A level ships ONE PER AIGraph TASK, named by that task's id (ai.md 1.1),
        // and every HumanAI names the one it walks through its own "Graph ID" parameter (ai.md 7.6).
        //
        // Level 1 ships eleven of them. Picking a single graph -- the one with the most nodes -- and giving
        // it to every soldier routed each one over node ids belonging to a graph that was not its own,
        // which is why guards walked to places no patrol goes and off the edge of the level.
        try
        {
            string graphDirectory = Path.Combine(
                gamePath, loaded.Descriptor.MissionDirectory, "graphs");
            Dictionary<int, GraphFile> graphs = [];
            if (Directory.Exists(graphDirectory))
            {
                foreach (string file in Directory.GetFiles(graphDirectory, "graph*.dat"))
                {
                    string stem = Path.GetFileNameWithoutExtension(file);
                    if (!int.TryParse(stem.AsSpan("graph".Length), out int graphId))
                    {
                        continue;
                    }

                    try
                    {
                        GraphFile graph = GraphFile.Read(File.ReadAllBytes(file));

                        // The graph file stores node positions in the graph's own frame. The world
                        // position is node + graphTask.Position — what 0x4F9770 adds every time.
                        Vector3d graphPos = FindTaskPosition(levelRoot, graphId);
                        graph.ApplyWorldOffset(graphPos);

                        graphs[graphId] = graph;
                    }
                    catch (GraphFormatException ex)
                    {
                        logger.Warning("Ai", $"Nav graph '{stem}' did not parse: {ex.Message}");
                    }
                }
            }

            if (graphs.Count > 0)
            {
                logger.Information(
                    "Ai",
                    $"Nav graphs: {graphs.Count} — " +
                    string.Join(", ", graphs.OrderBy(p => p.Key).Select(p => $"{p.Key}:{p.Value.Nodes.Count}n")));
                // The difficulty every guard reads its combat row at. Faithfully the ACTIVE PLAYER PROFILE's
                // (sub_406F70 takes it from sub_406220()'s first field), so the difficulty the player picked
                // in the menu actually reaches the AI -- which it did not while this was a hardcoded default,
                // leaving every guard on GD_2/Normal whatever the profile said. -difficulty still overrides,
                // for testing a difficulty without editing a profile.
                AiDifficulty effectiveDifficulty = options.Difficulty
                    ?? (AiDifficulty)Math.Clamp(
                        configuration.Active?.Difficulty ?? (int)AiDifficulty.Normal,
                        (int)AiDifficulty.Easy,
                        (int)AiDifficulty.Hard);
                logger.Information(
                    "Ai",
                    $"Difficulty {HumanAiConfigRegistry.NameOf(effectiveDifficulty)} ({effectiveDifficulty}) "
                        + (options.Difficulty is null
                            ? $"from profile '{configuration.Active?.Name ?? "(none)"}'."
                            : "from -difficulty override."));
                // The sight and bullet rays test placed objects AND the terrain, as the original's
                // 0x4CDB20 does (both of the sight ray's passes send the -1 mask whose sign bit turns
                // the terrain arm on -- player.md 7.6). Objects alone let guards see and shoot through
                // hills. Nearest contact wins.
                WorldRayCast objectCast = solids.Caster();
                WorldRayCast levelCast = (Vector3d a, Vector3d b, out WorldRayHit blockedAt) =>
                {
                    bool hitObject = objectCast(a, b, out WorldRayHit onObject);
                    if (playerTerrain is { } ground
                        && TerrainSightBlock.TryBlock(ground, a, b, out WorldRayHit onGround)
                        && (!hitObject || onGround.Distance < onObject.Distance))
                    {
                        blockedAt = onGround;
                        return true;
                    }

                    blockedAt = onObject;
                    return hitObject;
                };
                // The probe field, traced against the level's collision on a WORKER. Inline it blocks for
                // about three seconds in Release and half a minute in Debug, and by this point the window
                // and its GL context are live -- so the process stops answering, the GPU is handed no
                // work for the whole of it, and Windows' watchdog takes the context away. Loading mission
                // 1 from the menu died that way, with exit 0xCFFFFFFF. The main thread carries on loading
                // and presenting; IrradianceProbeBake hands the volume over when it is ready.
                //
                // It traces with every door SHUT, which is two things at once. A door standing open would
                // otherwise be baked as a hole in the wall it belongs to, and the field is static, so the
                // hole would outlast the door -- that is why this used to have to run before the doors
                // did. And the shut cast is the only one that reads no state the game mutates, which is
                // what makes running it alongside a live level safe rather than merely early.
                //
                // A level with no Dirlight has no sun to trace towards, so the field is sky and bounce
                // alone -- which is what such a level looks like anyway.
                if (RenderCVars.GlobalIllumination.Value)
                {
                    Vector3f bakeSunDirection = sun is { } declared && !declared.IsBelowHorizon
                        ? declared.Direction
                        : default;

                    WorldRayCast shutObjectCast = solids.CasterWithDoorsShut();
                    WorldRayCast bakeCast = (Vector3d a, Vector3d b, out WorldRayHit blockedAt) =>
                    {
                        bool hitObject = shutObjectCast(a, b, out WorldRayHit onObject);
                        if (playerTerrain is { } bakeGround
                            && TerrainSightBlock.TryBlock(bakeGround, a, b, out WorldRayHit onGround)
                            && (!hitObject || onGround.Distance < onObject.Distance))
                        {
                            blockedAt = onGround;
                            return true;
                        }

                        blockedAt = onObject;
                        return hitObject;
                    };

                    probeBake = IrradianceProbeBake.Start(
                        scene,
                        bakeCast,
                        bakeSunDirection,

                        // Whatever the console says, but never so fine that a big level spends a minute
                        // of load tracing rays nobody asked for.
                        RenderCVars.GlobalIlluminationSpacing.Value,

                        // One albedo for the whole level. The alternative is reading each hit's own
                        // texture, which needs the ray to report a material rather than a triangle --
                        // and IGI's palette is concrete, snow and dirt, so a warm grey is not far off
                        // any of them.
                        new Vector3f(0.42f, 0.40f, 0.37f),
                        logger);

                    // A capture is not allowed to race the worker. A reference image has to either
                    // contain the field or not contain it, never "whatever frame 30 happened to catch",
                    // so a lockstep run pays the block back and stays reproducible. Nobody is looking at
                    // this window, and the watchdog is a problem for an interactive load.
                    if (options.Lockstep)
                    {
                        probeField = probeBake.WaitForResult();
                        probeBake = null;
                    }
                    else
                    {
                        // A level is not ready to be played until it is lit. The alternative -- letting
                        // the player in and swapping the lighting under them when the bake lands -- is
                        // what a loading screen exists to prevent, and no amount of blending makes an
                        // unlit level the right thing to show.
                        //
                        // Waited for by PUMPING, not by blocking. Blocking is exactly what moved this
                        // bake onto a worker in the first place: the window answers nothing, the GPU is
                        // handed no work, and Windows' watchdog takes the context away. So the message
                        // queue keeps draining and the frame keeps presenting while the worker traces.
                        probeField = WaitForLighting(probeBake);
                        probeBake = null;
                    }
                }

                // The distance fields, on a worker beside the probe bake. Tracing every distinct
                // model is arithmetic and moves off the render thread freely; only the upload cannot,
                // because a GL texture belongs to the context that made it.
                if (RenderCVars.DistanceFields.Value && device.Capabilities.SupportsCompute)
                {
                    sdfClipmaps?.Dispose();
                    sdfClipmaps = null;
                    sdfScene = null;
                    sdfValidated = false;

                    sdfBake?.Cancel();
                    sdfBake = DistanceFieldBake.Start(
                        scene.Placements, RenderCVars.DistanceFieldResolution.Value, logger);

                    // A capture must not race the worker, for the reason the probe bake records: a
                    // reference image has to either contain the field or not, never whichever frame
                    // the worker happened to reach.
                    if (options.Lockstep && sdfBake.WaitForResult() is { } readyField)
                    {
                        sdfScene = readyField.Scene;
                        sdfClipmaps = GlobalDistanceField.Create(
                            device,
                            readyField.Scene,
                            readyField.Atlas,
                            GlobalDistanceFieldSettings.Default,
                            logger);
                        sdfBake = null;
                    }
                }

                // Every distinct model's surface, captured into the card atlas. On the render thread
                // because it IS a render pass -- the atlas is a framebuffer and the geometry is already
                // uploaded, and both belong to the context that made them.
                if (RenderCVars.SurfaceCache.Value && world is not null)
                {
                    if (world.CaptureSurfaceCache(scene, RenderCVars.SurfaceCacheDensity.Value)
                        is { } cards)
                    {
                        string into = RenderCVars.SurfaceCacheDump.Value;

                        if (into.Length > 0)
                        {
                            DumpCardAtlas(device, cards.Capture, into, logger);
                        }

                        // Only the ATLAS is built here. Lighting it needs the level's own sun, which is
                        // not read until further down this load -- and lighting the cards before that
                        // gave every page a zero sun direction and a picture of pure sky, which is
                        // exactly what the first run produced.
                        capturedCards = cards.Capture;
                        cardInstances = cards.Instances;
                    }
                }

                device.SetIrradianceVolume(probeField);
                ApplyColourLookup(options.StartMission);

                // The level's doors, before the brains: a walker asks the registry for the nearest door
                // on a door hop, and an open door is switched out of the collision index so the
                // doorway is walkable. Both have to exist before Animate hands each brain its query.

                DoorRegistry levelDoors = DoorRegistry.Build(levelRoot);
                soldiers.SetDoors(levelDoors, solids);
                if (levelDoors.Doors.Count > 0)
                {
                    logger.Information("World", $"{levelDoors.Doors.Count} door(s) the AI can open.");
                }

                soldiers.Animate(
                    graphs, levelCast, resources, loop.Natives, levelRoot, aiConfig, effectiveDifficulty);

                // A guard's round: it raised the trigger, the weapon's cadence let one out, and this is
                // where it becomes a cast. The AI's hit probability decides whether the round is on
                // target -- the original hands that same figure to the gun with every shot (ai.md 11.6),
                // so a miss is a round that went somewhere else rather than a round that never existed.
                soldiers.SetRoundHandler((from, at, probability, weapon) =>
                {
                    guardRounds++;
                    if (player is null)
                    {
                        return;
                    }

                    // The ray runs where the GUN points -- the tracked aim (or a patrol's own facing) --
                    // extended past the target, exactly as the retail round is a cast from the muzzle to
                    // the end of its remaining range (GunShot per-tick 0x47B750). A moving player walks
                    // out of the lagging aim and the round geometrically misses; that is retail's miss.
                    Vector3d to = new(
                        from.X + ((at.X - from.X) * 4.0),
                        from.Y + ((at.Y - from.Y) * 4.0),
                        from.Z + ((at.Z - from.Z) * 4.0));

                    Vector3d feet = player.Body.Position;

                    // Resolve the guard's round against the player's OWN skinned body (limb hits do less,
                    // and the hitbox is the real body, not a fat cylinder). Only if the body is not loaded
                    // does it fall back to the cylinder-and-trunk stand-in.
                    HumanHit hit = default;
                    bool met = soldiers is not null
                        && soldiers.TryHitPlayerBody(from, to, feet, player.Body.Yaw, out hit);
                    if (!met)
                    {
                        Vector3f facing = new(-MathF.Sin(player.Body.Yaw), MathF.Cos(player.Body.Yaw), 0f);
                        met = HumanHitTest.TryCast(from, to, feet, facing, pose: null, out hit);
                    }

                    // Where the world stops this ray, over its whole length -- one cast serves cover,
                    // fly-pasts and outright misses alike, and EVERY round that meets a surface leaves
                    // the same mark a player's round does. In retail a miss is visible: the round lands
                    // on the wall or the dirt near you (10.4's decals) -- rounds that silently vanished
                    // are why the guards seemed to hit with every bullet.
                    bool worldBlocked = levelCast(from, to, out WorldRayHit worldHit);
                    void MarkTheImpact()
                    {
                        if (worldBlocked && worldMaterials is { } table)
                        {
                            player.Marks.TryAdd(worldHit, table);
                        }
                    }

                    if (!met)
                    {
                        // Geometric miss -- aim lag against a moving target, or a running patrol's
                        // spray. The round flies on and lands where the world says.
                        MarkTheImpact();
                        return;
                    }

                    // The world is in the way: the retail cast meets the nearest surface first, so a wall
                    // or a crate between the muzzle and the body simply takes the round (0x47B750 -- the
                    // round penetrates or stops per the surface, but it does not reach the player). The
                    // small margin keeps the player's own contact from re-testing the surface it stands on.
                    if (worldBlocked && worldHit.Distance < hit.Distance - 40.0)
                    {
                        MarkTheImpact();
                        return;
                    }

                    // Only now the probability gate: retail rolls `random > probability -> no hit` inside
                    // the cast's evaluate when the trace actually MEETS a human (the context carries
                    // Human+1236, loaded at spawn by sub_474360). A failed roll is a round that flew past
                    // -- through to whatever stands behind the player, where it marks.
                    if (shotRandom.NextDouble() > probability)
                    {
                        MarkTheImpact();
                        return;
                    }

                    if (playerHealth.IsDead)
                    {
                        // Already down. Nothing kills you twice, and without this the log filled with
                        // hundreds of "DEAD" lines while the guards kept shooting the corpse.
                        return;
                    }

                    playerHealth.Apply(hit.Zone, weapon.DamageFactor);
                    guardHits++;

                    // The screen goes red. A full wash per hit, so a burst holds it solid and it fades
                    // over a second and a half once the fire stops -- the retail decay (sub_411300).
                    damageFlash.Hit();

                    // Every connecting round SHOVES the player 5 km/h along its travel -- the hit
                    // handler (0x412440) normalises the round's direction and adds x/y x 189.62962
                    // into the slide accumulator, whose 0.9-a-tick damping then bleeds it off. This is
                    // what makes sustained fire stagger the player in retail.
                    if (!playerHealth.IsDead)
                    {
                        double shoveX = to.X - from.X;
                        double shoveY = to.Y - from.Y;
                        double shoveZ = to.Z - from.Z;
                        double length = Math.Sqrt((shoveX * shoveX) + (shoveY * shoveY) + (shoveZ * shoveZ));
                        if (length > 1e-6)
                        {
                            player.Body.AddHitShove(
                                (float)(shoveX / length * PlayerBody.HitShoveUnitsPerTick),
                                (float)(shoveY / length * PlayerBody.HitShoveUnitsPerTick));
                        }
                    }

                    logger.Information(
                        "Weapon",
                        $"A guard's {weapon.ScriptId} hit the player in the {HumanDamage.ZoneName(hit.Zone)} "
                            + $"({guardHits} of {guardRounds} round(s); "
                            + $"{playerHealth.RemainingFraction:P0} left).");

                    if (playerHealth.IsDead)
                    {
                        // Locomotion state 47 (0x40E560): the clip comes from the LAST ZONE STRUCK's row
                        // in the player's own hit-zone table, one of its two reactions by rand(2). The
                        // player stops taking input from here on -- it is a state, not a message.
                        playerDead = true;
                        int fall = HumanPlayerDeath.Choose(
                            hit.Zone, HumanHitZoneTable.Player, shotRandom.Next(2));

                        // Hold the screen red. The wash decays a forty-fifth a tick, so on death it is
                        // pinned to full and left to fade -- the last thing the player sees is red.
                        damageFlash.Hit();

                        // Which way the body is thrown. The fall clip carries the body along its own
                        // backward axis, so the facing decides where "backward" points -- and it has to
                        // point AWAY FROM THE SHOOTER, so a hit in the stomach throws you onto your back.
                        // The soldier death does exactly this (SoldierDeath.TurnToTheShot, 0x45DAA0):
                        // it faces the body at the incoming round. Retail's player death instead falls
                        // along the look direction (0x40E560 does not turn), which only reads right when
                        // you were looking straight at the shooter -- so we take the soldier's shot-facing,
                        // which is right however you were facing. The heading whose backward axis (the
                        // clip's -Y, rotated) points from the shooter to the body is atan2(dx, -dy).
                        double throwX = player.Body.Position.X - from.X;
                        double throwY = player.Body.Position.Y - from.Y;
                        double throwLength = Math.Sqrt((throwX * throwX) + (throwY * throwY));
                        float throwFacing = throwLength > 1e-6
                            ? (float)Math.Atan2(throwX / throwLength, -(throwY / throwLength))
                            : player.Body.Yaw;

                        // Locomotion state 47 (0x40E560) plays the fall clip on the body rig; here that is
                        // the player's third-person body, posed with the clip the last zone struck chose.
                        // The camera hands itself to the death view and pulls back off the eye, so the fall
                        // is seen from the outside -- the original's death sequence.
                        soldiers?.KillPlayerBody(fall, player.Body.Position, throwFacing);
                        WorldRayCast deathCaster = solids.Caster();
                        deathCamera.Begin(
                            player.Body.Position,
                            throwFacing,
                            (Vector3d f, Vector3d t, out Vector3d h) =>
                            {
                                bool blocked = deathCaster(f, t, out WorldRayHit wall);
                                h = blocked ? wall.Position : default;
                                return blocked;
                            });

                        // The death SOUND is still not played -- there is no audio subsystem yet, so the
                        // three 'player_death_' variants have nothing to play through. Reported, not faked.
                        logger.Information(
                            "Weapon",
                            $"The player is DEAD after {guardHits} hit(s) from {guardRounds} round(s), "
                                + $"struck in zone {hit.Zone}. Falling with animation {fall}; the death "
                                + $"camera has pulled back. One of {HumanPlayerDeath.SoundVariants} "
                                + $"'{HumanPlayerDeath.SoundPrefix}' sounds is NOT PLAYED (no audio yet).");
                    }
                });
            }
            else
            {
                logger.Warning("Ai", $"No nav graph under '{graphDirectory}', so no soldier will patrol.");
            }
        }
        catch (Exception ex) when (ex is IOException or GraphFormatException)
        {
            logger.Warning("Ai", $"The nav graph could not be read: {ex.Message}");
        }

        // A level whose terrain files are not live data — level 14 is exactly that — draws its objects
        // over nothing rather than following offsets into whatever the geometry blob happens to hold.
        // The surface comes from the level's own TextureModifier tasks, so it needs the loaded tree.
        terrain?.Dispose();
        terrain = loaded.Terrain is { IsUsable: true } usable
            ? new TerrainRenderer(device, TerrainSurface.Build(usable, levelRoot, logger), logger, detailMaps)
            : null;

        // The sky is script data on the level's task tree; its fog colour is also what the frame
        // clears to, so the horizon meets sky-coloured backing rather than black.
        sky?.Dispose();
        sky = new SkyRenderer(device, fileSystem, levelRoot, farPlane, logger);
        shadowPass = new ShadowCasterPass(device);

        if (DirectionalLightSettings.TryRead(levelRoot, out DirectionalLightSettings readSun))
        {
            sun = readSun;
            logger.Information(
                "Light",
                FormattableString.Invariant(
                    $"Sun at {MathF.Asin(Math.Clamp(readSun.Direction.Z, -1f, 1f)) * 180f / MathF.PI:F1} degrees ")
                + FormattableString.Invariant(
                    $"elevation, time {readSun.TimeOfDay:F0}, colour ({readSun.Colour.X:F2}, {readSun.Colour.Y:F2}, {readSun.Colour.Z:F2}), ")
                + FormattableString.Invariant(
                    $"ambient ({readSun.Ambient.X:F2}, {readSun.Ambient.Y:F2}, {readSun.Ambient.Z:F2})."));

            if (readSun.IsBelowHorizon)
            {
                logger.Warning("Light", "This level's authored sun is below the horizon; see rendering.md section 12.");
            }
        }
        else
        {
            logger.Information("Light", "The level declares no Dirlight; the modern path will light it with a default sun.");
        }

        // Now that the sun is known. The pages hold unlit albedo by design, so this is the only thing
        // that puts light into the cache -- and it runs once, because this game's sun is a fixed
        // direction per mission, which is the bargain the whole surface cache is built on.
        if (capturedCards is { } surfaces && cardInstances is { } placedCards)
        {
            cardLighting?.Dispose();
            cardLighting = SurfaceCacheLighting.Create(device, surfaces, placedCards, logger);

            // A fresh pass has no history to accumulate onto, so the next bounce converges in one shot.
            cardBounceFrame = 0;

            if (cardLighting is not null)
            {
                cardLighting.SetLocalLights(ProposeLocalLights(scene, resources, id, logger));

                // Built once, from the same capture and placements the lighting was. A ray that stops
                // somewhere knows only that it stopped; this is what lets it read the surface it hit.
                cardLookup?.Dispose();
                cardLookup = SurfaceCacheLookup.Build(
                    device, surfaces, placedCards, cardLighting.Origin, logger);
                cardLighting.SetLookup(cardLookup);

                // A level with no Dirlight has no sun to light the cards with, so they take the sky
                // alone -- which is what such a level looks like anyway.
                // Unshadowed at load. The clipmaps do not exist yet -- the field bake is still on its
                // worker -- so this establishes sun and sky, and the per-frame pass below adds the
                // shadow once there is a field to trace against.
                cardLighting.Light(
                    sun is { IsBelowHorizon: false } lit ? lit.Direction : default,
                    sun?.Colour ?? default,
                    sun?.Ambient ?? new Vector3f(0.27f, 0.29f, 0.33f));

                if (RenderCVars.SurfaceCacheDump.Value is { Length: > 0 } lightingInto)
                {
                    DumpCardLighting(device, cardLighting, lightingInto, logger, 0, "cards-direct-load.png");
                }
            }
        }

        if (sky.HasSky)
        {
            scheduler.ClearColour = new Bgra32(
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

        // A fresh mission is a fresh life: the last one's death, its red wash and its death camera do not
        // carry over.
        playerHealth = new(HumanHitZoneTable.Player, playerMaximumHealth)
        {
            DamageScale = HumanHealth.DifficultyDamageScale(playerDifficulty),
        };
        playerDead = false;
        damageFlash = new();
        deathCamera.Reset();

        if (playerTerrain is not null)
        {
            PlayerCamera candidate = new();

            // 000.iff, kept so the player's third-person body can be built from the same set it walks on
            // (HumanPlayerBody: the body is skeleton 0, and this is skeleton 0's set).
            OpenIGI.Formats.Animation.BoneAnimationSet? playerBodySet = null;

            // Walking is the animation's own root motion, so without a set the player can stand and
            // fall but not walk. 000.iff is the set the player's skeleton indexes.
            try
            {
                string animations = Path.Combine(gamePath, "common", "anims", "000.iff");
                if (File.Exists(animations))
                {
                    playerBodySet = OpenIGI.Formats.Animation.BoneAnimationSet.Read(File.ReadAllBytes(animations));
                    candidate.UseAnimations(playerBodySet);
                }
                else
                {
                    logger.Warning("Player", $"No animation set at {animations}; the player cannot walk.");
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException)
            {
                logger.Warning("Player", $"Could not read the player's animations: {ex.Message}");
            }

            // The walls. Terrain alone gives the player ground to stand on but nothing to stop it
            // walking through crates and buildings; the solidity is in each model's own HSMC mesh.
            // The index is the one built above for the soldiers' ground probe.
            candidate.UseWalls(solids);
            logger.Information(
                "Player",
                $"{solids.Count} placed object(s) are solid, {solids.TriangleCount} collision triangle(s).");

            // The ladders the level attaches to its buildings, so the player can climb them (E to mount,
            // W/S up and down, E held to slide).
            if (scene is not null)
            {
                candidate.UseLadders(scene.Ladders);
                logger.Information("Player", $"{scene.Ladders.Count} ladder(s) to climb.");
            }

            // ORDER MATTERS, and it is the original's. WEAPON_ID_* and AMMO_ID_* are pattern-registered
            // symbols that get their values as the two tables are read, so the rosters have to load
            // before humanplayer.qvm runs -- otherwise every id in its weapon cycle is a placeholder
            // and the cycle names weapons nobody carries. section 8.
            WeaponTypeRegistry weaponRoster = new(logger);
            AmmoTypeRegistry ammoTable = new(logger);
            GameMaterialRegistry materialTable = new(logger);
            try
            {
                weaponRoster.Register(loop.Natives);
                weaponRoster.Load(fileSystem, resources, loop.Natives);
                ammoTable.Register(loop.Natives);
                ammoTable.Load(fileSystem, resources, loop.Natives);

                // What every surface in the world is made of, and therefore what a round leaves on it.
                materialTable.Register(loop.Natives);
                if (fileSystem.Exists(GameMaterialRegistry.ScriptResource))
                {
                    resources.Register(
                        GameMaterialRegistry.ScriptResource,
                        fileSystem.ReadAllBytes(GameMaterialRegistry.ScriptResource));
                }
                else
                {
                    logger.Warning("Material", $"No material table at {GameMaterialRegistry.ScriptResource}.");
                }

                int materialCount = materialTable.Load(resources, loop.Natives);
                candidate.UseMaterials(materialTable);
                worldMaterials = materialTable;
                logger.Information("Material", $"Read {materialCount} game material(s).");
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or KeyNotFoundException)
            {
                logger.Warning("Weapon", $"Could not read the weapon or ammunition tables: {ex.Message}");
            }

            // The guards' weapons, which have to wait for the roster: a soldier's gun is an authored
            // Gun child task naming a WEAPON_ID_*, and only the table knows which model that draws.
            soldiers?.Arm(loaded, weaponRoster);

            // The player's tuning is not in the executable: humanplayer.qvm is the whole of it, and it
            // is what says how high a jump goes and how far a step travels.
            try
            {
                HumanPlayerConfig config = new();
                config.Register(loop.Natives);
                config.Load(resources, loop.Natives);

                if (config.PlaceholderIds.Count > 0)
                {
                    logger.Warning(
                        "Player",
                        $"humanplayer.qvm left {config.PlaceholderIds.Count} id(s) unresolved; the weapon "
                            + "cycle may name weapons that do not exist.");
                }

                if (config.Parameters is { } tuning)
                {
                    candidate.UseParameters(tuning);

                    // Argument 7 is the player's maximum health (Human+600, 0x410370's overwrite) --
                    // 3.0 in retail, and the single number that sets the player's time-to-kill.
                    playerMaximumHealth = tuning.MaximumHealth;
                    playerHealth = new(HumanHitZoneTable.Player, playerMaximumHealth)
                    {
                        DamageScale = HumanHealth.DifficultyDamageScale(playerDifficulty),
                    };
                }
                else
                {
                    logger.Warning("Player", "humanplayer.qvm published no parameters; using the retail defaults.");
                }

                // The same script says which weapon starts in hand, and it is not a guess any more:
                // 0x40F760's cycle index begins at -1, so the first update lands on entry 0 of
                // DefineHumanPlayerWeaponCycle and steps on until it finds one you carry. The shipped
                // list starts with the knife. section 16.7.
                candidate.UseWeaponCycle(config.WeaponCycle);

                // And the nine categories the number keys select — 0x411000 reads exactly this table.
                candidate.Weapons?.UseCategories(config.Categories);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or KeyNotFoundException)
            {
                logger.Warning("Player", $"Could not run humanplayer.qvm: {ex.Message}");
            }

            // The first-person rig. Every Human builds one in its constructor and gives it skeleton 5
            // and the model 005_01_1 (0x45EB30, 0x464660); it is what the held weapon hangs off, so
            // without it there is nothing to draw a gun on.
            try
            {
                string viewRig = Path.Combine(gamePath, "common", "anims", "005.iff");
                if (File.Exists(viewRig))
                {
                    candidate.UseViewRig(
                        OpenIGI.Formats.Animation.BoneAnimationSet.Read(File.ReadAllBytes(viewRig)));

                    // The arms are the other half of what 0x4644A0 draws, and the half that needs CPU
                    // skinning: 005_01_1 is mesh format 1.
                    arms?.Dispose();
                    arms = new ViewModelRenderer(device, resources, logger);
                    if (!arms.Load(loaded))
                    {
                        arms.Dispose();
                        arms = null;
                    }
                }
                else
                {
                    logger.Warning("Player", $"No view rig at {viewRig}; no weapon can be held.");
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
            {
                logger.Warning("Player", $"Could not read the first-person rig: {ex.Message}");
            }

            // What the mission arms you with is declared, not computed: weapon tasks hang off the
            // level's own HumanPlayer, and every WEAPON_ID_* resolves to a roster entry naming a
            // Gun Model. With the rig in place that model can finally be put where the game puts it.
            heldWeapon?.Dispose();
    hud?.Dispose();
    marks?.Dispose();
            heldWeapon = null;

            if (PlayerLoadout.TryRead(levelRoot, out PlayerLoadout? loadout) && loadout is not null)
            {
                logger.Information("Player", $"Carrying {loadout}.");
                heldWeapon = CarryLoadout(candidate, loadout, loaded, weaponRoster, ammoTable);
                loadedMissionForWeapon = loaded;

                // The player's rounds are offered to the soldiers, which own their own health.
                if (soldiers is { } guards)
                {
                    candidate.Humans = guards.Shooter();
                }

                // The in-game HUD owns its own archive, exactly as StatusScreen's constructor does.
                marks?.Dispose();
                marks = new BulletMarkRenderer(device, resources, logger);

                hud?.Dispose();
                hud = new StatusScreenRenderer(device, logger);
                if (!hud.Load(fileSystem))
                {
                    hud.Dispose();
                    hud = null;
                }
            }

            // The level places its own player: every mission's objects.qvm declares a HumanPlayer task
            // with a Position and a Gamma. Standing there is standing where the game starts you.
            bool placed = PlayerSpawn.TryFind(levelRoot, out PlayerSpawnPoint spawn)
                ? candidate.PlaceAt(playerTerrain, spawn)
                : candidate.Place(playerTerrain, camera.Position, options.CameraYaw);

            if (placed)
            {
                player = candidate;

                // The player's own third-person body -- the rig the death camera reveals. Built from the
                // model the level's HumanPlayer declares, on skeleton 0's set (000.iff), the same one the
                // player walks on. It stays invisible until the player dies. Without the spawn's model or
                // the set there is simply no body to show, and death falls back to the frozen view.
                if (soldiers is { } bodies
                    && playerBodySet is { } bodySet
                    && !string.IsNullOrEmpty(spawn.Model))
                {
                    bodies.LoadPlayerBody(loaded, spawn.Model, bodySet, spawn.Position, spawn.Gamma);
                }

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

    // Puts a weapon in the player's hands and loads the model its roster entry names.
    //
    // Hands the player everything the mission declares, with the ammunition each weapon draws from.
    // WHICH of them ends up in hand is no longer decided here: the weapon cycler's index starts at -1
    // and the first tick lands on entry 0 of DefineHumanPlayerWeaponCycle, stepping on until it finds
    // one that is carried. That retires the divergence this function used to carry -- see
    // the original. The renderer's model follows whatever ends up in hand.
    HeldWeaponRenderer? CarryLoadout(
        PlayerCamera candidate,
        PlayerLoadout loadout,
        LoadedMission loadedMission,
        WeaponTypeRegistry roster,
        AmmoTypeRegistry ammunition)
    {
        if (candidate.View is null || loadout.Weapons.Count == 0)
        {
            return null;
        }

        foreach (LoadoutWeapon declared in loadout.Weapons)
        {
            if (!roster.TryGetByScriptId(declared.WeaponId, out WeaponType weapon))
            {
                logger.Warning("Weapon", $"{declared.WeaponId} is not in the roster; it is not carried.");
                continue;
            }

            // A weapon finds its ammunition by naming a Calibre ID, and -1 -- the knife's -- names
            // none at all, which is exactly why a knife never runs out.
            bool hasAmmo = ammunition.TryGetByCalibre(weapon.CalibreId, out AmmoType ammo);
            candidate.Carry(weapon, hasAmmo ? ammo : null);
        }

        foreach (LoadoutAmmo declared in loadout.Ammunition)
        {
            AmmoType? pool = ammunition.Types.FirstOrDefault(a =>
                string.Equals(a.ScriptId, declared.AmmoId, StringComparison.Ordinal));

            if (pool is not null)
            {
                candidate.Weapons?.Ammunition.Add(pool.AmmoId, declared.Count);
            }
        }

        return new HeldWeaponRenderer(device, resources, logger);
    }

    // Keeps the drawn model on whatever the cycle has put in hand. Called every frame; it only does
    // anything on the tick the weapon actually changed.
    void FollowHeldWeapon(PlayerCamera candidate, HeldWeaponRenderer? renderer, LoadedMission loadedMission)
    {
        if (renderer is null || !candidate.WeaponChanged)
        {
            return;
        }

        candidate.AcknowledgeWeaponChange();

        if (candidate.HeldWeaponType is not { } weapon)
        {
            return;
        }

        if (renderer.Load(loadedMission, weapon.GunModel))
        {
            logger.Information("Weapon", $"Holding {weapon.ScriptId} ('{weapon.GunModel}') on view bone "
                + $"{HumanAttachPoints.FirstPersonWeapon}.");
        }
    }

    // Holds the load open until the probe field lands, keeping the window alive throughout.
    //
    // The pump is the whole point. This runs on the thread that owns the GL context, so a plain wait
    // stops the process answering the operating system and the watchdog resets the driver -- which is
    // the fault that put this bake on a worker to begin with, and it exits 0xCFFFFFFF rather than
    // reporting anything. Draining the queue and presenting a cleared frame costs almost nothing and
    // keeps the process demonstrably alive.
    IrradianceVolume? WaitForLighting(IrradianceProbeBake bake)
    {
        int startedAt = loop.TimeSource.Milliseconds;

        while (true)
        {
            // Taken here rather than in the loop's condition, because TryCollect answers true exactly
            // once by contract -- asking again after the loop to find out what it handed over would
            // get nothing, and the level would load unlit having just waited for its lighting.
            if (bake.TryCollect(out IrradianceVolume? ready))
            {
                logger.Information(
                    "Light", $"Waited {loop.TimeSource.Milliseconds - startedAt} ms for the probe field.");

                return ready;
            }

            if (window.IsClosing)
            {
                bake.Cancel();
                return null;
            }

            window.PumpEvents();

            // Cleared to the level's own fog colour rather than to black, so the wait reads as the
            // level arriving rather than as the picture having gone.
            device.Clear(scheduler.ClearColour);
            device.SwapBuffers();

            // The bake is bounded work on a thread pool rather than a poll of something that may never
            // arrive -- but a level whose trace somehow never finishes must not become a game that
            // never starts. Falling through here leaves the blend to cover it.
            if (loop.TimeSource.Milliseconds - startedAt > LightingWaitMilliseconds)
            {
                logger.Warning(
                    "Light",
                    $"The probe field did not arrive within {LightingWaitMilliseconds / 1000} s; "
                    + "entering the level unlit and blending it in when it lands.");

                return null;
            }
        }
    }

    // Writes the captured atlas out as images, because a capture can only be judged by looking at it.
    // Wrong winding shows as models captured inside-out, a wrong card rotation as mirrored pages, a
    // missing scissor as pages bleeding together -- and each produces an atlas of plausible surfaces.
    // The lit pages, for the same reason the captured ones are dumped: a reconstruction that is
    // subtly wrong produces lit surfaces in slightly the wrong places, which looks like nothing at all
    // until a later stage bounces light off them.
    // How light-like a texture must score before it is treated as a source. The score is the share of a
    // texture's uses that are baked bright, weighted by how bright and discounted for textures used only
    // once or twice -- so half means bright across most of its uses and genuinely bright, not merely
    // pale. Level 1's known emitter scores far above this; the cut keeps out light-coloured walls that
    // happen to stand in the sun.
    const float EmitterScore = 0.5f;

    // Works out which lights a level has, given that it declares none. The survey ranks textures by how
    // much they behave like sources and the extractor turns the winning ones into placed fixtures; both
    // already existed and neither was ever called. What is decided here is only where to cut the
    // ranking, and the cut is deliberately generous -- a fixture that should not be there shows up as a
    // glow in the wrong place, which is reviewable, whereas a missing one is invisible and
    // indistinguishable from the stage not working.
    static IReadOnlyList<LightFixture> ProposeLocalLights(
        WorldScene scene, ResourceRegistry resources, int missionId, IIgiLogger logger)
    {
        // Beside the other authored data, one file per mission. When it exists it is the truth: the
        // survey ran once to propose the list and a human has had the chance to correct it since, exactly
        // the way authored material detail wins over the detail derived from a texture. Reading it also
        // keeps the survey's lightmap decode off the load path entirely.
        string fixturesPath = Path.Combine(
            AppContext.BaseDirectory, "Assets", "fixtures", $"mission-{missionId}.json");

        if (LightFixtureStore.TryLoad(fixturesPath, out IReadOnlyList<LightFixture> saved, logger))
        {
            return saved;
        }

        try
        {
            IReadOnlyList<EmissiveTextureSurvey.Finding> findings =
                EmissiveTextureSurvey.Survey(scene, resources, logger);

            List<string> emitters = [];

            foreach (EmissiveTextureSurvey.Finding finding in findings)
            {
                if (finding.Score >= EmitterScore)
                {
                    emitters.Add(finding.Texture);
                }
            }

            if (emitters.Count == 0)
            {
                logger.Information("Emissive", "No texture in this level looks enough like a light source.");
                LightFixtureStore.Save(fixturesPath, [], logger);
                return [];
            }

            // Named rather than counted. This is inferred from art that was never meant to be read this
            // way, so the list is the first thing to check when a level lights wrongly.
            logger.Information("Emissive", $"Emitter textures: {string.Join(", ", emitters)}.");

            IReadOnlyList<LightFixture> proposed = LightFixtureExtractor.ProposeFromScene(
                scene, emitters, EmitterColour(resources), logger: logger);

            // Written so the proposal can be reviewed and corrected, and so the next load reads it back
            // rather than surveying again.
            LightFixtureStore.Save(fixturesPath, proposed, logger);

            return proposed;
        }
        catch (Exception error) when (error is IOException or InvalidDataException)
        {
            logger.Warning("Emissive", $"The lights could not be proposed: {error.Message}");
            return [];
        }
    }

    // Resolves an emitter texture to its mean colour, linear and normalised so its brightest channel is
    // about one, for the light a fixture drawing it is given. Best effort, and cached per name.
    //
    // A texture that resolves as a standalone asset and decodes without a palette gives its own colour.
    // Most emitter textures do neither: they live inside a level's MTP pack, named the way a material
    // references them rather than as a file, and are paletted -- decoding those wants the pack and its
    // palette, which is not wired here yet. Those fall back to white, a tint a reviewer can correct in
    // the fixtures file, whereas the placement the fixture comes with cannot be guessed later. So a miss
    // here costs the tint, never the fixture, and never the load: the whole probe is guarded, because the
    // resource layer treats a bare texture name as an illegal path and throws rather than returning
    // false.
    static Func<string, Vector3f> EmitterColour(ResourceRegistry resources)
    {
        Dictionary<string, Vector3f> cache = new(StringComparer.OrdinalIgnoreCase);

        return name =>
        {
            if (cache.TryGetValue(name, out Vector3f cached))
            {
                return cached;
            }

            Vector3f colour = new(1f, 1f, 1f);

            foreach (string candidate in new[] { name, name + ".tex", name + ".spr" })
            {
                try
                {
                    if (!resources.TryLoad(candidate, out ReadOnlyMemory<byte> data)
                        || data.Length < 4
                        || !TexFile.IsTexture(data.Span))
                    {
                        continue;
                    }

                    colour = MeanColour(TexFile.Load(data.Span));
                    break;
                }
                catch (Exception)
                {
                    // Illegal-path names, paletted or malformed payloads -- a tint is never worth taking
                    // the load down for. It stays white; the fixtures file lets a reviewer correct it.
                }
            }

            cache[name] = colour;
            return colour;
        };
    }

    // Averages a decoded texture and normalises it so its brightest channel is about one.
    static Vector3f MeanColour(TextureImage image)
    {
        ReadOnlySpan<Bgra32> pixels = image.Pixels;
        (int width, int height) = image.GetLevelSize(0);
        int count = Math.Min(width * height, pixels.Length);

        if (count <= 0)
        {
            return new Vector3f(1f, 1f, 1f);
        }

        double r = 0.0;
        double g = 0.0;
        double b = 0.0;

        for (int i = 0; i < count; i++)
        {
            Bgra32 pixel = pixels[i];
            r += pixel.R;
            g += pixel.G;
            b += pixel.B;
        }

        float mr = (float)(r / count);
        float mg = (float)(g / count);
        float mb = (float)(b / count);

        // Normalised on the brightest channel, so the fixture's colour is a hue and the intensity is left
        // to the area and the scale the renderer applies. A texture that averaged to black -- a fully
        // transparent or empty page -- stays white rather than becoming a division by nothing.
        float peak = MathF.Max(mr, MathF.Max(mg, mb));

        return peak > 1e-3f
            ? new Vector3f(mr / peak, mg / peak, mb / peak)
            : new Vector3f(1f, 1f, 1f);
    }

    // Runs the screen probe gather once and writes its probe atlas to a PNG, to verify the gather reads
    // the G-buffer and reconstructs world positions before it is wired into the frame.
    static void DumpScreenGather(
        IGraphicsDevice device, SurfaceCacheLighting cards, GlobalDistanceField field, Camera3D eye,
        Matrix4f projection, float farPlane, int width, int height, string into, IIgiLogger logger)
    {
        try
        {
            const int step = 8;
            int gridW = (width + step - 1) / step;
            int gridH = (height + step - 1) / step;

            TextureHandle output = device.CreateStorageTexture(
                gridW, gridH, VolumeFormat.Rgba16F, "cards.screen.indirect");

            Vector3f cameraToCache = new(
                (float)(eye.Position.X - cards.Origin.X),
                (float)(eye.Position.Y - cards.Origin.Y),
                (float)(eye.Position.Z - cards.Origin.Z));

            cards.GatherScreen(
                field,
                device.SceneDepth,
                device.SceneNormal,
                DepthUnprojection.From(projection, 0.1f, farPlane),
                eye.Orientation.Transposed(),
                cameraToCache,
                width,
                height,
                step,
                output);

            Bgra32[] pixels = new Bgra32[gridW * gridH];
            device.ReadStorageTexture(output, pixels);

            long lit = 0;

            foreach (Bgra32 pixel in pixels)
            {
                if (pixel.R > 0 || pixel.G > 0 || pixel.B > 0)
                {
                    lit++;
                }
            }

            Directory.CreateDirectory(into);
            string path = Path.Combine(into, "screen-indirect.png");
            PngWriter.Write(path, pixels, gridW, gridH);
            logger.Information(
                "SurfaceCache", $"Wrote {path}: {gridW}x{gridH} probe(s), {lit} carry indirect.");

            device.DestroyTexture(output);
        }
        catch (GraphicsDeviceException error)
        {
            logger.Warning("SurfaceCache", $"The screen gather dump failed: {error.Message}");
        }
    }

    static void DumpCardLighting(
        IGraphicsDevice device, SurfaceCacheLighting lighting, string into, IIgiLogger logger,
        int probe = 0, string name = "cards-direct.png")
    {
        try
        {
            Directory.CreateDirectory(into);

            Bgra32[] pixels = new Bgra32[lighting.Width * lighting.Height];
            device.ReadStorageTexture(lighting.DirectLighting, pixels);

            // The bounce and the atlas a trace actually reads, beside the direct term. Judging indirect
            // light from the direct atlas alone is how a gather that returns nothing goes unnoticed.
            //
            // Guarded on its own, because a diagnostic must not be able to take the game down with it:
            // reading these threw at load, the throw escaped the outer catch, and every run after that
            // died before it reached anything worth measuring while looking exactly like a slow gather.
            try
            {
                Bgra32[] extra = new Bgra32[lighting.Width * lighting.Height];

                foreach ((TextureHandle source, string file) in new[]
                {
                    (lighting.IndirectLighting, "cards-indirect.png"),
                    (lighting.FinalLighting, "cards-final.png"),
                })
                {
                    logger.Information("SurfaceCache", $"Reading {file} from {source}.");
                    device.ReadStorageTexture(source, extra);
                    string other = Path.Combine(into, file);
                    PngWriter.Write(other, extra, lighting.Width, lighting.Height);

                    long carrying = 0;

                    foreach (Bgra32 pixel in extra)
                    {
                        if (pixel.R > 0 || pixel.G > 0 || pixel.B > 0)
                        {
                            carrying++;
                        }
                    }

                    logger.Information("SurfaceCache", $"Wrote {other}: {carrying} texel(s) carry light.");
                }
            }
            catch (Exception error)
            {
                logger.Warning("SurfaceCache", $"The bounce atlases could not be written: {error.Message}");
            }

            string path = Path.Combine(into, name);
            PngWriter.Write(path, pixels, lighting.Width, lighting.Height);

            // A probe is judged by its numbers, not its thumbnail: each channel is a question the
            // shader answered per texel, and min/mean/max over the covered texels is the answer.
            if (probe != 0)
            {
                long covered = 0;
                long inside = 0;
                long sumR = 0, sumG = 0, sumB = 0;
                int minR = 255, minG = 255, minB = 255;
                int maxR = 0, maxG = 0, maxB = 0;

                foreach (Bgra32 pixel in pixels)
                {
                    // The shader writes alpha for every covered texel: one when a clipmap contained
                    // the probe point, half when none did. Uncovered atlas stays at zero.
                    if (pixel.A < 64)
                    {
                        continue;
                    }

                    covered++;
                    inside += pixel.A >= 192 ? 1 : 0;
                    sumR += pixel.R;
                    sumG += pixel.G;
                    sumB += pixel.B;
                    minR = Math.Min(minR, pixel.R);
                    minG = Math.Min(minG, pixel.G);
                    minB = Math.Min(minB, pixel.B);
                    maxR = Math.Max(maxR, pixel.R);
                    maxG = Math.Max(maxG, pixel.G);
                    maxB = Math.Max(maxB, pixel.B);
                }

                long n = Math.Max(covered, 1);
                logger.Information(
                    "SurfaceCache",
                    $"Probe {probe} over {covered} texel(s), {inside * 100.0 / n:0.#}% inside a "
                    + $"clipmap: R {minR}..{maxR} mean {sumR / (double)n:0.#}, "
                    + $"G {minG}..{maxG} mean {sumG / (double)n:0.#}, "
                    + $"B {minB}..{maxB} mean {sumB / (double)n:0.#}.");
                return;
            }

            // What share of the written pages the sun actually reaches, reported rather than eyeballed.
            // A shadow pass is judged by this number: too high and it is not shadowing, too low and it
            // is shadowing everything, and both look plausible in a thumbnail. Unwritten atlas is
            // excluded, because at 77% occupancy the empty quarter would dominate either way.
            long written = 0;
            long bright = 0;

            foreach (Bgra32 pixel in pixels)
            {
                if (pixel.R == 0 && pixel.G == 0 && pixel.B == 0)
                {
                    continue;
                }

                written++;

                if (Math.Max(Math.Max(pixel.R, pixel.G), pixel.B) >= 128)
                {
                    bright++;
                }
            }

            logger.Information(
                "SurfaceCache",
                $"Wrote {path}: {bright * 100.0 / Math.Max(written, 1):0.#}% of {written} lit texel(s) "
                + "are in sunlight.");
        }
        catch (Exception error)
            when (error is IOException or UnauthorizedAccessException or GraphicsDeviceException)
        {
            logger.Warning("SurfaceCache", $"The card lighting could not be written: {error.Message}");
        }
    }

    static void DumpCardAtlas(
        IGraphicsDevice device, SurfaceCacheCapture.Result cards, string into, IIgiLogger logger)
    {
        try
        {
            Directory.CreateDirectory(into);

            int width = cards.Layout.Width;
            int height = cards.Layout.Height;
            Bgra32[] pixels = new Bgra32[width * height];

            string[] names = ["albedo", "normal", "material", "depth"];

            for (int attachment = 0; attachment < names.Length; attachment++)
            {
                device.ReadCardAtlas(attachment, pixels);

                string path = Path.Combine(into, $"cards-{names[attachment]}.png");
                PngWriter.Write(path, pixels, width, height);
                logger.Information("SurfaceCache", $"Wrote {path}.");
            }
        }
        catch (Exception error)
            when (error is IOException or UnauthorizedAccessException or GraphicsDeviceException)
        {
            logger.Warning("SurfaceCache", $"The card atlas could not be written: {error.Message}");
        }
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

        for (int i = 0; i < scene.Placements.Count; i++)
        {
            xs[i] = scene.Placements[i].Position.X;
            ys[i] = scene.Placements[i].Position.Y;
        }

        Array.Sort(xs);
        Array.Sort(ys);

        double centreX = xs[xs.Length / 2];
        double centreY = ys[ys.Length / 2];
        double ground = scene.GroundHeight;

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
            ["LOCAL:menusystem/missionsprites.res"])
        {
            Scale = options.UiScale,
        };

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

    // R11: the cinematic shell (OpenIGI.Frontend) is the default front end; -vanilla and -vanillamenu
    // reach the byte-for-byte retail menus instead. -mission still skips the menus straight into a level.
    Shell? shell = null;
    MenuShowcaseCamera? showcaseCamera = null;
    MissionWarmer? missionWarmer = null;
    UiFont? shellBodyFont = null;
    UiFont? shellHeadingFont = null;
    bool menuMode = false;
    const int ShowcaseMissionId = 1;

    if (options.StartMission > 0)
    {
        StartMission(options.StartMission);
    }
    else if (options.VanillaMenu)
    {
        loop.Flow.Request(FlowEvent.MainMenu);
    }
    else
    {
        MenuCameraLibrary menuCameras = MenuCameraLibrary.Load(
            Path.Combine(AppContext.BaseDirectory, "Assets", "menu-cameras.json"), logger);
        missionWarmer = new MissionWarmer(gamePath, missions, logger);

        // Saira for body, Saira Condensed as a distance field for the headings the shell sets large.
        shellBodyFont = UiFontLibrary.Load(device, UiFontLibrary.Body, 48f);
        shellHeadingFont = UiFontLibrary.Load(device, UiFontLibrary.Heading, 64f, FontAtlasKind.SignedDistanceField);
        var shellTheme = new UiTheme(shellBodyFont, shellHeadingFont);

        // The real DefineMission data, resolved to display strings through the language table.
        var shellMissions = new List<MissionInfo>();
        int missionNumber = 1;
        foreach (MissionDescriptor descriptor in missions.Missions)
        {
            shellMissions.Add(new MissionInfo(
                descriptor.Id,
                missionNumber++,
                strings.Get(descriptor.Name),
                strings.Get(descriptor.Description),
                descriptor.BriefingSprite));
        }

        var saves = new SaveSlotStore(Path.Combine(SettingsPaths.GetUserDirectory(), "saves"));

        var shellHost = new DesktopShellHost(
            startMission: (id, difficulty) =>
            {
                if (configuration.Active is { } activeProfile)
                {
                    activeProfile.Difficulty = difficulty;
                }

                menuMode = false;
                StartMission(id);
            },
            quit: () => running = false,
            previewMission: id => missionWarmer?.Warm(id ?? ShowcaseMissionId),
            saveSettings: () => console.SaveSettings(),
            captureThumbnail: (out int captureWidth, out int captureHeight) =>
            {
                captureWidth = window.Width;
                captureHeight = window.Height;
                var pixels = new Bgra32[captureWidth * captureHeight];
                device.CaptureFrame(captureWidth, captureHeight, pixels);
                return pixels;
            });

        shell = new Shell(new ShellContext(
            shellTheme,
            shellHost,
            new ScreenStack(),
            GameProfileResolver.BuildCatalogue(gamePath, null, null),
            shellMissions,
            CVarRegistry.Default,
            GraphicsPresetRegistry.Default,
            configuration,
            saves));

        // Load a showcase level behind the menu and orbit it slowly; heavy depth of field frames the UI.
        StartMission(ShowcaseMissionId);
        playerMode = false;
        menuMode = true;

        Vector3d showcaseCentre = default;
        if (scene is { Placements.Count: > 0 } showcaseScene)
        {
            double[] centreXs = new double[showcaseScene.Placements.Count];
            double[] centreYs = new double[showcaseScene.Placements.Count];
            for (int i = 0; i < showcaseScene.Placements.Count; i++)
            {
                centreXs[i] = showcaseScene.Placements[i].Position.X;
                centreYs[i] = showcaseScene.Placements[i].Position.Y;
            }

            Array.Sort(centreXs);
            Array.Sort(centreYs);
            showcaseCentre = new Vector3d(
                centreXs[centreXs.Length / 2],
                centreYs[centreYs.Length / 2],
                showcaseScene.GroundHeight + 15.0);
        }

        showcaseCamera = new MenuShowcaseCamera(showcaseCentre, menuCameras.For(ShowcaseMissionId));

        // Heavy depth of field behind the UI, on the deferred path the shell's preset leaves on.
        RenderCVars.FocalDistance.Value = 40f;
        RenderCVars.FocalRange.Value = 18f;
        RenderCVars.FocalBlur.Value = 1f;
    }

    window.CloseRequested += (_, _) => running = false;
    window.Show();
    logger.Information("Desktop", $"{platform.Name} up on {device.Capabilities.Renderer}.");

    // Proved once, here, rather than discovered several stages later as a wrong picture. Everything
    // from the distance fields onwards is compute, and none of it draws anything until a good deal of
    // it is in place -- so a driver that mis-binds a buffer or ignores a barrier would first show up
    // as lighting that looks subtly wrong, a very long way from its cause.
    if (GLComputeSelfTest.Run(device, logger))
    {
        // Compiled here rather than when a level first needs it, which is a long way into a run. A
        // shader that does not compile would surface there as a stage that silently did nothing.
        GlobalDistanceField.ValidateShader(device, logger);
    }

    // Used only for a level that declares no Dirlight at all. The angles are mission 1's, which the
    // probe showed put the sun 60 degrees up at noon, with the warm sun and cool ambient every retail
    // level pairs.
    DirectionalLightSettings defaultSun = new(
        DirectionalLightSettings.DirectionFrom(0.5236f, -4.92183f),
        new Vector3f(1.0f, 0.96f, 0.88f),
        new Vector3f(0.27f, 0.29f, 0.33f),
        Beta: 0.5236f,
        Gamma: -4.92183f,
        TimeOfDay: 12f,
        Intensity: 18f,
        AffectsTerrain: true,
        AffectsObjects: true);

    // Reused every frame rather than reallocated; the fit writes into it in place.
    ShadowCascade[] cascadeStorage = new ShadowCascade[ShadowSettings.MaxCascades];

    int lastCameraMilliseconds = loop.TimeSource.Milliseconds;
    int lastConsoleMilliseconds = loop.TimeSource.Milliseconds;
    int lastShellMilliseconds = loop.TimeSource.Milliseconds;
    int lastMenuCameraMilliseconds = loop.TimeSource.Milliseconds;

    // A capture run is a background process as far as the person at the keyboard is concerned: it must
    // not read the mouse and must never grab the cursor out from under whatever they are actually doing.
    bool captureMode = options.Lockstep;

    while (running && loop.IsRunning && !window.IsClosing)
    {
        window.PumpEvents();

        // The probe bake, if one is still tracing. Collected HERE rather than on the worker because
        // what comes back becomes a GL texture, and the context belongs to this thread. TryCollect
        // answers true once, so this costs a completed-flag read per frame until then and nothing after.
        if (probeBake is { } pendingBake && pendingBake.TryCollect(out IrradianceVolume? bakedField))
        {
            probeField = bakedField;
            probeBake = null;
            probeArrivedAt = loop.TimeSource.Milliseconds;
            device.SetIrradianceVolume(probeField);
        }

        // Same handover, same reason: the atlas becomes a GL volume, and the context belongs here.
        if (sdfBake is { } pendingField && pendingField.TryCollect(out DistanceFieldBake.Result baked))
        {
            sdfBake = null;
            sdfScene = baked.Scene;
            sdfClipmaps = GlobalDistanceField.Create(
                device,
                baked.Scene,
                baked.Atlas,
                GlobalDistanceFieldSettings.Default,
                logger);
        }

        // One tick's worth of synthetic time per RENDERED FRAME, from an absolute target rather than
        // an accumulated step so the schedule cannot drift. Per loop pass would be wrong: the loop
        // pumps many times per frame, so the clock would jump a whole tick each pump and the catch-up
        // cap would fire every pass -- measured at 200 ticks for 20 frames, a world running ten times
        // too fast. Deterministic either way, but a capture should show the moment it claims to.
        if (lockstepClock is not null)
        {
            lockstepClock.Milliseconds = (int)(((loop.Clock.FrameCount + 1L) * 1000L) / GameClock.DefaultTickRate);

            // A capture must not be able to see the mouse. The window grabs the cursor in player mode,
            // so moving the mouse in another application changed what got captured -- which is exactly
            // how the first reference set came out non-reproducible for a reason that had nothing to
            // do with the clock.
            window.Input.Captured = true;
        }

        // The console reads the frame's input first. If it is open it then marks the frame captured,
        // and every other reader below -- the debug keys, the cameras, the player, the menu -- sees an
        // idle keyboard and mouse. That is why none of them had to learn the console exists.
        int consoleNow = loop.TimeSource.Milliseconds;
        console.Update(window.Input, Math.Clamp((consoleNow - lastConsoleMilliseconds) / 1000f, 0f, 0.25f));
        lastConsoleMilliseconds = consoleNow;

        // The cinematic shell reads the frame's input after the console and, like it, marks the frame
        // captured so the free camera and the game beneath see an idle keyboard and pad. When the console
        // is open it owns the frame and the shell only advances its animation.
        if (menuMode && shell is not null)
        {
            int shellNow = loop.TimeSource.Milliseconds;
            float shellElapsed = Math.Clamp((shellNow - lastShellMilliseconds) / 1000f, 0f, 0.1f);
            lastShellMilliseconds = shellNow;

            var shellFrame = new LayoutContext(window.Width, window.Height);
            shell.Update(shellFrame, window.Input, shellElapsed);
            if (!console.IsOpen)
            {
                window.Input.Captured = true;
            }
        }

        // The camera is advanced on every pass of the loop, not only on the passes that render. The
        // pump clears the mouse's motion accumulator, and the loop pumps several times per rendered
        // frame — so consuming it only on a render threw most of the mouse movement away and made the
        // view lurch. It also runs on real elapsed time: a fixed step tied flying speed to the frame
        // rate, which is not what a person moving a mouse expects.
        // F12 toggles the debug overlay: the player's position and angle, and a crosshair readout of
        // whatever is under it — an object (with its task), a soldier, or nothing.
        if (window.Input.Keyboard.WasPressed(OpenIGI.Input.KeyCode.F12))
        {
            showDebug = !showDebug;
        }

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
                else
                {
                    // A placement is a teleport; the eye pair must not be interpolated across it.
                    playerEyeCaptured = false;
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

            if (playerDead && deathCamera.IsActive)
            {
                // Dead: the mouse orbits the death camera around the body rather than turning a head that
                // is no longer taking input -- HumanCamera_DeathCameraControlInput. The cursor stays locked
                // so the orbit keeps turning however far the mouse is moved.
                deathCamera.Control(rawX, rawY);
                window.CursorLocked = !captureMode;
            }
            else if (player is { IsOnLadder: true })
            {
                // On a ladder the mouse orbits the third-person interaction camera around the climbing
                // body, as the original's mode-3 camera does -- HumanCamera_ThirdPersonControlInput -- so
                // you can look around while you climb rather than being locked behind the character.
                interactionCamera.Control(rawX, rawY);
                window.CursorLocked = !captureMode;
            }
            else if (playerMode && player is not null)
            {
                // Looking is per frame so the view is as smooth as the display allows; the body itself
                // moves on the fixed tick below, so it steps identically whatever the frame rate.
                player.Look(window.Input, elapsed);
                window.CursorLocked = !captureMode && player.IsCapturingMouse;
            }
            else
            {
                camera.Update(window.Input, elapsed);

                // The cursor is locked away while a mouse button is held, the way a scene editor's is,
                // so the view keeps turning however far the mouse is dragged. It comes back on release.
                window.CursorLocked = !captureMode && camera.IsCapturingMouse;
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

        // The original's schedule admits ONE render per tick, which caps the whole presentation at
        // 30 Hz however fast the machine is. Smooth mode lifts exactly that latch and nothing else:
        // tick deadlines, the catch-up budget and everything the simulation observes are untouched.
        // Lockstep keeps the original pairing -- its synthetic clock advances per frame and the
        // reference captures depend on the one-to-one schedule.
        loop.Clock.UncappedRenders = !options.Lockstep && RenderCVars.SmoothPresentation.Value;

        FrameOutcome outcome = loop.RunFrame();

        // The body advances on the simulation tick and ONLY on it. Tick and Render are mutually
        // exclusive outcomes - a render is not a tick - so including Render here stepped the player
        // once per drawn frame as well, and at an uncapped frame rate that is several times 30 Hz.
        // The player then moved as fast as the display was quick, which is exactly what it looked like.
        if (outcome is FrameOutcome.Tick
            && playerMode && player is not null && playerTerrain is not null)
        {
            // A dead player is in locomotion state 47, whose per-tick is not the walking one -- so the
            // body stops being stepped rather than being stepped with no input. Without this a corpse
            // walks around.
            if (!playerDead)
            {
                player.Tick(window.Input, playerTerrain);

                // The eye's tick pair, for drawing frames between ticks from a position that glides
                // instead of stepping. Captured AFTER the body moved, so the pair is this tick's and
                // the one before's.
                playerEyePrevTick = playerEyeCaptured ? playerEyeTick : player.Camera.Position;
                playerEyeTick = player.Camera.Position;
                playerEyeCaptured = true;
            }

            // The first round of a mission, reported once — the same one-shot diagnostic the world
            // draw uses. It is what says the whole chain arrived: the trigger reached the state
            // machine, the machine reached the gun, the gun's clock let a round out, and the round
            // went somewhere.
            if (!reportedShot && player.FiredThisTick && player.HeldWeaponType is { } fired)
            {
                reportedShot = true;
                string where = player.Impacts.Count == 0
                    ? "nowhere"
                    : player.Impacts[0].HitSomething
                        ? $"{player.Impacts[0].Position}"
                        : $"nothing inside {fired.EffectiveRange} m";

                logger.Information(
                    "Weapon",
                    $"Fired {fired.ScriptId} — {GunTiming.TicksPerRound(fired.RoundsPerMinute)} tick(s) "
                        + $"a round at {fired.RoundsPerMinute} rpm, "
                        + $"{player.Weapons?.InHand?.Gun.RoundsInClip} left in the magazine, into {where}.");
            }

            // Every round that lands on a guard, reported once each so the zone and the kill are
            // visible without a per-shot log.
            if (player.HumanHits != reportedHumanHits)
            {
                reportedHumanHits = player.HumanHits;
                logger.Information(
                    "Weapon",
                    $"Hit a guard in zone {player.LastHumanZone} "
                        + $"({reportedHumanHits} hit(s), {soldiers?.Killed ?? 0} killed).");
            }
        }

        // The soldiers' rigs advance on the tick, not on the render, so their animation runs at the
        // engine's fixed 30 Hz whatever the frame rate is - the determinism contract's whole point.
        //
        // Tick and Render are SEPARATE outcomes and one logic tick produces one of each, so accepting both
        // here ran the AI and its rigs TWICE per tick: guards walked and animated at exactly double speed.
        // Measured against the original, which covers 160-185 units a tick, this port was covering ~357.
        if (outcome is FrameOutcome.Tick)
        {
            // The damage wash fades a forty-fifth a tick whatever the frame rate -- decayed here, on the
            // fixed tick, for the same determinism reason the bodies are.
            damageFlash.Tick();

            // The player's eye is what the guards' view cones are tested against -- and in the
            // fly-through it is the CAMERA's, not the body's. The body stops being stepped the moment
            // you leave walking mode, so feeding its eye meant every guard in the level was watching an
            // immobile figure at the spawn point while you flew past them: they never saw you because
            // there was nothing of you to see. A shell affordance, not engine behaviour.
            Vector3d watched = playerMode && player is not null
                ? player.Body.Eye
                : camera?.Position ?? player?.Body.Eye ?? default;

            // A dead player is no longer a target: the guards lose it and stop firing at the corpse, the
            // way the retail detection scan (0x489750) unregisters a human that has died.
            //
            // The eye is what vision watches; the FEET are what combat aims at (sub_451380 raises the
            // target task's position 0.5 m, 0.25 m crouched) -- in the fly-through there is no body, so
            // the camera stands in for both.
            Vector3d watchedFeet = playerMode && player is not null
                ? player.Body.Position
                : watched;
            soldiers?.Tick(
                watched,
                enemyAlive: !playerDead,
                enemyFeet: watchedFeet,
                enemyCrouching: playerMode && player is not null && player.IsCrouched);

            // The player's own body advances its death fall on the tick, the same as a soldier's corpse --
            // it does nothing until the player has died and the body was posed.
            soldiers?.TickPlayerBody();

            // On a ladder the same third-person body climbs (locomotion state 37, animation 168): it is
            // placed on the rungs and its climb clip advances while up or down is held, so the player sees
            // their character climb rather than a first-person slide. Hidden again on leaving the ladder.
            if (player is not null && player.IsOnLadder)
            {
                // Rung-by-rung climbing (state 37). Input is taken only at a rung boundary, so a rung once
                // begun plays to completion (you cannot cancel mid-step); holding up or down begins the next
                // rung the instant the last one's clip ends -- one rung per clip cycle, alternating hands.
                OpenIGI.Input.KeyboardState climbKeys = window.Input.Keyboard;
                bool climbUp = climbKeys.IsDown(OpenIGI.Input.KeyCode.W);
                bool climbDown = climbKeys.IsDown(OpenIGI.Input.KeyCode.S);
                bool climbUse = climbKeys.IsDown(OpenIGI.Input.KeyCode.E);

                // On mounting: start the climb clip as one continuous loop and arm the slide only after the
                // mount key (also E) is released, so mounting does not immediately slide.
                if (!climbActive)
                {
                    climbActive = true;
                    climbSlideArmed = false;
                    soldiers?.StartPlayerClimbLoop(PlayerCamera.LadderClimbAnimation);
                }

                if (!climbUse)
                {
                    climbSlideArmed = true;
                }

                bool climbSlide = climbUse && climbSlideArmed;

                // Input is taken only at a rung boundary; a rung once begun runs to the clip's next wrap.
                if (player.LadderAtBoundary)
                {
                    player.DecideLadderStep(climbUp, climbDown, climbSlide);
                }

                if (player.IsOnLadder && !player.LadderAtBoundary && soldiers is not null)
                {
                    // Mid-rung: move the body by the climb clip's OWN root motion -- the same way a walking
                    // guard is moved -- so it sways and climbs exactly as the retail game does (read from the
                    // Ladder_ClimbTick probe). The rung commits when the clip completes a cycle.
                    soldiers.AdvancePlayerClimbStep(player.LadderFacing, out Vector3d rootDelta, out _);
                    if (player.MoveLadder(rootDelta))
                    {
                        player.CompleteLadderStep();
                    }
                }

                // The third-person camera takes over on mounting and follows the body up the rungs; the
                // body is placed at this tick's feet (the clip advance above set the pose).
                if (player.IsOnLadder)
                {
                    soldiers?.PlacePlayerBodyClimbing(player.LadderFeet, player.LadderFacing);
                    if (!interactionCamera.IsActive)
                    {
                        interactionCamera.Begin(player.LadderFeet, player.LadderFacing);
                    }
                    else
                    {
                        interactionCamera.Follow(player.LadderFeet);
                    }
                }
            }

            if ((player is null || !player.IsOnLadder) && (climbActive || interactionCamera.IsActive))
            {
                soldiers?.HidePlayerBodyClimbing();
                interactionCamera.Reset();
                climbActive = false;
            }
        }

        if (outcome != FrameOutcome.Render)
        {
            continue;
        }

        if (scene is not null && world is not null && camera is not null)
        {
            float aspectRatio = (float)window.Width / Math.Max(1, window.Height);
            float verticalFieldOfView = VerticalFieldOfView(aspectRatio);
            // The near plane is the original's default: 0x493580 initialises it to 409.6 units, which
            // is 0.1 m. (Scripts can move it through the SetNearClippingPlane native, 0x497D00.) At
            // 0.2 the plane reached twice as far as retail's and cut into any wall the sweep lets the
            // body stand 2.5 mm from.
            Matrix4f unjitteredProjection = Matrix4f.CreatePerspective(
                verticalFieldOfView,
                aspectRatio,
                near: 0.1f,
                far: farPlane);

            // Temporal accumulation works by sampling a different point inside each pixel every frame
            // and averaging what comes back, so the jitter is not a refinement of the technique -- it
            // is the technique. Without it every frame samples the same pixel centre and the history
            // is an average of identical samples, which is a blur and nothing else.
            //
            // It is a translation in clip space, which is exactly a sub-pixel shift of the whole
            // frustum: clip.xy += offset * clip.w, applied uniformly at every depth. Moving the camera
            // instead would shift the near plane more than the far one and reproject wrongly.
            bool wantsTemporal = RenderCVars.TemporalFeedback.Value < 1f
                && RenderCVars.OffscreenScene.Value
                && RenderCVars.Deferred.Value;

            Matrix4f projection = unjitteredProjection;
            float jitterX = 0f;
            float jitterY = 0f;

            if (wantsTemporal)
            {
                (jitterX, jitterY) = TemporalJitter.At(temporalFrame);

                // In RENDER pixels, not window pixels: the offset has to be half a pixel of the buffer
                // being rasterised into, or at a render scale below one it moves the sample by less
                // than the pixel it is meant to be exploring and the upsample has nothing to gather.
                projection = Matrix4f.CreateTranslation(
                    jitterX * 2f / Math.Max(1, (int)(window.Width * RenderCVars.RenderScale.Value)),
                    jitterY * 2f / Math.Max(1, (int)(window.Height * RenderCVars.RenderScale.Value)),
                    0f) * unjitteredProjection;
                temporalFrame++;
            }

            // The sky is behind everything and writes no depth, so it goes first; then terrain, which
            // is opaque and covers most of the screen, so drawing it before the objects gives the
            // depth test the most to reject.
            //
            // A dead player watches from the death camera -- pulled back off the eye to a third-person
            // shot of the body -- rather than from the frozen first-person view.
            // On a ladder the eye pulls back to a third-person interaction view (camera mode 3), so you
            // see your character on the rungs rather than the back of your hands.
            // How far this frame sits between the last two simulation ticks. Everything the
            // simulation stepped -- the player's own position, the soldiers, their poses -- is drawn
            // blended between its last two tick states by this amount, which is what makes an
            // uncapped frame rate look uncapped while the simulation stays at exactly 30 Hz.
            bool smoothPresentation = !options.Lockstep && RenderCVars.SmoothPresentation.Value;
            float tickBlend = smoothPresentation ? loop.Clock.TickFraction(loop.TimeSource.Milliseconds) : 1f;

            Camera3D eye = playerDead && deathCamera.IsActive
                ? deathCamera.Camera
                : player is { IsOnLadder: true } && interactionCamera.IsActive
                    ? interactionCamera.Camera
                    : playerMode && player is not null ? player.Camera : camera.Camera;

            // The walking eye glides between its tick pair, and so does the recoil kick inside its
            // orientation; the mouse half of the look is this frame's own. The fly, death and ladder
            // cameras move per frame on their own and need nothing.
            if (smoothPresentation
                && playerMode
                && !playerDead
                && playerEyeCaptured
                && player is { IsOnLadder: false })
            {
                eye = new Camera3D(
                    new Vector3d(
                        playerEyePrevTick.X + ((playerEyeTick.X - playerEyePrevTick.X) * tickBlend),
                        playerEyePrevTick.Y + ((playerEyeTick.Y - playerEyePrevTick.Y) * tickBlend),
                        playerEyePrevTick.Z + ((playerEyeTick.Z - playerEyePrevTick.Z) * tickBlend)),
                    player.SmoothedLookRotation(tickBlend));
            }

            // Behind the cinematic shell the eye is the slow menu orbit, on real elapsed time, whatever
            // camera the scene would otherwise have used.
            if (menuMode && showcaseCamera is not null)
            {
                int menuCameraNow = loop.TimeSource.Milliseconds;
                float menuCameraElapsed = Math.Clamp((menuCameraNow - lastMenuCameraMilliseconds) / 1000f, 0f, 0.1f);
                lastMenuCameraMilliseconds = menuCameraNow;
                eye = showcaseCamera.Update(menuCameraElapsed);
            }

            // Everything soldier-shaped resolves its own frame state once, here, so the shadow
            // cascades and the geometry pass draw identical bodies in identical places.
            soldiers?.BeginFrame(tickBlend, smoothPresentation);

            // The modern path renders the scene offscreen so that it still exists, with its depth, once
            // it has been drawn -- which is what every effect from stage R3 on needs. With r_hdr off,
            // or on a backend that cannot offer a target, nothing here happens and the frame is drawn
            // straight to the back buffer exactly as the original draws it.
            SceneTargetDescription sceneTarget = new(
                Math.Max(1, (int)(window.Width * RenderCVars.RenderScale.Value)),
                Math.Max(1, (int)(window.Height * RenderCVars.RenderScale.Value)),
                RenderCVars.OffscreenScene.Value ? Math.Max(1, RenderCVars.MultiSampling.Value) : 1,
                Deferred: RenderCVars.OffscreenScene.Value && RenderCVars.Deferred.Value,

                // An 8-bit target under the vanilla preset: with no tone map and unit exposure the whole
                // offscreen detour is then a copy, which is what keeps that preset identical to the
                // build that had no offscreen path at all.
                HighDynamicRange: RenderCVars.ToneMap.Value != ToneMapOperator.None,

                // Asked for only when the accumulation is running, because it is the accumulation that
                // does the upsampling: without it these are two buffers holding a stretched copy.
                OutputWidth: wantsTemporal && RenderCVars.RenderScale.Value < 1f ? window.Width : 0,
                OutputHeight: wantsTemporal && RenderCVars.RenderScale.Value < 1f ? window.Height : 0);

            // The sun arrives in world space and the G-buffer's normals are in view space, so it is
            // rotated by the camera here. Transposed() is not decoration: the rows of the orientation
            // ARE the view axes, and Transform() applies the engine's row-vector convention which dots
            // the columns -- the inverse rotation. Getting it wrong lights the world from a direction
            // that swings as the player turns.
            DirectionalLightSettings light = sun ?? defaultSun;
            Vector3f viewSunDirection = eye.Orientation.Transposed().Transform(light.Direction);

            bool wantsShadows = RenderCVars.OffscreenScene.Value
                && RenderCVars.Deferred.Value
                && RenderCVars.Shadows.Value > 0;

            ShadowSettings shadowSettings = wantsShadows
                ? new ShadowSettings(
                    RenderCVars.ShadowCascades.Value,
                    RenderCVars.Shadows.Value,
                    RenderCVars.ShadowDistance.Value)
                {
                    ContactReach = RenderCVars.ContactShadows.Value,
                    FilterRadius = RenderCVars.ShadowSoftness.Value,
                }
                : ShadowSettings.Off;

            int cascadeCount = ShadowCascades.Fit(
                eye,
                light.Direction,
                shadowSettings,
                verticalFieldOfView,
                aspectRatio,
                0.1f,
                cascadeStorage);

            device.SetShadows(
                shadowSettings,
                cascadeStorage.AsSpan(0, cascadeCount),
                DepthUnprojection.From(unjitteredProjection, 0.1f, farPlane),

                // View space back into camera-relative world space, which is the frame the cascades
                // are fitted in. The same transpose, for the same reason.
                eye.Orientation.Transposed());

            // The medium. Density comes from the level's own FlatSky "Fog Amount" unless overridden:
            // a level that asked for thick fog gets thick fog, so the missions keep their own moods
            // instead of all being handed one number. The colours come from the sun, so a fog bank is
            // lit by the same light as the ground under it.
            float authoredDensity = sky is { HasSky: true }
                ? 0.004f + (Math.Clamp(sky.FogAmount, 0f, 1f) * 0.03f)
                : 0.014f;

            bool wantsFog = RenderCVars.FogQuality.Value > 0
                && RenderCVars.OffscreenScene.Value
                && RenderCVars.Deferred.Value;

            VolumetricFogSettings medium = VolumetricFogSettings.Off;
            if (wantsFog)
            {
                // The sky plane's height is an offset above the ground, not a world Z: a level's
                // coordinates carry a kilometres-large absolute offset, so it has to be anchored to
                // the scene's own ground before the march can compare a sample against it.
                medium = fogPresets.Resolve(
                    options.StartMission,
                    authoredDensity,
                    (float)(scene.GroundHeight + (sky is { HasSky: true } ? sky.PlaneHeight : 0f)),
                    RenderCVars.FogQuality.Value);

                // A console override beats the authored preset, so a value can be dialled in live and
                // then written back into the file.
                if (RenderCVars.FogDensity.Value > 0f)
                {
                    medium = medium with { Density = RenderCVars.FogDensity.Value };
                }
            }

            // The bake stored how much sky reaches each probe, never what colour it was, so the
            // mission's own sky is applied here -- and a level whose sun sits below the horizon leans
            // on its ambient exactly as the fog does.
            // The clipmaps follow the camera. Each level is rebuilt only when the camera has left it,
            // which for the outer levels is almost never -- I.G.I.'s geometry is static, so a clipmap
            // only becomes wrong by being walked out of.
            if (sdfClipmaps is { } liveField)
            {
                int rebuiltLevels = liveField.Update(eye.Position);

                // Once, after the first build, and only when asked. A wrong distance field produces a
                // picture that is merely subtly wrong several stages later, so the only way to know it
                // is right is to compare it against the exact answer at the same points.
                if (!sdfValidated && RenderCVars.DistanceFieldValidate.Value && sdfScene is { } exact)
                {
                    sdfValidated = true;
                    // Every level, not just the innermost. A trace falls through to the outer
                    // clipmaps as soon as it leaves the fine one, so an unbuilt level 2 is invisible to
                    // a check that only ever looked at level 0 -- and it shadows everything.
                    for (int level = 0; level < liveField.Settings.Levels; level++)
                    {
                        DistanceFieldValidation.Compare(device, liveField, exact, level, logger);
                    }
                }

                // Re-lit whenever a clipmap level moved, and only then. The clipmaps follow the camera
                // and reach a few hundred metres, so a page on the far side of the level has no field
                // to trace against -- re-lighting as they move is what brings each page's shadow in as
                // the player approaches it. Doing it every frame regardless would be tens of thousands
                // of work groups spent re-deriving an answer that had not changed.
                // The second clause is what makes a dump deterministic. Re-lighting is otherwise driven
                // by a clipmap having moved, which is a race against the field bake landing on its
                // worker: the same command produced two dumps on one run and one on the next, and when
                // it produced one the file held the UNSHADOWED load-time pass while looking exactly
                // like the shadowed one. That is how a probe came back reporting nothing at all.
                bool wantDump = !cardLightingDumped
                    && RenderCVars.SurfaceCacheDump.Value is { Length: > 0 };

                if ((rebuiltLevels > 0 || wantDump) && cardLighting is { } litCards)
                {
                    litCards.Light(
                        sun is { IsBelowHorizon: false } shadowSun ? shadowSun.Direction : default,
                        sun?.Colour ?? default,
                        sun?.Ambient ?? new Vector3f(0.27f, 0.29f, 0.33f),
                        liveField,
                        RenderCVars.SurfaceCacheProbe.Value);

                    // The bounce, once the direct term it gathers from is in place. Only when no probe
                    // is running: a probe writes a diagnostic into the direct atlas rather than light,
                    // and gathering from that would report on the diagnostic.
                    //
                    // The frame index converges the first relight in one shot and accumulates every one
                    // after, so a shadow that comes in as a clipmap moves settles rather than popping. A
                    // dump forces the deterministic first pass, so it is never blended.
                    if (RenderCVars.SurfaceCacheProbe.Value == 0)
                    {
                        litCards.Bounce(liveField, temporalIndex: wantDump ? 0 : cardBounceFrame);
                        cardBounceFrame++;
                    }

                    // Dumped after the SHADOWED pass rather than the one at load, which runs before
                    // there is any field to trace against and would show the unshadowed answer.
                    if (!cardLightingDumped
                        && RenderCVars.SurfaceCacheDump.Value is { Length: > 0 } shadowInto)
                    {
                        cardLightingDumped = true;
                        DumpCardLighting(
                            device, litCards, shadowInto, logger, RenderCVars.SurfaceCacheProbe.Value);
                    }
                }
            }

            device.SetDebugView(RenderCVars.Debug.Value);

            // The derived detail is a deferred-path feature: it changes the normal and the roughness
            // written into the G-buffer, and without one there is nothing to write them into.
            device.SetMaterialDetail(
                RenderCVars.OffscreenScene.Value && RenderCVars.Deferred.Value
                    ? RenderCVars.MaterialDetail.Value
                    : 0f);
            device.SetReflections(
                RenderCVars.OffscreenScene.Value && RenderCVars.Deferred.Value
                    ? new ReflectionSettings(
                        RenderCVars.Reflections.Value,
                        RenderCVars.ReflectionIntensity.Value,
                        RenderCVars.ReflectionMaxRoughness.Value)
                    : ReflectionSettings.Off);
            // Eased rather than linear. A linear ramp starts and stops abruptly, and the eye reads the
            // stop as a second, smaller event -- which is the thing this exists to remove.
            float probeFade = probeArrivedAt < 0
                ? 1f
                : Math.Clamp((loop.TimeSource.Milliseconds - probeArrivedAt) / ProbeFadeMilliseconds, 0f, 1f);
            probeFade = probeFade * probeFade * (3f - (2f * probeFade));

            device.SetIrradianceVolumeSettings(
                probeField is not null && RenderCVars.GlobalIllumination.Value
                    ? new IrradianceVolumeSettings(
                        true,
                        RenderCVars.GlobalIlluminationIntensity.Value,
                        light.Ambient,
                        probeFade)
                    : IrradianceVolumeSettings.Off);

            // Ambient occlusion needs the G-buffer's normals and depth, so it is offered only when
            // there is a G-buffer; without one the lighting has no ambient term of its own to shade.
            device.SetAmbientOcclusion(
                RenderCVars.OffscreenScene.Value && RenderCVars.Deferred.Value
                    ? new AmbientOcclusionSettings(
                        RenderCVars.AmbientOcclusion.Value,
                        RenderCVars.AmbientOcclusionRadius.Value,
                        RenderCVars.AmbientOcclusionIntensity.Value,
                        RenderCVars.ScreenSpaceGi.Value,
                        RenderCVars.ScreenSpaceGiIntensity.Value)
                    : AmbientOcclusionSettings.Off);

            // Reprojection compares two frames, and the jitter is precisely what differs between them,
            // so the matrix handed over is the one without it.
            device.SetTemporalAntiAliasing(
                new TemporalAaSettings(
                    wantsTemporal,
                    RenderCVars.TemporalFeedback.Value,
                    jitterX,
                    jitterY,
                    RenderCVars.TemporalSharpening.Value),
                unjitteredProjection,
                new Vector3f((float)eye.Position.X, (float)eye.Position.Y, (float)eye.Position.Z),
                eye.Orientation.Transposed());

            device.SetVolumetricFog(
                medium,
                new Vector3f((float)eye.Position.X, (float)eye.Position.Y, (float)eye.Position.Z),
                wantsFog ? fogPresets.VolumesFor(options.StartMission) : ReadOnlySpan<LocalFogVolume>.Empty);

            // Filled BEFORE the scene capture opens: a cascade renders into its own framebuffer, and
            // beginning a capture first would leave the shell holding two render targets at once.
            if (cascadeCount > 0 && shadowPass is not null)
            {
                shadowPass.Render(cascadeStorage, cascadeCount, eye, scene, world, soldiers);
            }

            bool captured = RenderCVars.OffscreenScene.Value && device.BeginSceneCapture(sceneTarget);
            if (captured)
            {
                // The scheduler cleared the back buffer; the target it did not know about needs it too.
                device.Clear(scheduler.ClearColour);

                device.SetLighting(new SceneLighting(
                    RenderCVars.Lighting.Value,
                    viewSunDirection,
                    light.Colour,
                    light.Ambient,
                    RenderCVars.LightScale.Value));
                device.BeginPass(ScenePass.Geometry);

                // The sky is geometry as far as the G-buffer is concerned, so without this the dynamic
                // model would multiply the dome by an ambient term and the horizon would go dark. It
                // is not lit; it IS light.
                device.SetMaterialOverride(new SurfaceMaterial(Emissive: 1f));
            }

            sky?.Draw(eye, projection, loop.TimeSource.Milliseconds / 1000f);

            if (captured)
            {
                device.SetMaterialOverride(SurfaceMaterial.Default);
            }
            terrain?.Draw(
                eye,
                projection,
                verticalFieldOfView,
                aspectRatio,

                // -viewdist still wins when it is given, so an existing diagnostic command line keeps
                // meaning what it did; otherwise the setting decides.
                options.TerrainViewDistance > 0 ? options.TerrainViewDistance : RenderCVars.TerrainDetail.Value);
            world.Draw(scene, eye, projection);

            // Soldiers draw under the same world projection: 0x4D9620 leaves +212 at 1, so 0x49F700
            // never swaps in the view-model block for them (player.md section 15.3).
            soldiers?.Draw(eye, projection);

            // The player's own body, once it has fallen -- drawn only under the death camera, so it never
            // appears in front of the first-person view during normal play.
            soldiers?.DrawPlayerBody(eye, projection);

            // The marks rounds have left, over the world and under the first-person rig.
            if (marks is not null && player is not null)
            {
                marks.Draw(player.Marks, eye, projection);
            }

            // L6/L9: the live screen probe gather. Traced here, while the G-buffer the geometry pass just
            // filled is still live and BEFORE the pass closes -- because closing it runs the deferred
            // lighting, which reads the atlas as its diffuse fill. So the gather has to have written it
            // first. Only without a card probe, which fills the cache atlas with a diagnostic rather than
            // light. It replaces the ambient inside the shade rather than compositing over the frame, so
            // there is no ambient to zero and nothing runs after the lighting.
            const int giStep = 8;
            const int giMaxAccum = 32;

            // The screen trace tier: how far its rays reach across the frame and their step cap, plus the
            // surface thickness its hit test assumes. Metres, save the step count.
            const int giTraceSteps = 32;
            const float giTraceDistance = 24f;
            const float giTraceThickness = 0.5f;
            float giStrength = RenderCVars.ScreenGather.Value;
            bool giActive = captured
                && giStrength > 0f
                && sdfClipmaps is not null
                && cardLighting is not null
                && RenderCVars.SurfaceCacheProbe.Value == 0;

            if (giActive && sdfClipmaps is { } liveGiField && cardLighting is { } liveGiCards)
            {
                int wantWidth = (window.Width + giStep - 1) / giStep;
                int wantHeight = (window.Height + giStep - 1) / giStep;

                if (giIndirectWidth != wantWidth || giIndirectHeight != wantHeight)
                {
                    // A resize drops the history: the atlases are a different shape, so nothing in them
                    // reprojects onto the new frame.
                    for (int i = 0; i < 2; i++)
                    {
                        if (giAccum[i].IsValid)
                        {
                            device.DestroyTexture(giAccum[i]);
                        }

                        if (giPosition[i].IsValid)
                        {
                            device.DestroyTexture(giPosition[i]);
                        }

                        giAccum[i] = device.CreateStorageTexture(
                            wantWidth, wantHeight, VolumeFormat.Rgba16F, "cards.screen.accum");
                        giPosition[i] = device.CreateStorageTexture(
                            wantWidth, wantHeight, VolumeFormat.Rgba16F, "cards.screen.accum.pos");
                    }

                    // The spatial denoise's output, regenerated each frame from the accumulation, so a
                    // single buffer rather than a ping-pong pair.
                    if (giFiltered.IsValid)
                    {
                        device.DestroyTexture(giFiltered);
                    }

                    giFiltered = device.CreateStorageTexture(
                        wantWidth, wantHeight, VolumeFormat.Rgba16F, "cards.screen.filtered");

                    giIndirectWidth = wantWidth;
                    giIndirectHeight = wantHeight;
                    giHasHistory = false;
                }

                Vector3f giCameraToCache = new(
                    (float)(eye.Position.X - liveGiCards.Origin.X),
                    (float)(eye.Position.Y - liveGiCards.Origin.Y),
                    (float)(eye.Position.Z - liveGiCards.Origin.Z));

                int write = giFrame & 1;
                int read = 1 - write;

                liveGiCards.GatherScreenTemporal(
                    liveGiField,
                    device.SceneDepth,
                    device.SceneNormal,
                    DepthUnprojection.From(unjitteredProjection, 0.1f, farPlane),
                    eye.Orientation.Transposed(),
                    giCameraToCache,
                    window.Width,
                    window.Height,
                    giStep,
                    giAccum[write],
                    giPosition[write],
                    giAccum[read],
                    giPosition[read],
                    giPrevWorldToView,
                    giPrevCameraToCache,
                    giFrame,
                    giMaxAccum,
                    giHasHistory,

                    // The screen trace tier (L5b): the previous frame's lit scene, read at on-screen hits
                    // before the ray falls through to the surface cache. device.SceneColour holds last
                    // frame's linear scene here -- this runs before the current frame is lit.
                    device.SceneColour,
                    RenderCVars.ScreenGatherTrace.Value ? giTraceSteps : 0,
                    giTraceDistance,
                    giTraceThickness,

                    // The sky-light fill (L9): a ray that escapes returns the mission's sky, the same sky
                    // the surface cache lights its cards with, so the indirect that replaces the ambient
                    // carries sky light -- with its occlusion, since a ray toward geometry hits it instead.
                    sun?.Ambient ?? new Vector3f(0.27f, 0.29f, 0.33f));

                // The spatial denoise (L6c), if on: smooths the accumulation into a separate atlas that
                // the deferred fill reads, leaving the history the next frame reprojects onto unfiltered.
                int giFilterRadius = RenderCVars.ScreenGatherFilter.Value;
                TextureHandle giRead = giAccum[write];

                if (giFilterRadius > 0)
                {
                    liveGiCards.FilterScreen(
                        giAccum[write], giPosition[write], giFiltered, giIndirectWidth, giIndirectHeight, giFilterRadius);
                    giRead = giFiltered;
                }

                // Recorded for the deferred lighting to fold in when the pass closes, just below.
                device.SetScreenIndirect(giRead, giStrength);

                // What this frame becomes to the next: its camera, so the next reprojection can find where
                // each surface sat, and the flag that says there is now a frame to reproject onto. The
                // world-to-view rotation is the orientation itself -- the inverse of the view-to-world the
                // gather traced with.
                giPrevWorldToView = eye.Orientation;
                giPrevCameraToCache = giCameraToCache;
                giHasHistory = true;
                giFrame++;
            }
            else
            {
                // The gather did not run this frame, so next frame has nothing coherent to reproject onto.
                giHasHistory = false;
            }

            // The view model and anything translucent belong to the forward pass: it is drawn with its
            // own narrower projection, so its depth must not end up in the buffer the fog and the
            // reflections read.
            if (captured)
            {
                device.EndPass();
                device.BeginPass(ScenePass.Forward);
            }

            // The first-person view goes last, and in 0x4644A0's own order: pose the rig, draw the arms,
            // then ask the human where the weapon is and draw that. Only in player mode, because the
            // original draws it out of the human's own render and the free camera is not a human -- and
            // NOT once the player is dead, when the death camera is watching the body from the outside
            // and a pair of arms hanging in front of it would be exactly wrong.
            if (playerMode && !(playerDead && deathCamera.IsActive) && player is { IsOnLadder: false, View: { } rig })
            {
                // Only an advancing clip changes the skin, so the re-skin is keyed to the tick rather
                // than the frame.
                if (player.PoseChanged)
                {
                    arms?.Invalidate();
                    player.AcknowledgePose();
                }

                // The view model gets its OWN projection: 0x49F700 saves the current view block,
                // rebuilds [16..19] from dword_B81700 -- HumanCamera+220, filled from HumanView+488 =
                // tan(30 degrees) -- and restores it afterwards, for any bone model with no LOD chain,
                // which the HumanView is (0x464660 leaves +212 at zero). The rebuild is sub_497D70's
                // own algebra, the SAME build every camera pass uses: [16] is the plain half-angle
                // tangent, [18] = halfWidth / [16] and [19] = Aspect43 * [18] are the pixel scales the
                // projection divides through (sub_50FE60 publishes them as the per-vertex xScale and
                // yScale). So against the world's tan(45), the arms and the weapon really are drawn
                // tan45/tan30 = 1.732x magnified about the screen centre -- which moves a hand held
                // below centre DOWN and out of frame. That is what holds the glove below the bottom
                // edge in retail. (An earlier pass refuted this as "larger is the wrong direction";
                // larger is exactly what hides it.) section 14.7.
                //
                // The vertical tangent is 3/4 of the horizontal one and does NOT follow the window's
                // aspect: [19] = Aspect43() * [18] works out to yScale = halfHeight / (0.75 * tan) at
                // EVERY aspect, so retail's vertical framing -- the glove at the bottom edge -- is
                // aspect-invariant. Dividing the tangent by the aspect instead (the world's widescreen
                // rule) shrinks the view model's vertical reach at 16:9 and pushes the hand off the
                // bottom of the frame. At 4:3 the two rules coincide, tanY = 0.433 and tanX = tan(30).
                float viewModelFieldOfView = 2f * MathF.Atan(0.75f * rig.ViewModelTanX);
                Matrix4f viewModelProjection = Matrix4f.CreatePerspective(
                    viewModelFieldOfView,
                    aspectRatio,
                    near: 0.1f,
                    far: farPlane);

                // Between-tick smoothing for everything the rig steps at 30 Hz: its origin (the eye
                // the body carried plus the clip's hold offset), its skin, and the weapon bone. The
                // LOOK is already per frame; without this the arms and the knife stepped at 30 Hz
                // against a camera that now glides -- which read as jitter -- and skipped several
                // poses per drawn frame whenever ticks ran back to back, which read as the animation
                // racing.
                Vector3d? rigOriginUnits = null;
                Matrix3f? rigOrientation = null;
                BoneAttachment? weaponAttachment = player.HeldWeapon;
                float previousPoseShare = 0f;
                if (smoothPresentation && player.HasViewTickPair)
                {
                    (Vector3d smoothedOrigin, Matrix3f smoothedOrientation, BoneAttachment? smoothedWeapon) =
                        player.SmoothedRig(tickBlend);
                    rigOriginUnits = smoothedOrigin;
                    rigOrientation = smoothedOrientation;
                    weaponAttachment = smoothedWeapon ?? weaponAttachment;
                    previousPoseShare = 1f - tickBlend;
                }

                arms?.Draw(rig, eye, viewModelProjection, rigOriginUnits, previousPoseShare, rigOrientation);

                // Cycling weapons changes the model as well as the animations, so the renderer has to
                // follow whatever the cycle put in hand.
                if (loadedMissionForWeapon is { } missionForWeapon)
                {
                    FollowHeldWeapon(player, heldWeapon, missionForWeapon);
                }

                if (weaponAttachment is { } attachment && heldWeapon is { IsReady: true })
                {
                    // The weapon in hand is the one object on screen at all times, so it is the one
                    // worth giving a material to first. Steel: fully metallic, and smooth enough for
                    // the blade to catch a highlight as it moves.
                    if (captured)
                    {
                        device.SetMaterialOverride(new SurfaceMaterial(Roughness: 0.28f, Metallic: 1f));
                    }

                    heldWeapon.Draw(attachment, eye, viewModelProjection);

                    if (captured)
                    {
                        device.SetMaterialOverride(SurfaceMaterial.Default);
                    }
                }

                // The HUD goes over everything, in 2D. StatusScreen's render sends one message to the
                // weapon in hand and the weapon draws its own readout -- so what appears is the roster
                // record's own SightDisplayType and AmmoDisplayType, not a choice made here.
                if (hud is { IsReady: true } && player.Weapons is { } carried)
                {
                    device.Set2DProjection(window.Width, window.Height);
                    batch.Begin();
                    hud.Draw(
                        batch,
                        carried.InHand?.Weapon,
                        carried.InHand?.Gun.RoundsInClip ?? 0,
                        carried.InHand is { } inHand ? inHand.Gun.Reserve(carried.Ammunition) : 0,
                        window.Width,
                        window.Height,
                        UiScale.Resolve(options.UiScale, window.Width, window.Height));
                    batch.End();
                }

                // The interaction prompt, when something is in reach — the original's "use" hint.
                if (hud is { IsReady: true } && player.ShowInteractPrompt)
                {
                    device.Set2DProjection(window.Width, window.Height);
                    batch.Begin();
                    hud.DrawPrompt(
                        batch,
                        player.InteractPromptText,
                        window.Width,
                        window.Height,
                        UiScale.Resolve(options.UiScale, window.Width, window.Height));
                    batch.End();
                }
            }

            // L6a slice 2: gather screen-space indirect from the surface cache and dump it, once, while
            // the G-buffer is still live inside the capture. Verifies the cross-layer bridge and the
            // depth-to-world reconstruction before the gather feeds the frame. Only when no card probe is
            // running, since that fills the cache atlas with a diagnostic rather than light.
            if (captured
                && !screenGiDumped
                && RenderCVars.SurfaceCacheDump.Value is { Length: > 0 } giInto
                && sdfClipmaps is { } giField
                && cardLighting is { } giCards
                && RenderCVars.SurfaceCacheProbe.Value == 0)
            {
                screenGiDumped = true;
                DumpScreenGather(
                    device, giCards, giField, eye, unjitteredProjection, farPlane,
                    window.Width, window.Height, giInto, logger);
            }

            // Everything 2D belongs AFTER the resolve, on the back buffer: the HUD, the damage wash,
            // the menus and the console are authored in display colours and must never be tone mapped.
            // Unconditional, and outside the player-mode block on purpose -- a free-camera frame draws
            // no view model, and leaving the capture open would strand it until the next frame threw.
            if (captured)
            {
                device.EndPass();
                device.EndSceneCapture();
                device.ResolveScene(new PostProcessSettings(
                    RenderCVars.ToneMap.Value,
                    RenderCVars.Exposure.Value,
                    RenderCVars.Bloom.Value,
                    RenderCVars.BloomThreshold.Value,
                    RenderCVars.WhitePoint.Value,
                    SceneIsDisplayEncoded: true,
                    Contrast: RenderCVars.Contrast.Value,
                    Saturation: RenderCVars.Saturation.Value,
                    Vignette: RenderCVars.Vignette.Value,
                    Grain: RenderCVars.Grain.Value,
                    ChromaticAberration: RenderCVars.ChromaticAberration.Value,
                    Sharpen: RenderCVars.Sharpen.Value,
                    FocalDistance: RenderCVars.FocalDistance.Value,
                    FocalRange: RenderCVars.FocalRange.Value,
                    FocalBlur: RenderCVars.FocalBlur.Value,
                    MotionBlur: RenderCVars.MotionBlur.Value,
                    CameraTaps: RenderCVars.CameraTaps.Value,
                    SpatialAa: RenderCVars.SpatialAa.Value,
                    SpatialAaThreshold: RenderCVars.SpatialAaThreshold.Value,
                    ColourLookup: RenderCVars.ColourLookup.Value,
                    AutoExposure: RenderCVars.AutoExposure.Value));
                captured = false;
            }

            // The damage wash goes over everything, in its own 2D pass: a screen-filling red quad whose
            // four vertex alphas are the four corners sub_411300 fades (HumanPlayer_DrawDamageFlash,
            // 0x4bb770). Outside the player-mode block on purpose -- it has to survive the player's death,
            // when the arms and the HUD are gone but the last thing the screen showed is still red.
            if (damageFlash.IsActive)
            {
                float flashWidth = window.Width;
                float flashHeight = window.Height;
                static Bgra32 Red(float alpha) =>
                    new(0, 0, 255, (byte)Math.Clamp((int)(alpha * 255f), 0, 255));

                device.Set2DProjection(window.Width, window.Height);
                batch.Begin();
                batch.DrawQuad(
                    TextureHandle.None,
                    new Vertex2D(0f, 0f, 0f, 0f, Red(damageFlash.TopLeft)),
                    new Vertex2D(flashWidth, 0f, 1f, 0f, Red(damageFlash.TopRight)),
                    new Vertex2D(flashWidth, flashHeight, 1f, 1f, Red(damageFlash.BottomRight)),
                    new Vertex2D(0f, flashHeight, 0f, 1f, Red(damageFlash.BottomLeft)));
                batch.End();
            }

            // The F12 debug overlay and crosshair. Works in both the walking player and the free (flying)
            // camera: the state lines come from whichever is active, the crosshair readout and the reticle
            // are cast from the eye that is actually drawing.
            if (showDebug && hud is { IsReady: true })
            {
                static string I(FormattableString s) => FormattableString.Invariant(s);

                Vector3d originU = new(
                    eye.Position.X * WorldScene.UnitsPerMetre,
                    eye.Position.Y * WorldScene.UnitsPerMetre,
                    eye.Position.Z * WorldScene.UnitsPerMetre);
                Matrix3f orient = eye.Orientation;
                Vector3d fwd = new(orient.M31, orient.M32, orient.M33);
                const double reachU = 300.0 * WorldScene.UnitsPerMetre;
                Vector3d endU = new(
                    originU.X + (fwd.X * reachU), originU.Y + (fwd.Y * reachU), originU.Z + (fwd.Z * reachU));

                string target = "target: (nothing / terrain / sky)";
                string? detect = null;
                double bestT = double.MaxValue;
                if (worldSolids is not null
                    && worldSolids.TryIdentify(originU, endU, out string objLabel, out WorldRayHit objHit))
                {
                    bestT = objHit.Distance;
                    target = I($"target: OBJECT {objLabel} @ {objHit.Distance / WorldScene.UnitsPerMetre:F1}m ({objHit.Position.X:F0}, {objHit.Position.Y:F0}, {objHit.Position.Z:F0}) mat={objHit.Material}");
                }

                if (soldiers is not null
                    && soldiers.TryPick(originU, fwd, reachU, out string botName, out double botT, out Vector3d feetU, out int botZone)
                    && botT < bestT)
                {
                    // The floor the game's collision puts under this soldier's feet — so a sunk guard shows
                    // its feet Z below the floor Z, and a correct one shows them level.
                    string floor = "?";
                    if (worldSolids is not null)
                    {
                        Vector3d fTop = new(feetU.X, feetU.Y, feetU.Z + 2048.0);
                        Vector3d fBot = new(feetU.X, feetU.Y, feetU.Z - (10.0 * WorldScene.UnitsPerMetre));
                        if (worldSolids.TryIdentify(fTop, fBot, out string floorLbl, out WorldRayHit floorHit))
                        {
                            floor = I($"{floorHit.Position.Z:F0} ({floorLbl} mat{floorHit.Material}) Δ={(feetU.Z - floorHit.Position.Z) / WorldScene.UnitsPerMetre:F2}m");
                        }
                    }

                    target = I($"target: SOLDIER {botName} zone={HumanDamage.ZoneName(botZone)} feetZ={feetU.Z:F0} floorZ={floor}");
                }

                // Whatever soldier the crosshair is on, ask its brain which detection gate is (not) passing
                // against the point the guards actually test -- the player's own eye in engine units, the same
                // point soldiers.Tick is fed.
                Vector3d watchedEye = playerMode && player is not null
                    ? player.Body.Eye
                    : camera.Position;
                if (soldiers is not null
                    && soldiers.TryDetectionInfo(originU, fwd, reachU, watchedEye, out string detectInfo))
                {
                    detect = detectInfo;
                }

                List<string> lines;
                if (playerMode && player is not null)
                {
                    Vector3d pu = player.PositionUnits;
                    Vector3d pm = player.Position;
                    lines =
                    [
                        "mode: WALK",
                        I($"pos units ({pu.X:F0}, {pu.Y:F0}, {pu.Z:F0})"),
                        I($"pos m     ({pm.X:F2}, {pm.Y:F2}, {pm.Z:F2})"),
                        I($"yaw {player.Yaw * 180f / MathF.PI:F1}  pitch {player.Pitch * 180f / MathF.PI:F1}"),
                        I($"grounded {player.IsGrounded}  crouch {player.IsCrouched}  wall {player.TouchedWall}"),
                        target,
                    ];
                }
                else
                {
                    Vector3d cm = camera.Position;
                    lines =
                    [
                        "mode: FREE CAM",
                        I($"pos units ({cm.X * WorldScene.UnitsPerMetre:F0}, {cm.Y * WorldScene.UnitsPerMetre:F0}, {cm.Z * WorldScene.UnitsPerMetre:F0})"),
                        I($"pos m     ({cm.X:F2}, {cm.Y:F2}, {cm.Z:F2})"),
                        I($"yaw {camera.Yaw * 180f / MathF.PI:F1}  pitch {camera.Pitch * 180f / MathF.PI:F1}"),
                        target,
                    ];
                }

                if (detect is not null)
                {
                    lines.Add(detect);
                }

                device.Set2DProjection(window.Width, window.Height);
                batch.Begin();
                hud.DrawDebug(batch, lines, UiScale.Resolve(options.UiScale, window.Width, window.Height));

                // A centre reticle so the target readout can be aimed — the crosshair the free camera lacks.
                float cx = window.Width * 0.5f;
                float cy = window.Height * 0.5f;
                const float len = 9f;
                const float th = 1f;
                static Bgra32 White() => new(255, 255, 255, 220);
                void Bar(float x0, float y0, float x1, float y1) => batch.DrawQuad(
                    TextureHandle.None,
                    new Vertex2D(x0, y0, 0f, 0f, White()),
                    new Vertex2D(x1, y0, 1f, 0f, White()),
                    new Vertex2D(x1, y1, 1f, 1f, White()),
                    new Vertex2D(x0, y1, 0f, 1f, White()));
                Bar(cx - len, cy - th, cx + len, cy + th);
                Bar(cx - th, cy - len, cx + th, cy + len);

                batch.End();
            }

            // r_perflog, its own overlay rather than a section of F12's: it answers a different question
            // and is read while the game is being played rather than while it is being inspected. Top
            // right, so the two can be up together.
            if (RenderCVars.PerformanceLog.Value && hud is { IsReady: true })
            {
                static string I(FormattableString s) => FormattableString.Invariant(s);

                IReadOnlyList<RenderStageTiming> timings = device.StageTimings();
                List<string> perf = ["gpu ms   now   peak"];
                double total = 0.0;

                foreach (RenderStageTiming timing in timings)
                {
                    total += timing.Milliseconds;

                    // The budget beside the measurement, because a number without the number it was
                    // supposed to beat is a number nobody acts on.
                    string budget = RenderBudget.For(timing.Stage) is { } allowed
                        ? I($" /{allowed:0.0}{(timing.Milliseconds > allowed ? " OVER" : string.Empty)}")
                        : string.Empty;

                    perf.Add(I($"{timing.Stage,-12} {timing.Milliseconds,5:0.00} {timing.PeakMilliseconds,6:0.00}{budget}"));
                }

                perf.Add(timings.Count > 0
                    ? I($"{"gpu total",-12} {total,5:0.00}")
                    : "measuring...");

                device.Set2DProjection(window.Width, window.Height);
                batch.Begin();
                hud.DrawDebugTopRight(
                    batch,
                    perf,
                    window.Width - (8f * UiScale.Resolve(options.UiScale, window.Width, window.Height)),
                    8f * UiScale.Resolve(options.UiScale, window.Width, window.Height),
                    UiScale.Resolve(options.UiScale, window.Width, window.Height));
                batch.End();
            }

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

        // The cinematic shell draws over the resolved showcase scene, in 2D on the back buffer, before
        // the console (which goes over even the shell).
        if (menuMode && shell is not null)
        {
            device.Set2DProjection(window.Width, window.Height);
            var shellPainter = new UiPainter(batch, new LayoutContext(window.Width, window.Height));
            batch.Begin();
            shell.Draw(shellPainter);
            batch.End();
        }

        // The console goes over everything, including the menus, so it is the last thing drawn.
        if (console.IsOpen)
        {
            device.Set2DProjection(window.Width, window.Height);
            batch.Begin();
            console.Draw(batch, window.Width, window.Height, UiScale.Resolve(options.UiScale, window.Width, window.Height));
            batch.End();
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

            soldiers?.LogAiSummary();
            running = false;
        }
    }

    // The measurement, written down. The overlay answers "is it fast right now" for somebody watching
    // it; this is what a capture run leaves behind, and it is the only form the acceptance criterion
    // can actually be checked in -- nobody reads an overlay in a thirty-frame headless capture.
    if (RenderCVars.PerformanceLog.Value)
    {
        IReadOnlyList<RenderStageTiming> finalTimings = device.StageTimings();

        if (finalTimings.Count == 0)
        {
            logger.Information("Perf", "No stage timings were collected; the run was too short to resolve a query.");
        }
        else
        {
            double total = 0.0;
            int over = 0;

            foreach (RenderStageTiming timing in finalTimings)
            {
                total += timing.Milliseconds;
                double? budget = RenderBudget.For(timing.Stage);
                bool exceeded = budget is { } allowed && timing.Milliseconds > allowed;
                over += exceeded ? 1 : 0;

                string against = budget is { } allowance
                    ? FormattableString.Invariant($"  budget {allowance:0.0} ms{(exceeded ? "  OVER" : string.Empty)}")
                    : "  (unbudgeted)";

                logger.Information(
                    "Perf",
                    FormattableString.Invariant(
                        $"{timing.Stage,-16} {timing.Milliseconds,6:0.000} ms  peak {timing.PeakMilliseconds,6:0.000} ms{against}"));
            }

            logger.Information(
                "Perf",
                FormattableString.Invariant(
                    $"GPU total {total:0.000} ms of the {RenderBudget.FrameMilliseconds:0.0} ms a 60 Hz frame has; {over} stage(s) over budget."));
        }
    }

    // A bake still tracing at this point is tracing for a level nobody will see again. The worker is a
    // thread-pool thread and cannot hold the process open, so this is not about shutting down cleanly --
    // it is about not spending every core on it while the rest of the shutdown runs.
    probeBake?.Cancel();

    SaveConfiguration(gamePath, configuration, logger);
    console.SaveSettings();
    sky?.Dispose();
    terrain?.Dispose();
    world?.Dispose();
    soldiers?.Dispose();
    heldWeapon?.Dispose();
    hud?.Dispose();
    marks?.Dispose();
    renderer?.Dispose();
    missionWarmer?.Dispose();
    shellBodyFont?.Dispose();
    shellHeadingFont?.Dispose();
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

static Vector3d FindTaskPosition(GameTask root, int taskId)
{
    return FindPosition(root, taskId);

    static Vector3d FindPosition(GameTask task, int id)
    {
        if (task.Id == id)
        {
            if (task is DeclaredTask declared)
            {
                if (declared.TryGetValue("Graph position", out object? graphPos) && graphPos is Vector3d gp)
                {
                    return gp;
                }

                if (declared.TryGetValue("Position", out object? pos) && pos is Vector3d p)
                {
                    return p;
                }
            }

            return default;
        }

        foreach (GameTask child in task.Children)
        {
            Vector3d found = FindPosition(child, id);
            if (found != default)
            {
                return found;
            }
        }

        return default;
    }
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
