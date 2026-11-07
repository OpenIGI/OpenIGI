namespace OpenIGI.Game.Multiplayer.Modes;

/// <summary>How a round-based match is played: rounds to win, how long a round lasts, and the pauses between.</summary>
public sealed record RoundSettings
{
    /// <summary>Gets the rounds a team must win to win the match.</summary>
    public int RoundsToWin { get; init; } = 6;

    /// <summary>Gets the seconds a round lasts before its clock decides it.</summary>
    public double RoundSeconds { get; init; } = 120;

    /// <summary>Gets the seconds a decided round's result is shown before the next begins.</summary>
    public double RoundOverSeconds { get; init; } = 5;

    /// <summary>Gets the seconds the scores are shown once the match is decided.</summary>
    public double IntermissionSeconds { get; init; } = 15;

    /// <summary>Gets the seconds from a round's start a mode with a buy phase sells for.</summary>
    public double BuySeconds { get; init; } = 20;

    /// <summary>Gets the seconds a round of a mode with a buy phase opens on, everyone held where they start to buy and plan
    /// (Counter-Strike's freeze time); the buy time and the round's clock run from its end.</summary>
    public double FreezeSeconds { get; init; } = 10;

    /// <summary>Gets how far from where it started the round a player may still buy, in metres, on a map without buy zones.</summary>
    public double BuyMetres { get; init; } = 15;

    /// <summary>Gets a value indicating whether the teams change sides at the half, for a mode whose sides differ (attack, defence).</summary>
    public bool SwapSides { get; init; } = true;
}

/// <summary>
/// A match of rounds between two teams, as the tactical shooters play them: every player stands at its team's spawns when a
/// round begins and has one life in it; the round goes to the team left standing, or as the mode says when its clock runs
/// out; the first team to <see cref="RoundSettings.RoundsToWin"/> rounds wins the match.
/// </summary>
/// <remarks>
/// A round is decided only while both teams have a player: one player alone on the map plays on, as in a warmup, until an
/// opponent joins. A player joining during a round waits, dead, for the next (Source's round rules); the dead watch on.
/// </remarks>
/// <param name="tickRate">The world's ticks per second.</param>
/// <param name="settings">How it is played, or the defaults.</param>
public abstract class RoundMode(int tickRate, RoundSettings? settings) : IGameMode
{
    /// <inheritdoc/>
    public abstract string Id { get; }

    /// <inheritdoc/>
    public bool IsTeamMode => true;

    /// <inheritdoc/>
    public MatchFlow Flow { get; } = new(tickRate);

    /// <summary>Gets how it is played.</summary>
    public RoundSettings Settings { get; } = settings ?? new RoundSettings();

    /// <summary>Gets what keeps the players inside the map's bounds.</summary>
    public OutOfBounds Bounds { get; } = new();

    /// <summary>Gets a value indicating whether the match was decided and its scores shown, and the map is not played again.</summary>
    public bool IsMapOver { get; private set; }

    /// <summary>Gets how many matches were played to the end on this map.</summary>
    public int MatchesPlayed { get; private set; }

    /// <summary>Gets or sets a value indicating whether the same map is played again after the intermission.</summary>
    public bool PlaysAgain { get; set; } = true;

    /// <summary>Gets the world's ticks per second.</summary>
    protected int TickRate => tickRate;

    /// <summary>Gets the rounds decided this match.</summary>
    public int RoundsPlayed { get; private set; }

    /// <summary>Gets a value indicating whether the mode's two sides play differently, so the teams change sides at the half.</summary>
    protected virtual bool SidesDiffer => false;

    /// <summary>Gets a value indicating whether the mode has a buy phase and an economy: round pay, and the armoury for the
    /// first <see cref="RoundSettings.BuySeconds"/> of each round.</summary>
    protected virtual bool HasArmoury => false;

    // When the round in play began, and where each player stood as it did: where it may buy.
    private long _roundBegan;
    private readonly Dictionary<int, Core.Numerics.Vector3d> _startedAt = [];

    // The seconds of the round's freeze time still to run: nought outside a round in play, or for a mode without one.
    private double FreezeLeft(long tick) =>
        HasArmoury && Flow.Phase == MatchPhase.Live ? Math.Max(0, Settings.FreezeSeconds - ((tick - _roundBegan) / (double)tickRate)) : 0;

