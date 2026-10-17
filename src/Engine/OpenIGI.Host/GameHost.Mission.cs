using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Core.Threading;
using OpenIGI.Engine.Audio;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ai;
using OpenIGI.Formats.World;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Level;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Game;
using OpenIGI.Host.Infrastructure;
using OpenIGI.Host.Lighting;
using OpenIGI.Host.Menu;
using OpenIGI.Presentation;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Presentation.Igi1;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;
using OpenIGI.Scripting.Runtime;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.Menus;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Host;

internal sealed partial class GameHost
{
    /// <summary>Test-only outro forcing writes re-applied on every logic tick.</summary>
    private List<(string Name, string Value)> _testOutroTickWrites = [];
    private void StartMission(int id)
    {
        if (!_missions.TryGet(id, out MissionDescriptor? chosen))
        {
            _logger.Warning("Mission", $"Asked to start mission {id}, which is not registered.");
            return;
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
        _activeLevelRoot = levelRoot;
        _activeMission = chosen;

        // The menu's lite profile ends here, before anything below reads the variables it overrides.
        UpdateBackdropProfile();
        _missionResultHandled = false;
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
            _missions, _resourceFileSystem, _resources, levelLoader, _logger, terrainLoader, _soundBank,
            _titleRuntime.Formats);
        LoadedMission loaded = missionLoader.Load(id);

        // Evaluate the level's ConditionalContainer conditions against the initial playable state, so the
        // scene, spawn and soldier walks below skip the subtrees the original leaves unregistered (the
        // intro/outro cut-scene props and actors). Runs after the tree is built and its variables exported,
        // and before anything walks it.
        new ConditionalContainerGate(_loop.Variables, _logger).Prepare(levelRoot);

        BringMissionUp(loaded, levelRoot, id, promoted: false);
    }

    /// <summary>Resolves a retail directory without assuming the install's casing matches the script.</summary>
    private static string? ResolveDirectoryCaseInsensitive(string root, string relativePath)
    {
        string current = Path.GetFullPath(root);
        foreach (string part in relativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries))
        {
            string exact = Path.Combine(current, part);
            if (Directory.Exists(exact))
            {
                current = exact;
                continue;
            }

            if (!Directory.Exists(current))
            {
                return null;
            }

            string? match = Directory.EnumerateDirectories(current)
                .FirstOrDefault(path => string.Equals(
                    Path.GetFileName(path), part, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                return null;
            }

            current = match;
        }

        return current;
    }

    /// <summary>Gets the along-track extent retail uses to space cars in a Train consist.</summary>
    /// <summary>Gets a wheel model's rolling radius in world units, for the wheel animation.</summary>
    private static float CutsceneWheelRadius(LoadedMission mission, string name)
    {
        return mission.TryGetModel(name, out MefModel? model) && model is not null
            ? WorldScene.WheelRadius(model)
            : 0f;
    }

    private static float CutsceneModelLength(LoadedMission mission, string name)
    {
        if (!mission.TryGetModel(name, out MefModel? model) || model is null)
        {
            return 0f;
        }

        return WorldScene.TrainCouplingDistance(model);
    }

    /// <summary>Gets the retail origin fraction between a Train model's two coupling vertices.</summary>
    private static float CutsceneModelPivot(LoadedMission mission, string name)
    {
        if (!mission.TryGetModel(name, out MefModel? model) || model is null)
        {
            return 0f;
        }

        return WorldScene.TrainCouplingPivotFraction(model);
    }

    /// <summary>Chooses the engine-unit occupant used by retail AreaActivate volumes.</summary>
    /// <param name="playerUnits">The player's body position in engine units.</param>
    /// <param name="cutsceneViewMetres">
    /// The authored cutscene camera in renderer metres when it owns the frame; otherwise null.
    /// </param>
    /// <param name="freeViewMetres">
    /// The diagnostic free camera in renderer metres. It is only a fallback when no player body exists;
    /// moving a debug camera must never satisfy gameplay gates such as Mission 4's extraction volume.
    /// </param>
    internal static Vector3d? AreaActivationOccupant(
        Vector3d? playerUnits,
        Vector3d? cutsceneViewMetres,
        Vector3d? freeViewMetres = null)
    {
        if (cutsceneViewMetres is { } cutscene)
        {
            return cutscene * WorldScene.UnitsPerMetre;
        }

        if (playerUnits is { } player)
        {
            return player;
        }

        return freeViewMetres is { } free
            ? free * WorldScene.UnitsPerMetre
            : null;
    }

    /// <summary>
    /// Gets the player-body projection that preserves occupancy while a cutscene camera drives activation.
    /// </summary>
    /// <param name="playerUnits">The player's body position in engine units.</param>
    /// <param name="cutsceneViewMetres">
    /// The authored cutscene camera in renderer metres when it owns the frame; otherwise null.
    /// </param>
    /// <remarks>
    /// Retail queries humans rather than cameras, so the stationary body never stops occupying its
    /// volume when a cinematic starts. Without this, Mission 4's latch-less outro gate
    /// (<c>AreaActivate_156.nActive</c>) drops as soon as the <c>CutScene_2222</c> camera leaves the
    /// extraction volume, aborting the cutscene and refiring it every other tick. The camera remains
    /// the primary projection (Mission 14 streams tunnel sections off it), while the body is an
    /// alternate projection of that same logical player rather than another occupant.
    /// </remarks>
    internal static Vector3d? AreaActivationExtraOccupant(
        Vector3d? playerUnits,
        Vector3d? cutsceneViewMetres) =>
        cutsceneViewMetres is not null && playerUnits is { } player ? player : null;

    /// <summary>Feeds the desktop host's player projections and liveness into authored areas.</summary>
    internal static void TickAreaActivations(
        MissionObjectiveRuntime? objectives,
        Vector3d? playerUnits,
        int playerTeam,
        Vector3d? cutsceneViewMetres,
        Vector3d? freeViewMetres,
        bool playerAlive) =>
        objectives?.TickLevelState(
            AreaActivationOccupant(playerUnits, cutsceneViewMetres, freeViewMetres),
            playerTeam,
            AreaActivationExtraOccupant(playerUnits, cutsceneViewMetres),
            playerAlive);

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

    /// <summary>
    /// Handles the retail transient-object cleanup native without giving it ownership of cinematic state.
    /// </summary>
    /// <param name="name">The native the script called.</param>
    /// <param name="clearTransientProjectiles">What to run when it is the cleanup one.</param>
    /// <returns>The script's answer, or null when this native is not the one.</returns>
    /// <remarks>
    /// <c>IGI.EXE!Game_CutsceneDelete</c> walks the mine/grenade/missile registry, not the QVM
    /// <c>CutScene</c> registry. Mission 3 calls it while scene 700 is starting, so completing scene 700
    /// here would immediately satisfy the authored mission-complete expression.
    /// </remarks>
    internal static QvmValue? TryHandleCutsceneCleanupNative(string name, Action clearTransientProjectiles)
    {
        ArgumentNullException.ThrowIfNull(clearTransientProjectiles);
        if (!string.Equals(name, "Game_CutsceneDelete", StringComparison.Ordinal))
        {
            return null;
        }

        clearTransientProjectiles();
        return QvmValue.FromInt32(1);
    }

    private void ClearTransientCutsceneObjects()
    {
        _player?.Grenades.Clear();
        _player?.Missiles.Clear();
        _vehicleMissiles.Clear();
        _player?.M203.Clear();
    }
    /// <summary>Advances missiles fired by authored vehicles against the player's own body.</summary>
    /// <summary>Puts the soldiers (with their doors and alarms) and the player into the level's walk.</summary>
    /// <remarks>
    /// Retail updates <c>HumanPlayer_0</c> where the walk reaches it inside <c>ConditionalContainer_560</c>, late in
    /// <c>Dynamic</c>: its HumanView, Shadow, then HumanPlayerInput, whose CHANNELS packet moves it, then the
    /// binoculars and the gun. A level without a player task keeps
    /// updating the player after the walk.
    /// </remarks>
    private void AttachActorsToLevelWalk(GameTask levelRoot)
    {
        _soldiersInWalk = false;
        _playerInWalk = false;
        if (_mission.Cutscenes is not { } cutscenes)
        {
            return;
        }

        LevelUpdateWalker walker = cutscenes.Walker;
        if (_soldiers is { } soldiers)
        {
            soldiers.Simulation.AttachTo(walker);
            _soldiersInWalk = true;
        }

        if (PlayerSpawn.FindGameplayTask(levelRoot) is { } playerTask)
        {
            walker.Bind(playerTask, task =>
            {
                UpdatePlayerTask();
                walker.RunChildren(task);
            });
            _playerInWalk = true;
        }
    }

    /// <summary>The player's update: input, movement and the weapon, once per tick.</summary>
    /// <remarks>
    /// A dead player is in locomotion state 47, whose per-tick is not the walking one -- so the body stops being
    /// stepped rather than being stepped with no input. Without this a corpse walks around. What the update set
    /// in motion -- sounds, effects, detonations -- is resolved after the walk while <see cref="_playerTicked"/>.
    /// </remarks>
    private void UpdatePlayerTask()
    {
        if (TickTitlePlayer())
        {
            return;
        }

        if (_playerTicked
            || _mission.Cutscenes?.IsPlaying == true
            || !_playerMode
            || _player is not { } player
            || _playerDead
            || _missionFailedMenu)
        {
            return;
        }

        _playerTicked = true;

        // The soldiers ahead of the player in the tree have moved this tick; the wall sweep meets them there.
        _soldiers?.Simulation.PublishCollisionBodies();
        player.InputProfile = _configuration.Active;
        GrantCheatWeapons(player);
        if (_computerOpen)
        {
            player.TickMapComputer();
        }
        else
        {
            player.Tick(ViewInput, _ground);

            // PlayerCamera advances its interaction registry before it resolves this tick's activate target.
            // Do not advance the same registry again after the walk.
            _interactionsTickedByPlayer = true;
        }

        TickVehicleMissiles();
        CheckRunOver();

        // Grenade physics runs inside PlayerCamera.Tick. The grenade's AI events reach the soldiers' next
        // update, as retail's grenade task -- a runtime task at the end of Dynamic, after every soldier -- does.
        NotifyGrenadeAiEvents();
    }

    /// <summary>The player as a soldier sees it at the moment the soldier updates.</summary>
    /// <remarks>
    /// In the fly-through the eye is the CAMERA's: guards otherwise watch an immobile body at the spawn. The feet
    /// are what combat aims at (sub_451380). A dead player is no longer a target (the detection scan
    /// 0x489750 unregisters it), and neither is one hidden behind a cinematic.
    /// </remarks>
    private SoldierEnemy SoldierEnemyNow()
    {
        bool body = _playerMode && _player is not null;
        Vector3d eye = body ? _player!.Body.Eye : _camera?.Position ?? _player?.Body.Eye ?? default;
        Vector3d feet = body ? _player!.Body.Position : eye;
        return new SoldierEnemy(eye, !_playerDead && _mission.Cutscenes?.IsPlaying != true, feet, body && _player!.IsCrouched);
    }

    /// <summary>The player as an area counts it at the moment the area updates.</summary>
    /// <remarks>
    /// During an authored cinematic the occupant is the cutscene camera rather than the stationary player body; a
    /// diagnostic free camera must not impersonate a player that exists (Mission 4's AreaActivate_156 would start
    /// the outro as the camera follows Bell 800).
    /// </remarks>
    private AreaPlayer AreaPlayerNow()
    {
        Vector3d? cutsceneView = _mission.Cutscenes?.Frame?.Camera.Position;
        Vector3d? freeView = !_playerMode ? _camera?.Position : null;
        return new AreaPlayer(
            AreaActivationOccupant(_player?.Body.Origin, cutsceneView, freeView),
            _soldiers?.Simulation.PlayerTeam ?? 0,
            AreaActivationExtraOccupant(_player?.Body.Origin, cutsceneView),
            !_playerDead);
    }

