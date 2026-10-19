using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Time;
using OpenIGI.Frontend.Saves;
using OpenIGI.Frontend;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Host.Configuration;
using OpenIGI.Host.Lighting;
using OpenIGI.Host.Menu;
using OpenIGI.Host.Services;
using OpenIGI.Presentation;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Presentation.Text;
using OpenIGI.Rendering;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Host;

/// <summary>What <see cref="Frames"/> builds before its first frame, in the order it builds it.</summary>
public sealed partial class GameHost
{
    /// <summary>Shows the log in the console, registers the commands, reads the settings and binds the default keys.</summary>
    private void SetUpConsole(FrameLoopState state)
    {
        if (_logger is LogRouter router)
        {
            state.Console.AttachLog(router);
        }

        state.Console.QuitRequested += (_, _) => state.Running = false;

        // The console reaches the system clipboard through the window, which is what owns the handle,
        // rather than taking a platform reference of its own.
        state.Console.View.ReadClipboard = () => _window.ClipboardText;
        state.Console.View.WriteClipboard = text => _window.ClipboardText = text;

        // Attached once rather than per mission: the inspector asks for a fresh snapshot every frame
        // it draws, and the implementation already answers with nothing when no world is loaded.
        state.Console.View.AttachWorld(_inspector);

        // Before Start, so a +lamp_switch on the command line and a settings.cfg that binds one both find
        // the command already there.
        RegisterLampCommands(state.Console.System);
        RegisterTimeOfDayCommands(state.Console.System);
        _flashlight.RegisterCommands(state.Console.System);
        RegisterTitlePlayerCommands(state.Console.System);
        _effects.Register(state.Console.System);
        RegisterCameraCommands(state.Console.System);
        RegisterRendererSwitchCommand(state.Console.System);
        _contentLayers.Register(state.Console.System);
        _memory.Register(state.Console.System);

        state.Console.Start(
            _fileSystem,
            _options.StartupCommands,
            useSavedSettings: !_options.Lockstep,
            _options.FirstRunPreset,
            _options.LockedPreset);
        // The scan LoadTitleResources ran: taken off the field because the scan now happens wherever the
        // resources are loaded, and that is a method a title switch calls too.
        _contentLayers.Report(_overrideScan);

        WarnAboutSettingsTheBackendIgnores();

        // The keys the game binds for itself, each only where the player has not bound it to something
        // else, so a binding they saved is never stolen. What they are and the rule about what may be in
        // the table is in DefaultBinds.
        foreach ((string key, string statement) in Configuration.DefaultBinds.All)
        {
            if (!state.Console.System.Binds.ContainsKey(key))
            {
                state.Console.System.SetBind(key, statement);
            }
        }

        // -novsync is the older spelling of r_vsync 0. It forces the setting rather than shadowing it, so
        // the console and the flag can never report different things.
        if (!_options.VSync)
        {
            RenderCVars.VSync.Value = false;
        }
    }

    /// <summary>Puts the device's settings in force and keeps them so as they change.</summary>
    private void BindDeviceVariables(FrameLoopState state)
    {
        // Set before anything uploads a texture: the sampler a texture is created with is the one it
        // keeps, so a level loaded before this would keep the filtering it started with.
        _device.SetTextureQuality(RenderCVars.Anisotropy.Value);
        WhenChanged(RenderCVars.Anisotropy, () => _device.SetTextureQuality(RenderCVars.Anisotropy.Value));

        _device.VSyncEnabled = RenderCVars.VSync.Value;
        WhenChanged(RenderCVars.VSync, () => _device.VSyncEnabled = RenderCVars.VSync.Value);

        WhenChanged(RenderCVars.WeatherEnabled, ApplyWeatherSettings);
        WhenChanged(RenderCVars.WeatherStyle, ApplyWeatherSettings);
        WhenChanged(RenderCVars.WeatherSpeed, ApplyWeatherSettings);
        WhenChanged(RenderCVars.WeatherForce, ApplyWeatherSettings);
        WhenChanged(RenderCVars.Storm, ApplyWeatherSettings);
        WhenChanged(RenderCVars.StormIntensity, ApplyWeatherSettings);

        // Timing is pushed on change rather than read per frame, because switching it off is what discards
        // the history -- the peaks after switching it back on should belong to the run being watched.
        _device.SetStageTiming(RenderCVars.PerformanceLog.Value);
        WhenChanged(RenderCVars.PerformanceLog, () => _device.SetStageTiming(RenderCVars.PerformanceLog.Value));

        _flowHost = new HostFlowHost(_logger);
    }

