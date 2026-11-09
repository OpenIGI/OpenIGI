using System.Globalization;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Multiplayer.Modes;

/// <summary>How search and destroy's bomb is played: how long it takes to plant and to defuse, its fuse and its blast.</summary>
public sealed record BombSettings
{
    /// <summary>Gets the seconds an attacker holds the use key at a site to plant.</summary>
    public double PlantSeconds { get; init; } = 3;

    /// <summary>Gets the seconds a defender holds the use key beside the bomb to defuse it.</summary>
    public double DefuseSeconds { get; init; } = 7;

    /// <summary>Gets the seconds from the plant to the blast.</summary>
    public double FuseSeconds { get; init; } = 40;

    /// <summary>Gets how far the blast kills, in metres.</summary>
    public double BlastMetres { get; init; } = 40;

    /// <summary>Gets how near the bomb a defender must stand, in metres.</summary>
    public double DefuseMetres { get; init; } = 2.5;

    /// <summary>Gets how near a dropped bomb an attacker must walk to take it up, in metres.</summary>
    public double PickUpMetres { get; init; } = 1.5;

    /// <summary>Gets how far ahead of its carrier a bomb the carrier drops is put down, in metres.</summary>
    public double DropAheadMetres { get; init; } = 0.8;
}

/// <summary>
/// Search and destroy: rounds of one life; the attackers win a round by planting the bomb at one of the map's sites and
/// seeing it go off, or by leaving no defender standing; the defenders by defusing it, by leaving no attacker standing before
/// it is planted, or by holding out until the round's clock runs out.
/// </summary>
/// <remarks>
/// One attacker carries the bomb, handed round the team a round at a time, and only the carrier plants; a carrier who falls
/// or leaves drops it where it last stood, and the first attacker to walk over it takes it up. Planting and defusing are
/// holding the use key, standing still or not, for <see cref="BombSettings.PlantSeconds"/> and
/// <see cref="BombSettings.DefuseSeconds"/> -- a hold let go starts again.
/// Once the bomb is down the round's clock no longer decides it: the fuse does, and the attackers falling no longer ends it
/// (the defenders must still defuse), as in the tactical shooters. A map with no sites plays as elimination with the clock
/// going to the defenders.
/// </remarks>
/// <param name="tickRate">The world's ticks per second.</param>
/// <param name="settings">How the rounds are played, or the defaults.</param>
/// <param name="bomb">How the bomb is played, or the defaults.</param>
public sealed class SearchAndDestroyMode(int tickRate, RoundSettings? settings = null, BombSettings? bomb = null) : RoundMode(tickRate, settings)
{
    // How long each player has held the use key at what it is working on, in ticks.
    private readonly Dictionary<int, int> _holding = [];

    // Where the carrier last stood alive, which the bomb drops at when it falls or leaves.
    private Vector3d? _carrierLast;

    // The attacker that dropped the bomb, which does not take it up again until it has stepped away; -1 for none.
    private int _droppedBy = -1;

    // The slot the bomb was last handed to at a round's start, which the next round hands it on from.
    private int _handedTo = -1;

    // The planted bomb's blast wave: how it reaches each place, built through the fuse; and, once it has gone off, the tick
    // it went off on and the players it has still to reach.
    private BlastWaveField? _wave;
    private long _waveFrom = -1;
    private readonly List<int> _waveYetToReach = [];
    private readonly List<(int Id, Vector3d At)> _glassYetToReach = [];

    /// <summary>How far past the killing radius the wave still shatters glass, as a multiple of it: overpressure breaks windows long after it stops killing.</summary>
    public const double GlassReachShare = 2.5;

    // What the planted bomb would take from each living player where they stand, for their health bars.
    private readonly List<ModeRisk> _risks = [];

    /// <summary>How fast the blast's front sweeps out, in metres a second: the speed of sound.</summary>
    public const double WaveMetresPerSecond = 343.0;

    // How long the field may take building each tick: a sliver of the frame, wherever the server runs.
    private static readonly TimeSpan WaveBudget = TimeSpan.FromMilliseconds(2.0);

