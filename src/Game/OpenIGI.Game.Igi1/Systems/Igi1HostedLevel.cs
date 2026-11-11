using OpenIGI.Core.Titles;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Level;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Systems;

/// <summary>
/// Project I.G.I.'s soldiers running in another game's level: their simulation and walk, the host's humans they stand for, and
/// the colliders they stand in the host's world as.
/// </summary>
/// <remarks>
/// The host ticks it once each of its own ticks (<see cref="Tick"/>), and takes it down as its mission is left (<see cref="Leave"/>).
/// A presenter draws the simulation's soldiers (<see cref="Soldiers"/>, <see cref="Groups"/>) whoever ticks it.
/// </remarks>
public sealed class Igi1HostedLevel
{
    private readonly TaskTree _tasks;
    private readonly LevelUpdateWalker _walker;
    private readonly IReadOnlyDictionary<HumanSoldierTask, Igi1StandIn> _standIns;
    private readonly IReadOnlyList<Igi1HumanLineCollider> _colliders;
    private readonly IHostCollisionWorld _world;
    private readonly IAiPlayer _player;
    private readonly HostedLevelHooks _hooks = new();
    private bool _left;

    internal Igi1HostedLevel(
        TaskTree tasks,
        LevelTask root,
        SoldierSimulation soldiers,
        LevelUpdateWalker walker,
        IReadOnlyList<SoldierLoadedGroup> groups,
        IReadOnlyDictionary<HumanSoldierTask, Igi1StandIn> standIns,
        IReadOnlyList<Igi1HumanLineCollider> colliders,
        IHostCollisionWorld world,
        IAiPlayer player,
        GunPickupRegistry gunPickups)
    {
        _tasks = tasks;
        GunPickups = gunPickups;
        Root = root;
        Soldiers = soldiers;
        _walker = walker;
        Groups = groups;
        _standIns = standIns;
        _colliders = colliders;
        _world = world;
        _player = player;
    }

    /// <summary>Gets the level's root, in the guest's task tree.</summary>
    public LevelTask Root { get; }

    /// <summary>Gets the game's guns lying in the host's level: those laid in the host's own's stead, and those the soldiers drop.</summary>
    public GunPickupRegistry GunPickups { get; }

    /// <summary>Gets the soldiers' simulation.</summary>
    public SoldierSimulation Soldiers { get; }

    /// <summary>Gets the soldiers as the simulation stood them, by model, for whatever draws them.</summary>
    public IReadOnlyList<SoldierLoadedGroup> Groups { get; }

    /// <summary>Gets each soldier as the host's level finds it.</summary>
    public IReadOnlyCollection<Igi1StandIn> StandIns => [.. _standIns.Values];

    /// <summary>Gets how many of the soldiers have a brain: a mind of a kind the game knows, on a graph the host ships.</summary>
    public int Thinking => Soldiers.Brains.Count;

    /// <summary>Gets the ticks the level has run.</summary>
    public int Ticks { get; private set; }

    /// <summary>Has a soldier stand for a human of the host's: the host gives it no body of its own, and finds the soldier in its stead.</summary>
    /// <param name="host">The host's human.</param>
    /// <param name="soldier">The soldier, one of this level's.</param>
    /// <exception cref="ArgumentException">The soldier is not this level's.</exception>
    public void StandFor(IHostedHuman host, HumanSoldierTask soldier)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(soldier);
        if (!_standIns.TryGetValue(soldier, out Igi1StandIn? standIn))
        {
            throw new ArgumentException("The soldier is not this level's.", nameof(soldier));
        }

        host.Driver = GameTitle.Igi1;
        host.StandIn = standIn;
    }

    /// <summary>
    /// Joins the soldiers to the host's level: struck by its blasts, told its AI events and seen by its detectors as its own humans
    /// are; and the host's humans hear the soldiers' events after the soldiers' own.
    /// </summary>
    /// <param name="joined">The host level's joiners.</param>
    /// <param name="hostHumans">The host's humans, which hear the soldiers' AI events.</param>
    public void JoinTo(LevelJoiners joined, IEnumerable<IHumanAiEventTarget> hostHumans)
    {
        ArgumentNullException.ThrowIfNull(joined);
        ArgumentNullException.ThrowIfNull(hostHumans);
        foreach (Igi1StandIn standIn in _standIns.Values)
        {
            joined.JoinExplosions(standIn);
            joined.JoinHumans(standIn);
            joined.JoinSeen(standIn);
        }

        Soldiers.Humans.AddRange(hostHumans);
    }

    /// <summary>Has something done at the end of each of the level's ticks.</summary>
    /// <param name="action">What is done.</param>
    public void EachTick(Action action) => _hooks.EachTick(action);

    /// <summary>Has something done as the level is left, before its tasks are deleted.</summary>
    /// <param name="action">What is done.</param>
    public void OnLeave(Action action) => _hooks.OnLeave(action);

    /// <summary>
    /// Runs the level a tick: every soldier where its walk reaches it, looking out for the host's player as it stands now, then the
    /// tick closed (<see cref="SoldierSimulation.EndTick"/>).
    /// </summary>
    public void Tick()
    {
        if (_left)
        {
            return;
        }

        Soldiers.BeginTick(Enemy);
        _walker.Tick();
        Soldiers.EndTick();
        Soldiers.PublishCollisionBodies();
        Ticks++;
        _hooks.RunTick();
    }

    /// <summary>Takes the level down as its mission is left: its colliders out of the host's world, and its tasks deleted.</summary>
    public void Leave()
    {
        if (_left)
        {
            return;
        }

        _left = true;
        _hooks.RunLeave();

        foreach (Igi1HumanLineCollider collider in _colliders)
        {
            _world.RemoveCollider(collider);
        }

        Soldiers.Retire();
        if (Root.Parent is not null)
        {
            _tasks.Delete(Root);
        }
    }

    // The host's player as a soldier sees it, read as each soldier updates.
    private SoldierEnemy Enemy() =>
        new(_player.OriginalViewPosition, !_player.IsDead, _player.Position, _player.IsKeepingLow);
}