    /// <summary>Builds the scheduler, the clock the loop runs on, and the lighting services with their caches.</summary>
    private void CreateClocksAndLighting(FrameLoopState state)
    {
        // The flow state machine owns the frame, and the menu is a task beneath it — so the frame walk
        // updates and renders the menu rather than this loop doing either by hand.
        _scheduler = new(_device, _pacing);

        // -lockstep replaces the wall clock with a synthetic one advanced a fixed amount per loop pass,
        // so how long a frame took to draw stops deciding how many ticks it covered. Without it a capture
        // is not reproducible -- two identical command lines land on different tick counts and so on
        // different soldier and animation states, which measured at 9% of pixels differing. Everything
        // else in the simulation is already deterministic (IgiRandom is seeded, not time-seeded), so this
        // is the whole of what a reference capture needs.
        state.LockstepClock = _options.Lockstep ? new ManualTimeSource() : null;

        // Held as a field, and built here rather than left to GameLoop's own default, because a second
        // loop is created for every mission the menu previews and one of those eventually takes the
        // session over. Two loops that each defaulted to their own SystemTimeSource would read
        // milliseconds from different origins, and every `last*Milliseconds` delta in the frame loop
        // would jump the moment the swap happened. One source, shared, keeps the handover invisible.
        _timeSource = state.LockstepClock is not null ? state.LockstepClock : new SystemTimeSource();
        // Baked global illumination: a mission's probe field is loaded from the shipped cache (Assets/gi)
        // when present, else traced and written to the per-user cache so the next load is fast. This is what
        // turns a level load from a few-seconds lighting trace into an instant read -- see GiBakeCache.
        GiBakeCache probeCache = GiBakeCache.Create(
            Path.Combine(AppContext.BaseDirectory, "Assets", "gi"),
            Path.Combine(SettingsPaths.GetUserDirectory(), "cache", "gi"),
            _logger);

        // The level's distance fields, cached the same way and for a sharper reason. This is the one bake
        // the real-time lighting model WAITS on -- until it lands the frame renders from the artists'
        // lightmaps and then visibly crosses over -- and it is the most cacheable thing in the renderer,
        // depending on nothing but the level's static geometry and one integer. See DistanceFieldCache.
        _sdfCache = DistanceFieldCache.Create(
            Path.Combine(AppContext.BaseDirectory, "Assets", "sdf"),
            Path.Combine(SettingsPaths.GetUserDirectory(), "cache", "sdf"),
            _logger);
        _gi = new GlobalIllumination(_device, _logger, _work, _options.Lockstep, new GlobalIlluminationHost(this));

        // The path-traced object lightmaps, cached the same way: baked once, read instantly after. The
        // shipped bakes live beside the probe fields; freshly-baked ones write to the per-user cache.
        LightmapBakeCache lightmapCache = LightmapBakeCache.Create(
            Path.Combine(AppContext.BaseDirectory, "Assets", "gi"),
            Path.Combine(SettingsPaths.GetUserDirectory(), "cache", "gi"),
            _logger);
        _baked = new BakedLighting(_device, _logger, _work, _options.Lockstep, probeCache, lightmapCache);

        // The mission state native, held because a switch has to re-register it into the loop it builds.
        _missionState = new GameMissionState();
    }