    /// <inheritdoc/>
    public override string Id => GameModes.SearchAndDestroy;

    /// <inheritdoc/>
    protected override bool SidesDiffer => true;

    /// <inheritdoc/>
    protected override bool HasArmoury => true;

    /// <summary>Gets how the bomb is played.</summary>
    public BombSettings Bomb { get; } = bomb ?? new BombSettings();

    /// <summary>Gets the team that attacks the sites.</summary>
    public static int Attackers => TeamRoster.Good;

    /// <summary>Gets the team that defends them.</summary>
    public static int Defenders => TeamRoster.Bad;

    /// <summary>Gets where the bomb was planted this round, or <see langword="null"/> while it is not.</summary>
    public Vector3d? PlantedAt { get; private set; }

    /// <summary>Gets the way the bomb lies turned, in radians: its planter's facing, or its last carrier's while dropped.</summary>
    public float BombYaw { get; private set; }

    /// <summary>Gets the site the bomb was planted at, or <see langword="null"/>.</summary>
    public string? PlantedSite { get; private set; }

    /// <summary>Gets the tick the planted bomb goes off on.</summary>
    public long BlowsAt { get; private set; }

    /// <summary>Gets the slot of the attacker carrying the bomb, or -1 while nobody does (dropped, planted, or no attackers).</summary>
    public int Carrier { get; private set; } = -1;

    /// <summary>Gets where the dropped bomb lies, in world units, or <see langword="null"/> while it is carried or planted.</summary>
    public Vector3d? DroppedAt { get; private set; }

    /// <summary>Gets whether the dropped bomb is a thrown one the world flies and draws, rather than one laid where it fell.</summary>
    public bool BombFlies { get; private set; }

    /// <summary>Gets a value indicating whether this round's bomb was defused.</summary>
    public bool Defused { get; private set; }

    /// <summary>Gets a value indicating whether this round's bomb went off.</summary>
    public bool Exploded { get; private set; }

    /// <inheritdoc/>
    public override string Status(long tick)
    {
        if (Exploded)
        {
            return "THE BOMB WENT OFF";
        }

        if (Defused)
        {
            return "BOMB DEFUSED";
        }

        if (PlantedAt is null)
        {
            return string.Empty;
        }

        int seconds = (int)Math.Ceiling(Math.Max(0, BlowsAt - tick) / (double)TickRate);
        return string.Create(CultureInfo.InvariantCulture, $"BOMB PLANTED AT {PlantedSite}  ·  {seconds / 60}:{seconds % 60:00}");
    }

    /// <inheritdoc/>
    public override ModeReport Report(long tick) => base.Report(tick) with
    {
        Attackers = Attackers,
        Planted = PlantedAt is not null && !Exploded && !Defused ? PlantedSite ?? string.Empty : string.Empty,
        FuseLeft = PlantedAt is not null ? Math.Max(0, BlowsAt - tick) / (double)TickRate : 0,
        BombAt = PlantedAt is not null && !Exploded && !Defused ? PlantedAt : null,
        BombYaw = BombYaw,
        Carrier = Carrier,
        DroppedAt = DroppedAt,
        BombFlies = DroppedAt is not null && BombFlies,
        FuseSeconds = Bomb.FuseSeconds,
        Risks = [.. _risks],
        Working = [.. _holding.Select(h => new ModeWork(
            h.Key, ProgressOf(h.Key) ?? 0f, PlantedAt is not null, (float)(PlantedAt is null ? Bomb.PlantSeconds : Bomb.DefuseSeconds)))],
    };

    /// <summary>Gets how far a player is with its plant or defuse, in <c>[0,1]</c>, or <see langword="null"/> when it is at neither.</summary>
    /// <param name="slot">The player's slot.</param>
    /// <returns>Its progress.</returns>
    public float? ProgressOf(int slot) =>
        _holding.TryGetValue(slot, out int held) && held > 0
            ? (float)Math.Clamp(held / (Ticks(PlantedAt is null ? Bomb.PlantSeconds : Bomb.DefuseSeconds)), 0, 1)
            : null;

