using System.Globalization;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Multiplayer.Modes;

/// <summary>One side of the score strip: a team's name, score and players, alive or down.</summary>
/// <param name="Team">The team.</param>
/// <param name="Name">Its name, as the strip shows it.</param>
/// <param name="Score">Its score: rounds won, or kills.</param>
/// <param name="Alive">Each of its players, <see langword="true"/> while standing; the player's own first.</param>
/// <param name="IsOwn">Whether it is the local player's team.</param>
public sealed record TeamStrip(int Team, string Name, int Score, IReadOnlyList<bool> Alive, bool IsOwn);

/// <summary>The large card a decided round or match shows in the middle of the screen.</summary>
/// <param name="Title">What happened, as <c>ROUND WON</c> or <c>VICTORY</c>.</param>
/// <param name="Detail">Why, or the final score.</param>
/// <param name="Outcome">1 for the local player's side winning, -1 for it losing, 0 for a draw or nobody's.</param>
public sealed record ResultCard(string Title, string Detail, int Outcome);

/// <summary>An action the local player can take where it stands, as <c>HOLD [E] TO PLANT AT A</c>.</summary>
/// <param name="Verb">The verb before the key, as <c>HOLD</c>.</param>
/// <param name="Action">What it does, as <c>TO PLANT THE BOMB AT A</c>.</param>
/// <param name="Keyed">Whether the use key is shown between the two, and holding it works; else the hint only says what to do.</param>
public sealed record ModeHint(string Verb, string Action, bool Keyed = true);

/// <summary>
/// Everything a multiplayer mode's HUD draws, made each frame from where the match stands, the scoreboard and the local
/// player: the score strip across the top with the clock and each side's players, the bomb and its fuse, a plant or defuse in
/// progress, what the player can do where it stands, and a decided round's or match's card.
/// </summary>
public sealed record MatchHud
{
    /// <summary>Gets the mode's id.</summary>
    public string Mode { get; init; } = string.Empty;

    /// <summary>Gets the mode's name as the strip shows it.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Gets the match's phase.</summary>
    public MatchPhase Phase { get; init; }

    /// <summary>Gets the round being played, or 0 for a mode without rounds.</summary>
    public int Round { get; init; }

    /// <summary>Gets the rounds or score that win, or 0.</summary>
    public int Target { get; init; }

    /// <summary>Gets the phase's clock, as <c>1:42</c>, or <see langword="null"/>.</summary>
    public string? Clock { get; init; }

    /// <summary>Gets whether the clock is in its last seconds.</summary>
    public bool ClockUrgent { get; init; }

    /// <summary>Gets a value indicating whether the round's freeze time runs: everyone held where they start, buying and planning.</summary>
    public bool Frozen { get; init; }

    /// <summary>Gets the local player's team's side of the strip (or the first team's, for a player with none); <see langword="null"/> in a free-for-all.</summary>
    public TeamStrip? Left { get; init; }

    /// <summary>Gets the other side.</summary>
    public TeamStrip? Right { get; init; }

    /// <summary>Gets the local player's role, as <c>ATTACK</c>, or <see langword="null"/>.</summary>
    public string? Role { get; init; }

    /// <summary>Gets the local player's score in a free-for-all.</summary>
    public int OwnScore { get; init; }

    /// <summary>Gets the local player's place in a free-for-all, from 1.</summary>
    public int OwnRank { get; init; }

    /// <summary>Gets the free-for-all's leader's name (another player's, or the local one's when it leads).</summary>
    public string LeaderName { get; init; } = string.Empty;

    /// <summary>Gets the leader's score.</summary>
    public int LeaderScore { get; init; }

    /// <summary>Gets how many play.</summary>
    public int Players { get; init; }

    /// <summary>Gets the site the bomb is planted at, or empty.</summary>
    public string Planted { get; init; } = string.Empty;

    /// <summary>Gets the planted bomb's fuse, as <c>0:32</c>, or <see langword="null"/>.</summary>
    public string? Fuse { get; init; }

