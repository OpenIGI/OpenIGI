using System.Globalization;
using OpenIGI.Core.Numerics;

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
    public double BlastMetres { get; init; } = 20;

    /// <summary>Gets how near the bomb a defender must stand, in metres.</summary>
    public double DefuseMetres { get; init; } = 2.5;
}

/// <summary>
/// Search and destroy: rounds of one life; the attackers win a round by planting the bomb at one of the map's sites and
/// seeing it go off, or by leaving no defender standing; the defenders by defusing it, by leaving no attacker standing before
/// it is planted, or by holding out until the round's clock runs out.
/// </summary>
/// <remarks>
/// Any attacker can plant (there is no bomb to carry); planting and defusing are holding the use key, standing still or not,
/// for <see cref="BombSettings.PlantSeconds"/> and <see cref="BombSettings.DefuseSeconds"/> -- a hold let go starts again.
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

    /// <inheritdoc/>
    public override string Id => GameModes.SearchAndDestroy;

    /// <summary>Gets how the bomb is played.</summary>
    public BombSettings Bomb { get; } = bomb ?? new BombSettings();

    /// <summary>Gets the team that attacks the sites.</summary>
    public static int Attackers => TeamRoster.Good;

    /// <summary>Gets the team that defends them.</summary>
    public static int Defenders => TeamRoster.Bad;

    /// <summary>Gets where the bomb was planted this round, or <see langword="null"/> while it is not.</summary>
    public Vector3d? PlantedAt { get; private set; }

    /// <summary>Gets the site the bomb was planted at, or <see langword="null"/>.</summary>
    public string? PlantedSite { get; private set; }

    /// <summary>Gets the tick the planted bomb goes off on.</summary>
    public long BlowsAt { get; private set; }

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
        FuseSeconds = Bomb.FuseSeconds,
        Working = [.. _holding.Select(h => new ModeWork(h.Key, ProgressOf(h.Key) ?? 0f, PlantedAt is not null))],
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
        PlantedAt = null;
        PlantedSite = null;
        Defused = false;
        Exploded = false;
    }

    /// <inheritdoc/>
    protected override void TickRound(IModeHost host, long tick)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (Exploded || Defused)
        {
            return;
        }

        if (PlantedAt is { } bomb)
        {
            if (tick >= BlowsAt)
            {
                Blow(host, bomb);
                return;
            }

            Work(host, Defenders, at => Vector3d.Distance(at, bomb) <= Bomb.DefuseMetres * WorldUnits.PerMetre, Bomb.DefuseSeconds, (_, _) => Defused = true);
            return;
        }

        MpPoint[] sites = [.. (host.Layout?.Points ?? []).Where(p => p.Kind == MpPointKind.BombSite)];
        Work(
            host,
            Attackers,
            at => SiteAt(sites, at) is not null,
            Bomb.PlantSeconds,
            (_, at) =>
            {
                PlantedAt = at;
                PlantedSite = SiteAt(sites, at)?.Id ?? "?";
                BlowsAt = tick + (long)Ticks(Bomb.FuseSeconds);
                _holding.Clear();
            });
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

    // The players of a team at what it works on, holding the use key, count on; one that lets go or steps off starts again.
    private void Work(IModeHost host, int team, Func<Vector3d, bool> at, double seconds, Action<int, Vector3d> done)
    {
        foreach ((int slot, MatchPlayer player) in host.Match.Players)
        {
            if (player.Team != team || !player.IsAlive || !host.World.IsUsing(slot) || host.World.PositionOf(slot) is not { } where || !at(where))
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

    // The bomb goes off: everyone near it dies, and the round is the attackers'.
    private void Blow(IModeHost host, Vector3d bomb)
    {
        Exploded = true;
        double reach = Bomb.BlastMetres * WorldUnits.PerMetre;
        host.World.Blast(bomb, reach);
        foreach ((int slot, MatchPlayer player) in host.Match.Players)
        {
            if (player.IsAlive && host.World.PositionOf(slot) is { } where && Vector3d.Distance(where, bomb) <= reach)
            {
                host.World.KillPlayer(slot);
            }
        }
    }

    private static MpPoint? SiteAt(MpPoint[] sites, Vector3d at) =>
        sites.FirstOrDefault(s => Vector3d.Distance(new Vector3d(s.Position[0], s.Position[1], s.Position[2]), at) <= s.Radius);

    private double Ticks(double seconds) => Math.Max(1, Math.Ceiling(seconds * TickRate));
}
