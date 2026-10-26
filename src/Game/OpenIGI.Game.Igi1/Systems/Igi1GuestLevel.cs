using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.Weapons;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Level;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Systems;

/// <summary>
/// A Project I.G.I. level of soldiers hosted inside another game's level: its tasks made in a guest session of the game, then
/// brought up as the game brings its own -- the bodies, the brains, the weapons and the level's walk.
/// </summary>
/// <remarks>
/// <para>
/// The host decides who stands where; this does what the game's own mission build does with them (<see cref="Igi1MissionSetup"/>):
/// the soldiers stood (<see cref="SoldierSimulation.Load"/>), given brains on the host's graphs (<see cref="SoldierSimulation.Animate"/>)
/// and armed (<see cref="SoldierSimulation.Arm"/>), their rounds marched through the host's world, and the walk that runs them.
/// </para>
/// <para>
/// Each soldier's mind keeps the id the host asks for where the guest has not given it out, else the next spare id from the top:
/// a mind finds its script by its id (<see cref="Igi1GuestContent.Scripts"/>).
/// </para>
/// </remarks>
public sealed class Igi1GuestLevel
{
    /// <summary>The type the game's levels arm a human with, a child of it naming the weapon.</summary>
    public const string GunType = "Gun";

    private readonly Igi1GuestContent _content;
    private readonly GameLoop _loop;
    private readonly GuestTaskIds _ids;

    private Igi1GuestLevel(Igi1GuestContent content, LevelTask root)
    {
        _content = content;
        _loop = content.Guest.Loop;
        _ids = new GuestTaskIds(_loop.Tasks);
        Root = root;
        GunPickups = GunPickupRegistry.Empty(content.Arsenal.Ammunition);
    }

    /// <summary>Gets the level's root, in the guest's task tree.</summary>
    public LevelTask Root { get; }

    /// <summary>
    /// Gets the game's guns lying in the host's level: those the host lays in its own's stead (<see cref="AddGunPickup"/>), and those
    /// the soldiers drop. The player takes them as the game's own; the soldiers' simulation drops and settles them.
    /// </summary>
    public GunPickupRegistry GunPickups { get; }

    /// <summary>Opens a level in the guest session of the game, empty, its soldiers' scripts forgotten.</summary>
    /// <param name="content">What the level's soldiers are made of, in the guest session it was read in.</param>
    /// <returns>The level.</returns>
    public static Igi1GuestLevel Open(Igi1GuestContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        GameLoop loop = content.Guest.Loop;
        content.Scripts.SoldierScripts.Clear();
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        return new Igi1GuestLevel(content, root);
    }

    /// <summary>Adds a soldier: its body, its mind on the host's graph, the guns it carries, and the script its orders make.</summary>
    /// <param name="soldier">The soldier.</param>
    /// <returns>Its task, which the host's human it stands for names.</returns>
    public HumanSoldierTask AddSoldier(Igi1GuestSoldier soldier)
    {
        ArgumentNullException.ThrowIfNull(soldier);
        HumanSoldierTask body = (HumanSoldierTask)New(Root, PlayerTaskTypes.HumanSoldier);
        body.Position = soldier.Position;
        body.Gamma = soldier.Gamma;
        body.Model = soldier.Model;
        body.Team = soldier.Team;
        Bind(body, -1);

        HumanAiTask mind = (HumanAiTask)New(body, AiTaskTypes.HumanAi);
        mind.AiTypeName = soldier.AiType;
        mind.GraphId = soldier.GraphId;
        Bind(mind, soldier.PreferredId);

        TaskType gun = GunTaskType();
        foreach (string weapon in soldier.Weapons)
        {
            DeclaredTask carried = (DeclaredTask)_loop.Tasks.New(body, gun);
            carried.Assign(new Dictionary<string, object?>
            {
                [PlayerLoadout.IdParameter] = weapon,
                [PlayerLoadout.SlotParameter] = 0,
            });
        }

        if (!soldier.Orders.IsEmpty)
        {
            _content.Scripts.SoldierScripts[mind.Id] = AiGuardScript.Build(soldier.Orders, squads: false);
        }

        return body;
    }

    /// <summary>
    /// Stands Project I.G.I.'s player in the host's level in its own player's stead, as the game's own mission stands it
    /// (<see cref="Igi1PlayerSetup"/>): the rig moving through the host's world and tuned by <c>humanplayer.qvm</c>, its view rig and
    /// what it carries, its life, and the host's player task standing for it -- the level's AI, detectors and scripts find it
    /// there, and the host's rounds and blasts strike it there.
    /// </summary>
    /// <param name="world">The host's world, which the player's grenades and view model meet, and which it stands in.</param>
    /// <param name="start">Who the player is.</param>
    /// <returns>The player, which the host ticks as its title player.</returns>
    public Igi1TitlePlayer AddPlayer(IHostCollisionWorld world, Igi1GuestPlayerStart start)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(start);
        IOpenIgiLogger log = start.Surroundings.Logger;
        PlayerSpawnPoint spawn = PlayerSpawn.Of(start.Task);