    /// <summary>Gets how much of the fuse is left, in <c>[0,1]</c>.</summary>
    public float FuseFraction { get; init; }

    /// <summary>Gets how far the local player's plant or defuse has got, or <see langword="null"/> while it is at neither.</summary>
    public float? Progress { get; init; }

    /// <summary>Gets what the progress is of, as <c>PLANTING THE BOMB</c>.</summary>
    public string? ProgressLabel { get; init; }

    /// <summary>Gets a teammate's work the player is told of, as <c>ANYA IS DEFUSING</c>, or <see langword="null"/>.</summary>
    public string? TeamNote { get; init; }

    /// <summary>Gets what the player can do where it stands, or <see langword="null"/>.</summary>
    public ModeHint? Hint { get; init; }

    /// <summary>Gets who has the bomb, as an attacker is told it (<c>YOU CARRY THE BOMB</c>), or <see langword="null"/>.</summary>
    public string? BombNote { get; init; }

    /// <summary>Gets a value indicating whether this player carries the bomb.</summary>
    public bool CarriesBomb { get; init; }

    /// <summary>Gets a value indicating whether the bomb lies on the ground or flies, thrown down, with nobody carrying it.</summary>
    public bool BombIsLoose { get; init; }

    /// <summary>Gets where the dropped bomb lies, in world units, for an attacker to find it; <see langword="null"/> otherwise.</summary>
    public Vector3d? BombMarkAt { get; init; }

    /// <summary>Gets a decided round's or match's card, or <see langword="null"/> while one is played.</summary>
    public ResultCard? Result { get; init; }

    /// <summary>Gets the local player's money in a mode with an economy, or <see langword="null"/>.</summary>
    public int? Money { get; init; }

    /// <summary>Gets the buy time left, as <c>0:14</c>, while the armoury sells; <see langword="null"/> once it is shut.</summary>
    public string? BuyClock { get; init; }

    /// <summary>Gets a value indicating whether the armoury is a free loadout to pick from now (a deathmatch's), rather than a shop.</summary>
    public bool FreeLoadout { get; init; }

    /// <summary>Gets a line under the strip explaining the phase (a warmup's), or <see langword="null"/>.</summary>
    public string? Notice { get; init; }

    /// <summary>Gets the vote for the next map while a decided match's card is up, or <see langword="null"/>.</summary>
    public NextMapVote? MapVote { get; init; }

    /// <summary>Gets a value indicating whether the mode plays teams.</summary>
    public bool HasTeams => Left is not null;

