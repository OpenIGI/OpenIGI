using OpenIGI.Core.Diagnostics;

namespace OpenIGI.Game.Multiplayer;

/// <summary>
/// A client's side of a server's files: each list the server sends checked against the files this game plays, those that
/// differ fetched, checked and kept, and every one of the server's that differs from this game's put in its place.
/// </summary>
/// <remarks>
/// <para>
/// Only what differs travels. A small file comes whole; a larger one's table of pieces comes first
/// (<see cref="ContentChunks"/>), this game's own copy is cut the same way, and only the pieces it lacks are asked for --
/// deflated where that is smaller. A retail copy joining a host with a few meshes changed in an archive fetches those
/// meshes' pieces, not the archive.
/// </para>
/// <para>
/// A file put together is kept by its hash under <see cref="CacheDirectory"/>, never in the game's own folder, so the next
/// server with the same file costs nothing and the installation is left as it was. The folder is kept to
/// <see cref="MaxCacheBytes"/>, the longest unused going first. What is put in place lasts for the session:
/// <see cref="Pins"/> is what the file system reads in place of the installation's own, and is emptied as the session goes.
/// </para>
/// <para>
/// Nothing a server sends is trusted. A list may name only paths its map plays from (<see cref="GameplayFileRules.IsListable"/>),
/// within a size; a table must add up to the file; a piece must be the length and hash its table gave; and a file is taken
/// only if the whole of it hashes to what the list said.
/// </para>
/// </remarks>
public sealed class SessionFileSync
{
    /// <summary>The most bytes of servers' files kept between sessions.</summary>
    public const long MaxCacheBytes = 512L * 1024 * 1024;

    private const string LogCategory = "Files";

    private readonly ISessionFileSource _local;
    private readonly IOpenIgiLogger _logger;
    private readonly Dictionary<string, string> _pins = new(StringComparer.Ordinal);
    private readonly Dictionary<int, Incoming> _incoming = [];
    private SessionFileList? _list;