    /// <inheritdoc/>
    protected override void OnRoundBegun(IModeHost host, long tick)
    {
        host.Match.DefendingTeam = Defenders;
        _holding.Clear();
        ShowWork(host);
        PlantedAt = null;
        PlantedSite = null;
        Defused = false;
        Exploded = false;
        _wave = null;
        _waveFrom = -1;
        _waveYetToReach.Clear();
        _glassYetToReach.Clear();
        _risks.Clear();
        DroppedAt = null;
        _droppedBy = -1;
        if (BombFlies)
        {
            host.World.TakeThrownBomb();
            BombFlies = false;
        }

        Carrier = NextCarrier(host);
        _handedTo = Carrier >= 0 ? Carrier : _handedTo;
        _carrierLast = Carrier >= 0 ? host.World.PositionOf(Carrier) : null;
    }

    /// <inheritdoc/>
    protected override void TickRound(IModeHost host, long tick)
    {
        ArgumentNullException.ThrowIfNull(host);
        TickBomb(host, tick);
        TickWave(host, tick);
        ShowWork(host);
        TallyRisks(host);
    }

    // Each living player's risk, while a bomb is planted and has not gone off.
    private void TallyRisks(IModeHost host)
    {
        _risks.Clear();
        if (PlantedAt is null || Exploded || Defused)
        {
            return;
        }

        foreach ((int slot, MatchPlayer player) in host.Match.Players)
        {
            if (player.IsAlive)
            {
                _risks.Add(new ModeRisk(slot, RiskOf(host, slot)));
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The bomb is in its carrier's hands from the round's first tick, as in CS: it can be seen, dropped for a team mate and
    /// picked up through the strategy time. A carrier who leaves or changes sides before the round runs hands it straight to
    /// the next attacker rather than leaving it on the floor at the spawn.
    /// </remarks>
    protected override void TickFrozen(IModeHost host, long tick)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (Carrier >= 0 && !(host.Match.Players.TryGetValue(Carrier, out MatchPlayer? carrier) && carrier.IsAlive && carrier.Team == Attackers))
        {
            Carrier = -1;
            _carrierLast = null;
        }

        Carry(host);
        host.World.ShowCarrier(Carrier);
    }

    /// <inheritdoc/>
    protected override void TickRoundOver(IModeHost host, long tick)
    {
        ArgumentNullException.ThrowIfNull(host);
        TickWave(host, tick);
    }

    /// <summary>How much of a player's health the planted bomb would take where it stands, were it to go off now.</summary>
    /// <param name="host">The match.</param>
    /// <param name="slot">The player.</param>
    /// <returns>The share of a whole health, or nought while no bomb is planted.</returns>
    public float RiskOf(IModeHost host, int slot)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (PlantedAt is not { } bomb || Exploded || Defused || host.World.PositionOf(slot) is not { } feet)
        {
            return 0f;
        }

        return BombDamage.Share(EffectiveMetres(host, bomb, feet), Bomb.BlastMetres);
    }

    // How far the wave effectively travels to a player: by the field once it is built; before that, the straight line, a
    // wall between counted as the field would count it.
    private double EffectiveMetres(IModeHost host, Vector3d bomb, Vector3d feet)
    {
        Vector3d chest = feet + new Vector3d(0.0, 0.0, 1.2 * WorldUnits.PerMetre);
        if (_wave is { IsReady: true } wave)
        {
            return wave.EffectiveMetres(chest);
        }

        Vector3d air = bomb + new Vector3d(0.0, 0.0, 0.4 * WorldUnits.PerMetre);
        double straight = (chest - air).Length / WorldUnits.PerMetre;
        return host.World.Collision is { } cast && cast(air, chest, out _)
            ? (straight + BlastWaveField.WallPenaltyMetres) * (1.0 + BlastWaveField.DetourWeight)
            : straight;
    }

    // How far the wave effectively travels to a pane: its own centre, which the pane's surface may not hide from itself.
    private double PaneMetres(IModeHost host, Vector3d bomb, Vector3d pane)
    {
        if (_wave is { IsReady: true } wave)
        {
            return wave.EffectiveMetres(pane);
        }

        Vector3d air = bomb + new Vector3d(0.0, 0.0, 0.4 * WorldUnits.PerMetre);
        double straight = (pane - air).Length / WorldUnits.PerMetre;
        return host.World.Collision is { } cast && cast(air, pane, out WorldRayHit hit)
            && (hit.Position - pane).Length > 0.35 * WorldUnits.PerMetre && !GameMaterial.IsBlastTransparent(hit.Material)
            ? (straight + BlastWaveField.WallPenaltyMetres) * (1.0 + BlastWaveField.DetourWeight)
            : straight;
    }

    // The front sweeping out from the bomb: a player is hurt as it reaches them, by how far it travelled to get there.
    private void TickWave(IModeHost host, long tick)
    {
        if (_wave is { IsReady: false } building && PlantedAt is not null && !Exploded && !Defused)
        {
            building.Advance(WaveBudget);
        }

        if (_waveFrom < 0 || (_waveYetToReach.Count == 0 && _glassYetToReach.Count == 0) || PlantedAt is not { } bomb)
        {
            return;
        }

        double front = (WaveMetresPerSecond * (tick - _waveFrom) / TickRate) + (_wave?.CellMetres ?? BlastWaveField.FinestCellMetres);

        // Every pane the wave reaches, near enough, shatters as the front arrives: overpressure, not a hit.
        double glassReach = Bomb.BlastMetres * GlassReachShare;
        for (int i = _glassYetToReach.Count - 1; i >= 0; i--)
        {
            (int id, Vector3d pane) = _glassYetToReach[i];
            double effective = PaneMetres(host, bomb, pane);
            if (effective > glassReach)
            {
                _glassYetToReach.RemoveAt(i);
            }
            else if (effective <= front)
            {
                _glassYetToReach.RemoveAt(i);
                host.World.ShatterGlass(id, bomb);
            }
        }
        for (int i = _waveYetToReach.Count - 1; i >= 0; i--)
        {
            int slot = _waveYetToReach[i];
            if (!host.Match.Players.TryGetValue(slot, out MatchPlayer? player) || !player.IsAlive || host.World.PositionOf(slot) is not { } feet)
            {
                _waveYetToReach.RemoveAt(i);
                continue;
            }

            double effective = EffectiveMetres(host, bomb, feet);
            if (effective > front)
            {
                continue;
            }

            _waveYetToReach.RemoveAt(i);
            float share = BombDamage.Share(effective, Bomb.BlastMetres);
            if (share > 0f)
            {
                host.World.BlastPlayer(slot, share, bomb);
            }
        }

        // Past the farthest the bell reaches, nobody else is touched, and past the glass's reach no pane.
        if (front > Bomb.BlastMetres * 3.0 * (1.0 + BlastWaveField.DetourWeight))
        {
            _waveYetToReach.Clear();
        }

        if (front > glassReach)
        {
            _glassYetToReach.Clear();
        }
    }

    // Each player's body at its work -- kneeling over the plant, or at the defuse -- or back at its own moves.
    private void ShowWork(IModeHost host)
    {
        int total = (int)Ticks(PlantedAt is null ? Bomb.PlantSeconds : Bomb.DefuseSeconds);
        ModeWorkKind kind = PlantedAt is null ? ModeWorkKind.Plant : ModeWorkKind.Defuse;
        host.World.ShowCarrier(PlantedAt is null && !Exploded && !Defused ? Carrier : -1);
        foreach (int slot in host.Match.Players.Keys)
        {
            if (_holding.TryGetValue(slot, out int held) && held > 0)
            {
                host.World.ShowWork(slot, kind, held, total);
            }
            else
            {
                host.World.ShowWork(slot, null, 0, total);
            }
        }
    }

    private void TickBomb(IModeHost host, long tick)
    {
        if (Exploded || Defused)
        {
            return;
        }

        if (PlantedAt is { } bomb)
        {
            if (tick >= BlowsAt)
            {
                Blow(host, bomb, tick);
                return;
            }

            Work(host, Defenders, at => Vector3d.Distance(at, bomb) <= Bomb.DefuseMetres * WorldUnits.PerMetre, Bomb.DefuseSeconds, (_, _) =>
            {
                Defused = true;
                _holding.Clear();
            });
            return;
        }

        Carry(host);
        MpPoint[] sites = [.. (host.Layout?.Points ?? []).Where(p => p.Kind == MpPointKind.BombSite)];
        Work(
            host,
            Attackers,
            at => SiteAt(sites, at) is not null,
            Bomb.PlantSeconds,
            (planter, at) =>
            {
                host.Match.Award(planter, host.Match.Rules.MoneyPlant);
                PlantedAt = at;

                // The blast's wave starts building as the bomb is set down, a slice a tick through the fuse.
                _wave = host.World.Collision is { } collision
                    ? new BlastWaveField(collision, at, Bomb.BlastMetres * GlassReachShare)
                    : null;
                BombYaw = host.World.YawOf(planter) ?? BombYaw;
                PlantedSite = SiteAt(sites, at)?.Id ?? "?";
                BlowsAt = tick + (long)Ticks(Bomb.FuseSeconds);
                _holding.Clear();
                Carrier = -1;
                DroppedAt = null;
            },
            slot => slot == Carrier,
            host.World.IsPlanting);
    }

    /// <inheritdoc/>
    protected override int? RoundWinner(IModeHost host, long tick)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (Exploded)
        {
            EndReason = RoundEnd.Detonated;
            return Attackers;
        }

        if (Defused)
        {
            EndReason = RoundEnd.Defused;
            return Defenders;
        }

        if (PlantedAt is not null)
        {
            // Down: the defenders must defuse it; with none left standing, it is the attackers'.
            if (PlayersOn(host, Defenders) > 0 && AliveOn(host, Defenders) == 0)
            {
                EndReason = RoundEnd.Eliminated;
                return Attackers;
            }

            return null;
        }

        return base.RoundWinner(host, tick);
    }

