using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Audio;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.ViewModel;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Player;

/// <summary>I.G.I. 2's own player for the host: the ported <c>HumanPlayer</c> over the level's collision.</summary>
/// <remarks>
/// <para>
/// The level's <c>HumanPlayer</c> task says where the player stands and which way it faces. The collision world is
/// built from the same placements and ground the scene walk produced; the tuning is the one <c>humanplayer.qvm</c>
/// declared at start-up. The player is made as <c>HumanPlayer_CreateHandler</c> makes it and ticked as the original
/// ticks it: the channels read, <c>HumanPlayer_Update</c>, then the delayed half.
/// </para>
/// <para>
/// The body plays the clips the level's packs load, picked by state through the held weapon
/// (<see cref="Igi2HumanAnimation"/>), and the view carries the level's first-person model with the weapon in hand,
/// which the host draws through <see cref="ITitleFirstPerson"/>. The level's sound definitions are read as the level is,
/// and what the player and its weapons sound plays through the host's mixer. Not yet behind it: water, AI events and
/// conveyors.
/// </para>
/// </remarks>
public sealed class Igi2Gameplay : ITitleGameplay
{
    /// <summary>What a terrain hit reports as struck.</summary>
    public const string TerrainOwner = "Terrain";

    /// <summary>The model the first-person view falls back to, <c>005_01_1</c>.</summary>
    public const string FirstPersonFallbackModel = "005_01_1";

    /// <summary>Gets the shared instance.</summary>
    public static Igi2Gameplay Instance { get; } = new();

    /// <inheritdoc/>
    public ITitlePlayer? CreatePlayer(in TitlePlayerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Services);
        if (!PlayerSpawn.TryFindGameplay(request.World.LevelRoot, out PlayerSpawnPoint spawn))
        {
            return null;
        }

        Igi2CollisionWorld world = BuildCollision(request.World, request.Built);
        request.Services.TryGet(out Igi2PlayerTuning? tuning);
        Igi2HumanPlayerGeneral general = tuning?.General ?? Igi2HumanPlayerGeneral.Unset;
        ArgumentNullException.ThrowIfNull(request.World.Mission);
        Igi2AnimationLibrary clips = Igi2AnimationLibrary.Load(request.World.Mission.PacksInLoadOrder, request.World.Resources);
        Func<string, Formats.World.MefModel?> models = Models(request.World);
        Igi2BoneHierarchy? rig = models(spawn.Model)?.BoneHierarchy;
        string? armsModel = FirstPersonModel(models, PlayerSpawn.FindGameplayTask(request.World.LevelRoot) as Igi2HumanPlayerTask);
        Igi2BoneHierarchy? arms = armsModel is null ? null : models(armsModel)?.BoneHierarchy;
        Igi2PlayerWeapons? weapons = Arm(request.Services, request.World.LevelRoot, tuning, clips);
        Igi2Sounds sounds = LevelSounds(request);
        Igi2TitlePlayer player = Igi2TitlePlayer.Spawn(world, general, spawn.Position, spawn.Gamma, request.Difficulty, clips, rig, weapons, arms, sounds);
        if (request.Services.TryGet(out Igi2WhizzbyClock? clock))
        {
            player.Human.WhizzbyClock = clock;
        }

        if (request.Services.TryGet(out Igi2DeadBodySoundCycle? deadBodySounds))
        {
            player.Human.DeadBodySounds = deadBodySounds;
        }

        player.Human.Model = Igi2BodyModel.From(spawn.Model, models(spawn.Model));
        player.Human.Collider = new Igi2BoneCollider(player.Human, player.Human);
        world.AddBody(player.Human.Collider);
        if (weapons is not null)
        {
            weapons.Effects = new Igi2LevelWeaponEffects(sounds, () => player.Human.View.Position);
            weapons.ImpactEffects = new Igi2LevelImpactEffects(sounds);
        }

        if (request.Services.TryGet(out Igi2GameMaterials? materials))
        {
            if (weapons is not null)
            {
                weapons.World = new Igi2LevelGunShotWorld(world, materials);
                if (request.Services.TryGet(out Physics.Igi2PhysicsObjectTypes? physicsTypes))
                {
                    Weapons.Igi2ExplosionWorld explosions = new(player.Human.Random, world, materials, player.Dynamic)
                    {
                        Effects = new Igi2LevelExplosionEffects(sounds),
                    };
                    explosions.AddTarget(player.Human);
                    Physics.Igi2PhysicsWorld flight = new(world, materials, static owner => !Igi2ObjectCollision.PassesThrough(owner));
                    Weapons.Igi2Grenades grenades = new(physicsTypes, models, flight, player.Dynamic)
                    {
                        Effects = explosions,
                    };
                    weapons.Grenades = grenades;
                    player.Grenades = grenades;
                    player.Explosions = explosions;
                }
            }

            if (request.Services.TryGet(out Igi2HumanWeapons? armoury))
            {
                player.Human.Pickups = Pickups.Igi2PickupList.Build(request.World.LevelRoot, armoury.Tables, models, world, materials);
                player.Human.Pickups.Collision = world;
                player.Human.Pickups.Random = player.Human.Random;
                player.Human.Pickups.PlaySound = (name, at) => sounds.New(name, at);
            }
        }

        if (request.Services.TryGet(out Igi2AnimTriggers? triggers))
        {
            player.Human.AnimTriggers = triggers;
        }

        if (models(spawn.Model) is { } body)
        {
            // BoneDynCubeObj_SetLODVirModel (igi2 0x567BE0): the root's bind translation and the mesh's radius.
            player.Human.ModelRadius = body.NativeMarkerBound;
            if (body.BoneHierarchy is { BoneCount: > 0 } bones)
            {
                player.Human.RootTranslation = bones.Translations[0];
            }
        }

        player.Human.View.ObjectModel = armsModel ?? string.Empty;
        player.Human.WeaponCycle = tuning?.WeaponCycle ?? [];
        IReadOnlyDictionary<Engine.Tasks.GameTask, string>? worn = request.World.LevelRoot is { } facesRoot ? Igi2HumanFaces.Assign(facesRoot) : null;
        player.Soldiers = request.World.LevelRoot is { } levelRoot
            ? SpawnSoldiers(
                levelRoot,
                request.Services,
                world,
                clips,
                models,
                tuning,
                sounds,
                player.Human.Random,
                player.Human.Difficulty,
                player.Human.Pickups,
                player.Grenades,
                request.Natives,
                request.Services.TryGet(out Scripting.Igi2ScriptFunctions? lineFunctions) ? lineFunctions : null,
                worn)
            : null;

        // The level's figures: each its face, its clips and its first pause, from the module's blink stream; then its weapon expression.
        if (request.World.LevelRoot is { } figuresRoot && worn is not null)
        {
            IgiRandom blink = new(unchecked((uint)-912896271), unchecked((uint)-463112362), unchecked((uint)-1522052915));
            List<World.Igi2EditBoneObjTask> figures = [];
            foreach (World.Igi2EditBoneObjTask figure in Descendants(figuresRoot).OfType<World.Igi2EditBoneObjTask>())
            {
                figure.Prepare(clips, models, worn.TryGetValue(figure, out string? face) ? face : figure.Model, blink);
                if (request.Natives is { } figureNatives && request.Services.TryGet(out Scripting.Igi2ScriptFunctions? figureFunctions))
                {
                    figure.AfterLoad(figureNatives, figureFunctions);
                }

                figures.Add(figure);
            }

            player.Figures = figures;
        }
        if (player.Soldiers is { } soldiers && player.Explosions is { } blasts)
        {
            foreach (Soldiers.Igi2HumanSoldier soldier in soldiers.Bodies)
            {
                blasts.AddTarget(soldier);
            }
        }

        // Human_ptQTaskList: every human registered in the world, the bodies each move is kept out of.
        List<Igi2Human> humans = [player.Human];
        if (player.Soldiers is { } registered)
        {
            humans.AddRange(registered.Bodies);
        }

        // Particle2DDynCubeObj's module: one stream and collision phase for every system the level makes.
        Igi2Particles particles = new(world, player.Dynamic);
        foreach (Igi2Human human in humans)
        {
            if (human.Surroundings is Igi2LevelSurroundings surroundings)
            {
                surroundings.Humans = humans;
                surroundings.Particles = particles;
            }
        }