    /// <summary>Initializes a new instance of the <see cref="SessionFileSync"/> class.</summary>
    /// <param name="local">This game's files, as it plays them now.</param>
    /// <param name="cacheDirectory">Where fetched files are kept, by hash.</param>
    /// <param name="logger">Where what happens is told.</param>
    public SessionFileSync(ISessionFileSource local, string cacheDirectory, IOpenIgiLogger? logger = null)
    {
        _local = local ?? throw new ArgumentNullException(nameof(local));
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        CacheDirectory = cacheDirectory;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Gets where fetched files are kept.</summary>
    public string CacheDirectory { get; }

    /// <summary>Gets the server's files put in place of this game's: each path, and the kept file read for it.</summary>
    public IReadOnlyDictionary<string, string> Pins => _pins;

    /// <summary>Gets the map of the list last checked, or <see langword="null"/> before one came.</summary>
    public string? Map => _list?.Map;

    /// <summary>Gets whether a list has come and every file of it is this game's or in place.</summary>
    public bool Settled => _list is not null && _incoming.Count == 0 && Failure is null;

    /// <summary>Gets why the files could not be made the server's, or <see langword="null"/>.</summary>
    public string? Failure { get; private set; }

    /// <summary>Gets the bytes of the server's that have come, and those to come in all; the whole file until its table says.</summary>
    public (long Received, long Total) Progress =>
        (_incoming.Values.Sum(static i => i.Received), _incoming.Values.Sum(static i => i.ToFetch));

    /// <summary>Gets the bytes of files this game had of its own and so did not fetch, across every file put together.</summary>
    public long BytesReused { get; private set; }

    /// <summary>Gets the bytes of pieces fetched, as put together; deflated on the line, they were fewer.</summary>
    public long BytesFetched { get; private set; }

    /// <summary>Gets whether a file put in place since <see cref="TakeChanges"/> was last called is one read once a game.</summary>
    public bool SharedChanged { get; private set; }

    /// <summary>Gets whether any file was put in place, or taken out of place, since <see cref="TakeChanges"/> was last called.</summary>
    public bool Changed { get; private set; }

    /// <summary>Takes what changed since the last call.</summary>
    /// <returns>Whether anything did, and whether any of it is read once a game.</returns>
    public (bool Changed, bool Shared) TakeChanges()
    {
        (bool, bool) changes = (Changed, SharedChanged);
        Changed = false;
        SharedChanged = false;
        return changes;
    }

    /// <summary>Checks a list the server sent, and asks for what differs.</summary>
    /// <param name="list">The list.</param>
    /// <param name="want">Sends the request for files of the list, by place in it.</param>
    public void Accept(SessionFileList list, Action<string, IReadOnlyCollection<int>> want)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(want);
        _list = list;
        _incoming.Clear();
        Failure = null;

        // A server names only the files of the map's folders and those every map plays from, each one this game has: never
        // a file of another kind, of another map, or one the installation lacks, which no level of it reads.
        if (list.Files.FirstOrDefault(f => !_local.Rules.IsListable(list.Map, f.Path)) is { } stray)
        {
            Fail($"its list names {stray.Path}, which isn't a file map {list.Map} plays from");
            return;
        }

        List<int> wanted = [];
        for (int i = 0; i < list.Files.Count; i++)
        {
            SessionFile file = list.Files[i];
            if (_pins.TryGetValue(file.Path, out string? pinned) && CachedMatches(pinned, file))
            {
                continue;
            }

            if (_local.Read(file.Path) is not { } own)
            {
                Fail($"its list names {file.Path}, which this game doesn't have");
                return;
            }

            if (file.Matches(own))
            {
                Unpin(file.Path);
                continue;
            }

            // What comes must be a file of the same kind as this game's own: the same signature at its start.
            if (!SameKind(own, file))
            {
                Fail($"its {file.Path} is a different kind of file from this game's");
                return;
            }

            string cached = CachePath(file);
            if (CachedMatches(cached, file, Signature(own)))
            {
                Touch(cached);
                Pin(file.Path, cached);
                continue;
            }

            _incoming[i] = new Incoming(file, own);
            wanted.Add(i);
        }

        if (wanted.Count > 0)
        {
            _logger.Information(
                LogCategory,
                $"{wanted.Count} of the {list.Files.Count} files map {list.Map} plays from differ here; fetching only their differing pieces: "
                    + string.Join(", ", wanted.Take(6).Select(i => list.Files[i].Path)) + (wanted.Count > 6 ? ", ..." : string.Empty) + ".");
            want(list.Map, wanted);
        }
        else
        {
            _logger.Information(LogCategory, $"The {list.Files.Count} files map {list.Map} plays from are the server's.");
        }
    }

    /// <summary>Takes tables of pieces the server sent, and asks for the pieces this game's own copies lack.</summary>
    /// <param name="tables">The tables.</param>
    /// <param name="wantChunks">Sends the request for pieces of a file.</param>
    public void TakeTables(IReadOnlyList<FileChunkTable> tables, Action<ChunkWant> wantChunks)
    {
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(wantChunks);
        foreach (FileChunkTable table in tables)
        {
            if (Failure is not null || _list is null || table.Map != _list.Map || !_incoming.TryGetValue(table.Index, out Incoming? incoming)
                || incoming.Table is not null || incoming.File.Size <= ContentChunks.MaxLength)
            {
                continue;
            }

            // The pieces must add up to the file the list named, or the table is of some other file.
            if (table.Chunks.Sum(static c => (long)c.Length) != incoming.File.Size)
            {
                Fail($"the server's table of {incoming.File.Path} doesn't add up to the file");
                return;
            }

            List<int> missing = incoming.Plan(table.Chunks);
            BytesReused += incoming.File.Size - incoming.ToFetch;
            if (missing.Count == 0)
            {
                Finish(table.Index, incoming);
                if (Failure is not null)
                {
                    return;
                }

                continue;
            }

            IReadOnlyList<(int Start, int Count)> runs = ChunkWant.RunsOf(missing);
            if (runs.Count > ChunkWant.MaxRuns)
            {
                // Scattered past what one ask carries: the whole table, at the cost of a few pieces this game had.
                BytesReused -= incoming.File.Size - incoming.ToFetch;
                incoming.ForgetOwn();
                runs = [(0, table.Chunks.Count)];
            }

            wantChunks(new ChunkWant(table.Map, table.Index, runs));
        }
    }

