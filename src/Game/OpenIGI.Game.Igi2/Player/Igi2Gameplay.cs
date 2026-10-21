using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Audio;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.ViewModel;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Igi2.Systems;
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
        Igi2Particles particles = new(world, player.Dynamic);
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
    private static Dictionary<int, string> OverlayTypeNames(OpenIGI.Scripting.Runtime.NativeRegistry? natives)
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

/// <summary>The ported player as the host steps it.</summary>
public sealed partial class Igi2TitlePlayer : ITitlePlayer, ITitleFirstPerson, ITitleWorldModels
{
    /// <summary>Gets or sets the title profile whose HUD preferences are currently active.</summary>
    public Config.Igi2GameConfig? GameConfig { get; set; }

    /// <summary>Gets or sets the cameras whose lenses and indicator lights are drawn.</summary>
    public IReadOnlyList<Interaction.Igi2SecurityCameraTask> Cameras { get; set; } = [];

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

    /// <summary>Keeps the click that chose something on a screen from firing the gun, the input's <c>isFreezeFire</c>.</summary>
    internal void FreezeFire() => _reader.FreezeFire = true;
    private readonly float[] _frame = new float[Igi2HumanChannels.Count];
    private readonly Igi2FirstPersonFrame _firstPerson = new();
    private long _ticks;
    private TitleFirstPersonView? _held;
    private long _heldTick = -1;
    private TitleWorldModel[] _lying = [];
    private long _lyingTick = -1;
    private TitleWorldSprite[] _lights = [];
    private long _lightsTick = -1;

    /// <inheritdoc/>
    public IReadOnlyList<TitleWorldSprite> WorldSprites
    {
        get
        {
            if (_lightsTick != _ticks)
            {
                _lights = [.. Cameras.Select(static c => c.Indicator()).OfType<TitleWorldSprite>()];
                _lightsTick = _ticks;
            }
            return _lights;
        }
    }

    private TitleGroundDecal[] _decals = [];
    private long _decalsTick = -1;

    /// <inheritdoc/>
    /// <remarks>
    /// Each body's mark where it shows (<see cref="Humans.Igi2HumanShadow"/>), the player's and the soldiers': coloured by the
    /// light where it lay, or nine tenths grey, times its strength above the ground (<c>Shadow_MagicObjEnterHandler</c>).
    /// </remarks>
    public IReadOnlyList<TitleGroundDecal> GroundDecals
    {
        get
        {
            if (_decalsTick != _ticks)
            {
                IEnumerable<Humans.Igi2Human> bodies = Soldiers?.Bodies ?? [];
                _decals = [.. bodies.Prepend(Human).Select(static h => Decal(h.Shadow)).OfType<TitleGroundDecal>()];
                _decalsTick = _ticks;
            }

            return _decals;
        }
    }

    private static TitleGroundDecal? Decal(Humans.Igi2HumanShadow shadow)
    {
        if (!shadow.IsEnabled || !shadow.IsPositionValid || shadow.Size <= 0f)
        {
            return null;
        }

        float shade = (shadow.Light ?? 0.9f) * shadow.Strength;
        byte channel = (byte)Math.Clamp((int)(shade * 255f), 0, 255);
        return new TitleGroundDecal(
            "LOCAL:common/textures/" + shadow.Texture,
            shadow.Position,
            shadow.Normal,
            shadow.Size,
            Humans.Igi2HumanShadow.Hover,
            OpenIGI.Core.Graphics.Bgra32.FromRgba(channel, channel, channel, 255));
    }

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

    /// <inheritdoc/>
    public IPlacementVariant? Variants { get; set; }

    /// <summary>Gets or sets the level's tick the player runs inside, or <see langword="null"/> to run the player alone.</summary>
    public Igi2LevelRun? Level { get; set; }

    /// <summary>Gets or sets the game-wide switches the level's scripts flip, or <see langword="null"/> for none.</summary>
    public Igi2FlowState? Flow { get; set; }

    /// <summary>Gets or sets the player's input task, or <see langword="null"/> when the level gives the player none.</summary>
    public Igi2HumanPlayerInputTask? Input { get; set; }

