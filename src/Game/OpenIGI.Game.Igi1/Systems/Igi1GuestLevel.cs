using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.Weapons;
using OpenIGI.Game.Igi1.World;
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

    private const int TopId = 4095;

    private readonly Igi1GuestContent _content;
    private readonly GameLoop _loop;
    private int _spare = TopId;

    private Igi1GuestLevel(Igi1GuestContent content, LevelTask root)
    {
        _content = content;
        _loop = content.Guest.Loop;
        Root = root;
    }

    /// <summary>Gets the level's root, in the guest's task tree.</summary>
    public LevelTask Root { get; }

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
            _loop.Tasks.New(body, gun, new Dictionary<string, object?>
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
        return new Igi1HostedLevel(_loop.Tasks, Root, soldiers, walker, groups, standIns, colliders, world, start.Player);
    }

    // The type a soldier's guns are made as: the game's levels declare it, and a guest session of it, which reads no level, has
    // it declared with what the game reads of a gun (PlayerLoadout.WeaponsOf).
    private TaskType GunTaskType() =>
        _loop.Types.TryGet(GunType, out TaskType? declared)
            ? declared
            : _loop.Types.Declare(GunType, [new(PlayerLoadout.IdParameter, ScriptParamType.String32), new(PlayerLoadout.SlotParameter, ScriptParamType.Int32)]);

    private GameTask New(GameTask parent, string typeName) => _loop.Tasks.New(parent, _loop.Types.Get(typeName));

    private void Bind(GameTask task, int preferred)
    {
        TaskTree tasks = _loop.Tasks;
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
}
