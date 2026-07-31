using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Formats.Ai;
using OpenIGI.Core.Numerics;
using OpenIGI.Desktop;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
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
    GameLoop loop = new(host: menuHost, renderer: scheduler, logger: logger);

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
    // on CutScene_1200.nTick, cutting its patrol handling short.
    loop.Types.Register<OpenIGI.Game.World.CutSceneTask>(OpenIGI.Game.World.CutSceneTask.TypeName);

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
    // soldiers' 100 -- humanplayer.qvm overrides the executable's default (weapons.md 11).
    HumanHealth playerHealth = new(HumanHitZoneTable.Player);
    bool playerDead = false;
    Random shotRandom = new(20000101);
    int reportedHumanHits = 0;
    WorldScene? scene = null;
    WorldRenderer? world = null;
    TerrainRenderer? terrain = null;
    SkyRenderer? sky = null;
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
    bool playerMode = false;

    // The camera's projection, in one place: the terrain traversal needs the same field of view the
    // projection matrix is built from, or it would cull against a frustum the picture does not have.
    // The far plane is shared with the sky, which sizes its dome from it.
    //
    // The field of view is the original's, not a chosen one. HumanView's constructor (0x4643A0) sets
    // its half-tangent to tan(45 degrees) = 1.0, the camera copies it to HumanCamera+96 and submits it
    // to the view (0x4D9870) -- and it is HORIZONTAL, which the terrain traversal's own 115/sqrt(tanX)
    // view distance independently confirms. So the horizontal field of view is 90 degrees and the
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

        scene = WorldScene.Build(loaded, levelRoot, magic, logger);
        world = new WorldRenderer(device, resources, logger);

        // The walls. Built here because two consumers want it: the player's collision, and the
        // soldiers' ground probe - a guard's declared Position is only where it starts, and without
        // the settle the retail integrator performs, floor-authored spawns bury the body to the waist.
        ObjectCollisionIndex solids = ObjectCollisionIndex.Build(scene.Placements);

        // The soldiers the level places. Each names its own model, skeleton and stand animation
        // (player.md section 15); the sets they index were loaded when the packs opened. They are not
        // WorldScene placements - a human task is a typed task, and the original draws it through the
        // bone path, not the static one.
        soldiers?.Dispose();
        soldiers = new SoldierRenderer(device, resources, logger);
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
                soldiers.Animate(
                    graphs, solids.Caster(), resources, loop.Natives, levelRoot, aiConfig, options.Difficulty);

                // A guard's round: it raised the trigger, the weapon's cadence let one out, and this is
                // where it becomes a cast. The AI's hit probability decides whether the round is on
                // target -- the original hands that same figure to the gun with every shot (ai.md 11.6),
                // so a miss is a round that went somewhere else rather than a round that never existed.
                soldiers.SetRoundHandler((from, at, probability, weapon) =>
                {
                    guardRounds++;
                    if (player is null || shotRandom.NextDouble() > probability)
                    {
                        return;
                    }

                    Vector3d to = new(
                        from.X + ((at.X - from.X) * 4.0),
                        from.Y + ((at.Y - from.Y) * 4.0),
                        from.Z + ((at.Z - from.Z) * 4.0));

                    Vector3d feet = player.Body.Position;
                    Vector3f facing = new(
                        -MathF.Sin(player.Body.Yaw), MathF.Cos(player.Body.Yaw), 0f);

                    if (!HumanHitTest.TryCast(from, to, feet, facing, pose: null, out HumanHit hit))
                    {
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
                    logger.Information(
                        "Weapon",
                        $"A guard's {weapon.ScriptId} hit the player in zone {hit.Zone} "
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

                        // NOT played here. Those ids index the player's BODY set, and the only rig the
                        // first-person view has is the arms on skeleton 5 -- playing a body clip on it
                        // is the same mistake that made guards throw themselves on the floor. A real
                        // death needs the player's body rig drawn and the camera handed to it, and
                        // neither exists yet. Reported so it is visible; not faked.
                        logger.Information(
                            "Weapon",
                            $"The player is DEAD after {guardHits} hit(s) from {guardRounds} round(s), "
                                + $"struck in zone {hit.Zone}. The fall is animation {fall} with one of "
                                + $"{HumanPlayerDeath.SoundVariants} '{HumanPlayerDeath.SoundPrefix}' "
                                + "sounds -- NOT PLAYED: the player has no body rig and the death "
                                + "camera is not built.");
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

            // Walking is the animation's own root motion, so without a set the player can stand and
            // fall but not walk. 000.iff is the set the player's skeleton indexes.
            try
            {
                string animations = Path.Combine(gamePath, "common", "anims", "000.iff");
                if (File.Exists(animations))
                {
                    candidate.UseAnimations(
                        OpenIGI.Formats.Animation.BoneAnimationSet.Read(File.ReadAllBytes(animations)));
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
            // The player's eye is what the guards' view cones are tested against -- and in the
            // fly-through it is the CAMERA's, not the body's. The body stops being stepped the moment
            // you leave walking mode, so feeding its eye meant every guard in the level was watching an
            // immobile figure at the spawn point while you flew past them: they never saw you because
            // there was nothing of you to see. A shell affordance, not engine behaviour.
            Vector3d watched = playerMode && player is not null
                ? player.Body.Eye
                : camera?.Position ?? player?.Body.Eye ?? default;

            soldiers?.Tick(watched);
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
            Matrix4f projection = Matrix4f.CreatePerspective(
                verticalFieldOfView,
                aspectRatio,
                near: 0.1f,
                far: farPlane);

            // The sky is behind everything and writes no depth, so it goes first; then terrain, which
            // is opaque and covers most of the screen, so drawing it before the objects gives the
            // depth test the most to reject.
            Camera3D eye = playerMode && player is not null ? player.Camera : camera.Camera;

            sky?.Draw(eye, projection, loop.TimeSource.Milliseconds / 1000f);
            terrain?.Draw(eye, projection, verticalFieldOfView, aspectRatio, options.TerrainViewDistance);
            world.Draw(scene, eye, projection);

            // Soldiers draw under the same world projection: 0x4D9620 leaves +212 at 1, so 0x49F700
            // never swaps in the view-model block for them (player.md section 15.3).
            soldiers?.Draw(eye, projection);

            // The marks rounds have left, over the world and under the first-person rig.
            if (marks is not null && player is not null)
            {
                marks.Draw(player.Marks, eye, projection);
            }

            // The first-person view goes last, and in 0x4644A0's own order: pose the rig, draw the arms,
            // then ask the human where the weapon is and draw that. Only in player mode, because the
            // original draws it out of the human's own render and the free camera is not a human.
            if (playerMode && player is { View: { } rig })
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

                arms?.Draw(rig, eye, viewModelProjection);

                // Cycling weapons changes the model as well as the animations, so the renderer has to
                // follow whatever the cycle put in hand.
                if (loadedMissionForWeapon is { } missionForWeapon)
                {
                    FollowHeldWeapon(player, heldWeapon, missionForWeapon);
                }

                if (player.HeldWeapon is { } attachment && heldWeapon is { IsReady: true })
                {
                    heldWeapon.Draw(attachment, eye, viewModelProjection);
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

    SaveConfiguration(gamePath, configuration, logger);
    sky?.Dispose();
    terrain?.Dispose();
    world?.Dispose();
    soldiers?.Dispose();
    heldWeapon?.Dispose();
    hud?.Dispose();
    marks?.Dispose();
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
