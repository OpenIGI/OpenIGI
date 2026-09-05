using System.IO.Enumeration;
using OpenIGI.Core;

namespace OpenIGI.Engine.Vfs;

/// <summary>
/// The <c>LOCAL</c> device: files on disk, below the installation root.
/// </summary>
/// <remarks>
/// <para>
/// The original resolves <c>LOCAL:</c> paths against the process working directory, which is the game
/// folder. The port takes the root explicitly so a host can point the engine at an installation
/// anywhere, and so tests can run against a temporary directory.
/// </para>
/// <para>
/// <b>Case.</b> The game's own data was authored on a case-insensitive filesystem and its resources name
/// each other in whatever case the author typed, so <c>graph.res</c> and <c>GRAPH.RES</c> are the same
/// file as far as every path inside the data is concerned. That holds for free on Windows and on a
/// default macOS volume, and not at all on Linux or on a case-sensitive APFS volume, where an
/// installation copied from a Windows machine would half-load. The exact path is therefore tried first
/// and a case-folded walk of the directories is the fallback, so the common case costs one
/// <c>File.Exists</c> and only a genuine miss pays for a directory listing.
/// </para>
/// </remarks>
public sealed class LocalFileDevice : IFileDevice
{
    /// <summary>The device name the original uses for on-disk files.</summary>
    public const string DeviceName = "LOCAL";

