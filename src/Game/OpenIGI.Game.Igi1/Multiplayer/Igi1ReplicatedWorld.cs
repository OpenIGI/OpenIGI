using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Player;
using OpenIGI.Net;

namespace OpenIGI.Game.Igi1.Multiplayer;

/// <summary>An I.G.I. mission played together: every client's player beside the mission's own, all on one side against the soldiers.</summary>
/// <remarks>
/// <para>
/// The original has no multiplayer, so co-op is the campaign with company. Each client's player is made as the mission's
/// own is (<see cref="Igi1PlayerSetup.BeginExtra()"/>) and stands a pace from the level's spawn per slot. Everyone is on the
/// player's team. The mission's own player is entity <see cref="HostEntity"/> on a listen server; each client's, its slot
/// plus one.
/// </para>
/// </remarks>
public sealed class Igi1ReplicatedWorld : IReplicatedWorld
{
    /// <summary>The entity id of the mission's own player.</summary>
    public const int HostEntity = 0;

    /// <summary>How finely a clip's time is carried: sixteenths.</summary>
    public const float ClipTimeScale = 16f;

    /// <summary>The entity id a soldier's task id is carried past in snapshots.</summary>
    public const int SoldierEntityBase = 100000;

    /// <summary>The team every player of a co-op session is on, the original player's.</summary>
    public const int PlayerTeam = 0;

    private const double SpawnSpacing = 4096;

    /// <summary>How long a teammate stands by a fallen player to bring it back, in ticks: five seconds.</summary>
    public const int ReviveTicks = 5 * 30;

    /// <summary>How near a teammate stands to a fallen player to revive it, in world units: two metres.</summary>
    public const double ReviveReachUnits = 2 * 4096;

    /// <summary>How much tougher the soldiers are for each player past the first: half as much health again.</summary>
    public const float EnemyHealthPerExtraPlayer = 0.5f;

    // Each fallen player's revival so far, in ticks, by entity.
    private readonly Dictionary<int, int> _reviving = [];

    // The scale the soldiers' health was last set to.
    private float _enemyScale = 1f;


    private readonly Igi1Gameplay _game;
    private readonly SortedDictionary<int, Igi1NetworkPlayer> _bySlot = [];
    private readonly HashSet<int> _dead = [];

    // The rounds that landed since the last snapshot, for every client to show.
    private readonly List<ReplicatedImpact> _impacts = [];

    /// <summary>Initializes a new instance of the <see cref="Igi1ReplicatedWorld"/> class.</summary>
    /// <param name="game">The mission.</param>
    public Igi1ReplicatedWorld(Igi1Gameplay game)
    {
        _game = game ?? throw new ArgumentNullException(nameof(game));
        _game.Impacts.Recorded = impact => _impacts.Add(impact);

        // A partner's rounds land on the server's world too: their marks and dust shown here, and so sent to everyone.
        _game.World.PartnerRan = partner =>
        {
            foreach (Weapons.GunImpact landed in partner.Player.Impacts)
            {
                if (landed.HitSomething && !landed.StruckHuman && !landed.Melee)
                {
                    _game.Impacts.Replay(new ReplicatedImpact(ImpactKind.Surface, partner.Entity, Int3.From(landed.Wall.Position), landed.Direction), partner);
                }
            }
        };
        // Every partner, the fallen too: a body lies where it fell until a teammate brings it back.
        _game.World.PartnerBodies = () =>
            [.. _bySlot.Values.Select(p => Ai.PartnerBody.Of(p, p.Player, p.LadderClipTime) with
            {
                Fired = p.FiredThisTick,
                Name = p.Name,
                Dead = p.IsDead,
                Revive = p.IsDead ? ReviveProgressOf(p.Entity) : null,
                Model = PlayerSkins.Of(p.Skin).Model,
            })];
    }

    /// <summary>Gets or sets a value indicating whether the mission's own player is a player of the session (a listen server).</summary>
    public bool IncludesHost { get; set; } = true;

    /// <inheritdoc/>
    public int ChannelCount => Igi1Command.ChannelCount;

    /// <inheritdoc/>
    public long CurrentTick => _game.World.PlayerTicks;

