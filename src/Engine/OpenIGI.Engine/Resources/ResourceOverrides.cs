using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using OpenIGI.Core;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Resources;
using OpenIGI.Formats.Ilff;

namespace OpenIGI.Engine.Resources;

/// <summary>
/// An ordered stack of folders whose files stand in for the game's own assets, addressed the way the
/// installation addresses them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Each folder mirrors the game.</b> An asset is overridden by putting a file where that asset lives
/// in the installation — <c>overrides/common/textures/000_04_1.tex</c> replaces the shared texture,
/// <c>overrides/missions/location0/level10/textures/000_04_1.tex</c> replaces level 10's own, and
/// <c>overrides/missions/location0/level1/terrain/terrain.tex</c> replaces a file that was already
/// loose. Nothing has to be declared and there is no manifest: the path IS the identity.
/// </para>
/// <para>
/// <b>That is what makes it able to replace an archived asset at all.</b> Most of this game's art lives
/// inside <c>.res</c> archives and is addressed by a name that is not unique — the art is layered, so
/// <c>common</c>, a location and a level can each ship a different <c>000_04_1</c>, and which one a
/// level draws depends on mount order. A replacement keyed by that name could not tell them apart. A
/// replacement keyed by WHERE THE ARCHIVE IS can, because an archive has exactly one place.
/// </para>
/// <para>
/// <b>An archive in a folder works too, and means the same thing.</b> A <c>.res</c> file in a layer is
/// opened and every entry in it is registered at the path that entry would have been overridden at
/// individually — so <c>overrides/common/textures/textures.res</c> holding <c>water_igi.tex</c> is exactly
/// equivalent to <c>overrides/common/textures/water_igi.tex</c>. The archive is a delivery mechanism, not a
/// new rule. It holds only what it replaces, so a name it does not carry resolves the way it always did.
/// </para>
/// <para>
/// <b>Several layers, one rule.</b> The layers are ordered, first highest. For every path the first
/// ENABLED layer that has it wins; the layers below fill whatever it leaves out; a path no enabled layer
/// has is the game's own. Within one layer a loose file beats an archived entry (someone who drops one
/// <c>.tex</c> beside a generated pack is correcting that pack), and two archives that carry the same path
/// resolve by archive path, alphabetically first, so the answer never depends on directory order.
/// </para>
/// <para>
/// <b>Rescannable by design.</b> <see cref="Rescan"/> rebuilds every layer's index and reports what moved,
/// so a file dropped in while the game is running is picked up without a restart. An archive is INDEXED
/// rather than loaded: the walk reads chunk headers and names and seeks past every payload, because a 4x
/// pack's level archive is a quarter of a gigabyte and there may be fifteen of them. Switching a layer on
/// or off (<see cref="SetEnabled"/>) re-merges the indexes already held and touches no disk.
/// </para>
/// <para>
/// <b>The bytes are read on demand and not kept here.</b> Whoever asks holds them -- a level's resource
/// registry, for as long as that level lives -- and lets them go with it.
/// </para>
/// </remarks>
public sealed class ResourceOverrides
{
    private const string LogCategory = "Overrides";

    /// <summary>The game folder's own overrides directory name, relative to the installation root.</summary>
    public const string DirectoryName = "overrides";

    /// <summary>The extension that marks a file in a layer as an archive of overrides, not an asset.</summary>
    public const string ArchiveExtension = ".res";

    private readonly IOpenIgiLogger _logger;
    private readonly Layer[] _layers;

    // The index is read from more than one thread. A menu backdrop builds its level on a worker, and the
    // registry it builds into resolves every archived entry through this store while the render thread is
    // free to rescan or swap a layer out of the frame. The lock covers the indexes only; the file read is
    // made outside it.
    private readonly Lock _gate = new();