    /// <inheritdoc/>
    protected override int TimeoutWinner(IModeHost host) => Defenders;

    // The bomb this tick: its carrier followed, put down a little ahead of a carrier that drops it, dropped where a carrier
    // that fell or left last stood, and taken up by the first attacker standing over it -- not by the one who dropped it until
    // it has stepped away; handed out afresh when it is nowhere (a round begun with no attacker yet).
    private void Carry(IModeHost host)
    {
        if (Carrier >= 0)
        {
            if (host.Match.Players.TryGetValue(Carrier, out MatchPlayer? carrier) && carrier.IsAlive && carrier.Team == Attackers)
            {
                _carrierLast = host.World.PositionOf(Carrier) ?? _carrierLast;
                BombYaw = host.World.YawOf(Carrier) ?? BombYaw;
                if (!host.World.TakeBombDrop(Carrier) || _carrierLast is not { } stood)
                {
                    return;
                }

                _droppedBy = Carrier;
                Let(host, Carrier, stood, far: true);
                return;
            }

            _droppedBy = -1;
            if (_carrierLast is { } fell)
            {
                Let(host, -1, fell, far: false);
            }
            else
            {
                Carrier = -1;
            }
        }

        if (DroppedAt is not { } dropped)
        {
            Carrier = NextCarrier(host);
            _handedTo = Carrier >= 0 ? Carrier : _handedTo;
            _carrierLast = Carrier >= 0 ? host.World.PositionOf(Carrier) : null;
            return;
        }

        // A thrown bomb is where its flight has taken it.
        if (BombFlies && host.World.ThrownBombAt is { } flown)
        {
            DroppedAt = dropped = flown;
        }

        double reach = Bomb.PickUpMetres * WorldUnits.PerMetre;
        foreach ((int slot, MatchPlayer player) in host.Match.Players.OrderBy(p => p.Key))
        {
            bool over = player.Team == Attackers && player.IsAlive && host.World.PositionOf(slot) is { } stands && Vector3d.Distance(stands, dropped) <= reach;
            if (slot == _droppedBy)
            {
                _droppedBy = over ? slot : -1;
                continue;
            }

            if (over && host.World.PositionOf(slot) is { } where)
            {
                _droppedBy = -1;
                Carrier = slot;
                DroppedAt = null;
                if (BombFlies)
                {
                    host.World.TakeThrownBomb();
                    BombFlies = false;
                }

                _carrierLast = where;
                return;
            }
        }
    }