    /// <summary>Starts the player's damage, the screen effects and the capture switches from nothing.</summary>
    private void ResetPlayerAndCaptures(FrameLoopState state)
    {
        state.ReportedWorld = false;
        _igi1.Tick.ResetReports();

        _mission.ResetPlayerLife((int)(_options.Difficulty
            ?? (AiDifficulty)Math.Clamp(_configuration.Active?.Difficulty ?? 1, 0, 2)));
        _flashlight.Reset();

        // The camera the game hands the view to when the player dies -- it pulls back off the first-person eye
        // to a third-person shot of the body (HumanCamera death mode). The body it looks at is the player's
        // own third-person rig, which SoldierRenderer loads and poses with the death fall.
        _viewpoint?.Renew();
        _igi1.Tick.ResetClimbs();
        state.TemporalFrame = 0;

        // Test-only authored-cutscene capture harness. Set OPENIGI_CUTSCENE_CAPTURE_DIR to enable;
        // OPENIGI_CUTSCENE_CAPTURE_SCENE selects one scene id (unset/0 captures whichever scene is
        // active), OPENIGI_CUTSCENE_CAPTURE_START_TICK is the first scene tick written and
        // OPENIGI_CUTSCENE_CAPTURE_COUNT the still budget (default 5). One still is written every
        // 90 logic ticks (3 s) while the scene runs, named mission-NN-scene-ID-tick-NNNNNN.bmp;
        // OPENIGI_CUTSCENE_CAPTURE_EVERY changes the interval, and 0 writes every rendered frame (the
        // name then carries the frame too), for flicker that changes between frames of one tick.
        state.CutsceneCaptureDirectory = Environment.GetEnvironmentVariable("OPENIGI_CUTSCENE_CAPTURE_DIR");
        state.CutsceneCaptureScene = 0;
        state.CaptureCutscene = state.CutsceneCaptureDirectory is not null;
        _ = int.TryParse(Environment.GetEnvironmentVariable("OPENIGI_CUTSCENE_CAPTURE_SCENE"), out int captureScene);
        state.CutsceneCaptureScene = captureScene;
        state.CutsceneCaptureTick = int.TryParse(
            Environment.GetEnvironmentVariable("OPENIGI_CUTSCENE_CAPTURE_START_TICK"),
            out int configuredCaptureTick)
            ? Math.Max(0, configuredCaptureTick)
            : 90;
        state.CutsceneCaptureEvery = int.TryParse(
            Environment.GetEnvironmentVariable("OPENIGI_CUTSCENE_CAPTURE_EVERY"),
            out int configuredCaptureEvery)
            ? Math.Max(0, configuredCaptureEvery)
            : 90;
        state.CutsceneCaptureCount = 0;
        state.CutsceneCaptureStartTick = state.CutsceneCaptureTick;
        state.PreviousCapturedScene = null;
        state.CutsceneCaptureLimit = int.TryParse(
            Environment.GetEnvironmentVariable("OPENIGI_CUTSCENE_CAPTURE_COUNT"),
            out int configuredCaptureCount) && configuredCaptureCount > 0
            ? configuredCaptureCount
            : 5;

        // A scripted free-camera flight (-camend), if one was asked for. The opening pose is captured on
        // the first rendered frame and the camera is moved along a straight line to the end pose across the
        // frame budget, so the temporal filters (GI, TAA) can be seen resolving under real motion -- which a
        // static capture cannot show. state.FlightFrame is the rendered-frame index, and the sequence's frame number.
        state.FlightStarted = false;
        state.FlightStartPos = default;
        state.FlightStartYaw = 0f;
        state.FlightStartPitch = 0f;
        state.FlightFrame = 0;

        _baked.Forget();

        // The level's signed distance fields, and the clipmaps that resample them around the camera. Baked
        // on a worker for the same reason the probe field is; the clipmaps themselves are GL and are built
        // on the render thread once the bake lands.
        _gi.Forget();
        state.CardLightingDumped = false;

        // How many times the bounce has been re-lit this mission. Zero converges in one shot; each relight
        // after that accumulates over frames. Reset when a mission loads.

        // One screen-space indirect dump per run, to verify the gather reads the G-buffer and reconstructs
        // world positions correctly before it feeds the frame.
        state.ScreenGiDumped = false;

        // What the screen-space GI keeps from one frame to the next.
        state.ScreenGi = new();

        _mission.ShotRandom = new(20000101);
        _scene = null;
        _world = null;
        _terrain = null;
        _sky = null;
        _shadowPass = null;

        // Authored atmosphere. Levels with no entry fall back to what their own FlatSky task asks for.
        state.FogPresets = FogPresets.Load(
            Path.Combine(AppContext.BaseDirectory, "Assets", "fog-presets.json"),
            _logger);
    }

