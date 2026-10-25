using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Audio;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Systems;
using OpenIGI.Game.Igi2.ViewModel;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Level;
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

    /// <inheritdoc/>
    /// <remarks>config.c Config_Open; humanplayer.c HumanPlayer_StateSignalHandler: held crouch and the three stance edges are separate actions.</remarks>
    public IReadOnlyList<TitleInputBinding> InputBindings { get; } =
    [
        new((ulong)Igi2PlayerAction.LowerStance, new("Title_igi2_LowerStance", Game.Config.InputBindingKeyNames.KeyboardDevice, "KEY_Z"), "Lower stance"),
        new((ulong)Igi2PlayerAction.LowerStance, new("Title_igi2_LowerStanceAlt", Game.Config.InputBindingKeyNames.KeyboardDevice, "KEY_DELETE"), "Lower stance (alternate)"),
        new((ulong)Igi2PlayerAction.RaiseStance, new("Title_igi2_RaiseStance", Game.Config.InputBindingKeyNames.KeyboardDevice, "KEY_Q"), "Raise stance"),
        new((ulong)Igi2PlayerAction.RaiseStance, new("Title_igi2_RaiseStanceAlt", Game.Config.InputBindingKeyNames.KeyboardDevice, "KEY_INSERT"), "Raise stance (alternate)"),
        new((ulong)Igi2PlayerAction.ToggleStandLie, new("Title_igi2_ToggleStandLie", Game.Config.InputBindingKeyNames.KeyboardDevice, Game.Config.PlayerProfile.UnboundKey), "Toggle stand/prone"),
        new((ulong)Igi2PlayerAction.Crouch, new("Title_igi2_Crouch", Game.Config.InputBindingKeyNames.KeyboardDevice, "KEY_LEFT_SHIFT"), "Crouch", PlayerAction.Crouch),
        new((ulong)Igi2PlayerAction.Crouch, new("Title_igi2_CrouchAlt", Game.Config.InputBindingKeyNames.KeyboardDevice, "KEY_RIGHT_CTRL"), "Crouch (alternate)"),
        new((ulong)Igi2PlayerAction.ToggleWalkRun, new("Title_igi2_ToggleWalkRun", Game.Config.InputBindingKeyNames.KeyboardDevice, "KEY_CAPITAL"), "Toggle walk/run", PlayerAction.ToggleWalkRun),
        new((ulong)Igi2PlayerAction.ToggleWalkRun, new("Title_igi2_ToggleWalkRunAlt", Game.Config.InputBindingKeyNames.KeyboardDevice, "KEY_END"), "Toggle walk/run (alternate)"),
    ];

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
        player.GameConfig = request.Services.TryGet(out Config.Igi2GameConfig? gameConfig) ? gameConfig : null;
        player.ModelAttachments = new Igi2ModelAttachments(models,
            request.Services.TryGet(out Igi2MagicObjTypes? attachmentTypes) ? attachmentTypes : null);
        player.Casings = new Weapons.Igi2GunCasings(models);
        player.OverlayTypeNames = OverlayTypeNames(request.Natives);
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

        // The panes of glass the buildings hang, each made as the level's objects are, drawing its threshold from the run stream.
        player.Glass = Interaction.Igi2GlassContainer.Build(world, models, player.Human.Random, explosions: null);
        if (weapons is not null)
        {
            weapons.Effects = new Igi2LevelWeaponEffects(sounds, () => player.Human.View.Position, LevelAi(request.Services), player.EjectCasing);
            weapons.ImpactEffects = new Igi2LevelImpactEffects(sounds, LevelAi(request.Services));
        }

        if (request.Services.TryGet(out Igi2GameMaterials? materials))
        {
            if (weapons is not null)
            {
                weapons.World = new Igi2LevelGunShotWorld(world, materials, LevelAi(request.Services));
                if (request.Services.TryGet(out OpenIGI.Game.Physics.PhysicsObjectTypes? physicsTypes))
                {
                    Weapons.Igi2ExplosionWorld explosions = new(player.Human.Random, world, materials, player.Dynamic)
                    {
                        Effects = new Igi2LevelExplosionEffects(sounds, LevelAi(request.Services)),
                    };
                    if (weapons.ImpactEffects is Igi2LevelImpactEffects impacts)
                    {
                        impacts.Explosions = explosions;
                    }

                    explosions.AddTarget(player.Human);
                    foreach (Interaction.Igi2Glass pane in player.Glass.Panes)
                    {
                        pane.Explosions = explosions;
                        explosions.AddTarget(pane);
                    }

                    // A thrown object breaks the glass it strikes and goes on through it (Glass_PhysicsObjHitHandler), and goes on
                    // through water.
                    Interaction.Igi2GlassContainer panes = player.Glass;
                    Physics.Igi2PhysicsWorld flight = new(world, materials, static owner => !Igi2ObjectCollision.PassesThrough(owner))
                    {
                        Hit = (owner, thrown) =>
                        {
                            // Water_PhysicsObjHitHandler: water lets a thrown object through.
                            if (owner is World.WaterTask)
                            {
                                return true;
                            }

                            if (owner is Collision.Igi2CollisionOwner { GlassKey: { } key } && panes.Find(key) is { } glass)
                            {
                                glass.PhysicsHit(thrown.Position);
                                return true;
                            }

                            return false;
                        },
                    };
                    Weapons.Igi2Grenades grenades = new(physicsTypes, models, flight, player.Dynamic)
                    {
                        Effects = explosions,
                        Explosions = explosions,
                    };
                    if (request.Services.TryGet(out OpenIGI.Game.Weapons.MissileRandom? missileStream))
                    {
                        grenades.MissileStream = missileStream.Random;
                    }

                    weapons.Grenades = grenades;
                    player.Grenades = grenades;
                    player.Explosions = explosions;
                }
            }

            if (request.Services.TryGet(out Igi2HumanWeapons? armoury))
            {
                player.Human.Pickups = Pickups.Igi2PickupList.Build(request.World.LevelRoot, armoury.Tables, models, world, materials);
                player.Human.Pickups.Collision = world;
                player.Human.Pickups.Dynamic = player.Dynamic;
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
                worn,
                player.Casings)
            : null;

        // The level's figures: each its face, its clips and its first pause, from the module's blink stream; then its weapon expression.
        if (request.World.LevelRoot is { } figuresRoot && worn is not null)
        {
            IgiRandom blink = new(unchecked((uint)-912896271), unchecked((uint)-463112362), unchecked((uint)-1522052915));
            List<World.Igi2EditBoneObjTask> figures = [];
            foreach (World.Igi2EditBoneObjTask figure in figuresRoot.Descendants().OfType<World.Igi2EditBoneObjTask>())
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
        List<Igi2Human> humans = LevelHumans(request.World.LevelRoot, player);

        // Particle2DDynCubeObj's module: one stream and collision phase for every system the level makes.
        Igi2Particles particles = new(world, player.Dynamic, new OpenIGI.Game.World.Particles.Particle2DSprites(request.World.Resources.FileSystem).FramesOf);
        if (player.Explosions is { } grenadeEffects)
        {
            grenadeEffects.Humans = humans;
            grenadeEffects.Particles = particles;
        }
        foreach (Igi2Human human in humans)
        {
            if (human.Surroundings is Igi2LevelSurroundings surroundings)
            {
                surroundings.Humans = humans;
                surroundings.Particles = particles;
            }
        }

        player.MapHumans = humans;
        player.MissionId = request.World.Mission.Descriptor.Id;
        player.Quirks = request.Services.TryGet(out Igi2Quirks? quirks) ? quirks : new Igi2Quirks();
        Engine.Tasks.GameTask? taskRoot = request.World.LevelRoot;
        player.TaskById = id => taskRoot?.Tree.FromId(id);
        player.Level = LevelRun(request, player);
        player.Saves = MissionSaves(request, player);
        if (taskRoot is not null && request.Services.TryGet(out Engine.Events.GameEvents? events))
        {
            player.EventWatch = new Igi2EventWatch(player, taskRoot, events);
            events.Raise(new Events.LevelLoaded(Core.Titles.GameTitle.Igi2, player.MissionId));
        }

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
            player.Human)
        {
            Glass = player.Glass,
            PlayedMods = request.Services.TryGet(out Engine.Titles.PlayedMods? played) ? played.Mods : [],
        };
        if (PlayerSpawn.FindGameplayTask(request.World.LevelRoot) is Igi2HumanPlayerTask task)
        {
            saves.LoadCarryOver(task);
        }

        // The menus answer from these slots; a slot the load list asked for is loaded once the level is made (Game_CreateHandler).
        if (request.Services.TryGet(out Saves.Igi2SaveSession? session))
        {
            session.Slots = saves.SlotStore;
            session.Owner = saves.Owner;
            if (session.TakeMenuLoadSlot() is var slot and >= 0)
            {
                saves.Load(slot);
            }
        }

        return saves;
    }

    // The session's AI, which hears what the level's weapons and explosions do (HumanAI_SendEvent); none when it has none.
    /// <summary>The overlay task types' names by the ids the session's <c>TASKTYPE_*</c> constants give them.</summary>
    internal static Dictionary<int, string> OverlayTypeNames(OpenIGI.Scripting.Runtime.NativeRegistry? natives)
    {
        Dictionary<int, string> names = [];
        if (natives is null)
        {
            return names;
        }

        foreach ((string constant, string type) in Weapons.Igi2WeaponConstants.TaskTypes)
        {
            if (type.EndsWith("Overlay", StringComparison.Ordinal)
                && natives.TryResolve(constant, out OpenIGI.Scripting.Runtime.QvmSymbol? symbol))
            {
                names.TryAdd(symbol.Read().ToInt32(), type);
            }
        }

        return names;
    }

    private static Ai.Runtime.Igi2AiWorld? LevelAi(Engine.Titles.IServiceCatalog services) =>
        services.TryGet(out Ai.Runtime.Igi2AiWorld? ai) ? ai : null;

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
    /// <param name="casings">Where the soldiers' spent casings fly, or <see langword="null"/> for nowhere.</param>
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
        IReadOnlyDictionary<Engine.Tasks.GameTask, string>? worn = null,
        Weapons.Igi2GunCasings? casings = null)
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
        services.TryGet(out Soldiers.Igi2DeathAnimCycle? deathCycle);
        Soldiers.Igi2Soldiers soldiers = Soldiers.Igi2Soldiers.Spawn(
            root,
            (task, rig) =>
            {
                Igi2LevelSurroundings surroundings = new(world, sounds) { Materials = materials, Pickups = pickups, Models = models, Grenades = grenades };
                IIgi2PlayerWeaponControl weapon = new Igi2NoWeaponControl();
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
                Soldiers.Igi2HumanSoldier shooter = body;
                arms.Effects = new Igi2LevelWeaponEffects(sounds, body.CheapHeadBonePosition, LevelAi(services), casing =>
                {
                    if (casings is not null && shooter.Model is { } dress && arms.Inventory?.Current?.WeaponType.FixedModel is { } held)
                    {
                        Igi2TitlePlayer.EjectFrom(casings, casing, Igi2BodyFrame.Build(shooter, shooter.BodyPose(), dress.Name, held, 0), held, shooter.TotalSpeed);
                    }
                });
                arms.ImpactEffects = new Igi2LevelImpactEffects(sounds, LevelAi(services)) { Explosions = grenades?.Explosions };
                if (materials is not null)
                {
                    arms.World = new Igi2LevelGunShotWorld(world, materials, LevelAi(services));
                }
            }

            body.Mind = new Soldiers.Igi2StandingSoldierMind();
            body.Difficulty = difficulty;
            body.AnimTriggers = triggers;
            if (deadBodySounds is not null)
            {
                body.DeadBodySounds = deadBodySounds;
            }

            if (deathCycle is not null)
            {
                body.DeathCycle = deathCycle;
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
            foreach (Ai.Igi2HumanAiTask ai in root.Descendants().OfType<Ai.Igi2HumanAiTask>())
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

    /// <summary>
    /// Every human of the level in <c>Human_ptQTaskList</c>'s order: each registers as it is made (<c>Human_CreateHandler</c>),
    /// so in the tree's order, the player where its <c>HumanPlayer</c> task stands.
    /// </summary>
    internal static List<Igi2Human> LevelHumans(Engine.Tasks.GameTask? root, Igi2TitlePlayer player)
    {
        List<Igi2Human> humans = [];
        if (root is not null)
        {
            foreach (Engine.Tasks.GameTask node in root.Descendants())
            {
                if (node is Igi2HumanPlayerTask && !humans.Contains(player.Human))
                {
                    humans.Add(player.Human);
                }
                else if (node is Ai.Igi2HumanSoldierTask { Body: { } soldier })
                {
                    humans.Add(soldier);
                }
            }
        }

        if (!humans.Contains(player.Human))
        {
            humans.Insert(0, player.Human);
        }

        return humans;
    }

    /// <summary>The level's tick, with the player bound to its <c>HumanPlayer</c> task, as <c>Flow_RunChildren</c> (<c>igi2 0x401A50</c>) runs it.</summary>
    /// <param name="request">The level and the session.</param>
    /// <param name="player">The player.</param>
    /// <param name="systems">
    /// The systems that bring the level up, in order; when none, the title's own (<see cref="Igi2LevelSystems.Create"/>) with
    /// the session's <see cref="LevelSystemEdits"/> made to them.
    /// </param>
    /// <returns>The level's run, or <see langword="null"/> when the level's scripts are not at hand or it places no player in its tree.</returns>
    /// <remarks>
    /// Everything the level's tasks are handed before the first tick is a <see cref="ILevelSystem{TBuild}"/> of
    /// <see cref="Igi2LevelSystems"/>, run in the list's order over one <see cref="Igi2LevelBuild"/>.
    /// </remarks>
    public static Igi2LevelRun? LevelRun(in TitlePlayerRequest request, Igi2TitlePlayer player, LevelSystems<Igi2LevelBuild>? systems = null)
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

        Igi2LevelRun level = new(root, natives, functions) { Dynamic = player.Dynamic };
        Igi2LevelBuild build = new(request, player, level, natives, functions, task, Models(request.World));
        if (systems is null)
        {
            systems = Igi2LevelSystems.Create();
            if (request.Services.TryGet(out LevelSystemEdits? edits))
            {
                edits.Apply(systems);
            }
        }

        systems.BuildAll(build);
        return level;
    }

    /// <summary>Gives a human the detectors it is seen and sees through, as the level's detection system makes them.</summary>
    /// <param name="human">The human, as a level's spawn made it.</param>
    /// <param name="detection">The level's detectors.</param>
    public static void SeeThrough(Igi2Human human, Detection.Igi2DetectionWorld detection)
    {
        ArgumentNullException.ThrowIfNull(human);
        ArgumentNullException.ThrowIfNull(detection);
        if (human.Surroundings is Igi2LevelSurroundings around)
        {
            around.Detection = detection;
        }
    }

    /// <summary>Sends the AI events humans raise to a level's AI, once it is made (<c>HumanAI_SendEvent</c>).</summary>
    /// <param name="humans">The humans, as a level's spawn made them.</param>
    /// <param name="ai">The level's AI, read as each event is raised.</param>
    public static void HearAiEvents(IEnumerable<Igi2Human> humans, Func<Ai.Runtime.Igi2AiWorld?> ai)
    {
        ArgumentNullException.ThrowIfNull(humans);
        ArgumentNullException.ThrowIfNull(ai);
        foreach (Igi2Human human in humans)
        {
            if (human.Surroundings is Igi2LevelSurroundings heard)
            {
                heard.AiEvents = e => ai()?.SendEvent(e);
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
        Igi2CollisionWorld collision = Igi2CollisionWorld.Build(scene, models, ground, TerrainOwner, forests, magic);
        collision.Ground = built.Ground;
        return collision;
    }

    /// <summary>Resolves a placed model name to its mesh through the mission's packs.</summary>
    private static Func<string, Formats.World.MefModel?> Models(in WorldBuildRequest world)
    {
        ArgumentNullException.ThrowIfNull(world.Mission);
        List<Formats.World.MtpPack> mtp = [.. world.Mission.PacksInLoadOrder.Select(static pack => pack.Pack)];
        return Igi2DrawableScene.Models(Igi2ModelTable.Create(mtp), world.Mission.PacksInLoadOrder);
    }
}