    // The bomb out of its carrier's hands: thrown into the world -- far when let go on purpose, short when its carrier fell -- or,
    // in a world that throws nothing, laid ahead of where it stood (where it stood, for a fall).
    private void Let(IModeHost host, int slot, Vector3d stood, bool far)
    {
        Carrier = -1;
        _carrierLast = null;
        BombFlies = host.World.ThrowBomb(slot, stood, BombYaw, far);
        if (BombFlies && host.World.ThrownBombAt is { } thrown)
        {
            DroppedAt = thrown;
            return;
        }

        BombFlies = false;
        double ahead = far ? Bomb.DropAheadMetres * WorldUnits.PerMetre : 0.0;
        DroppedAt = new Vector3d(stood.X + (Math.Sin(BombYaw) * ahead), stood.Y + (Math.Cos(BombYaw) * ahead), stood.Z);
    }

    // The attacker the bomb goes to next: the first standing after the one it went to last, round the team in slot order.
    private int NextCarrier(IModeHost host)
    {
        int[] attackers = [.. host.Match.Players.Where(p => p.Value.Team == Attackers && p.Value.IsAlive).Select(p => p.Key).Order()];
        if (attackers.Length == 0)
        {
            return -1;
        }

        foreach (int slot in attackers)
        {
            if (slot > _handedTo)
            {
                return slot;
            }
        }

        return attackers[0];
    }

