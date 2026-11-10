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
public sealed partial class Igi1ReplicatedWorld : IReplicatedWorld
{
    /// <summary>The entity id of the mission's own player.</summary>
    public const int HostEntity = 0;

    /// <summary>How finely a clip's time is carried: sixteenths of a tick, so a clip of up to 136 seconds fits the 16 bits.</summary>
    public const float ClipTimeScale = 16f;

    /// <summary>Gets a clip's time as a snapshot carries it, in sixteenths of a tick.</summary>
    /// <param name="time">The clip's own time, <see cref="Formats.Animation.BoneAnimation.FrameStep"/> a tick.</param>
    /// <returns>The time carried.</returns>
    /// <remarks>The clip's own units ran 160 a tick: carried as sixteenths of those, any clip past 0.85 s stood still.</remarks>
    public static ushort PackClipTime(float time) =>
        (ushort)Math.Clamp(Math.Round(time / Formats.Animation.BoneAnimation.FrameStep * ClipTimeScale), 0, ushort.MaxValue);

    /// <summary>Gets a clip's own time back from what a snapshot carries.</summary>
    /// <param name="carried">The time carried, in sixteenths of a tick.</param>
    /// <returns>The clip's own time.</returns>
    public static float UnpackClipTime(ushort carried) => carried / ClipTimeScale * Formats.Animation.BoneAnimation.FrameStep;

    /// <summary>The entity id a soldier's task id is carried past in snapshots.</summary>
    public const int SoldierEntityBase = 100000;

    /// <summary>The team every player of a co-op session is on, the original player's.</summary>
    public const int PlayerTeam = 0;

    private const double SpawnSpacing = 4096;

    // How far from the level's start the host must have gone for one joining to be stood beside the host instead: 30 m.
    private const double LateJoinDistance = 30 * 4096;

    /// <summary>How long a teammate stands by a fallen player to bring it back, in ticks: five seconds.</summary>
    public static int ReviveTicks => CoopCVars.ReviveSeconds.Value * 30;

    /// <summary>How near a teammate stands to a fallen player to revive it, in world units: two metres.</summary>
    public const double ReviveReachUnits = 2 * 4096;

    // How often, while the whole squad stands, where each stands is kept as the checkpoint: fifteen seconds of ticks.
    private const int CheckpointTicks = 15 * 30;

    // Where each player stood when the squad last stood together, by entity, and the tick it was kept on.
    private readonly Dictionary<int, (Vector3d At, float Yaw)> _checkpoint = [];
    private long? _checkpointTick;

    // Each fallen player's revival so far, in ticks, by entity.
    private readonly Dictionary<int, int> _reviving = [];

    // The players whose clients dropped, kept aside out of the level by their parked keys.
    private readonly Dictionary<int, Igi1NetworkPlayer> _parked = [];

    // The player whose round last hurt each player, by entity, and the tick it did: the death that follows is its kill.
    private readonly Dictionary<int, (ShotTally By, long Tick)> _lastHurtBy = [];

    // How long after a player's round hurt another its death is still that player's kill, in ticks (Source credits the last
    // attacker the same way).
    private const long KillCreditTicks = 5 * 30;

    // Who took each pickup, by entity: a client shows it gone, and takes what its own player took into its own hands.
    private readonly Dictionary<object, int> _takenBy = [];


    private readonly Igi1Gameplay _game;
    private readonly SortedDictionary<int, Igi1NetworkPlayer> _bySlot = [];
    private readonly HashSet<int> _dead = [];

    // The rounds that landed since the last snapshot, for every client to show.
    private readonly List<ReplicatedImpact> _impacts = [];

    // Where each soldier stood before it was put back for a partner's command, to stand there again after it.
    private readonly List<(Player.SoldierView View, Vector3d Position, Matrix3f Orientation)> _rewound = [];

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
        if (_game.World.Player is { } own)
        {
            Record(own, HostEntity);

            // A listen host walks into its players as they walk into it.
            SessionHumans.Meet(_game.World, own, () => IncludesHost);
        }

