using OpenIGI.Core.Diagnostics;

namespace OpenIGI.Game.Multiplayer;

/// <summary>
/// A client's side of a server's files: each list the server sends checked against the files this game plays, those that
/// differ fetched, checked and kept, and every one of the server's that differs from this game's put in its place.
/// </summary>
/// <remarks>
/// <para>
/// A file fetched is kept by its hash under <see cref="CacheDirectory"/>, never in the game's own folder, so the next server
/// with the same file costs nothing and the installation is left as it was. What is put in place lasts for the session:
/// <see cref="Pins"/> is what the file system reads in place of the installation's own, and is emptied as the session goes.
/// </para>
/// <para>
/// Nothing a server sends is trusted. A list may name only paths a level plays from (<see cref="GameplayFiles.IsSafePath"/>),
/// within a size; a file is taken only whole, in order, and only if its hash is the one the list gave.
/// </para>
/// </remarks>
public sealed class SessionFileSync
{
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

    /// <summary>Gets the bytes that have come and the bytes of every file being fetched.</summary>
    public (long Received, long Total) Progress =>
        (_incoming.Values.Sum(static i => i.Received), _incoming.Values.Sum(static i => i.File.Size));

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

        // A server names only the files of the map's level and those every level plays from, each one this game has: never
        // a file of another kind, of another map, or one the installation lacks, which no level of it reads.
        if (list.Files.FirstOrDefault(f => !GameplayFiles.IsListable(_local.TitleId, list.Map, f.Path)) is { } stray)
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
                Pin(file.Path, cached);
                continue;
            }

            _incoming[i] = new Incoming(file, Signature(own));
            wanted.Add(i);
        }

        if (wanted.Count > 0)
        {
            _logger.Information(
                LogCategory,
                $"{wanted.Count} of the {list.Files.Count} files map {list.Map} plays from differ here; fetching {list.Files.Where((_, i) => _incoming.ContainsKey(i)).Sum(static f => f.Size) / 1024} KiB from the server: "
                    + string.Join(", ", wanted.Take(6).Select(i => list.Files[i].Path)) + (wanted.Count > 6 ? ", ..." : string.Empty) + ".");
            want(list.Map, wanted);
        }
        else
        {
            _logger.Information(LogCategory, $"The {list.Files.Count} files map {list.Map} plays from are the server's.");
        }
    }

    /// <summary>Takes pieces of files that came.</summary>
    /// <param name="pieces">The pieces.</param>
    public void Take(IReadOnlyList<FilePiece> pieces)
    {
        ArgumentNullException.ThrowIfNull(pieces);
        foreach (FilePiece piece in pieces)
        {
            if (_list is null || piece.Map != _list.Map || !_incoming.TryGetValue(piece.Index, out Incoming? incoming))
            {
                continue;
            }

            // In order and within the file, or the file is not the one the list named.
            if (piece.Offset != incoming.Received || incoming.Received + piece.Data.Length > incoming.File.Size)
            {
                Fail($"the server's {incoming.File.Path} came out of order");
                return;
            }

            piece.Data.CopyTo(incoming.Data, incoming.Received);
            incoming.Received += piece.Data.Length;
            if (incoming.Received < incoming.File.Size)
            {
                continue;
            }

            if (!incoming.File.Matches(incoming.Data))
            {
                Fail($"the server's {incoming.File.Path} didn't arrive whole");
                return;
            }

            if (!Signature(incoming.Data).AsSpan().SequenceEqual(incoming.Signature))
            {
                Fail($"the server's {incoming.File.Path} is a different kind of file from this game's");
                return;
            }

            string kept = CachePath(incoming.File);
            try
            {
                Directory.CreateDirectory(CacheDirectory);
                string temporary = kept + ".part";
                File.WriteAllBytes(temporary, incoming.Data);
                File.Move(temporary, kept, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Fail($"the server's {incoming.File.Path} couldn't be kept: {ex.Message}");
                return;
            }

            _incoming.Remove(piece.Index);
            Pin(incoming.File.Path, kept);
            if (_incoming.Count == 0)
            {
                _logger.Information(LogCategory, $"Map {_list.Map}'s files are the server's now.");
            }
        }
    }

    /// <summary>Takes every file out of place, as the session goes.</summary>
    public void Clear()
    {
        Changed |= _pins.Count > 0;
        SharedChanged |= _pins.Keys.Any(static p => !IsLevelFile(p));
        _pins.Clear();
        _incoming.Clear();
        _list = null;
        Failure = null;
    }

    // A file a level plays from, as against one every level does.
    private static bool IsLevelFile(string path) => path.StartsWith("missions/location0/level", StringComparison.Ordinal);

    private void Pin(string path, string kept)
    {
        if (_pins.TryGetValue(path, out string? was) && was == kept)
        {
            return;
        }

        _pins[path] = kept;
        Changed = true;
        SharedChanged |= !IsLevelFile(path);
    }

    private void Unpin(string path)
    {
        if (_pins.Remove(path))
        {
            Changed = true;
            SharedChanged |= !IsLevelFile(path);
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

    // A file being fetched: what the list said it is, and what of it has come.
    private sealed class Incoming(SessionFile file, byte[] signature)
    {
        public SessionFile File { get; } = file;

        public byte[] Signature { get; } = signature;

        // Made as the first piece comes, so a list asks no memory of this game before its server sends anything.
        public byte[] Data => _data ??= new byte[File.Size];

        private byte[]? _data;

        public int Received { get; set; }
    }
}
