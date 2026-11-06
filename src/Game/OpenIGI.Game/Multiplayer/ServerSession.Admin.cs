using OpenIGI.Game.Multiplayer.Modes;

namespace OpenIGI.Game.Multiplayer;

/// <content>What a server's operator does to the session: bans kept beyond it, a player's key, a mode begun again.</content>
public sealed partial class ServerSession
{
    /// <summary>Gets or sets what says whether a player's key is banned from the server, and why; <see langword="null"/> for no list.</summary>
    /// <remarks>Asked as a player joins, with its verified key's id: the reason it is told, or <see langword="null"/> to let it in.</remarks>
    public Func<string, string?>? Banned { get; set; }

    /// <summary>Gets a client's verified key's id, which bans and rejoins follow, or <see langword="null"/>.</summary>
    /// <param name="slot">The client's slot.</param>
    /// <returns>The id.</returns>
    public string? PlayerIdOf(int slot) => _clients.TryGetValue(slot, out ClientState? client) ? client.PlayerId : null;

    /// <summary>Gets a client's round trip in milliseconds, or 0.</summary>
    /// <param name="slot">The client's slot.</param>
    /// <returns>The round trip.</returns>
    public int PingOf(int slot) => PingOfSlot(slot);

    /// <summary>Puts a client out with a reason it is told, and keeps its key out while the session stands.</summary>
    /// <param name="slot">The client's slot.</param>
    /// <param name="reason">What it is told.</param>
    public void Remove(int slot, string reason)
    {
        if (!_clients.Remove(slot, out ClientState? client))
        {
            return;
        }

        _barred[client.PlayerId] = client.Name;
        _world.RemovePlayer(slot);
        Match.Leave(slot);
        _net.Disconnect(slot, reason);
    }

    /// <summary>Begins the mode again on the next tick: a new map's match, or a new mode put in.</summary>
    /// <param name="mode">The mode to play now, or <see langword="null"/> to begin the same one again.</param>
    public void RestartMode(IGameMode? mode = null)
    {
        if (mode is not null)
        {
            Mode = mode;
        }

        _modeStarted = false;
        _toldFlow = -1;
    }

    /// <summary>Gets or sets what answers the remote console: given the asking client's slot, the password it gave and its command,
    /// the answer's lines; <see langword="null"/> for a server with no remote console.</summary>
    public Func<int, string, string, IReadOnlyList<string>>? Rcon { get; set; }

    // A client's remote command: answered as the server's console answers, never faster than its chat budget allows.
    private void ReadRcon(int slot, ClientState client, ref Net.BitReader reader)
    {
        string password = reader.ReadString(64);
        string command = reader.ReadString(256);
        if (reader.IsOverflowed || !client.Chat.TrySpend(_now))
        {
            return;
        }

        IReadOnlyList<string> answer = Rcon is { } rcon ? rcon(slot, password, command) : ["This server has no remote console."];
        _writer.Reset();
        _writer.WriteBits((uint)SessionMessage.Rcon, 8);
        int count = Math.Min(answer.Count, 64);
        _writer.WriteBits((uint)count, 8);
        for (int i = 0; i < count; i++)
        {
            _writer.WriteString(answer[i], 200);
        }

        _net.Send(slot, Net.NetChannel.Reliable, _writer.AsSpan());
    }

    /// <summary>Reads what has arrived without acting on it: between ticks, so each packet is timed as it comes and not when the next tick reads it.</summary>
    /// <param name="now">The clock, in seconds.</param>
    /// <remarks>The messages wait, queued, for <see cref="Receive"/>: the commands still reach the level on the tick.</remarks>
    public void Poll(double now) => _net.Update(now);

    // The reason a joining key is turned away, or null to let it in.
    private string? TurnedAway(string playerId) =>
        _barred.ContainsKey(playerId) ? "the host removed you from this room" : Banned?.Invoke(playerId);
}
