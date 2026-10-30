namespace OpenIGI.Game.Multiplayer;

/// <summary>One objective of a round as the round sees it: its order, its team, and whether it has been won or failed.</summary>
public interface IRoundObjective
{
    /// <summary>Gets its place in the order objectives are taken, from one.</summary>
    int Order { get; }

    /// <summary>Gets the team whose objective it is: the attackers while it is current.</summary>
    int Team { get; }

    /// <summary>Gets whether its success holds now.</summary>
    /// <returns><see langword="true"/> when it is won.</returns>
    bool IsCompleted();

    /// <summary>Gets whether its failure holds now.</summary>
    /// <returns><see langword="true"/> when it is failed.</returns>
    bool IsFailed();
}

/// <summary>What a round's tick decided.</summary>
public enum RoundEvent
{
    /// <summary>Nothing.</summary>
    None,

    /// <summary>The current objective was resolved, won or failed, and the next is current.</summary>
    ObjectiveResolved,

    /// <summary>The mission was decided: the last objective resolved, or the time ran out.</summary>
    MissionOver,

    /// <summary>The pause after the mission is over: the round starts again.</summary>
    Restart,
}

/// <summary>A multiplayer map's round: its objectives taken in order against the clock, and the mission decided by them.</summary>
/// <remarks>
/// <para>
/// I.G.I. 2's <c>Network_Mission</c> and its <c>Network_Objective</c> children (netmission.c). Only the objective whose order is
/// current is judged (<c>NetObjective_RunHandler</c>). Its success and its failure are each latched the tick they first hold;
/// either resolves it: a success pays its team and the player who did it, a failure pays the other team, and the next order
/// becomes current with its team attacking (<c>NetMission_SetObjectiveComplete</c>). With no next order, or the mission timer
/// run out, the mission is over (<c>NetMission_RunHandler</c>): the attackers of the first objective win it only when every
/// objective was won, and the winners score a point. Six hundred ticks later the round starts again from the first.
/// </para>
/// <para>
/// The mission timer is <see cref="MultiplayerRules.ObjectiveMaxMinutes"/> for the whole round.
/// </para>
/// </remarks>
public sealed class ObjectiveRound
{
    /// <summary>The ticks between a mission's end and the round's restart, <c>nRestartTimer = 600</c>.</summary>
    public const int RestartTicks = 600;

    private readonly IReadOnlyList<IRoundObjective> _objectives;
    private readonly Match _match;
    private readonly int _tickRate;
    private readonly HashSet<IRoundObjective> _won = [];
    private readonly HashSet<IRoundObjective> _failed = [];
    private long _endTick;
    private int _restart;

    /// <summary>Initializes a new instance of the <see cref="ObjectiveRound"/> class.</summary>
    /// <param name="objectives">The map's objectives.</param>
    /// <param name="match">The match, which the round pays and scores.</param>
    /// <param name="tickRate">The world's ticks per second.</param>
    public ObjectiveRound(IReadOnlyList<IRoundObjective> objectives, Match match, int tickRate)
    {
        _objectives = objectives ?? throw new ArgumentNullException(nameof(objectives));
        _match = match ?? throw new ArgumentNullException(nameof(match));
        _tickRate = tickRate;
    }

    /// <summary>Gets the order of the objective being played, or zero between missions.</summary>
    public int Current { get; private set; }

    /// <summary>Gets the team attacking the current objective, or -1.</summary>
    public int AttackingTeam { get; private set; } = -1;

    /// <summary>Gets the team that won the last mission, or -1.</summary>
    public int LastWinner { get; private set; } = -1;

    /// <summary>Gets the mission's seconds left.</summary>
    /// <param name="tick">The tick now.</param>
    /// <returns>The seconds, never below zero.</returns>
    public double SecondsLeft(long tick) => Math.Max(0, (double)(_endTick - tick) / _tickRate);

    /// <summary>Starts a round from the first objective.</summary>
    /// <param name="tick">The tick it starts on.</param>
    public void Start(long tick)
    {
        _won.Clear();
        _failed.Clear();
        _restart = 0;
        _endTick = tick + ((long)_match.Rules.ObjectiveMaxMinutes * 60 * _tickRate);
        Advance(0);
    }

    /// <summary>Runs one tick of the round.</summary>
    /// <param name="tick">The tick just run.</param>
    /// <param name="resolver">The slot of the player to credit with an objective won this tick, or -1.</param>
    /// <returns>What the tick decided.</returns>
    public RoundEvent Tick(long tick, int resolver = -1)
    {
        if (_restart > 0)
        {
            if (--_restart == 0)
            {
                Start(tick);
                return RoundEvent.Restart;
            }

            return RoundEvent.None;
        }

        if (Current == 0)
        {
            return RoundEvent.None;
        }

        if (tick >= _endTick)
        {
            return EndMission();
        }

        IRoundObjective? objective = Find(Current);
        if (objective is null)
        {
            return EndMission();
        }

        bool won = objective.IsCompleted();
        bool failed = !won && objective.IsFailed();
        if (!won && !failed)
        {
            return RoundEvent.None;
        }

        if (won)
        {
            _won.Add(objective);
            _match.ObjectiveWon(objective.Team, resolver);
        }
        else
        {
            _failed.Add(objective);
            _match.ObjectiveWon(Other(objective.Team), -1);
        }

        return Advance(Current) ? RoundEvent.ObjectiveResolved : EndMission();
    }

    private RoundEvent EndMission()
    {
        int attackers = _objectives.OrderBy(static o => o.Order).FirstOrDefault()?.Team ?? TeamRoster.Good;
        LastWinner = _objectives.All(_won.Contains) ? attackers : Other(attackers);
        _match.RoundOver(LastWinner);
        Current = 0;
        AttackingTeam = -1;
        _match.DefendingTeam = -1;
        _restart = RestartTicks;
        return RoundEvent.MissionOver;
    }

    // NetMission_SetObjectiveComplete: the objective of the next order becomes current, its team attacking.
    private bool Advance(int from)
    {
        if (Find(from + 1) is not { } next)
        {
            return false;
        }

        Current = next.Order;
        AttackingTeam = next.Team;
        _match.DefendingTeam = Other(next.Team);
        return true;
    }

    private IRoundObjective? Find(int order)
    {
        foreach (IRoundObjective objective in _objectives)
        {
            if (objective.Order == order)
            {
                return objective;
            }
        }

        return null;
    }

    private static int Other(int team) => team == TeamRoster.Good ? TeamRoster.Bad : TeamRoster.Good;
}
