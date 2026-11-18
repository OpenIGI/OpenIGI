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
public sealed partial class Igi2ReplicatedWorld : IReplicatedWorld
{
    /// <summary>The entity id of the level's own player.</summary>
    public const int HostEntity = ReplicatedEntity.Host;

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
        _level.Marks = this;
        _level.Human.Spares = by => Spared(_level.Human, by);
        _level.Human.KeepsWeaponsWhenDown = HoldsTheDead;
        HearHarm(HostEntity, _level.Human);

        // Every explosion the level sets off is told to the clients, who show what their own players did not set off.
        _level.Explosions?.Started = request => _blasts.Note(EntityOf(request.Owner), request);

        // And everything every body fires, for the clients to fire again from their copies of it.
        HearLaunches(_level.Weapons?.Firing, () => IncludesHost ? HostEntity : -1);
        if (_level.Soldiers is { } soldiers)
        {
            for (int i = 0; i < soldiers.Bodies.Count; i++)
            {
                int entity = SoldierEntity(i);
                HearLaunches(soldiers.Bodies[i].Weapon.Firing, () => entity);
            }
        }

        _vehicles = new Igi2VehicleStates(_level);
        foreach ((int source, Weapons.Igi2WeaponFiring firing) in _vehicles.Weapons())
        {
            HearLaunches(firing, () => source);
        }
    }

    // A weapon's launches noted against its body's entity, as the tick has it then; none for a body no client is shown.
    private void HearLaunches(Weapons.Igi2WeaponFiring? firing, Func<int> entity)
    {
        firing?.Launched = note =>
        {
            if (entity() is >= 0 and var source)
            {
                _fired.Note(CurrentTick, source, note);
            }
        };
    }

    /// <inheritdoc/>
    public LagCompensation? LagCompensation { get; set; }

    /// <summary>Gets which team each client's player is on.</summary>
    public TeamRoster Teams { get; } = new();

    /// <summary>Gets which spawn point each player is put at.</summary>
    public Igi2SpawnSelector Spawns { get; } = new();

    /// <summary>Gets or sets a value indicating whether the level's own player is a player of the session (a listen server).</summary>
    /// <remarks>
    /// Without one -- a dedicated server -- the level's own player is nobody's, and is taken out of the level's world: the
    /// guards hunted it at the spawn and the areas counted it, though no one played it. The scripts weigh it until a client
    /// joins, so an empty server does not fail its mission.
    /// </remarks>
    public bool IncludesHost
    {
        get => _includesHost;
        set
        {
            _includesHost = value;
            if (!value)
            {
                _level.RetireLevelPlayer();
            }
        }
    }

    private bool _includesHost = true;

    /// <inheritdoc/>
    public int ChannelCount => Igi2Command.ChannelCount;

    /// <inheritdoc/>
    public long CurrentTick => _level.TickCount;

    /// <summary>Gets a client's networked player.</summary>
    /// <param name="slot">The client's slot.</param>
    /// <returns>The player, or <see langword="null"/>.</returns>
    public Igi2NetworkHuman? PlayerOf(int slot) => _bySlot.GetValueOrDefault(slot);

    /// <inheritdoc/>
    public int? AddPlayer(int slot, string name)
    {
        _names[slot] = name;
        return Place(slot, team: null);
    }

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
                // The killing blow's weapon and head, for the death's line in the feed.
                (string weapon, bool head) = _killingBlow.Remove(entity, out (string Weapon, bool Head) blow) ? blow : (string.Empty, false);
                deaths.Add(new WorldDeath(entity, EntityOf(human.Killer), weapon, Headshot: head));
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
        // The listen server's own player is the level's, which this world does not stand again yet.
        if (slot == ServerSession.HostSlot)
        {
            return;
        }

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

    // How high a body's chest is over its feet, and how far below its feet a spot's ground may lie: a metre, a metre and a half.
    private const double ChestHeight = 4096;
    private const double GroundReach = 6144;

    // Where a player joining the host's side stands: the first of eight spots about the host, two metres out, that the host can
    // see in a straight line at chest height and that has ground under it within a metre and a half, stood as high over it as
    // the host stands over its own; the host's own spot when none has. A joiner was once stood two metres along the world's
    // first axis whatever lay there -- inside a wall, over a drop -- and stuck or fell.
    private Vector3d BesideHost(Igi2HumanPlayer host, int slot)
    {
        Collision.Igi2CollisionWorld world = _level.CollisionWorld;
        Vector3d chest = host.Position + new Vector3d(0, 0, ChestHeight);
        double over = world.Cast(chest, host.Position - new Vector3d(0, 0, GroundReach), out OpenIGI.Game.World.WorldRayHit under)
            ? host.Position.Z - under.Position.Z
            : 0;
        for (int i = 0; i < 8; i++)
        {
            double angle = host.Gamma + ((slot + i) * Math.PI / 4);
            Vector3d spot = host.Position + new Vector3d(Math.Cos(angle) * SpawnSpacing, Math.Sin(angle) * SpawnSpacing, 0);
            Vector3d spotChest = spot + new Vector3d(0, 0, ChestHeight);
            if (world.Cast(chest, spotChest, out _))
            {
                continue;
            }

            if (world.Cast(spotChest, spot - new Vector3d(0, 0, GroundReach), out OpenIGI.Game.World.WorldRayHit ground))
            {
                return new Vector3d(spot.X, spot.Y, ground.Position.Z + over);
            }
        }

        return host.Position;
    }

    private int? Place(int slot, int? team)
    {
        Igi2HumanPlayer host = _level.Human;
        if (host.Model is not { } model)
        {
            return null;
        }

        Vector3d at = BesideHost(host, slot);
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

        string body = _level.PlayerBodyFor(side, _looks.GetValueOrDefault(slot)) ?? model.Name;
        if (_level.AddNetworkHuman(body, at, gamma, side) is not { } human)
        {
            return null;
        }

        Teams.Assign(slot, side);
        Seat(slot, human);
        return slot + 1;
    }

    // A client's player put in its slot, armed with the mission's loadout, heard as it fires, and spared its teammates' fire.
    private void Seat(int slot, Igi2NetworkHuman human)
    {
        _bySlot[slot] = human;
        Igi2HumanPlayer struck = human.Human;
        struck.Spares = by => Spared(struck, by);
        struck.KeepsWeaponsWhenDown = HoldsTheDead;
        HearHarm(slot + 1, struck);

        // A dedicated server's level player is weighed by the scripts only until a player has joined.
        if (!IncludesHost)
        {
            _level.ScriptPlayers?.Remove(_level.Human);
        }

        Arm(human);
        HearLaunches(human.Weapons?.Firing, () => EntityOf(human.Human));
    }

    // The look each client's player chose, by slot, a PlayerSkins id.
    private readonly Dictionary<int, string> _looks = [];

    /// <inheritdoc/>
    /// <remarks>
    /// A client's player is made again in the body of its look where it stands, as it joins: a body's bones are its model's, so a
    /// new look is a new body. The host's own look is the clients' to draw, each on its copy of the host.
    /// </remarks>
    public void SetSkin(int slot, string skin)
    {
        _looks[slot] = skin;
        if (!_bySlot.TryGetValue(slot, out Igi2NetworkHuman? worn)
            || _level.PlayerBodyFor(Teams.TeamOf(slot), skin) is not { } body
            || string.Equals(worn.Human.Model?.Name, body, StringComparison.OrdinalIgnoreCase)
            || _level.AddNetworkHuman(body, worn.Human.Position, worn.Human.Gamma, worn.Human.Team) is not { } dressed)
        {
            return;
        }

        _level.RemoveNetworkHuman(worn);
        Seat(slot, dressed);
    }

    /// <inheritdoc/>
    public void RemovePlayer(int slot)
    {
        Teams.Remove(slot);
        _names.Remove(slot);
        _looks.Remove(slot);

        // Its revival and its fall go with it, as a parked player's do: the next player in the slot inherited both.
        _squad.Forget(slot + 1);
        _dead.Remove(slot + 1);
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
        KeepStartKit();
        _scratch.Clear();
        if (IncludesHost)
        {
            _scratch.Add(WithRevive(OnLift(Replicate(HostEntity, _level.Human), _level.Human)));
        }

        List<ReplicatedSense> felt = [];
        foreach ((int slot, Igi2NetworkHuman human) in _bySlot)
        {
            _scratch.Add(WithRevive(OnLift(Replicate(slot + 1, human.Human), human.Human)));
            human.TakeFelt(slot + 1, felt);
        }

        // The soldiers, which only the server's minds run: every client poses its copies by these.
        CaptureSoldiers(_scratch);

        // By id, as the codec writes them and a snapshot is searched: the players stand in the order they last spawned.
        _scratch.Sort(static (a, b) => a.Id.CompareTo(b.Id));
        return new Snapshot(
            tick,
            [.. _scratch],
            Igi2DoorCommands.Capture(DoorTasks),
            Senses: felt.Count == 0 ? null : [.. felt],
            Lifts: Lifts(),
            Vehicles: _vehicles.Capture(),
            Fired: _fired.Capture(tick, _level.Weapons?.Inventory.Tables));
    }

    /// <summary>Puts every player but a shooter where the shooter's client drew them when it fired, until the scope ends.</summary>
    /// <param name="shooter">The client's player whose shots are about to be traced.</param>
    /// <returns>The scope, or <see langword="null"/> when there is nothing to rewind to.</returns>
    /// <remarks>
    /// The bodies are moved and turned (a shot at a man turning meets his side, not his back), not re-posed: the bones stand
    /// as this tick has them, which is what a round traced through the mesh reads (<see cref="Collision.IIgi2BonedBody.Orientation"/>).
    /// The turn is the change of yaw laid onto the body's own frame, so the slope it leans with stays.
    /// </remarks>
    private RewindScope? RewindFor(Igi2NetworkHuman shooter)
    {
        long commandTick = shooter.Commands.LastTakenTick;
        if (LagCompensation is not { } lag || commandTick == long.MinValue)
        {
            return null;
        }

        List<(Igi2Human Human, Vector3d Was, Matrix3f Turned)> moved = [];
        void MoveBack(int id, Igi2Human human)
        {
            // The living, where the shooter's client drew them, to the fraction of a tick it drew them at.
            double moment = lag.AimedAt(CurrentTick, shooter.Commands.LastViewTick, shooter.Commands.LastViewFraction);
            if (!ReferenceEquals(human, shooter.Human) && !human.IsDead && lag.History.Sample(moment, id, out ReplicatedHuman then))
            {
                moved.Add((human, human.Position, human.Orientation));
                human.Position = then.Position.ToVector();
                float turn = Quantize.AngleFromBits(then.Yaw) - human.Gamma;
                human.Orientation *= Matrix3f.RotationZ(turn);
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

        // The soldiers too: a partner's round meets the guard where its client drew him.
        if (_level.Soldiers is { } soldiers)
        {
            for (int i = 0; i < soldiers.Bodies.Count; i++)
            {
                MoveBack(SoldierEntity(i), soldiers.Bodies[i]);
            }
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

        // On the ground and climbing, as Project I.G.I.'s server says them: a client's correction lifting its falling player onto
        // the floor the server has it standing on stops the fall.
        if ((human.Signal & Igi2PlayerSignal.Grounded) != 0)
        {
            condition |= ReplicatedHumanCondition.OnGround;
        }

        if (human.AttachParent is not null)
        {
            condition |= ReplicatedHumanCondition.Climbing;
        }

        int shots = human.Weapon.Inventory?.ShotSequence ?? 0;

        return new ReplicatedHuman(
            id,
            Int3.From(human.Position),
            human.Speed,
            Quantize.AngleToBits(human.Gamma),
            Quantize.AngleToBits(human.View.Pitch),
            (ushort)Math.Clamp(human.Locomotion.CurrentState, 0, ushort.MaxValue),
            // The upper body as the original's remote human is sent it (eUpperBody), with the fire type above it.
            (ushort)((human.CurrentUpperBody & 0xF) | ((human.FireType & 0xF) << 4)),
            human.Weapon.Inventory?.HeldForSnapshot ?? 0,
            (byte)Math.Round(left * 255),
            (byte)Math.Clamp(human.Team, 0, 255),
            condition,
            unchecked((byte)shots),
            ActionSequence: human.UpperBodySequence,

            // Fallen, the death clip it plays: each game once drew its own, and the partner fell one way on its own screen and
            // another on the others'.
            ClipTime: human.IsDead ? (ushort)Math.Clamp(human.Animation.LastState, 0, ushort.MaxValue) : (ushort)0);
    }

    private sealed class RewindScope(List<(Igi2Human Human, Vector3d Was, Matrix3f Turned)> moved) : IDisposable
    {
        public void Dispose()
        {
            foreach ((Igi2Human human, Vector3d was, Matrix3f turned) in moved)
            {
                human.Position = was;
                human.Orientation = turned;
            }
        }
    }
}
