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
                    _game.Impacts.Replay(new ReplicatedImpact(false, partner.Entity, Int3.From(landed.Wall.Position), landed.Direction), partner);
                }
            }
        };
        _game.World.PartnerBodies = () => [.. _bySlot.Values.Where(static p => !p.IsDead).Select(static p => Ai.PartnerBody.Of(p, p.Player, p.LadderClipTime) with { Name = p.Name })];
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
            humans.Add(Replicate(HostEntity, host, _game.World.PlayerHealth, host.IsOnLadder ? _game.World.Soldiers?.PlayerLadderClipTime : null));
        }

        foreach ((int slot, Igi1NetworkPlayer other) in _bySlot)
        {
            humans.Add(Replicate(slot + 1, other.Player, other.Health, other.LadderClipTime));
        }

        // The soldiers, which only the server's minds run: every client poses its copies by these.
        if (_game.World.Soldiers is { } soldiers)
        {
            foreach (Player.SoldierView soldier in soldiers.SoldierViews.OrderBy(static v => v.Soldier.Id))
            {
                humans.Add(new ReplicatedHuman(
                    SoldierEntityBase + soldier.Soldier.Id,
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
                    unchecked((byte)(soldiers.AgentOf(soldier)?.ShotSequence ?? 0))));
            }
        }

        Snapshot snapshot = new(tick, [.. humans], Doors(_game.World.Doors), _impacts.Count == 0 ? null : [.. _impacts]);
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
            player.Health.Reset();
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