    /// <summary>Takes pieces of files that came.</summary>
    /// <param name="pieces">The pieces.</param>
    public void Take(IReadOnlyList<FilePiece> pieces)
    {
        ArgumentNullException.ThrowIfNull(pieces);
        foreach (FilePiece piece in pieces)
        {
            if (Failure is not null || _list is null || piece.Map != _list.Map || !_incoming.TryGetValue(piece.Index, out Incoming? incoming))
            {
                continue;
            }

            if (incoming.Expected(piece.Chunk) is not { } expected)
            {
                // A piece of no table this game was sent, or one it has already: passed by.
                continue;
            }

            if (ChunkCodec.Unpack(piece.Deflated, piece.Data, expected.Length) is not { } data
                || (expected.Hash is { } hash && ContentChunks.HashOf(data) != hash))
            {
                Fail($"a piece of the server's {incoming.File.Path} didn't arrive whole");
                return;
            }

            incoming.Put(piece.Chunk, data);
            BytesFetched += data.Length;
            if (incoming.Complete)
            {
                Finish(piece.Index, incoming);
                if (Failure is not null)
                {
                    return;
                }
            }
        }
    }

    /// <summary>Lets go of the list being checked and whatever of it was coming, keeping every file already in place.</summary>
    /// <remarks>For a list sent ahead of its map that could not be taken: the map's own list, when it goes there, decides.</remarks>
    public void Abandon()
    {
        _incoming.Clear();
        _list = null;
        Failure = null;
    }

    /// <summary>Takes every file out of place, as the session goes.</summary>
    public void Clear()
    {
        Changed |= _pins.Count > 0;
        SharedChanged |= _pins.Keys.Any(p => !_local.Rules.IsLevelFile(p));
        _pins.Clear();
        _incoming.Clear();
        _list = null;
        Failure = null;
    }

    // A file whose every piece is in: checked whole, kept, and put in place.
    private void Finish(int index, Incoming incoming)
    {
        byte[] data = incoming.Assemble();
        if (!incoming.File.Matches(data))
        {
            Fail($"the server's {incoming.File.Path} didn't arrive whole");
            return;
        }

        if (!Signature(data).AsSpan().SequenceEqual(incoming.Signature))
        {
            Fail($"the server's {incoming.File.Path} is a different kind of file from this game's");
            return;
        }

        string kept = CachePath(incoming.File);
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            string temporary = kept + ".part";
            File.WriteAllBytes(temporary, data);
            File.Move(temporary, kept, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Fail($"the server's {incoming.File.Path} couldn't be kept: {ex.Message}");
            return;
        }

        _incoming.Remove(index);
        Pin(incoming.File.Path, kept);
        if (_incoming.Count == 0)
        {
            _logger.Information(
                LogCategory,
                $"Map {_list!.Map}'s files are the server's now: {BytesFetched / 1024} KiB fetched, {BytesReused / 1024} KiB of this game's own reused.");
            PruneCache();
        }
    }