    /// <summary>Loads what every level draws with: the fog presets, the materials, the lamp manifest and the detail maps.</summary>
    private void LoadSharedRenderData(FrameLoopState state)
    {
        _materials = MaterialLibrary.Load(
            Path.Combine(AppContext.BaseDirectory, "Assets", "materials.json"),
            _logger);

        // The lamps somebody has named by hand. One list for the whole game -- a model is a global
        // asset and binds the same textures in every mission it appears in -- so this is read once for
        // the session rather than per level.
        _lampManifest = LampManifest.Load(
            Path.Combine(AppContext.BaseDirectory, "Assets", "lamps.json"),
            _logger);

        // The derived surface detail this game never shipped -- normals, roughness, metal. Built by
        // derived here for anything no replacement carries a map for; an installation that has never run a tool
        // has none, and renders exactly as it did before any of this existed.
        //
        // Constructed once for the whole session rather than per level, because a texture pack is shared
        // across missions and the maps are the textures' own property rather than a level's.
        //
        // The cache is what makes the tool optional. A texture with no map derives one on a worker the
        // first time it is seen and the result is written here, so a player who never runs ResExtract
        // still gets the maps -- from their OWN installation's art, which is the only place this
        // repository can get it from. Under %APPDATA% rather than in the checkout because it is derived
        // per-user data, and because the game's art with a filter over it is not ours to redistribute.
        _detailMaps = new(
            _device,
            _work,
            _logger,
            Path.Combine(SettingsPaths.GetUserDirectory(), "cache", "detail"));

        // Set HERE as well as on the frame, and the difference only shows on the path a capture takes.
        // The loop assigns this every pass so that turning r_material_detail up mid-session starts
        // deriving for whatever loads next -- but a level reached with -mission loads its textures BEFORE
        // the first pass, so every one of them saw the default of false and cached the answer "no map".
        // A mission started from the menu never hit it, because the loop is already running by then.
        // Measured: 27 maps derived over 600 frames of -mission, against a level's worth of textures.
        _detailMaps.DeriveMissing = RenderCVars.MaterialDetail.Value > 0f;
        _detailMaps.Enabled = _detailMaps.DeriveMissing;

        // What a menu backdrop needs to be promotable into the played mission rather than thrown away,
        // and what it needs to be LIT the way that mission will be: the lamp manifest its proposal is
        // checked against and the field cache it reads. Built here because every part of it is finished
        // being written by this point and none of it is mutated afterwards, which is what makes it safe
        // to hand to a worker.
            RebuildPreviewContext();

        // Skeleton 0's and skeleton 5's animation sets, read here rather than when a mission starts.
        // They live under common/, so they are the same 2.4 MB of IFF for every mission, and parsing
        // them was the single largest thing standing a player up used to do on the render thread.
        //
        // Started rather than done: at start-up the thing between the player and the first frame is this
        // thread, and the read needs nothing from it. The first mission waits for the result, by when it
        // has been sitting finished for several seconds.
        BeginReadingSharedAnimations();

        // Re-graded for whatever is actually on screen: the mission being played, else the menu's
        // backdrop, else whatever -mission asked for. Reading `_options.StartMission` unconditionally
        // meant changing the table from the console while browsing the campaign re-graded to mission 0.
        WhenChanged(RenderCVars.ColourLookupTable, () =>
            ApplyColourLookup(_session.Mission?.Id ?? _menuBackdrop.MissionId ?? _options.StartMission));
    }

    /// <summary>Starts the session with no level, no player and no world.</summary>
    private void ForgetLevel(FrameLoopState state)
    {
        // The level's own sun, read from its Dirlight task -- section 12.
        // Null until a level is loaded, and for a level that declares none.
        _sun = null;
        _camera = null;

        // The walking player. F1 swaps between it and the free camera: the camera is how a level is
        // inspected, the player is how it is stood in. Both draw through the same Camera3D.
        _mission.Player = null;
        _heldWeapon = null;
        _hud = null;
        _debugOverlay = null;
        _marks = null;
        _tracers = null;
        _effects.Forget();
        _cameraStatusLights = null;
        _grenadeRenderer = null;
        _missileRenderer = null;
        _loadedMissionForWeapon = null;
        _arms = null;
        _soldiers = null;
        _mission.AdoptSoldiers(null);
        _mission.SoldiersInWalk = false;
        _mission.PlayerInWalk = false;
        _playerTerrain = null;
        _mission.EffectWalls = null;
        _effectTerrainRow = null;
        _ground = null;

        // The material table, shared with the guard-round handler so an AI round's impact can leave the
        // same mark a player's does -- retail rounds are physical and decal whatever they meet (10.4).
        _mission.Materials = null;
        _mission.AmmoTypes = null;
        _mission.WeaponTypes = null;
        _mission.AlarmSpeakers = null;
        _mission.Destructibles = null;
        _igi1.Audio.ForgetSirens();
        _playerMode = false;

        // The debug overlay (F3): the player's state and a crosshair "what am I looking at" readout. The
        // collision index is kept here so the overlay can name the object under the crosshair.
        _mission.Solids = null;
        _mission.Trace = null;
        state.ShowDebug = false;
    }

    /// <summary>Teaches the flow host how to bring the title's own main menu up.</summary>
    private void WireMainMenu(FrameLoopState state, GameMusicNatives musicNatives)
    {
        _flowHost.LoadMainMenu = () =>
        {
            _retailMenus?.Dispose();
            _retailMenus = _presentation.Presenters.Create<RetailMenusRequest, IRetailMenus>(
                new RetailMenusRequest(
                    _device,
                    _fileSystem,
                    _loop,
                    _configuration,
                    _menuDisplay,
                    () => _menuMissions,
                    () => _menuStrings,
                    _window.Input,
                    state.Batch,
                    () => _window.Width,
                    () => _window.Height,
                    // The menu and its hit-test have to agree about the magnification. See UiScale for why the
                    // interface is scaled at all.
                    _options.UiScale,
                    _audioOut.Sounds,
                    _options.StartScreen,
                    // Both of these are game natives rather than menu-system ones.
                    (name, arguments) =>
                        _missionState.TryInvokeFromMenu(name, arguments) ?? musicNatives.TryInvokeFromMenu(name, arguments),
                    startGame =>
                    {
                        if (startGame)
                        {
                            StartMission(_missionState.PendingMissionId);
                        }
                        else
                        {
                            state.Running = false;
                        }
                    },
                    _logger));
            if (_retailMenus is null)
            {
                _logger.Warning("Menu", "This title draws no retail menus; the menu script is not started.");
            }
        };
    }