    /// <summary>Gets or sets the map computer, or <see langword="null"/>; the player opens it.</summary>
    public Computer.Igi2Computer? Computer
    {
        get => Human.Computer;
        set => Human.Computer = value;
    }

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
        : IsMapShown && ComputerDisplay?.Map is { } map ? map.Position
        : Human.CameraMode is 3 or 5 ? _movementCamera.Position
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

            if (IsMapShown)
            {
                return OpenIGI.Game.Igi2.Computer.Igi2ComputerMap.Orientation;
            }

            Matrix3f m = Human.CameraMode is 3 or 5 ? _movementCamera.Orientation
                : Human.View.IsOverride ? Human.View.OverrideOrientation : Human.View.Orientation;
            return Matrix3f.FromRows(
                new Vector3f(m.M11, m.M21, m.M31),
                new Vector3f(m.M13, m.M23, m.M33),
                new Vector3f(m.M12, m.M22, m.M32));
        }
    }

    /// <inheritdoc/>
    /// <remarks>The view's <c>vFOV</c>, which a zoom steers; a cutscene camera's own while one is placed.</remarks>
    public float FieldOfViewScale => Level?.CutsceneView is { } cut ? cut.FieldOfView
        : IsMapShown && ComputerDisplay?.Map is { } map ? map.FieldOfView
        : Human.CameraMode is 3 or 5 ? 1f : Human.View.FieldOfView;

    /// <inheritdoc/>
    /// <remarks>
    /// Built once a tick, on the first ask after it, from the view as the tick left it; none while a cutscene's camera is placed,
    /// which is not the player's eyes.
    /// </remarks>
    public TitleFirstPersonView? FirstPerson
    {
        get
        {
            if (Level?.CutsceneView is not null || Human.CameraMode != 1 || Human.IsThirdPerson || IsComputerShown)
            {
                return null;
            }

            if (_heldTick != _ticks)
            {
                _held = _firstPerson.Build(Human, Inventory?.Current?.WeaponType.FixedModel, _ticks, ModelAttachments, Weapons?.Firing.Flashes);
                _heldTick = _ticks;
            }

            return _held;
        }
    }

    /// <summary>Gets or sets the mission's resolver for the held model's nested magic objects.</summary>
    public Igi2ModelAttachments? ModelAttachments { get; set; }

    /// <summary>Gets or sets the spent casings in flight, the player's and the soldiers'; <see langword="null"/> for none.</summary>
    public Weapons.Igi2GunCasings? Casings { get; set; }

    /// <summary>
    /// Throws a casing from the player's weapon object: the one the first-person view holds, or, while the body is drawn working,
    /// the one in the body's hand.
    /// </summary>
    /// <param name="ejection">What the shot drew for it.</param>
    internal void EjectCasing(Weapons.Igi2CasingEjection ejection)
    {
        if (Casings is not { } casings || Inventory?.Current?.WeaponType.FixedModel is not { } held)
        {
            return;
        }

        if (Human.CameraMode is 3 or 5 && Human.Model is { } body)
        {
            EjectFrom(casings, ejection, Igi2BodyFrame.Build(Human, Human.BodyPose(), body.Name, held, _ticks), held, Human.TotalSpeed);
            return;
        }

        if (_firstPerson.Build(Human, held, _ticks, ModelAttachments) is not { } view)
        {
            return;
        }

        int index = 0;
        foreach (TitleViewModelPart part in view.Parts)
        {
            if (part.Pose is not null || !string.Equals(part.Model, held, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (index++ == ejection.WeaponObject)
            {
                Vector3f local = view.Orientation.Transposed().Transform(part.Position);
                Vector3d at = new(view.Origin.X + local.X, view.Origin.Y + local.Y, view.Origin.Z + local.Z);
                casings.Eject(ejection, part.Model, at, view.Orientation * part.Orientation, Human.TotalSpeed);
                return;
            }
        }
    }

    /// <summary>Throws a casing from the weapon object a body's frame holds, the weapon objects counted in the frame's order.</summary>
    /// <param name="casings">Where it flies.</param>
    /// <param name="ejection">What the shot drew for it.</param>
    /// <param name="frame">The body's models as this tick poses them.</param>
    /// <param name="held">The weapon in hand's model.</param>
    /// <param name="holderSpeed">The holder's speed.</param>
    internal static void EjectFrom(
        Weapons.Igi2GunCasings casings,
        in Weapons.Igi2CasingEjection ejection,
        IReadOnlyList<TitleWorldModel> frame,
        string held,
        Vector3f holderSpeed)
    {
        int index = 0;
        foreach (TitleWorldModel model in frame)
        {
            if (model.Pose is null && string.Equals(model.Model, held, StringComparison.OrdinalIgnoreCase) && index++ == ejection.WeaponObject)
            {
                casings.Eject(ejection, model.Model, model.Position, model.Orientation, holderSpeed);
                return;
            }
        }
    }

    /// <summary>Gets or sets the level's automatic guns.</summary>
    public IReadOnlyList<Weapons.Igi2AiStationaryGunHolderTask> Turrets { get; set; } = [];

    /// <summary>Gets or sets the level's in-game menu, which the shell presents.</summary>
    public Menus.Igi2InGameMenu? Menu { get; set; }

    /// <summary>Gets or sets the mission's save games.</summary>
    public Saves.Igi2MissionSaves? Saves { get; set; }

    /// <summary>Gets or sets the level's panes of glass.</summary>
    public Interaction.Igi2GlassContainer Glass { get; set; } = new();

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

    /// <summary>Gets or sets the level's list of what humans tell their place to, <c>GameFunctions_ptNotifyQTaskList</c>.</summary>
    public Weapons.Igi2NotifyList Notified { get; set; } = new();

    /// <summary>Gets or sets the level the player's mines are laid in, or <see langword="null"/>.</summary>
    public Igi2LevelMines? Mines { get; set; }

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
                        .Concat(grenades.Dynamic.Tasks.OfType<Weapons.Igi2ImpactGrenade>()
                            .Where(static g => g.Parameters.Model.Length != 0)
                            .Select(static g => new TitleWorldModel(g.Parameters.Model, g.Position, Rows(g.Orientation))))
                        .Concat(grenades.Dynamic.Tasks.OfType<Weapons.Igi2Missile>()
                            .Select(static m => new TitleWorldModel(m.Model, m.Position, Rows(m.Body.Orientation))))
                    : [];
                IEnumerable<TitleWorldModel> soldiers = Soldiers?.Models(_ticks) ?? [];
                IEnumerable<TitleWorldModel> figures = Figures.SelectMany(f => f.Models(_ticks));
                IEnumerable<TitleWorldModel> barrels = StationaryGuns.Select(static g => g.Barrel()).OfType<TitleWorldModel>();
                IEnumerable<TitleWorldModel> wrecks = Cars.Select(static c => c.Wreck()).OfType<TitleWorldModel>();
                IEnumerable<TitleWorldModel> laid = Mines is { } mineWorld
                    ? mineWorld.Laid.Select(static m => new TitleWorldModel(m.Explosive.Model, m.Position, Rows([.. m.Frame])))
                    : [];
                // human.c Human_GetHumanCameraInfoHandler; humancamera.c HumanCamera_MovementSetCamera:
                // interaction mode draws the working body rather than the first-person weapon.
                IReadOnlyList<TitleWorldModel> working = Human.CameraMode is 3 or 5 && Human.Model is { } body
                    ? Igi2BodyFrame.Build(Human, Human.BodyPose(), body.Name, Inventory?.Current?.WeaponType.FixedModel, _ticks, Weapons?.Firing.Flashes) : [];
                IEnumerable<TitleWorldModel> casings = Casings?.Models() ?? [];
                _lying = [.. pickups, .. flying, .. soldiers, .. figures, .. barrels, .. wrecks, .. laid, .. working, .. casings];
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
        IIgi2PlayerWeaponControl weapon = weapons ?? (IIgi2PlayerWeaponControl)new Igi2NoWeaponControl();
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
        Igi2PlayerAction held = Map(input.Held) | (Igi2PlayerAction)(input.TitleHeld | input.TitlePressed);
        Igi2PlayerAction pressed = Map(input.Pressed) | (Igi2PlayerAction)input.TitlePressed;
        _reader.MouseSensitivity = input.MouseSensitivity;
        _reader.InvertMouse = input.InvertMouse;
        Human.InvertMouse = input.InvertMouse;
        Human.UnfilteredMouse = input.UnfilteredMouse && CanPreviewLook;
        _lookInput = input;
        _rawKeys = input.Raw;
        _reader.Read(new Igi2PlayerInputState(
            Analog(input.MouseX, Human.UnfilteredMouse), Analog(input.MouseY, Human.UnfilteredMouse), held, pressed), _frame);
        if (Level is { } level)
        {
            Flow?.IsBreakKeyDown = input.BreakCutscene;
            level.Tick();

            // The glass container is the level's last child, made as the first pane was: its panes' glass tick follows the level's.
            Glass.Run();
            StatusMessages.Run();
        }
        else
        {
            RunPlayer();
            Dynamic.Run();
            RunPlayerDelayed();
        }

        _movementCamera.Run(Human, CollisionWorld, input.MouseX * CountsToAnalog, input.MouseY * CountsToAnalog);
        ResetLook();
        Casings?.Run();
        ReadBinoculars();
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
        if (Human.UnfilteredMouse) { WriteSmoothLook(); }
        Human.Ticks = (int)_ticks;
        Human.Update(_frame);
        Human.RunDelayed();

        // The computer's menu, a task of the game screen, runs after the player, and its screens with it.
        RunComputerSaves();
        Computer?.Run();
        RunComputerDisplay();
    }

    /// <summary>Nine floats row by row as a matrix.</summary>
    private static Matrix3f Rows(float[] m) => new(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8]);

    /// <summary>The mouse's counts as a 255th each; retail clamps to one, while Smooth Motion retains the raw range.</summary>
    private static float Analog(float counts, bool unfilteredMouse = false)
    {
        float analog = ((Fpu)(double)counts * CountsToAnalog).F;
        return unfilteredMouse ? analog : analog < -1f ? -1f : analog > 1f ? 1f : analog;
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
internal sealed class Igi2LevelGunShotWorld(IIgi2CollisionQuery collision, Igi2GameMaterials materials, Ai.Runtime.Igi2AiWorld? ai = null)
    : IIgi2GunShotWorld
{
    public void EvaluateGunShot(Igi2GunShot shot, object shooter) => ai?.EvaluateGunShot(shot, shooter);

    public IIgi2CollisionQuery Collision { get; } = collision;

    public Igi2GameMaterials Materials { get; } = materials;

    public bool IsWater(IIgi2Collider struck) => struck is World.Igi2WaterBody;

    public bool IsBoned(IIgi2Collider struck) => struck is Igi2BoneCollider;

    public bool IsHuman(IIgi2Collider struck) => struck is Igi2BoneCollider { Owner: Humans.Igi2Human };
}

/// <summary>What the player's states ask of the world, answered from the collision world and the level's sounds.</summary>
internal sealed class Igi2LevelSurroundings(Igi2CollisionWorld world, Igi2Sounds sounds) : IIgi2HumanSurroundings
{
    /// <summary>Gets or sets the body these surroundings are asked about.</summary>
    public Igi2Human? Owner { get; set; }

    public IIgi2CollisionQuery Collision { get; } = world;

    /// <summary>Gets or sets the list detection looks over, which a dying soldier joins.</summary>
    public Detection.Igi2DetectionWorld? Detection { get; set; }

    public void RegisterDetectable(Igi2Human body) => Detection?.Register(body);

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

    public void ThrowGunPickup(
        Igi2WeaponTypeTask weaponType,
        Vector3d position,
        IReadOnlyList<float> orientation,
        float throwGamma,
        int ammoInClip,
        IReadOnlyList<int> ammoCount,
        bool isThrowShort = true)
    {
        Weapons.Igi2WeaponTables? tables = Owner switch
        {
            Soldiers.Igi2HumanSoldier soldier => soldier.Weapon.Inventory?.Tables,
            Igi2HumanPlayer player => player.Weapon.Inventory?.Tables,
            _ => null,
        };
        if (Pickups is not { } pickups || tables is null)
        {
            return;
        }

        float radius = weaponType.FixedModel.Length > 0 && Models?.Invoke(weaponType.FixedModel) is { } model ? model.NativeMarkerBound : 1f;
        pickups.Add(OpenIGI.Game.Igi2.Pickups.Igi2GunPickup.Thrown(weaponType, tables, position, orientation, throwGamma, isMoving: true, isThrowShort, ammoInClip, ammoCount, radius));
    }

    public bool IsOnElevator => false;

    public bool FitsLyingFlat() => Owner?.FitsWhereItStands() ?? true;

    public float? SoundFalloffEnd(string name) =>
        sounds.Definitions.TryResolve(name, out Engine.Audio.SoundDefinition? definition) && definition!.Kind == Engine.Audio.SoundDefinitionKind.Sound
            ? definition.MaximumDistanceMetres
            : null;

    public void SendLandingEvent()
    {
    }

    /// <summary>Gets or sets the level's water, which the owner's depth is asked of.</summary>
    public World.Igi2Water? Water { get; set; }

    // The body the owner was last found in, Human_s.iWaterIndex.
    private int _waterIndex = -1;

    public bool IsUnderWater(out float depth)
    {
        depth = 0f;
        _waterIndex = -1;
        return Water is { } water && Owner is { } owner && water.IsUnderWater(owner.Position, out depth, out _waterIndex);
    }

    public void StirWater(bool isRunning)
    {
        if (Water is { } water && Owner is { } owner)
        {
            water.PlayerMovementEffect(owner.Position, _waterIndex, isRunning);
        }
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

    /// <summary>Gets or sets where the AI's events go, <c>HumanAI_SendEvent</c>, once the level's AI is made.</summary>
    public Action<Ai.Igi2AiEvent>? AiEvents { get; set; }

    /// <inheritdoc/>
    public void SendAiEvent(in Ai.Igi2AiEvent aiEvent) => AiEvents?.Invoke(aiEvent);

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
        sounds.New(name, View(), View) ?? (IIgi2SoundInstance)new SilentSound();

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
        public bool IsAlive => false;

        public void MoveToHuman()
        {
        }

        public void Delete()
        {
        }
    }
}

/// <summary>
/// The level the player's proximity mines are laid in: each joins the dynamic task, the collision world, the blasts' targets and the
/// notify list, and leaves them all once it has exploded.
/// </summary>
/// <param name="player">The player, whose dynamic task and explosions the mines use.</param>
/// <param name="collision">The collision world.</param>
/// <param name="models">The level's models.</param>
/// <param name="notified">The list humans tell their place to.</param>
public sealed class Igi2LevelMines(Igi2TitlePlayer player, Igi2CollisionWorld collision, Func<string, Formats.World.MefModel?> models, Weapons.Igi2NotifyList notified)
    : Weapons.IIgi2MineWorld
{
    private readonly List<Weapons.Igi2Mine> _laid = [];

    /// <summary>Gets the mines lying in the level, in the order they were laid.</summary>
    public IReadOnlyList<Weapons.Igi2Mine> Laid => _laid;

    /// <inheritdoc/>
    public Igi2CollisionWorld Collision => collision;

    /// <inheritdoc/>
    public Func<string, Formats.World.MefModel?> Models => models;

    /// <inheritdoc/>
    public Weapons.Igi2ExplosionWorld? Explosions => player.Explosions;

    /// <inheritdoc/>
    public Weapons.Igi2NotifyList Notified => notified;

    /// <inheritdoc/>
    public void Lay(Weapons.Igi2Mine mine)
    {
        ArgumentNullException.ThrowIfNull(mine);
        _laid.Add(mine);
        player.Dynamic.Add(mine);
        if (mine.Solid is { } solid)
        {
            collision.AddObject(solid);
        }

        player.Explosions?.AddTarget(mine);
        notified.Add(mine);
    }

    /// <inheritdoc/>
    public void Remove(Weapons.Igi2Mine mine)
    {
        ArgumentNullException.ThrowIfNull(mine);
        _laid.Remove(mine);
        if (mine.Solid is { } solid)
        {
            collision.RemoveObject(solid);
        }

        player.Explosions?.RemoveTarget(mine);
        notified.Remove(mine);
    }

    /// <inheritdoc/>
    public void PlaySoundAt(string sound, Vector3d position) => player.Sounds.New(sound, position);
}
