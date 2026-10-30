using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Net;

namespace OpenIGI.Game.Igi2.Multiplayer;

/// <summary>An I.G.I. 2 level as the server session sees it: a networked player per client, and every player in each snapshot.</summary>
/// <remarks>
/// <para>
/// The level's own player is the host's: on a listen server it is the host playing, and goes in every snapshot as entity
/// <see cref="HostEntity"/>; on a dedicated server <see cref="IncludesHost"/> is off and it stays out of them. Each client's
/// player is entity <c>slot + 1</c>.
/// </para>
/// <para>
/// On a multiplayer map a client is put on the fairer team (<see cref="Teams"/>) and stands at that team's spawn area's least
/// lately used point (<see cref="Igi2SpawnSelector"/>); on a map without spawn areas, a campaign mission played together, it
/// stands beside the level's own spawn, a pace apart per slot, on the level player's team.
/// </para>
/// </remarks>
public sealed class Igi2ReplicatedWorld : IReplicatedWorld
{
    /// <summary>The entity id of the level's own player.</summary>
    public const int HostEntity = 0;

    private const double SpawnSpacing = 8192;

    private readonly Igi2TitlePlayer _level;
    private readonly SortedDictionary<int, Igi2NetworkHuman> _bySlot = [];
    private readonly List<ReplicatedHuman> _scratch = [];
    private readonly HashSet<int> _dead = [];
    private IReadOnlyList<IRoundObjective>? _objectives;

    /// <summary>Initializes a new instance of the <see cref="Igi2ReplicatedWorld"/> class.</summary>
    /// <param name="level">The level, as its own player holds it.</param>
    public Igi2ReplicatedWorld(Igi2TitlePlayer level)
    {
        _level = level ?? throw new ArgumentNullException(nameof(level));
        _level.NetworkRewind = RewindFor;
    }

    /// <inheritdoc/>
    public LagCompensation? LagCompensation { get; set; }

    /// <summary>Gets which team each client's player is on.</summary>
    public TeamRoster Teams { get; } = new();

    /// <summary>Gets which spawn point each player is put at.</summary>
    public Igi2SpawnSelector Spawns { get; } = new();

    /// <summary>Gets or sets a value indicating whether the level's own player is a player of the session (a listen server).</summary>
    public bool IncludesHost { get; set; } = true;

    /// <inheritdoc/>
    public int ChannelCount => Igi2HumanChannels.Count;

    /// <inheritdoc/>
    public long CurrentTick => _level.TickCount;

    /// <summary>Gets a client's networked player.</summary>
    /// <param name="slot">The client's slot.</param>
    /// <returns>The player, or <see langword="null"/>.</returns>
    public Igi2NetworkHuman? PlayerOf(int slot) => _bySlot.GetValueOrDefault(slot);

    /// <inheritdoc/>
    public int? AddPlayer(int slot, string name) => Place(slot, team: null);

    /// <inheritdoc/>
    public int TeamOf(int slot) => Teams.TeamOf(slot);

    /// <inheritdoc/>
    public IReadOnlyList<IRoundObjective> RoundObjectives =>
        _objectives ??= _level.Level is { } run
            ? [.. _level.LevelTasks().OfType<Igi2NetworkObjectiveTask>().Select(task => new Igi2RoundObjective(task, run))]
            : [];