    /// <summary>Starts the front end's state -- the shell, the backdrop, the dissolve and the entry shot -- from nothing.</summary>
    private void ResetFrontEndState(FrameLoopState state)
    {
        // R11: the cinematic shell (OpenIGI.Frontend) is the default front end; -vanilla and -vanillamenu
        // reach the byte-for-byte retail menus instead. -mission still skips the menus straight into a level.
        state.Shell = null;
        _showcaseCamera = null;
        state.MissionWarmer = null;
        state.PreviewLoader = null;
        _menuCameras = MenuCameraLibrary.Empty;
        state.ShellTypography = null;

        // Hoisted so a title switch can rebuild the shell around them. Neither belongs to a game: the
        // host seam calls back into this class, and the save store is the player's, one directory for
        // the session. What the rebuilt shell does get anew is its CONTEXT -- the game list and the
        // mission list, which are the two things that change when the game does.
        state.ShellHost = null;
        state.Saves = null;

        // The menu is down and has shown nothing yet; the showcase opens on its default mission.
        _menuFlow.Reset(ShowcaseMissionId);
        state.FirstFrameReported = false;

        // -entrycam: Play has not been pressed yet. Set only when the flag asked for it.
        state.EntryCamPending = false;
        state.EntryTimeChosenAt = -1;
        state.ShellHostForEntryCam = null;

        // When the mission-entry shot last read the clock.
        state.LastEntryMilliseconds = 0;
        state.LastComputerMilliseconds = 0;

        // The menu is held to the display's rate rather than rendering as fast as it can, where the
        // platform leaves the rate to the loop. See IFramePacer.
        state.MenuPacer = _platform.CreateFramePacer();
        state.LastPassRendered = false;
    }