        // With friendly fire on, a player's round meets the other players as it meets the guards.
        _game.World.Bodies.Add(new CoopPlayerBodies(_game, this));
        _game.Harm.RoundTaken = NoteHurt;

        // Every partner, the fallen too: a body lies where it fell until a teammate brings it back.
        _game.World.PartnerBodies = () =>
            [.. _bySlot.Select(pair => (Slot: pair.Key, p: pair.Value)).Select(e => Ai.PartnerBody.Of(e.p, e.p.Player, e.p.ClipTime) with
            {
                Fired = e.p.FiredThisTick,
                Name = Game.Multiplayer.Modes.PlayerSides.ShowsName(VersusMode, TeamOf(ServerSession.HostSlot), TeamOf(e.Slot)) ? e.p.Name : null,
                Dead = e.p.IsDead,
                Revive = e.p.IsDead && Game.Multiplayer.Modes.PlayerSides.Revivable(VersusMode) ? ReviveProgressOf(e.p.Entity) : null,
                Model = PlayerSkins.Of(e.p.Skin).Model,
                CarriesBomb = e.Slot == _bombCarrier,
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

        Igi1NetworkPlayer player = new(made.Player, made.Health) { Name = name, Entity = slot + 1, BeforeCommand = RewindSoldiers, AfterCommand = RestoreSoldiers };
        Record(made.Player, slot + 1);
        SessionHumans.Meet(_game.World, made.Player, () => IncludesHost);
        Stand(player, slot);
        _bySlot[slot] = player;

        // One joining a match under way is armed as a first spawn is, not with the campaign level's kit: now without sides,
        // or once its side is known (SetTeam).
        if (_versus == Versus.Everyone || (_versus == Versus.Teams && _teams.ContainsKey(slot)))
        {
            Restock(made.Player, slot, fresh: true);
        }
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
    public bool ParkPlayer(int slot, int parked)
    {
        if (!_bySlot.Remove(slot, out Igi1NetworkPlayer? player))
        {
            return false;
        }

        _game.World.NetworkPlayers.Remove(player);
        _reviving.Remove(player.Entity);
        _dead.Remove(player.Entity);
        _parked[parked] = player;

        // Its side goes aside with it: left under the slot, it was the side of whoever next joined on that slot, and the
        // player coming back on another had none.
        if (_teams.Remove(slot, out int side))
        {
            _parkedTeams[parked] = side;
        }

        return true;
    }

    /// <inheritdoc/>
    public int? UnparkPlayer(int parked, int slot)
    {
        if (!_parked.Remove(parked, out Igi1NetworkPlayer? player))
        {
            return null;
        }

        player.Entity = slot + 1;
        _bySlot[slot] = player;
        if (_parkedTeams.Remove(parked, out int side))
        {
            _teams[slot] = side;
        }

        _game.World.NetworkPlayers.Add(player);
        return player.Entity;
    }

    /// <inheritdoc/>
    public void ForgetPlayer(int parked)
    {
        _parked.Remove(parked);
        _parkedTeams.Remove(parked);
    }

    // The sides of the players kept aside, by the key each is kept under.
    private readonly Dictionary<int, int> _parkedTeams = [];

    /// <inheritdoc/>
    public void RemovePlayer(int slot)
    {
        if (_bySlot.Remove(slot, out Igi1NetworkPlayer? player))
        {
            _game.World.NetworkPlayers.Remove(player);
        }

        _teams.Remove(slot);
    }

    /// <inheritdoc/>
    public UserCommandBuffer? CommandsFor(int slot) => _bySlot.GetValueOrDefault(slot)?.Commands;

    /// <inheritdoc/>
    public int TeamOf(int slot) =>
        _teams.TryGetValue(slot, out int team) ? team : _bySlot.ContainsKey(slot) || slot == ServerSession.HostSlot ? PlayerTeam : -1;

    /// <inheritdoc/>
    public Snapshot Capture(long tick)
    {
        // A match keeps the level's scenes held every tick, not only once it began: a level up after the match was made
        // (or put back between rounds) has its scenes, and their voices, start otherwise.
        if (IsVersus && _game.World.Cutscenes is { Held: false } scenes)
        {
            scenes.Held = true;
        }

        List<ReplicatedHuman> humans = [];
        if (IncludesHost && _game.World.Player is { } host)
        {
            humans.Add(Carrying(OnLift(WithRevive(Replicate(
                HostEntity, host, _game.World.PlayerHealth, host.ModeClipTimeShown ?? (host.BodyClip is null ? null : _game.World.Soldiers?.PlayerLadderClipTime), _game.World.IsPlayerDead)), host), ServerSession.HostSlot));
        }

        foreach ((int slot, Igi1NetworkPlayer other) in _bySlot)
        {
            humans.Add(Carrying(OnLift(WithRevive(Replicate(slot + 1, other.Player, other.Health, other.ClipTime, other.IsDead)), other.Player), slot));
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
            tick,
            [.. humans],
            Doors(_game.World.Doors),
            _impacts.Count == 0 ? null : [.. _impacts],
            senses.Count == 0 ? null : [.. senses],
            Lifts(_game.World.Elevators),
            _game.World.SecurityCameras?.Replicate(),
            _game.Blasts.CaptureWrecks(),
            CaptureProjectiles(),
            _game.World.Interactions?.CaptureState());
        _impacts.Clear();
        return snapshot;
    }

    private readonly List<OpenIGI.Game.Weapons.ShownProjectile> _shownProjectiles = [];

    // What every player's weapons have in the world, each under its player's entity: the mission's own player's pool holds
    // the soldiers' lobs as well, which go out as the host's.
    private ReplicatedProjectile[]? CaptureProjectiles()
    {
        List<ReplicatedProjectile> sent = [];
        void Add(int entity, PlayerCamera rig)
        {
            _shownProjectiles.Clear();
            OpenIGI.Game.Weapons.ShownProjectile.Collect(rig.Grenades, rig.Missiles, rig.M203, _shownProjectiles);
            foreach (OpenIGI.Game.Weapons.ShownProjectile shown in _shownProjectiles)
            {
                if (sent.Count < ReplicatedProjectile.MaxPerSnapshot)
                {
                    sent.Add(ReplicatedProjectile.Of(entity, shown));
                }
            }
        }

        if (_game.World.Player is { } own)
        {
            Add(HostEntity, own);
        }

        foreach ((int slot, Igi1NetworkPlayer other) in _bySlot)
        {
            Add(slot + 1, other.Player);
        }

        return sent.Count == 0 ? null : [.. sent];
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
                deaths.Add(new WorldDeath(entity, KillerOf(entity)));
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

    // A round from a player's gun hurt a player: noted against the one it hurt, if a player fired it, and whether in the head.
    private void NoteHurt(IHarmedPlayer hurt, ShotTally? by, bool head)
    {
        if (EntityOf(hurt.Rig) is not { } entity)
        {
            return;
        }

        int? attacker = by is not null ? EntityOf(by) : null;
        if (by is not null && attacker != entity)
        {
            _lastHurtBy[entity] = (by, CurrentTick);
        }

        NoteCombat(hurt, entity, attacker == entity ? null : attacker, head);
    }

    // The player who killed one that just died: the last whose round hurt it, lately enough; -1 for the level (a guard, a fall).
    private int KillerOf(int entity)
    {
        if (!_lastHurtBy.Remove(entity, out (ShotTally By, long Tick) last) || CurrentTick - last.Tick > KillCreditTicks)
        {
            return -1;
        }

        return EntityOf(last.By) ?? -1;
    }

    // The entity of a player of the session by its rig, or by the tally its rounds count on.
    private int? EntityOf(Player.PlayerCamera? rig)
    {
        if (rig is null)
        {
            return null;
        }

        if (IncludesHost && ReferenceEquals(rig, _game.World.Player))
        {
            return HostEntity;
        }

        foreach (Igi1NetworkPlayer other in _bySlot.Values)
        {
            if (ReferenceEquals(other.Player, rig))
            {
                return other.Entity;
            }
        }

        return null;
    }

    private int? EntityOf(ShotTally tally)
    {
        if (IncludesHost && _game.World.Player is { } own && ReferenceEquals(own.Shots, tally))
        {
            return HostEntity;
        }

        foreach (Igi1NetworkPlayer other in _bySlot.Values)
        {
            if (ReferenceEquals(other.Player.Shots, tally))
            {
                return other.Entity;
            }
        }

        return null;
    }

    /// <inheritdoc/>
    /// <remarks>A fallen co-op partner comes back at full health beside the level's spawn.</remarks>
    public void Respawn(int slot)
    {
        if (slot == ServerSession.HostSlot)
        {
            bool hostDied = _dead.Contains(HostEntity);
            if (hostDied && IsVersus && _game.World.Player is { } fallen)
            {
                Igi1PlayerSetup.Disarm(fallen);
            }

            _game.Harm.RevivePlayer(at: null, facing: 0f);

            // Put back whole: a revive mends only the dead, and one who lived through the last round kept its wounds.
            _game.World.Player?.Body.Health.Reset();
            if (_game.World.Player is { } own && _game.PlayerSetup?.LevelSpawn() is { } spawn)
            {
                if (IsVersus)
                {
                    PlaceVersus(own, slot, spawn);
                }
                else
                {
                    own.PlaceAt(_game.World.Ground, spawn);
                }

                Restock(own, slot, hostDied);
            }

            _dead.Remove(HostEntity);
            return;
        }

        if (_bySlot.TryGetValue(slot, out Igi1NetworkPlayer? player))
        {
            bool died = _dead.Contains(slot + 1);
            if (died && IsVersus)
            {
                Igi1PlayerSetup.Disarm(player.Player);
            }

            Igi1PlayerHarm.RevivePlayer(player, at: null, player.Player.Body.Yaw);
            player.Player.Body.Health.Reset();
            _dead.Remove(slot + 1);
            Stand(player, slot);
            Restock(player.Player, slot, died);
        }
    }

    /// <summary>One I.G.I. player as a snapshot carries it; its clips one past their ids, nought for none.</summary>
    /// <param name="id">Its entity id.</param>
    /// <param name="player">The rig.</param>
    /// <param name="health">Its health, or <see langword="null"/>.</param>
    /// <returns>Its replicated state.</returns>
    /// <param name="ladderTime">Where its climbing body is in its ladder clip, or <see langword="null"/>.</param>
    /// <param name="down">Whether the death handler put it down whatever its health: a kill for leaving the match's bounds, or
    /// at a team change, runs the death without spending the health, and a player sent as alive then never fell on its own
    /// game -- it played on there, its switches and moves running on a body the server held dead.</param>
    public static ReplicatedHuman Replicate(int id, PlayerCamera player, HumanHealth? health, float? ladderTime = null, bool down = false)
    {
        ArgumentNullException.ThrowIfNull(player);
        bool dead = down || health?.IsDead == true;
        ReplicatedHumanCondition condition = dead ? ReplicatedHumanCondition.None : ReplicatedHumanCondition.Alive;

        // A ladder's clip, or a world interaction's -- a computer, a wire -- played on the same climbing body: its clip held
        // at its time, where the clip has the body, facing as the clip faces it.
        PlayerBodyClip? clip = ladderTime is null ? null : player.BodyClip;
        if (clip is not null)
        {
            condition |= ReplicatedHumanCondition.Climbing;
        }

        if (player.Weapons?.FiredThisTick == true)
        {
            condition |= ReplicatedHumanCondition.Firing;
        }

        // A shot's or a reload's torso clip is played once: the torso holds it until the gun is ready again, a pistol's for
        // two thirds of a second, so a window that looped it showed the shot five times over.
        if (!player.UpperBodyAnimationLoops)
        {
            condition |= ReplicatedHumanCondition.UpperBodyOnce;
        }

        // Standing: a client its own correction puts back on this floor stops any fall it made alone.
        if (clip is null && player.Body.IsGrounded)
        {
            condition |= ReplicatedHumanCondition.OnGround;
        }

        return new ReplicatedHuman(
            id,
            Int3.From(clip?.Feet ?? player.Body.Position),
            default,

            // In a clip the body faces as the clip does (its gamma, the yaw negated) and plays it.
            Quantize.AngleToBits(clip is { } facing ? -facing.Facing : player.Body.Yaw),
            Quantize.AngleToBits(player.Body.Pitch),
            (ushort)Math.Clamp((clip?.Animation ?? player.LocomotionAnimation) + 1, 0, ushort.MaxValue),
            (ushort)Math.Clamp(player.UpperBodyAnimation + 1, 0, ushort.MaxValue),
            (ushort)Math.Clamp((player.Weapons?.InHand?.Weapon.WeaponId ?? -1) + 1, 0, ushort.MaxValue),
            (byte)Math.Round(Math.Clamp(health?.RemainingFraction ?? 1f, 0f, 1f) * 255),
            PlayerTeam,
            condition,
            unchecked((byte)(player.Weapons?.ShotSequence ?? 0)),
            unchecked((byte)player.UpperBodySequence),
            clip is null ? (ushort)0 : PackClipTime(ladderTime ?? 0f));
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

    /// <summary>The level's lifts as a snapshot carries them, in the level's own order.</summary>
    /// <param name="lifts">The level's lifts.</param>
    /// <returns>The lifts, or <see langword="null"/> for a level without any.</returns>
    public static ReplicatedLift[]? Lifts(OpenIGI.Game.World.ElevatorRegistry? lifts)
    {
        if (lifts is not { Elevators.Count: > 0 } registry)
        {
            return null;
        }

        ReplicatedLift[] all = new ReplicatedLift[Math.Min(registry.Elevators.Count, ReplicatedLift.MaxPerSnapshot)];
        for (int i = 0; i < all.Length; i++)
        {
            OpenIGI.Game.World.ElevatorMotion motion = registry.Elevators[i].State.Motion;
            all[i] = new ReplicatedLift(Int3.From(motion.Position), motion.Floor, motion.Target ?? -1, (float)motion.Speed);
        }

        return all;
    }

    /// <inheritdoc/>
    /// <remarks>Co-op: a fallen player lies where it fell until a teammate stands by it for <see cref="ReviveTicks"/>.</remarks>
    public bool HoldsTheDead => _versus == Versus.None;

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
        WritePickups(_level);
        return _level.AsSpan().ToArray();
    }

    // Who takes what: each player's pickups, as it walks over them.
    private void Record(PlayerCamera player, int entity)
    {
        player.CanDropWeapons = true;
        if (IsVersus)
        {
            UseVersusBody(player);
        }

        player.GunPickedUp += pickup => _takenBy[pickup] = entity;
        player.GenericPickedUp += pickup => _takenBy[pickup] = entity;
    }

    // The pickups: each gun by who took it (none, an entity, or the world), the dropped ones where they lie; each item likewise.
    private void WritePickups(BitWriter writer)
    {
        IReadOnlyList<OpenIGI.Game.World.GunPickup> guns = _game.World.GunPickups?.Items ?? [];
        writer.WriteVarUInt((uint)guns.Count);
        foreach (OpenIGI.Game.World.GunPickup gun in guns)
        {
            writer.WriteBits(TakenBy(gun, gun.IsCollected), 8);
            writer.WriteBool(gun.Task is null);
            if (gun.Task is null)
            {
                writer.WriteVarUInt((uint)gun.Weapon.WeaponId);
                writer.WriteVarUInt(gun.Rounds is { } rounds ? (uint)Math.Max(0, rounds) + 1 : 0u);
                writer.WriteBool(gun.Launch is not null);
                if (gun.Launch is { } launch)
                {
                    WritePlace(writer, launch.Position, launch.Orientation);
                    writer.WriteSingle((float)launch.Velocity.X);
                    writer.WriteSingle((float)launch.Velocity.Y);
                    writer.WriteSingle((float)launch.Velocity.Z);
                }

                // A throw in the air is left to each machine's flight, so the state does not change every tick it flies.
                bool lies = gun.Launch is null || !gun.IsMoving;
                writer.WriteBool(lies);
                if (lies)
                {
                    WritePlace(writer, gun.Position, gun.Orientation);
                }
            }
        }

        IReadOnlyList<OpenIGI.Game.World.GenericPickup> items = _game.World.GenericPickups?.Items ?? [];
        writer.WriteVarUInt((uint)items.Count);
        foreach (OpenIGI.Game.World.GenericPickup item in items)
        {
            writer.WriteBits(TakenBy(item, item.IsCollected), 8);
        }
    }

    // A pickup's place: where, to the unit, and how it is turned.
    private static void WritePlace(BitWriter writer, Vector3d position, Matrix3f o)
    {
        Int3 at = Int3.From(position);
        writer.WriteVarInt(at.X);
        writer.WriteVarInt(at.Y);
        writer.WriteVarInt(at.Z);
        foreach (float f in (ReadOnlySpan<float>)[o.M11, o.M12, o.M13, o.M21, o.M22, o.M23, o.M31, o.M32, o.M33])
        {
            writer.WriteSingle(f);
        }
    }

    // Nought for one lying there, the taker's entity plus one, or 255 for one gone with nobody's hand on it.
    private byte TakenBy(object pickup, bool collected) =>
        !collected ? (byte)0 : _takenBy.TryGetValue(pickup, out int entity) && entity is >= 0 and < 254 ? (byte)(entity + 1) : (byte)255;

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

        // The whole squad standing: where each stands is the checkpoint, kept every so often.
        if (down.Count == 0 && up.Count > 0 && (_checkpointTick is not { } kept || CurrentTick - kept >= CheckpointTicks))
        {
            KeepCheckpoint();
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

    /// <inheritdoc/>
    public bool CanRetryFromCheckpoint =>
        _checkpoint.Count > 0
        && (!IncludesHost || _game.World.IsPlayerDead)
        && _bySlot.Values.All(static p => p.IsDead);

    /// <inheritdoc/>
    public void RetryFromCheckpoint()
    {
        if (IncludesHost && _game.World.Player is { } own)
        {
            (Vector3d at, float yaw) = _checkpoint.GetValueOrDefault(HostEntity, (own.Body.Position, own.Body.Yaw));
            _game.Harm.RevivePlayer(at, yaw);
            _dead.Remove(HostEntity);
        }

        foreach (Igi1NetworkPlayer partner in _bySlot.Values)
        {
            (Vector3d at, float yaw) = _checkpoint.GetValueOrDefault(partner.Entity, (partner.Player.Body.Position, partner.Player.Body.Yaw));
            Igi1PlayerHarm.RevivePlayer(partner, at, yaw);
            _dead.Remove(partner.Entity);
        }

        _reviving.Clear();
    }

    private void KeepCheckpoint()
    {
        _checkpoint.Clear();
        if (IncludesHost && _game.World.Player is { } own)
        {
            _checkpoint[HostEntity] = (own.Body.Position, own.Body.Yaw);
        }

        foreach (Igi1NetworkPlayer partner in _bySlot.Values)
        {
            _checkpoint[partner.Entity] = (partner.Player.Body.Position, partner.Player.Body.Yaw);
        }

        _checkpointTick = CurrentTick;
    }

    // The lift a player rides, by the rule its car carries it by: a client draws it on that car, wherever its own copy of
    // the car stands.
    private ReplicatedHuman OnLift(ReplicatedHuman human, PlayerCamera player)
    {
        if (_game.World.Elevators is not { Elevators.Count: > 0 } lifts || player.IsOnLadder)
        {
            return human;
        }

        for (int i = 0; i < lifts.Elevators.Count && i < ReplicatedLift.MaxPerSnapshot; i++)
        {
            if (OpenIGI.Game.World.ElevatorRegistry.IsPassenger(lifts.Elevators[i], player.Body.Position, _game.World.Solids))
            {
                return human with { Platform = (byte)(i + 1) };
            }
        }

        return human;
    }

    // A fallen player's health byte carries its revival instead, for every client's prompt and panel to show.
    private ReplicatedHuman WithRevive(ReplicatedHuman human) =>
        (human.Flags & ReplicatedHumanCondition.Alive) != 0
            ? human
            : human with { Health = (byte)Math.Round((ReviveProgressOf(human.Id) ?? 0f) * 255f) };

    // The soldiers made tougher for a bigger squad, every one of them, keeping what has been taken from each in proportion.
    private void ScaleEnemies()
    {
        int players = _bySlot.Count + (IncludesHost ? 1 : 0);
        if (_game.World.Soldiers is { } soldiers)
        {
            soldiers.HealthScale = 1f + (CoopCVars.EnemyHealthPerPlayer.Value / 100f * Math.Max(0, players - 1));
        }
    }

    // Lag compensation for a partner's command: every living soldier, and in a versus game every other living player, put where
    // the partner's client drew it when the command was made (the server's own snapshots sampled at the moment the client was
    // drawing, to the fraction of a tick, as the client itself samples them), so the rounds the command fires meet the one the
    // partner aimed at. The history reaches back no further than LagCompensation.MaxUnlag; one it never held, or a command with
    // no view tick, stands where it is.
    private void RewindSoldiers(Igi1NetworkPlayer shooter, long commandTick, long viewTick, float fraction)
    {
        if (LagCompensation is not { } lag || viewTick == long.MinValue)
        {
            return;
        }

        double moment = lag.AimedAt(commandTick, viewTick, fraction);
        if (_game.World.Soldiers is { } soldiers)
        {
            foreach (Player.SoldierView soldier in soldiers.SoldierViews)
            {
                if (soldier.IsDead || !lag.History.Sample(moment, SoldierEntityBase + soldiers.RuntimeIdOf(soldier), out ReplicatedHuman then))
                {
                    continue;
                }

                _rewound.Add((soldier, soldier.Position, soldier.Orientation));
                soldier.PlaceAt(then.Position.ToVector(), Matrix3f.RotationZ(Quantize.AngleFromBits(then.Yaw)));
            }

            soldiers.Rewound = _rewound.Count > 0;
        }

        if (IsVersus)
        {
            RewindPlayers(lag, shooter, moment);
        }
    }

    // The soldiers and players put back where this tick has them.
    private void RestoreSoldiers()
    {
        foreach ((Player.SoldierView soldier, Vector3d position, Matrix3f orientation) in _rewound)
        {
            soldier.PlaceAt(position, orientation);
        }

        _rewound.Clear();
        if (_game.World.Soldiers is { } soldiers)
        {
            soldiers.Rewound = false;
        }

        RestorePlayers();
    }

    // Where a player is stood: beside the level's start, or -- joining a level the host has gone on into -- where the host is.
    private void Stand(Igi1NetworkPlayer player, int slot)
    {
        if (_game.PlayerSetup?.LevelSpawn() is not { } spawn)
        {
            return;
        }

        if (IsVersus)
        {
            PlaceVersus(player.Player, slot, spawn);
            return;
        }

        if (!IsVersus
            && IncludesHost
            && _game.World.Player is { } host
            && !_game.World.IsPlayerDead
            && Vector3d.Distance(host.Body.Position, spawn.Position) > LateJoinDistance)
        {
            player.Player.PlaceAt(_game.World.Ground, spawn with { Position = host.Body.Position, Gamma = -host.Body.Yaw });
            return;
        }

        PlayerSpawnPoint beside = spawn with { Position = spawn.Position + new Vector3d(SpawnSpacing * (slot + 1), 0, 0) };
        player.Player.PlaceAt(_game.World.Ground, beside);
    }
}
