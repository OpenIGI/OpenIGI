using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Flow;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.World;

/// <summary>
/// What a Project I.G.I. level's <c>AreaActivate</c> volumes count: the player the host reports, a co-op session's other
/// players, the soldiers and the vehicles, each through the criterion the original's module registers for it.
/// </summary>
/// <remarks>
/// Owned by <see cref="MissionObjectiveRuntime"/>. The areas themselves are tasks that run where the level's walk reaches
/// them (<see cref="AreaActivateTask"/>); this is the humans' and the vehicles' side of it.
/// </remarks>
internal sealed class MissionAreaActivation
{
    private readonly GameTask _levelRoot;
    private readonly List<AreaActivateTask> _areas = [];
    private readonly Dictionary<int, HumanOccupant> _soldierOccupants = [];
    private readonly Dictionary<int, VehicleOccupant> _vehicleOccupants = [];
    private bool _playerAlive = true;
    private Vector3d? _statePlayer;
    private int _stateTeam;
    private Vector3d? _stateExtraPlayer;
    private Func<IEnumerable<VehicleOccupant>>? _vehicleSource;
    private Func<IEnumerable<HumanOccupant>>? _soldierSource;
    private Func<AreaPlayer>? _playerSource;

    // As the match began: each area's count, and who stood where.
    private (int[] Counts, bool Alive, Vector3d? Player, int Team, Vector3d? Extra) _start = ([], true, null, 0, null);

    /// <summary>Builds the occupant set over the loaded level.</summary>
    /// <param name="levelRoot">The loaded level's task tree, which resolves occupants to their tasks.</param>
    public MissionAreaActivation(GameTask levelRoot)
    {
        _levelRoot = levelRoot;
    }

    /// <summary>Gets or sets what reads the level's other players -- a co-op session's -- each counted as the player is.</summary>
    public Func<IEnumerable<AreaPlayer>>? Partners { get; set; }

    /// <summary>Gets the state a save keeps of each area.</summary>
    internal IEnumerable<KeyValuePair<GameTask, Saves.ISavedState>> SavedStates =>
        _areas.Select(static a => new KeyValuePair<GameTask, Saves.ISavedState>(a, a));

    internal void MarkLevelStart() =>
        _start = ([.. _areas.Select(static a => a.Active)], _playerAlive, _statePlayer, _stateTeam, _stateExtraPlayer);

    /// <summary>Passes through a save who the areas last counted as the player, and where. Not retail.</summary>
    /// <param name="exchange">The exchange.</param>
    internal void Exchange(Saves.StateExchange exchange)
    {
        _playerAlive = exchange.Bool(_playerAlive);
        _statePlayer = exchange.Position(_statePlayer);
        _stateTeam = exchange.Int(_stateTeam);
        _stateExtraPlayer = exchange.Position(_stateExtraPlayer);
        if (exchange.IsReading)
        {
            _soldierOccupants.Clear();
            _vehicleOccupants.Clear();
        }
    }

    internal void ResetToLevelStart()
    {
        for (int i = 0; i < _areas.Count && i < _start.Counts.Length; i++)
        {
            _areas[i].Active = _start.Counts[i];
        }

        (_playerAlive, _statePlayer, _stateTeam, _stateExtraPlayer) = (_start.Alive, _start.Player, _start.Team, _start.Extra);
        _soldierOccupants.Clear();
        _vehicleOccupants.Clear();
    }

    /// <summary>Lists one of the level's areas, in the tree's order.</summary>
    /// <param name="area">The area.</param>
    public void Add(AreaActivateTask area) => _areas.Add(area);

    /// <summary>Registers the humans' and the vehicles' criteria with a level's run.</summary>
    /// <param name="criteria">The run's criteria.</param>
    public void Register(AreaCriteria criteria)
    {
        criteria.Set(Igi1AreaCriteria.Human, (area, _) => CountHumans(area, static _ => true));
        for (int team = 0; team < Igi1AreaCriteria.Teams; team++)
        {
            // Human_CountTeamCriteria (0x460A10): every human whose own team's bit is in the mask, once for each team bit set.
            criteria.Set(Igi1AreaCriteria.Team(team), (area, mask) => CountHumans(area, t => (Igi1AreaCriteria.Team(t) & mask) != 0));
        }

        criteria.Set(Igi1AreaCriteria.Vehicle, (area, _) => CountVehicles(area));
    }