        player.Level = LevelRun(request, player);
        player.Saves = MissionSaves(request, player);
        return player;
    }

    /// <summary>
    /// The mission's save games, kept where retail keeps them, in the game's own <c>savegames</c> folder, so each game reads the
    /// other's; and the weapons the last mission left, given to the player as the level starts when it asks for them
    /// (<c>Game_LoadCarryOver</c>, after the level's objects).
    /// </summary>
    private static Saves.Igi2MissionSaves? MissionSaves(in TitlePlayerRequest request, Igi2TitlePlayer player)
    {
        if (player.Level is not { } level)
        {
            return null;
        }

        string folder = request.World.Resources.FileSystem.TryGetDevice(Engine.Vfs.LocalFileDevice.DeviceName, out Engine.Vfs.IFileDevice? device)
            && device is Engine.Vfs.LocalFileDevice local
            ? local.RootDirectory
            : Path.Combine(Core.Configuration.SettingsPaths.GetUserDirectory(), "igi2");
        request.Services.TryGet(out Config.Igi2GameConfig? config);
        int mission = request.World.Mission.Descriptor.Id;
        Saves.Igi2MissionSaves saves = new(
            new Saves.Igi2SaveSlots(folder),
            () => new Saves.Igi2SaveOwner(config?.ActiveIndex ?? 0, config?.Active?.Difficulty ?? (int)player.Human.Difficulty, mission),
            level,
            player.Human);
        if (PlayerSpawn.FindGameplayTask(request.World.LevelRoot) is Igi2HumanPlayerTask task)
        {
            saves.LoadCarryOver(task);
        }

        return saves;
    }

    /// <summary>Makes the level's soldiers over its collision, each armed by its own weapon script.</summary>
    /// <param name="root">The level.</param>
    /// <param name="services">The session's tables.</param>
    /// <param name="world">The level's collision.</param>
    /// <param name="clips">The level's clips.</param>
    /// <param name="models">Resolves a placed model to its mesh.</param>
    /// <param name="tuning">The player tuning, whose ammunition limits bound every human's inventory, or <see langword="null"/>.</param>
    /// <param name="sounds">The level's sounds.</param>
    /// <param name="random">The session's run stream.</param>
    /// <param name="difficulty">The session's difficulty, which tunes the soldiers' hit points.</param>
    /// <param name="pickups">The level's pickups, which a dying soldier's weapon joins; <see langword="null"/> for none.</param>
    /// <param name="grenades">The level's grenades, which a soldier's throw joins; <see langword="null"/> for none.</param>
    /// <param name="natives">The session's script namespace, which the minds' type names are read in; <see langword="null"/> to leave them unread.</param>
    /// <param name="functions">The natives those names may call.</param>
    /// <param name="worn">The model each soldier wears, its face given (<see cref="Igi2HumanFaces.Assign"/>).</param>
    /// <returns>The soldiers.</returns>
    /// <remarks>
    /// Every human's inventory is made within the ammunition limits (<c>Human_CreateHandler</c>, <c>igi2 0x4FDC90</c>) and filled by
    /// its <c>Weapon</c> script once loaded (<c>Human_UpdateInternalDataAfterCarryoverHandler</c>, <c>0x4FF440</c>). Each soldier
    /// stands (<see cref="Soldiers.Igi2StandingSoldierMind"/>) until the level's AI gives it its mind (<see cref="Ai.Runtime.Igi2LevelAi"/>).
    /// </remarks>
    public static Soldiers.Igi2Soldiers SpawnSoldiers(
        Engine.Tasks.GameTask root,
        Engine.Titles.IServiceCatalog services,
        Igi2CollisionWorld world,
        Igi2AnimationLibrary? clips,
        Func<string, Formats.World.MefModel?> models,
        Igi2PlayerTuning? tuning,
        Igi2Sounds sounds,
        IgiRandom random,
        OpenIGI.Game.Ai.AiDifficulty difficulty,
        Pickups.Igi2PickupList? pickups = null,
        Weapons.Igi2Grenades? grenades = null,
        OpenIGI.Scripting.Runtime.NativeRegistry? natives = null,
        Scripting.Igi2ScriptFunctions? functions = null,
        IReadOnlyDictionary<Engine.Tasks.GameTask, string>? worn = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(sounds);
        ArgumentNullException.ThrowIfNull(random);
        services.TryGet(out Igi2HumanWeapons? armoury);
        services.TryGet(out Igi2GameMaterials? materials);
        services.TryGet(out Igi2AnimTriggers? triggers);
        services.TryGet(out Igi2DeadBodySoundCycle? deadBodySounds);
        Soldiers.Igi2Soldiers soldiers = Soldiers.Igi2Soldiers.Spawn(
            root,
            (task, rig) =>
            {
                Igi2LevelSurroundings surroundings = new(world, sounds) { Materials = materials, Pickups = pickups, Models = models, Grenades = grenades };
                IIgi2PlayerWeaponControl weapon = Igi2NoWeaponControl.Instance;
                if (armoury is not null)
                {
                    Igi2Inventory inventory = armoury.CreateInventory(tuning?.AmmoLimits ?? []);
                    armoury.RunWeaponScript(inventory, task.WeaponScript);
                    weapon = new Igi2PlayerWeapons(inventory, armoury.FireTaskTypes, clips);
                }

                IIgi2PlayerWeaponControl held = weapon;
                Igi2HumanAnimation animation = new(clips, rig, Soldiers.Igi2SoldierStateAnimations.Table, () => held.BodyClips(rig));
                return (surroundings, weapon, animation);
            },
            models,
            random,
            worn);
        foreach (Soldiers.Igi2HumanSoldier body in soldiers.Bodies)
        {
            ((Igi2LevelSurroundings)body.Surroundings).Owner = body;

            // A soldier's rounds go into the same world as the player's, its weapon sounds at its head.
            if (body.Weapon is Igi2PlayerWeapons arms)
            {
                arms.Effects = new Igi2LevelWeaponEffects(sounds, body.CheapHeadBonePosition);
                arms.ImpactEffects = new Igi2LevelImpactEffects(sounds);
                if (materials is not null)
                {
                    arms.World = new Igi2LevelGunShotWorld(world, materials);
                }
            }

            body.Mind = new Soldiers.Igi2StandingSoldierMind();
            body.Difficulty = difficulty;
            body.AnimTriggers = triggers;
            if (deadBodySounds is not null)
            {
                body.DeadBodySounds = deadBodySounds;
            }

            if (body.Animation is Igi2HumanAnimation animation)
            {
                animation.TriggerTarget = new Soldiers.Igi2SoldierBodyTriggers(body);
            }
            if (body.Collider is { } collider)
            {
                world.AddBody(collider);
            }
        }

        // HumanAI_UpdateInternalDataHandler reads each mind's AI and animation types from their names.
        if (natives is not null && functions is not null)
        {
            foreach (Ai.Igi2HumanAiTask ai in Igi2TaskTree.Descendants(root).OfType<Ai.Igi2HumanAiTask>())
            {
                ai.ResolveTypes(natives, functions);
            }
        }

        if (services.TryGet(out Ai.Igi2HumanAiConfig? config))
        {
            Soldiers.Igi2Soldiers.AfterLoad(root, config, (int)difficulty);
        }

        return soldiers;
    }

    /// <summary>The level's tick, with the player bound to its <c>HumanPlayer</c> task, as <c>Flow_RunChildren</c> (<c>igi2 0x401A50</c>) runs it.</summary>
    /// <param name="request">The level and the session.</param>
    /// <param name="player">The player.</param>
    /// <returns>The level's run, or <see langword="null"/> when the level's scripts are not at hand or it places no player in its tree.</returns>
    public static Igi2LevelRun? LevelRun(in TitlePlayerRequest request, Igi2TitlePlayer player)
    {
        ArgumentNullException.ThrowIfNull(request.Services);
        ArgumentNullException.ThrowIfNull(player);
        if (request.Natives is not { } natives
            || request.World.LevelRoot is not { } root
            || !request.Services.TryGet(out Scripting.Igi2ScriptFunctions? functions)
            || PlayerSpawn.FindGameplayTask(root) is not { } task)
        {
            return null;
        }

        if (request.Services.TryGet(out Igi2FlowState? flow))
        {
            flow.Reset();
            player.Flow = flow;
        }

        Igi2LevelRun level = new(root, natives, functions) { Dynamic = player.Dynamic };

        // GameScreen_CreateHandler: the in-game menu, made of ingamemenu.qsc as the level's screen is; the shell presents it.
        if (request.Services.TryGet(out Menus.Igi2InGameMenu? menu) && menu.Load(root, request.World.Resources, natives))
        {
            menu.LevelFailed = () => level.LevelFlow?.IsFailed == true;
            menu.PlayerDead = () => player.Human.IsDead;
            player.Menu = menu;
        }
        if (request.Services.TryGet(out Igi2CutsceneShake? shake))
        {
            level.CutsceneShake = shake;
        }

        level.Bind(task, player.RunPlayer);
        if (task is Igi2HumanPlayerTask playerTask)
        {
            playerTask.Human = player.Human;
        }

        player.Input = task.Children.OfType<Igi2HumanPlayerInputTask>().FirstOrDefault();

        // The microphone hears from the game's camera, inside whatever sector it stands in; each ambient area plays to it.
        Func<string, Formats.World.MefModel?> sceneModels = Models(request.World);
        Igi2SectorLocator locator = new(
            player.CollisionWorld,
            World.Igi2SectorGraph.Build(root, model => sceneModels(model)?.Portals));
        Audio.Igi2CameraListener listener = new(
            () => level.CutsceneView?.Position ?? player.Human.View.Position,
            locator,
            () => level.Ticks);
        List<Audio.Igi2AmbientAreaTask> ambience = [];
        foreach (Audio.Igi2AmbientAreaTask area in Descendants(root).OfType<Audio.Igi2AmbientAreaTask>())
        {
            area.Audio = player.Sounds;
            area.Listener = listener;
            area.Random = player.Human.Random;
            area.Prepare();
            ambience.Add(area);
        }

        // The map computer's tasks join it as they are made: the last limit, every highlight with its markers read.
        if (request.Services.TryGet(out Computer.Igi2Computer? computer))
        {
            computer.BeginLevel();
            player.Computer = computer;
            foreach (Engine.Tasks.GameTask node in Descendants(root))
            {
                switch (node)
                {
                    case Computer.Igi2DefineComputerObjectiveTask objectives:
                        objectives.Computer = computer;
                        break;
                    case Computer.Igi2DefineComputerLimitTask limit:
                        limit.Attach(computer);
                        break;
                    case Computer.Igi2ComputerPlanTask plan:
                        plan.Attach(computer);
                        break;
                    case Computer.Igi2ComputerHilightTask hilight:
                        hilight.Attach(computer);
                        hilight.ReadMarkers(natives, functions);
                        break;
                }
            }
        }
        foreach (Igi2SoundGeneratorTask generator in Descendants(root).OfType<Igi2SoundGeneratorTask>())
        {
            generator.Sounds = player.Sounds;
        }

        // The level's humans, in Human_ptQTaskList's order, are the player alone until the AI's soldiers are its own.
        IIgi2AreaOccupant[] humans = [player.Human];
        foreach (Igi2AreaActivateTask area in Descendants(root).OfType<Igi2AreaActivateTask>())
        {
            area.Humans = humans;
            area.Player = player.Human;
        }

        // Each line's pieces are counted once its parameters are read, in the language the screens load.
        Igi2Strings strings = new(request.World.Resources);
        foreach (Igi2StatusMessageTask message in Descendants(root).OfType<Igi2StatusMessageTask>())
        {
            message.Strings = strings;
            message.Screen = player.StatusMessages;
            message.Sounds = player.Sounds;
            message.Listener = () => player.Human.View.Position;
            message.InitText();
        }

        Func<string, Formats.World.MefModel?> models = Models(request.World);
        List<Interaction.IIgi2Activatable> activatables = [];
        player.Human.LevelTick = () => level.LevelFlow?.Tick ?? 0;

        // What detectors see, in the order the tree makes them (AIDetection_ptDetectionQTaskList): the cameras and the player.
        Detection.Igi2DetectionWorld? sight = null;
        if (request.Services.TryGet(out Igi2GameMaterials? sightMaterials))
        {
            Detection.Igi2DetectionWorld detection = new(player.CollisionWorld, sightMaterials)
            {
                FlowTicks = () => level.Ticks,
                LevelTick = () => level.LevelFlow?.Tick ?? 0,
            };
            foreach (Engine.Tasks.GameTask node in Descendants(root))
            {
                if (node is Interaction.Igi2SecurityCameraTask camera)
                {
                    camera.Models = models;
                    camera.Sounds = player.Sounds;
                    camera.Explosions = player.Explosions;
                    camera.Difficulty = player.Human.Difficulty;
                    camera.Prepare();
                    camera.Bind(detection, player.CollisionWorld.SolidsOf(Interaction.Igi2SecurityCameraTask.TypeName, camera.Id));
                    player.Explosions?.AddTarget(camera);
                    detection.Detectables.Add(camera);
                }
                else if (ReferenceEquals(node, task))
                {
                    detection.Detectables.Add(player.Human);
                }
                else if (node is Ai.Igi2HumanSoldierTask { Body: { } soldier })
                {
                    detection.Detectables.Add(soldier);
                }
            }

            sight = detection;
        }

        World.Igi2MagicObjTypes? magicTypes = request.Services.TryGet(out World.Igi2MagicObjTypes? loadedMagic) ? loadedMagic : null;
        // Fences hum while live with an ambient area of their own, are told where the player is, and are climbed while dead.
        List<Interaction.IIgi2Notified> notified = [];
        foreach (Interaction.Igi2FenceTask fence in Descendants(root).OfType<Interaction.Igi2FenceTask>())
        {
            fence.Models = models;
            fence.EquipHum = hum =>
            {
                hum.Audio = player.Sounds;
                hum.Listener = listener;
                hum.Random = player.Human.Random;
            };
            fence.Prepare();
            activatables.Add(fence);
            notified.Add(fence);
        }

        player.Human.Notified = notified;

        // Cabinets are searched from their first magic vertex, their drawers sliding on the search clip's triggers.
        foreach (Interaction.Igi2CabinetTask cabinet in Descendants(root).OfType<Interaction.Igi2CabinetTask>())
        {
            cabinet.Models = models;
            cabinet.Prepare(magicTypes);
            activatables.Add(cabinet);
        }

        // A level's model LOD settings over the game's (LocalModelLODSettingsContainer_UpdateInternalDataAfterLoadHandler).
        if (request.Services.TryGet(out World.Igi2ModelLodTable? lodTable))
        {
            lodTable.BeginLevel();
            foreach (World.Igi2LocalModelLodSettingsContainerTask container in Descendants(root).OfType<World.Igi2LocalModelLodSettingsContainerTask>())
            {
                lodTable.Load(container, container.Path, request.World.Resources, natives);
            }
        }

        // Flocks start their birds' swings from the stream every flock draws from.
        if (request.Services.TryGet(out World.Igi2FlockRandom? flockRandom))
        {
            foreach (World.Igi2BirdFlockTask flock in Descendants(root).OfType<World.Igi2BirdFlockTask>())
            {
                flock.Prepare(flockRandom);
            }
        }

        // Scripted overlays go up on the game screen.
        if (request.Services.TryGet(out Igi2ScreenOverlay? screenOverlay))
        {
            screenOverlay.Hide();
            player.ScreenOverlay = screenOverlay;
            foreach (Igi2OverlayHolderTask holder in Descendants(root).OfType<Igi2OverlayHolderTask>())
            {
                holder.Screen = screenOverlay;
            }
        }

        // Wires are strung between their ends and climbed or ridden down.
        foreach (Interaction.Igi2WireTask wire in Descendants(root).OfType<Interaction.Igi2WireTask>())
        {
            wire.Models = models;
            wire.Level = level;
            wire.Prepare();
            activatables.Add(wire);
        }

        // Ladders read their steps off their models' magic vertices, and look down their lines of sight before they are taken.
        foreach (Interaction.Igi2LadderTask ladder in Descendants(root).OfType<Interaction.Igi2LadderTask>())
        {
            ladder.Models = models;
            ladder.Sight = sight;
            ladder.Prepare();
            activatables.Add(ladder);
        }

        // And the ladders the buildings' attach points make (Ladder_CreateHandler with a magic object's context).
        if (magicTypes is not null)
        {
            World.Igi2WorldScene ladderScene = World.Igi2WorldScene.Build(root, models);
            foreach (Collision.Igi2MagicPlacement point in Collision.Igi2CollisionWorld.MagicObjects(ladderScene, models, magicTypes))
            {
                if (point.Type.TaskType == Interaction.Igi2LadderTask.TypeName)
                {
                    Interaction.Igi2LadderTask ladder = Interaction.Igi2LadderTask.FromAttachPoint(point, models);
                    ladder.Sight = sight;
                    activatables.Add(ladder);
                }
            }
        }

        foreach (Interaction.Igi2SwitchTask button in Descendants(root).OfType<Interaction.Igi2SwitchTask>())
        {
            button.Models = models;
            button.Sounds = player.Sounds;
            activatables.Add(button);
        }

        List<Interaction.IIgi2Mover> movers = [];
        foreach (Interaction.Igi2LiftTask lift in Descendants(root).OfType<Interaction.Igi2LiftTask>())
        {
            lift.Models = models;
            lift.Magic = magicTypes;
            lift.Sounds = player.Sounds;
            lift.Bind(player.CollisionWorld.SolidsOf(Interaction.Igi2LiftTask.TypeName, lift.Id));
            movers.Add(lift);
        }

        foreach (Interaction.Igi2DoorTask door in Descendants(root).OfType<Interaction.Igi2DoorTask>())
        {
            door.Models = models;
            door.Magic = magicTypes;
            door.Sounds = player.Sounds;
            door.Bind(player.CollisionWorld.SolidsOf(Interaction.Igi2DoorTask.TypeName, door.Id));
            activatables.Add(door);
            movers.Add(door);
        }

        // Spline path guides connect to the models they move once the level is loaded; those models move, and are drawn moving.
        foreach (Paths.Igi2SplinePathGuideTask guide in Descendants(root).OfType<Paths.Igi2SplinePathGuideTask>())
        {
            guide.Connect();
        }

        foreach (Interaction.Igi2EditRigidObjTask prop in Descendants(root).OfType<Interaction.Igi2EditRigidObjTask>())
        {
            if (prop.Guide is not null)
            {
                prop.Models = models;
                prop.Magic = magicTypes;
                prop.Bind(player.CollisionWorld.SolidsOf(Interaction.Igi2EditRigidObjTask.TypeName, prop.Id));
                movers.Add(prop);
            }
        }

        // Trains are measured and put on the splines they ride, every carriage moving and drawn moving; then the AnimTasks that drive
        // them set them back to their start (Train_UpdateInternalDataAfterLoadHandler, AnimQTask_UpdateInternalDataHandler).
        foreach (Vehicles.Igi2TrainTask train in Descendants(root).OfType<Vehicles.Igi2TrainTask>().Where(static t => !t.IsCarriage))
        {
            Engine.Tasks.GameTask? rails = train.RailroadId < 0
                ? null
                : Descendants(root).FirstOrDefault(t => t.Id == train.RailroadId && t.Type.Name == World.Igi2SplineObject.SplineType);
            train.Prepare(models, rails is null ? null : World.Igi2SplineObject.Read(rails).Path);
            foreach (Vehicles.Igi2TrainTask car in train.Rake)
            {
                car.Sounds = player.Sounds;
                car.Magic = magicTypes;
                car.Bind(player.CollisionWorld.SolidsOf(Vehicles.Igi2TrainTask.TypeName, car.Id));
                movers.Add(car);
            }
        }

        foreach (Flow.Igi2AnimTask animation in Descendants(root).OfType<Flow.Igi2AnimTask>())
        {
            animation.Begin();
        }

        // Mounted guns are armed with weapons of their own and turn on their mounts; the player and the AI's soldiers work them.
        List<Weapons.Igi2StationaryGunTask> guns = [];
        if (request.Services.TryGet(out Weapons.Igi2HumanWeapons? gunWeapons))
        {
            Igi2GameMaterials? gunMaterials = request.Services.TryGet(out Igi2GameMaterials? found) ? found : null;
            foreach (Weapons.Igi2StationaryGunTask gun in Descendants(root).OfType<Weapons.Igi2StationaryGunTask>())
            {
                gun.Magic = magicTypes;
                gun.Random = player.Human.Random;
                gun.Prepare(gunWeapons, models);
                gun.Bind(player.CollisionWorld.SolidsOf(Weapons.Igi2StationaryGunTask.TypeName, gun.Id));
                if (gun.Firing is { } firing)
                {
                    firing.Effects = new Igi2LevelWeaponEffects(player.Sounds, () => gun.Mount);
                    firing.ImpactEffects = new Igi2LevelImpactEffects(player.Sounds);
                    if (gunMaterials is not null)
                    {
                        firing.World = new Igi2LevelGunShotWorld(player.CollisionWorld, gunMaterials);
                    }
                }

                activatables.Add(gun);
                movers.Add(gun);
                guns.Add(gun);
            }
        }

        player.StationaryGuns = guns;

        // Automatic guns watch from their muzzles and turn their heads and barrels towards what they see.
        List<Weapons.Igi2AiStationaryGunHolderTask> turrets = [];
        if (request.Services.TryGet(out Weapons.Igi2HumanWeapons? turretWeapons))
        {
            Igi2GameMaterials? turretMaterials = request.Services.TryGet(out Igi2GameMaterials? found) ? found : null;
            Detection.Igi2DetectionWorld? detection = request.Services.TryGet(out Ai.Runtime.Igi2AiWorld? turretAi) ? turretAi.Detection : null;
            foreach (Weapons.Igi2AiStationaryGunHolderTask turret in Descendants(root).OfType<Weapons.Igi2AiStationaryGunHolderTask>())
            {
                turret.Prepare(turretWeapons, models, magicTypes, detection, player.Sounds, player.Human.Random);
                if (turret.Gun is { } gun)
                {
                    Igi2WeaponFiring firing = gun.Firing;
                    firing.Effects = new Igi2LevelWeaponEffects(player.Sounds, () => gun.Muzzle);
                    firing.ImpactEffects = new Igi2LevelImpactEffects(player.Sounds);
                    if (turretMaterials is not null)
                    {
                        firing.World = new Igi2LevelGunShotWorld(player.CollisionWorld, turretMaterials);
                    }
                }

                turrets.Add(turret);
            }
        }

        player.Turrets = turrets;

        // Cars are physics objects of the types their models name, on sprung wheels, with their guns on their attach points; they move,
        // and are drawn moving, and blasts reach them (Car_UpdateInternalDataHandler).
        List<Vehicles.Igi2CarTask> cars = [];
        if (request.Services.TryGet(out Physics.Igi2PhysicsObjectTypes? carTypes)
            && request.Services.TryGet(out Igi2GameMaterials? carMaterials))
        {
            request.Services.TryGet(out Weapons.Igi2HumanWeapons? carWeapons);
            Detection.Igi2DetectionWorld? carDetection = request.Services.TryGet(out Ai.Runtime.Igi2AiWorld? carAi) ? carAi.Detection : null;
            Physics.Igi2PhysicsWorld roads = new(player.CollisionWorld, carMaterials, static owner => !Igi2ObjectCollision.PassesThrough(owner));
            foreach (Vehicles.Igi2CarTask car in Descendants(root).OfType<Vehicles.Igi2CarTask>())
            {
                car.Magic = magicTypes;
                car.Sounds = player.Sounds;
                car.Explosions = player.Explosions;
                car.Bind(player.CollisionWorld.SolidsOf(Vehicles.Igi2CarTask.TypeName, car.Id));
                car.Prepare(carTypes, models, roads, carWeapons, carDetection, player.Human.Random);
                if (car.Gun is { } gun)
                {
                    Igi2WeaponFiring firing = gun.Firing;
                    firing.Effects = new Igi2LevelWeaponEffects(player.Sounds, () => gun.Muzzle);
                    firing.ImpactEffects = new Igi2LevelImpactEffects(player.Sounds);
                    firing.World = new Igi2LevelGunShotWorld(player.CollisionWorld, carMaterials);
                }

                player.Explosions?.AddTarget(car);
                movers.Add(car);
                cars.Add(car);
            }
        }

        player.Cars = cars;
        if (request.Services.TryGet(out Ai.Runtime.Igi2AiWorld? gunAi))
        {
            Dictionary<int, Weapons.Igi2StationaryGunTask> gunsById = guns.Where(static g => g.Id >= 0).ToDictionary(static g => g.Id);
            gunAi.StationaryGun = id => gunsById.GetValueOrDefault(id);
        }

        List<Interaction.IIgi2Destructible> destructibles = [];
        foreach (Interaction.Igi2ExplodeObjectTask explosive in Descendants(root).OfType<Interaction.Igi2ExplodeObjectTask>())
        {
            Arm(explosive, Interaction.Igi2ExplodeObjectTask.TypeName);
        }

        foreach (Interaction.Igi2GenericTbaTask action in Descendants(root).OfType<Interaction.Igi2GenericTbaTask>())
        {
            Arm(action, Interaction.Igi2GenericTbaTask.TypeName);
            activatables.Add(action);
        }

        List<Pickups.Igi2GenericPickupTask> genericPickups = [];
        foreach (Pickups.Igi2GenericPickupTask pickup in Descendants(root).OfType<Pickups.Igi2GenericPickupTask>())
        {
            pickup.Models = models;
            pickup.PlaySound = name => player.Human.Surroundings.PlaySound(name);
            genericPickups.Add(pickup);
            destructibles.Add(pickup);
        }

        player.Human.GenericPickups = genericPickups;

        foreach (Interaction.Igi2TerminalTask terminal in Descendants(root).OfType<Interaction.Igi2TerminalTask>())
        {
            Arm(terminal, Interaction.Igi2TerminalTask.TypeName);
            activatables.Add(terminal);
        }

        foreach (Interaction.Igi2AlarmControlTask alarm in Descendants(root).OfType<Interaction.Igi2AlarmControlTask>())
        {
            Arm(alarm, Interaction.Igi2AlarmControlTask.TypeName);
            alarm.AlarmRaised = () => player.Human.TimesAlarmTriggered++;
            activatables.Add(alarm);
        }

        player.Destruction = destructibles.Count > 0 ? new Interaction.Igi2PlacementDestruction(destructibles) : null;

        // An ExplodeObject of any kind: its models and blasts, its solids answering bullets, a target for blasts, and drawn
        // destroyed once it is.
        void Arm(Interaction.IIgi2ExplodingTask task, string typeName)
        {
            task.Explosive.Models = models;
            task.Explosive.Explosions = player.Explosions;
            task.Explosive.Bind(player.CollisionWorld.SolidsOf(typeName, task.Id));
            player.Explosions?.AddTarget(task);
            destructibles.Add(task);
        }

        // A minefield's blasts go off at a task of the level, put on its ground.
        Dictionary<int, object> placedById = [];
        foreach (Engine.Tasks.GameTask node in Descendants(root))
        {
            if (node.Id != Engine.Tasks.GameTask.NoId)
            {
                placedById.TryAdd(node.Id, node switch
                {
                    Ai.Igi2HumanSoldierTask { Body: { } body } => body,
                    Igi2HumanPlayerTask => player.Human,
                    _ => node,
                });
            }
        }

        // Smokes make their particle systems as they are made, each setting the module's stream back to its seed.
        if (player.Human.Surroundings is Igi2LevelSurroundings { Particles: { } particles })
        {
            World.Igi2SmokeRandom smokeRandom = new();
            foreach (World.Igi2SmokeTask smoke in Descendants(root).OfType<World.Igi2SmokeTask>())
            {
                smoke.Particles = particles;
                smoke.Stream = smokeRandom;
                smoke.Player = player.Human;
                smoke.Prepare();
            }
        }

        // The natives scripts reach the player and the humans through answer for this level.
        if (request.Services.TryGet(out Igi2LevelNatives? levelNatives))
        {
            levelNatives.Bind(player.Human, id => placedById.GetValueOrDefault(id), root.Children.OfType<Igi2LevelFlowTask>().FirstOrDefault());
        }

        foreach (Interaction.Igi2MineFieldTask field in Descendants(root).OfType<Interaction.Igi2MineFieldTask>())
        {
            field.Explosions = player.Explosions;
            field.Collision = player.CollisionWorld;
            field.TaskById = id => placedById.GetValueOrDefault(id);
        }

        foreach (Interaction.Igi2ConveyorBeltTask belt in Descendants(root).OfType<Interaction.Igi2ConveyorBeltTask>())
        {
            player.Conveyors[belt.Id] = belt;
        }

        if (request.Services.TryGet(out Igi2GameMaterials? materials))
        {
            player.Materials = materials;
        }

        player.Activatables.AddRange(activatables);
        if (player.Soldiers is { } soldiers)
        {
            Dictionary<int, Interaction.IIgi2Activatable> byId = [];
            foreach (Interaction.IIgi2Activatable thing in activatables)
            {
                if (thing is Engine.Tasks.GameTask { Id: >= 0 } placed)
                {
                    byId.TryAdd(placed.Id, thing);
                }
            }

            foreach (Soldiers.Igi2HumanSoldier soldier in soldiers.Bodies)
            {
                if (soldier.Surroundings is Igi2LevelSurroundings surroundings)
                {
                    surroundings.ById = byId;
                }
            }
        }

        List<Interaction.IIgi2PartMover> partMovers = [.. Descendants(root).OfType<Interaction.Igi2CabinetTask>(), .. turrets, .. cars];
        player.Motions = movers.Count > 0 || partMovers.Count > 0 ? new Interaction.Igi2MoverMotions(movers, partMovers) : null;

        // The game is played at the active profile's difficulty (Config_GetActiveDifficultyLevel); here the difficulty is chosen
        // before the level, so the profile takes it, and the squads' scripts read it back.
        if (request.Services.TryGet(out Config.Igi2GameConfig? gameConfig) && gameConfig.Active is { } profile)
        {
            profile.Difficulty = (int)player.Human.Difficulty;
        }

        if (player.Soldiers is { } soldiersWithMinds)
        {
            request.Services.TryGet(out Ai.Igi2HumanAiConfig? config);
            request.Services.TryGet(out Igi2HumanWeapons? armoury);
            List<Igi2Human> everyone = [player.Human, .. soldiersWithMinds.Bodies];
            Func<Vector3d, float> noise = point =>
            {
                // AmbientArea_GetNoiseLevelAtPosition: the loudest area whose box and falloff hold the point, at most one.
                float loudest = 0f;
                foreach (Audio.Igi2AmbientAreaTask area in ambience)
                {
                    float level = area.NoiseAt(point);
                    if (loudest < level)
                    {
                        loudest = level >= 1f ? 1f : level;
                    }
                }

                return loudest;
            };
            player.Ai = Ai.Runtime.Igi2LevelAi.Build(new Ai.Runtime.Igi2LevelAiSetup(
                root,
                request.World.Resources,
                natives,
                functions,
                player.Human,
                everyone,
                player.Human.Random,
                () => level.LevelFlow?.Tick ?? 0)
            {
                Detection = sight,
                FireTaskTypes = armoury?.FireTaskTypes,
                Config = config,
                Difficulty = (int)player.Human.Difficulty,
                Logger = request.World.Logger,
                World = request.Services.TryGet(out Ai.Runtime.Igi2AiWorld? session) ? session : null,
                NoiseAt = noise,
            });

            // The kill boxes of the cars and the trains are tested against everyone.
            foreach (Vehicles.Igi2CarTask car in player.Cars)
            {
                car.Humans = everyone;
            }

            foreach (Vehicles.Igi2TrainTask train in Descendants(root).OfType<Vehicles.Igi2TrainTask>())
            {
                train.Humans = everyone;
            }

            // CarAI_UpdateInternalDataAfterLoadHandler: each driver finds its car and its graph, and sets the car back to its start.
            foreach (Vehicles.Igi2CarAiTask driver in Descendants(root).OfType<Vehicles.Igi2CarAiTask>())
            {
                driver.Prepare(player.Ai);
            }
        }

        return level;
    }

    private static IEnumerable<Engine.Tasks.GameTask> Descendants(Engine.Tasks.GameTask task)
    {
        foreach (Engine.Tasks.GameTask child in task.Children)
        {
            yield return child;
            foreach (Engine.Tasks.GameTask inner in Descendants(child))
            {
                yield return inner;
            }
        }
    }

    /// <summary>Reads the level's sound definitions and hands them to the host's mixer, as <c>Game_CreateHandler</c> (<c>igi2 0x4070E0</c>) does.</summary>
    /// <param name="request">The level and the session.</param>
    /// <returns>The level's sounds.</returns>
    public static Igi2Sounds LevelSounds(in TitlePlayerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Services);
        ArgumentNullException.ThrowIfNull(request.World.Mission);
        if (!request.Services.TryGet(out Igi2SoundDefinitions? definitions))
        {
            definitions = new Igi2SoundDefinitions(request.World.Logger);
        }

        IReadOnlyList<string> directories = Igi2SoundDefinitions.Directories(request.World.Mission.Descriptor);
        definitions.Load(request.World.Resources, directories);
        request.Audio?.UseDefinitions(definitions, Igi2SoundDefinitions.BankDirectories(request.World.Mission.Descriptor));
        return new Igi2Sounds(definitions, request.Audio);
    }

    /// <summary>The rig of the model the first-person view carries.</summary>
    /// <param name="models">The level's models, by name.</param>
    /// <param name="player">The level's player, whose <c>1st Person Model</c> names it.</param>
    /// <returns>The rig, or <see langword="null"/> when neither that model nor the fallback has bones.</returns>
    /// <remarks><c>HumanView_CreateHandler</c> (<c>igi2 0x4D5A30</c>): a missing or unboned model falls back to <c>005_01_1</c>.</remarks>
    public static Igi2BoneHierarchy? FirstPersonRig(Func<string, Formats.World.MefModel?> models, Igi2HumanPlayerTask? player)
    {
        ArgumentNullException.ThrowIfNull(models);
        return FirstPersonModel(models, player) is { } name ? models(name)?.BoneHierarchy : null;
    }

    /// <summary>The model the first-person view carries.</summary>
    /// <param name="models">The level's models, by name.</param>
    /// <param name="player">The level's player, whose <c>1st Person Model</c> names it.</param>
    /// <returns>Its name, or <see langword="null"/> when neither that model nor the fallback has bones.</returns>
    /// <remarks><c>HumanView_CreateHandler</c> (<c>igi2 0x4D5A30</c>): a missing or unboned model falls back to <c>005_01_1</c>.</remarks>
    public static string? FirstPersonModel(Func<string, Formats.World.MefModel?> models, Igi2HumanPlayerTask? player)
    {
        ArgumentNullException.ThrowIfNull(models);
        if (player is { FirstPersonModel.Length: > 0 } && models(player.FirstPersonModel)?.BoneHierarchy is not null)
        {
            return player.FirstPersonModel;
        }

        return models(FirstPersonFallbackModel)?.BoneHierarchy is not null ? FirstPersonFallbackModel : null;
    }

    /// <summary>Makes the player's inventory, fills it from the level's <c>Weapon</c> script and puts it behind the weapon control.</summary>
    /// <param name="services">The session's tables.</param>
    /// <param name="levelRoot">The level, whose <c>HumanPlayer</c> carries the script.</param>
    /// <param name="tuning">The player tuning, whose ammunition limits bound the inventory.</param>
    /// <param name="clips">The level's clips, which the held weapon's body clips come from.</param>
    /// <returns>The weapons, or <see langword="null"/> when the session has none.</returns>
    /// <remarks>
    /// <c>Human_CreateHandler</c> (<c>igi2 0x4FDC90</c>) makes the inventory within the limits; the script runs once the level
    /// is loaded (<c>Human_UpdateInternalDataAfterCarryoverHandler</c>, <c>0x4FF440</c>).
    /// </remarks>
    public static Igi2PlayerWeapons? Arm(
        Engine.Titles.IServiceCatalog services,
        Engine.Tasks.GameTask? levelRoot,
        Igi2PlayerTuning? tuning,
        Igi2AnimationLibrary? clips = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (!services.TryGet(out Igi2HumanWeapons? weapons))
        {
            return null;
        }

        Igi2Inventory inventory = weapons.CreateInventory(tuning?.AmmoLimits ?? []);
        if (levelRoot is not null && PlayerSpawn.FindGameplayTask(levelRoot) is Igi2HumanPlayerTask player)
        {
            weapons.RunWeaponScript(inventory, player.WeaponScript);
        }

        return new Igi2PlayerWeapons(inventory, weapons.FireTaskTypes, clips);
    }

    /// <summary>Builds the collision a level's player moves through: its solid placements and its ground.</summary>
    /// <param name="world">The request the scene was built from.</param>
    /// <param name="built">What the scene walk produced; its ground is reused.</param>
    /// <returns>The collision world.</returns>
    public static Igi2CollisionWorld BuildCollision(in WorldBuildRequest world, in WorldBuildResult built)
    {
        ArgumentNullException.ThrowIfNull(world.Mission);
        ArgumentNullException.ThrowIfNull(world.LevelRoot);

        Func<string, Formats.World.MefModel?> models = Models(world);
        Igi2WorldScene scene = Igi2WorldScene.Build(world.LevelRoot, models);
        Igi2TerrainSurface? ground = built.Ground is Igi2Ground level ? level.Surface : null;
        Igi2Forest forests = Igi2Forest.Build(world.LevelRoot, world.Resources);
        World.Igi2MagicObjTypes? magic = world.Services is { } services && services.TryGet(out World.Igi2MagicObjTypes? table) ? table : null;
        return Igi2CollisionWorld.Build(scene, models, ground, TerrainOwner, forests, magic);
    }

    /// <summary>Resolves a placed model name to its mesh through the mission's packs.</summary>
    private static Func<string, Formats.World.MefModel?> Models(in WorldBuildRequest world)
    {
        ArgumentNullException.ThrowIfNull(world.Mission);
        List<Formats.World.MtpPack> mtp = [.. world.Mission.PacksInLoadOrder.Select(static pack => pack.Pack)];
        return Igi2DrawableScene.Models(Igi2ModelTable.Create(mtp), world.Mission.PacksInLoadOrder);
    }
}