    private void TickVehicleMissiles()
    {
        if (_player is not { } player)
        {
            return;
        }
        bool PlayerProbe(Vector3d from, Vector3d to, out HumanHit hit)
        {
            hit = default;
            if (_playerHealth.IsDead)
            {
                return false;
            }
            if (_soldiers?.Simulation.TryHitPlayerBody(from, to, player.Body.Position, player.Body.Yaw, out hit) == true)
            {
                return true;
            }
            Vector3f facing = new(-MathF.Sin(player.Body.Yaw), MathF.Cos(player.Body.Yaw), 0f);
            return HumanHitTest.TryCast(from, to, player.Body.Position, facing, pose: null, out hit);
        }
        MissileWorldTraceCast? walls = _worldTrace is { } trace
            ? (LiveMissile missile, Vector3d from, Vector3d to, out WorldTraceHit hit) =>
            {
                bool found = trace(
                    new WorldTraceRequest(
                        from,
                        to,
                        WorldTraceMask.World,
                        IgnoredOwner: missile.CollisionOwner),
                    out hit);
                return found;
            }
            : _effectWalls is { } effectWalls
                ? (LiveMissile _, Vector3d from, Vector3d to, out WorldTraceHit hit) =>
                {
                    bool found = effectWalls(from, to, out WorldRayHit rayHit);
                    hit = found
                        ? WorldTraceHit.FromRayHit(
                            new WorldTraceRequest(from, to, WorldTraceMask.World),
                            rayHit,
                            WorldTraceHitKind.StaticObject)
                        : default;
                    return found;
                }
                : null;
        _vehicleMissiles.TickWithMissileContext(walls, PlayerProbe);
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
    /// <c>_playerTerrain</c>, the interactions have to exist before the brains are animated, and the
    /// weapon rosters have to load before <c>humanplayer.qvm</c> runs (weapons.md section 8).
    /// </param>
    private void BringMissionUp(LoadedMission loaded, LevelTask levelRoot, int id, bool promoted)
    {
        BeginStage();
        LevelUpdateWalker walker = new(levelRoot);
        MissionBuild build = new(walker, _title.Services, loaded, levelRoot, id, promoted);
        (_missionSystems ??= MissionSystems()).BuildAll(build);
    }

    // The systems a mission is brought up by, in order; see MissionSystemNames.
    private LevelSystems<MissionBuild>? _missionSystems;

    /// <summary>Makes the host's mission systems, in the order a mission is brought up.</summary>
    /// <returns>A fresh list.</returns>
    private LevelSystems<MissionBuild> MissionSystems() => new LevelSystems<MissionBuild>()
        .Add(new LevelSystem<MissionBuild>(MissionSystemNames.Sounds, MissionSounds))
        .Add(new LevelSystem<MissionBuild>(MissionSystemNames.Cutscenes, MissionCutscenes))
        .Add(new LevelSystem<MissionBuild>(MissionSystemNames.Objectives, MissionObjectives))
        .Add(new LevelSystem<MissionBuild>(MissionSystemNames.Computer, MissionComputer))
        .Add(new LevelSystem<MissionBuild>(MissionSystemNames.Scene, MissionScene))
        .Add(new LevelSystem<MissionBuild>(MissionSystemNames.Soldiers, MissionSoldiers))
        .Add(new LevelSystem<MissionBuild>(MissionSystemNames.Interactions, MissionInteractions))
        .Add(new LevelSystem<MissionBuild>(MissionSystemNames.Cameras, MissionCameras))
        .Add(new LevelSystem<MissionBuild>(MissionSystemNames.NavGraphs, MissionNavGraphs))
        .Add(new LevelSystem<MissionBuild>(MissionSystemNames.LightBakes, MissionLightBakes))
        .Add(new LevelSystem<MissionBuild>(MissionSystemNames.NavigationAi, MissionNavigationAi))
        .Add(new LevelSystem<MissionBuild>(MissionSystemNames.LevelWalk, MissionLevelWalk))
        .Add(new LevelSystem<MissionBuild>(MissionSystemNames.Picture, MissionPicture))
        .Add(new LevelSystem<MissionBuild>(MissionSystemNames.Player, MissionPlayer))
        .Add(new LevelSystem<MissionBuild>(MissionSystemNames.Adornments, MissionAdornments));

    // A system ran before the one it depends on.
    private static InvalidOperationException MissingSystem(string name) =>
        new($"The mission system '{name}' has not run.");

    /// <summary>The mission's terrain, its sounds and its score.</summary>
    private void MissionSounds(MissionBuild b)
    {
        LoadedMission loaded = b.Loaded;
        bool promoted = b.Promoted;

        // The mission's terrain, first thing, because three separate passes below close over it and it
        // used to be assigned a thousand lines after the first of them. A cold start therefore built the
        // security cameras' sight trace and the lightmap bake's occlusion cast against whatever the
        // FIELD still held -- null, since the last mission's unload cleared it -- so a camera saw
        // through a hillside and a bake let the sun through one. Nothing between here and there reads
        // it, and the value depends on nothing but the argument.
        //
        // Terrain stays optional: Mission 14 is entirely inside placed building and tunnel models, whose
        // collision floors are enough to walk on even though its terrain files are not live data.
        _playerTerrain = loaded.Terrain is { IsUsable: true } standable ? standable : null;

        // The mission's sounds and its score. Both routes into a mission come through here -- a cold
        // start and a backdrop being promoted -- and only the cold one passes the mission loader, so
        // the mount has to happen on this side or a promoted mission plays nothing at all.
        //
        // A cold start emptied the bank in StartMission, before the loader mounted into it. A PROMOTED
        // backdrop never went near StartMission, so without this the bank still holds the mission the
        // player last played -- and since the bank keeps the FIRST mount of a name, the mount below
        // cannot replace it. Every level ships its own sounds/game_music.wav, so what leaked through was
        // the previous level's score playing over the new one.
        //
        // Tearing the bank down costs a re-read of the menu archive and the common directory: 15 ms
        // warm, 23 ms cold for all 375 sounds, which is the same bill the cold path already pays and is
        // spent here under the going-in card rather than during play.
        if (promoted)
        {
            ResetSoundsForMission();
        }

        MountMissionSounds(loaded);
        _music?.Play(MusicPlayer.GameMusic);
    }

    /// <summary>The mission's text and the cutscene runtime, with the natives the level's cutscenes call.</summary>
    private void MissionCutscenes(MissionBuild b)
    {
        LoadedMission loaded = b.Loaded;
        LevelTask levelRoot = b.LevelRoot;

        MenuStringTable missionStrings = new(_resources);

        // Mission text is addressed by its own key, not by the menu table's LOCAL:<language>/<key> —
        // see MissionStringResolver. Everything that resolves mission data goes through this one.
        MissionStringResolver missionText = new(_resources, missionStrings);

        // The objective runtime owns the message slots but is built after the cutscene runtime, which
        // needs it for StatusMessageScreen_ClearMessages. The native reads the build's rather than the
        // field so a clear during construction cannot reach the previous mission's messages.
        _mission.Cutscenes = new CutsceneRuntime(
            levelRoot,
            _loop.Variables,
            (name, arguments) =>
            {
                if (name == GameMusicNatives.EnableNative)
                {
                    if (_music is not null)
                    {
                        _music.Enabled = true;
                    }
                    return QvmValue.FromInt32(1);
                }
                if (name == GameMusicNatives.DisableNative)
                {
                    if (_music is not null)
                    {
                        _music.Enabled = false;
                    }
                    return QvmValue.FromInt32(1);
                }
                if (name == "MenuManager_SetEnabled")
                {
                    // Cutscene start/stop expressions own this native in retail.  The menu manager
                    // lives in the shared loop task tree, so update its live script context rather
                    // than returning success while leaving input enabled during cinematics.
                    MenuManagerTask? manager = FindMenuManager();
                    if (manager?.ScriptContext is { } context && arguments.Count >= 1)
                    {
                        context.IsEnabled = arguments.GetBoolean(0);
                    }

                    return QvmValue.FromInt32(1);
                }

                if (name == "StatusMessageScreen_ClearMessages")
                {
                    // 0x485AD0: clears every slot and returns true.
                    b.Objectives?.ClearMessages();
                    return QvmValue.FromInt32(1);
                }

                if (TryHandleCutsceneCleanupNative(name, ClearTransientCutsceneObjects) is { } cleanup)
                {
                    return cleanup;
                }

                return null;
            },
            SetCutsceneSound,
            modelLength: name => CutsceneModelLength(loaded, name),
            modelPivot: name => CutsceneModelPivot(loaded, name),
            vehiclePhysics: _vehiclePhysics,
            terrain: loaded.Terrain,
            logger: _logger,
            vehicleObstacleHit: (task, origin, end) =>
            {
                if (_worldSolids?.TryCastIgnoring(
                        task,
                        origin,
                        end,
                        out WorldRayHit hit,
                        out GameTask? obstacle,
                        out string obstacleLabel) == true
                    && obstacle is not null)
                {
                    return (obstacle, hit, obstacleLabel);
                }

                return null;
            },
            vehicleObstacleReleased: obstacle =>
                _mission.Doors?.Doors.FirstOrDefault(door => ReferenceEquals(door.Task, obstacle))?.State.IsFullyOpen == true,
            walker: b.Walker);
        bool collectDiagnostics = _logger.IsEnabled(LogLevel.Information);
        if (collectDiagnostics && levelRoot.Tree.FromId(1608) is DeclaredTask levelTimer)
        {
            string parameters = string.Join(
                ", ",
                levelTimer.Type.Parameters.Select(
                    (parameter, index) => $"{parameter.Name}={levelTimer.GetValue(index) ?? "<null>"}"));
            _logger.Information("Mission.Timer", $"LevelTimer_1608 {parameters}");
        }
        if (collectDiagnostics && levelRoot.Tree.FromId(161) is DeclaredTask activation)
        {
            string parameters = string.Join(
                ", ",
                activation.Type.Parameters.Select(
                    (parameter, index) => $"{parameter.Name}={activation.GetValue(index) ?? "<null>"}"));
            _logger.Information("Mission.Timer", $"AreaActivate_161 {parameters}");
        }

        b.MissionText = missionText;
    }

    /// <summary>The objective runtime, the intro skipped when cinematics are off, and the test captures' script writes.</summary>
    private void MissionObjectives(MissionBuild b)
    {
        if (_mission.Cutscenes is null)
        {
            throw MissingSystem(MissionSystemNames.Cutscenes);
        }

        LevelTask levelRoot = b.LevelRoot;
        MissionStringResolver missionText = b.MissionText!;

        // LevelFlow exports eDifficulty straight from the active profile (0x488ED0), unclamped, and the
        // number-row keys as Key0..9.isPressed. -difficulty overrides the profile here as it does for the AI.
        MissionObjectiveRuntime objectives;
        _mission.Objectives = b.Objectives = objectives = new MissionObjectiveRuntime(
            levelRoot,
            _loop.Variables,
            missionText.Get,
            _mission.Cutscenes.TryInvokeNative,
            difficulty: (int?)_options.Difficulty ?? _configuration.Active?.Difficulty ?? 1,
            isKeyDown: key => _window.Input.Keyboard.IsDown(key));

        // The areas and edit variables update inside the level's one walk, where the tree places them.
        objectives.AttachTo(
            _mission.Cutscenes.Walker,
            _mission.Cutscenes.VehicleOccupants,
            () => _soldiers?.Simulation.LiveOccupants() ?? [],
            AreaPlayerNow);

        // Cinematics are opt-in while their retail parity work is in progress. Do not tear the runtime
        // out: the level expressions still use its native surface, and mission outros may activate it
        // later. Instead take the exact path Escape takes in retail. The objective flow observes the
        // break key, closes the intro container, and the cutscene tick applies that transition before
        // the first gameplay frame, so neither its camera nor its ConditionalSounds leak through.
        if (!_options.PlayCutscenes && _mission.Cutscenes.IsPlaying)
        {
            _mission.Cutscenes.RequestBreak();
            _mission.Objectives.TickLevelState();
            _mission.Cutscenes.Tick();
            _mission.Cutscenes.FinishExpressionTick();
            StopCutsceneSounds();
        }

        // Same native edge as Escape (LevelFlow_GetBreakCutSceneKey). Used by the
        // agent playtest so lockstep/-frames can skip the intro without a HITL key.
        if (string.Equals(
                Environment.GetEnvironmentVariable("OPENIGI_BREAK_CUTSCENE"),
                "1",
                StringComparison.Ordinal)
            && _mission.Cutscenes.IsPlaying)
        {
            _mission.Cutscenes.RequestBreak();
            _mission.Objectives.TickLevelState();
            _mission.Cutscenes.Tick();
            _mission.Cutscenes.FinishExpressionTick();
            StopCutsceneSounds();
        }

        // Test-only capture switch: exercise an authored outro through its real QVM gate rather than
        // injecting a scene. OPENIGI_TEST_OUTRO_WRITES is a comma list of script writes, e.g.
        // "EditVariable_199.nValue=1,AreaActivate_5.nActive=1"; the same writes are re-applied on
        // every logic tick while the run lasts, so gates the level scripts reset stay satisfied.
        string? testOutroWrites = Environment.GetEnvironmentVariable("OPENIGI_TEST_OUTRO_WRITES");
        _testOutroTickWrites = TestOutroWrites(testOutroWrites);
        if (_testOutroTickWrites.Count > 0)
        {
            foreach ((string assignmentName, string assignmentValue) in _testOutroTickWrites)
            {
                double parsed = double.Parse(assignmentValue, System.Globalization.CultureInfo.InvariantCulture);
                if (parsed != Math.Floor(parsed))
                {
                    _logger.Warning(
                        "Mission",
                        $"Test capture write {assignmentName}={assignmentValue} is not an integer; "
                            + $"script variables carry ints, so it truncates to {(int)parsed}.");
                }

                _logger.Information("Mission", $"Test capture write: {assignmentName}={assignmentValue}.");
                _loop.Variables.TryWrite(assignmentName, QvmValue.FromInt32((int)parsed));
            }

            _mission.Cutscenes.RequestBreak();
            _mission.Cutscenes.Tick();
            _mission.Objectives.TickLevelState();
            _mission.Objectives.TickAfterLevelState();
            _mission.Cutscenes.FinishExpressionTick();
            _logger.Information("Mission", "Test capture outro forcing active.");
        }
    }

    /// <summary>The map computer, its highlights, the tactical computer and the comms cards.</summary>
    private void MissionComputer(MissionBuild b)
    {
        LoadedMission loaded = b.Loaded;
        LevelTask levelRoot = b.LevelRoot;
        MissionStringResolver missionText = b.MissionText!;

        _computerHighlights = _presentation.CreateComputerHighlights(new ComputerHighlightRequest(levelRoot, _loop.Variables));
        _mapComputer = _presentation.CreateMapComputer(
            new MapComputerPresentationRequest(_device, _resources, _logger, missionText.Get));
        if (_mapComputer is not null && !_mapComputer.Load(_fileSystem))
        {
            _mapComputer.Dispose();
            _mapComputer = null;
        }

        _mapComputerDevice = new MapComputerDeviceRenderer(_device, _resources, _logger);
        if (!_mapComputerDevice.Load(loaded))
        {
            _mapComputerDevice.Dispose();
            _mapComputerDevice = null;
        }

        // Both presentations are built, and r_computer_tactical decides which is drawn — see
        // GameHost.TacticalComputer. The retail pair above needs the installation's own archives; this
        // one needs only the shipped faces, so a mission whose computer.res is missing still has a map.
        _tacticalComputer = _presentation.CreateTacticalComputer(
            new MapComputerPresentationRequest(_device, _resources, _logger, missionText.Get));
        if (_tacticalComputer is not null && !_tacticalComputer.Load())
        {
            _tacticalComputer.Dispose();
            _tacticalComputer = null;
        }

        // The comms cards. Built here rather than beside the HUD below, because the messages belong to the
        // mission rather than to the loadout: a level whose player carries nothing still sends them.
        _messages?.Dispose();
        _messages = new StatusMessageRenderer(_device, _logger);
        if (!_messages.Load())
        {
            _messages.Dispose();
            _messages = null;
        }

        // The walls. Built here because two consumers want it: the player's collision, and the
        // soldiers' ground probe - a guard's declared Position is only where it starts, and without
        // the settle the retail integrator performs, floor-authored spawns bury the body to the waist.
        //
        // A promoted backdrop arrives with all three already built on a worker: the scene walk, the
        // world renderer and the collision index are exactly what AdoptPreview put up to draw the menu
        // over, and they are the same level.
        EndStage("objectives, highlights and the map computer");
    }

    /// <summary>The scene walk, the world renderer and the collision index; the vehicles on it, what can be destroyed, and the weather.</summary>
    private void MissionScene(MissionBuild b)
    {
        if (_mission.Cutscenes is null)
        {
            throw MissingSystem(MissionSystemNames.Cutscenes);
        }

        LoadedMission loaded = b.Loaded;
        LevelTask levelRoot = b.LevelRoot;
        int id = b.Id;
        bool promoted = b.Promoted;

        ObjectCollisionIndex solids;
        IWorldPresenter world;
        WorldScene scene;
        if (promoted)
        {
            solids = _worldSolids ?? throw new InvalidOperationException(
                "A promoted mission must arrive with the backdrop's collision index already built.");
            world = _world ?? throw new InvalidOperationException(
                "A promoted mission must arrive with the backdrop's world renderer already built.");
            scene = _scene ?? throw new InvalidOperationException(
                "A promoted mission must arrive with the backdrop's scene already walked.");
        }
        else
        {
            WorldBuildResult built = _worldBuilder.Build(
                new WorldBuildRequest(loaded, levelRoot, _resources, _magic, _logger, Services: _title.Services));
            scene = built.Scene;
            _scene = scene;
            _ground = built.Ground;
            _spins = built.Spins;

            if (built.UnresolvedCount > 0 || built.LightmappedCount > 0)
            {
                _logger.Information(
                    "World",
                    $"{scene.Placements.Count} placement(s), {built.LightmappedCount} lightmapped, "
                        + $"{built.UnresolvedCount} unresolved.");
            }

            // Unlike the thirteen optional passes, a mission cannot load without a world to put in it:
            // the collision index, the destructibles and the cut-scene bindings below all attach to it.
            // A title that has not written one yet can still show a menu, and says so here rather than
            // by a null reference three hundred lines later.
            world = _presentation.CreateWorld(new WorldPresentationRequest(_device, _resources, _logger))
                ?? throw new InvalidOperationException(
                    "The title supplies no world presenter, so it cannot load a mission yet.");

            world.Materials = _materials;
            world.DetailMaps = _detailMaps;
            world.Lamps = _lamps;

            // The parts of the level's objects that never stop turning. Set here rather than beside the
            // other registries below because a promoted backdrop skips that block entirely, and it
            // arrives with its own already walked.
            world.Spins = _spins;
            _world = world;
            solids = ObjectCollisionIndex.Build(
                scene.Placements,
                _titleRuntime.Formats.Collision,
                name => loaded.TryGetModel(name, out MefModel? model) ? model : null);
            _worldSolids = solids;
            EndStage("the scene walk, the world renderer and the collision index");
        }
        // Mission 4's authored getaway Bell is a one-shot terminal landing. Configure that
        // behavior explicitly here; CutsceneRuntime remains generic and does not infer it from ids.
        if (id == 4 && levelRoot.Tree.FromId(800) is { } getawayBell)
        {
            _mission.Cutscenes.EnableTerminalTerrainLanding(getawayBell);
        }

        // Every mission: a car's suspension reads this for the surface under each wheel, and much
        // of what a vehicle stands on is placed geometry -- Mission 1's outro truck starts on a
        // garage floor above the dirt. Re-seating the helicopters on it stays Mission 4's.
        _mission.Cutscenes.CollisionIndex = solids;
        if (id == 4)
        {
            _mission.Cutscenes.RestHelicoptersOnSupport();
        }

        // Bind each car's drawn TASKTYPE_WHEEL attachments to its physics record so the drive force
        // and the tyre torque act at the real axle offsets, and the wheels roll and steer.
        _mission.Cutscenes.BindVehicleWheels(scene, name => CutsceneWheelRadius(loaded, name));

        // Every car runs on the original's rigid body, its wheel rays answered by the exact terrain query.
        if (loaded.Terrain is { } carTerrain)
        {
            RetailTerrainRay carRays = new(
                carTerrain.Octree,
                carTerrain.Commands,
                TerrainDiscardBinding.For(carTerrain).Regions,
                TerrainHeightBindings.Bind(carTerrain, levelRoot));
            Func<string, MefModel?> carModels = name => loaded.TryGetModel(name, out MefModel? model) ? model : null;
            CutsceneRuntime cutscenes = _mission.Cutscenes;
            _mission.Cutscenes.ConfigureRetailCars(
                new RetailCarWorld(
                    carRays,
                    RetailWorldObjects.Build(
                        levelRoot,
                        carModels,
                        name => _magic?.TryGet(name, out MagicObjectDefinition attached) == true ? attached : null),
                    cutscenes.RetailVehicleModels,
                    static owner => owner is not GameTask task || ConditionalContainerGate.IsTaskActive(task)),
                carModels,
                name => _magic?.TryGet(name, out MagicObjectDefinition magic) == true ? magic : null);
        }

        // Every Car exports isExploded and blows up from its own CAR record's explosion points; the
        // CarAI stops, and follows its body, through the same registry.
        _mission.Destructibles = DestructibleObjectRegistry.Build(
            levelRoot,
            scene.Placements,
            _loop.Variables,
            _vehiclePhysics,
            random: _soundRandom);
        world.Destructibles = _mission.Destructibles;
        solids.Destructibles = _mission.Destructibles;
        _mission.Cutscenes.Destructibles = _mission.Destructibles;
        world.Cutscenes = _mission.Cutscenes;

        // Rain is a gameplay effect, not part of the backdrop picture: it is configured fresh for every
        // mission — cold start or promoted — straight from the level's own objects.qvm (a RainEffect task
        // enabled by its Is Rain and Is Active flags), and whatever rain the previous mission left is
        // dropped first, exactly as the reference editor resets its rain at every level load. The same scene's
        // geometry also provides the roofs the rain is suppressed under.
        _rain?.Dispose();
        _rain = new RainRenderer(_device, _logger);
        _rain.ReadParams(levelRoot);
        _snow?.Dispose();
        _snow = new SnowRenderer(_device, _logger);
        _snow.ReadParams(levelRoot);
        _storm?.Dispose();
        _storm = new StormRenderer(_device, _logger);
        BuildShelterField(scene, loaded);
        StartTerrainRaster();

        // The storm falls where the level says rain falls -- it is the same weather, drawn better -- so
        // it inherits the vanilla renderer's reading of the RainEffect task rather than parsing it a
        // second time, and shelters under the same roofs.
        _storm.SetShelter(_shelterField);
        ApplyWeatherSettings();

        b.Scene = scene;
        b.Solids = solids;
        b.World = world;
    }

    /// <summary>The soldiers' meshes and textures.</summary>
    private void MissionSoldiers(MissionBuild b)
    {
        LoadedMission loaded = b.Loaded;
        LevelTask levelRoot = b.LevelRoot;
        bool promoted = b.Promoted;
        ObjectCollisionIndex solids = b.Solids!;

        // The soldiers the level places. Each names its own model, skeleton and stand animation
        // (player.md section 15); the sets they index were loaded when the packs opened. They are not
        // WorldScene placements - a human task is a typed task, and the original draws it through the
        // bone path, not the static one.
        // A promoted backdrop has already built these, behind the menu and against the very same level
        // and collision index, so it hands them over rather than uploading a second copy of every
        // soldier's mesh. Taking the reference is also what stops the backdrop disposing them.
        if (promoted && _backdropSoldiers is { } armedSoldiers)
        {
            _soldiers?.Dispose();
            _soldiers = armedSoldiers;
            _backdropSoldiers = null;
        }
        else
        {
            _soldiers?.Dispose();
            _soldiers = new SoldierRenderer(_device, _resources, _logger, _magic) { DetailMaps = _detailMaps };
            _soldiers.Load(
                loaded,
                levelRoot,
                // The walk's ground when the title keeps one in the level, and the mission's terrain
                // otherwise. _ground itself is not settled until the player is stood up below.
                _ground ?? _playerTerrain,
                solids.Caster());
        }

        EndStage("the soldiers' meshes and textures");
    }

    /// <summary>The doors, elevators, pickups and world interactions, and the soldiers' sounds.</summary>
    private void MissionInteractions(MissionBuild b)
    {
        if (_soldiers is null)
        {
            throw MissingSystem(MissionSystemNames.Soldiers);
        }

        LoadedMission loaded = b.Loaded;
        LevelTask levelRoot = b.LevelRoot;
        int id = b.Id;
        ObjectCollisionIndex solids = b.Solids!;
        IWorldPresenter world = b.World!;

        _soldiers.Simulation.Cutscenes = _mission.Cutscenes;

        // A round -- or a blade -- that meets a body bleeds before it does anything else (0x45F94A),
        // whoever struck it. The splash it throws onto the wall behind (0x463AD0) is the only mark a
        // knife leaves: KnifeStab itself never reaches the decal message.
        _soldiers.BloodSpray = (owner, at, direction) => SpawnRetailBlood(owner, at, direction);
        _mission.Cutscenes?.SetTargetPoseResolver(_soldiers.Simulation.CutscenePose);

        // Interactions are level systems, not AI systems: build them even when a mission has no usable
        // navigation graph. The same door objects are shared by player use, AI door hops and collision.
        _mission.Doors = DoorRegistry.Build(levelRoot);
        _mission.Elevators = ElevatorRegistry.Build(levelRoot, _loop.Variables);
        _mission.Interactions = WorldInteractionRegistry.Build(levelRoot, loaded, _mission.Doors, _loop.Variables, _mission.Elevators);
        _mission.GenericPickups = GenericPickupRegistry.Build(levelRoot, _loop.Variables);
        world.GenericPickups = _mission.GenericPickups;

        // Never handed over after the move to the host, which left the renderer's interaction-driven
        // transforms dead: a gun's stand did not turn with the gun it holds.
        world.Interactions = _mission.Interactions;
        solids.GenericPickups = _mission.GenericPickups;
        _soldiers.Simulation.SetAiTaskActivator(id => _mission.Interactions?.ActivateFromAi(id));
        _soldiers.Simulation.UseInteractions(_mission.Interactions);
        _soldiers.Simulation.SetDoors(_mission.Doors, solids);
        _soldiers.Simulation.SetElevators(_mission.Elevators);

        // The first genuinely world-positioned sound in the port. Its distances are the definition's
        // own -- ai_death_1 is authored 0 to 50 metres -- so a guard dying across the map is silent
        // without anything here choosing a range.
        _soldiers.Simulation.SetDeathSound(position => PlayDeathSound(
            HumanVariantSound.AiDeathPrefix, HumanVariantSound.AiDeathVariants, position));

        // The level's authored ambience -- the AmbientArea boxes that bed a sound in around the player.
        _mission.Ambience = AmbientAreaRegistry.Build(levelRoot, _soundRandom);
        _ambientVoices.Clear();
        if (_mission.Ambience.Areas.Count > 0)
        {
            _logger.Information("Audio", $"{_mission.Ambience.Areas.Count} ambient area(s).");
        }

        // The grunt a soldier makes at a non-fatal hit, rate-limited per human.
        _soldiers.Simulation.SetHitSound((prefix, variants, position) => PlayHitSound(prefix, variants, position));
        _soldiers.Simulation.SetHitCooldown(() => HumanVariantSound.HitCooldownTicks(_soundRandom));
        _soldiers.Simulation.SetMeleeStrikeSink(StrikePlayer);
        _soldiers.Simulation.SetPlayerDeltaTranslationScale(() => _playerDeltaTranslationScale);

        // AIAction_PlaySound (0x48DF80) either attaches the definition to the human or starts it at
        // HumanAI's current position. Both begin at the same live position; the backend owns any voice
        // following after that point.
        _soldiers.Simulation.SetScriptSound((name, position, attached) =>
            _sounds?.PlayDefinition(name, position, priority: 200));

        // Each AI weapon owns its automatic loop and updates its world position every tick. The round
        // callback handles ballistics only; without this parallel path AI shots were completely silent.
        _soldiers.Simulation.SetWeaponSound(TickSoldierWeaponSound);

        // Footsteps: animation event 1010, picked from the six walk sounds of whatever is underfoot.
        _soldiers.Simulation.SetFootstepSound(step => PlayFootstep(step));

        // Doors: door_open_1 / door_close_1 / door_slide_1 in mission 1, all definition names.
        _mission.Doors.SetSoundSink(PlayDoorSound);
        _mission.Doors.SetDoorwayBlockedQuery(DoorwayIsBlockedByHuman);
        _mission.Elevators.SetSoundSink(PlayElevatorSound);
        world.Doors = _mission.Doors;
        world.Elevators = _mission.Elevators;
        if (_mission.Doors.Doors.Count > 0)
        {
            _logger.Information("World", $"{_mission.Doors.Doors.Count} door(s) available to players and AI.");
        }

        EndStage("the doors, elevators and world interactions");
    }

    /// <summary>The security cameras, their sight, and the rotating objects.</summary>
    private void MissionCameras(MissionBuild b)
    {
        LevelTask levelRoot = b.LevelRoot;
        WorldScene scene = b.Scene!;
        ObjectCollisionIndex solids = b.Solids!;
        IWorldPresenter world = b.World!;

        // Camera sight uses the same placed-object + terrain trace as HumanAI. Cameras are level
        // objects, not navigation actors, so construct them independently of whether this mission
        // happens to ship any AI graphs.
        WorldTraceCast worldTrace = _playerTerrain is { } ground
            ? WorldTrace.Nearest(solids.Tracer(), TerrainRayTrace.Caster(ground))
            : solids.Tracer();
        _cameraBeepVoices.Clear();
        _cameraMoveVoices.Clear();
        StopCutsceneSounds();
        _mission.SecurityCameras = SecurityCameraRegistry.Build(levelRoot, _loop.Variables, worldTrace);
        _mission.SecurityCameras.BindScene(scene.Placements);
        world.SecurityCameras = _mission.SecurityCameras;
        _cameraStatusLights?.Dispose();
        _cameraStatusLights = _presentation.CreateCameraStatusLights(EffectsRequest());
        if (_cameraStatusLights is not null && !_cameraStatusLights.Load(_fileSystem))
        {
            _cameraStatusLights.Dispose();
            _cameraStatusLights = null;
        }
        _logger.Information(
            "Ai",
            $"{_mission.SecurityCameras.Cameras.Count} security camera(s), "
                + $"{_mission.SecurityCameras.ControlCount} camera control(s) placed.");

        // Radar dishes and the authored Rotating tasks. Built here and handed to the presenter, which is
        // what turns them: the walk gives a placement, this gives it an angle per tick.
        _mission.RotatingObjects = RotatingObjectRegistry.Build(levelRoot, scene.Placements, _loop.Variables, _magic);
        world.RotatingObjects = _mission.RotatingObjects;

        b.WorldTrace = worldTrace;
    }

    /// <summary>The nav graphs and the soldiers' brains, with what their rounds, blasts and alarms do.</summary>
    /// <summary>The level's nav graphs, which the soldiers and the vehicles drive over.</summary>
    private void MissionNavGraphs(MissionBuild b)
    {
        string graphDirectory = ResolveDirectoryCaseInsensitive(
                _gamePath, Path.Combine(b.Loaded.Descriptor.MissionDirectory, "graphs"))
            ?? Path.Combine(_gamePath, b.Loaded.Descriptor.MissionDirectory, "graphs");
        try
        {
            b.Graphs = NavGraphs.Read(graphDirectory, b.LevelRoot, _logger);
        }
        catch (IOException ex)
        {
            _logger.Warning("Ai", $"The nav graph could not be read: {ex.Message}");
            b.Graphs = [];
        }

        Dictionary<int, GraphFile> graphs = b.Graphs;
        if (graphs.Count > 0)
        {
            _logger.Information(
                "Ai",
                $"Nav graphs: {graphs.Count} — " +
                string.Join(", ", graphs.OrderBy(p => p.Key).Select(p => $"{p.Key}:{p.Value.Nodes.Count}n@{p.Value.CoverMidOffsetMetres:0.##}m")));

            // CarAI is a separate retail task family from HumanAI. Give the runtime the same graph
            // corpus the soldiers receive so every graph-driven vehicle -- Mission 7's checkpoint APC
            // and T80, Mission 6's alarm APC, the objective-gated limousines -- follows its authored
            // PatrolPath route. d843661b dropped this call and left every one of them parked.
            _mission.Cutscenes?.ConfigureCarAi(graphs);
        }
        else
        {
            // A missing patrol graph only removes movement: retail still registers every HumanAI as a live human, which
            // can stand, be seen and be selected by the binocular detector.
            _logger.Warning("Ai", $"No nav graph under '{graphDirectory}', so no soldier will patrol.");
        }

        EndStage("reading the nav graphs");
    }

    /// <summary>The level's baked lighting: the probe field, the path-traced lightmaps and the distance fields, started.</summary>
    private void MissionLightBakes(MissionBuild b)
    {
        if (_scene is null)
        {
            throw MissingSystem(MissionSystemNames.Scene);
        }

        LevelTask levelRoot = b.LevelRoot;
        int id = b.Id;
        bool promoted = b.Promoted;
        ObjectCollisionIndex solids = b.Solids!;

        // The level's sun has to be known BEFORE the bakes below, not after: the Dirlight was read
        // some hundreds of lines further down (the `if (!promoted)` block), so every probe field and
        // path-traced lightmap was baked with `_sun` still null -- a zero sun direction and a fallback
        // sky -- which is exactly why the baked GI came out dim. Read it here, unless a promoted
        // backdrop already carried one in. The later read stays and simply re-confirms the same value.
        if (_sun is null && DirectionalLightSettings.TryRead(levelRoot, out DirectionalLightSettings bakeSun))
        {
            _sun = bakeSun;
        }

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
        // The shared bake inputs -- sun and a glass-passing cast -- are built when EITHER the probe
        // field or the path-traced lightmaps are wanted; each bake below is then guarded on its own
        // cvar, so r_lightmap works at a preset (ultra) that runs no probe field.
        if (RenderCVars.GlobalIllumination.Value || RenderCVars.BakedLightmaps.Value > 0)
        {
            Vector3f bakeSunDirection = _sun is { } declared && !declared.IsBelowHorizon
                ? declared.Direction
                : default;

            WorldRayCast shutObjectCast = solids.CasterWithDoorsShut();

            // A bake ray passes through glass and chain-link the way a line of sight does
            // (GameMaterial.IsSightTransparent), so daylight reaches a room through its windows
            // instead of stopping at the pane -- the very thing that left window rooms as dark as
            // sealed ones. It steps a hair past each see-through face and casts on until it meets
            // something solid or leaves the level. Portal polygons (doorway/window openings) are
            // already skipped by the cast itself. Terrain always blocks -- it reports material Air.
            WorldRayCast seeThroughObjectCast = (a, b, out blockedAt) =>
            {
                Vector3d origin = a;
                for (int pass = 0; pass < 8; pass++)
                {
                    if (!shutObjectCast(origin, b, out WorldRayHit hit))
                    {
                        blockedAt = default;
                        return false;
                    }

                    if (!GameMaterial.IsSightTransparent(hit.Material))
                    {
                        blockedAt = hit;
                        return true;
                    }

                    Vector3d toEnd = new(b.X - hit.Position.X, b.Y - hit.Position.Y, b.Z - hit.Position.Z);
                    double length = Math.Sqrt((toEnd.X * toEnd.X) + (toEnd.Y * toEnd.Y) + (toEnd.Z * toEnd.Z));
                    if (length <= 1e-6)
                    {
                        blockedAt = default;
                        return false;
                    }

                    // A few units past the pane (4/4096 m ~= 1 mm); thicker or layered glass is
                    // cleared by the loop, one see-through face at a time.
                    double scale = 4.0 / length;
                    origin = new Vector3d(
                        hit.Position.X + (toEnd.X * scale),
                        hit.Position.Y + (toEnd.Y * scale),
                        hit.Position.Z + (toEnd.Z * scale));
                }

                blockedAt = default;
                return false;
            };

            WorldRayCast bakeCast = (a, b, out blockedAt) =>
            {
                bool hitObject = seeThroughObjectCast(a, b, out WorldRayHit onObject);
                if (_playerTerrain is { } bakeGround
                    && TerrainSightBlock.TryBlock(bakeGround, a, b, out WorldRayHit onGround)
                    && (!hitObject || onGround.Distance < onObject.Distance))
                {
                    blockedAt = onGround;
                    return true;
                }

                blockedAt = onObject;
                return hitObject;
            };

            // Dev verification for the path-traced lightmapper (phases 1-2): when IGI_BAKELM_DIR is
            // set, enumerate the lightmapped texels, bake direct sun + sky, and dump the largest few
            // pages' normals and radiance so the texel map and the direct pass can be checked by eye.
            // Off unless the env var is set.
            if (Environment.GetEnvironmentVariable("IGI_BAKELM_DIR") is { Length: > 0 } lightmapDir
                && _scene is { } lightmapScene)
            {
                DumpLightmapVerification(lightmapScene, bakeCast, bakeSunDirection, lightmapDir);
            }

            // Path-traced object lightmaps (r_lightmap): re-bake the level's static object lighting
            // -- direct sun, sky and multi-bounce indirect -- and swap the pages in where the shipped
            // .olm ones would be sampled. Cached to disk like the probe field, so only a mission
            // without a bake pays to trace it, and traced on a worker so the load does not stall.
            // The sky is the daytime skylight (r_sky_light), not the dim raw ambient, so sky-lit
            // surfaces read closer to what the artists painted. r_lightmap 2 discards the shipped
            // lightmaps entirely; 1 keeps them where the bake produced nothing.
            bool discardRetailLightmaps = RenderCVars.BakedLightmaps.Value >= 2;
            if (RenderCVars.BakedLightmaps.Value > 0 && _scene is { } bakeScene && _world is not null)
            {
                Vector3f lmSun = _sun?.Colour ?? new Vector3f(1f, 1f, 1f);

                // The LEVEL's ambient, not SkyFillColour's. That reads the effective sun -- the
                // authored one after the day/night sweep and the daylight re-point have had it,
                // which moves every frame and belongs to the frame before this load. The two
                // inputs beside it come from the level's own Dirlight, so mixing the two gave a
                // disk-cache key that could not be reproduced: entering the same mission twice in
                // a session missed its own bake and traced the level again.
                Vector3f lmSky = (_sun?.Ambient ?? new Vector3f(0.27f, 0.29f, 0.33f))
                    * RenderCVars.SkyLight.Value;
                _lightmapBakeMission = id;
                _lightmapBakeSun = bakeSunDirection;
                _lightmapBakeSunColour = lmSun;
                _lightmapBakeSky = lmSky;

                if (_lightmapCache.TryLoad(id, bakeSunDirection, lmSun, lmSky) is { } cachedLightmaps)
                {
                    _world.SetBakedLightmaps(cachedLightmaps, discardRetailLightmaps);
                }
                else
                {
                    // Set the mode straight away so the worker's seconds are not spent showing the
                    // shipped lightmaps under an exclusive (r_lightmap 2) request: with no bake yet,
                    // lightmapped surfaces read black until the bake lands. Mode 1 keeps the shipped
                    // pages meanwhile, which is what its fallback means.
                    _world.SetBakedLightmaps(null, discardRetailLightmaps);

                    // Resolving each surface's albedo reads the resource registry, so the texel
                    // maps, textured triangles and their BVH are built here on the main thread; the
                    // worker then does only the tracing, which touches immutable data and the cast.
                    IReadOnlyList<LightmapBaker.LightmapTarget> lmTargets = LightmapBaker.EnumerateTargets(bakeScene);
                    LightmapBaker.LightmapTriangle[] lmTris = LightmapBaker.BuildTriangles(bakeScene, lmTargets, _resources);
                    LightmapBaker.LightmapBvh lmBvh = LightmapBaker.LightmapBvh.Build(lmTris);

                    _lightmapBake?.Cancel();
                    _lightmapBake = LightmapBake.Start(
                        bakeScene, lmTargets, lmTris, lmBvh, bakeCast, bakeSunDirection, lmSun, lmSky,
                        _work, bounces: 4, hemisphereRays: 128, logger: _logger);

                    if (_options.Lockstep && _lightmapBake.WaitForResult() is { } readyLightmaps)
                    {
                        _world.SetBakedLightmaps(readyLightmaps, discardRetailLightmaps);
                        _lightmapCache.Save(id, bakeSunDirection, lmSun, lmSky, readyLightmaps);
                        _lightmapBake = null;
                    }
                }
            }
            else
            {
                // The setting is off, or off since the last load: make sure no stale bake lingers.
                _world?.SetBakedLightmaps(null, discardRetail: false);
            }

            // The probe field, guarded on its own cvar so it does not trace under a lightmap-only
            // request (r_gi off, r_lightmap on -- the ultra preset is exactly that).
            if (RenderCVars.GlobalIllumination.Value)
            {
            // Whatever the console says, but never so fine that a big level spends a minute of
            // load tracing rays nobody asked for.
            float giSpacing = RenderCVars.GlobalIlluminationSpacing.Value;

            // Remembered so the live re-bake (F11) can trace the field again from exactly the
            // inputs this load used -- the same cast, sun and spacing -- whether the field arrived
            // from the cache or the tracer below.
            _bakeCast = bakeCast;
            _probeBakeMission = id;
            _probeBakeSpacing = giSpacing;
            _probeBakeSun = bakeSunDirection;

            // A shipped (or previously-written) bake loads instantly; only a mission without one
            // pays to trace it here, and then writes the result so no load pays for it again.
            if (_giCache.TryLoad(id, giSpacing, bakeSunDirection) is { } cachedField)
            {
                _probeField = cachedField;
            }
            else
            {
                _probeBake = IrradianceProbeBake.Start(
                    _scene,
                    bakeCast,
                    bakeSunDirection,
                    giSpacing,

                    // One albedo for the whole level. The alternative is reading each hit's own
                    // texture, which needs the ray to report a material rather than a triangle --
                    // and IGI's palette is concrete, snow and dirt, so a warm grey is not far off
                    // any of them.
                    ProbeBakeAlbedo,
                    _work,
                    _logger);

                if (_options.Lockstep)
                {
                    // A capture is not allowed to race the worker: a reference image has to either
                    // contain the field or not, never "whatever frame 30 caught". So a lockstep run
                    // blocks for the finished field and writes the cache inline.
                    _probeField = _probeBake.WaitForResult();
                    _probeBake = null;

                    if (_probeField is not null)
                    {
                        _giCache.Save(id, giSpacing, bakeSunDirection, _probeField);
                    }
                }
                else
                {
                    // Interactive play enters the level on flat ambient and watches the light
                    // stream in: the render loop polls TryCollectLatest and eases each snapshot in
                    // as it lands, so a room fills with light over the first seconds rather than the
                    // load stalling until it is lit. The cache is written once the bake completes,
                    // under the key already remembered above.
                    _probeField = null;
                }
            }
            }
        }

        // The distance fields, on a worker beside the probe bake. Tracing every distinct
        // model is arithmetic and moves off the render thread freely; only the upload cannot,
        // because a GL texture belongs to the context that made it.
        //
        // Started here rather than built here: EnsureDistanceFields is the same call the render
        // loop makes every frame, so a level begins its bake at load and a player who switches the
        // lighting model on later gets one started for them. See GameHost.RealtimeGi.cs.
        // The outgoing level's cards go first, before the incoming field is adopted. Adopting a
        // field is one of the two moments that can complete the pair and converge the cache, and
        // with the old cards still standing it would converge THOSE against the new level's field
        // -- then latch, so the real convergence a few hundred lines below was skipped and the
        // level opened on the ramp this is all meant to remove. It failed silently, because a
        // converged-looking cache and a stale one differ only in the picture.
        // Not on a promotion: the backdrop already carries this very mission's cards and field
        // -- built or baking behind the menu, which is what lets the menu show the lit level --
        // and discarding them here would throw the field away and bake it again in front of
        // the player.
        if (!promoted)
        {
            DiscardSurfaceCache();
            DiscardDistanceFields();
        }

        EnsureDistanceFields();

        // A capture must not race the worker, for the reason the probe bake records: a
        // reference image has to either contain the field or not, never whichever frame
        // the worker happened to reach.
        if (_options.Lockstep && _sdfBake is { } pendingField
            && pendingField.WaitForResult() is { } readyField)
        {
            _sdfBake = null;
            AdoptDistanceField(readyField.Scene, readyField.Atlas, fromCache: false);
        }

        // Every distinct model's surface is captured into the card atlas, but deliberately NOT
        // here -- even though this is where the geometry first exists. Lighting the cache needs
        // the level's own sun, which is not read until further down this load, and lighting the
        // cards before that gave every page a zero sun direction and a picture of pure sky, which
        // is exactly what the first run produced. EnsureSurfaceCache captures and lights in one
        // go, so it is called once the sun is known; the outgoing cards were dropped above.

        _device.SetIrradianceVolume(_probeField);
        ApplyColourLookup(_options.StartMission);
        EndStage("starting the light bakes");
    }

    /// <summary>The soldiers' brains, the alarm speakers and the guards' rounds.</summary>
    private void MissionNavigationAi(MissionBuild b)
    {
        if (_scene is null)
        {
            throw MissingSystem(MissionSystemNames.Scene);
        }

        if (_soldiers is null)
        {
            throw MissingSystem(MissionSystemNames.Soldiers);
        }

        LoadedMission loaded = b.Loaded;
        LevelTask levelRoot = b.LevelRoot;
        WorldScene scene = b.Scene!;
        IWorldPresenter world = b.World!;
        WorldTraceCast worldTrace = b.WorldTrace!;
        Dictionary<int, GraphFile> graphs = b.Graphs ?? throw MissingSystem(MissionSystemNames.NavGraphs);

        // The difficulty every guard reads its combat row at. Faithfully the ACTIVE PLAYER PROFILE's
        // (sub_406F70 takes it from sub_406220()'s first field), so the difficulty the player picked
        // in the menu actually reaches the AI -- which it did not while this was a hardcoded default,
        // leaving every guard on GD_2/Normal whatever the profile said. -difficulty still overrides,
        // for testing a difficulty without editing a profile.
        AiDifficulty effectiveDifficulty = EffectiveDifficulty();
        _logger.Information(
            "Ai",
            $"Difficulty {HumanAiConfigRegistry.NameOf(effectiveDifficulty)} ({effectiveDifficulty}) "
                + (_options.Difficulty is null
                    ? $"from profile '{_configuration.Active?.Name ?? "(none)"}'."
                    : "from -difficulty override."));

        // The sight and bullet rays test placed objects AND the terrain, as the original's
        // 0x4CDB20 does (both of the sight ray's passes send the -1 mask whose sign bit turns
        // the terrain arm on -- player.md 7.6). Objects alone let guards see and shoot through
        // hills.
        // The level's doors, before the brains: a walker asks the registry for the nearest door
        // on a door hop, and an open door is switched out of the collision index so the
        // doorway is walkable. Both have to exist before Animate hands each brain its query.
        _soldiers.Simulation.Animate(
            graphs, worldTrace, _resources, _loop.Natives, levelRoot, _aiConfig, effectiveDifficulty,
            _loop.Variables);
        EndStage("animating the soldiers' brains");

        // The level's alarm speakers, for the sirens: each follows its own expression over
        // the same live variables the controls export through.
        _sirenVoices.Clear();
        _reportedSirenFailures.Clear();
        _mission.AlarmSpeakers = AlarmSpeakerRegistry.Build(
            levelRoot,
            _loop.Variables,
            () => _soldiers.Simulation.AnyAlarmOn);
        _mission.AlarmSpeakers.BindScene(scene.Placements);
        world.AlarmSpeakers = _mission.AlarmSpeakers;
        _logger.Information("Ai", $"{_mission.AlarmSpeakers.Speakers.Count} alarm speaker(s) placed.");

        // A guard's round: it raised the trigger, the weapon's cadence let one out, and GuardRounds makes it a cast.
        _guardRounds = new GuardRounds(_soldiers.Simulation, worldTrace, new GuardRoundHost(this), _logger);
        _soldiers.Simulation.SetRoundHandler(_guardRounds.Resolve);
    }

    /// <summary>The difficulty every guard reads its combat row at.</summary>
    /// <returns>The <c>-difficulty</c> override, or else the active profile's.</returns>
    private AiDifficulty EffectiveDifficulty() => _options.Difficulty
        ?? (AiDifficulty)Math.Clamp(
            _configuration.Active?.Difficulty ?? (int)AiDifficulty.Normal,
            (int)AiDifficulty.Easy,
            (int)AiDifficulty.Hard);

    /// <summary>The soldiers, the doors, the alarm controls and the player join the level's walk.</summary>
    private void MissionLevelWalk(MissionBuild b)
    {
        LevelTask levelRoot = b.LevelRoot;

        // The soldiers, the doors, the alarm controls and the player update inside the level's walk.
        AttachActorsToLevelWalk(levelRoot);
    }

    /// <summary>The terrain, sky, sun, grade and camera; the lamps and the surface cache; what impact effects trace against; the player's state reset.</summary>
    private void MissionPicture(MissionBuild b)
    {
        if (_scene is null)
        {
            throw MissingSystem(MissionSystemNames.Scene);
        }

        LoadedMission loaded = b.Loaded;
        LevelTask levelRoot = b.LevelRoot;
        int id = b.Id;
        bool promoted = b.Promoted;
        WorldScene scene = b.Scene!;

        // Everything from here to the free camera is what makes the level a PICTURE, and a promoted
        // backdrop is already that picture -- AdoptPreview built the terrain (with its ground detail
        // derived on the worker, which is most of the cost), the sky, the shadow pass, the clear colour,
        // the grade and the camera, and the menu has been drawing them for as long as the player has been
        // reading the briefing. Rebuilding them here would throw away the one thing this whole path
        // exists to reuse, and would do it on the render thread.
        if (!promoted)
        {
            // A level whose terrain files are not live data — level 14 is exactly that — draws its objects
            // over nothing rather than following offsets into whatever the geometry blob happens to hold.
            // The surface comes from the level's own TextureModifier tasks, so it needs the loaded tree.
            // A title whose ground it reads itself answers CreateGround; one whose ground the host
            // already built as a TerrainSurface answers CreateTerrain. Asking in that order is what lets
            // the two coexist without either pretending to the other's shape.
            _terrain?.Dispose();
            _terrain = _presentation.CreateGround(
                new GroundPresentationRequest(_device, levelRoot, _resources, _logger));

            _terrain ??= loaded.Terrain is { IsUsable: true } usable
                ? _presentation.CreateTerrain(new TerrainPresentationRequest(
                    _device, TerrainSurface.Build(usable, levelRoot, _logger), _logger, _detailMaps, null))
                : null;

            // The sky is script data on the level's task tree; its fog colour is also what the frame
            // clears to, so the horizon meets sky-coloured backing rather than black.
            _sky?.Dispose();
            _sky = _presentation.CreateSky(
                new SkyPresentationRequest(_device, levelRoot, _fileSystem, _resources, FarPlane, _logger));
            _shadowPass = new ShadowCasterPass(_device);

            ApplyLevelConstants(levelRoot);

            if (DirectionalLightSettings.TryRead(levelRoot, out DirectionalLightSettings readSun))
            {
                _sun = readSun;
                _cycleSun = DayNightBinding.Bind(readSun, _deferredLighting);

                // At the hour the level's authors placed the sun for, not a fixed one: the cycle lands
                // back on the authored direction only there.
                _dayNight.ResetForLevel(_cycleSun, RenderCVars.TimeOfDay.Value);

                // After the reset, which clears it: a level that authors its own night hands the cycle
                // that night to cross to, instead of the faint one the cycle invents for a level that
                // does not have one. See DayNightCycle.SetNightLighting.
                if (_deferredLighting is { Night: { } boundNight } authoredNight)
                {
                    _dayNight.SetNightLighting(
                        boundNight.SunIsMoon ? readSun.Colour : null,
                        authoredNight.Ambient);
                }
                _logger.Information(
                    "Light",
                    FormattableString.Invariant(
                        $"Sun at {MathF.Asin(Math.Clamp(readSun.Direction.Z, -1f, 1f)) * 180f / MathF.PI:F1} degrees ")
                    + FormattableString.Invariant(
                        $"elevation, time {readSun.TimeOfDay:F0}, colour ({readSun.Colour.X:F2}, {readSun.Colour.Y:F2}, {readSun.Colour.Z:F2}), ")
                    + FormattableString.Invariant(
                        $"ambient ({readSun.Ambient.X:F2}, {readSun.Ambient.Y:F2}, {readSun.Ambient.Z:F2})."));

                if (readSun.IsBelowHorizon)
                {
                    _logger.Warning("Light", "This level's authored sun is below the horizon; see rendering.md section 12.");
                }
            }
            else
            {
                _cycleSun = null;
                _dayNight.ResetForLevel(null, RenderCVars.TimeOfDay.Value);
                _logger.Information("Light", "The level declares no Dirlight; the modern path will light it with a default sun.");
            }

            // A title may decline a sky entirely -- the factory returns null when the level declares
            // none -- so the clear colour keeps whatever it had rather than reading off nothing.
            if (_sky is { HasSky: true } sky)
            {
                _scheduler.ClearColour = new Bgra32(
                    (byte)Math.Clamp((int)(sky.FogColour.Z * 255f), 0, 255),
                    (byte)Math.Clamp((int)(sky.FogColour.Y * 255f), 0, 255),
                    (byte)Math.Clamp((int)(sky.FogColour.X * 255f), 0, 255),
                    255);
            }

            // The mission's own grade. It used to be applied with `_options.StartMission` from inside the
            // nav-graph block, which was wrong twice over: -mission happens to set that option so the grade
            // looked right under the flag that was being tested with, but a mission started from the shell
            // leaves it 0 and got mission 0's grade, and a level shipping no nav graph got no grade at all.
            // The backdrop always applied the right one (AdoptPreview passes the preview's id), so the two
            // disagreed and the picture changed colour the instant Play was pressed.
            ApplyColourLookup(id);

            // Open on the middle of what was placed, backed off and looking down slightly, so the first
            // frame shows the level rather than the inside of whichever object happens to be at the origin.
            // -campos overrides that with an exact spot, which is how a bug at a known position is looked at.
            _camera = _options.CameraPosition is { } at
                ? new FreeCamera(new Vector3d(at.X, at.Y, at.Z), _options.CameraYaw, _options.CameraPitch)
                : FreeCameraFactory.Create(scene, _options.CameraYaw, _options.CameraPitch, _options.CameraHeight);
        }

        // Now that the sun is known. The pages hold unlit albedo by design, so this is the only thing
        // that puts light into the cache -- and it runs once, because this game's sun is a fixed
        // direction per mission, which is the bargain the whole surface cache is built on.
        // The lamps this level implies but never declares, worked out once and used in three places: as
        // EMISSION on the fixture geometry itself, so a lamp looks like the thing doing the lighting; as
        // real lights in the deferred pass, so a windowless room has a source at all; and as emitters the
        // surface cache bounces off, where there is one. All three matter together -- a room lit from a
        // fixture that does not glow reads as lit by nothing, and a fixture that glows without lighting
        // reads as a decal.
        //
        // Above the surface cache and not inside it. The cache is an ultra-only structure and none of
        // this depends on it: the glow is a material and the lights are punctual, so binding the whole
        // conclusion to whether the cards were captured would have switched a level's lamps off for
        // anyone running ultra with r_surface_cache 0.
        //
        // A promoted backdrop surveyed its lamps on the preview worker and has been showing them for as
        // long as the player was reading the briefing; surveying again here was the largest single cost
        // of pressing Play, and it would have produced the same answer.
        // A cold start has a NEW world renderer that has to be handed them either way.
        if (_lampsMissionId != id || !promoted)
        {
            ApplyLamps(
                RenderCVars.GlobalIlluminationLamps.Value != 0
                    ? EmissiveLightProposer.Propose(
                        _scene, _resources, _lampManifest, id, ActiveTitle.Id, _logger)
                    : EmissiveLightProposer.Proposal.None,
                id);
        }

        // The surfaces, now that the sun and the fixtures they are lit by are both known. Captures the
        // atlas and lights it in one step -- see GameHost.RealtimeGi.cs, which is also what the render
        // loop calls when the lighting model is switched on mid-level.
        EnsureSurfaceCache();

        // The walk has already spoken for a title that keeps its ground in the level; for the one that
        // keeps it in the mission, the terrain IS the ground.
        _ground ??= _playerTerrain;
        _worldTrace = _worldSolids is { } liveSolids
            ? _playerTerrain is { } liveTerrain
                ? WorldTrace.Nearest(liveSolids.Tracer(), TerrainRayTrace.Caster(liveTerrain))
                : liveSolids.Tracer()
            : null;

        // What the impact effects trace against, built here rather than beside the security cameras
        // because the terrain is only known now: a blood droplet lands on the ground at least as often
        // as it hits a wall, and the splash behind a body needs whatever is three metres past it. The
        // player's movement cast will not do: it is a coarser collision whose rows name no mark, so every
        // splash traced against it was refused.
        //
        // The terrain leaf reports no material row, so a hit on open ground comes back as Air and marks
        // nothing -- which would leave the ground clean of both bullet holes and blood, where retail's
        // Ground row names hole_concrete1.tex. The level's own terrain row is substituted, which is the
        // same row the footsteps and the player's material lookup read.
        _effectWalls = _worldTrace is { } effectTrace
            ? (Vector3d origin, Vector3d end, out WorldRayHit hit) =>
            {
                if (!effectTrace(
                        new WorldTraceRequest(origin, end, WorldTraceMask.World), out WorldTraceHit rich))
                {
                    hit = default;
                    return false;
                }

                hit = rich.RayHit;
                if (rich.Kind == WorldTraceHitKind.Terrain && hit.Material == 0 && _effectTerrainRow is int ground)
                {
                    hit = hit with { Material = ground };
                }

                return true;
            }
            : null;
        _player = null;
        _titlePlayer = null;
        _titleHud?.Dispose();
        _titleHud = null;
        _playerMode = false;
        _pendingGenericTbaExplosions.Clear();

        // A fresh mission is a fresh life: the last one's death, its red wash and its death camera do not
        // carry over.
        _playerHealth = new(HumanHitZoneTable.Player, _playerMaximumHealth)
        {
            DamageScale = HumanHealth.DifficultyDamageScale(_playerDifficulty),
        };
        _playerDead = false;
        _damageFlash = new();
        _flashbangScreenFlash = new();
        _deathCamera.Reset();
        _flashlight.Reset();

        EndStage("the terrain, sky, sun, grade and camera");
    }

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
            PlayerCamera candidate = new(_logger);

            // 000.iff, kept so the player's third-person body can be built from the same set it walks on
            // (HumanPlayerBody: the body is skeleton 0, and this is skeleton 0's set). Walking is the
            // animation's own root motion, so without it the player can stand and fall but not walk.
            //
            // Read once for the session rather than per mission: it lives under common/, so it is the
            // same 1.5 MB of IFF for all fourteen, and parsing it again on the render thread every time
            // a mission started was most of that step's cost.
            ReadSharedAnimations();
            Formats.Animation.BoneAnimationSet? playerBodySet = _playerBodyAnimations;
            if (playerBodySet is not null)
            {
                candidate.UseAnimations(playerBodySet);
            }

            // The walls. Terrain alone gives the player ground to stand on but nothing to stop it
            // walking through crates and buildings; the solidity is in each model's own HSMC mesh.
            // The index is the one built above for the soldiers' ground probe.
            candidate.UseWalls(solids);
            _logger.Information(
                "Player",
                $"{solids.Count} placed object(s) are solid, {solids.TriangleCount} collision triangle(s).");

            // The grenade's own cast INCLUDES the terrain: the body meets the ground through its
            // ground query, but a grenade has no such query and would fall through the world.
            candidate.GrenadeWalls = _playerTerrain is { } grenadeGround
                ? WorldTrace.AsRayCast(WorldTrace.Nearest(solids.Tracer(), TerrainRayTrace.Caster(grenadeGround)))
                : solids.Caster();
            candidate.GrenadeTrace = _playerTerrain is { } grenadeTraceGround
                ? WorldTrace.Nearest(solids.Tracer(), TerrainRayTrace.Caster(grenadeTraceGround))
                : solids.Tracer();

            // The view model's forward sweep, which wants a ray through the terrain rather than the
            // height under a point. Null for a title whose ground nothing can trace yet, and the
            // pullback then meets placed objects only.
            candidate.TerrainTrace = _playerTerrain is { } viewModelGround
                ? TerrainRayTrace.Caster(viewModelGround)
                : null;

            // The ladders the level attaches to its buildings, so the player can climb them (E to mount,
            // W/S up and down, E held to slide).
            if (_scene is not null)
            {
                candidate.UseLadders(_scene.Ladders);
                _logger.Information("Player", $"{_scene.Ladders.Count} ladder(s) to climb.");
            }
            if (_mission.Interactions is { } interactions)
            {
                candidate.UseInteractions(interactions);
                candidate.TaskActivated += id => _soldiers?.Simulation.ActivateTask(id);
                candidate.ElectrifiedFenceTouched += fence => ResolvePlayerElectricFence(candidate, fence);
                candidate.UnarmedStationaryGun += gun => _logger.Warning(
                    "Player",
                    $"Mounted stationary gun {gun.TaskId}, but its {gun.WeaponId} is not bound to a weapon; it cannot fire.");
                _logger.Information("Player", $"{interactions.Items.Count} direct world interaction(s).");
            }
            if (_mission.GenericPickups is { } genericPickups)
            {
                candidate.UseGenericPickups(genericPickups);
                candidate.GenericPickedUp += PlayGenericPickupSound;
                _logger.Information("Player", $"{genericPickups.Items.Count} generic pickup(s) available.");
            }

            // ORDER MATTERS, and it is the original's. WEAPON_ID_* and AMMO_ID_* are pattern-registered
            // symbols that get their values as the two tables are read, so the rosters have to load
            // before humanplayer.qvm runs -- otherwise every id in its weapon cycle is a placeholder
            // and the cycle names weapons nobody carries. section 8.
            WeaponTypeRegistry weaponRoster = new(_logger);
            AmmoTypeRegistry ammoTable = new(_logger);
            GameMaterialRegistry materialTable = new(_logger);
            try
            {
                weaponRoster.Register(_loop.Natives);
                weaponRoster.Load(_fileSystem, _resources, _loop.Natives);
                WeaponConfigRegistry.RegisterTypes(_loop.Types);
                WeaponConfigRegistry weaponConfig = new(_logger);
                if (weaponConfig.Load(_resources, _loop.Natives, _loop.Tasks) == WeaponConfigRegistry.NoScript)
                {
                    _logger.Warning(
                        "Weapon",
                        $"No weapon configuration at {WeaponConfigRegistry.ScriptResource}; this title keeps it elsewhere.");
                }
                else
                {
                    weaponConfig.AttachTo(weaponRoster);
                }
                ammoTable.Register(_loop.Natives);
                ammoTable.Load(_fileSystem, _resources, _loop.Natives);

                // Shared with the guard-round handler: the ammunition record is what colours a
                // ballistic streak, exactly as it colours the retail tracer.
                _ammoTypes = ammoTable;

                // Kept beside it for the all-weapons cheat, which can be switched on mid-mission and
                // then needs the whole roster, not just what the level declared.
                _weaponTypes = weaponRoster;

                // What every surface in the world is made of, and therefore what a round leaves on it.
                materialTable.Register(_loop.Natives);
                if (_fileSystem.Exists(GameMaterialRegistry.ScriptResource))
                {
                    _resources.Register(
                        GameMaterialRegistry.ScriptResource,
                        _fileSystem.ReadAllBytes(GameMaterialRegistry.ScriptResource));
                }
                else
                {
                    _logger.Warning("Material", $"No material table at {GameMaterialRegistry.ScriptResource}.");
                }

                int materialCount = materialTable.Load(_resources, _loop.Natives);
                candidate.UseMaterials(materialTable);
                _soldiers?.Simulation.UseMaterials(materialTable);
                _worldMaterials = materialTable;
                _logger.Information("Material", $"Read {materialCount} game material(s).");

                // What the open ground is made of. The terrain declares it the same way a mesh face
                // does -- see FootstepSounds.TerrainGameMaterialIndex -- and it is Snow in the Arctic
                // missions rather than Ground, which is the whole difference in how they sound.
                int? terrainRow = loaded.Terrain is { } terrainData
                    ? FootstepSounds.TerrainGameMaterialIndex(terrainData.Materials)
                    : null;
                candidate.TerrainGameMaterialIndex = terrainRow;
                _effectTerrainRow = terrainRow;
                if (_soldiers is not null)
                {
                    _soldiers.Simulation.TerrainGameMaterialIndex = terrainRow;
                }

                if (terrainRow is int row && materialTable.TryGet(row, out GameMaterial groundRow))
                {
                    _logger.Information("Material", $"Terrain walks on '{groundRow.Name}' (row {row}).");
                }

                _mission.GunPickups = GunPickupRegistry.Build(levelRoot, weaponRoster, ammoTable);
                GunPickupRegistry gunPickups = _mission.GunPickups;
                candidate.UseGunPickups(gunPickups);
                candidate.GunPickedUp += pickup =>
                {
                    PlayGunPickupSound(pickup);
                    string key = $"PICKEDUPWEAPON{pickup.Weapon.DisplayName}";
                    _mission.Objectives?.ShowTransientMessage(missionText.Get(key));
                };
                _soldiers?.Simulation.UseDroppedWeaponPickups(gunPickups);
                _soldiers?.Simulation.SetWeaponDropSink((weapon, position, orientation, velocity) =>
                {
                    GunPickup pickup = gunPickups.Spawn(position, orientation, weapon, velocity);
                    _gunPickupRenderer?.Add(loaded, pickup);
                });

                // What a dropped gun lands on: the same solids and terrain a round strikes, as triangles
                // for the realistic body and as one ray for the original's fifty-metre drop probe.
                ObjectCollisionIndex? dropSolids = _worldSolids;
                TerrainData? dropTerrain = _playerTerrain;
                _soldiers?.Simulation.SetDroppedWeaponWorld(
                    (min, max, into) =>
                    {
                        dropSolids?.CollectTriangles(min, max, into);
                        if (dropTerrain is not null)
                        {
                            TerrainRayTrace.CollectTriangles(dropTerrain, min, max, into);
                        }
                    },
                    _worldTrace is { } dropTrace ? WorldTrace.AsRayCast(dropTrace) : null);
                if (_mission.GunPickups.Items.Count > 0)
                {
                    _logger.Information("Weapon", $"{_mission.GunPickups.Items.Count} gun pickup(s) available.");
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or KeyNotFoundException)
            {
                _logger.Warning("Weapon", $"Could not read the weapon or ammunition tables: {ex.Message}");
            }

            EndStage("the player's animation sets, walls, ladders and the weapon tables");

            // The guards' weapons, which have to wait for the roster: a soldier's gun is an authored
            // Gun child task naming a WEAPON_ID_*, and only the table knows which model that draws.
            _soldiers?.Arm(loaded, weaponRoster);
            ArmVehicles(loaded, levelRoot, weaponRoster, candidate);
            EndStage("the guards' weapon models");

            // The player's tuning is not in the executable: humanplayer.qvm is the whole of it, and it
            // is what says how high a jump goes and how far a step travels.
            try
            {
                HumanPlayerConfig config = new();
                config.Register(_loop.Natives);
                config.Load(_resources, _loop.Natives);

                if (config.PlaceholderIds.Count > 0)
                {
                    _logger.Warning(
                        "Player",
                        $"humanplayer.qvm left {config.PlaceholderIds.Count} id(s) unresolved; the weapon "
                            + "cycle may name weapons that do not exist.");
                }

                if (config.Parameters is { } tuning)
                {
                    candidate.UseParameters(tuning);

                    // Argument 0 scales the animation's delta translation -- the player's, and Ekk's.
                    _playerDeltaTranslationScale = tuning.DeltaTranslationScale;

                    // Argument 7 is the player's maximum health (Human+600, 0x410370's overwrite) --
                    // 3.0 in retail, and the single number that sets the player's time-to-kill.
                    _playerMaximumHealth = tuning.MaximumHealth;

                    // Argument 8 is the dose one powered fence contact adds (0x412F80's fmul against
                    // the human's damage scale) -- 0.5 in retail. Read it for the same reason argument
                    // 7 is read: a retuned humanplayer.qvm owns its own shock, not the shipped number.
                    _electricFenceDose = tuning.ElectricFenceDose;
                    _playerHealth = new(HumanHitZoneTable.Player, _playerMaximumHealth)
                    {
                        DamageScale = HumanHealth.DifficultyDamageScale(_playerDifficulty),
                    };
                }
                else
                {
                    _logger.Warning("Player", "humanplayer.qvm published no parameters; using the retail defaults.");
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
                _logger.Warning("Player", $"Could not run humanplayer.qvm: {ex.Message}");
            }

            EndStage("humanplayer.qvm");

            // PlayerBody resolves the landing on the tick after contact, just as retail does. Its
            // default health exists for standalone physics use; the desktop must give it the exact
            // accumulator the HUD, guard hits and death state already share. The sink crosses the
            // presentation boundary for sound, the red wash, AI hearing and lethal-fall death.
            candidate.UseHealth(_playerHealth);
            candidate.Body.SetFallImpactSink(impact => ResolvePlayerFallImpact(candidate, impact));

            // The first-person rig. Every Human builds one in its constructor and gives it skeleton 5
            // and the model 005_01_1 (0x45EB30, 0x464660); it is what the held weapon hangs off, so
            // without it there is nothing to draw a gun on.
            if (_viewRigAnimations is { } viewRigSet)
            {
                candidate.UseViewRig(viewRigSet);

                // The arms are the other half of what 0x4644A0 draws, and the half that needs CPU
                // skinning: 005_01_1 is mesh format 1.
                _arms?.Dispose();
                _arms = _presentation.CreateViewModel(ViewModelRequest());
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
            _impactParticles?.Dispose();
            _impactParticles = null;
            _retailImpacts?.Dispose();
            _retailImpacts = null;
            _impactEffects.Clear();
            _explosions?.Dispose();
            _explosions = null;
            _missileSmoke?.Dispose();
            _missileSmoke = null;
            _heldWeapon = null;

            // Set whether or not the level declares a loadout: a weapon the all-weapons cheat grants
            // later is loaded from this mission's packs like any other.
            _loadedMissionForWeapon = loaded;

            if (PlayerLoadout.TryRead(levelRoot, out PlayerLoadout? loadout) && loadout is not null)
            {
                _logger.Information("Player", $"Carrying {loadout}.");
                _heldWeapon = CarryLoadout(candidate, loadout, loaded, weaponRoster, ammoTable);

                // The player's rounds are offered to the soldiers, which own their own health.
                if (_soldiers is { } guards)
                {
                    candidate.Humans = guards.Shooter();
                    candidate.HumanProbes = guards.Simulation.Probe();
                    HumanProximityCast soldierProximity = guards.Simulation.ProximityProbe();
                    candidate.HumanProximityTargets = (Vector3d center, float radiusUnits, out HumanProximityTarget target) =>
                    {
                        bool found = soldierProximity(center, radiusUnits, out target);
                        if (!_playerHealth.IsDead)
                        {
                            double distance = Vector3d.Distance(center, candidate.Body.Position);
                            if (distance <= radiusUnits && (!found || distance < target.Distance))
                            {
                                target = new HumanProximityTarget(candidate.Body.Position, distance);
                                found = true;
                            }
                        }

                        return found;
                    };
                    candidate.GroundImpact = impact => guards.Simulation.NotifyGroundImpact(impact, candidate.Body.Position);
                    candidate.AiNoise = noise => guards.Simulation.NotifyHumanNoise(
                        noise, candidate.Body.Position, candidate.Body.Position);

                    // A glancing round whizzes where it turned, on the ricochet sound's own limit --
                    // 0x47BD7B sounds it there directly rather than through the material's window.
                    candidate.Deflected = PlayRicochetSound;

                    // One grenade pool for both throwers: the player's own and every guard's lob fly,
                    // bounce and detonate through the same simulation and the same drain.
                    guards.Simulation.SetGrenades(candidate.Grenades);
                }

                // The in-game HUD owns its own archive, exactly as StatusScreen's constructor does.
                _marks?.Dispose();
                _marks = _presentation.CreateBulletMarks(EffectsRequest());

                // The ballistic streaks, beside the marks they end in. Cheap to have around: with
                // g_ballistics off nothing is ever spawned and Draw returns at the count check.
                _tracers?.Dispose();
                _tracers = new TracerTrailRenderer(_device);

                // And the bursts where rounds land. Also cheap when off: r_impact_particles 0
                // spawns nothing, and an empty pool returns at the count check.
                _impactParticles?.Dispose();
                _impactParticles = new ImpactParticleRenderer(_device, _resources, _fileSystem);
                if (!_impactParticles.Load(loaded))
                {
                    _logger.Warning("Impact", "The retail IGI1 explosion model '1009_01_1' was not bound; using the sprite fallback.");
                }

                // The original's own impact effects: concrete1.spr dust, the material's own chip model,
                // and blood1/blood2 for a body. The runtime keeps the motion; this only draws it. Without
                // it the blood is still thrown and still stamps its splashes, but no drop or mist is seen.
                _retailImpacts?.Dispose();
                _retailImpacts = new RetailImpactRenderer(_device, _resources, _fileSystem);
                int impactModels = _retailImpacts.Load(loaded);
                _impactEffects.Clear();
                _impactEffects.SetMistFrameCount(_retailImpacts.MistFrameCount);
                if (!_retailImpacts.SpritesAvailable)
                {
                    _logger.Warning(
                        "Impact",
                        "concrete1.spr, blood1.spr or blood2.spr did not resolve; the original's "
                            + "impact dust and blood will not draw.");
                }
                else
                {
                    _logger.Debug(
                        "Impact",
                        $"Retail impact effects bound: {impactModels} chip model(s), "
                            + $"{_retailImpacts.MistFrameCount} blood frame(s).");
                }

                // The retail 1009_01_1 fireball, shared by every blast in the level. Loads its real
                // texture lazily on first blast; falls back to the procedural burst without it.
                _explosions?.Dispose();
                _explosions = new ExplosionEffectRenderer(_device, _fileSystem, _logger);

                // The retail smoke2.spr trail behind rockets and the grey column a blast leaves.
                _missileSmoke?.Dispose();
                _missileSmoke = new SmokeSpriteRenderer(_device, _fileSystem, _logger);

                _hud?.Dispose();
                _hud = _presentation.CreateHud(new HudPresentationRequest(_device, _logger));
                if (_hud is not null && !_hud.Load(_fileSystem))
                {
                    _hud.Dispose();
                    _hud = null;
                }

                _remasteredHud?.Dispose();
                _remasteredHud = _presentation.CreateRemasteredHud(new HudPresentationRequest(_device, _logger));
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
                _gunPickupRenderer = _presentation.CreateGunPickups(ViewModelRequest());
                if (_gunPickupRenderer is not null)
                {
                    _gunPickupRenderer.Materials = _materials;
                    _gunPickupRenderer.Load(loaded, pickups);
                }
            }

            // The grenades in the air, drawn with their own retail models. Loaded beside the
            // pickups because it is the same model machinery at a transform the simulation owns.
            _grenadeRenderer?.Dispose();
            _grenadeRenderer = _presentation.CreateGrenades(ViewModelRequest());
            _grenadeRenderer?.Load(loaded);
            _missileRenderer?.Dispose();
            _missileRenderer = _presentation.CreateMissiles(ViewModelRequest());
            _missileRenderer?.Load(loaded);

            EndStage("the loadout, the bullet marks and the HUD");

            // The level places its own player: every mission's objects.qvm declares a HumanPlayer task
            // with a Position and a Gamma. Standing there is standing where the game starts you.
            // Without a declared spawn the player is stood where the opening camera looks from, so that
            // fallback needs a camera to read; a level with neither is simply not placed, which the
            // warning below already covers.
            bool placed = PlayerSpawn.TryFindGameplay(levelRoot, out PlayerSpawnPoint spawn)
                ? candidate.PlaceAt(_ground, spawn, solids.PlayerRegistrationCaster())
                : _camera is { } opening && candidate.Place(_ground, opening.Position, _options.CameraYaw);

            if (placed)
            {
                _player = candidate;

                // The player's own third-person body -- the rig the death camera reveals. Built from the
                // model the level's HumanPlayer declares, on skeleton 0's set (000.iff), the same one the
                // player walks on. It stays invisible until the player dies. Without the spawn's model or
                // the set there is simply no body to show, and death falls back to the frozen view.
                if (_soldiers is { } bodies
                    && playerBodySet is { } bodySet
                    && !string.IsNullOrEmpty(spawn.Model))
                {
                    bodies.LoadPlayerBody(loaded, spawn.Model, bodySet, spawn.Position, spawn.Gamma);
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

    /// <summary>The forest and the water, the menu's renderer and backdrop dropped, and the mission reported built.</summary>
    private void MissionAdornments(MissionBuild b)
    {
        bool promoted = b.Promoted;
        LoadedMission loaded = b.Loaded;
        LevelTask levelRoot = b.LevelRoot;

        // The things standing in the world rather than being it: planted foliage, and water. Both are
        // handed the mission's model lookup, which is the one thing the ground and the sky do not need --
        // a forest plants instances of a model, and the model lives in a pack rather than in the level's
        // own files.
        //
        // Outside the block above, which exists to keep what the backdrop already built. The backdrop
        // builds the ground and the sky and stops there, so a promoted mission that skipped these would
        // be a level with no trees and no water in it -- which is what pressing Play produced while this
        // sat with the rest.
        ScenePresentationRequest adornments = new(
            _device,
            levelRoot,
            _resources,
            name => loaded.TryGetModel(name, out MefModel? model) ? model : null,
            (name, slot) => loaded.TryGetTextureName(name, slot, out string texture) ? texture : null,
            _logger);

        _forest?.Dispose();
        _forest = _presentation.CreateForest(adornments);

        _water?.Dispose();
        _water = _presentation.CreateWater(adornments);

        // The menus are done; without dropping the renderer the last screen would keep drawing over
        // the world.
        _renderer = null;

        // The menu's backdrop is not needed once a real mission is up. On a cold start that releases the
        // previewed level's packs outright. On a promotion it releases nothing: the preview IS this
        // mission now, and its registry and file system are reachable through `_resources` and through
        // the renderers that were built against them — dropping this reference only says the level has
        // stopped being scenery.
        _backdrop = null;
        _arming = null;

        // Built. What the hold is waiting on from here is the lighting, not the level.
        NotifyMissionBuilt();
        _logger.Information("Mission", $"{(promoted ? "Promoted" : "Loaded")} {loaded}.");
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
        _activeLevelRoot = preview.LevelRoot;
        _activeMission = chosen;

        // The backdrop stops being drawn lite before BringMissionUp reads the lighting variables: the
        // real-time structures it skipped are built from here, under the warm-up's hold.
        UpdateBackdropProfile();
        _missionResultHandled = false;
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

    /// <summary>
    /// The mid-swing doorway ray: whether a human currently stands in the opening between the two
    /// doorway endpoints.
    /// </summary>
    /// <param name="from">One end of the doorway segment, in metres.</param>
    /// <param name="to">The other end of the doorway segment, in metres.</param>
    /// <remarks>
    /// This is the player body and every living AI soldier, tested against the doorway segment the
    /// door spans. The retail ray (0x446D10 at 0x447302) casts across the doorway at roughly body
    /// height; we test each human's feet against the segment with a generous half-metre radius, which
    /// finds a body standing in the opening whether it is at waist, chest or head.
    /// </remarks>
    private bool DoorwayIsBlockedByHuman(Vector3d from, Vector3d to)
    {
        if (HumanStandsInSegment(_player?.Body.Position, from, to))
        {
            return true;
        }

        if (_soldiers is { } soldiers)
        {
            foreach (Vector3d feet in soldiers.Simulation.MapContacts())
            {
                if (HumanStandsInSegment(feet, from, to))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Whether a human's feet (world units) stand within a half metre of the doorway segment (metres).</summary>
    private static bool HumanStandsInSegment(Vector3d? feetWorld, Vector3d from, Vector3d to)
    {
        if (feetWorld is not { } feet)
        {
            return false;
        }

        Vector3d point = new(
            feet.X / WorldScene.UnitsPerMetre,
            feet.Y / WorldScene.UnitsPerMetre,
            feet.Z / WorldScene.UnitsPerMetre);
        Vector3d closest = ClosestPointOnSegment(from, to, point);
        return Vector3d.Distance(closest, point) <= 0.5;
    }

    private static Vector3d ClosestPointOnSegment(Vector3d start, Vector3d end, Vector3d point)
    {
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double dz = end.Z - start.Z;
        double lengthSquared = (dx * dx) + (dy * dy) + (dz * dz);
        if (lengthSquared <= 0.0)
        {
            return start;
        }

        double t = (((point.X - start.X) * dx)
            + ((point.Y - start.Y) * dy)
            + ((point.Z - start.Z) * dz)) / lengthSquared;
        t = Math.Clamp(t, 0.0, 1.0);
        return new Vector3d(start.X + (dx * t), start.Y + (dy * t), start.Z + (dz * t));
    }

    /// <summary>Builds the level's top-down map of what the solid world stands over, and binds it.</summary>
    /// <param name="scene">The mission's placed objects.</param>
    /// <param name="loaded">The mission whose models bend its spline segments, as the collision index bends them.</param>
    /// <remarks>
    /// <para>
    /// The one answer to "can the sky reach this point", for the fog, the falling rain, its splashes and
    /// the standing water alike. It is a raster of the collision geometry seen from above rather than a
    /// list of building bounding boxes, because the boxes were wrong both ways round: on mission one a
    /// quarter of the ground inside them has open sky over it — a building at an angle to the world axes
    /// fills barely a third of its own box — while two fifths of the ground genuinely under a roof lay
    /// outside every box, its shelter never having been labelled a building.
    /// </para>
    /// <para>
    /// The CPU keeps the raster for its own questions and the GPU is handed a packed copy, so the puddle
    /// a pixel draws and the drop the storm culls are decided by the same numbers.
    /// </para>
    /// </remarks>
    private void BuildShelterField(WorldScene scene, LoadedMission loaded)
    {
        ReleaseShelterField();

        long started = _loop.TimeSource.Milliseconds;
        _shelterField = ShelterHeightField.Build(
            scene.Placements,
            collision: _titleRuntime.Formats.Collision,
            models: name => loaded.TryGetModel(name, out MefModel? model) ? model : null);
        if (_shelterField.IsEmpty)
        {
            _logger.Information("Shelter", "the level places nothing solid; nothing is sheltered");
            return;
        }

        Bgra32[] texels = _shelterField.Encode(out float scale);
        _shelterMap = _device.CreateTexture(
            new TextureDescription(
                _shelterField.Width,
                _shelterField.Depth,
                MipLevels: 1,
                TextureFilter.Point,
                TextureAddressMode.Clamp) { HoldsData = true },
            texels);

        _giRoofTransform = new ShelterMapTransform(
                (float)_shelterField.OriginX,
                (float)_shelterField.OriginY,
                (float)_shelterField.CellMetres,
                _shelterField.Width,
                _shelterField.Depth,
                _shelterField.LowestZ,
                scale,
                (float)ShelterHeightField.SurfaceHeadroomMetres,
                (float)ShelterHeightField.MaxOverheadMetres);
        _terrainHeightsGivenTo = null;
        _device.SetShelterMap(
            _shelterMap,
            new ShelterMapTransform(
                (float)_shelterField.OriginX,
                (float)_shelterField.OriginY,
                (float)_shelterField.CellMetres,
                _shelterField.Width,
                _shelterField.Depth,
                _shelterField.LowestZ,
                scale,
                (float)ShelterHeightField.SurfaceHeadroomMetres,
                (float)ShelterHeightField.MaxOverheadMetres));

        _logger.Information(
            "Shelter",
            FormattableString.Invariant(
                $"{_shelterField.Width}x{_shelterField.Depth} at {_shelterField.CellMetres:0.00} m, z {_shelterField.LowestZ:0.0}..{_shelterField.HighestZ:0.0}, built in {_loop.TimeSource.Milliseconds - started} ms"));
    }

    /// <summary>Drops the level's shelter map, and tells the device there is none.</summary>
    private void ReleaseShelterField()
    {
        _giRoofTransform = ShelterMapTransform.None;
        ReleaseTerrainRaster();
        _shelterField = ShelterHeightField.Empty;
        _device.SetShelterMap(TextureHandle.None, ShelterMapTransform.None);
        if (_shelterMap.IsValid)
        {
            _device.DestroyTexture(_shelterMap);
            _shelterMap = TextureHandle.None;
        }
    }

    private void ReleasePlayedMission()
    {
        // First, because both are worker threads tracing the level that is going and they hold the whole
        // of what they trace: the lightmap bake's texel maps, triangles and BVH run to a few hundred
        // megabytes, and nothing here released them. A bake was only ever cancelled by the NEXT one
        // starting, so leaving a level left every core path-tracing a level nobody was in -- which is
        // what the new level then had to load against -- and a session that entered a handful of missions
        // carried a handful of those at once. The heap stood at one and a half gigabytes of lightmap
        // texels alone by the sixth.
        _probeBake?.Cancel();
        _probeBake = null;
        _lightmapBake?.Cancel();
        _lightmapBake = null;

        _computerOpen = false;
        _computerFlight.Reset();
        _missionFailureTicks = 0;
        _missionFailedMenu = false;
        _missionResultHandled = false;
        _mapComputer?.Dispose();
        _mapComputer = null;
        _mapComputerDevice?.Dispose();
        _mapComputerDevice = null;
        _tacticalComputer?.Dispose();
        _tacticalComputer = null;
        _messages?.Dispose();
        _messages = null;
        _computerHighlights = null;
        _mission.Objectives?.Release();
        _mission.Objectives = null;
        _mission.Cutscenes = null;
        _soldiersInWalk = false;
        _playerInWalk = false;
        _mission.SecurityCameras = null;
        _cameraBeepVoices.Clear();
        _cameraMoveVoices.Clear();
        StopCutsceneSounds();
        _activeMission = null;

        // The level's own sun goes with the level. It is read on load only when it is not already set --
        // which is how a promoted backdrop hands its own in -- so a sun left standing here was taken for
        // one that had been handed in, and the next mission was lit and BAKED by the one before it. The
        // backdrop the menu puts up next sets its own in AdoptPreview, so nothing that needs one is left
        // without.
        _sun = null;
        _effectiveSun = null;

        // And so does what the level said about its own light. Left standing, the next title's mission
        // would be lit by the last one's answer -- and for Project I.G.I. that means its sky model would
        // stay switched off after a visit to the sequel.
        _deferredLighting = null;
        _cycleSun = null;

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
        _backdropSoldiers?.Dispose();
        _heldWeapon?.Dispose();
        _gunPickupRenderer?.Dispose();
        _hud?.Dispose();
        _remasteredHud?.Dispose();
        _debugOverlay?.Dispose();
        _marks?.Dispose();
        _tracers?.Dispose();
        _impactParticles?.Dispose();
        _retailImpacts?.Dispose();
        _explosions?.Dispose();
        _missileSmoke?.Dispose();
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
        _backdropSoldiers = null;
        _heldWeapon = null;
        _gunPickupRenderer = null;
        _hud = null;
        _remasteredHud = null;
        _debugOverlay = null;
        _marks = null;
        _tracers = null;
        _impactParticles = null;
        _retailImpacts = null;
        _impactEffects.Clear();
        _explosions = null;
        _missileSmoke = null;
        _cameraStatusLights = null;
        _grenadeRenderer = null;
        _missileRenderer = null;
        _arms = null;
        _player = null;
        _titlePlayer = null;
        _titleArms = null;
        _playerMode = false;
        _scene = null;
        _playerTerrain = null;
        _ground = null;
        _effectWalls = null;
        _worldSolids = null;
        _mission.Destructibles = null;
        _worldMaterials = null;
        _mission.Doors = null;
        _mission.Elevators = null;
        _mission.Interactions = null;
        _mission.GunPickups = null;
        _mission.GenericPickups = null;
        _loadedMissionForWeapon = null;

        // Rockets a vehicle fired in the run being left must not keep flying into the next one, nor its engines
        // and guns keep sounding.
        _vehicleMissiles.Clear();
        _vehicleSounds?.StopAll();
        _vehicleSounds = null;

        if (_activeLevelRoot is not null && !_activeLevelRoot.IsDeleted)
        {
            // Deleting a task does not unexport its names (TaskTree.Delete), and a restart reuses this loop's
            // variables. Left bound, AreaActivate_175.nActive, Car_361.isExploded and the rest answered for the
            // run being left until the new level's runtimes rebound them -- after the conditional containers had
            // already been decided. Mission 7's real T80 (ConditionalContainer_700, which latches itself through
            // this.isRun) came up live at tick 0 whenever the player restarted from inside its trigger area.
            // Unbound, the new level starts from the same zeros a first load does.
            _loop.Variables.UnexportTree(_activeLevelRoot);
            _loop.Tasks.Delete(_activeLevelRoot);
        }
        _activeLevelRoot = null;

        // Back to the front end, so back to the menu theme. Every voice the level started goes with it:
        // an ambience bed outliving its level would follow the player into the menus.
        _sounds?.StopAll();
        _music?.Play(MusicPlayer.MenuMusic);
    }

    // The path-traced lightmapper's phase 1-2 verification: enumerate the lightmapped texels, bake the
    // direct sun + sky, report coverage, and dump the largest few pages as normal and radiance images. A
    // correct normal page reads as a UV unwrap of the object; a correct radiance page shows the sun and its
    // shadows. Dev-only, driven by IGI_BAKELM_DIR, so it never runs in a normal load.
    private void DumpLightmapVerification(
        WorldScene scene, WorldRayCast cast, Vector3f sunDirection, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        IReadOnlyList<LightmapBaker.LightmapTarget> targets = LightmapBaker.EnumerateTargets(scene);

        long covered = 0;
        long total = 0;
        foreach (LightmapBaker.LightmapTarget target in targets)
        {
            total += target.Texels.Length;
            foreach (LightmapBaker.LightmapTexel texel in target.Texels)
            {
                if (texel.IsCovered)
                {
                    covered++;
                }
            }
        }

        _logger.Information(
            "Lightmap", $"Enumerated {targets.Count} target(s); {covered}/{total} texels covered.");

        Vector3f sunColour = _sun?.Colour ?? new Vector3f(1f, 1f, 1f);
        Vector3f skyColour = _sun?.Ambient ?? new Vector3f(0.3f, 0.3f, 0.3f);
        Stepping.Drain(LightmapBaker.BakeDirectAndSky(targets, cast, sunDirection, sunColour, skyColour, 64, _work));

        // Phase 3: fold in the multi-bounce indirect. Build the lightmapped triangles and a BVH over them,
        // then gather several bounces reading the last pass's light. Grey albedo for now (colour is 3b),
        // so this verifies the bounce fills and brightens rather than the colour it carries.
        LightmapBaker.LightmapTriangle[] triangles = LightmapBaker.BuildTriangles(scene, targets, _resources);
        int coloured = triangles.Count(t => MathF.Abs(t.Albedo.X - 0.5f) > 0.02f || MathF.Abs(t.Albedo.Y - 0.5f) > 0.02f || MathF.Abs(t.Albedo.Z - 0.5f) > 0.02f);
        _logger.Information(
            "Lightmap",
            FormattableString.Invariant(
                $"Albedo: {coloured}/{triangles.Length} triangles got a resolved colour; samples ({triangles[0].Albedo.X:F2},{triangles[0].Albedo.Y:F2},{triangles[0].Albedo.Z:F2}) ({triangles[triangles.Length / 2].Albedo.X:F2},{triangles[triangles.Length / 2].Albedo.Y:F2},{triangles[triangles.Length / 2].Albedo.Z:F2})."));
        LightmapBaker.LightmapBvh bvh = LightmapBaker.LightmapBvh.Build(triangles);
        _logger.Information("Lightmap", $"Built a BVH over {triangles.Length} triangle(s); gathering bounces.");
        Stepping.Drain(LightmapBaker.BakeBounces(targets, triangles, bvh, skyColour, 4, 64, _work));

        // A numerical spot-check on the largest page: how many texels it covered, and a few of them with
        // their world position (should sit near the placement) and baked radiance (sunlit near the sun
        // colour, shadowed/skylit near the ambient). This settles what an image cannot show clearly.
        if (targets.OrderByDescending(t => t.Texels.Length).FirstOrDefault() is { } sample)
        {
            int sampleCovered = 0;
            foreach (LightmapBaker.LightmapTexel t in sample.Texels)
            {
                if (t.IsCovered)
                {
                    sampleCovered++;
                }
            }

            Vector3d origin = scene.Placements[sample.PlacementIndex].Position;
            _logger.Information(
                "Lightmap",
                FormattableString.Invariant(
                    $"Largest page p{sample.PlacementIndex} {sample.Width}x{sample.Height}: {sampleCovered} covered, placement at ({origin.X:F1},{origin.Y:F1},{origin.Z:F1})."));

            int shown = 0;
            for (int i = 0; i < sample.Texels.Length && shown < 4; i++)
            {
                if (!sample.Texels[i].IsCovered)
                {
                    continue;
                }

                LightmapBaker.LightmapTexel t = sample.Texels[i];
                Vector3f rad = sample.Radiance[i];
                _logger.Information(
                    "Lightmap",
                    FormattableString.Invariant(
                        $"  texel {i}: pos ({t.WorldPosition.X:F1},{t.WorldPosition.Y:F1},{t.WorldPosition.Z:F1}) n ({t.Normal.X:F2},{t.Normal.Y:F2},{t.Normal.Z:F2}) rad ({rad.X:F2},{rad.Y:F2},{rad.Z:F2})"));
                shown++;
            }
        }

        int dumped = 0;
        foreach (LightmapBaker.LightmapTarget target in targets.OrderByDescending(t => t.Texels.Length).Take(6))
        {
            string stem = FormattableString.Invariant(
                $"lm_p{target.PlacementIndex}_r{target.RecordIndex}_{target.Width}x{target.Height}");

            Bgra32[] normals = new Bgra32[target.Texels.Length];
            LightmapBaker.DumpTargetNormals(target, normals);
            BitmapWriter.Write(
                Path.Combine(outputDirectory, stem + "_normal.bmp"), target.Width, target.Height, normals);

            Bgra32[] radiance = new Bgra32[target.Texels.Length];
            LightmapBaker.DumpTargetRadiance(target, radiance, exposure: 1f);
            BitmapWriter.Write(
                Path.Combine(outputDirectory, stem + "_radiance.bmp"), target.Width, target.Height, radiance);

            dumped++;
        }

        _logger.Information("Lightmap", $"Dumped {dumped} page(s) to {outputDirectory}.");
    }

    // Puts a weapon in the player's hands and loads the model its roster entry names.
    //
    // Hands the player everything the mission declares, with the ammunition each weapon draws from.
    // WHICH of them ends up in hand is no longer decided here: the weapon cycler's index starts at -1
    // and the first tick lands on entry 0 of DefineHumanPlayerWeaponCycle, stepping on until it finds
    // one that is carried. That retires the divergence this function used to carry -- see
    // the original. The renderer's model follows whatever ends up in hand.
    private IHeldWeaponPresenter? CarryLoadout(
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
                _logger.Warning("Weapon", $"{declared.WeaponId} is not in the roster; it is not carried.");
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
                if (string.Equals(declared.AmmoId, "AMMO_ID_M203", StringComparison.Ordinal))
                {
                    candidate.Weapons?.SetM203Ammunition(pool);
                }
            }
        }

        IHeldWeaponPresenter? presenter = _presentation.CreateHeldWeapon(ViewModelRequest());
        if (presenter is not null)
        {
            presenter.Materials = _materials;
        }

        return presenter;
    }

    // Tops the player's inventory up to the whole roster while the all-weapons cheat is in force. Runs
    // before every player tick, so it covers both mission start and switching the cheat on mid-mission;
    // a weapon already carried is left exactly as it is. Switching the cheat off takes nothing away.
    private void GrantCheatWeapons(PlayerCamera player)
    {
        if (!PlayerCheatOptions.Current.AllWeapons
            || _weaponTypes is not { } roster
            || _ammoTypes is not { } ammunition)
        {
            return;
        }

        int added = player.EnsureAllWeapons(roster.Weapons, ammunition);
        if (added == 0)
        {
            return;
        }

        _logger.Information("Player", $"All-weapons cheat added {added} weapon(s).");

        // A level that declared no loadout never built a presenter for the hand, so the first granted
        // weapon would be carried but never drawn.
        if (_heldWeapon is null && player.View is not null
            && _presentation.CreateHeldWeapon(ViewModelRequest()) is { } presenter)
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
        new(_device, _resources, _magic, _logger, _work);

    /// <summary>The player's grunt — <c>player_hit_</c> through <c>0x4637C0</c>'s per-human rate limit.</summary>
    /// <remarks>
    /// Flat, not positional: the sound belongs to the player, who is the listener. The limit is what lets
    /// sustained fire stagger the player without stuttering the sound.
    /// </remarks>
    /// <summary>
    /// The shake a bullet or a blow puts in the view — <c>0x412440</c> and <c>0x412CE0</c>: a draw clamped
    /// to <c>[0.25, 1]</c> scales the full 5 cm and 0.75 degrees, for fifteen ticks.
    /// </summary>
    private void InstallPlayerHitShake()
    {
        float share = Math.Clamp(_soundRandom.NextSingle(), 0.25f, 1f);
        _player!.InstallViewShake(
            HumanViewShake.HitPositionUnits * share, HumanViewShake.HitAngleRadians * share, HumanViewShake.HitTicks);
    }

    private void PlayPlayerGrunt()
    {
        if (_loop.Clock.TickCount < _playerHitQuietUntil)
        {
            return;
        }

        _playerHitQuietUntil = _loop.Clock.TickCount + HumanVariantSound.HitCooldownTicks(_soundRandom);
        PlayHitSound(HumanVariantSound.PlayerHitPrefix, HumanVariantSound.PlayerHitVariants, null);
    }

    /// <summary>A soldier's close-combat blow landing on the player — the blunt-hit handler <c>0x412CE0</c>.</summary>
    /// <param name="attacker">The striking soldier's position.</param>
    /// <param name="damage">The blow's damage, the settings row's close-combat figure.</param>
    /// <remarks>
    /// The zone is 13 when the attacker is in front of the player and 14 when behind, the damage is that
    /// zone's multiplier times the blow (scaled by difficulty, as every player hit is), and a surviving
    /// player grunts and then hears <c>punched_01</c> or <c>punched_02</c> by the parity of a random draw.
    /// Each corner of the red flash takes its own draw.
    /// </remarks>
    private void StrikePlayer(Vector3d attacker, float damage)
    {
        if (_player is null || _playerDead)
        {
            return;
        }

        // The body's forward axis against the direction from the attacker: negative when facing it.
        Vector3d towardsPlayer = new(
            _player.Body.Position.X - attacker.X,
            _player.Body.Position.Y - attacker.Y,
            _player.Body.Position.Z - attacker.Z);
        float yaw = _player.Body.Yaw;
        double facing = (towardsPlayer.X * -Math.Sin(yaw)) + (towardsPlayer.Y * Math.Cos(yaw));
        int zone = facing < 0.0 ? PlayerBluntFrontZone : PlayerBluntBackZone;

        _playerHealth.Apply(zone, damage);
        if (!_playerHealth.IsDead)
        {
            PlayPlayerGrunt();
            _sounds?.PlayDefinition(
                (_soundRandom.NextRaw() & 1) != 0 ? "punched_01" : "punched_02",
                positionInUnits: null,
                priority: 250);
        }

        InstallPlayerHitShake();
        _damageFlash.HitEveryCorner(
            _soundRandom.NextSingle(),
            _soundRandom.NextSingle(),
            _soundRandom.NextSingle(),
            _soundRandom.NextSingle());

        if (_playerHealth.IsDead)
        {
            // A close-combat blow is melee, not a round.
            BeginPlayerDeath(attacker, zone, bone: -1, WeaponCategory.Knife);
        }
    }

    // 0x412CE0's two zones: 13 for a blow from in front, 14 from behind.
    private const int PlayerBluntFrontZone = 13;
    private const int PlayerBluntBackZone = 14;

    /// <summary>Puts the player through the whole death sequence, whatever killed them.</summary>
    /// <param name="from">Where the killing thing came from — the shooter's muzzle, or the blast.</param>
    /// <param name="zone">The zone the killing damage landed on.</param>
    /// <param name="bone">The bone the round went through, or <c>-1</c> when nothing so precise.</param>
    /// <param name="category">The killing weapon's category, which picks the fall's family.</param>
    /// <remarks>
    /// Extracted from the guard-round handler so that EVERYTHING lethal runs it — a grenade that
    /// zeroes the health must kill exactly the way a bullet does, or the player stands at zero
    /// health forever, which is precisely the bug this fixed.
    /// </remarks>
    private void BeginPlayerDeath(Vector3d from, int zone, int bone, WeaponCategory category)
    {
        if (_player is null || _playerDead)
        {
            return;
        }

        // Locomotion state 47 (0x40E560): the clip comes from the LAST ZONE STRUCK's row in the
        // player's own hit-zone table, one of its two reactions by rand(2). The player stops taking
        // input from here on -- it is a state, not a message.
        _playerDead = true;
        _player.ResetOptics();
        _player.StopWeaponsForDeath();
        StopBinocularTransitionSound();

        // The player receives no further simulation ticks after death, so the weapon sound player
        // cannot observe a quiet tick and retire an automatic loop on its own.
        _weaponSounds?.Reset();
        int fall = HumanPlayerDeath.Choose(zone, HumanHitZoneTable.Player, _shotRandom.Next(2));

        // The lethal hit has already written the retail corner flash. Do not replace it with a uniform
        // full-screen wash here: 0x412440 writes the flash as part of damage receipt, before it changes
        // the locomotion state to death.

        // Which way the body is thrown: the facing whose backward axis points away from the killer,
        // exactly as the soldier death faces the incoming round (SoldierDeath.TurnToTheShot).
        double throwX = _player.Body.Position.X - from.X;
        double throwY = _player.Body.Position.Y - from.Y;
        double throwLength = Math.Sqrt((throwX * throwX) + (throwY * throwY));
        float throwFacing = throwLength > 1e-6
            ? (float)Math.Atan2(throwX / throwLength, -(throwY / throwLength))
            : _player.Body.Yaw;

        _soldiers?.KillPlayerBody(
            fall,
            _player.Body.Position,
            throwFacing,
            bone,
            new Vector3d(
                _player.Body.Position.X - from.X,
                _player.Body.Position.Y - from.Y,
                _player.Body.Position.Z - from.Z),
            category,
            new Vector3d(
                _player.Body.Velocity.X,
                _player.Body.Velocity.Y,
                _player.Body.Velocity.Z));

        if (_worldSolids is { } deathSolids)
        {
            WorldRayCast deathCaster = deathSolids.Caster();
            _deathCamera.Begin(
                _player.Body.Position,
                throwFacing,
                (f, t, out h) =>
                {
                    bool blocked = deathCaster(f, t, out WorldRayHit wall);
                    h = blocked ? wall.Position : default;
                    return blocked;
                });
        }

        // And the death sound -- state 47's own sub_4638A0(human, "player_death_", 3).
        PlayDeathSound(HumanPlayerDeath.SoundPrefix, HumanPlayerDeath.SoundVariants, null);
    }

    /// <summary>Routes a resolved body landing into the desktop-owned presentation and death state.</summary>
    private void ResolvePlayerFallImpact(PlayerCamera player, HumanFallImpact impact)
    {
        // 0x408460 arms the camera's dip on every ground contact, before it has decided whether the
        // landing hurt: a step off a crate drops the view a little, a long drop drops it the full
        // quarter-metre. 0x4659B0 is two stores, so this is unconditional and cannot fail.
        player.InstallLandingViewKick(impact.ViewKickUnits);

        // The ground's own landing sound, which is what every harmless drop sounds like. Both airborne
        // states play it on contact from the struck material, independently of the fall-damage cue
        // below -- a hurting fall is heard twice, once as the surface and once as the player.
        if (player.TakeLandingSound() is { } landing)
        {
            PlayLandingSound(landing);
        }

        // State 35 names one of these three definitions only when damage is non-zero. Like the player's
        // own hit and death sounds it belongs to the listener, so it is flat rather than world-panned.
        if (!string.IsNullOrEmpty(impact.SoundName))
        {
            _sounds?.PlayDefinition(impact.SoundName, priority: 250);
        }

        // Every ground contact posts type 11. A safe landing carries five metres; a damaging one ten.
        _soldiers?.Simulation.NotifyGroundImpact(
            player.Body.Position,
            player.Body.Position,
            impact.HearingRadiusUnits);

        if (impact.Damage <= 0f)
        {
            return;
        }

        // PlayerBody has already added the direct, difficulty-independent dose to the shared health.
        // Raise the desktop wash that the render pass actually draws rather than the body's standalone one.
        _damageFlash.Hit(Vector3f.Zero, _soundRandom.NextSingle());
        _logger.Information(
            "Player",
            $"Fall impact {impact.SpeedMetresPerSecond:F2} m/s dealt {impact.Damage:F3} "
                + $"({_playerHealth.RemainingFraction:P0} left), sound '{impact.SoundName}'.");

        if (_playerHealth.IsDead)
        {
            // Zone 13 is the retail fall bucket. With no external attacker the source is the feet,
            // leaving the death fall aligned to the player's current facing.
            BeginPlayerDeath(
                player.Body.Position,
                HumanDamage.FrontZone,
                bone: -1,
                WeaponCategory.Gun);
        }
    }

    /// <summary>Applies one native electric-fence shock after the player's 45-tick guard permits it.</summary>
    private void ResolvePlayerElectricFence(PlayerCamera player, WorldInteraction fence)
    {
        if (_playerHealth.IsDead)
        {
            return;
        }

        float healthBefore = _playerHealth.RemainingFraction;
        _logger.Information(
            "Player.Fence",
            $"contact task={fence.Task.Id} powered={(fence.IsElectrified ? "true" : "false")} "
                + $"healthBefore={healthBefore:P0} position=({player.Body.Position.X:F0},"
                + $"{player.Body.Position.Y:F0},{player.Body.Position.Z:F0})");

        ElectricFenceShock shock = ElectricFenceContact.Shock(_playerHealth, _electricFenceDose);
        float dealt = shock.Damage;
        if (dealt <= 0f)
        {
            _logger.Warning(
                "Player.Fence",
                $"contact produced no damage task={fence.Task.Id} "
                    + $"health={_playerHealth.RemainingFraction:P0} hits={_playerHealth.Hits}.");
            return;
        }

        // 0x412F80: the full shake for forty-five ticks, and four separate corner draws.
        player.InstallViewShake(
            HumanViewShake.HitPositionUnits, HumanViewShake.HitAngleRadians, HumanViewShake.FenceTicks);
        _damageFlash.HitEveryCorner(
            _soundRandom.NextSingle(),
            _soundRandom.NextSingle(),
            _soundRandom.NextSingle(),
            _soundRandom.NextSingle());
        if (!shock.IsLethal)
        {
            // 0x412F80 selects this fence-specific cue after it has added the direct zone-13 dose.
            _sounds?.PlayDefinition("elfence_spark1", priority: 250);
        }

        _logger.Information(
            "Player",
            $"Electric fence shock dealt {dealt:F3} ({_playerHealth.RemainingFraction:P0} left).");

        if (shock.IsLethal)
        {
            BeginPlayerDeath(fence.Anchor, ElectricFenceContact.Zone, bone: -1, WeaponCategory.Gun);
        }
    }

    // Keeps the drawn model on whatever the cycle has put in hand. Called every frame; it only does
    // anything on the tick the weapon actually changed.
    private void FollowHeldWeapon(PlayerCamera candidate, IHeldWeaponPresenter? held, LoadedMission loadedMission)
    {
        if (held is null || !candidate.WeaponChanged)
        {
            return;
        }

        candidate.AcknowledgeWeaponChange();

        if (candidate.HeldWeaponType is not { } weapon)
        {
            return;
        }

        if (held.Load(loadedMission, weapon.GunModel))
        {
            _logger.Information("Weapon", $"Holding {weapon.ScriptId} ('{weapon.GunModel}') on view bone "
                + $"{HumanAttachPoints.FirstPersonWeapon}.");
        }
    }

    private MenuManagerTask? FindMenuManager()
    {
        foreach (GameTask task in _loop.Tasks.Root.Descendants())
        {
            if (task is MenuManagerTask manager)
            {
                return manager;
            }
        }

        return null;
    }

    /// <summary>Gives every authored vehicle that can fire the guns its model carries, and aims them at the player.</summary>
    /// <remarks>
    /// <para>
    /// A vehicle's guns are attachments of its model (<c>0x4F4800</c> spawns them through <c>0x4C4950</c>);
    /// each <c>TASKTYPE_AISTATIONARYGUN</c> names its weapon by model (<c>0x413C00</c>). A gun mounted on
    /// another gun -- a T80's coaxial M2HB on its turret -- sits at the sum of both attachment offsets.
    /// </para>
    /// <para>
    /// Missile rows (T80, APC) launch into the vehicle missile simulation; gunshot rows (M2HB, MIL) go
    /// through the soldiers' round cast at probability 1, the value <c>0x46F950</c>'s fire message carries.
    /// </para>
    /// </remarks>
    /// <summary>The vehicle whose gun's round is being cast, so the cast passes through its own hull.</summary>
    private GameTask? _vehicleRoundOwner;

    private void ArmVehicles(LoadedMission loaded, LevelTask levelRoot, WeaponTypeRegistry roster, PlayerCamera player)
    {
        if (_mission.Cutscenes is not { } runtime || _magic is not { } magic)
        {
            return;
        }

        int armed = 0;
        List<string> weaponModels = [];
        foreach (DeclaredTask vehicle in levelRoot.Descendants().OfType<DeclaredTask>())
        {
            if (vehicle.Type.Name is ("Car" or "Heli")
                && vehicle.TryGetValue("Model", out object? rawZoneModel)
                && rawZoneModel is string zoneModel)
            {
                runtime.ConfigureDeathZones(
                    vehicle,
                    VehicleDeathZone.FromModel(zoneModel, ModelOf, magic));
            }

            // A helicopter's gun runs only while its AnimTask flies it; CutsceneRuntime gates that.
            if (vehicle.Type.Name is not ("Car" or "Heli")
                || !vehicle.TryGetValue("Can fire", out object? rawCanFire)
                || rawCanFire is not string canFire
                || string.IsNullOrWhiteSpace(canFire)
                || !vehicle.TryGetValue("Model", out object? rawModel)
                || rawModel is not string modelName)
            {
                continue;
            }

            IReadOnlyList<VehicleGun> guns = VehicleGun.FromModel(
                modelName,
                ModelOf,
                magic,
                roster,
                (attachment, weaponModel) => _logger.Warning(
                    "Ai",
                    $"Vehicle gun {attachment} names weapon model {weaponModel}, which no weapon row uses."));
            if (guns.Count == 0)
            {
                continue;
            }

            runtime.ConfigureVehicleGuns(vehicle, guns);
            weaponModels.AddRange(guns.Select(static gun => gun.Mount.WeaponModel));
            armed++;
            _logger.Information(
                "Ai",
                $"{vehicle.Type.Name} {vehicle.Id} ({modelName}) carries "
                    + string.Join(", ", guns.Select(static gun => gun.Weapon.ScriptId)) + ".");
        }

        if (armed == 0)
        {
            return;
        }

        // The guns turn their mounts and carry weapon models of their own, which the mount models do not include.
        if (_scene is { } armedScene)
        {
            runtime.BindVehicleGunPlacements(armedScene);
        }

        _soldiers?.ArmVehicleGuns(loaded, weaponModels, runtime.VehicleGunVisuals);

        // A vehicle gun aims at what its vehicle's vision holds: the human's model origin (Human+32),
        // about a metre over the feet standing and half that crouched -- not the eye.
        runtime.VehicleCombatTarget = () => !_playerHealth.IsDead
            ? player.Body.Position + new Vector3d(
                0d,
                0d,
                player.Body.IsCrouched ? AiSoldier.HumanOriginCrouchedInUnits : AiSoldier.HumanOriginStandingInUnits)
            : null;
        // A vehicle's detection sweep tests the point the human reports to every viewer -- the soldiers' mark over
        // the feet -- along the soldiers' sight ray (0x489B20), which passes its own body.
        runtime.VehicleSightTarget = () => !_playerHealth.IsDead
            ? player.Body.Position + new Vector3d(0d, 0d, AiSoldier.DetectionMarkAboveFeet(player.Body.IsCrouched))
            : null;
        runtime.VehicleCombatLineOfSight = (owner, from, to) =>
            _worldTrace is null
            || SoldierSimulation.HasLineOfSight(_worldTrace, from, to, AiSoldier.SightObstructionTolerance, owner);
        runtime.VehicleCombatShotFired = shot =>
        {
            if (shot.IsHitscan)
            {
                if (roster.TryGetByScriptId(shot.WeaponScriptId, out WeaponType gunshot))
                {
                    _vehicleRoundOwner = shot.Owner;
                    try
                    {
                        _soldiers?.Simulation.RoundHandler?.Invoke(shot.Origin, shot.Target, 1f, gunshot, null);
                    }
                    finally
                    {
                        _vehicleRoundOwner = null;
                    }
                }

                return;
            }

            // The report is the gun's, on the hold edges Gun_Fire plays it on (VehicleSoundPlayer); the missile
            // task makes no launch sound of its own.
            _vehicleMissiles.Spawn(
                shot.Origin,
                shot.Target,
                shot.MissileType,
                shot.MuzzleVelocityMetresPerSecond,
                shot.Direction,
                shot.ModelName,
                collisionOwner: shot.Owner,
                sourceWeaponId: roster.TryGetByScriptId(shot.WeaponScriptId, out WeaponType launcher) ? launcher.WeaponId : 0);
        };
        _logger.Information("Ai", $"{armed} armed vehicle(s).");

        MefModel? ModelOf(string name) =>
            loaded.LevelPack.TryGetModel(name, out MefModel? mef) || loaded.CommonPack.TryGetModel(name, out mef)
                ? mef
                : null;
    }

    /// <summary>Kills the player when a moving vehicle's death zone reaches them (<c>0x45FA80</c>).</summary>
    private void CheckRunOver()
    {
        if (_player is null || _playerDead || _mission.Cutscenes is not { } runtime)
        {
            return;
        }

        Vector3d origin = _player.Body.Position + new Vector3d(0d, 0d, AiSoldier.HumanOriginStandingInUnits);
        if (!runtime.TryRunOver(origin, out GameTask? vehicle) || vehicle is null)
        {
            return;
        }

        _playerHealth.Apply(VehicleDeathZone.Zone, VehicleDeathZone.Damage);
        _logger.Information("Ai", $"The player was run over by {vehicle.Type.Name} {vehicle.Id}.");
        if (_playerHealth.IsDead)
        {
            Vector3d from = runtime.TryGetDrawTransform(vehicle, out Vector3d metres, out _)
                ? metres * WorldScene.UnitsPerMetre
                : origin;
            BeginPlayerDeath(from, VehicleDeathZone.Zone, bone: -1, WeaponCategory.Knife);
        }
    }

    /// <summary>Parses the test-only outro forcing list: "Var=Value,Var=Value".</summary>
    private static List<(string Name, string Value)> TestOutroWrites(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
        {
            return [];
        }

        List<(string, string)> writes = [];
        foreach (string part in spec.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = part.IndexOf('=');
            if (equals > 0)
            {
                writes.Add((part[..equals].Trim(), part[(equals + 1)..].Trim()));
            }
        }
        return writes;
    }
}