    /// <inheritdoc/>
    public int CurrentObjective { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<WorldDeath> TakeDeaths()
    {
        List<WorldDeath> deaths = [];
        void Check(int entity, Igi2HumanPlayer human)
        {
            if (!human.IsDead)
            {
                _dead.Remove(entity);
                return;
            }

            if (_dead.Add(entity))
            {
                deaths.Add(new WorldDeath(entity, EntityOf(human.Killer)));
            }
        }

        if (IncludesHost)
        {
            Check(HostEntity, _level.Human);
        }

        foreach ((int slot, Igi2NetworkHuman other) in _bySlot)
        {
            Check(slot + 1, other.Human);
        }

        return deaths;
    }

    /// <inheritdoc/>
    /// <remarks>The dead body is taken out and a new one made at a spawn point of the same team, as the original spawns afresh.</remarks>
    public void Respawn(int slot)
    {
        int team = Teams.TeamOf(slot);
        if (_bySlot.Remove(slot, out Igi2NetworkHuman? dead))
        {
            _level.RemoveNetworkHuman(dead);
        }

        _dead.Remove(slot + 1);
        Place(slot, team < 0 ? null : team);
    }

    private int EntityOf(object? killer)
    {
        if (killer is null)
        {
            return -1;
        }

        if (ReferenceEquals(killer, _level.Human))
        {
            return IncludesHost ? HostEntity : -1;
        }

        foreach ((int slot, Igi2NetworkHuman other) in _bySlot)
        {
            if (ReferenceEquals(killer, other.Human))
            {
                return slot + 1;
            }
        }

        return -1;
    }

    private int? Place(int slot, int? team)
    {
        Igi2HumanPlayer host = _level.Human;
        if (host.Model is not { } model)
        {
            return null;
        }

        Vector3d at = host.Position + new Vector3d(SpawnSpacing * (slot + 1), 0, 0);
        float gamma = host.Gamma;
        int side = team ?? host.Team;
        Igi2SpawnAreaTask[] areas = [.. _level.LevelTasks().OfType<Igi2SpawnAreaTask>()];
        if (areas.Length > 0)
        {
            side = team ?? Teams.FairTeam();

            // The current objective's own spawn areas first, as NetMission_SetObjectiveComplete makes them the active ones.
            Igi2SpawnAreaTask? area = _level.LevelTasks().OfType<Igi2NetworkObjectiveTask>()
                .FirstOrDefault(o => o.Order == CurrentObjective)?.Children.OfType<Igi2SpawnAreaTask>()
                .FirstOrDefault(a => a.Team == side && a.Points().Any());
            area ??= Array.Find(areas, a => a.Team == side && a.Points().Any());
            if (area is not null && Spawns.Pick(area) is { } point)
            {
                at = point.Position;
                gamma = point.Gamma;
            }
        }

        // On a multiplayer map each team wears its own bodies (NetManager_atGoodModel / atBadModel); elsewhere the level's.
        string body = (areas.Length > 0 && _level.SpawnKit is { } kit ? Igi2Gameplay.TeamModel(side, kit.Models) : null) ?? model.Name;
        if (_level.AddNetworkHuman(body, at, gamma, side) is not { } human)
        {
            return null;
        }

        Teams.Assign(slot, side);
        _bySlot[slot] = human;
        return slot + 1;
    }

    /// <inheritdoc/>
    public void RemovePlayer(int slot)
    {
        Teams.Remove(slot);
        if (_bySlot.Remove(slot, out Igi2NetworkHuman? human))
        {
            _level.RemoveNetworkHuman(human);
        }
    }

    /// <inheritdoc/>
    public UserCommandBuffer? CommandsFor(int slot) => _bySlot.GetValueOrDefault(slot)?.Commands;

    /// <inheritdoc/>
    public Snapshot Capture(long tick)
    {
        _scratch.Clear();
        if (IncludesHost)
        {
            _scratch.Add(Replicate(HostEntity, _level.Human));
        }

        foreach ((int slot, Igi2NetworkHuman human) in _bySlot)
        {
            _scratch.Add(Replicate(slot + 1, human.Human));
        }

        return new Snapshot(tick, [.. _scratch]);
    }

    /// <summary>Puts every player but a shooter where the shooter's client drew them when it fired, until the scope ends.</summary>
    /// <param name="shooter">The client's player whose shots are about to be traced.</param>
    /// <returns>The scope, or <see langword="null"/> when there is nothing to rewind to.</returns>
    /// <remarks>The bodies are moved, not turned or re-posed: where a body stands is what a shot at a moving man misses by.</remarks>
    private RewindScope? RewindFor(Igi2NetworkHuman shooter)
    {
        long commandTick = shooter.Commands.LastTakenTick;
        if (LagCompensation is not { } lag || commandTick == long.MinValue)
        {
            return null;
        }

        List<(Igi2HumanPlayer Human, Vector3d Was)> moved = [];
        void MoveBack(int id, Igi2HumanPlayer human)
        {
            if (!ReferenceEquals(human, shooter.Human) && lag.TargetAt(commandTick, id, out ReplicatedHuman then))
            {
                moved.Add((human, human.Position));
                human.Position = then.Position.ToVector();
            }
        }

        if (IncludesHost)
        {
            MoveBack(HostEntity, _level.Human);
        }

        foreach ((int slot, Igi2NetworkHuman other) in _bySlot)
        {
            MoveBack(slot + 1, other.Human);
        }

        return moved.Count == 0 ? null : new RewindScope(moved);
    }

    /// <summary>One I.G.I. 2 human as a snapshot carries it.</summary>
    /// <param name="id">Its entity id.</param>
    /// <param name="human">The human.</param>
    /// <returns>Its replicated state.</returns>
    public static ReplicatedHuman Replicate(int id, Igi2HumanPlayer human)
    {
        ArgumentNullException.ThrowIfNull(human);
        float max = human.Damage.MaxDamage;
        float left = max > 0 ? Math.Clamp(1f - (human.Damage.Damage / max), 0f, 1f) : 1f;
        ReplicatedHumanCondition condition = ReplicatedHumanCondition.None;
        if (!human.IsDead)
        {
            condition |= ReplicatedHumanCondition.Alive;
        }

        if (human.IsKeepingLow)
        {
            condition |= ReplicatedHumanCondition.Low;
        }

        return new ReplicatedHuman(
            id,
            Int3.From(human.Position),
            human.Speed,
            Quantize.AngleToBits(human.Gamma),
            Quantize.AngleToBits(human.View.Pitch),
            (ushort)Math.Clamp(human.Locomotion.CurrentState, 0, ushort.MaxValue),
            (ushort)Math.Clamp(human.ActionState, 0, ushort.MaxValue),
            (ushort)Math.Clamp(human.Weapon.Inventory?.Current?.WeaponType.Id ?? 0, 0, ushort.MaxValue),
            (byte)Math.Round(left * 255),
            (byte)Math.Clamp(human.Team, 0, 255),
            condition);
    }

    private sealed class RewindScope(List<(Igi2HumanPlayer Human, Vector3d Was)> moved) : IDisposable
    {
        public void Dispose()
        {
            foreach ((Igi2HumanPlayer human, Vector3d was) in moved)
            {
                human.Position = was;
            }
        }
    }
}