/// <summary>The ported player as the host steps it.</summary>
public sealed class Igi2TitlePlayer : ITitlePlayer, ITitleFirstPerson, ITitleWorldModels
{
    /// <summary>DirectInput's relative counts to the analog axis, <c>Mouse_Update</c> (<c>igi2ed 0x55ED60</c>): a 255th each.</summary>
    private const double CountsToAnalog = 0.00392156862745098;

    private static readonly (PlayerAction Host, Igi2PlayerAction Title)[] Actions =
    [
        (PlayerAction.MoveLeft, Igi2PlayerAction.MoveLeft),
        (PlayerAction.MoveRight, Igi2PlayerAction.MoveRight),
        (PlayerAction.MoveUp, Igi2PlayerAction.MoveForwards),
        (PlayerAction.MoveDown, Igi2PlayerAction.MoveBackwards),
        (PlayerAction.Fire, Igi2PlayerAction.Fire),
        (PlayerAction.AlternateFire, Igi2PlayerAction.AlternateFire),
        (PlayerAction.Reload, Igi2PlayerAction.Reload),
        (PlayerAction.Activate, Igi2PlayerAction.Activate),
        (PlayerAction.Crouch, Igi2PlayerAction.Crouch),
        (PlayerAction.Jump, Igi2PlayerAction.Jump),
        (PlayerAction.Peek, Igi2PlayerAction.Peek),
        (PlayerAction.ZoomIn, Igi2PlayerAction.ZoomIn),
        (PlayerAction.ZoomOut, Igi2PlayerAction.ZoomOut),
        (PlayerAction.NextWeapon, Igi2PlayerAction.NextWeapon),
        (PlayerAction.PreviousWeapon, Igi2PlayerAction.PrevWeapon),
        (PlayerAction.ToggleWalkRun, Igi2PlayerAction.ToggleWalkRun),
        (PlayerAction.Binoculars, Igi2PlayerAction.Binoculars),
        (PlayerAction.MapComputer, Igi2PlayerAction.ComputerMap),
    ];