    /// <summary>Builds the front end the command line asked for: none for a bake, the original's menus, or the cinematic shell.</summary>
    private void BuildFrontEnd(FrameLoopState state)
    {
        // -vanillamenu reaches the retail menus, which carry their own in-game menu; everything else runs
        // the cinematic shell. -mission still opens straight into a level — what it no longer does is leave
        // the shell unbuilt, because the pause menu IS the shell and Escape has to reach it from a mission
        // the menus were skipped into.
        if (_options.BakeAll)
        {
            // The bake sweep runs after the window is shown (the probe wait pumps the message queue); the
            // shell and menus are skipped entirely.
        }
        else if (_options.VanillaMenu && _options.StartMission <= 0)
        {
            _loop.Flow.Request(FlowEvent.MainMenu);
        }
        else
        {
            _menuCameras = MenuCameraLibrary.Load(
                Path.Combine(AppContext.BaseDirectory, "Assets", "menu-cameras.json"), _logger);
            state.MissionWarmer = new MissionWarmer(_gamePath, _missions, _logger, _work);
            state.PreviewLoader = new MissionPreviewLoader(
                _gamePath, _missions, _mission.MagicObjects, _baked.ProbeCache, _menuCameras, _previewContext, _logger, _work);

            // The design's four faces — Barlow, Barlow Condensed in two weights, JetBrains Mono — the same
            // set the launcher draws with, uploaded once and shared by every page of the shell.
            state.ShellTypography = UiFontLibrary.Typography(_device);
            UiTheme shellTheme = state.ShellTypography.CreateTheme();

            // Held so the mission-entry shot can typeset its title with the same faces the shell uses.
            _shellBodyFont = state.ShellTypography.Body;
            _shellHeadingFont = state.ShellTypography.Display;
            _shellType = state.ShellTypography;

            // The real DefineMission data, resolved to display strings through the language table, in the
            // order the CAMPAIGN CHAIN gives — each descriptor names its successor. The registry's own
            // order is the startup scan's, which is a directory walk and therefore alphabetical, so it
            // listed level 10 between level 1 and level 2. The retail menu walks the chain (0x405660) and
            // so does this.
            state.Saves = new SaveSlotStore(Path.Combine(SettingsPaths.GetUserDirectory(), "state.Saves"));

            state.ShellHost = new ShellHost(
                startMission: (id, difficulty) =>
                {
                    if (_configuration.Active is { } activeProfile)
                    {
                        activeProfile.Difficulty = difficulty;
                    }

                    // A load already under way owns the screen; a second press is nothing.
                    if (_loadSequence.IsActive)
                    {
                        return;
                    }

                    // Reachable from the pause menu as well as from the front end — the campaign is in the
                    // bar either way — so the shell is told it is no longer over a mission it was paused on.
                    // Deferred where a sequence covers the load: the menu keeps drawing under the departure
                    // and is put away on the frame the load actually runs.
                    void LeaveTheMenu()
                    {
                        state.Shell?.ClosePauseMenu();
                        _menuFlow.MenuMode = false;
                        _menuFlow.PauseMenu = false;
                        ApplyGameplayMotionBlur();
                    }

                    // The menu has been drawing this very level for as long as the player has been
                    // reading the briefing, built on a worker and complete. When it is the mission they
                    // picked, the level is PROMOTED rather than loaded a second time — see
                    // PromoteBackdrop — behind the same going-in sequence a restart gets: the picture
                    // falls out of focus under the card, the promotion runs, and the shot opens over the
                    // start. The backdrop is gone over a played mission, so this cannot fire from the pause
                    // menu. -lockstep is excluded: the sequence runs on wall-clock time, and a reference
                    // capture has to land on the same frame every run.
                    if (_menuBackdrop.Preview is { } backdrop && backdrop.MissionId == id && !_options.Lockstep)
                    {
                        BeginWatchingFramePasses($"Entering mission {id}");

                        void Promote()
                        {
                            LeaveTheMenu();
                            if (PromoteBackdrop(backdrop))
                            {
                                _window.CursorVisible = false;
                                ReportWatchedFramePassesIn(2);
                            }
                            else
                            {
                                StartMission(id);
                                _window.CursorVisible = false;
                            }
                        }

                        if (CinematicLoadAvailable)
                        {
                            BeginCinematicLoad(id, Promote);
                        }
                        else
                        {
                            Promote();
                        }

                        return;
                    }

                    void LoadCold()
                    {
                        LeaveTheMenu();
                        BeginWatchingFramePasses($"Loading mission {id}");
                        StartMission(id);
                        ReportWatchedFramePassesIn(5);
                        _window.CursorVisible = false;
                    }

                    if (CinematicLoadAvailable)
                    {
                        BeginCinematicLoad(id, LoadCold);
                    }
                    else
                    {
                        LoadCold();
                    }
                },
                setMissionConditions: conditions =>
                {
                    _conditions = conditions;

                    // One variable carries the hour, because one already means exactly this:
                    // r_time_of_day is "the hour a level opens at, or negative for the level's own", and
                    // both the mission loader and the menu's backdrop already reset the cycle from it. It
                    // is deliberately not archived, which is what makes a campaign choice safe to put in
                    // it -- a night run lasts the session and does not become a preference.
                    RenderCVars.TimeOfDay.Value = conditions.StartHour;

                    // The weather gets no variable of its own. Forcing rain everywhere through
                    // r_weather_force WOULD be archived, so a campaign choice would outlive the campaign
                    // and rain on every mission of the next run; ApplyWeatherSettings reads the choice
                    // straight off _conditions instead.
                    ApplyWeatherSettings();

                    // The level behind the menu is re-lit at once, so choosing night shows night while
                    // the briefing is still being read. Never over a mission being played: the sky is not
                    // to change under a player halfway through a level, and the hour they chose is picked
                    // up by the next level they load.
                    if (_menuFlow.OverShowcase)
                    {
                        _dayNight.ResetForLevel(_sun, RenderCVars.TimeOfDay.Value);
                    }
                },
                resumeMission: () =>
                {
                    // Nothing is reloaded and nobody is placed again: the level was never torn down, only
                    // left un-ticked, so resuming is this loop putting the menu away.
                    state.Shell?.ClosePauseMenu();
                    _menuFlow.MenuMode = false;
                    _menuFlow.PauseMenu = false;
                    _window.CursorVisible = false;
                },
                restartMission: () =>
                {
                    if (_session.Mission is { } mission && !_loadSequence.IsActive)
                    {
                        // The frozen mission under the pause menu is the picture the sequence holds: the
                        // menu leaves, the place falls out of focus, and it comes back reset.
                        void Restart()
                        {
                            state.Shell?.ClosePauseMenu();
                            _menuFlow.MenuMode = false;
                            _menuFlow.PauseMenu = false;
                            ApplyGameplayMotionBlur();
                            StartMission(mission.Id);
                            _window.CursorVisible = false;
                        }

                        if (CinematicLoadAvailable)
                        {
                            BeginCinematicLoad(mission.Id, Restart);
                        }
                        else
                        {
                            Restart();
                        }
                    }
                },
                abandonMission: () =>
                {
                    // Before it was ever shown, possibly. Counting the rest of the warm-up down would
                    // hold a loading panel over the campaign screen.
                    CancelMissionWarmUp();
                    ReleasePlayedMission();
                    state.Shell?.ClosePauseMenu();
                    state.Shell?.OpenOn("campaign");
                    _menuFlow.MenuMode = true;

                    // The shell is over the showcase again from here, so the backdrop has to be streamed:
                    // the level it was drawing over has just been released, and it is framed as a
                    // backdrop again rather than as something being played through.
                    _menuFlow.PauseMenu = false;
                    _menuFlow.ShowcasePending = true;
                    _window.CursorVisible = true;
                },
                quit: () => state.Running = false,
                switchTitle: RequestTitle,
                previewMission: id =>
                {
                    // Never over a live mission. Adopting a preview disposes the world renderer and drops
                    // the player, which from the pause menu would be the shell demolishing the mission it
                    // is drawn over. The screens already know not to ask (ShellContext.InGame); this is the
                    // guarantee rather than the manners.
                    if (_menuFlow.PauseMenu)
                    {
                        return;
                    }

                    int wanted = id ?? ShowcaseMissionId;

                    // -entrycam holds its own mission: the campaign opens on its first row and previews
                    // it, which would replace the backdrop the flag is waiting to press Play over.
                    if (_options.EntryCamMission > 0)
                    {
                        wanted = _options.EntryCamMission;
                    }

                    if (wanted != _menuFlow.CurrentShowcaseMissionId)
                    {
                        // Warm the level's files on a worker now, and note the moment: once the selection
                        // has held for the settle window the loop asks the preview loader to build it, also
                        // on a worker. Nothing here touches the render thread, so a selection that keeps
                        // moving simply keeps re-arming this.
                        _menuFlow.PendingPreviewMissionId = wanted;
                        _menuFlow.PreviewRequestedAtMs = _loop.TimeSource.Milliseconds;
                        state.MissionWarmer?.Warm(wanted);
                    }
                    else
                    {
                        _menuFlow.PendingPreviewMissionId = null;
                    }
                },
                saveSettings: () => state.Console.SaveSettings(),
                saveProfiles: () =>
                {
                    RestoreTitleInputBindings();
                    HostConfiguration.Save(_configuration, _logger);
                },
                captureThumbnail: (out captureWidth, out captureHeight) =>
                {
                    captureWidth = _window.Width;
                    captureHeight = _window.Height;
                    var pixels = new Bgra32[captureWidth * captureHeight];
                    _device.CaptureFrame(captureWidth, captureHeight, pixels);
                    return pixels;
                },
                settingUnavailable: SettingUnavailable,
                missionSaves: () => TitlePlayer?.SaveGames,
                inputBindingLabel: action => TitleInputBindings.Label(action, _gameplay?.InputBindings ?? []),
                resetInputBindings: profile =>
                {
                    TitleInputBindings.Reset(profile, _gameplay?.InputBindings ?? []);
                });

            state.ShellHostForEntryCam = state.ShellHost;

            state.Shell = new Shell(BuildShellContext(state.ShellHost, shellTheme, state.Saves));

            // -shelltab is a development shortcut, not a game feature, and the shell's counterpart to
            // -screen: it opens straight on a section so a capture does not need someone to click there.
            if (_options.ShellTab is { Length: > 0 } openOn && !state.Shell.OpenOn(openOn))
            {
                _logger.Warning("Menu", $"-shelltab '{openOn}' is not a section; opening on the main menu.");
            }

            if (_options.StartMission > 0)
            {
                // Straight into the level, with the shell built and waiting behind Escape rather than shown.
                // Once its files can be read: on the desktop that is now, in a browser it is when the page
                // has fetched them -- a level started before that opened with no terrain and no models.
                ResumeConditions();
                StartMissionWhenReadable(_options.StartMission);
            }
            else
            {
                // The showcase level is ASKED FOR here and arrives when it arrives. It used to be
                // requested a couple of frames into the loop, because building it once blocked for
                // seconds and doing that before the window was shown was a blue screen where the menu
                // should be. It has not blocked since the build moved to a worker, and the wait between
                // asking and the backdrop appearing is that worker's -- so asking now rather than two
                // frames later is the whole of it earlier. The menu still draws over its dark backdrop
                // in the meantime, which is what the deferral was protecting.
                _menuFlow.MenuMode = true;
                int firstShowcase = _options.EntryCamMission > 0 ? _options.EntryCamMission : ShowcaseMissionId;
                _menuFlow.CurrentShowcaseMissionId = firstShowcase;

                if (state.PreviewLoader is { } opening)
                {
                    RequestBackdrop(opening, firstShowcase);
                }
                else
                {
                    _menuFlow.ShowcasePending = true;
                }

                // -entrycam: open on the campaign and remember that Play is to be pressed as soon as this
                // mission's backdrop is up. The backdrop itself is requested through the ordinary showcase
                // path below, so what gets measured is the real thing rather than a special case.
                if (_options.EntryCamMission > 0)
                {
                    state.Shell.OpenOn("campaign");
                    state.EntryCamPending = true;
                }
            }
        }
    }