        // Its grenades and view model meet the host's world; its view model's forward sweep meets the ground alone.
        WorldTraceCast trace = world.Trace;
        WorldTraceCast ground = (in WorldTraceRequest request, out WorldTraceHit hit) =>
            world.Trace(request with { Mask = WorldTraceMask.Terrain }, out hit);
        Igi1PlayerAnimations animations = _content.PlayerAnimations;
        PlayerCamera rig = Igi1PlayerRig.Build(
            new Igi1PlayerWorld(start.Solids, world.Cast, trace, ground, start.Ladders), animations.Body, log);
        rig.UseMaterials(_content.Arsenal.Materials);

        Igi1PlayerLife life = new();
        int difficulty = (int)start.Difficulty;
        life.Reset(difficulty);
        try
        {
            if (Igi1PlayerRig.Tune(rig, _loop, _content.Guest.Resources, log) is { } tuning)
            {
                life.Tune(tuning, difficulty);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or KeyNotFoundException)
        {
            log.Warning("Player", $"Could not run humanplayer.qvm: {ex.Message}");
        }

        Igi1HumanPlayer human = new(rig, life, start.Surroundings);
        rig.UseHealth(life.Health);
        rig.Body.SetFallImpactSink(human.Land);
        if (animations.ViewRig is { } viewRig)
        {
            rig.UseViewRig(viewRig);
        }

        Igi1PlayerRig.Carry(rig, start.Loadout, _content.Arsenal.Weapons, _content.Arsenal.Ammunition, log);
        rig.Humans = Igi1PlayerRig.Rounds(rig, start.Bodies, () => start.Task);

        // It takes the game's guns lying in the level as it reaches them, with the game's cue.
        rig.UseGunPickups(GunPickups);
        rig.GunPickedUp += _ => start.Surroundings.PlayDefinition(Igi1MissionAudio.GunPickupSound, Igi1MissionAudio.GunPickupSoundPriority);
        rig.PlaceAt(world.Ground, spawn, start.Solids.PlayerRegistrationCaster());

        // The host's player task stands for it: its body is the rig, its events the player's, and it is the human the host's
        // world, AI and scripts meet.
        HumanPlayerTask task = start.Task;
        task.Driver = Core.Titles.GameTitle.Igi1;
        task.Body = new Igi1PlayerBody(() => rig, task);
        task.Events = human;
        task.StandIn = task;
        world.AddCollider(new Igi1HumanLineCollider(task, start.HumanGroup, start.HumanMaterial));
        return new Igi1TitlePlayer(human, world.Ground);
    }

    /// <summary>Lays one of the game's guns where the host laid one of its own, for the player to take.</summary>
    /// <param name="weaponId">The weapon, a <c>WEAPON_ID_*</c> name of the game's.</param>
    /// <param name="position">Where it lies, in world units.</param>
    /// <param name="orientation">How it lies.</param>
    /// <returns>The pickup; <see langword="null"/> where the game has no such weapon.</returns>
    public GunPickup? AddGunPickup(string weaponId, Core.Numerics.Vector3d position, Core.Numerics.Matrix3f orientation)
    {
        ArgumentException.ThrowIfNullOrEmpty(weaponId);
        return _content.Arsenal.Weapons.TryGetByScriptId(weaponId, out Game.Weapons.WeaponType weapon)
            ? GunPickups.Lay(position, orientation, weapon)
            : null;
    }

    /// <summary>
    /// Adds a patrol path under the id the host gives it, which a soldier's script sends it on by (<c>AIAction_Patrol</c>): its
    /// commands in order, as the game keeps them.
    /// </summary>
    /// <param name="id">The path's id.</param>
    /// <param name="commands">Its commands.</param>
    /// <returns>The path; <see langword="null"/> where the id is the guest's already, and no script could reach the path by it.</returns>
    public PatrolPathTask? AddPatrolPath(int id, IEnumerable<AiPatrolCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        TaskTree tasks = _loop.Tasks;
        if (tasks.FromId(id) is not null)
        {
            return null;
        }

        PatrolPathTask path = (PatrolPathTask)New(Root, AiTaskTypes.PatrolPath);
        if (!tasks.TryBindId(id, path))
        {
            tasks.Delete(path);
            return null;
        }

        foreach (AiPatrolCommand command in commands)
        {
            PatrolPathCommandTask step = (PatrolPathCommandTask)New(path, AiTaskTypes.PatrolPathCommand);
            step.Command = (int)command.Kind;
            step.CommandParameter = command.Operand;
        }

        return path;
    }

