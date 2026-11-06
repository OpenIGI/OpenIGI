using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.Net;

namespace OpenIGI.Game.Multiplayer;

/// <content>The session as its mode sees it: the players, the match, respawns, and what the clients are told of the match.</content>
public sealed partial class ServerSession
{
    /// <summary>Gets the round of objectives, once a map that has them is played under the objective game, or <see langword="null"/>.</summary>
    public ObjectiveRound? Round => (Mode as ObjectiveMode)?.Round;

    /// <inheritdoc/>
    IReplicatedWorld IModeHost.World => _world;

    /// <inheritdoc/>
    IReadOnlyList<int> IModeHost.PlayerSlots => [.. _clients.Keys, HostSlot];

    /// <inheritdoc/>
    IReadOnlyList<int> IModeHost.ClientSlots => [.. _clients.Keys];

    /// <inheritdoc/>
    int IModeHost.SlotOf(int entity) => SlotOf(entity);

    /// <summary>Gets or sets the layout of the map being played, or <see langword="null"/> when it has none.</summary>
    public MpLayout? Layout { get; set; }

    /// <summary>Gets where the match stands now, as a client is told it.</summary>
    /// <param name="tick">The tick now.</param>
    /// <returns>The state.</returns>
    public MatchState CurrentMatch(long tick) =>
        new(Mode.Id, Mode.Flow.Phase, Mode.Flow.Round, Mode.Flow.Winner, Mode.Flow.SecondsLeft(tick), WinnerName(Mode.Flow.Winner), Mode.Report(tick));

    // The winner as players read it: a team's name, or a player's.
    private string WinnerName(int winner) =>
        winner < 0 ? string.Empty
        : Mode.IsTeamMode ? MatchState.TeamName(winner)
        : Match.Players.TryGetValue(winner, out MatchPlayer? player) ? player.Name : string.Empty;

    /// <inheritdoc/>
    void IModeHost.Respawn(int slot)
    {
        // The match's team first: a side swap or a scramble changed it, and the spawn the world picks follows it.
        if (Match.Players.TryGetValue(slot, out MatchPlayer? player))
        {
            _world.SetTeam(slot, player.Team);
        }

        _world.Respawn(slot);
        if (player is not null)
        {
            player.IsAlive = true;
            player.DiedAt = null;
        }
    }

    // Whether the mode has begun, and the flow's version the clients were last told.
    private bool _modeStarted;
    private int _toldFlow = -1;

    /// <summary>Gets the kills of late, as the host's own screen lists them.</summary>
    public KillFeed Feed { get; } = new();

    /// <inheritdoc/>
    void IModeHost.Announce(int victim, int killer)
    {
        MatchPlayer? by = killer < 0 ? null : Match.Players.GetValueOrDefault(killer);
        MatchPlayer? dead = Match.Players.GetValueOrDefault(victim);
        KillNote note = new(
            by is null || killer == victim ? string.Empty : by.Name,
            by?.Team ?? -1,
            dead?.Name ?? string.Empty,
            dead?.Team ?? -1);
        Feed.Add(note, _now);
        _writer.Reset();
        _writer.WriteBits((uint)SessionMessage.KillNote, 8);
        note.Write(_writer);
        _net.Broadcast(NetChannel.Reliable, _writer.AsSpan());
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The server's level first, then every client told on the reliable line: whatever the server sends after it -- the level's
    /// state, the snapshots -- reaches a client after its own level went back too.
    /// </remarks>
    void IModeHost.ResetLevel()
    {
        // The first round's start is the level's: settled from its load (its own first ticks run, an arena made of it), the
        // state every later round goes back to.
        if (!_levelMarked)
        {
            _world.MarkLevelStart();
            _levelMarked = true;
            return;
        }

        _world.ResetToLevelStart();
        _writer.Reset();
        _writer.WriteBits((uint)SessionMessage.LevelReset, 8);
        _net.Broadcast(NetChannel.Reliable, _writer.AsSpan());
    }

    private readonly List<CombatNote> _hostCombat = [];

    /// <summary>Takes the hits the listen server's own player scored or took since the last call, for its HUD.</summary>
    /// <returns>The hits.</returns>
    public IReadOnlyList<CombatNote> TakeHostCombat()
    {
        if (_hostCombat.Count == 0)
        {
            return [];
        }

        CombatNote[] notes = [.. _hostCombat];
        _hostCombat.Clear();
        return notes;
    }

    // Each hit to the two in it -- the shooter and the one hit -- and to no one else: what a player learns of a fight is its own.
    private void TellCombat()
    {
        foreach (CombatNote note in _world.TakeCombat())
        {
            int shooter = note.Attacker >= 0 ? SlotOf(note.Attacker) : -1;
            int victim = note.Victim >= 0 ? SlotOf(note.Victim) : -1;
            foreach (int slot in shooter == victim ? [shooter] : (int[])[shooter, victim])
            {
                if (slot == HostSlot)
                {
                    _hostCombat.Add(note);
                }
                else if (slot >= 0 && _clients.ContainsKey(slot))
                {
                    _writer.Reset();
                    _writer.WriteBits((uint)SessionMessage.Combat, 8);
                    note.Write(_writer);
                    _net.Send(slot, NetChannel.Reliable, _writer.AsSpan());
                }
            }
        }

        if (_hostCombat.Count > 64)
        {
            _hostCombat.RemoveRange(0, _hostCombat.Count - 64);
        }
    }

    // Where the match stands, when it changes and every few seconds besides, which a late joiner and a clock that drifted are
    // put right by.
    private void TellMatch(long tick)
    {
        // The mode's report: told as soon as it changes (a plant, a defuse's progress a tenth at a time); the fuse runs down on
        // each client's own clock, put right with the rest every few seconds.
        ModeReport report = Mode.Report(tick);
        string key = report.Key;
        if (Mode.Flow.Version == _toldFlow && tick % (TickRate * 5) != 0 && key == _toldReport)
        {
            return;
        }

        _toldFlow = Mode.Flow.Version;
        _toldReport = key;
        _writer.Reset();
        _writer.WriteBits((uint)SessionMessage.MatchState, 8);
        Mode.Flow.Write(_writer, Mode.Id, tick, WinnerName(Mode.Flow.Winner), report);
        _net.Broadcast(NetChannel.Reliable, _writer.AsSpan());
    }

    // The report's key the clients were last told.
    private string _toldReport = string.Empty;
}