    // The kept files past the cache's size let go, the longest unused first; never one in place now.
    private void PruneCache()
    {
        try
        {
            if (!Directory.Exists(CacheDirectory))
            {
                return;
            }

            HashSet<string> inUse = new(_pins.Values.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
            FileInfo[] kept = [.. new DirectoryInfo(CacheDirectory).EnumerateFiles().OrderByDescending(static f => f.LastWriteTimeUtc)];
            long total = 0;
            foreach (FileInfo file in kept)
            {
                total += file.Length;
                if (total > MaxCacheBytes && !inUse.Contains(file.FullName))
                {
                    file.Delete();
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warning(LogCategory, $"The kept servers' files couldn't be trimmed: {ex.Message}");
        }
    }

    // A kept file used again: the newest, so the trim keeps it longest.
    private static void Touch(string path)
    {
        try
        {
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Kept as it was; at worst it goes sooner.
        }
    }

    private void Pin(string path, string kept)
    {
        if (_pins.TryGetValue(path, out string? was) && was == kept)
        {
            return;
        }

        _pins[path] = kept;
        Changed = true;
        SharedChanged |= !_local.Rules.IsLevelFile(path);
    }

    private void Unpin(string path)
    {
        if (_pins.Remove(path))
        {
            Changed = true;
            SharedChanged |= !_local.Rules.IsLevelFile(path);
        }
    }

    private void Fail(string reason)
    {
        Failure = reason;
        _incoming.Clear();
        _logger.Warning(LogCategory, $"Could not take the server's files: {reason}.");
    }

    // The first bytes of a file, which name its format: an archive, a script, a terrain table.
    private static byte[] Signature(ReadOnlySpan<byte> data) => data[..Math.Min(4, data.Length)].ToArray();

    // A file listed can be one of the same kind as this game's only if it is long enough to carry the same signature.
    private static bool SameKind(byte[] own, SessionFile file) => file.Size >= Math.Min(4, own.Length);

    private string CachePath(SessionFile file) => Path.Combine(CacheDirectory, Convert.ToHexStringLower(file.Hash));

    // A kept file that is the one listed -- and, kept for another path of another kind, not taken for this one.
    private static bool CachedMatches(string path, SessionFile file, byte[]? signature = null)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length != file.Size)
            {
                return false;
            }

            byte[] data = File.ReadAllBytes(path);
            return file.Matches(data) && (signature is null || Signature(data).AsSpan().SequenceEqual(signature));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // A file being fetched: what the list said it is, this game's own copy to take pieces from, and the pieces so far.
    private sealed class Incoming
    {
        private byte[]? _own;
        private byte[]?[] _pieces = new byte[]?[1];
        private int[] _offsets = [0];
        private int _missing = 1;

        public Incoming(SessionFile file, byte[] own)
        {
            File = file;
            _own = own;
            Signature = SessionFileSync.Signature(own);
            ToFetch = file.Size;
        }

        public SessionFile File { get; }

        public byte[] Signature { get; }

        public IReadOnlyList<ContentChunk>? Table { get; private set; }

        public long Received { get; private set; }

        public long ToFetch { get; private set; }

        public bool Complete => _missing == 0;

        // The table come: every piece this game's own copy has taken from it, and the places of those it lacks.
        public List<int> Plan(IReadOnlyList<ContentChunk> table)
        {
            Table = table;
            _pieces = new byte[]?[table.Count];
            _offsets = new int[table.Count];
            for (int i = 1; i < table.Count; i++)
            {
                _offsets[i] = _offsets[i - 1] + table[i - 1].Length;
            }

            Dictionary<(ulong, int), int> own = [];
            if (_own is { } bytes)
            {
                int at = 0;
                foreach (ContentChunk chunk in ContentChunks.Cut(bytes))
                {
                    own.TryAdd((chunk.Hash, chunk.Length), at);
                    at += chunk.Length;
                }
            }

            List<int> missing = [];
            long toFetch = 0;
            for (int i = 0; i < table.Count; i++)
            {
                if (_own is not null && own.TryGetValue((table[i].Hash, table[i].Length), out int offset))
                {
                    _pieces[i] = _own.AsSpan(offset, table[i].Length).ToArray();
                }
                else
                {
                    missing.Add(i);
                    toFetch += table[i].Length;
                }
            }

            _missing = missing.Count;
            ToFetch = toFetch;
            _own = null;
            return missing;
        }

        // Asked for every piece after all: those taken from this game's copy are fetched again with the rest.
        public void ForgetOwn()
        {
            Array.Clear(_pieces);
            _missing = _pieces.Length;
            ToFetch = File.Size;
        }

        // What a piece at a place must be: its length and, from a table, its hash; null for one not wanted.
        public (int Length, ulong? Hash)? Expected(int chunk)
        {
            if (Table is null)
            {
                return File.Size <= ContentChunks.MaxLength && chunk == 0 && _pieces[0] is null ? ((int)File.Size, null) : null;
            }

            return chunk < Table.Count && _pieces[chunk] is null ? (Table[chunk].Length, Table[chunk].Hash) : null;
        }

        public void Put(int chunk, byte[] data)
        {
            _pieces[chunk] = data;
            _missing--;
            Received += data.Length;
        }

        public byte[] Assemble()
        {
            if (Table is null)
            {
                return _pieces[0]!;
            }

            byte[] data = new byte[File.Size];
            for (int i = 0; i < _pieces.Length; i++)
            {
                _pieces[i]!.CopyTo(data, _offsets[i]);
            }

            return data;
        }
    }
}
