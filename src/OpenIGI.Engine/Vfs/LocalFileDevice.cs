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
    public bool Exists(string path) => TryResolveExistingPath(path, out string fullPath) && File.Exists(fullPath);

    /// <inheritdoc/>
    public bool TryOpenRead(string path, out Stream? stream)
    {
        if (!TryResolveExistingPath(path, out string fullPath) || !File.Exists(fullPath))
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

        if (!TryResolveExistingPath(path, out string directory) || !Directory.Exists(directory))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(directory, searchPattern)
            .Select(file => ResourcePath.Normalize(Path.GetRelativePath(RootDirectory, file)));
    }

    /// <inheritdoc/>
    public IEnumerable<string> EnumerateDirectories(string path)
    {
        if (!TryResolveExistingPath(path, out string directory) || !Directory.Exists(directory))
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

    private bool TryResolveExistingPath(string path, out string fullPath)
    {
        ArgumentNullException.ThrowIfNull(path);

        string relative = ResourcePath.Normalize(path).TrimStart(ResourcePath.DirectorySeparator);
        string candidate = Path.GetFullPath(Path.Combine(RootDirectory, relative));
        if (!IsWithinRoot(candidate))
        {
            fullPath = RootDirectory;
            return false;
        }

        if (File.Exists(candidate) || Directory.Exists(candidate))
        {
            fullPath = candidate;
            return true;
        }

        string relativeToRoot = Path.GetRelativePath(RootDirectory, candidate);
        if (relativeToRoot == ".")
        {
            fullPath = RootDirectory;
            return Directory.Exists(fullPath);
        }

        string current = RootDirectory;
        foreach (string segment in relativeToRoot.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            string? child = FindChild(current, segment);
            if (child is null)
            {
                fullPath = candidate;
                return false;
            }

            current = child;
        }

        fullPath = current;
        return true;
    }

    private static string? FindChild(string directory, string name)
    {
        string? exact = Directory.EnumerateFileSystemEntries(directory)
            .FirstOrDefault(entry => string.Equals(
                Path.GetFileName(entry), name, StringComparison.Ordinal));
        if (exact is not null)
        {
            return exact;
        }

        return Directory.EnumerateFileSystemEntries(directory)
            .FirstOrDefault(entry => string.Equals(
                Path.GetFileName(entry), name, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsWithinRoot(string fullPath) =>
        fullPath.Equals(RootDirectory, StringComparison.OrdinalIgnoreCase)
        || fullPath.StartsWith(
            RootDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public override string ToString() => $"{Name}: -> {RootDirectory}";
}