    /// <summary>Sets where the occupants are read from when an area counts them; see <see cref="MissionObjectiveRuntime.AttachTo"/>.</summary>
    /// <param name="vehicles">Reads the vehicles' live positions when an area counts them.</param>
    /// <param name="soldiers">Reads the soldiers where they stand when an area counts them.</param>
    /// <param name="player">Reads the player where it stands when an area counts it.</param>
    public void SetSources(
        Func<IEnumerable<VehicleOccupant>>? vehicles,
        Func<IEnumerable<HumanOccupant>>? soldiers,
        Func<AreaPlayer>? player)
    {
        _vehicleSource = vehicles;
        _soldierSource = soldiers;
        _playerSource = player;
    }

    /// <summary>Latches the player's projections and liveness; see <see cref="MissionObjectiveRuntime.SetLevelStateInputs"/>.</summary>
    /// <param name="playerPosition">The player's primary world projection, when one exists.</param>
    /// <param name="playerTeam">The player's team.</param>
    /// <param name="extraPlayerPosition">An alternate projection of the same player.</param>
    /// <param name="playerAlive">Whether the player can occupy areas.</param>
    public void SetLevelStateInputs(Vector3d? playerPosition, int playerTeam, Vector3d? extraPlayerPosition, bool playerAlive)
    {
        _playerAlive = playerAlive;
        _statePlayer = playerPosition;
        _stateTeam = playerTeam;
        _stateExtraPlayer = extraPlayerPosition;
        if (_levelRoot.Tree.FromId(0) is HumanPlayerTask player && !playerAlive)
        {
            player.IsDead = true;
        }
    }

    /// <summary>Feeds the per-tick state of every soldier; see <see cref="MissionObjectiveRuntime.SetSoldierOccupants"/>.</summary>
    /// <param name="occupants">One entry per placed soldier, with its current position, team and death.</param>
    public void SetSoldierOccupants(IEnumerable<HumanOccupant> occupants)
    {
        _soldierOccupants.Clear();
        foreach (HumanOccupant occupant in occupants)
        {
            _soldierOccupants[occupant.TaskId] = occupant;

            // Write the report through to the task itself, which is where the original keeps it and
            // where HumanTask's export reads it from. A soldier the host has not reported keeps the
            // latch it already had, and a freshly declared one starts alive.
            if (_levelRoot.Tree.FromId(occupant.TaskId) is HumanTask human && occupant.IsDead)
            {
                human.IsDead = true;
            }
        }
    }

    /// <summary>Feeds the live positions of graph-driven vehicles into vehicle-only areas.</summary>
    /// <param name="occupants">One entry per vehicle.</param>
    public void SetVehicleOccupants(IEnumerable<VehicleOccupant> occupants)
    {
        _vehicleOccupants.Clear();
        foreach (VehicleOccupant occupant in occupants)
        {
            _vehicleOccupants[occupant.TaskId] = occupant;
        }
    }

    // The humans the original's list holds -- the player, a co-op session's other players, and each soldier that lives and
    // is in the world -- that stand inside and whose team the criterion takes.
    private int CountHumans(AreaActivateTask area, Func<int, bool> takesTeam)
    {
        int count = 0;
        AreaPlayer player = _playerSource?.Invoke()
            ?? new AreaPlayer(_statePlayer, _stateTeam, _stateExtraPlayer, _playerAlive);
        if (takesTeam(player.Team) && Holds(area, player))
        {
            count++;
        }

        // Every other player stands in an area as the player does: a co-op partner reaching the exit is a player there.
        foreach (AreaPlayer partner in Partners?.Invoke() ?? [])
        {
            if (takesTeam(partner.Team) && Holds(area, partner))
            {
                count++;
            }
        }

        IEnumerable<HumanOccupant> soldiers = _soldierSource?.Invoke() ?? _soldierOccupants.Values;
        foreach (HumanOccupant occupant in soldiers)
        {
            GameTask? soldier = _levelRoot.Tree.FromId(occupant.TaskId);
            if (soldier is not null
                && !occupant.IsDead
                && ConditionalContainerGate.IsTaskActive(soldier)
                && takesTeam(occupant.Team)
                && area.IsInside(occupant.Position))
            {
                count++;
            }
        }

        return count;
    }

    // CRITERIA_VEHICLE (0x4F4560) counts the whole vehicle list; a vehicle stays listed while its container is closed, so no
    // active test here.
    private int CountVehicles(AreaActivateTask area)
    {
        int count = 0;
        IEnumerable<VehicleOccupant> vehicles = _vehicleSource?.Invoke() ?? _vehicleOccupants.Values;
        foreach (VehicleOccupant occupant in vehicles)
        {
            if (_levelRoot.Tree.FromId(occupant.TaskId) is not null && area.IsInside(occupant.Position))
            {
                count++;
            }
        }

        return count;
    }

    private static bool Holds(AreaActivateTask area, AreaPlayer player) =>
        player.Alive
        && ((player.Position is { } point && area.IsInside(point))
            || (player.ExtraPosition is { } extra && area.IsInside(extra)));
}
