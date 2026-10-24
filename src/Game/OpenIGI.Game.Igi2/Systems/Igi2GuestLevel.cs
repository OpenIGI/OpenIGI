using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Titles;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi2.Ai;
using OpenIGI.Game.Igi2.Ai.Runtime;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Audio;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Detection;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Scripting;
using OpenIGI.Game.Igi2.ViewModel;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Physics;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Igi2.Systems;

/// <summary>
/// An I.G.I. 2 level of squads and soldiers hosted inside another game's level: its tasks made in a guest session of
/// I.G.I. 2, then brought up as I.G.I. 2 brings its own -- the bodies, the detectors, the minds and the level's walk.
/// </summary>
/// <remarks>
/// <para>
/// The host decides who stands where; this does what I.G.I. 2's own level build does with them, in its order
/// (<see cref="Igi2ScriptSystems"/>): the soldiers spawned (<see cref="Igi2Gameplay.SpawnSoldiers"/>), the detectors listing
/// them and the player, the minds given their scripts and graphs (<see cref="Igi2LevelAi.Build"/>), every human's AI events
/// sent to the level's AI, and the level run that walks it.
/// </para>
/// <para>
/// Each guest task keeps the id the host asks for when the guest has not given it out, else the next spare id from the top:
/// a mind finds its script by its id, and one without an id would run none.
/// </para>
/// </remarks>
public sealed class Igi2GuestLevel
{
    private const int TopId = 4095;

    private readonly Igi2GuestContent _content;
    private readonly GuestTitle _guest;
    private readonly List<Interaction.Igi2AlarmControlTask> _alarms = [];
    private Igi2TitlePlayer? _player;
    private Igi2CollisionWorld? _playerWorld;
    private Igi2Sounds? _sounds;
    private readonly List<Weapons.Igi2StationaryGunTask> _guns = [];
    private int _spare = TopId;

    private Igi2GuestLevel(Igi2GuestContent content, LevelTask root, GameTask dynamic)
    {
        _content = content;
        _guest = content.Guest;
        Root = root;
        Dynamic = dynamic;
    }

    /// <summary>Gets the level's root, in the guest's task tree.</summary>
    public LevelTask Root { get; }

    /// <summary>Gets the level's dynamic container, which its squads are made under.</summary>
    public GameTask Dynamic { get; }

    /// <summary>Opens a level in a guest session of I.G.I. 2: its root and its dynamic container.</summary>
    /// <param name="content">What the level's soldiers are made of, in the guest session it was read in.</param>
    /// <returns>The level, empty.</returns>
    public static Igi2GuestLevel Open(Igi2GuestContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        Engine.Flow.GameLoop loop = content.Guest.Loop;
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        GameTask dynamic = loop.Tasks.New(root, loop.Types.Get(EngineTaskTypes.Dynamic));
        return new Igi2GuestLevel(content, root, dynamic);
    }

    /// <summary>Gets the level's own player, I.G.I. 2's, where the host stood one (<see cref="AddPlayer"/>); else <see langword="null"/>.</summary>
    public Igi2TitlePlayer? Player => _player;

    /// <summary>
    /// Stands I.G.I. 2's player in the level in the host's player's stead, as I.G.I. 2 stands its own (<see cref="Igi2Gameplay.CreatePlayer"/>):
    /// dressed and armed, its first-person view and its weapons' effects, moving through the level's collision; before the level
    /// starts, which then makes it the one the soldiers look out for, the one their blasts and the level's pickups reach.
    /// </summary>
    /// <param name="world">The level's collision, which the soldiers move through too (<see cref="Igi2GuestLevelStart.World"/>).</param>
    /// <param name="start">Who the player is.</param>
    /// <returns>The player, which the host ticks as I.G.I. 2's own (<see cref="Game.Player.ITitlePlayer"/>).</returns>
    /// <exception cref="InvalidOperationException">The level has a player already.</exception>
    public Igi2TitlePlayer AddPlayer(Igi2CollisionWorld world, Igi2GuestPlayerStart start)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(start);
        if (_player is not null)
        {
            throw new InvalidOperationException("The level has its player.");
        }