    /// <summary>Initializes a new instance of the <see cref="LocalFileDevice"/> class.</summary>
    /// <param name="rootDirectory">The installation root that device-relative paths resolve against.</param>
    /// <param name="name">The device name; defaults to <see cref="DeviceName"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rootDirectory"/> or <paramref name="name"/> is <see langword="null"/>.</exception>
    public LocalFileDevice(string rootDirectory, string name = DeviceName)
    {
        ArgumentNullException.ThrowIfNull(rootDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        RootDirectory = Path.GetFullPath(rootDirectory);
        Name = name;
    }

    // One entry per directory the fallback has had to look inside, mapping a lower-cased child name to
    // the name the filesystem actually holds. Built lazily and never invalidated: it is consulted only
    // after an exact-case lookup has already failed, and a file written while the game runs is written
    // through the name the writer used, which is the name the exact lookup then finds.
    private readonly Dictionary<string, Dictionary<string, string>> _foldedDirectories =
        new(StringComparer.Ordinal);

    /// <summary>Gets the absolute directory device-relative paths resolve against.</summary>
    public string RootDirectory { get; }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public bool Exists(string path) => ResolveFile(ToFullPath(path)) is not null;

    /// <inheritdoc/>
    public bool TryOpenRead(string path, out Stream? stream)
    {
        string? fullPath = ResolveFile(ToFullPath(path));
        if (fullPath is null)
        {
            stream = null;
            return false;
        }

        stream = File.OpenRead(fullPath);
        return true;
    }

    /// <inheritdoc/>
    public IEnumerable<string> EnumerateFiles(string path, string searchPattern)
    {
        ArgumentNullException.ThrowIfNull(searchPattern);

        string? directory = ResolveDirectory(ToFullPath(path));
        if (directory is null)
        {
            return [];
        }

        // Retail installations use mixed/uppercase names (for example GRAPH*.DAT), while the
        // original Windows filesystem lookup is case-insensitive. Directory.EnumerateFiles applies
        // the platform's pattern matching rules, which are case-sensitive on macOS/Linux even when
        // the underlying volume is not. Match the filename explicitly so VFS enumeration has the
        // same behavior on every supported host.
        return Directory
            .EnumerateFiles(directory)
            .Where(file => FileSystemName.MatchesSimpleExpression(
                searchPattern,
                Path.GetFileName(file),
                ignoreCase: true))
            .Select(file => ResourcePath.Normalize(Path.GetRelativePath(RootDirectory, file)));
    }

    /// <inheritdoc/>
    public IEnumerable<string> EnumerateDirectories(string path)
    {
        string? directory = ResolveDirectory(ToFullPath(path));
        if (directory is null)
        {
            return [];
        }

        return Directory
            .EnumerateDirectories(directory)
            .Select(child => ResourcePath.Normalize(Path.GetRelativePath(RootDirectory, child)));
    }

    /// <summary>Maps a device-relative path to an absolute path below <see cref="RootDirectory"/>.</summary>
    /// <param name="path">The device-relative path.</param>
    /// <returns>The absolute path.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The result is confined to the root: a path that would escape it (through <c>..</c> or a rooted
    /// path) resolves back to the root itself rather than reaching outside the installation.
    /// </remarks>
    public string ToFullPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        string relative = ResourcePath.Normalize(path).TrimStart(ResourcePath.DirectorySeparator);
        string combined = Path.GetFullPath(Path.Combine(RootDirectory, relative));

        if (!combined.StartsWith(RootDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return RootDirectory;
        }

        if (File.Exists(combined) || Directory.Exists(combined))
        {
            return combined;
        }

        // A retail install may preserve uppercase directory names (MISSIONS/LEVEL1/GRAPHS). Resolve
        // each segment against the actual directory entries so lookups remain case-insensitive on a
        // case-sensitive host filesystem, matching the original Windows behavior.
        string rootRelative = Path.GetRelativePath(RootDirectory, combined);
        string current = RootDirectory;
        foreach (string segment in rootRelative.Split(
                     Path.DirectorySeparatorChar,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            string exact = Path.Combine(current, segment);
            if (File.Exists(exact) || Directory.Exists(exact))
            {
                current = exact;
                continue;
            }

            if (Directory.Exists(current))
            {
                string? match = Directory
                    .EnumerateFileSystemEntries(current)
                    .FirstOrDefault(entry => string.Equals(
                        Path.GetFileName(entry),
                        segment,
                        StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                {
                    current = match;
                    continue;
                }
            }

            // Preserve the normal missing-path behavior for the unresolved suffix.
            current = exact;
        }

        return current;
    }

    /// <inheritdoc/>
    public override string ToString() => $"{Name}: -> {RootDirectory}";

    private string? ResolveFile(string fullPath) =>
        File.Exists(fullPath) ? fullPath : ResolveIgnoringCase(fullPath, File.Exists);

    private string? ResolveDirectory(string fullPath) =>
        Directory.Exists(fullPath) ? fullPath : ResolveIgnoringCase(fullPath, Directory.Exists);

    /// <summary>Walks a path a segment at a time, matching each against what the directory really holds.</summary>
    /// <remarks>
    /// Segment by segment rather than by listing the whole tree, because only the directories actually
    /// on the path are ever read, and because a case-folded name is only ambiguous within one directory.
    /// Where two children of a directory differ only in case — legal on the filesystems this exists for,
    /// impossible on the one the data was authored on — the first the filesystem reports wins, which is
    /// arbitrary but stable.
    /// </remarks>
    private string? ResolveIgnoringCase(string fullPath, Func<string, bool> exists)
    {
        string relative = Path.GetRelativePath(RootDirectory, fullPath);
        if (relative is "." or "")
        {
            return Directory.Exists(RootDirectory) ? RootDirectory : null;
        }

        string current = RootDirectory;
        foreach (string segment in relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            if (!TryFoldChild(current, segment, out string? actual))
            {
                return null;
            }

            current = Path.Combine(current, actual);
        }

        return exists(current) ? current : null;
    }

    private bool TryFoldChild(string directory, string name, out string actual)
    {
        Dictionary<string, string>? children;
        lock (_foldedDirectories)
        {
            if (!_foldedDirectories.TryGetValue(directory, out children))
            {
                children = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (Directory.Exists(directory))
                {
                    foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
                    {
                        string entryName = Path.GetFileName(entry);
                        children.TryAdd(entryName, entryName);
                    }
                }

                _foldedDirectories[directory] = children;
            }
        }

        if (children.TryGetValue(name, out string? found))
        {
            actual = found;
            return true;
        }

        actual = string.Empty;
        return false;
    }
}
