using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Audio;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Hosting;
using OpenIGI.Game.Level;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Host.Menu;
using OpenIGI.Host.Services;
using OpenIGI.Presentation;
using OpenIGI.Presentation.Lighting;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Host;

public sealed partial class GameHost
{
    // The mission being played: which, its level tree, and how far it has got to being decided.
    private readonly MissionSession _session;

    private void StartMission(int id)
    {
        if (!_missions.TryGet(id, out MissionDescriptor? chosen))
        {
            _logger.Warning("Mission", $"Asked to start mission {id}, which is not registered.");
            return;
        }

        // A co-op host's players load the mission alongside it, from now rather than once its own is up.
        if (TickObserver is Multiplayer.ISessionHold hosting)
        {
            hosting.AnnounceGoing(id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        // The hold starts before anything blocks, and so does the panel. Everything below this line
        // runs on the render thread without presenting, so without a frame pushed out here the window
        // simply freezes on whatever it was last showing for the half second it takes -- and the warm-up
        // panel cannot cover that, being drawn by the loop this is blocking. One frame now, holding the
        // picture the player was looking at, and the panel is already up when the load blocks.
        BeginMissionWarmUp(chosen);
        PresentLoadingFrame();

        ReleasePlayedMission();

        _logger.Information("Mission", $"Starting {chosen}.");
        LevelTask levelRoot = (LevelTask)_loop.Tasks.New(_loop.Tasks.Root, _loop.Types.Get(EngineTaskTypes.Level));
        _session.Begin(chosen, levelRoot);

        // The menu's lite profile ends here, before anything below reads the variables it overrides.
        UpdateBackdropProfile();
        _computerOpen = false;
        _computerFlight.Reset();

        // The marker belongs to a task in the level being torn down; carrying its fade into the next
        // one would project a dead anchor for a frame.
        _interactionPrompt.Reset();
        LevelLoader levelLoader = new(levelRoot, _loop.TaskCommands, _loop.Natives, _resources, _logger);
        TerrainLoader terrainLoader = new(_resources, _loop.Natives, _logger);

        // The bank is rebuilt per mission, so the mission's own sounds mount into one holding only the
        // menu's. MISSION: has already been re-pointed by the loader below, in the same step order the
        // original uses.
        ResetSoundsForMission();

        // The resource registry may have arrived from a promoted preview, but audio and other session
        // services retain the file system created at startup. Both aliases must move before the loader
        // reads through either side.
        if (!ReferenceEquals(_resourceFileSystem, _fileSystem))
        {
            _fileSystem.SetAlias(VirtualFileSystem.MissionAlias, chosen.MissionAliasTarget);
        }
        // The title's format profile, without which every pack is read as the first game's and a title
        // shipping a later mesh version has every model in it refused on the way in -- the pack still
        // opens, still reports a name, and holds nothing.
        MissionLoader missionLoader = new(
            _missions, _resourceFileSystem, _resources, levelLoader, _logger, terrainLoader, _audioOut.Bank,
            _titleRuntime.Formats);
        LoadedMission loaded = missionLoader.Load(id);

        // Evaluate the level's ConditionalContainer conditions against the initial playable state, so the
        // scene, spawn and soldier walks below skip the subtrees the original leaves unregistered (the
        // intro/outro cut-scene props and actors). Runs after the tree is built and its variables exported,
        // and before anything walks it.
        new ConditionalContainerGate(_loop.Variables, _logger).Prepare(levelRoot);

        BringMissionUp(loaded, levelRoot, id, promoted: false);
    }

    /// <summary>Applies what a title reads off a level once and the renderer keeps for the whole of it.</summary>
    /// <param name="levelRoot">The level's task tree.</param>
    /// <remarks>
    /// <para>
    /// Two title seams, both constants of the level rather than of the frame. The AIR: I.G.I. 2 reads
    /// a fog colour, a fog density and a flat ambient off its <c>GlobalLight</c> and fogs every surface
    /// against them; Project I.G.I. authors none of this and answers nothing, which leaves its picture
    /// exactly as it was (<see cref="IGraphicsDevice.SetFog"/>). The OBJECT SHADING: the sequel's
    /// bakes are <c>MODULATE2X</c> and its unlightmapped objects are lit by a fixed-function sun read off
    /// the level; the other title keeps <see cref="ObjectShading.Default"/>, the compositing it always
    /// had. The gain and the sun's direction are the world renderer's, per draw; the sun's colours are
    /// the device's, baked in beside the fog.
    /// </para>
    /// <para>
    /// Called from BOTH ways a level comes up: <see cref="AdoptPreview"/>, where the menu builds it as a
    /// backdrop, and the cold path of <see cref="BringMissionUp"/>. It used to live in the latter's
    /// not-promoted block alone, so a mission entered through the menu -- the way the game is played --
    /// had no haze, white tanks and unlit objects, while one launched straight to had all three.
    /// </para>
    /// </remarks>
    private void ApplyLevelConstants(GameTask levelRoot)
    {
        // The level's own fog and flat model light, which every frame sets on the device. Read for every level, however it
        // came up: a mission loaded cold used to keep whatever the last backdrop had left here -- another level's fog and
        // light, or none at all.
        _globalLight = GlobalLightSettings.Read(levelRoot);

        SceneAtmosphere? air = _presentation.ReadAtmosphere(levelRoot);
        _atmosphere = air;
        // Through the SAME SceneFog every world draw already wears, rather than a mechanism of its own.
        // Two were built independently -- this game's air as pipeline specialization constants, and
        // Project I.G.I.'s retail fog as device state with a per-vertex factor. The second is strictly
        // the more capable: it changes per draw without a pipeline variant, and SceneFog.ColourFor
        // already knows an additive pass fogs to black.
        _device.SetFog(air is { } authored
            ? new SceneFog(authored.FogColour, authored.FogDensity)
            : SceneFog.None);
        if (air is { } reported)
        {
            _logger.Information(
                "Mission",
                $"The level's air: fog {reported.FogColour} at {reported.FogDensity:G4} per metre, "
                    + $"ambient {reported.Ambient}.");
        }

        // How the level drives the dynamic renderer, for a title whose sun is a lighting direction
        // rather than a position in the sky, or for a level whose night only its sky dome says. Project
        // I.G.I. answers nothing for its daylit levels, which keep the sweep as it was.
        _deferredLighting = _presentation.ReadDeferredLighting(levelRoot);
        if (_deferredLighting is { } lit)
        {
            _logger.Information(
                "Mission",
                $"The level's own light: ambient {lit.Ambient}, nightfall {lit.Nightfall:F2} "
                    + "(its sun is authored, so the day/night sweep is off).");
        }

        ObjectShading shading = _presentation.ReadObjectShading(levelRoot) ?? ObjectShading.Default;
        if (_world is { } shadedWorld)
        {
            shadedWorld.Shading = shading;
        }

        LightLooseModels(shading.Sun);

        // How the level's models give up detail, which only the original's look draws by.
        _modelLod = _presentation.ReadModelLod(levelRoot, _resources, _title?.Services);

        _device.SetSceneSun(shading.Sun?.Ambient ?? Vector3f.Zero, shading.Sun?.Colour ?? Vector3f.Zero);
        if (shading.Sun is { } fixedFunctionSun)
        {
            _logger.Information(
                "Light",
                $"Objects without a bake are lit by the level's sun: ambient {fixedFunctionSun.Ambient}, "
                    + $"colour {fixedFunctionSun.Colour}, towards {fixedFunctionSun.Direction}; "
                    + FormattableString.Invariant($"bakes are multiplied by {shading.LightmapGain:G2}."));
        }
    }

    /// <summary>Builds everything a loaded level needs in order to be played, and stands the player in it.</summary>
    /// <param name="loaded">The mission's descriptor, packs and terrain.</param>
    /// <param name="levelRoot">The level's task tree, already loaded and gated.</param>
    /// <param name="id">The mission id, for the colour grade and the log.</param>
    /// <param name="promoted">
    /// <see langword="true"/> when the level arrived as the menu's backdrop and is being promoted rather
    /// than loaded. The backdrop is already drawn — its scene, collision index, terrain, sky, sun, grade
    /// and camera were all built by <see cref="AdoptPreview"/> off the render thread — so the steps that
    /// would rebuild them are skipped and the rest runs exactly as it does for a cold start. The order of
    /// everything else is deliberately untouched between the two paths: the AI's ray cast closes over
    /// <c>_mission.Terrain</c>, the interactions have to exist before the brains are animated, and the
    /// weapon rosters have to load before <c>humanplayer.qvm</c> runs.
    /// </param>
    private void BringMissionUp(LoadedMission loaded, LevelTask levelRoot, int id, bool promoted)
    {
        BeginStage();
        LevelUpdateWalker walker = new(levelRoot);
        MissionBuild build = new(walker, _title.Services, loaded, levelRoot, id, promoted, _mission);
        LevelSystems<MissionLevelBuild> systems = MissionSystems();
        if (_title.Services.TryGet(out LevelSystemEdits? edits))
        {
            // A mod's systems join the host's by their names (MissionSystemNames), for this session's missions.
            edits.Apply(systems);
        }

        systems.BuildAll(build);
    }

    /// <summary>Makes the mission systems, in the order a mission is brought up: the host's, and the game's set among them.</summary>
    /// <returns>A fresh list, which the session's level-system edits may change.</returns>
    private LevelSystems<MissionLevelBuild> MissionSystems()
    {
        LevelSystems<MissionLevelBuild> systems = new LevelSystems<MissionLevelBuild>()
            .Add(HostSystem(MissionSystemNames.Sounds, MissionSounds))
            .Add(HostSystem(MissionSystemNames.TestCaptures, MissionTestCaptures))
            .Add(HostSystem(MissionSystemNames.Computer, MissionComputer))
            .Add(HostSystem(MissionSystemNames.Scene, MissionScene))
            .Add(HostSystem(MissionSystemNames.SceneView, MissionSceneView))
            .Add(HostSystem(MissionSystemNames.Soldiers, MissionSoldiers))
            .Add(HostSystem(MissionSystemNames.InteractionsView, MissionInteractionsView))
            .Add(HostSystem(MissionSystemNames.CamerasView, MissionCamerasView))
            .Add(HostSystem(MissionSystemNames.LightBakes, MissionLightBakes))
            .Add(HostSystem(MissionSystemNames.AlarmsView, MissionAlarmsView))
            .Add(HostSystem(MissionSystemNames.Picture, MissionPicture))
            .Add(HostSystem(MissionSystemNames.Player, MissionPlayer))
            .Add(HostSystem(MissionSystemNames.Adornments, MissionAdornments));

        // The cutscenes, objectives, vehicles, interactions, cameras, nav graphs, brains and walk are the game's own.
        _hosted.AddMissionSystems(systems);
        return systems;
    }

    // One of the host's own systems, which reads the host's build; a mod's reads the public one.
    private static LevelSystem<MissionLevelBuild> HostSystem(string name, Action<MissionBuild> build) =>
        new(name, b => build((MissionBuild)b));

    // A system ran before the one it depends on.
    private static InvalidOperationException MissingSystem(string name) =>
        new($"The mission system '{name}' has not run.");

    /// <summary>The difficulty every guard reads its combat row at.</summary>
    /// <returns>The <c>-difficulty</c> override, or else the active profile's.</returns>
    /// <remarks>
    /// Faithfully the ACTIVE PLAYER PROFILE's (sub_406F70 takes it from sub_406220()'s first field), so the difficulty the
    /// player picked in the menu reaches the AI; -difficulty still overrides, for testing one without editing a profile.
    /// </remarks>
    private AiDifficulty EffectiveDifficulty() => _options.Difficulty
        ?? (AiDifficulty)Math.Clamp(
            _configuration.Active?.Difficulty ?? (int)AiDifficulty.Normal,
            (int)AiDifficulty.Easy,
            (int)AiDifficulty.Hard);

    /// <summary>Where <see cref="EffectiveDifficulty"/> came from, for the log.</summary>
    private string DifficultySource => _options.Difficulty is null
        ? $"from profile '{_configuration.Active?.Name ?? "(none)"}'."
        : "from -difficulty override.";

    /// <summary>The player: its rig, its animations, its loadout and HUD, stood up at the spawn; or the title's own player.</summary>
    private void MissionPlayer(MissionBuild b)
    {
        LoadedMission loaded = b.Loaded;
        LevelTask levelRoot = b.LevelRoot;
        int id = b.Id;
        MissionStringResolver missionText = b.MissionText!;
        WorldScene scene = b.Scene!;
        ObjectCollisionIndex solids = b.Solids!;

        {
            var playerSetup = _hosted.NewPlayerSetup();
            playerSetup.Begin(loaded, levelRoot, solids, missionText);
            Formats.Animation.BoneAnimationSet? playerBodySet = _playerBodyAnimations;

            // The guards' weapons, which have to wait for the roster: a soldier's gun is an authored
            // Gun child task naming a WEAPON_ID_*, and only the table knows which model that draws.
            _soldiers?.Arm(loaded, playerSetup.Weapons);
            playerSetup.ArmVehicles(loaded, levelRoot);
            EndStage("the guards' weapon models");

            playerSetup.Configure();

            // The first-person rig. Every Human builds one in its constructor and gives it skeleton 5
            // and the model 005_01_1 (0x45EB30, 0x464660); it is what the held weapon hangs off, so
            // without it there is nothing to draw a gun on.
            if (_viewRigAnimations is { } viewRigSet)
            {
                playerSetup.UseViewRig(viewRigSet);

                // The arms are the other half of what 0x4644A0 draws, and the half that needs CPU
                // skinning: 005_01_1 is mesh format 1.
                _arms?.Dispose();
                _arms = _presentation.Presenters.Create<ViewModelPresentationRequest, IViewModelPresenter>(ViewModelRequest());
                if (_arms is not null && !_arms.Load(loaded))
                {
                    _arms.Dispose();
                    _arms = null;
                }
            }

            EndStage("the first-person rig and the arms");

            // What the mission arms you with is declared, not computed: weapon tasks hang off the
            // level's own HumanPlayer, and every WEAPON_ID_* resolves to a roster entry naming a
            // Gun Model. With the rig in place that model can finally be put where the game puts it.
            _gunPickupRenderer?.Dispose();
            _gunPickupRenderer = null;
            _heldWeapon?.Dispose();
            _hud?.Dispose();
            _marks?.Dispose();
            _tracers?.Dispose();
            _tracers = null;
            _effects.Release();
            _heldWeapon = null;

            // Set whether or not the level declares a loadout: a weapon the all-weapons cheat grants
            // later is loaded from this mission's packs like any other.
            _loadedMissionForWeapon = loaded;

            if (playerSetup.CarryDeclaredLoadout(levelRoot, out bool carried))
            {
                _heldWeapon = carried ? CreateHeldWeapon() : null;

                // The player's rounds are offered to the soldiers, which own their own health.
                playerSetup.ConnectSoldiers(_hosted.Sounds.PlayRicochet);

                // The in-game HUD owns its own archive, exactly as StatusScreen's constructor does.
                _marks?.Dispose();
                _marks = _presentation.Presenters.Create<EffectsPresentationRequest, IBulletMarkPresenter>(EffectsRequest());

                // The ballistic streaks, beside the marks they end in. Cheap to have around: with
                // g_ballistics off nothing is ever spawned and Draw returns at the count check.
                _tracers?.Dispose();
                _tracers = new TracerTrailRenderer(_device);

                // The world's effects: the bursts where rounds land, the original's own dust, chips and blood,
                // the fireball every blast shares, and the smoke rockets and blasts leave.
                _effects.Load(loaded, _presentation.Presenters, _resources, _fileSystem);

                _hud?.Dispose();
                _hud = _presentation.Presenters.Create<HudPresentationRequest, IHudPresenter>(new HudPresentationRequest(_device, _logger));
                if (_hud is not null && !_hud.Load(_fileSystem))
                {
                    _hud.Dispose();
                    _hud = null;
                }

                _remasteredHud?.Dispose();
                _remasteredHud = _presentation.Presenters.Create<HudPresentationRequest, IRemasteredHudPresenter>(new HudPresentationRequest(_device, _logger));
                if (_remasteredHud is not null && !_remasteredHud.Load())
                {
                    _remasteredHud.Dispose();
                    _remasteredHud = null;
                }

                _debugOverlay?.Dispose();
                _debugOverlay = new DebugOverlayRenderer(_device, _logger);
                if (!_debugOverlay.Load())
                {
                    _debugOverlay.Dispose();
                    _debugOverlay = null;
                }
            }

            if (_mission.GunPickups is { } pickups)
            {
                _gunPickupRenderer = _presentation.Presenters.Create<ViewModelPresentationRequest, IGunPickupPresenter>(ViewModelRequest());
                if (_gunPickupRenderer is not null)
                {
                    _gunPickupRenderer.Materials = _materials;
                    _gunPickupRenderer.Load(loaded, pickups);
                }
            }

            // The grenades in the air, drawn with their own retail models. Loaded beside the
            // pickups because it is the same model machinery at a transform the simulation owns.
            _grenadeRenderer?.Dispose();
            _grenadeRenderer = _presentation.Presenters.Create<ViewModelPresentationRequest, IGrenadePresenter>(ViewModelRequest());
            _grenadeRenderer?.Load(loaded);
            _missileRenderer?.Dispose();
            _missileRenderer = _presentation.Presenters.Create<ViewModelPresentationRequest, IMissilePresenter>(ViewModelRequest());
            _missileRenderer?.Load(loaded);
            LightLooseModels(_world?.Shading.Sun);

            EndStage("the loadout, the bullet marks and the HUD");

            // A level with neither a declared spawn nor an opening camera is simply not placed, which the
            // warning below already covers.
            if (playerSetup.Stand(levelRoot, _mission.Ground, solids, _camera?.Position, _options.CameraYaw, out PlayerSpawnPoint spawn))
            {
                IHostedPlayer candidate = playerSetup.Player;

                // The player's own third-person body -- the rig the death camera reveals. Built from the
                // model the level's HumanPlayer declares, on skeleton 0's set (000.iff), the same one the
                // player walks on. It stays invisible until the player dies. Without the spawn's model or
                // the set there is simply no body to show, and death falls back to the frozen view.
                if (_soldiers is { } bodies
                    && playerBodySet is { } bodySet
                    && !string.IsNullOrEmpty(spawn.Model))
                {
                    // A co-op partner's look this level doesn't ship is borrowed from one that does; and so is this
                    // player's own, which it is seen in climbing or fallen as the others see it.
                    bodies.BorrowLevel = level => Borrow(loaded, _resources, level);
                    string? look = TickObserver is Multiplayer.ISessionLook { OwnSkin.Length: > 0 } session
                        ? Game.Multiplayer.PlayerSkins.Of(session.OwnSkin).Model
                        : null;
                    bodies.LoadPlayerBody(loaded, spawn.Model, bodySet, spawn.Position, spawn.Gamma, look);
                }

                EndStage("standing the player up and building its third-person body");

                // Walking is what starting a mission means. The free camera is the debugging tool, so
                // it is the thing you switch to rather than the thing you start in.
                _playerMode = !_options.StartFlying;

                // After a switch of renderer, back where the view was before it.
                ResumePose();
                _logger.Information(
                    "Player",
                    _playerMode
                        ? $"Standing at {candidate.Position}; F1 for the free camera."
                        : $"Free camera; F1 to stand at {candidate.Position}.");

                // The eye, spelled the way the capture flags take it. Walking and flying are supposed to
                // draw the same picture from the same place, and the only way to check that is to put the
                // free camera exactly where the walking eye is -- which needs the eye, not the feet the
                // line above reports, and needs it before a capture run can be scripted around it.
                Camera3D spawnEye = candidate.Camera;
                _logger.Information(
                    "Player",
                    FormattableString.Invariant(
                        $"Eye at -campos {spawnEye.Position.X:F3},{spawnEye.Position.Y:F3},{spawnEye.Position.Z:F3} ")
                        + FormattableString.Invariant(
                            $"-yaw {candidate.Body.Yaw:F4} -pitch {candidate.Body.Pitch:F4}"));
            }
            else
            {
                _logger.Warning(
                    "Player",
                    "Nothing to stand on at the level's spawn, so this mission opens in the free camera.");
            }
        }

        // A title that brings its own player stands it here, in place of the rig above.
        StandTitlePlayer(loaded, levelRoot, scene);
    }

    /// <summary>Turns the menu's backdrop into the mission being played, without loading anything.</summary>
    /// <param name="preview">The backdrop, which must be the mission the player asked for.</param>
    /// <returns><see langword="true"/> when the mission is up and the player is standing in it.</returns>
    /// <remarks>
    /// <para>
    /// The level under the menu is not a picture of the mission — it <em>is</em> the mission, built by
    /// <see cref="MissionPreviewLoader"/> on a worker thread while the player was reading the briefing.
    /// The only reason it was not playable is that it was built into its own private
    /// <see cref="GameLoop"/>, which is exactly what made building it off the render thread safe: its
    /// task tree, its script namespace and its variables are unreachable from the running game, so
    /// nothing in it could ever be ticked. Promotion is the game changing which loop it advances.
    /// </para>
    /// <para>
    /// What that buys is the whole of the load: the packs are parsed, the scene walked, the collision
    /// index built, the terrain surface built and — the single largest cost — every ground tile's detail
    /// map derived, all of it already done and none of it on this thread. What is left is the part that
    /// genuinely cannot leave the render thread, which is the GL uploads, and those are the subject of
    /// the arming that runs while the menu is idle.
    /// </para>
    /// <para>
    /// The outgoing loop and its resource registry are dropped here. That is not housekeeping: a
    /// resource registry holds a level's worth of packs, and <see cref="AdoptPreview"/>
    /// already carries the scar of a leak in the same shape — a world renderer dropped without being
    /// disposed leaked a level's textures per row of the mission list.
    /// </para>
    /// </remarks>
    private bool PromoteBackdrop(MissionPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);

        if (!_missions.TryGet(preview.MissionId, out MissionDescriptor? chosen))
        {
            _logger.Warning("Mission", $"Asked to promote mission {preview.MissionId}, which is not registered.");
            return false;
        }

        var clock = System.Diagnostics.Stopwatch.StartNew();

        // The level is about to be built; it is not yet worth looking at. Everything temporal -- the
        // probe gather, the radiance cache, the resolve's history, the eye -- needs frames rather than
        // load time, so those frames are drawn with the picture withheld, under the entry shot's hold.
        // See GameHost.MissionWarmUp.cs.
        BeginMissionWarmUp(chosen);

        // A mission with an authored intro is promoted straight from the menu with no shot to cover it,
        // so it goes in behind the sequence's card, over the picture the menu held.
        if (!_entry.IsRunning)
        {
            PresentLoadingFrame();
        }

        // The schedule moves across before the incoming loop runs a single frame. A fresh GameLoop builds
        // its own GameClock starting at tick zero, and -lockstep derives its synthetic milliseconds FROM
        // the frame count, so letting the counters restart would make wall-clock time jump backwards.
        GameLoop outgoing = _loop;
        preview.Loop.Clock.AdoptScheduleFrom(outgoing.Clock);

        _loop = preview.Loop;
        _resourceFileSystem = preview.FileSystem;
        _resources = preview.Resources;
        // Built before a change of language, it holds the old one's text.
        MountLanguage(_resources);
        _session.Begin(chosen, preview.LevelRoot);

        // The backdrop stops being drawn lite before BringMissionUp reads the lighting variables: the
        // real-time structures it skipped are built from here, under the warm-up's hold.
        UpdateBackdropProfile();
        _computerOpen = false;
        _computerFlight.Reset();

        // The outgoing loop's level tasks, if it had any. Coming from the front end it has none — the
        // shell is drawn over a backdrop, not over a played mission — but a promotion reached from
        // anywhere else must not leave the previous level's tree alive in a loop nobody advances.
        if (outgoing.Tasks.Root is { } outgoingRoot)
        {
            foreach (GameTask child in outgoingRoot.Children.ToArray())
            {
                if (child is LevelTask level && !level.IsDeleted)
                {
                    outgoing.Tasks.Delete(level);
                }
            }
        }

        BringMissionUp(preview.Mission, preview.LevelRoot, preview.MissionId, promoted: true);

        _logger.Information(
            "Mission",
            $"Promoted the backdrop for mission {preview.MissionId} in {clock.Elapsed.TotalMilliseconds:F1} ms "
                + "on the render thread.");
        return true;
    }

    private void ReleasePlayedMission()
    {
        ReleaseTaskPresenters();

        // First, because both are worker threads tracing the level that is going and they hold the whole
        // of what they trace: the lightmap bake's texel maps, triangles and BVH run to a few hundred
        // megabytes, and nothing here released them. A bake was only ever cancelled by the NEXT one
        // starting, so leaving a level left every core path-tracing a level nobody was in -- which is
        // what the new level then had to load against -- and a session that entered a handful of missions
        // carried a handful of those at once. The heap stood at one and a half gigabytes of lightmap
        // texels alone by the sixth.
        _baked.Abandon();

        _computerOpen = false;
        _computerFlight.Reset();
        _session.ResetOutcome();
        _mapComputer?.Dispose();
        _mapComputer = null;
        _mapComputerDevice?.Dispose();
        _mapComputerDevice = null;
        _tacticalComputer?.Dispose();
        _tacticalComputer = null;
        _messages?.Dispose();
        _messages = null;
        _computerHighlights = null;
        _mission.ReleaseRuntimes();
        _hosted.Audio.ForgetCameras();
        _cutsceneSounds.StopAll();
        _session.Mission = null;

        // The level's own sun goes with the level. It is read on load only when it is not already set --
        // which is how a promoted backdrop hands its own in -- so a sun left standing here was taken for
        // one that had been handed in, and the next mission was lit and BAKED by the one before it. The
        // backdrop the menu puts up next sets its own in AdoptPreview, so nothing that needs one is left
        // without.
        _timeOfDay.Sun = null;
        _timeOfDay.EffectiveSun = null;
        _earCamera = null;

        // And so does what the level said about its own light. Left standing, the next title's mission
        // would be lit by the last one's answer -- and for Project I.G.I. that means its sky model would
        // stay switched off after a visit to the sequel.
        _deferredLighting = null;
        _timeOfDay.BoundSun = null;

        // Before a single mesh is destroyed. The backend has assembled its own copy of the level from
        // the frames it drew -- the traceable world, and the material records a hit is looked up in --
        // and destroying the meshes one by one tells it each has gone without telling it the level has.
        // Left to accumulate, the records ran up to their ceiling over half a dozen mission entries and
        // the traced world went on holding structures nothing would place again.
        _device.ReleaseWorld();

        // And the residency walk, which holds the world renderer being disposed below and is stepped by
        // the idle loop for as long as a backdrop is up. Left standing it keeps calling MakeResident on
        // a disposed renderer -- reliably, on the first frame after a title switch, because that path
        // releases the level while the menu is still arming the next backdrop.
        _arming = null;

        _sky?.Dispose();
        _terrain?.Dispose();
        _world?.Dispose();
        _soldiers?.Dispose();
        _menuBackdrop.ReleaseSoldiers();
        _heldWeapon?.Dispose();
        _gunPickupRenderer?.Dispose();
        _hud?.Dispose();
        _remasteredHud?.Dispose();
        _debugOverlay?.Dispose();
        _marks?.Dispose();
        _tracers?.Dispose();
        _effects.Release();
        _cameraStatusLights?.Dispose();
        _grenadeRenderer?.Dispose();
        _missileRenderer?.Dispose();
        _arms?.Dispose();
        _titleArms?.Dispose();
        _sky = null;
        _rain?.Dispose();
        _rain = null;
        _storm?.Dispose();
        _storm = null;
        _snow?.Dispose();
        _snow = null;
        ReleaseShelterField();
        _terrain = null;
        _world = null;
        _soldiers = null;
        _heldWeapon = null;
        _gunPickupRenderer = null;
        _hud = null;
        _remasteredHud = null;
        _debugOverlay = null;
        _marks = null;
        _tracers = null;
        _cameraStatusLights = null;
        _grenadeRenderer = null;
        _missileRenderer = null;
        _arms = null;
        _titleFeatures = null;
        _titleArms = null;
        _playerMode = false;
        _scene = null;
        _mission.Terrain = null;
        _mission.Ground = null;
        _mission.ReleaseWorld();
        _loadedMissionForWeapon = null;

        // A vehicle's engines and guns must not keep sounding into the next run.
        _vehicleSounds?.StopAll();
        _vehicleSounds = null;

        if (_session.LevelRoot is not null && !_session.LevelRoot.IsDeleted)
        {
            // Deleting a task does not unexport its names (TaskTree.Delete), and a restart reuses this loop's
            // variables. Left bound, AreaActivate_175.nActive, Car_361.isExploded and the rest answered for the
            // run being left until the new level's runtimes rebound them -- after the conditional containers had
            // already been decided. Mission 7's real T80 (ConditionalContainer_700, which latches itself through
            // this.isRun) came up live at tick 0 whenever the player restarted from inside its trigger area.
            // Unbound, the new level starts from the same zeros a first load does.
            _loop.Variables.UnexportTree(_session.LevelRoot);
            _loop.Tasks.Delete(_session.LevelRoot);
        }
        _session.LevelRoot = null;

        // Back to the front end, so back to the menu theme. Every voice the level started goes with it:
        // an ambience bed outliving its level would follow the player into the menus.
        _audioOut.Sounds?.StopAll();
        _audioOut.Music?.Play(MusicPlayer.MenuMusic);
    }

    // The presenter for the player's hand, which follows whatever weapon ends up in it (Igi1PlayerSetup.Carry hands
    // the player everything the mission declares).
    private IHeldWeaponPresenter? CreateHeldWeapon()
    {
        IHeldWeaponPresenter? presenter = _presentation.Presenters.Create<ViewModelPresentationRequest, IHeldWeaponPresenter>(ViewModelRequest());
        if (presenter is not null)
        {
            presenter.Materials = _materials;
        }

        return presenter;
    }

    // The all-weapons cheat put weapons in the player's hands (Igi1MissionTick tops the inventory up).
    private void ShowCheatWeapons()
    {
        // A level that declared no loadout never built a presenter for the hand, so the first granted
        // weapon would be carried but never drawn.
        if (_heldWeapon is null && HostedPlayer?.View is not null
            && _presentation.Presenters.Create<ViewModelPresentationRequest, IHeldWeaponPresenter>(ViewModelRequest()) is { } presenter)
        {
            presenter.Materials = _materials;
            _heldWeapon = presenter;
        }
    }

    /// <summary>The three things the small world-effect presenters are built from.</summary>
    private EffectsPresentationRequest EffectsRequest() => new(_device, _resources, _logger);

    /// <summary>The four things every first-person and projectile presenter is built from.</summary>
    /// <remarks>
    /// A helper because five call sites want the same value and the magic-object table is not readable
    /// until a mission has loaded, so it cannot be cached beside the provider.
    /// </remarks>
    private ViewModelPresentationRequest ViewModelRequest() =>
        new(_device, _resources, _mission.MagicObjects, _logger, _work);

    /// <summary>The mission's side of one pass of the loop: the world, the player and the level's state, ticked.</summary>
    /// <param name="state">The frame loop's state.</param>
    /// <param name="outcome">What the loop's frame did; only a tick advances anything.</param>
    private void TickMission(FrameLoopState state, FrameOutcome outcome)
    {
        _missionHost.State = state;

        // A mission that serves a session itself (I.G.I.) is observed around its tick; a title's own player is observed around
        // its own (GameHost.TitlePlayer), so neither is seen twice.
        bool observed = outcome == FrameOutcome.Tick && _hosted is Game.Multiplayer.IReplicatedTitle && TickObserver is not null;
        if (observed)
        {
            TickObserver!.BeforeTitleTick(_hosted);
        }

        _hosted.Tick.Run(outcome == FrameOutcome.Tick);
        if (observed)
        {
            TickObserver!.AfterTitleTick(_hosted);
        }
    }

    /// <summary>What the player's rounds struck, heard and seen: the impact sounds, the sparks and dust, and the rockets' trails.</summary>
    private void PresentPlayerImpacts()
    {
        IHostedPlayer player = HostedPlayer!;

        // What this tick's ballistic rounds landed on -- dirt, steel or flesh, out of the
        // material table, where the round arrived rather than where it was fired.
        _hosted.Sounds.PlayPlayerImpacts();

        // The same impacts, seen: sparks and dust composed from the struck material.
        // Ticked before spawning so a new burst draws its birth frame unmoved.
        _effects.Tick();

        // The original's own effects, on the same rhythm: the dust and chips step, and the
        // blood droplets in the air are traced against the world, marking whatever they
        // land on. Ticked before this tick's impacts spawn, so a new puff is drawn where
        // it was born.
        _hosted.Impacts.Tick();

        SpawnBallisticImpactParticles();
        _hosted.Impacts.SpawnPlayerImpacts();

        // The launcher rockets in the air lay their smoke trail, one column of puffs per
        // segment they crossed this tick.
        _effects.SpawnMissileTrails(player.Missiles, _mission.VehicleMissiles);
    }

    /// <summary>
    /// The world's effects stepping on where the player's own update does not step them: under the death camera, or while
    /// another game's player stands in for the mission's.
    /// </summary>
    /// <remarks>
    /// A blast that killed the player burns out, and its debris and smoke settle, under the death camera as they do in the
    /// original; and a camera another game's player shoots out burns as one the mission's own player shoots. Stepping them only
    /// from the player's update froze them on screen.
    /// </remarks>
    private void StepWorldEffects()
    {
        _effects.Tick();
        _hosted.Impacts.Tick();
    }

    /// <summary>Lights the pickups, grenades and missiles by the level's light, as the original lights any unbaked model.</summary>
    private void LightLooseModels(FixedFunctionSun? sun)
    {
        if (_gunPickupRenderer is { } pickups)
        {
            pickups.Sun = sun;
        }

        if (_grenadeRenderer is { } grenades)
        {
            grenades.Sun = sun;
        }

        if (_missileRenderer is { } missiles)
        {
            missiles.Sun = sun;
        }
    }
}