    /// <inheritdoc/>
    public LagCompensation? LagCompensation { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<IRoundObjective> RoundObjectives => [];

    /// <inheritdoc/>
    public int CurrentObjective { get; set; }

    /// <summary>Gets a client's player.</summary>
    /// <param name="slot">The client's slot.</param>
    /// <returns>The player, or <see langword="null"/>.</returns>
    public Igi1NetworkPlayer? PlayerOf(int slot) => _bySlot.GetValueOrDefault(slot);

    /// <inheritdoc/>
    public int? AddPlayer(int slot, string name)
    {
        if (_game.PlayerSetup?.BeginExtra() is not { } made)
        {
            return null;
        }

        Igi1NetworkPlayer player = new(made.Player, made.Health) { Name = name, Entity = slot + 1 };
        Stand(player, slot);
        _bySlot[slot] = player;
        _game.World.NetworkPlayers.Add(player);
        return slot + 1;
    }

    /// <inheritdoc/>
    /// <remarks>The host's own look is the clients' to draw; the server only draws its partners.</remarks>
    public void SetSkin(int slot, string skin)
    {
        if (_bySlot.TryGetValue(slot, out Igi1NetworkPlayer? player))
        {
            player.Skin = skin;
        }
    }

    /// <inheritdoc/>
    public void RemovePlayer(int slot)
    {
        if (_bySlot.Remove(slot, out Igi1NetworkPlayer? player))
        {
            _game.World.NetworkPlayers.Remove(player);
        }
    }

    /// <inheritdoc/>
    public UserCommandBuffer? CommandsFor(int slot) => _bySlot.GetValueOrDefault(slot)?.Commands;

    /// <inheritdoc/>
    public int TeamOf(int slot) => _bySlot.ContainsKey(slot) ? PlayerTeam : -1;

    /// <inheritdoc/>
    public Snapshot Capture(long tick)
    {
        List<ReplicatedHuman> humans = [];
        if (IncludesHost && _game.World.Player is { } host)
        {
            humans.Add(WithRevive(Replicate(HostEntity, host, _game.World.PlayerHealth, host.IsOnLadder ? _game.World.Soldiers?.PlayerLadderClipTime : null)));
        }

        foreach ((int slot, Igi1NetworkPlayer other) in _bySlot)
        {
            humans.Add(WithRevive(Replicate(slot + 1, other.Player, other.Health, other.LadderClipTime)));
        }

        // The soldiers, which only the server's minds run: every client poses its copies by these.
        if (_game.World.Soldiers is { } soldiers)
        {
            // Each by its runtime id, unique to the live soldier and the same on every machine; a generator's by its generator
            // too, for a client to make the replacement the server made.
            foreach (Player.SoldierView soldier in soldiers.SoldierViews.OrderBy(soldiers.RuntimeIdOf))
            {
                humans.Add(new ReplicatedHuman(
                    SoldierEntityBase + soldiers.RuntimeIdOf(soldier),
                    Int3.From(soldier.Position),
                    default,
                    Quantize.AngleToBits(Ai.SoldierPuppet.FacingOf(soldier.Orientation)),
                    Quantize.AngleToBits(soldier.LookPitch),
                    (ushort)Math.Clamp(soldier.CurrentAnimation + 1, 0, ushort.MaxValue),
                    (ushort)Math.Clamp(soldier.UpperBodyAnimation + 1, 0, ushort.MaxValue),
                    0,
                    255,
                    (byte)Math.Clamp(soldier.Soldier.Team, 0, 255),
                    soldier.IsDead ? ReplicatedHumanCondition.None : ReplicatedHumanCondition.Alive,
                    unchecked((byte)(soldiers.AgentOf(soldier)?.ShotSequence ?? 0)),
                    Source: (ushort)((soldiers.GeneratorIdOf(soldier) ?? -1) + 1)));
            }
        }

        List<ReplicatedSense> senses = [];
        foreach (Igi1NetworkPlayer partner in _bySlot.Values)
        {
            partner.RemoteSenses.TakeInto(senses);
        }

        Snapshot snapshot = new(
            tick, [.. humans], Doors(_game.World.Doors), _impacts.Count == 0 ? null : [.. _impacts], senses.Count == 0 ? null : [.. senses]);
        _impacts.Clear();
        return snapshot;
    }

    /// <inheritdoc/>
    public IReadOnlyList<WorldDeath> TakeDeaths()
    {
        List<WorldDeath> deaths = [];
        void Check(int entity, bool isDead)
        {
            if (!isDead)
            {
                _dead.Remove(entity);
            }
            else if (_dead.Add(entity))
            {
                deaths.Add(new WorldDeath(entity, -1));
            }
        }

        if (IncludesHost)
        {
            Check(HostEntity, _game.World.IsPlayerDead);
        }

        foreach ((int slot, Igi1NetworkPlayer other) in _bySlot)
        {
            Check(slot + 1, other.IsDead);
        }

        return deaths;
    }

    /// <inheritdoc/>
    /// <remarks>A fallen co-op partner comes back at full health beside the level's spawn.</remarks>
    public void Respawn(int slot)
    {
        if (slot == ServerSession.HostSlot)
        {
            _game.Harm.RevivePlayer(at: null, facing: 0f);
            if (_game.World.Player is { } own && _game.PlayerSetup?.LevelSpawn() is { } spawn)
            {
                own.PlaceAt(_game.World.Ground, spawn);
            }

            _dead.Remove(HostEntity);
            return;
        }

        if (_bySlot.TryGetValue(slot, out Igi1NetworkPlayer? player))
        {
            Igi1PlayerHarm.RevivePlayer(player, at: null, player.Player.Body.Yaw);
            _dead.Remove(slot + 1);
            Stand(player, slot);
        }
    }

    /// <summary>One I.G.I. player as a snapshot carries it; its clips one past their ids, nought for none.</summary>
    /// <param name="id">Its entity id.</param>
    /// <param name="player">The rig.</param>
    /// <param name="health">Its health, or <see langword="null"/>.</param>
    /// <returns>Its replicated state.</returns>
    /// <param name="ladderTime">Where its climbing body is in its ladder clip, or <see langword="null"/>.</param>
    public static ReplicatedHuman Replicate(int id, PlayerCamera player, HumanHealth? health, float? ladderTime = null)
    {
        ArgumentNullException.ThrowIfNull(player);
        bool dead = health?.IsDead == true;
        ReplicatedHumanCondition condition = dead ? ReplicatedHumanCondition.None : ReplicatedHumanCondition.Alive;
        if (player.LadderAnimation >= 0 && ladderTime is not null)
        {
            condition |= ReplicatedHumanCondition.Climbing;
        }

        if (player.Weapons?.FiredThisTick == true)
        {
            condition |= ReplicatedHumanCondition.Firing;
        }

        return new ReplicatedHuman(
            id,
            Int3.From(player.LadderAnimation >= 0 ? player.LadderFeet : player.Body.Position),
            default,

            // On a ladder the body faces the ladder (its gamma, the yaw negated) and plays the climb's clip.
            Quantize.AngleToBits(player.LadderAnimation >= 0 ? -player.LadderFacing : player.Body.Yaw),
            Quantize.AngleToBits(player.Body.Pitch),
            (ushort)Math.Clamp((player.LadderAnimation >= 0 ? player.LadderAnimation : player.LocomotionAnimation) + 1, 0, ushort.MaxValue),
            (ushort)Math.Clamp(player.UpperBodyAnimation + 1, 0, ushort.MaxValue),
            (ushort)Math.Clamp((player.Weapons?.InHand?.Weapon.WeaponId ?? -1) + 1, 0, ushort.MaxValue),
            (byte)Math.Round(Math.Clamp(health?.RemainingFraction ?? 1f, 0f, 1f) * 255),
            PlayerTeam,
            condition,
            unchecked((byte)(player.Weapons?.ShotSequence ?? 0)),
            unchecked((byte)player.UpperBodySequence),
            (ushort)Math.Clamp(Math.Round((ladderTime ?? 0f) * ClipTimeScale), 0, ushort.MaxValue));
    }

    /// <summary>The level's doors as a snapshot carries them: one bit each, set where the door is commanded open.</summary>
    /// <param name="doors">The level's doors.</param>
    /// <returns>The bits, or <see langword="null"/> for a level without doors.</returns>
    public static byte[]? Doors(OpenIGI.Game.World.DoorRegistry? doors)
    {
        if (doors is not { Doors.Count: > 0 } registry)
        {
            return null;
        }

        byte[] bits = new byte[(registry.Doors.Count + 7) / 8];
        for (int i = 0; i < registry.Doors.Count; i++)
        {
            if (registry.Doors[i].State.IsCommandedOpen)
            {
                bits[i >> 3] |= (byte)(1 << (i & 7));
            }
        }

        return bits;
    }

    /// <inheritdoc/>
    /// <remarks>Co-op: a fallen player lies where it fell until a teammate stands by it for <see cref="ReviveTicks"/>.</remarks>
    public bool HoldsTheDead => true;

    // The level's running state as a client takes it: the objectives' counters, messages and result, and the scenes.
    private readonly BitWriter _level = new(1024);

    /// <inheritdoc/>
    public byte[]? CaptureLevel()
    {
        if (_game.World.Objectives is not { } objectives)
        {
            return null;
        }

        _level.Reset();
        objectives.WriteState(_level);
        _level.WriteBool(_game.World.Cutscenes is not null);
        _game.World.Cutscenes?.WriteRuns(_level);
        return _level.AsSpan().ToArray();
    }

    /// <inheritdoc/>
    public float? ReviveProgressOf(int entity)
    {
        bool down = entity == HostEntity ? IncludesHost && _game.World.IsPlayerDead : _bySlot.TryGetValue(entity - 1, out Igi1NetworkPlayer? p) && p.IsDead;
        return down ? Math.Min(1f, _reviving.GetValueOrDefault(entity) / (float)ReviveTicks) : null;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Each fallen player within <see cref="ReviveReachUnits"/> of a living one counts a tick towards its revival; one left
    /// alone starts again from nothing. At <see cref="ReviveTicks"/> it stands up where it fell, at full health. The
    /// soldiers' toughness is kept to the squad's size here too, once a tick.
    /// </remarks>
    public IReadOnlyList<int> TakeRevivals()
    {
        ScaleEnemies();

        List<(int Entity, int Slot, Vector3d At)> down = [];
        List<Vector3d> up = [];
        if (IncludesHost && _game.World.Player is { } host)
        {
            if (_game.World.IsPlayerDead)
            {
                down.Add((HostEntity, ServerSession.HostSlot, host.Body.Position));
            }
            else
            {
                up.Add(host.Body.Position);
            }
        }

        foreach ((int slot, Igi1NetworkPlayer other) in _bySlot)
        {
            if (other.IsDead)
            {
                down.Add((slot + 1, slot, other.Player.Body.Position));
            }
            else
            {
                up.Add(other.Player.Body.Position);
            }
        }

        List<int> revived = [];
        foreach ((int entity, int slot, Vector3d at) in down)
        {
            bool tended = up.Exists(p => Vector3d.Distance(p, at) <= ReviveReachUnits);
            int ticks = tended ? _reviving.GetValueOrDefault(entity) + 1 : 0;
            if (ticks < ReviveTicks)
            {
                _reviving[entity] = ticks;
                continue;
            }

            _reviving.Remove(entity);
            _dead.Remove(entity);
            if (entity == HostEntity && _game.World.Player is { } own)
            {
                _game.Harm.RevivePlayer(at, own.Body.Yaw);
            }
            else if (_bySlot.TryGetValue(slot, out Igi1NetworkPlayer? partner))
            {
                Igi1PlayerHarm.RevivePlayer(partner, at, partner.Player.Body.Yaw);
            }

            revived.Add(slot);
        }

        foreach (int entity in _reviving.Keys.Where(e => !down.Exists(d => d.Entity == e)).ToArray())
        {
            _reviving.Remove(entity);
        }

        return revived;
    }

    // A fallen player's health byte carries its revival instead, for every client's prompt and panel to show.
    private ReplicatedHuman WithRevive(ReplicatedHuman human) =>
        (human.Flags & ReplicatedHumanCondition.Alive) != 0
            ? human
            : human with { Health = (byte)Math.Round((ReviveProgressOf(human.Id) ?? 0f) * 255f) };

    // The soldiers made tougher for a bigger squad, keeping what has been taken from each in proportion.
    private void ScaleEnemies()
    {
        int players = _bySlot.Count + (IncludesHost ? 1 : 0);
        float scale = 1f + (EnemyHealthPerExtraPlayer * Math.Max(0, players - 1));
        if (scale == _enemyScale || _game.World.Soldiers is not { } soldiers)
        {
            return;
        }

        foreach (Player.SoldierView soldier in soldiers.SoldierViews)
        {
            if (!soldier.IsDead)
            {
                OpenIGI.Game.Player.HumanHealth health = soldiers.HealthOf(soldier);
                health.RescaleMaximumHealth(health.BaseMaximumHealth * scale);
            }
        }

        _enemyScale = scale;
    }

    private void Stand(Igi1NetworkPlayer player, int slot)
    {
        if (_game.PlayerSetup?.LevelSpawn() is not { } spawn)
        {
            return;
        }

        PlayerSpawnPoint beside = spawn with { Position = spawn.Position + new Vector3d(SpawnSpacing * (slot + 1), 0, 0) };
        player.Player.PlaceAt(_game.World.Ground, beside);
    }
}
