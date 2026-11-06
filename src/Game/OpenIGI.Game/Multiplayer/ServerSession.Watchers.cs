using OpenIGI.Net;

namespace OpenIGI.Game.Multiplayer;

/// <content>
/// Watchers: clients let in to be sent everything a player is -- each snapshot, the level, the board, the talk -- with no player
/// of their own, no team and no place in the match. A recorder making a demo of the session is one.
/// </content>
public sealed partial class ServerSession
{
    /// <summary>The entity a watcher is welcomed as: one no player is given.</summary>
    public const int WatcherEntity = 65000;

    // The connected clients that asked to watch, until they are let in.
    private readonly HashSet<int> _watching = [];

    // A join that asked only to watch, noted for when its proof is in.
    private void NoteWatch(int slot, JoinRequest? request)
    {
        if (request?.Watch == true)
        {
            _watching.Add(slot);
        }
        else
        {
            _watching.Remove(slot);
        }
    }

    // A watcher let in: kept as a client for all it is sent, welcomed as nobody.
    private void Watch(int slot, string name, string playerId)
    {
        _clients[slot] = new ClientState(name, WatcherEntity, playerId) { Watching = true };
        Welcome(slot, WatcherEntity);
    }

    // The welcome: the client's entity, the tick rate, the map, the level's epoch and the mode.
    private void Welcome(int slot, int entity)
    {
        _writer.Reset();
        _writer.WriteBits((uint)SessionMessage.Welcome, 8);
        _writer.WriteVarUInt((uint)entity);
        _writer.WriteVarUInt((uint)TickRate);
        _writer.WriteString(Map, 64);
        _writer.WriteBits(_epoch, 8);
        _writer.WriteString(Mode.Id, 16);
        _net.Send(slot, NetChannel.Reliable, _writer.AsSpan());
    }
}