    private readonly Igi2PlayerInputReader _reader = new();
    private readonly float[] _frame = new float[Igi2HumanChannels.Count];
    private readonly Igi2FirstPersonFrame _firstPerson = new();
    private long _ticks;
    private TitleFirstPersonView? _held;
    private long _heldTick = -1;
    private TitleWorldModel[] _lying = [];
    private long _lyingTick = -1;

    private readonly Igi2LevelSurroundings _surroundings;

    private Igi2TitlePlayer(Igi2HumanPlayer human, Igi2PlayerWeapons? weapons, Igi2LevelSurroundings surroundings)
    {
        Human = human;
        Weapons = weapons;
        _surroundings = surroundings;
    }

    /// <summary>Gets the ported player.</summary>
    public Igi2HumanPlayer Human { get; }

    /// <summary>Gets the player's weapons, or <see langword="null"/> when the session has none.</summary>
    public Igi2PlayerWeapons? Weapons { get; }

    /// <summary>Gets what the player carries, or <see langword="null"/> when the session has no weapons.</summary>
    public Igi2Inventory? Inventory => Weapons?.Inventory;

    /// <summary>Gets or sets the grenades the player has thrown that are still in flight, or <see langword="null"/> for none.</summary>
    public Weapons.Igi2Grenades? Grenades { get; set; }

