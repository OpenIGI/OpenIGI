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
    /// <summary>The longest a sync connection is kept, in seconds.</summary>
    public const double SyncSeconds = 300;

    // A piece goes only while fewer of this client's reliable fragments wait than this: the line is kept for play.
    private const int FileFragmentsInFlight = 24;

    // For a player in the match, fetching the next map's files while it plays: fewer still, so play never waits behind them.
    private const int PlayFileFragmentsInFlight = 6;

    // The times a client may ask for the same file of the same list.
    private const int MaxAsksPerFile = 3;

    // The sync connections, by slot, and when each came.
    private readonly Dictionary<int, double> _syncing = [];

    // What each client is being sent, by slot.
    private readonly Dictionary<int, FileSend> _sends = [];

    // The map whose list went ahead of it (Foresee), so a vote's leader changing back and forth sends it once.
    private string? _foreseen;

    // The lists made, by map: a map's files are read and hashed once.
    private readonly Dictionary<string, SessionFileList> _lists = new(StringComparer.Ordinal);

    // The pieces of each file sent in pieces, by the file's hash: cut once whoever asks.
    private readonly Dictionary<string, FileChunks> _chunks = new(StringComparer.Ordinal);

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
            list = Files.Rules.Build(map, Files);

            // An empty list is not kept: one made before the game knew its missions would otherwise stand for the map for good.
            if (list.Files.Count > 0)
            {
                _lists[map] = list;
            }
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

    /// <summary>Sends every player the list of the map likely next, so a player whose files differ fetches them while it plays.</summary>
    /// <param name="map">The map.</param>
    /// <remarks>
    /// The list that goes with the word that the server is going (<see cref="AnnounceGoing"/>) then finds the files in place, and
    /// the change of map waits for nothing. Each player is sent it once it has loaded the map it plays, so it never stands in
    /// for that map's own list, and never while it is still fetching anything.
    /// </remarks>
    public void Foresee(string map)
    {
        ArgumentNullException.ThrowIfNull(map);
        _foreseen = Files is null ? null : map;
    }

    // The foreseen map's list to each player that has loaded the map it plays and is fetching nothing, once.
    private void SendForeseen()
    {
        if (_foreseen is not { } map)
        {
            return;
        }

        foreach ((int slot, ClientState client) in _clients)
        {
            bool loaded = client.ReadyFor is null ? _going == 0 : client.ReadyFor == _going;
            if (loaded && (!_sends.TryGetValue(slot, out FileSend? send) || (send.List.Map != map && send.Wanted.Count == 0)))
            {
                SendFileList(slot, map);
            }
        }
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

    // A client's ask about files of the list it was sent: the files it lacks, or the pieces of one it lacks.
    private void ReadFileMessage(int slot, SessionMessage kind, ref BitReader reader)
    {
        if (kind == SessionMessage.FileWant)
        {
            ReadFileWant(slot, ref reader);
        }
        else if (kind == SessionMessage.ChunkWant && ChunkWant.Read(ref reader) is { } ask)
        {
            ReadChunkWant(slot, ask);
        }
    }

    // The files of the list it was sent that a client lacks: a small one queued to go whole, a larger one's table first.
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

            if (index < send.List.Files.Count && send.Asks[index]++ < MaxAsksPerFile)
            {
                send.Queue((int)index, send.List.Files[(int)index].Size <= ContentChunks.MaxLength ? 0 : FileSend.Table);
            }
        }
    }

    // The pieces of a file a client lacks, of a table it was sent: each queued once, however often it asks.
    private void ReadChunkWant(int slot, ChunkWant ask)
    {
        if (!_sends.TryGetValue(slot, out FileSend? send)
            || send.List.Map != ask.Map
            || ask.Index >= send.List.Files.Count
            || !send.Tables.TryGetValue(ask.Index, out FileChunks? table)
            || send.Asks[ask.Index]++ >= MaxAsksPerFile)
        {
            return;
        }

        foreach ((int start, int count) in ask.Runs)
        {
            for (int chunk = start; chunk < start + count && chunk < table.Chunks.Count; chunk++)
            {
                send.Queue(ask.Index, chunk);
            }
        }
    }

    // A file's pieces, cut once per content and kept by its hash, for every client and every map that lists it.
    private FileChunks ChunksOf(SessionFile file, byte[] data)
    {
        string key = Convert.ToHexString(file.Hash);
        if (!_chunks.TryGetValue(key, out FileChunks? chunks))
        {
            chunks = new FileChunks(ContentChunks.Cut(data));
            _chunks[key] = chunks;
        }

        return chunks;
    }

    // Each client's next tables and pieces, while its line and the server's have room for them; sync connections that have
    // stayed too long let go.
    private void PumpFiles(double now)
    {
        SendForeseen();
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

            int inFlight = _clients.ContainsKey(slot) ? PlayFileFragmentsInFlight : FileFragmentsInFlight;
            while (send.Wanted.Count > 0 && line.PendingReliableCount < inFlight)
            {
                (int index, int chunk) = send.Wanted.Peek();
                SessionFile file = send.List.Files[index];
                if (send.DataIndex != index)
                {
                    send.Data = Files?.Read(file.Path);
                    send.DataIndex = index;
                }

                if (send.Data is null || !file.Matches(send.Data))
                {
                    // Changed or gone since the list was made: nothing the client could check, so nothing of it is sent.
                    send.Drop(index);
                    continue;
                }

                _writer.Reset();
                if (chunk == FileSend.Table)
                {
                    FileChunks table = ChunksOf(file, send.Data);
                    new FileChunkTable(send.List.Map, index, table.Chunks).Write(_writer);
                    send.Tables[index] = table;
                }
                else if (PieceOf(send, file, index, chunk) is { } piece)
                {
                    (bool deflated, byte[] packed) = ChunkCodec.Pack(piece.Span);
                    new FilePiece(send.List.Map, index, chunk, deflated, packed).Write(_writer);
                }
                else
                {
                    send.Next();
                    continue;
                }

                if (!_fileLine.TrySpend(now, Math.Max(_writer.AsSpan().Length, 1)))
                {
                    return;
                }

                _net.Send(slot, NetChannel.Reliable, _writer.AsSpan());
                FileBytesSent += _writer.AsSpan().Length;
                send.Next();
            }

            if (send.Wanted.Count == 0)
            {
                // Nothing left to send it: the file's bytes are let go until it asks again.
                send.Data = null;
                send.DataIndex = -1;
            }
        }
    }

    // A piece's bytes: a small file whole, or a piece of a table the client was sent.
    private static ReadOnlyMemory<byte>? PieceOf(FileSend send, SessionFile file, int index, int chunk)
    {
        if (file.Size <= ContentChunks.MaxLength)
        {
            return chunk == 0 ? send.Data : null;
        }

        return send.Tables.TryGetValue(index, out FileChunks? table) && chunk < table.Chunks.Count
            ? send.Data.AsMemory(table.Offsets[chunk], table.Chunks[chunk].Length)
            : null;
    }

    // A client gone, or a sync connection let go: what it was being sent is dropped.
    private void ForgetFiles(int slot)
    {
        _syncing.Remove(slot);
        _sends.Remove(slot);
    }

    // A file's pieces, with where each begins.
    private sealed class FileChunks
    {
        public FileChunks(IReadOnlyList<ContentChunk> chunks)
        {
            Chunks = chunks;
            Offsets = new int[chunks.Count];
            for (int i = 1; i < chunks.Count; i++)
            {
                Offsets[i] = Offsets[i - 1] + chunks[i - 1].Length;
            }
        }

        public IReadOnlyList<ContentChunk> Chunks { get; }

        public int[] Offsets { get; }
    }

    // What one client is being sent: the list it may ask from, what it asked, the tables it was sent, and the file going now.
    private sealed class FileSend(SessionFileList list)
    {
        // A queued file's table rather than one of its pieces.
        public const int Table = -1;

        private readonly HashSet<(int Index, int Chunk)> _queued = [];

        public SessionFileList List { get; } = list;

        public int[] Asks { get; } = new int[list.Files.Count];

        public Queue<(int Index, int Chunk)> Wanted { get; } = new();

        public Dictionary<int, FileChunks> Tables { get; } = [];

        public byte[]? Data { get; set; }

        public int DataIndex { get; set; } = -1;

        public void Queue(int index, int chunk)
        {
            if (_queued.Add((index, chunk)))
            {
                Wanted.Enqueue((index, chunk));
            }
        }

        public void Next() => _queued.Remove(Wanted.Dequeue());

        // Everything queued of a file, dropped.
        public void Drop(int index)
        {
            int left = Wanted.Count;
            for (int i = 0; i < left; i++)
            {
                (int Index, int Chunk) item = Wanted.Dequeue();
                if (item.Index == index)
                {
                    _queued.Remove(item);
                }
                else
                {
                    Wanted.Enqueue(item);
                }
            }
        }
    }
}