        IServiceCatalog services = _guest.Session.Services;
        services.TryGet(out Igi2PlayerTuning? tuning);
        Func<string, Formats.World.MefModel?> models = _content.Models;
        string? arms = start.FirstPersonModel is { Length: > 0 } wanted && models(wanted)?.BoneHierarchy is not null
            ? wanted
            : models(Igi2Gameplay.FirstPersonFallbackModel)?.BoneHierarchy is not null ? Igi2Gameplay.FirstPersonFallbackModel : null;
        Igi2PlayerWeapons? weapons = null;
        if (services.TryGet(out Igi2HumanWeapons? armoury))
        {
            Igi2Inventory inventory = armoury.CreateInventory(tuning?.AmmoLimits ?? []);
            armoury.RunWeaponScript(inventory, start.WeaponScript);
            weapons = new Igi2PlayerWeapons(inventory, armoury.FireTaskTypes, _content.Clips);
        }

        _sounds ??= new Igi2Sounds(_content.SoundDefinitions, start.Audio);
        Igi2TitlePlayer player = Igi2TitlePlayer.Spawn(
            world,
            tuning?.General ?? Igi2HumanPlayerGeneral.Unset,
            start.Position,
            start.Gamma,
            start.Difficulty,
            _content.Clips,
            models(start.Model)?.BoneHierarchy,
            weapons,
            arms is null ? null : models(arms)?.BoneHierarchy,
            _sounds);
        player.GameConfig = services.TryGet(out Config.Igi2GameConfig? config) ? config : null;
        // The session's materials name what the player stands on: his steps' sounds, his landings, what his eyes see through.
        player.Materials = services.TryGet(out Igi2GameMaterials? standing) ? standing : null;
        player.ModelAttachments = new Igi2ModelAttachments(models, services.TryGet(out Igi2MagicObjTypes? attachments) ? attachments : null);
        player.Casings = new Igi2GunCasings(models);
        player.OverlayTypeNames = Igi2Gameplay.OverlayTypeNames(_guest.Loop.Natives);
        if (services.TryGet(out Igi2WhizzbyClock? clock))
        {
            player.Human.WhizzbyClock = clock;
        }

        if (services.TryGet(out Igi2DeadBodySoundCycle? deadBodySounds))
        {
            player.Human.DeadBodySounds = deadBodySounds;
        }

        player.Human.Model = Igi2BodyModel.From(start.Model, models(start.Model));
        player.Human.Collider = new Igi2BoneCollider(player.Human, player.Human);
        world.AddBody(player.Human.Collider);
        Igi2AiWorld? ai = services.TryGet(out Igi2AiWorld? levelAi) ? levelAi : null;
        if (weapons is not null)
        {
            weapons.Effects = new Igi2LevelWeaponEffects(_sounds, () => player.Human.View.Position, ai, player.EjectCasing);
            weapons.ImpactEffects = new Igi2LevelImpactEffects(_sounds, ai);
            if (services.TryGet(out Igi2GameMaterials? materials))
            {
                weapons.World = new Igi2LevelGunShotWorld(world, materials, ai);
            }
        }

        if (services.TryGet(out Igi2AnimTriggers? triggers))
        {
            player.Human.AnimTriggers = triggers;
        }

        if (models(start.Model) is { } body)
        {
            player.Human.ModelRadius = body.NativeMarkerBound;
            if (body.BoneHierarchy is { BoneCount: > 0 } bones)
            {
                player.Human.RootTranslation = bones.Translations[0];
            }
        }

        player.Human.View.ObjectModel = arms ?? string.Empty;
        player.Human.WeaponCycle = tuning?.WeaponCycle ?? [];
        player.Quirks = services.TryGet(out Igi2Quirks? quirks) ? quirks : new Igi2Quirks();
        TaskTree tasks = _guest.Loop.Tasks;
        player.TaskById = id => tasks.FromId(id);
        player.Strings ??= new Flow.Igi2Strings(_guest.Resources);

        // The map computer, which the player opens as I.G.I. 2's: its map looks down on the host's level.
        if (services.TryGet(out Computer.Igi2Computer? computer))
        {
            computer.BeginLevel();
            player.Computer = computer;
        }