    /// <summary>Gets or sets the explosions the level's blasts go off in, or <see langword="null"/> for none.</summary>
    public Weapons.Igi2ExplosionWorld? Explosions { get; set; }

    /// <summary>Gets the level's dynamic tasks, <c>Level_ptDynamicQTask</c>: the grenades, explosions and what they leave.</summary>
    public World.Igi2DynamicTasks Dynamic { get; } = new();

    /// <summary>Gets or sets the level's soldiers, or <see langword="null"/> before the level made them.</summary>
    public Soldiers.Igi2Soldiers? Soldiers { get; set; }

    /// <summary>Gets or sets the level's AI, which gives the soldiers their minds, or <see langword="null"/>.</summary>
    public Ai.Runtime.Igi2AiWorld? Ai { get; set; }

    /// <summary>Gets the level's sounds.</summary>
    public Igi2Sounds Sounds { get; private init; } = new(new Igi2SoundDefinitions(), null);

    /// <summary>Gets the screen the level's status messages are printed on, which runs after the level each tick.</summary>
    public Igi2StatusMessageScreen StatusMessages { get; } = new();

    /// <summary>Gets the things in the level the player may activate, in the order the stationary tree offers them.</summary>
    public List<Interaction.IIgi2Activatable> Activatables => _surroundings.Activatables;