    /// <summary>Makes the HUD of a match.</summary>
    /// <param name="state">Where the match stood when the server last said.</param>
    /// <param name="secondsSince">The seconds since, which its clocks have run down by.</param>
    /// <param name="board">The scoreboard, or <see langword="null"/> before there is one.</param>
    /// <param name="localEntity">The local player's entity, or <see langword="null"/>.</param>
    /// <param name="ownPosition">Where the local player stands, in world units, or <see langword="null"/>.</param>
    /// <param name="layout">The map's layout, for the sites the player stands in, or <see langword="null"/>.</param>
    /// <param name="use">
    /// The local player's use key on a client, which starts and ends its own plant or defuse bar without waiting on the server;
    /// <see langword="null"/> where the report is the mode's own (a listen host).
    /// </param>
    /// <param name="bombInHand">Whether the local player holds the bomb in hand, which it must to plant it.</param>
    /// <returns>The HUD, or <see langword="null"/> for a match with none (co-op).</returns>
    public static MatchHud? Of(
        MatchState state, double secondsSince, Scoreboard? board, int? localEntity, Vector3d? ownPosition = null, MpLayout? layout = null, LocalUse? use = null,
        bool bombInHand = true)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Mode == GameModes.Coop || string.IsNullOrEmpty(state.Mode))
        {
            return null;
        }

        ModeReport report = state.Report ?? ModeReport.None;
        ScoreboardLine[] lines = board?.Lines ?? [];
        ScoreboardLine? own = localEntity is { } entity && Array.FindIndex(lines, l => l.Entity == entity) is var at and >= 0 ? lines[at] : null;
        bool teams = GameModes.HasTeams(state.Mode);
        bool rounds = state.Mode is GameModes.Elimination or GameModes.SearchAndDestroy;
        double? left = state.SecondsLeft is { } s ? Math.Max(0, s - secondsSince) : null;

        // The freeze time: its own countdown on the clock, and a word on what it is for.
        double freeze = state.Phase == MatchPhase.Live ? Math.Max(0, report.FreezeLeft - secondsSince) : 0;
        if (freeze > 0)
        {
            left = freeze;
        }

        MatchHud hud = new()
        {
            Mode = state.Mode,
            Title = GameModes.Title(state.Mode),
            Phase = state.Phase,
            Round = rounds ? state.Round : 0,
            Target = report.Target,
            Clock = left is { } l ? MatchState.Clock(l) : null,
            ClockUrgent = state.Phase == MatchPhase.Live && freeze <= 0 && left is <= 10,
            Frozen = freeze > 0,
            Players = lines.Length,
            Notice = state.Phase == MatchPhase.Warmup ? "WARMUP  ·  THE MATCH BEGINS WHEN THE CLOCK RUNS OUT"
                : freeze > 0 ? "STRATEGY TIME  ·  BUY AND PLAN  ·  THE ROUND BEGINS WHEN THE CLOCK RUNS OUT"
                : null,
        };

        int ownTeam = own?.Team ?? -1;
        if (teams)
        {
            int first = ownTeam is TeamRoster.Good or TeamRoster.Bad ? ownTeam : TeamRoster.Good;
            int second = first == TeamRoster.Good ? TeamRoster.Bad : TeamRoster.Good;
            hud = hud with
            {
                Left = Strip(first, board, lines, own, report),
                Right = Strip(second, board, lines, own, report),
                Role = report.Attackers >= 0 && ownTeam >= 0 ? (ownTeam == report.Attackers ? "ATTACK" : "DEFEND") : null,
            };
        }
        else
        {
            ScoreboardLine[] ranked = [.. lines.OrderByDescending(x => x.Kills)];
            int ownScore = own?.Kills ?? 0;
            ScoreboardLine? leader = ranked.Length > 0 ? ranked[0] : null;
            ScoreboardLine? rival = own is { } me && leader is { } top && top.Slot == me.Slot && ranked.Length > 1 ? ranked[1] : leader;
            hud = hud with
            {
                OwnScore = ownScore,
                OwnRank = 1 + lines.Count(x => x.Kills > ownScore),
                LeaderName = rival?.Name ?? string.Empty,
                LeaderScore = rival?.Kills ?? 0,
            };
        }

        if (report.Planted.Length > 0)
        {
            double fuse = Math.Max(0, report.FuseLeft - secondsSince);
            hud = hud with
            {
                Planted = report.Planted,
                Fuse = MatchState.Clock(fuse),
                FuseFraction = report.FuseSeconds > 0 ? (float)Math.Clamp(fuse / report.FuseSeconds, 0, 1) : 0f,
            };
        }

        if (state.Phase == MatchPhase.Live && report.Attackers >= 0 && report.Planted.Length == 0 && own is { } attacker && attacker.Team == report.Attackers)
        {
            string? carrier = Array.FindIndex(lines, l => l.Slot == report.Carrier) is var held and >= 0 ? lines[held].Name : null;
            hud = hud with
            {
                BombNote = report.Carrier == attacker.Slot ? "YOU CARRY THE BOMB"
                    : report.DroppedAt is not null ? "THE BOMB IS DOWN  ·  WALK OVER IT TO TAKE IT"
                    : carrier is { Length: > 0 } ? $"{carrier.ToUpperInvariant()} CARRIES THE BOMB"
                    : null,
                BombMarkAt = report.DroppedAt,
                CarriesBomb = report.Carrier == attacker.Slot,
                BombIsLoose = report.DroppedAt is not null || report.BombFlies,
            };
        }

        ModeHint? could = state.Phase == MatchPhase.Live && own is { IsAlive: true } alive && ownPosition is { } where
            ? HintFor(report, alive, where, layout, false, bombInHand)
            : null;
        hud = Work(hud, report, lines, own, secondsSince, use, could is { Keyed: true });
        if (hud.Progress is null)
        {
            hud = hud with { Hint = could };
        }

        double buyLeft = Math.Max(0, report.BuyLeft - secondsSince);
        hud = hud with
        {
            Money = state.Mode == GameModes.SearchAndDestroy ? own?.Money : null,
            BuyClock = buyLeft > 0 && own is { IsAlive: true } ? MatchState.Clock(buyLeft) : null,
            FreeLoadout = state.Mode is GameModes.TeamDeathmatch or GameModes.FreeForAll
                && state.Phase is MatchPhase.Live or MatchPhase.Warmup
                && own is { IsAlive: true },
        };

        return hud with { Result = ResultOf(state, report, own, hud, left) };
    }


    private static TeamStrip Strip(int team, Scoreboard? board, ScoreboardLine[] lines, ScoreboardLine? own, ModeReport report)
    {
        int score = board is null ? 0 : team == TeamRoster.Good ? board.GoodScore : board.BadScore;
        bool[] alive = [.. lines.Where(l => l.Team == team).OrderBy(l => own is { } me && l.Slot == me.Slot ? 0 : 1).ThenBy(l => l.Slot).Select(l => l.IsAlive)];
        // A side keeps its name whichever way it plays, as I.G.I. 2's do; its role is the player's own chip.
        string name = MatchState.TeamName(team).ToUpperInvariant();
        return new TeamStrip(team, name, score, alive, own is { } o && o.Team == team);
    }

    // The local player's own plant or defuse, and a teammate's. A client's own bar starts as its key goes down and goes as it
    // comes up, ahead of the server hearing of either; between reports each bar runs on by the time since.
    private static MatchHud Work(MatchHud hud, ModeReport report, ScoreboardLine[] lines, ScoreboardLine? own, double secondsSince, LocalUse? use, bool canWork)
    {
        bool reported = false;
        foreach (ModeWork work in report.Working)
        {
            string what = work.Defusing ? "DEFUSING" : "PLANTING";
            if (own is { } me && work.Slot == me.Slot)
            {
                reported = true;
                if (use is { Held: false })
                {
                    continue;
                }

                double progress = work.Seconds > 0 ? work.Progress + (secondsSince / work.Seconds) : work.Progress;
                if (use is { } local && work.Seconds > 0)
                {
                    progress = Math.Max(progress, local.Seconds / work.Seconds);
                }

                hud = hud with { Progress = (float)Math.Clamp(progress, 0, 1), ProgressLabel = $"{what} THE BOMB" };
            }
            else if (Array.FindIndex(lines, l => l.Slot == work.Slot) is var at and >= 0 && own is { } mine && lines[at].Team == mine.Team)
            {
                hud = hud with { TeamNote = $"{lines[at].Name.ToUpperInvariant()} IS {what}" };
            }
        }

        if (!reported && canWork && use is { Held: true } held)
        {
            bool defusing = report.BombAt is not null;
            BombSettings bomb = new();
            double seconds = defusing ? bomb.DefuseSeconds : bomb.PlantSeconds;
            hud = hud with
            {
                Progress = (float)Math.Clamp(held.Seconds / seconds, 0, 1),
                ProgressLabel = defusing ? "DEFUSING THE BOMB" : "PLANTING THE BOMB",
            };
        }

        return hud;
    }

    // What the local player can do where it stands: plant at a site it is in, carrying the bomb; defuse a bomb it is beside.
    private static ModeHint? HintFor(ModeReport report, ScoreboardLine own, Vector3d where, MpLayout? layout, bool working, bool bombInHand)
    {
        int team = own.Team;
        if (working || report.Attackers < 0)
        {
            return null;
        }

        if (report.Planted.Length == 0 && team == report.Attackers && report.Carrier == own.Slot && layout is not null)
        {
            foreach (MpPoint site in layout.Points)
            {
                if (site.Kind == MpPointKind.BombSite
                    && Vector3d.Distance(new Vector3d(site.Position[0], site.Position[1], site.Position[2]), where) <= site.Radius)
                {
                    return bombInHand
                        ? new ModeHint("HOLD", $"OR FIRE TO PLANT THE BOMB AT {site.Id}")
                        : new ModeHint("SWITCH TO THE BOMB", $"TO PLANT IT AT {site.Id}", Keyed: false);
                }
            }
        }

        if (report.BombAt is { } bomb && team != report.Attackers
            && Vector3d.Distance(bomb, where) <= new BombSettings().DefuseMetres * WorldUnits.PerMetre)
        {
            return new ModeHint("HOLD", "TO DEFUSE THE BOMB");
        }

        return null;
    }

    private static ResultCard? ResultOf(MatchState state, ModeReport report, ScoreboardLine? own, MatchHud hud, double? left)
    {
        int ownTeam = own?.Team ?? -1;
        bool teams = hud.HasTeams;
        int outcome = state.Winner < 0 ? 0
            : teams ? (ownTeam < 0 ? 0 : state.Winner == ownTeam ? 1 : -1)
            : own is { } me ? (state.Winner == me.Slot ? 1 : -1) : 0;
        string next = left is { } l ? string.Create(CultureInfo.InvariantCulture, $"NEXT MATCH IN {MatchState.Clock(l)}") : string.Empty;
        switch (state.Phase)
        {
            case MatchPhase.RoundOver:
                string title = state.Winner < 0 ? "ROUND DRAWN" : outcome == 0 ? $"{state.WinnerName.ToUpperInvariant()} WIN THE ROUND" : outcome > 0 ? "ROUND WON" : "ROUND LOST";
                return new ResultCard(title, ReasonOf(report.Reason, outcome), outcome);
            case MatchPhase.Intermission when teams:
                string score = string.Create(CultureInfo.InvariantCulture, $"{hud.Left?.Score ?? 0} – {hud.Right?.Score ?? 0}");
                return new ResultCard(
                    state.Winner < 0 ? "DRAW" : outcome == 0 ? $"{state.WinnerName.ToUpperInvariant()} WIN" : outcome > 0 ? "VICTORY" : "DEFEAT",
                    next.Length > 0 ? $"{score}  ·  {next}" : score,
                    outcome);
            case MatchPhase.Intermission:
                string place = own is null ? string.Empty : $"YOU PLACED {Ordinal(hud.OwnRank)}";
                return new ResultCard(
                    outcome > 0 ? "VICTORY" : state.WinnerName.Length > 0 ? $"{state.WinnerName.ToUpperInvariant()} WINS" : "DRAW",
                    string.Join("  ·  ", new[] { place, next }.Where(x => x.Length > 0)),
                    outcome);
            default:
                return null;
        }
    }

    private static string ReasonOf(RoundEnd reason, int outcome) => reason switch
    {
        RoundEnd.Eliminated => outcome < 0 ? "YOUR TEAM WAS ELIMINATED" : "ENEMY ELIMINATED",
        RoundEnd.TimeRanOut => "TIME RAN OUT",
        RoundEnd.Detonated => "TARGET DESTROYED",
        RoundEnd.Defused => "BOMB DEFUSED",
        _ => string.Empty,
    };

    private static string Ordinal(int n) =>
        string.Create(CultureInfo.InvariantCulture, $"{n}{(n % 100 is 11 or 12 or 13 ? "TH" : (n % 10) switch { 1 => "ST", 2 => "ND", 3 => "RD", _ => "TH" })}");
}
