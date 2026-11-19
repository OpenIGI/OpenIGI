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

    // Each soldier standing for a human of the host's level, with that human's task, and whether the host has it in the world.
    private readonly Dictionary<HumanSoldierTask, GameTask> _hosts = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<HumanSoldierTask, bool> _inWorld = new(ReferenceEqualityComparer.Instance);
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
        if (host is GameTask task)
        {
            _hosts[soldier] = task;
            Follow(soldier, task, initial: true);
        }
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

    /// <summary>
    /// Gives the player standing in the host's level what of that level it works by hand and climbs: the host's switches, doors,
    /// terminals and the like, each an interaction standing for the host's own (<see cref="WorldInteraction.Hosted"/>), and its
    /// ladders. The interactions are run each tick as the level's closes.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="interactions">The host's things a human works by hand.</param>
    /// <param name="ladders">The host's ladders.</param>
    /// <returns>The interactions as the player finds them.</returns>
    public WorldInteractionRegistry GivePlayer(
        Player.Igi1TitlePlayer player, IEnumerable<WorldInteraction> interactions, IReadOnlyList<LadderPlacement> ladders)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(interactions);
        ArgumentNullException.ThrowIfNull(ladders);
        WorldInteractionRegistry registry = WorldInteractionRegistry.OfHosted(interactions);
        registry.Clock = () => Ticks;
        player.Human.Rig.UseInteractions(registry);
        player.Human.Rig.UseLadders(ladders);
        EachTick(registry.Tick);
        return registry;
    }

    /// <summary>
    /// Has the object collision the guests walk kept where the host's objects stand, each tick as the level's closes: the host
    /// collides in a world of its own, and moves nothing in this one.
    /// </summary>
    /// <param name="solids">The collision the guests walk.</param>
    /// <param name="placements">The host's placements, which it was built from.</param>
    /// <param name="motions">What moves the host's placements, or <see langword="null"/> where nothing does.</param>
    /// <param name="presence">What takes them out of the host's world, or <see langword="null"/> where nothing does.</param>
    public void KeepSolids(
        ObjectCollisionIndex solids, IReadOnlyList<WorldPlacement> placements, IPlacementMotion? motions, IPlacementPresence? presence)
    {
        PlacementCollisionFollower follower = new(solids, placements, motions, presence);
        follower.Follow();
        EachTick(follower.Follow);
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

        FollowWorld();
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

    // A soldier is in the world while the host's human it stands for is, as the host's containers put that one in and take it
    // out: a guard the level has not brought on yet stands nowhere, sees nothing and is not seen.
    private void FollowWorld()
    {
        foreach ((HumanSoldierTask soldier, GameTask host) in _hosts)
        {
            Follow(soldier, host, initial: false);
        }
    }

    // Written as the host's answer changes, not each tick: between changes the soldier's own update may take its body out of the
    // world (a dead guard's, once it has lain long enough out of sight), and that stands.
    private void Follow(HumanSoldierTask soldier, GameTask host, bool initial)
    {
        bool now = ConditionalContainerGate.IsTaskActive(host);
        if (initial || _inWorld.GetValueOrDefault(soldier) != now)
        {
            _inWorld[soldier] = now;
            soldier.IsOutOfWorld = !now;
        }
    }

    // The host's player as a soldier sees it, read as each soldier updates.
    private SoldierEnemy Enemy() =>
        new(_player.OriginalViewPosition, !_player.IsDead, _player.Position, _player.IsKeepingLow);
}