    /// <summary>Gets the collision world the player moves through.</summary>
    public Igi2CollisionWorld CollisionWorld => _surroundings.World;

    /// <summary>Gets the level's conveyor belts the player may stand on, by level id.</summary>
    public Dictionary<int, Interaction.Igi2ConveyorBeltTask> Conveyors => _surroundings.Conveyors;

    /// <summary>Gets or sets the session's materials, which name what the player stands on.</summary>
    public Igi2GameMaterials? Materials
    {
        get => _surroundings.Materials;
        set => _surroundings.Materials = value;
    }

    /// <inheritdoc/>
    public IPlacementMotion? Motions { get; set; }

    /// <inheritdoc/>
    public IPlacementDestruction? Destruction { get; set; }

    /// <summary>Gets or sets the level's tick the player runs inside, or <see langword="null"/> to run the player alone.</summary>
    public Igi2LevelRun? Level { get; set; }

    /// <summary>Gets or sets the game-wide switches the level's scripts flip, or <see langword="null"/> for none.</summary>
    public Igi2FlowState? Flow { get; set; }

    /// <summary>Gets or sets the player's input task, or <see langword="null"/> when the level gives the player none.</summary>
    public Igi2HumanPlayerInputTask? Input { get; set; }

    /// <summary>Gets or sets the map computer, or <see langword="null"/>.</summary>
    public Computer.Igi2Computer? Computer { get; set; }

    /// <inheritdoc/>
    /// <remarks><c>CutScene_isRunning</c> as the last tick left it.</remarks>
    public bool IsCutscenePlaying => Level?.IsCutsceneRunning ?? false;

    /// <inheritdoc/>
    public bool IsMenuEnabled => Flow?.IsMenuEnabled ?? true;

    /// <inheritdoc/>
    public Vector3d Position => Human.Position;

    /// <inheritdoc/>
    /// <remarks>
    /// A cutscene's camera, while one is placed, outranks the player's eyes; a mounted gun's seat, while the view is overridden,
    /// stands in for them (the human camera's mode 4).
    /// </remarks>
    public Vector3d EyePosition => Level?.CutsceneView is { } cut
        ? cut.Position
        : Human.View.IsOverride ? Human.View.OverridePosition : Human.View.Position;