    // The merged answer: each path's winning candidate, from the enabled layers only.
    private Dictionary<string, Candidate> _files = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Initializes a new instance of the <see cref="ResourceOverrides"/> class with one folder.</summary>
    /// <param name="root">The overrides directory, or <see langword="null"/> or absent for none.</param>
    /// <param name="logger">Where to report what was found.</param>
    public ResourceOverrides(string? root, IOpenIgiLogger? logger = null)
        : this(string.IsNullOrWhiteSpace(root) ? [] : [new OverrideLayer(DirectoryName, DirectoryName, root)], logger)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ResourceOverrides"/> class with a stack of folders.</summary>
    /// <param name="layers">The layers, highest priority first.</param>
    /// <param name="logger">Where to report what was found.</param>
    /// <exception cref="ArgumentNullException"><paramref name="layers"/> is <see langword="null"/>.</exception>
    public ResourceOverrides(IReadOnlyList<OverrideLayer> layers, IOpenIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(layers);

        _logger = logger ?? NullLogger.Instance;
        _layers = [.. layers.Select(layer => new Layer(
            layer.Id,
            layer.Name,
            string.IsNullOrWhiteSpace(layer.Root) ? null : Path.GetFullPath(layer.Root),
            layer.Enabled))];
    }

    /// <summary>Gets how many paths the enabled layers override, after resolution.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _files.Count;
            }
        }
    }

    /// <summary>Gets a value indicating whether any layer, enabled or not, holds a file.</summary>
    /// <remarks>What decides whether there is anything for the originals switch to compare against.</remarks>
    public bool HasContent
    {
        get
        {
            lock (_gate)
            {
                return _layers.Any(layer => layer.Index.Count > 0);
            }
        }
    }

    /// <summary>Gets or sets a value indicating whether every override is ignored.</summary>
    /// <remarks>
    /// The comparison switch. Bypassing rather than unloading is deliberate: the index and the bytes stay
    /// where they are, so flipping it back costs nothing and a folder somebody spent an hour building is
    /// never thrown away by a keypress.
    /// </remarks>
    public bool Bypassed { get; set; }

    /// <summary>Gets a value indicating whether any override is present and in use.</summary>
    public bool IsActive => !Bypassed && Count > 0;

    /// <summary>Gets every overridden path, relative to its layer and normalised.</summary>
    public IReadOnlyCollection<string> Paths
    {
        get
        {
            lock (_gate)
            {
                // A copy, not the live key collection: the caller orders and pages through it, and a
                // rescan on another thread would invalidate the enumerator half way down the listing.
                return _files.Keys.ToArray();
            }
        }
    }

    /// <summary>Gets the layers, highest priority first, with what each holds and supplies.</summary>
    public IReadOnlyList<LayerInfo> Layers
    {
        get
        {
            lock (_gate)
            {
                int[] wins = new int[_layers.Length];
                foreach (Candidate candidate in _files.Values)
                {
                    wins[candidate.Layer]++;
                }

                return [.. _layers.Select((layer, index) =>
                    new LayerInfo(layer.Id, layer.Name, layer.Root, layer.Enabled, layer.Index.Count, wins[index]))];
            }
        }
    }

    /// <summary>Rebuilds every layer's index from disk.</summary>
    /// <returns>What changed, after resolution, since the previous scan.</returns>
    /// <remarks>
    /// Safe to call at any time, including with no layers or with folders that do not exist, in which case
    /// it reports an empty scan rather than failing — an installation with no overrides is the ordinary one.
    /// </remarks>
    public ScanResult Rescan()
    {
        // Built off to the side and published at the end, so the walk -- which opens and reads the header
        // of every archive in every folder -- never holds the lock a level load is resolving through.
        var indexes = new Dictionary<string, Candidate>[_layers.Length];
        for (int layer = 0; layer < _layers.Length; layer++)
        {
            indexes[layer] = ScanLayer(layer);
        }

        lock (_gate)
        {
            Dictionary<string, Candidate> previous = _files;

            for (int layer = 0; layer < _layers.Length; layer++)
            {
                _layers[layer].Index = indexes[layer];
            }

            _files = Merge();
            return Compare(previous, _files);
        }
    }

    /// <summary>Switches one layer on or off, re-resolving every path from the indexes already held.</summary>
    /// <param name="layer">The layer's id, or its 1-based position in <see cref="Layers"/>.</param>
    /// <param name="enabled">Whether it should take part.</param>
    /// <param name="result">Receives what changed after resolution.</param>
    /// <returns><see langword="true"/> when the layer was found.</returns>
    /// <remarks>
    /// The store's answer changes at once; whoever already holds bytes (a resource registry) is brought up
    /// to date by its own reapply, exactly as after a <see cref="Rescan"/> or a flip of <see cref="Bypassed"/>.
    /// </remarks>
    public bool SetEnabled(string layer, bool enabled, out ScanResult result)
    {
        ArgumentNullException.ThrowIfNull(layer);

        result = default;
        int found = Find(layer);
        if (found < 0)
        {
            return false;
        }

        lock (_gate)
        {
            if (_layers[found].Enabled == enabled)
            {
                result = new ScanResult(_files.Count, 0, 0, 0);
                return true;
            }

            _layers[found].Enabled = enabled;
            Dictionary<string, Candidate> previous = _files;
            _files = Merge();
            result = Compare(previous, _files);
            return true;
        }
    }

    /// <summary>Finds a layer by id or by 1-based position.</summary>
    /// <param name="layer">The id, or the position as text.</param>
    /// <returns>The 0-based index, or -1 when there is no such layer.</returns>
    public int Find(string layer)
    {
        ArgumentNullException.ThrowIfNull(layer);

        for (int index = 0; index < _layers.Length; index++)
        {
            if (string.Equals(_layers[index].Id, layer, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return int.TryParse(layer, out int position) && position >= 1 && position <= _layers.Length
            ? position - 1
            : -1;
    }

    /// <summary>Gets the layer that supplies a path.</summary>
    /// <param name="relativePath">The path, from one of the <see cref="OverridePath"/>.</param>
    /// <returns>The 0-based layer index, or -1 when no enabled layer has it.</returns>
    public int LayerOf(string relativePath)
    {
        lock (_gate)
        {
            return _files.TryGetValue(relativePath, out Candidate candidate) ? candidate.Layer : -1;
        }
    }

    /// <summary>Lists every path that more than one enabled layer carries.</summary>
    /// <returns>Each contested path with the layer that wins it and the layers it covers, ordered by path.</returns>
    public IReadOnlyList<Conflict> Conflicts()
    {
        List<Conflict> conflicts = [];

        lock (_gate)
        {
            foreach ((string path, Candidate winner) in _files)
            {
                List<int>? covered = null;
                for (int layer = winner.Layer + 1; layer < _layers.Length; layer++)
                {
                    if (_layers[layer].Enabled && _layers[layer].Index.ContainsKey(path))
                    {
                        (covered ??= []).Add(layer);
                    }
                }

                if (covered is not null)
                {
                    conflicts.Add(new Conflict(path, winner.Layer, covered));
                }
            }
        }

        conflicts.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Path, b.Path));
        return conflicts;
    }

    /// <summary>Determines whether any layer, enabled or not, carries a path.</summary>
    /// <param name="relativePath">The path, from one of the <see cref="OverridePath"/>.</param>
    /// <returns><see langword="true"/> when some layer has a file for it.</returns>
    /// <remarks>
    /// Deliberately blind to <see cref="Bypassed"/> and to which layers are on: it answers "could this path
    /// be overridden", which is what a reapply needs to evict an entry whose override has just come or
    /// gone in either direction.
    /// </remarks>
    public bool Has(string relativePath)
    {
        if (relativePath.Length == 0)
        {
            return false;
        }

        lock (_gate)
        {
            foreach (Layer layer in _layers)
            {
                if (layer.Index.ContainsKey(relativePath))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Reads an override's bytes, from whichever enabled layer wins the path.</summary>
    /// <param name="relativePath">The path, from one of the <see cref="OverridePath"/>.</param>
    /// <param name="data">Receives the bytes, or empty when there is no such override.</param>
    /// <returns><see langword="true"/> when an override was read.</returns>
    /// <remarks>
    /// A fresh array every call, owned by the caller: a resource's bytes are handed out as a
    /// <see cref="ReadOnlyMemory{T}"/> the registry holds for as long as its entry lives, so they cannot be
    /// a transient buffer, and they are not kept here, so they go when the registry does.
    /// </remarks>
    public bool TryRead(string relativePath, out ReadOnlyMemory<byte> data) =>
        TryRead(relativePath, out data, out _);

    /// <summary>Reads an override's bytes, with a stamp that names exactly which file supplied them.</summary>
    /// <param name="relativePath">The path, from one of the <see cref="OverridePath"/>.</param>
    /// <param name="data">Receives the bytes, or empty when there is no such override.</param>
    /// <param name="stamp">
    /// Receives sixteen hex digits that change whenever the supplying file, layer, slice or write time does,
    /// or the empty string when nothing was read. Stable across runs, so it can name a file in a disk cache
    /// of things derived from these bytes.
    /// </param>
    /// <returns><see langword="true"/> when an override was read.</returns>
    public bool TryRead(string relativePath, out ReadOnlyMemory<byte> data, out string stamp)
    {
        data = default;
        stamp = string.Empty;

        if (Bypassed || relativePath.Length == 0)
        {
            return false;
        }

        Candidate candidate;

        lock (_gate)
        {
            if (!_files.TryGetValue(relativePath, out candidate))
            {
                return false;
            }
        }

        try
        {
            byte[] bytes;

            if (candidate.Offset < 0)
            {
                bytes = File.ReadAllBytes(candidate.Path);
            }
            else
            {
                // A slice of an override archive, read now rather than at scan time. The index recorded
                // where it is precisely so that a folder of large archives costs a walk of their headers.
                bytes = new byte[candidate.Length];

                using FileStream stream = File.OpenRead(candidate.Path);
                stream.Position = candidate.Offset;
                stream.ReadExactly(bytes);
            }

            // Read outside the lock, because a slice of a 4x pack's level archive is megabytes and the
            // render thread must not wait on that disk read.
            data = bytes;
            stamp = Stamp(candidate);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _logger.Warning(LogCategory, $"Override '{relativePath}' could not be read: {error.Message}");
            return false;
        }
    }

    private static string Stamp(Candidate candidate)
    {
        // Hashed rather than spelled out, so it fits in a file name; SHA-256 rather than HashCode, because
        // HashCode is seeded per process and the stamp has to mean the same thing next run.
        string identity = string.Create(
            CultureInfo.InvariantCulture,
            $"{candidate.Path.ToUpperInvariant()}|{candidate.Offset}|{candidate.Length}|{candidate.Written.Ticks}");
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return Convert.ToHexStringLower(hash.AsSpan(0, 8));
    }

    private Dictionary<string, Candidate> ScanLayer(int layer)
    {
        Dictionary<string, Candidate> index = new(StringComparer.OrdinalIgnoreCase);
        string? root = _layers[layer].Root;

        if (root is null || !Directory.Exists(root))
        {
            return index;
        }

        List<(string Relative, string Path)> archives = [];

        foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            string relative = ResourcePath.Normalize(Path.GetRelativePath(root, path));

            if (relative.Length == 0)
            {
                continue;
            }

            if (relative.EndsWith(ArchiveExtension, StringComparison.OrdinalIgnoreCase))
            {
                // Held back and indexed after the loose files, so that a hand-placed file already holds
                // its path by the time the archive's copy of that name is offered.
                archives.Add((relative, path));
                continue;
            }

            FileInfo info;

            try
            {
                info = new FileInfo(path);
            }
            catch (IOException)
            {
                continue;
            }

            index.TryAdd(relative, new Candidate(path, info.Length, info.LastWriteTimeUtc) { Layer = layer });
        }

        // Sorted, because the directory walk's order is the file system's and two archives may carry the
        // same path: the first by archive path wins, the same answer on every machine and every run.
        archives.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Relative, b.Relative));

        foreach ((string relative, string path) in archives)
        {
            IndexArchive(index, layer, relative, path);
        }

        return index;
    }

    private void IndexArchive(Dictionary<string, Candidate> index, int layer, string archive, string path)
    {
        FileInfo info;
        IReadOnlyList<IresIndex.Located> entries;

        try
        {
            info = new FileInfo(path);

            using FileStream stream = File.OpenRead(path);
            entries = IresIndex.Read(stream);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or IlffFormatException)
        {
            _logger.Warning(LogCategory, $"Override archive '{path}' could not be read: {error.Message}");
            return;
        }

        int registered = 0;

        foreach (IresIndex.Located entry in entries)
        {
            if (entry.Kind != IresEntryKind.Body)
            {
                // Only a body is an asset. PATH and CSTR describe the archive's own structure, which an
                // overlay has no business replacing.
                continue;
            }

            string relative = OverridePath.For(archive, entry.Name);

            // TryAdd: a loose file, or an alphabetically earlier archive, already holds the path.
            if (relative.Length > 0
                && index.TryAdd(
                    relative,
                    new Candidate(path, entry.Length, info.LastWriteTimeUtc) { Offset = entry.Offset, Layer = layer }))
            {
                registered++;
            }
        }

        _logger.Information(LogCategory, $"'{_layers[layer].Name}': '{archive}' contributes {registered} override(s).");
    }

    // Under _gate. Walks the layers top-down so each path keeps the first enabled layer that has it.
    private Dictionary<string, Candidate> Merge()
    {
        Dictionary<string, Candidate> merged = new(StringComparer.OrdinalIgnoreCase);

        foreach (Layer layer in _layers)
        {
            if (!layer.Enabled)
            {
                continue;
            }

            foreach ((string path, Candidate candidate) in layer.Index)
            {
                merged.TryAdd(path, candidate);
            }
        }

        return merged;
    }

    private static ScanResult Compare(Dictionary<string, Candidate> previous, Dictionary<string, Candidate> next)
    {
        int added = 0;
        int changed = 0;

        foreach ((string path, Candidate candidate) in next)
        {
            if (!previous.TryGetValue(path, out Candidate existing))
            {
                added++;
            }
            else if (existing != candidate)
            {
                // Every field counts, the file and the layer included: the same size and time in a
                // different pack is a different picture.
                changed++;
            }
        }

        int removed = previous.Keys.Count(path => !next.ContainsKey(path));
        return new ScanResult(next.Count, added, removed, changed);
    }

    /// <summary>What a <see cref="Rescan"/> or <see cref="SetEnabled"/> changed, after resolution.</summary>
    /// <param name="Total">How many paths are overridden now.</param>
    /// <param name="Added">How many were not before.</param>
    /// <param name="Removed">How many are no longer.</param>
    /// <param name="Changed">How many are the same path from a different file, layer or version.</param>
    public readonly record struct ScanResult(int Total, int Added, int Removed, int Changed)
    {
        /// <summary>Gets a value indicating whether anything moved.</summary>
        public bool Moved => Added > 0 || Removed > 0 || Changed > 0;

        /// <inheritdoc/>
        public override string ToString() =>
            $"{Total} override(s); {Added} new, {Changed} changed, {Removed} gone";
    }

    /// <summary>One layer as it stands.</summary>
    /// <param name="Id">The layer's id.</param>
    /// <param name="Name">The layer's display name.</param>
    /// <param name="Root">Its folder, or <see langword="null"/> when it has none.</param>
    /// <param name="Enabled">Whether it takes part.</param>
    /// <param name="Files">How many paths it carries.</param>
    /// <param name="Wins">How many of those it supplies, because no enabled layer above it has them.</param>
    public readonly record struct LayerInfo(string Id, string Name, string? Root, bool Enabled, int Files, int Wins);

    /// <summary>A path more than one enabled layer carries.</summary>
    /// <param name="Path">The path.</param>
    /// <param name="Winner">The index of the layer that supplies it.</param>
    /// <param name="Covered">The indexes of the enabled layers below that also carry it.</param>
    public sealed record Conflict(string Path, int Winner, IReadOnlyList<int> Covered);

    private sealed class Layer(string id, string name, string? root, bool enabled)
    {
        public string Id { get; } = id;

        public string Name { get; } = name;

        public string? Root { get; } = root;

        public bool Enabled { get; set; } = enabled;

        public Dictionary<string, Candidate> Index { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private readonly record struct Candidate(string Path, long Length, DateTime Written)
    {
        /// <summary>Where the bytes start in <see cref="Path"/>, or -1 when the file itself is the asset.</summary>
        public long Offset { get; init; } = -1;

        /// <summary>The layer the file belongs to.</summary>
        public int Layer { get; init; }
    }
}
