using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.World;

/// <summary>
/// The mission's <c>AreaActivate</c> volumes and the occupants they count: the player the host reports, the
/// soldiers and the vehicles.
/// </summary>
/// <remarks>
/// Owned by <see cref="MissionObjectiveRuntime"/>, which decides when the areas update -- from the level walk
/// once attached, otherwise in its own level-state phase.
/// </remarks>
internal sealed class MissionAreaActivation
{
    private readonly GameTask _levelRoot;
    private readonly List<AreaActivation> _areaActivations = [];
    private readonly Dictionary<int, HumanOccupant> _soldierOccupants = [];
    private readonly Dictionary<int, VehicleOccupant> _vehicleOccupants = [];
    private bool _playerAlive = true;
    private Vector3d? _statePlayer;
    private int _stateTeam;
    private Vector3d? _stateExtraPlayer;
    private Func<IEnumerable<VehicleOccupant>>? _vehicleSource;
    private Func<IEnumerable<HumanOccupant>>? _soldierSource;
    private Func<AreaPlayer>? _playerSource;

    /// <summary>Builds the area set over the loaded level.</summary>
    /// <param name="levelRoot">The loaded level's task tree, which resolves occupants to their tasks.</param>
    public MissionAreaActivation(GameTask levelRoot)
    {
        _levelRoot = levelRoot;
    }

    /// <summary>Adds one authored area and exports its <c>nActive</c> on its task.</summary>
    /// <param name="declared">The area's task.</param>
    /// <param name="area">The area read from it.</param>
    /// <param name="variables">The mission's script variables.</param>
    public void Declare(DeclaredTask declared, AreaActivation area, ScriptVarRegistry variables)
    {
        _areaActivations.Add(area);
        variables.BindInt(declared, "nActive", () => area.ActiveCount, value => area.ActiveCount = value);
    }

    /// <summary>Updates every area in declaration order, for a level that is not attached to a walk.</summary>
    public void UpdateAll()
    {
        foreach (AreaActivation area in _areaActivations)
        {
            UpdateArea(area);
        }
    }

    /// <summary>Puts each area into the level's update walk; see <see cref="MissionObjectiveRuntime.AttachTo"/>.</summary>
    /// <param name="walker">The level walk.</param>
    /// <param name="vehicles">Reads the vehicles' live positions when an area counts them.</param>
    /// <param name="soldiers">Reads the soldiers where they stand when an area counts them.</param>
    /// <param name="player">Reads the player where it stands when an area counts it.</param>
    public void AttachTo(
        LevelUpdateWalker walker,
        Func<IEnumerable<VehicleOccupant>>? vehicles,
        Func<IEnumerable<HumanOccupant>>? soldiers,
        Func<AreaPlayer>? player)
    {
        _vehicleSource = vehicles;
        _soldierSource = soldiers;
        _playerSource = player;
        foreach (AreaActivation area in _areaActivations)
        {
            walker.Bind(area.Task, task =>
            {
                UpdateArea(area);
                walker.RunChildren(task);
            });
        }
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

    /// <summary>Whether the named soldier's last reported state was dead.</summary>
    private bool IsSoldierDead(int taskId) =>
        _soldierOccupants.TryGetValue(taskId, out HumanOccupant occupant) && occupant.IsDead;

    /// <summary>AreaActivate::Update (<c>0x5048A0</c>): nActive cleared and recounted over every matching occupant.</summary>
    private void UpdateArea(AreaActivation area)
    {
        int count = 0;
        AreaPlayer player = _playerSource?.Invoke()
            ?? new AreaPlayer(_statePlayer, _stateTeam, _stateExtraPlayer, _playerAlive);
        if (player.Alive
            && area.AcceptsPlayer(player.Team)
            && ((player.Position is { } point && area.Contains(point))
                || (player.ExtraPosition is { } extra && area.Contains(extra))))
        {
            count++;
        }

        IEnumerable<HumanOccupant> soldiers = _soldierSource?.Invoke() ?? _soldierOccupants.Values;
        foreach (HumanOccupant occupant in soldiers)
        {
            GameTask? soldier = _levelRoot.Tree.FromId(occupant.TaskId);
            if (soldier is not null
                && !occupant.IsDead
                && ConditionalContainerGate.IsTaskActive(soldier)
                && area.AcceptsSoldier(occupant.Team)
                && area.Contains(occupant.Position))
            {
                count++;
            }
        }

        // CRITERIA_VEHICLE (0x4F4560) counts the whole vehicle list; a vehicle stays listed while its container is
        // closed, so no active test here.
        IEnumerable<VehicleOccupant> vehicles = _vehicleSource?.Invoke() ?? _vehicleOccupants.Values;
        foreach (VehicleOccupant occupant in vehicles)
        {
            GameTask? vehicle = _levelRoot.Tree.FromId(occupant.TaskId);
            if (vehicle is not null
                && area.AcceptsVehicle
                && area.Contains(occupant.Position))
            {
                count++;
            }
        }

        area.ActiveCount = count;
    }
}