    /// <summary>Starts watching the passes and puts the window on screen.</summary>
    private void ShowWindow(FrameLoopState state)
    {
        // Watch every pass from here on, so a hitch is attributed to the frame it happened on rather than
        // only reported as a run-wide peak. The stage profiler's peaks say a stage was once expensive
        // without saying when, and "when" is the whole question: a cost paid behind the menu is free and
        // the same cost paid on the frame Play is pressed is the bug.
        BeginWatchingFramePasses("Session");
        state.PassStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        state.FramePerformance = new();

        _logger.Information(
            "Render",
            FormattableString.Invariant(
                $"Camera effects: r_motion_blur {RenderCVars.MotionBlur.Value:0.###}, r_hdr {RenderCVars.OffscreenScene.Value}, r_deferred {RenderCVars.Deferred.Value}, r_taa {RenderCVars.TemporalFeedback.Value:0.###}, r_smooth {RenderCVars.SmoothPresentation.Value}, r_scale {RenderCVars.RenderScale.Value:0.###}."));

        _window.CloseRequested += (_, _) => state.Running = false;
        _window.Show();
        _logger.Information(
            "Desktop",
            $"{_platform.Name} up on {_device.Capabilities.Renderer}; the window is on screen "
            + $"{SinceProcessStart():0} ms after the process started.");
    }