    /// <inheritdoc/>
    /// <remarks>
    /// The view's columns are its right, forward and up axes; the camera wants them as rows, up before forward. A cutscene
    /// camera's columns are right, down and forward, the engine camera's own (<c>HumanCamera_FirstPersonSetCamera</c>,
    /// <c>igi2 0x4D4730</c>, turns the view a quarter about its right to get there).
    /// </remarks>
    public Matrix3f ViewOrientation
    {
        get
        {
            if (Level?.CutsceneView is { } cut)
            {
                IReadOnlyList<float> c = cut.Orientation;
                return Matrix3f.FromRows(
                    new Vector3f(c[0], c[3], c[6]),
                    new Vector3f(-c[1], -c[4], -c[7]),
                    new Vector3f(c[2], c[5], c[8]));
            }

            Matrix3f m = Human.View.IsOverride ? Human.View.OverrideOrientation : Human.View.Orientation;
            return Matrix3f.FromRows(
                new Vector3f(m.M11, m.M21, m.M31),
                new Vector3f(m.M13, m.M23, m.M33),
                new Vector3f(m.M12, m.M22, m.M32));
        }
    }

    /// <inheritdoc/>
    /// <remarks>The view's <c>vFOV</c>, which a zoom steers; a cutscene camera's own while one is placed.</remarks>
    public float FieldOfViewScale => Level?.CutsceneView is { } cut ? cut.FieldOfView : Human.View.FieldOfView;

    /// <inheritdoc/>
    /// <remarks>
    /// Built once a tick, on the first ask after it, from the view as the tick left it; none while a cutscene's camera is placed,
    /// which is not the player's eyes.
    /// </remarks>
    public TitleFirstPersonView? FirstPerson
    {
        get
        {
            if (Level?.CutsceneView is not null)
            {
                return null;
            }

            if (_heldTick != _ticks)
            {
                _held = _firstPerson.Build(Human, Inventory?.Current?.WeaponType.FixedModel, _ticks);
                _heldTick = _ticks;
            }

            return _held;
        }
    }

    /// <summary>Gets or sets the level's automatic guns.</summary>
    public IReadOnlyList<Weapons.Igi2AiStationaryGunHolderTask> Turrets { get; set; } = [];

    /// <summary>Gets or sets the level's in-game menu, which the shell presents.</summary>
    public Menus.Igi2InGameMenu? Menu { get; set; }

    /// <summary>Gets or sets the mission's save games.</summary>
    public Saves.Igi2MissionSaves? Saves { get; set; }

    /// <inheritdoc/>
    public ITitleSaveGames? SaveGames => Saves;

    /// <inheritdoc/>
    /// <remarks>The level flow's <c>Complete</c> and <c>Failed</c> scripts, failure first (<c>LevelFlow_RunHandler</c>).</remarks>
    public TitleMissionOutcome Outcome => Level?.LevelFlow switch
    {
        { IsFailed: true } => TitleMissionOutcome.Failed,
        { IsComplete: true } => TitleMissionOutcome.Completed,
        _ => TitleMissionOutcome.Running,
    };

    /// <inheritdoc/>
    /// <remarks>The player's weapons are left for the mission that follows, <c>Game_SaveCarryover</c> (<c>Game_RunHandler</c>).</remarks>
    public void MissionCompleted(int nextMissionId)
    {
        if (Saves is { } saves && Level is { } level && PlayerSpawn.FindGameplayTask(level.Root) is Igi2HumanPlayerTask task)
        {
            saves.SaveCarryOver(task, nextMissionId);
        }
    }

    /// <summary>Gets or sets the level's cars.</summary>
    public IReadOnlyList<Vehicles.Igi2CarTask> Cars { get; set; } = [];

    /// <summary>Gets or sets the level's mounted guns, whose barrels are drawn with the soldiers.</summary>
    public IReadOnlyList<Weapons.Igi2StationaryGunTask> StationaryGuns { get; set; } = [];

    /// <summary>Gets or sets the overlay the scripts have put on the game screen, which the HUD draws.</summary>
    public Igi2ScreenOverlay? ScreenOverlay { get; set; }

    /// <summary>Gets or sets the level's figures (<see cref="World.Igi2EditBoneObjTask"/>), drawn with the soldiers.</summary>
    public IReadOnlyList<World.Igi2EditBoneObjTask> Figures { get; set; } = [];

    /// <inheritdoc/>
    /// <remarks>
    /// The level's pickups not yet taken, each the weapon's <c>Fixed Model</c> where it lies, then the grenades in flight, each
    /// its <c>ProjectileModel</c>, then the soldiers, each posed with the weapon in its hand, then the figures; built once a tick.
    /// </remarks>
    public IReadOnlyList<TitleWorldModel> WorldModels
    {
        get
        {
            if (_lyingTick != _ticks)
            {
                IEnumerable<TitleWorldModel> pickups = Human.Pickups is { } lying
                    ? lying.Items.Select(static p => new TitleWorldModel(p.WeaponType.FixedModel, p.Position, Rows(p.Orientation)))
                    : [];
                IEnumerable<TitleWorldModel> flying = Grenades is { } grenades
                    ? grenades.Flying.Select(static g => new TitleWorldModel(g.Model, g.Position, Rows(g.Orientation)))
                    : [];
                IEnumerable<TitleWorldModel> soldiers = Soldiers?.Models(_ticks) ?? [];
                IEnumerable<TitleWorldModel> figures = Figures.SelectMany(f => f.Models(_ticks));
                IEnumerable<TitleWorldModel> barrels = StationaryGuns.Select(static g => g.Barrel()).OfType<TitleWorldModel>();
                IEnumerable<TitleWorldModel> wrecks = Cars.Select(static c => c.Wreck()).OfType<TitleWorldModel>();
                _lying = [.. pickups, .. flying, .. soldiers, .. figures, .. barrels, .. wrecks];
                _lyingTick = _ticks;
            }

            return _lying;
        }
    }

    /// <summary>Makes the player at its spawn, as <c>HumanPlayer_CreateHandler</c> does, over a collision world.</summary>
    /// <param name="world">The level's collision.</param>
    /// <param name="general">The player tuning.</param>
    /// <param name="position">Where the level places the player's feet.</param>
    /// <param name="gamma">The heading the level gives it.</param>
    /// <param name="difficulty">The active difficulty.</param>
    /// <param name="clips">The level's clips, or <see langword="null"/> for none.</param>
    /// <param name="rig">The player model's rig, or <see langword="null"/> for a model without bones.</param>
    /// <param name="weapons">The player's weapons, or <see langword="null"/> for none.</param>
    /// <param name="firstPersonRig">The first-person model's rig, or <see langword="null"/> for none.</param>
    /// <param name="sounds">The level's sounds, or <see langword="null"/> for a level that defines none.</param>
    /// <returns>The player.</returns>
    public static Igi2TitlePlayer Spawn(
        Igi2CollisionWorld world,
        Igi2HumanPlayerGeneral general,
        Vector3d position,
        float gamma,
        OpenIGI.Game.Ai.AiDifficulty difficulty,
        Igi2AnimationLibrary? clips = null,
        Igi2BoneHierarchy? rig = null,
        Igi2PlayerWeapons? weapons = null,
        Igi2BoneHierarchy? firstPersonRig = null,
        Igi2Sounds? sounds = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(general);

        sounds ??= new Igi2Sounds(new Igi2SoundDefinitions(), null);
        Igi2LevelSurroundings surroundings = new(world, sounds);
        IIgi2PlayerWeaponControl weapon = weapons ?? (IIgi2PlayerWeaponControl)Igi2NoWeaponControl.Instance;
        Igi2HumanAnimation animation = new(clips, rig, Igi2PlayerStateAnimations.Table, () => weapon.BodyClips(rig));
        Igi2HumanPlayer human = new(weapon, animation, surroundings)
        {
            General = general,
            Position = position,
            Gamma = gamma,
            IdealGamma = gamma,
        };
        surroundings.Owner = human;
        human.View.ObjectRig = firstPersonRig;
        animation.TriggerTarget = human.BodyTriggers;

        // Human_UpdateInternalDataHandler (igi2 0x4FF2F0): the level's Gamma is the heading and the ideal heading
        // both, and the body stands upright turned by it.
        float[] facing = Igi2Pose.Rotation(0f, 0f, gamma);
        human.Orientation = new Matrix3f(
            facing[0], facing[1], facing[2],
            facing[3], facing[4], facing[5],
            facing[6], facing[7], facing[8]);
        human.Create(difficulty);
        human.MaintainCollisionZOffset();

        // The view would first be placed on the first tick; placing it now gives the host's first frames the eyes
        // rather than the world's origin. It draws nothing from the run stream: a new view does not shake.
        human.UpdateViewBody(updateAnimation: false);
        return new Igi2TitlePlayer(human, weapons, surroundings) { Sounds = sounds };
    }

    /// <inheritdoc/>
    public void Tick(in TitlePlayerInput input)
    {
        Igi2PlayerAction held = Map(input.Held);
        Igi2PlayerAction pressed = Map(input.Pressed);
        _reader.MouseSensitivity = input.MouseSensitivity;
        _reader.InvertMouse = input.InvertMouse;
        _reader.Read(new Igi2PlayerInputState(Analog(input.MouseX), Analog(input.MouseY), held, pressed), _frame);
        if (Level is { } level)
        {
            Flow?.IsBreakKeyDown = input.BreakCutscene;
            level.Tick();
            StatusMessages.Run();
        }
        else
        {
            RunPlayer();
            Dynamic.Run();
            RunPlayerDelayed();
        }

        Sounds.Run();
        _ticks++;
    }