    /// <inheritdoc/>
    public string Buy(IModeHost host, int slot, string asked, long tick)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(asked);
        IReadOnlyList<ArmouryItem> items = Armoury.For(host.Title);
        if (!HasArmoury || items.Count == 0)
        {
            return "There is nothing to buy in this mode.";
        }

        // "ammo ak47" asks for a carried gun's ammunition, as I.G.I. 2's shop sells it beside the gun.
        string[] words = asked.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool ammo = words.Length == 2 && string.Equals(words[0], Armoury.AmmoWord, StringComparison.OrdinalIgnoreCase);
        if (Armoury.Find(items, ammo ? words[1] : asked) is not { } item || (ammo && item.AmmoPrice <= 0))
        {
            return $"Buy: {string.Join(", ", items.Select((i, n) => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{n + 1} {i.Id} ${i.Price}")))}; ammo <gun> for a gun's ammunition.";
        }

        string name = ammo ? $"{item.Name} ammo" : item.Name;
        int price = ammo ? item.AmmoPrice : item.Price;

        if (BuyLeft(tick) <= 0)
        {
            return "The buy time is over.";
        }

        if (!host.Match.Players.TryGetValue(slot, out MatchPlayer? player) || !player.IsAlive)
        {
            return "Only the living can buy.";
        }

        if (!InBuyZone(host, slot, player.Team))
        {
            return "Buy where your side starts.";
        }

        if (!ammo && item.Rounds > 0 && host.AmmoRoom(slot, item.Weapon, item.Rounds) is 0)
        {
            return $"You can carry no more {name}.";
        }

        if (ammo)
        {
            if (host.AmmoRoom(slot, item.Weapon, item.Rounds) is not { } room)
            {
                return $"You carry no {item.Name}.";
            }

            if (room <= 0)
            {
                return $"Your {name} is full.";
            }
        }

        if (!host.Match.TrySpend(slot, price))
        {
            return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{name} costs ${price}; you have ${player.Money}.");
        }

        // A gun comes with its rounds; ammunition fills the pouch back to them.
        if (!host.Give(slot, item.Weapon, item.Rounds))
        {
            host.Match.Award(slot, price);
            return $"{name} can't be handed over here.";
        }

        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Bought {name}. ${player.Money} left.");
    }

    // The seconds the armoury still sells for this round: nought outside a round in play, or for a mode without one.
    private double BuyLeft(long tick) =>
        HasArmoury && Flow.Phase == MatchPhase.Live ? Math.Max(0, Settings.FreezeSeconds + Settings.BuySeconds - ((tick - _roundBegan) / (double)tickRate)) : 0;

    // A player may buy in one of its side's buy zones, or on a map without them near where it began the round.
    private bool InBuyZone(IModeHost host, int slot, int team)
    {
        if (host.World.PositionOf(slot) is not { } at)
        {
            return false;
        }

        MpPoint[] zones = [.. (host.Layout?.Points ?? []).Where(p => p.Kind == MpPointKind.BuyZone && (p.Team < 0 || p.Team == team))];
        if (zones.Length > 0)
        {
            return zones.Any(z => Core.Numerics.Vector3d.Distance(new Core.Numerics.Vector3d(z.Position[0], z.Position[1], z.Position[2]), at) <= z.Radius);
        }

        return !_startedAt.TryGetValue(slot, out Core.Numerics.Vector3d start)
            || Core.Numerics.Vector3d.Distance(start, at) <= Settings.BuyMetres * Core.Numerics.WorldUnits.PerMetre;
    }

    /// <inheritdoc/>
    public int TeamFor(IModeHost host, int slot) => GameModes.FairTeam(host, slot);

    /// <inheritdoc/>
    public virtual string Status(long tick) => string.Empty;

    /// <inheritdoc/>
    public virtual ModeReport Report(long tick) => new()
    {
        Target = Settings.RoundsToWin,
        Reason = Flow.Phase is MatchPhase.RoundOver or MatchPhase.Intermission ? EndReason : RoundEnd.None,
        BuyLeft = BuyLeft(tick),
        FreezeLeft = FreezeLeft(tick),
    };

    /// <summary>Gets or sets why the round was decided, which <see cref="RoundWinner"/> says as it decides it.</summary>
    protected RoundEnd EndReason { get; set; }

    /// <inheritdoc/>
    public void Start(IModeHost host, long tick)
    {
        ArgumentNullException.ThrowIfNull(host);
        host.Match.IsFreeForAll = false;
        host.Match.NewMap();
        host.World.BeginMatch();
        RoundsPlayed = 0;
        BeginRound(host, tick);
    }

    /// <inheritdoc/>
    public void Tick(IModeHost host, long tick, IReadOnlyList<WorldDeath> deaths)
    {
        ArgumentNullException.ThrowIfNull(host);
        GameModes.CountDeaths(host, tick, deaths);
        switch (Flow.Phase)
        {
            case MatchPhase.Live when FreezeLeft(tick) > 0:
                // The freeze time: held where they start, buying and planning; nothing of the round runs yet.
                host.World.HoldStill(true);
                break;
            case MatchPhase.Live:
                host.World.HoldStill(false);
                Bounds.Tick(host, tick);
                TickRound(host, tick);
                if (RoundWinner(host, tick) is { } winner)
                {
                    EndRound(host, tick, winner);
                }

                break;
            case MatchPhase.RoundOver when Flow.IsDue(tick):
                host.World.HoldStill(false);
                if (MatchWinner(host) is { } champion)
                {
                    Flow.Enter(MatchPhase.Intermission, tick, Settings.IntermissionSeconds, champion);
                }
                else
                {
                    // At the half (one round short of the most a team needs), attack and defence change over.
                    if (SidesDiffer && Settings.SwapSides && RoundsPlayed == Settings.RoundsToWin - 1)
                    {
                        host.Match.SwapSides();
                    }

                    BeginRound(host, tick);
                }

                break;
            case MatchPhase.Intermission when Flow.IsDue(tick):
                MatchesPlayed++;
                if (PlaysAgain)
                {
                    host.Match.NewMap();
                    host.World.BeginMatch();
                    RoundsPlayed = 0;
                    BeginRound(host, tick);
                }
                else
                {
                    IsMapOver = true;
                }

                break;
        }
    }

    /// <summary>Gets how many of a team's players are alive now.</summary>
    /// <param name="host">The session.</param>
    /// <param name="team">The team.</param>
    /// <returns>The count.</returns>
    protected static int AliveOn(IModeHost host, int team)
    {
        ArgumentNullException.ThrowIfNull(host);
        return host.Match.Players.Values.Count(p => p.Team == team && p.IsAlive);
    }

    /// <summary>Gets how many players a team has.</summary>
    /// <param name="host">The session.</param>
    /// <param name="team">The team.</param>
    /// <returns>The count.</returns>
    protected static int PlayersOn(IModeHost host, int team)
    {
        ArgumentNullException.ThrowIfNull(host);
        return host.Match.Players.Values.Count(p => p.Team == team);
    }

    /// <summary>Runs the mode's own part of a round in play (a bomb's plant, its fuse); nothing by default.</summary>
    /// <param name="host">The session.</param>
    /// <param name="tick">The tick.</param>
    protected virtual void TickRound(IModeHost host, long tick)
    {
    }

    /// <summary>Gets who the round goes to when its clock runs out with both teams standing.</summary>
    /// <param name="host">The session.</param>
    /// <returns>The team, or -1 for a draw.</returns>
    protected abstract int TimeoutWinner(IModeHost host);

    /// <summary>Gets who the round has gone to by now, or <see langword="null"/> while it is open.</summary>
    /// <param name="host">The session.</param>
    /// <param name="tick">The tick.</param>
    /// <returns>The winning team, -1 for a draw, or <see langword="null"/>.</returns>
    /// <remarks>The team left standing wins; both down together, a draw; the clock out, <see cref="TimeoutWinner"/>.</remarks>
    protected virtual int? RoundWinner(IModeHost host, long tick)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (PlayersOn(host, TeamRoster.Good) == 0 || PlayersOn(host, TeamRoster.Bad) == 0)
        {
            // A side nobody plays on can't be beaten, so the round only runs out: a draw, scoring nothing, and the next begins.
            if (!Flow.IsDue(tick))
            {
                return null;
            }

            EndReason = RoundEnd.TimeRanOut;
            return -1;
        }

        int good = AliveOn(host, TeamRoster.Good);
        int bad = AliveOn(host, TeamRoster.Bad);
        if (good == 0 || bad == 0)
        {
            EndReason = RoundEnd.Eliminated;
            return good > 0 ? TeamRoster.Good : bad > 0 ? TeamRoster.Bad : -1;
        }

        if (!Flow.IsDue(tick))
        {
            return null;
        }

        EndReason = RoundEnd.TimeRanOut;
        return TimeoutWinner(host);
    }

    /// <summary>Called as a round begins, everyone standing again: a mode's own reset (the bomb back with the attackers).</summary>
    /// <param name="host">The session.</param>
    /// <param name="tick">The tick.</param>
    protected virtual void OnRoundBegun(IModeHost host, long tick)
    {
    }

    private void BeginRound(IModeHost host, long tick)
    {
        // Those who asked for the other side in the middle of the last round go over to it now.
        foreach (MatchPlayer player in host.Match.Players.Values)
        {
            if (player.NextTeam is { } next)
            {
                player.Team = next;
                player.NextTeam = null;
            }
        }

        host.ResetLevel();
        foreach (int slot in host.PlayerSlots)
        {
            if (host.Match.Players.ContainsKey(slot))
            {
                host.Respawn(slot);
            }
        }

        Flow.Enter(MatchPhase.Live, tick, Settings.RoundSeconds + (HasArmoury ? Settings.FreezeSeconds : 0));
        EndReason = RoundEnd.None;
        _roundBegan = tick;
        _startedAt.Clear();
        foreach (int slot in host.PlayerSlots)
        {
            if (host.World.PositionOf(slot) is { } at)
            {
                _startedAt[slot] = at;
            }
        }

        OnRoundBegun(host, tick);
    }

    private void EndRound(IModeHost host, long tick, int winner)
    {
        if (winner >= 0)
        {
            host.Match.AddTeamScore(winner, 1);
        }

        RoundsPlayed++;
        if (HasArmoury)
        {
            host.Match.PayRound(winner);
        }

        Flow.Enter(MatchPhase.RoundOver, tick, Settings.RoundOverSeconds, winner);
    }

    /// <inheritdoc/>
    public void EndNow(IModeHost host, long tick)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (Flow.Phase is MatchPhase.Live or MatchPhase.RoundOver)
        {
            int good = host.Match.ScoreOf(TeamRoster.Good);
            int bad = host.Match.ScoreOf(TeamRoster.Bad);
            Flow.Enter(MatchPhase.Intermission, tick, Settings.IntermissionSeconds, good == bad ? -1 : good > bad ? TeamRoster.Good : TeamRoster.Bad);
        }
    }

    // The team that has won its rounds, once one has.
    private int? MatchWinner(IModeHost host)
    {
        foreach (int team in (int[])[TeamRoster.Good, TeamRoster.Bad])
        {
            if (host.Match.ScoreOf(team) >= Settings.RoundsToWin)
            {
                return team;
            }
        }

        return null;
    }
}

/// <summary>Elimination: rounds of one life each, won by the team left standing; a round the clock ends goes to the team with more alive.</summary>
/// <param name="tickRate">The world's ticks per second.</param>
/// <param name="settings">How it is played, or the defaults.</param>
public sealed class EliminationMode(int tickRate, RoundSettings? settings = null) : RoundMode(tickRate, settings)
{
    /// <inheritdoc/>
    public override string Id => GameModes.Elimination;

    /// <inheritdoc/>
    /// <remarks>Its rounds are bought for as search and destroy's are: the freeze time, the buy time, and the round's money.</remarks>
    protected override bool HasArmoury => true;

    /// <inheritdoc/>
    protected override int TimeoutWinner(IModeHost host)
    {
        int good = AliveOn(host, TeamRoster.Good);
        int bad = AliveOn(host, TeamRoster.Bad);
        return good == bad ? -1 : good > bad ? TeamRoster.Good : TeamRoster.Bad;
    }
}
