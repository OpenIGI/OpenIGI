using OpenIGI.Net;

namespace OpenIGI.Game.Multiplayer;

/// <content>
/// The server's files: the list a map is played from, sent to every client so its own can be checked against it, and the
/// pieces of whichever it asks for.
/// </content>
/// <remarks>
/// <para>
/// A client comes for them twice. Before it joins, on a connection of its own that asks only to sync (<see cref="JoinRequest.Sync"/>):
/// sent the list at once, with no proof of who it is to give, it is given what differs and then leaves, and only then joins
/// and loads, so even the tables a game reads once are the server's. And while it plays, as the server goes on to another map,
/// whose list goes with the word that it is going.
/// </para>
/// <para>
/// What a client can take is bounded on every side, as a line anyone may open: only the files of a list it was sent, each
/// asked for a few times at most, pieces sent only while its line has room, a share of the server's line for files across
/// every client, and a sync connection that is let go after a time whatever it is doing.
/// </para>
/// </remarks>
public sealed partial class ServerSession
{
    /// <summary>The bytes of one piece of a file.</summary>
    public const int FilePieceSize = 8 * 1024;

    /// <summary>The longest a sync connection is kept, in seconds.</summary>
    public const double SyncSeconds = 300;

    // A piece goes only while fewer of this client's reliable fragments wait than this: the line is kept for play.
    private const int FileFragmentsInFlight = 24;

    // The times a client may ask for the same file of the same list.
    private const int MaxAsksPerFile = 3;

    // The sync connections, by slot, and when each came.
    private readonly Dictionary<int, double> _syncing = [];

    // What each client is being sent, by slot.
    private readonly Dictionary<int, FileSend> _sends = [];

    // The lists made, by map: a map's files are read and hashed once.
    private readonly Dictionary<string, SessionFileList> _lists = new(StringComparer.Ordinal);

    // The server's line for files, across every client: four megabytes a second, a megabyte at once.
    private readonly TokenBucket _fileLine = new(4 * 1024 * 1024, 1024 * 1024);

    /// <summary>Gets or sets where the server's files are read, or <see langword="null"/> to compare and send none.</summary>
    public ISessionFileSource? Files { get; set; }

    /// <summary>Gets the bytes of files sent so far.</summary>
    public long FileBytesSent { get; private set; }

    /// <summary>Gets the sync connections open now.</summary>
    public int SyncingCount => _syncing.Count;

    // The list of a map, made once; empty when the server has no files to share.
    private SessionFileList ListFor(string map)
    {
        if (Files is null)
        {
            return new SessionFileList(map, []);
        }

        if (!_lists.TryGetValue(map, out SessionFileList? list))
        {
            list = GameplayFiles.Build(map, Files);
            _lists[map] = list;
        }

        return list;
    }

    // A connection that asked only for the files: sent the list of the map now played, nothing else.
    private bool TryBeginSync(int slot, JoinRequest? request)
    {
        if (request?.Sync != true)
        {
            return false;
        }

        // No more of them at once than a quarter of the server's places: each holds one a player could have.
        if (_syncing.Count >= Math.Max(1, _net.MaxClients / 4))
        {
            _net.Disconnect(slot, "the server is busy sending files; try again in a moment");
            return true;
        }

        _syncing[slot] = _now;
        SendFileList(slot, Map);
        return true;
    }

    // The list of a map to one client, which becomes the one it may ask from.
    private void SendFileList(int slot, string map)
    {
        SessionFileList list = ListFor(map);
        _sends[slot] = new FileSend(list);
        _writer.Reset();
        list.Write(_writer);
        _net.Send(slot, NetChannel.Reliable, _writer.AsSpan());
    }

    // The files of the list it was sent that a client lacks, queued to be sent.
    private void ReadFileWant(int slot, ref BitReader reader)
    {
        string map = reader.ReadString(64);
        uint count = reader.ReadVarUInt();
        if (reader.IsOverflowed || count > SessionFileList.MaxFiles || !_sends.TryGetValue(slot, out FileSend? send) || send.List.Map != map)
        {
            return;
        }

        for (int i = 0; i < count; i++)
        {
            uint index = reader.ReadVarUInt();
            if (reader.IsOverflowed)
            {
                return;
            }

            if (index < send.List.Files.Count && send.Asks[index]++ < MaxAsksPerFile && !send.Wanted.Contains((int)index))
            {
                send.Wanted.Enqueue((int)index);
            }
        }
    }

    // Each client's next pieces, while its line and the server's have room for them; sync connections that have stayed too
    // long let go.
    private void PumpFiles(double now)
    {
        foreach ((int slot, double since) in _syncing.ToArray())
        {
            if (now - since > SyncSeconds)
            {
                _net.Disconnect(slot, "the files took too long to send");
                ForgetFiles(slot);
            }
        }

        foreach ((int slot, FileSend send) in _sends)
        {
            if (_net.GetConnection(slot) is not { } line)
            {
                continue;
            }

            while (send.Wanted.Count > 0 && line.PendingReliableCount < FileFragmentsInFlight)
            {
                int index = send.Wanted.Peek();
                SessionFile file = send.List.Files[index];
                send.Data ??= Files?.Read(file.Path);
                if (send.Data is null || !file.Matches(send.Data))
                {
                    // Changed or gone since the list was made: nothing the client could check, so nothing is sent.
                    send.Wanted.Dequeue();
                    send.Data = null;
                    send.Offset = 0;
                    continue;
                }

                int length = (int)Math.Min(FilePieceSize, send.Data.Length - send.Offset);
                if (!_fileLine.TrySpend(now, Math.Max(length, 1)))
                {
                    return;
                }

                _writer.Reset();
                _writer.WriteBits((uint)SessionMessage.FilePart, 8);
                _writer.WriteString(send.List.Map, 64);
                _writer.WriteVarUInt((uint)index);
                _writer.WriteVarUInt((uint)send.Offset);
                _writer.WriteBytes(send.Data.AsSpan((int)send.Offset, length));
                _net.Send(slot, NetChannel.Reliable, _writer.AsSpan());
                send.Offset += length;
                FileBytesSent += length;
                if (send.Offset >= send.Data.Length)
                {
                    send.Wanted.Dequeue();
                    send.Data = null;
                    send.Offset = 0;
                }
            }
        }
    }

    // A client gone, or a sync connection let go: what it was being sent is dropped.
    private void ForgetFiles(int slot)
    {
        _syncing.Remove(slot);
        _sends.Remove(slot);
    }

    // What one client is being sent: the list it may ask from, what it asked, and where the file going now is.
    private sealed class FileSend(SessionFileList list)
    {
        public SessionFileList List { get; } = list;

        public int[] Asks { get; } = new int[list.Files.Count];

        public Queue<int> Wanted { get; } = new();

        public byte[]? Data { get; set; }

        public long Offset { get; set; }
    }
}