    /// <summary>
    /// The player's run handler: the shots its weapon fired last tick are traced, and the rest of its tick is queued for the
    /// delayed pass (<c>HumanPlayer_RunHandler</c>, <c>igi2 0x4B8A30</c>).
    /// </summary>
    internal void RunPlayer()
    {
        Weapons?.RunGunShot();
        if (Level is { } level)
        {
            // The player's first child, HumanPlayerInput, reads the controls into the frame it sends up as the player's update.
            if (Input is { } input)
            {
                input.Run(level);
                input.Apply(_frame);
                Computer?.IsEnabled = input.IsComputerEnabled;
            }

            level.Delay(RunPlayerDelayed);
        }
    }

    /// <summary>The player's delayed half: its pickups, zoom, update and delayed handler (<c>HumanPlayer_RunDelayedHandler</c>, <c>igi2 0x4B8A50</c>).</summary>
    private void RunPlayerDelayed()
    {
        Human.Pickups?.Run();
        Human.View.RunZoom();
        Human.Ticks = (int)_ticks;
        Human.Update(_frame);
        Human.RunDelayed();
    }

    /// <summary>Nine floats row by row as a matrix.</summary>
    private static Matrix3f Rows(float[] m) => new(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8]);

    /// <summary>The mouse's counts as the input port's analog axis: a 255th each, held within one either way.</summary>
    private static float Analog(float counts)
    {
        float analog = ((Fpu)(double)counts * CountsToAnalog).F;
        return analog < -1f ? -1f : analog > 1f ? 1f : analog;
    }

    private static Igi2PlayerAction Map(PlayerAction actions)
    {
        Igi2PlayerAction mapped = Igi2PlayerAction.None;
        foreach ((PlayerAction host, Igi2PlayerAction title) in Actions)
        {
            if ((actions & host) != 0)
            {
                mapped |= title;
            }
        }

        return mapped;
    }
}

/// <summary>The level a bullet flies through: the collision world and the session's materials.</summary>
/// <remarks>Nothing in the collision world is water yet; the boned objects are its humans.</remarks>
internal sealed class Igi2LevelGunShotWorld(IIgi2CollisionQuery collision, Igi2GameMaterials materials) : IIgi2GunShotWorld
{
    public IIgi2CollisionQuery Collision { get; } = collision;

    public Igi2GameMaterials Materials { get; } = materials;

    public bool IsWater(IIgi2Collider struck) => false;

    public bool IsBoned(IIgi2Collider struck) => struck is Igi2BoneCollider;

    public bool IsHuman(IIgi2Collider struck) => struck is Igi2BoneCollider { Owner: Humans.Igi2Human };
}

/// <summary>What the player's states ask of the world, answered from the collision world and the level's sounds.</summary>
internal sealed class Igi2LevelSurroundings(Igi2CollisionWorld world, Igi2Sounds sounds) : IIgi2HumanSurroundings
{
    /// <summary>Gets or sets the body these surroundings are asked about.</summary>
    public Igi2Human? Owner { get; set; }

    public IIgi2CollisionQuery Collision { get; } = world;

    /// <summary>Gets the collision world itself, whose placed solids the level's moving objects carry.</summary>
    public Igi2CollisionWorld World { get; } = world;

    /// <summary>Gets or sets the level's pickups, which a thrown weapon joins.</summary>
    public Pickups.Igi2PickupList? Pickups { get; set; }

    /// <summary>Gets or sets the level's grenades, which a soldier's throw joins.</summary>
    public Weapons.Igi2Grenades? Grenades { get; set; }

    public bool ThrowGrenade(in Weapons.Igi2GrenadeThrow grenade)
    {
        if (Grenades is not { } grenades)
        {
            return false;
        }

        grenades.Launch(grenade);
        return grenades.KindOf(grenade.WeaponType) == Weapons.Igi2GrenadeKind.Normal;
    }

    /// <summary>Gets or sets what resolves a thrown weapon's fixed model, whose radius the pickup takes.</summary>
    public Func<string, Formats.World.MefModel?>? Models { get; set; }

    public void ThrowGunPickup(Igi2WeaponTypeTask weaponType, Vector3d position, IReadOnlyList<float> orientation, float throwGamma, int ammoInClip, IReadOnlyList<int> ammoCount)
    {
        if (Pickups is not { } pickups || Owner is not Soldiers.Igi2HumanSoldier { Weapon.Inventory.Tables: { } tables })
        {
            return;
        }

        float radius = weaponType.FixedModel.Length > 0 && Models?.Invoke(weaponType.FixedModel) is { } model ? model.NativeMarkerBound : 1f;
        pickups.Add(OpenIGI.Game.Igi2.Pickups.Igi2GunPickup.Thrown(weaponType, tables, position, orientation, throwGamma, isMoving: true, isThrowShort: true, ammoInClip, ammoCount, radius));
    }

    public bool IsOnElevator => false;

    public bool FitsLyingFlat() => Owner?.FitsWhereItStands() ?? true;

    public void SendWalkEvent()
    {
    }

    public void SendLandingEvent()
    {
    }

    public bool IsUnderWater(out float depth)
    {
        depth = 0f;
        return false;
    }

    public void StirWater(bool isRunning)
    {
    }

    /// <summary>Gets the level's conveyor belts, by level id.</summary>
    public Dictionary<int, Interaction.Igi2ConveyorBeltTask> Conveyors { get; } = [];

    /// <summary>Gets or sets the session's materials, which name a footing's material, or <see langword="null"/> for none.</summary>
    public Igi2GameMaterials? Materials { get; set; }

    /// <inheritdoc/>
    /// <remarks>
    /// <c>Human_Move</c> (<c>igi2 0x501685</c>) asks whatever stands underfoot, and whatever does not answer pushes nothing: only a
    /// belt's own solid does (<see cref="Interaction.Igi2ConveyorBeltTask.Push"/>).
    /// </remarks>
    public Vector3f? PushOnConveyor(IIgi2Collider ground, Igi2Intersection footing)
    {
        ArgumentNullException.ThrowIfNull(footing);
        if (ground is Igi2RigidObject { Owner: Igi2CollisionOwner { Depth: 0 } owner }
            && owner.TypeName == Interaction.Igi2ConveyorBeltTask.TypeName
            && Conveyors.TryGetValue(owner.LevelId, out Interaction.Igi2ConveyorBeltTask? belt))
        {
            return belt.Push(footing.EnterMaterial is int row ? Materials?[row]?.Name : null);
        }

        return default(Vector3f);
    }

    /// <summary>Gets or sets the level's 2-D particles, which blood is thrown through.</summary>
    public Igi2Particles? Particles { get; set; }

    /// <summary>Gets or sets every human registered in the level, <c>Human_ptQTaskList</c>.</summary>
    public IReadOnlyList<Igi2Human> Humans { get; set; } = [];

    public string? LandingSound(int material) =>
        Materials?[material] is { LandSound.Length: > 0 } game ? game.LandSound : null;

    public Igi2GameMaterial? Material(int row) => Materials?[row];

    public IIgi2SoundInstance? PlaySoundAt(string name, Vector3d position) =>
        Owner is Igi2HumanPlayer ? StartSound(name) : sounds.New(name, position);

    public void PlaySound(string name) => StartSound(name);

    public void ShowStatusMessage(string key)
    {
    }

    public IIgi2SoundInstance StartSound(string name) =>
        sounds.New(name, View(), View) ?? (IIgi2SoundInstance)SilentSound.Instance;

    private Vector3d View() => Owner?.View.Position ?? default;

    /// <summary>Gets the things the player may activate, in the order the stationary tree offers them.</summary>
    public List<Interaction.IIgi2Activatable> Activatables { get; } = [];

    /// <summary>Gets or sets the level's things by task id, which a soldier's activation names.</summary>
    public IReadOnlyDictionary<int, Interaction.IIgi2Activatable>? ById { get; set; }

    public Interaction.IIgi2Activatable? ActivatableById(int id) =>
        ById is { } byId && byId.TryGetValue(id, out Interaction.IIgi2Activatable? thing) ? thing : null;

    /// <inheritdoc/>
    /// <remarks>Each thing's sphere against the player's: the two meet when their centres are no further apart than their radii added.</remarks>
    public IEnumerable<Interaction.IIgi2Activatable> FindActivatables(Vector3d centre, float radius)
    {
        foreach (Interaction.IIgi2Activatable candidate in Activatables)
        {
            Vector3d d = candidate.Centre - centre;
            double reach = radius + candidate.Radius;
            if ((d.X * d.X) + (d.Y * d.Y) + (d.Z * d.Z) <= reach * reach)
            {
                yield return candidate;
            }
        }
    }

    private sealed class SilentSound : IIgi2SoundInstance
    {
        public static SilentSound Instance { get; } = new();

        public bool IsAlive => false;

        public void MoveToHuman()
        {
        }

        public void Delete()
        {
        }
    }
}