        _player = player;
        _playerWorld = world;
        return player;
    }

    /// <summary>
    /// Lays a weapon in the level for its player to pick up, as a level's <c>GunPickup</c> lies (<see cref="Pickups.Igi2GunPickupTask"/>),
    /// before the level starts, which then lists it among its pickups.
    /// </summary>
    /// <param name="weaponId">The weapon, by I.G.I. 2's id (<c>WEAPON_ID_AK47</c>); one I.G.I. 2 has no weapon of is not laid.</param>
    /// <param name="position">Where it lies, in world units.</param>
    /// <param name="orientation">How it lies, as a level gives a placement's angles.</param>
    /// <returns>Whether it was laid.</returns>
    public bool AddGunPickup(string weaponId, Vector3d position, Vector3f orientation)
    {
        ArgumentException.ThrowIfNullOrEmpty(weaponId);
        if (!_guest.Session.Services.TryGet(out Igi2HumanWeapons? armoury)
            || !armoury.Tables.WeaponTypes.Any(w => string.Equals(w.WeaponId, weaponId, StringComparison.Ordinal)))
        {
            return false;
        }

        Pickups.Igi2GunPickupTask pickup = (Pickups.Igi2GunPickupTask)New(Root, Pickups.Igi2GunPickupTask.TypeName);
        pickup.WeaponId = weaponId;
        pickup.Position = position;
        pickup.Orientation = orientation;
        return true;
    }

    /// <summary>
    /// Mounts one of I.G.I. 2's guns in the level where the host mounts one of its own, under the host's id, as a level's
    /// <c>StationaryGun</c> stands (<see cref="Weapons.Igi2StationaryGunTask"/>), before the level starts, which then arms it
    /// for its player and its soldiers to work.
    /// </summary>
    /// <param name="id">The host gun's id, which soldiers ordered to man it name (<c>AIFunction_SetGunnerID</c>).</param>
    /// <param name="position">Its pivot, in world units.</param>
    /// <param name="orientation">The way it faces, as a level gives a placement's angles.</param>
    /// <param name="maxUp">The most it tilts up, in degrees.</param>
    /// <param name="maxDown">The most it tilts down, in degrees (negative).</param>
    /// <param name="maxSideways">The most it turns either way, in degrees.</param>
    /// <param name="weaponId">Its weapon, by I.G.I. 2's id.</param>
    /// <returns>The gun; <see langword="null"/> where the id is the guest's already.</returns>
    public Weapons.Igi2StationaryGunTask? AddStationaryGun(
        int id, Vector3d position, Vector3f orientation, float maxUp, float maxDown, float maxSideways, string weaponId = "WEAPON_ID_M2HB")
    {
        TaskTree tasks = _guest.Loop.Tasks;
        if (tasks.FromId(id) is not null)
        {
            return null;
        }

        Weapons.Igi2StationaryGunTask gun = (Weapons.Igi2StationaryGunTask)New(Root, Weapons.Igi2StationaryGunTask.TypeName);
        if (!tasks.TryBindId(id, gun))
        {
            tasks.Delete(gun);
            return null;
        }

        gun.Position = position;
        gun.Orientation = orientation;
        gun.MaxUpAngle = maxUp;
        gun.MaxDownAngle = maxDown;
        gun.MaxSidewaysAngle = maxSideways;
        gun.WeaponId = weaponId;
        _guns.Add(gun);
        return gun;
    }

    /// <summary>Adds a squad to the level, under its dynamic container, at a spare id.</summary>
    /// <returns>The squad.</returns>
    public Igi2AiSquadTask AddSquad()
    {
        Igi2AiSquadTask squad = (Igi2AiSquadTask)New(Dynamic, Igi2AiSquadTask.TypeName);
        Bind(squad, -1);
        return squad;
    }

    /// <summary>Adds a soldier to a squad, at the id the host asks for where it is free.</summary>
    /// <param name="squad">The squad.</param>
    /// <param name="preferredId">The id wanted, or a negative one for any.</param>
    /// <returns>The soldier, for the host to place, dress and arm.</returns>
    public Igi2HumanSoldierTask AddSoldier(Igi2AiSquadTask squad, int preferredId)
    {
        ArgumentNullException.ThrowIfNull(squad);
        Igi2HumanSoldierTask soldier = (Igi2HumanSoldierTask)New(squad, Igi2HumanSoldierTask.TypeName);
        Bind(soldier, preferredId);
        return soldier;
    }

    /// <summary>Adds a soldier's mind, at the id the host asks for where it is free.</summary>
    /// <param name="soldier">The soldier.</param>
    /// <param name="preferredId">The id wanted, or a negative one for any.</param>
    /// <returns>The mind, for the host to give its type and graph.</returns>
    public Igi2HumanAiTask AddMind(Igi2HumanSoldierTask soldier, int preferredId)
    {
        ArgumentNullException.ThrowIfNull(soldier);
        Igi2HumanAiTask mind = (Igi2HumanAiTask)New(soldier, Igi2HumanAiTask.TypeName);
        Bind(mind, preferredId);
        return mind;
    }

    /// <summary>
    /// Adds an alarm of the host's to the level, as I.G.I. 2's soldiers raise and hear their own: a control whose alarm is the
    /// host's, and, where the host's has one, a trigger where the host's is, which a soldier the squad sends presses
    /// (<c>AISquad_TriggerAlarm</c>).
    /// </summary>
    /// <param name="controlId">The host's alarm control's id, which the squads that run for it name (<c>AlarmControlId</c>).</param>
    /// <param name="isOn">Whether the host's alarm is sounding this tick.</param>
    /// <param name="trigger">
    /// The host's alarm trigger: its id, which the squads press (<c>AlarmTriggerId</c>), where it is in world units, and what
    /// presses the host's own, as one of its guards presses it; <see langword="null"/> for an alarm the soldiers only hear.
    /// </param>
    /// <returns>Whether the alarm was added; not where an id is the guest's already.</returns>
    /// <remarks>
    /// The control's alarm expression reads the host's alarm, so as it starts and stops the control tells every soldier
    /// (<c>AIEVENT_ALARMON</c>, <c>AIEVENT_ALARMOFF</c>), as I.G.I. 2's own does, whoever raised it.
    /// </remarks>
    public bool AddAlarm(int controlId, Func<bool> isOn, (int Id, Vector3d Position, Action Pressed)? trigger = null)
    {
        ArgumentNullException.ThrowIfNull(isOn);
        TaskTree tasks = _guest.Loop.Tasks;
        if (tasks.FromId(controlId) is not null || trigger is { } named && (named.Id == controlId || tasks.FromId(named.Id) is not null))
        {
            return false;
        }

        Interaction.Igi2AlarmControlTask control = (Interaction.Igi2AlarmControlTask)New(Root, Interaction.Igi2AlarmControlTask.TypeName);
        string variable = $"HostAlarm_{controlId}.isOn";
        _guest.Loop.Natives.RegisterVariable(variable, new QvmIntegerVariable(() => isOn() ? 1 : 0, static _ => { }));
        control.AlarmScript = variable;
        if (!tasks.TryBindId(controlId, control))
        {
            tasks.Delete(control);
            return false;
        }

        if (trigger is { } pressed)
        {
            Interaction.Igi2SwitchTask button = (Interaction.Igi2SwitchTask)New(Root, Interaction.Igi2SwitchTask.TypeName);
            button.Position = pressed.Position;
            button.Pressed = pressed.Pressed;
            control.Position = pressed.Position;
            if (!tasks.TryBindId(pressed.Id, button))
            {
                tasks.Delete(button);
                tasks.Delete(control);
                return false;
            }
        }

        _alarms.Add(control);
        return true;
    }

    /// <summary>The ticks a host's patrol animation pauses its soldier for: two seconds, about what a retail one takes.</summary>
    public const int AnimationPauseTicks = 60;

    /// <summary>
    /// Adds a patrol path to the level under the id the host gives it, which a soldier's script sends it on by
    /// (<c>AIAction_Patrol</c>): its commands in order, each as I.G.I. 2 keeps one.
    /// </summary>
    /// <param name="id">The path's id.</param>
    /// <param name="commands">Its commands, in the host's terms.</param>
    /// <returns>The path; <see langword="null"/> where the id is the guest's already, and no script could reach the path by it.</returns>
    /// <remarks>
    /// I.G.I. 2 reads a command's node, its delay or its animation from its <c>Command Parameter</c>
    /// (<c>HumanAIContent_PatrolRunCB</c>), as Project I.G.I. reads its operand, so each command keeps its kind and operand. An
    /// animation is the host's own number, which names nothing of I.G.I. 2's: it is a pause of about the length a retail
    /// patrol's animation takes instead (<see cref="AnimationPauseTicks"/>).
    /// </remarks>
    public Igi2PatrolPathTask? AddPatrolPath(int id, IEnumerable<AiPatrolCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        TaskTree tasks = _guest.Loop.Tasks;
        if (tasks.FromId(id) is not null)
        {
            return null;
        }

        Igi2PatrolPathTask path = (Igi2PatrolPathTask)New(Root, Igi2PatrolPathTask.TypeName);
        if (!tasks.TryBindId(id, path))
        {
            tasks.Delete(path);
            return null;
        }

        foreach (AiPatrolCommand command in commands)
        {
            Igi2PatrolPathCommandTask step = (Igi2PatrolPathCommandTask)New(path, Igi2PatrolPathCommandTask.TypeName);
            bool animation = command.Kind == AiPatrolCommandKind.Animation;
            step.CommandValue = animation ? (int)Igi2PatrolCommand.Delay : (int)command.Kind;
            step.Parameter = animation ? AnimationPauseTicks : command.Operand;
        }

        return path;
    }

    /// <summary>Brings the level up, as I.G.I. 2 brings its own: the bodies, the detectors, the minds and the run.</summary>
    /// <param name="start">What the host gives it.</param>
    /// <returns>The level, running, for the host to have its soldiers stand for its humans and then <see cref="Igi2HostedLevel.Begin"/>.</returns>
    public Igi2HostedLevel Start(Igi2GuestLevelStart start)
    {
        ArgumentNullException.ThrowIfNull(start);
        Engine.Flow.GameLoop loop = _guest.Loop;
        IServiceCatalog services = _guest.Session.Services;
        Igi2ScriptFunctions functions = services.Require<Igi2ScriptFunctions>();
        Igi2HostedLevel? hosted = null;
        Func<int> ticks = () => hosted?.Ticks ?? 0;

        Igi2Sounds sounds = _sounds ??= new(_content.SoundDefinitions, start.Audio);
        if (_player is not null && !ReferenceEquals(_playerWorld, start.World))
        {
            throw new InvalidOperationException("The level's player moves through another collision than its soldiers.");
        }
        Igi2GameMaterials materials = services.Require<Igi2GameMaterials>();
        Igi2AiWorld levelAi = services.Require<Igi2AiWorld>();
        Igi2DynamicTasks dynamic = new();

        // The level's world as I.G.I. 2 brings its own up (Igi2Gameplay.CreatePlayer): the blasts and what flies through
        // them, the pickups a soldier drops, and the casings its weapon throws.
        Igi2ExplosionWorld explosions = new(start.Random, start.World, materials, dynamic)
        {
            Effects = new Igi2LevelExplosionEffects(sounds, levelAi),
        };
        Igi2Grenades? grenades = null;
        if (services.TryGet(out PhysicsObjectTypes? physicsTypes))
        {
            Physics.Igi2PhysicsWorld flight = new(start.World, materials, static owner => !Collision.Igi2ObjectCollision.PassesThrough(owner));
            grenades = new Igi2Grenades(physicsTypes, _content.Models, flight, dynamic, start.Logger)
            {
                Effects = explosions,
                Explosions = explosions,
            };
            if (services.TryGet(out MissileRandom? missileStream))
            {
                grenades.MissileStream = missileStream.Random;
            }
        }

        Pickups.Igi2PickupList? pickups = null;
        if (services.TryGet(out Igi2HumanWeapons? weaponTables))
        {
            pickups = Pickups.Igi2PickupList.Build(Root, weaponTables.Tables, _content.Models, start.World, materials);
            pickups.Collision = start.World;
            pickups.Dynamic = dynamic;
            pickups.Random = start.Random;
            pickups.PlaySound = (name, at) => sounds.New(name, at);
        }

        Igi2GunCasings casings = new(_content.Models);
        Soldiers.Igi2Soldiers soldiers = Igi2Gameplay.SpawnSoldiers(
            Root,
            services,
            start.World,
            _content.Clips,
            _content.Models,
            services.TryGet(out Igi2PlayerTuning? tuning) ? tuning : null,
            sounds,
            start.Random,
            start.Difficulty,
            pickups,
            grenades,
            loop.Natives,
            functions,
            worn: null,
            casings);

        // Human_ptQTaskList: every human of the level, the bodies each move is kept out of; and one particle system for all. The
        // level's own player is among them, and throws, blows up and picks up in the level's world.
        List<Humans.Igi2Human> humans = [.. soldiers.Bodies];
        if (_player is { } own)
        {
            humans.Insert(0, own.Human);
            own.MapHumans = humans;
            own.Grenades = grenades;
            own.Explosions = explosions;
            own.Human.Pickups = pickups;
            if (grenades is not null && own.Weapons is { } arms)
            {
                arms.Grenades = grenades;
            }

            if (own.Weapons?.ImpactEffects is Igi2LevelImpactEffects impacts)
            {
                impacts.Explosions = explosions;
            }
        }

        Igi2Particles particles = new(start.World, dynamic);
        explosions.Humans = humans;
        explosions.Particles = particles;
        foreach (Humans.Igi2Human human in humans)
        {
            if (human.Surroundings is Igi2LevelSurroundings surroundings)
            {
                surroundings.Humans = humans;
                surroundings.Particles = particles;
            }
        }

        // A soldier out of the world -- held by its host's container, or a generator's life not yet spawned -- is struck by no blast.
        // And one whose mind has no graph of the host's to stand it on finds the host's ground by casting, as a body does.
        foreach (Soldiers.Igi2HumanSoldier body in soldiers.Bodies)
        {
            explosions.AddTarget(new ActiveBody(body));
            body.CastsForGroundOffGraph = true;
        }

        // The player is struck by the soldiers' blasts as by their rounds: the level's own, else the host's.
        if (_player is { } blasted)
        {
            explosions.AddTarget(blasted.Human);
        }
        else
        {
            explosions.AddTarget(start.Player);
        }

        // What the host's own level adds to the guest's world, after the guest's bodies: its player among them.
        foreach (IWorldLineCollider collider in start.Colliders)
        {
            start.World.AddCollider(collider);
        }

        Igi2DetectionWorld detection = new(start.World, services.Require<Igi2GameMaterials>())
        {
            FlowTicks = ticks,
            LevelTick = ticks,
        };
        // The host's doors and switches, which the level's own player uses as I.G.I. 2's player uses its own.
        if (_player is { } user)
        {
            foreach (WorldInteraction interaction in start.Interactions)
            {
                if (Interaction.Igi2HostInteraction.For(interaction, start.Activated) is { } usable)
                {
                    user.Activatables.Add(usable);
                }
            }

            foreach (LadderPlacement ladder in start.Ladders)
            {
                user.Activatables.Add(new Interaction.Igi2HostLadder(ladder));
            }
        }

        // The detectors look out for the level's own player where it has one, as I.G.I. 2's do; else for the host's.
        if (_player is { } seenPlayer)
        {
            detection.Detectables.Add(seenPlayer.Human);
            Igi2Gameplay.SeeThrough(seenPlayer.Human, detection);
            if (seenPlayer.Human.Surroundings is Igi2LevelSurroundings seenFrom)
            {
                seenFrom.Detection = detection;
            }
        }
        else
        {
            detection.Detectables.Add(start.Player);
        }

        foreach (IAiDetectable other in start.Others)
        {
            detection.Detectables.Add(other);
        }
        foreach (Soldiers.Igi2HumanSoldier body in soldiers.Bodies)
        {
            detection.Detectables.Add(body);
            Igi2Gameplay.SeeThrough(body, detection);
        }

        // HumanSoldier_UpdateInternalDataAfterLoadHandler: a living soldier of team 1 is no business of the detectors; it is listed
        // again as it dies.
        foreach (Soldiers.Igi2HumanSoldier body in soldiers.Bodies)
        {
            if (body is { Team: 1, IsDead: false })
            {
                detection.Remove(body);
            }
        }

        Igi2AiGraphs graphs = Igi2AiGraphs.Of(start.Graphs.Select(entry =>
        {
            // A graph on something that moves is held in its frame and rides it.
            Igi2AiGraph walked = Igi2AiGraph.From(
                entry.Value,
                text => Igi2LineScript.Evaluate(text, loop.Natives, functions),
                start.CarrierOf?.Invoke(entry.Key));
            walked.Id = entry.Key;

            // The host ships no cover for its graphs: it is felt for in the host's world, as the client does for a graph flagged to.
            walked.UpdateCover(start.World);
            return walked;
        }));
        Igi2AiWorld ai = Igi2LevelAi.Build(new Igi2LevelAiSetup(
            Root,
            _guest.Resources,
            loop.Natives,
            functions,
            (IAiPlayer?)_player?.Human ?? start.Player,
            [.. soldiers.Bodies.Cast<Humans.Igi2Human>()],
            start.Random,
            ticks)
        {
            Detection = detection,
            FireTaskTypes = services.TryGet(out Igi2HumanWeapons? armoury) ? armoury.FireTaskTypes : null,
            Config = services.TryGet(out Igi2HumanAiConfig? config) ? config : null,
            Difficulty = (int)start.Difficulty,
            Logger = start.Logger,
            World = services.Require<Igi2AiWorld>(),
            Graphs = graphs,
        });
        ai.Doors = [.. ai.Doors, .. start.Doors];
        // The guns mounted in the host's stead, armed as a level's own are (Igi2VehicleSystems.StationaryGuns): its player works
        // them, its soldiers find them by id, and they are run each tick.
        if (_guns.Count > 0 && services.TryGet(out Igi2HumanWeapons? gunWeapons))
        {
            Igi2MagicObjTypes? magic = services.TryGet(out Igi2MagicObjTypes? magicTypes) ? magicTypes : null;
            foreach (Weapons.Igi2StationaryGunTask gun in _guns)
            {
                gun.Magic = magic;
                gun.Random = start.Random;
                gun.Prepare(gunWeapons, _content.Models);
                gun.Bind([]);
                if (gun.Firing is { } firing)
                {
                    Weapons.Igi2StationaryGunTask armed = gun;
                    firing.Effects = new Igi2LevelWeaponEffects(sounds, () => armed.Mount, ai);
                    firing.ImpactEffects = new Igi2LevelImpactEffects(sounds, ai) { Explosions = explosions };
                    firing.World = new Igi2LevelGunShotWorld(start.World, materials, ai);
                }

                _player?.Activatables.Add(gun);
            }

            _player?.StationaryGuns = [.. _guns];
            Dictionary<int, Weapons.Igi2StationaryGunTask> gunsById = _guns.ToDictionary(static g => g.Id);
            ai.StationaryGun = id => gunsById.GetValueOrDefault(id);
        }

        Igi2Gameplay.HearAiEvents(soldiers.Bodies, () => ai);
        if (_player is { } heard)
        {
            Igi2Gameplay.HearAiEvents([heard.Human], () => ai);
        }
        foreach (Interaction.Igi2AlarmControlTask alarm in _alarms)
        {
            alarm.AiEvents = e => ai.SendEvent(e);
        }

        Igi2LevelRun run = new(Root, loop.Natives, functions) { Dynamic = dynamic };

        // The soldiers see the player as I.G.I. 2 sees its own, not at their cones' full length -- from their first look.
        // The level's own player measures its own (Igi2HumanPlayer), as I.G.I. 2's does.
        Igi2HostPlayerVisibility? visibility = _player is null && start.Visibility is { } seen ? new(start.Player, seen, start.Light) : null;
        visibility?.Update();
        hosted = new Igi2HostedLevel(loop.Tasks, Root, run, sounds, soldiers, detection, ai, visibility)
        {
            Pickups = pickups,
            Grenades = grenades,
            Casings = casings,
            Alarms = [.. _alarms],
            Player = _player,
            Guns = [.. _guns],
            WeaponTables = services.TryGet(out Igi2HumanWeapons? dropped) ? dropped.Tables : null,
            PickupModels = _content.Models,
        };
        return hosted;
    }

    private GameTask New(GameTask parent, string typeName) => _guest.Loop.Tasks.New(parent, _guest.Loop.Types.Get(typeName));

    private void Bind(GameTask task, int preferred)
    {
        TaskTree tasks = _guest.Loop.Tasks;
        if (preferred >= 0 && tasks.FromId(preferred) is null && tasks.TryBindId(preferred, task))
        {
            return;
        }

        while (tasks.FromId(_spare) is not null)
        {
            _spare--;
        }

        tasks.TryBindId(_spare--, task);
    }

    // A soldier as a blast finds it: only while it takes part in the level.
    private sealed class ActiveBody(Soldiers.Igi2HumanSoldier body) : IExplosionHitTarget
    {
        public void ExplosionHit(in ExplosionHit hit)
        {
            if (body.IsActive)
            {
                ((IExplosionHitTarget)body).ExplosionHit(hit);
            }
        }
    }
}
