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
        new(Mode.Id, Mode.Flow.Phase, Mode.Flow.Round, Mode.Flow.Winner, Mode.Flow.SecondsLeft(tick), WinnerName(Mode.Flow.Winner), Mode.Status(tick));

    // The winner as players read it: a team's name, or a player's.
    private string WinnerName(int winner) =>
        winner < 0 ? string.Empty
        : Mode.IsTeamMode ? MatchState.TeamName(winner)
        : Match.Players.TryGetValue(winner, out MatchPlayer? player) ? player.Name : string.Empty;

    /// <inheritdoc/>
    void IModeHost.Respawn(int slot)
    {
        _world.Respawn(slot);
        if (Match.Players.TryGetValue(slot, out MatchPlayer? player))
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

    // Where the match stands, when it changes and every few seconds besides, which a late joiner and a clock that drifted are
    // put right by.
    private void TellMatch(long tick)
    {
        // The mode's status line changes on its own (a fuse counting down): told when it does, at most each second.
        string status = Mode.Status(tick);
        if (Mode.Flow.Version == _toldFlow && tick % (TickRate * 5) != 0 && (status == _toldStatus || tick % TickRate != 0))
        {
            return;
        }

        _toldFlow = Mode.Flow.Version;
        _toldStatus = status;
        _writer.Reset();
        _writer.WriteBits((uint)SessionMessage.MatchState, 8);
        Mode.Flow.Write(_writer, Mode.Id, tick, WinnerName(Mode.Flow.Winner), status);
        _net.Broadcast(NetChannel.Reliable, _writer.AsSpan());
    }

    // The status line the clients were last told.
    private string _toldStatus = string.Empty;
}
