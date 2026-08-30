using System.IO.Enumeration;
using OpenIGI.Core;

namespace OpenIGI.Engine.Vfs;

/// <summary>
/// The <c>LOCAL</c> device: files on disk, below the installation root.
/// </summary>
/// <remarks>
/// The original resolves <c>LOCAL:</c> paths against the process working directory, which is the game
/// folder. The port takes the root explicitly so a host can point the engine at an installation
/// anywhere, and so tests can run against a temporary directory.
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

    /// <summary>Gets the absolute directory device-relative paths resolve against.</summary>
    public string RootDirectory { get; }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public bool Exists(string path) => File.Exists(ToFullPath(path));

    /// <inheritdoc/>
    public bool TryOpenRead(string path, out Stream? stream)
    {
        string fullPath = ToFullPath(path);
        if (!File.Exists(fullPath))
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

        string directory = ToFullPath(path);
        if (!Directory.Exists(directory))
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
        string directory = ToFullPath(path);
        if (!Directory.Exists(directory))
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

        return combined.StartsWith(RootDirectory, StringComparison.OrdinalIgnoreCase)
            ? combined
            : RootDirectory;
    }

    /// <inheritdoc/>
    public override string ToString() => $"{Name}: -> {RootDirectory}";
}