    /// <summary>Runs the tools that build something and quit: <c>-poidump</c> and <c>-bakeall</c>.</summary>
    private void RunOneShotTools(FrameLoopState state, MenuStringTable strings)
    {
        // -poidump: build every mission, find the places its menu backdrop should circle, write them out
        // and quit. A tool run once; what it writes is then owned and edited by hand.
        if (_options.ShowcasePointDump is { Length: > 0 } dumpPath)
        {
            DumpShowcasePoints(dumpPath, strings);
            state.Running = false;
        }

        // -bakeall: trace every mission's probe field into the cache, then quit. Each StartMission loads the
        // level and writes its .giv (the load path caches a fresh bake); the window is up so the probe wait
        // can pump. Run once and ship the results.
        if (_options.BakeAll)
        {
            IReadOnlyList<MissionDescriptor> toBake = _missions.Missions;
            _logger.Information("Bake", $"Baking global illumination for {toBake.Count} mission(s).");
            int baked = 0;
            foreach (MissionDescriptor descriptor in toBake)
            {
                if (_window.IsClosing)
                {
                    break;
                }

                _logger.Information("Bake", $"[{++baked}/{toBake.Count}] mission {descriptor.Id} '{strings.Get(descriptor.Name)}'.");
                StartMission(descriptor.Id);
            }

            _logger.Information(
                "Bake",
                $"Baked {baked} mission(s). To ship them, copy the .giv files from "
                    + $"{Path.Combine(SettingsPaths.GetUserDirectory(), "cache", "gi")} into apps/OpenIGI.Composition/Assets/gi.");
            state.Running = false;
        }
    }

    /// <summary>Validates the distance-field shader and starts the loop's clocks.</summary>
    private void PrimeLoop(FrameLoopState state)
    {
        // The distance-field shader is compiled here, when the feature is on, rather than when a level
        // first needs it -- a shader that does not compile would otherwise surface a long way into a run
        // as a stage that silently did nothing. Gated so a device with the feature off pays nothing.
        if (RenderCVars.DistanceFields.Value && _device.Capabilities.SupportsCompute)
        {
            GlobalDistanceField.ValidateShader(_device, _logger);
        }

        // Reused every frame rather than reallocated; the fit writes into it in place.
        state.CascadeStorage = new ShadowCascade[ShadowSettings.MaxCascades];

        state.LastCameraMilliseconds = _loop.TimeSource.Milliseconds;
        state.LastConsoleMilliseconds = _loop.TimeSource.Milliseconds;
        state.LastShellMilliseconds = _loop.TimeSource.Milliseconds;
        state.LastMenuCameraMilliseconds = _loop.TimeSource.Milliseconds;
        _baked.StartChaseClock(_loop.TimeSource.Milliseconds);

        // A capture run is a background process as far as the person at the keyboard is concerned: it must
        // not read the mouse and must never grab the cursor out from under whatever they are actually doing.
        state.CaptureMode = _options.Lockstep;

        // What the last pass of the loop did, and how many ticks in a row have run without a step
        // returning to its host. See the top of the loop.
        state.LastOutcome = FrameOutcome.Idle;
        state.TicksSinceStep = 0;
    }
}