    /// <summary>Brings the level up, as the game brings its own: the bodies, the brains, the weapons, their rounds and the walk.</summary>
    /// <param name="start">What the host gives it.</param>
    /// <returns>The level, running, for the host to have its soldiers stand for its humans (<see cref="Igi1HostedLevel.StandFor"/>).</returns>
    public Igi1HostedLevel Start(Igi1GuestLevelStart start)
    {
        ArgumentNullException.ThrowIfNull(start);
        Igi1Arsenal arsenal = _content.Arsenal;
        IHostCollisionWorld world = start.World;
        SoldierSimulation soldiers = new(start.Logger);
        soldiers.UseRandom(start.Random);
        soldiers.UseMaterials(arsenal.Materials);

        // Their sight, the same trace as their rounds: the host's world, its materials read as the game's own rows.
        Func<int, int?> materialOf = start.MaterialOf;
        WorldTraceCast sight = (in WorldTraceRequest request, out WorldTraceHit hit) =>
        {
            if (!world.Trace(request, out hit))
            {
                return false;
            }

            hit = hit with { Material = materialOf(hit.Material) ?? 0 };
            return true;
        };

        IReadOnlyList<SoldierLoadedGroup> groups = soldiers.Load(_content.Mission, Root, world.Ground, world.Cast);
        soldiers.Animate(
            start.Graphs,
            sight,
            _content.Guest.Resources,
            _loop.Natives,
            Root,
            _content.Guest.Session.Services.TryGet(out HumanAiConfigRegistry? config) ? config : null,
            start.Difficulty,
            _loop.Variables);
        soldiers.Arm(_content.Mission, arsenal.Weapons);

        // A gun a soldier drops falls through the host's world into the level's pickups, for the player to take.
        soldiers.UseDroppedWeaponPickups(GunPickups);
        soldiers.SetWeaponDropSink((weapon, position, orientation, velocity) => GunPickups.Spawn(position, orientation, weapon, velocity));
        soldiers.SetDroppedWeaponWorld(null, world.Cast);

        // Each soldier stands in the host's world as a human the host's rounds and sight meet, and takes their hits as its stand-in.
        HashSet<object> own = new(ReferenceEqualityComparer.Instance);
        Dictionary<HumanSoldierTask, Igi1StandIn> standIns = new(ReferenceEqualityComparer.Instance);
        List<Igi1HumanLineCollider> colliders = [];
        foreach (HumanSoldierTask soldier in Root.Descendants().OfType<HumanSoldierTask>())
        {
            Igi1StandIn standIn = new(soldier, start.DamageFromHost);
            Igi1HumanLineCollider collider = new(soldier, start.HumanGroup, start.HumanMaterial) { Handler = standIn };
            world.AddCollider(collider);
            standIns[soldier] = standIn;
            colliders.Add(collider);
            own.Add(soldier);
        }

        // Their rounds march through the host's world, and strike their own as the game's do.
        Igi1HostRounds rounds = new(world, soldiers, arsenal, materialOf, owner => own.Contains(owner))
        {
            DamageScale = start.DamageToHost,
            Sound = start.Audio is { } audio ? (name, at) => audio.Play(name, at) : null,
        };
        soldiers.SetGunShotWorld(rounds, new GunShotSoundWindows(), afterMarch: null);
        if (start.Audio is { } voices)
        {
            soldiers.SetScriptSound((name, at, _) => voices.Play(name, at));
            soldiers.SetDeathSound(at => voices.Play(HumanVariantSound.AiDeathPrefix + "1", at));
        }

        LevelUpdateWalker walker = new(Root);
        soldiers.AttachTo(walker);
        return new Igi1HostedLevel(_loop.Tasks, Root, soldiers, walker, groups, standIns, colliders, world, start.Player, GunPickups);
    }

    // The type a soldier's guns are made as: the game's levels declare it, and a guest session of it, which reads no level, has
    // it declared with what the game reads of a gun (PlayerLoadout.WeaponsOf).
    private TaskType GunTaskType() =>
        _loop.Types.TryGet(GunType, out TaskType? declared)
            ? declared
            : _loop.Types.Declare(GunType, [new(PlayerLoadout.IdParameter, ScriptParamType.String32), new(PlayerLoadout.SlotParameter, ScriptParamType.Int32)]);

    private GameTask New(GameTask parent, string typeName) => _loop.Tasks.New(parent, _loop.Types.Get(typeName));

    private void Bind(GameTask task, int preferred) => _ids.Bind(task, preferred);
}