    // The players of a team at what it works on, holding the use key, count on; one that lets go or steps off starts again.
    // Only the players the filter allows work at all (the carrier alone plants).
    private void Work(
        IModeHost host, int team, Func<Vector3d, bool> at, double seconds, Action<int, Vector3d> done, Func<int, bool>? may = null, Func<int, bool>? holding = null)
    {
        holding ??= host.World.IsUsing;
        foreach ((int slot, MatchPlayer player) in host.Match.Players)
        {
            if (player.Team != team || !player.IsAlive || (may is not null && !may(slot)) || !holding(slot) || host.World.PositionOf(slot) is not { } where || !at(where))
            {
                _holding.Remove(slot);
                continue;
            }

            int held = _holding.GetValueOrDefault(slot) + 1;
            _holding[slot] = held;
            if (held >= Ticks(seconds))
            {
                done(slot, where);
                return;
            }
        }
    }

    // The bomb goes off and the round is the attackers': its front then sweeps out, hurting each player as it reaches them by
    // how far it had to travel -- through the open, round corners, through walls -- to get there.
    private void Blow(IModeHost host, Vector3d bomb, long tick)
    {
        Exploded = true;
        _wave?.Advance(TimeSpan.MaxValue);
        host.World.BlastSparingPlayers(bomb, Bomb.BlastMetres * WorldUnits.PerMetre);
        _waveFrom = tick;
        _waveYetToReach.Clear();
        foreach ((int slot, MatchPlayer player) in host.Match.Players)
        {
            if (player.IsAlive)
            {
                _waveYetToReach.Add(slot);
            }
        }

        _glassYetToReach.Clear();
        _glassYetToReach.AddRange(host.World.GlassNear(bomb, Bomb.BlastMetres * GlassReachShare * WorldUnits.PerMetre));
        TickWave(host, tick);
    }

    private static MpPoint? SiteAt(MpPoint[] sites, Vector3d at) =>
        sites.FirstOrDefault(s => Vector3d.Distance(new Vector3d(s.Position[0], s.Position[1], s.Position[2]), at) <= s.Radius);

    private double Ticks(double seconds) => Math.Max(1, Math.Ceiling(seconds * TickRate));
}
