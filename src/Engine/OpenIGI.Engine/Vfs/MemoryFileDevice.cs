using System.IO.Enumeration;
using OpenIGI.Core;

namespace OpenIGI.Engine.Vfs;

/// <summary>
/// A device whose files are bytes already in memory: an installation a browser has read, or a test's.
/// </summary>
/// <remarks>
/// <para>
/// A page cannot open a file by name the way <see cref="LocalFileDevice"/> does. What it has is what the
/// person picked, read into memory ahead of the mission that needs it, and this is the device those bytes
/// are handed to. Files can be added while the game runs -- the next mission's folder arriving while the
/// current one plays -- and a file not yet added simply does not exist yet.
/// </para>
/// <para>
/// <b>Case.</b> Looked up without regard to case, for the reason <see cref="LocalFileDevice"/> gives: the
/// data was authored on a filesystem that ignored it and names its own files in whatever case its author
/// typed. Paths come back as they were added.
/// </para>
/// </remarks>
public sealed class MemoryFileDevice : IFileDevice
{
    private readonly Dictionary<string, Entry> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

    /// <summary>Initialises a new instance of the <see cref="MemoryFileDevice"/> class.</summary>
    /// <param name="name">
    /// The device name; <see cref="LocalFileDevice.DeviceName"/> by default, since it stands in for the
    /// installation on disk.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    public MemoryFileDevice(string name = LocalFileDevice.DeviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <summary>Gets how many files the device holds.</summary>
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

    /// <summary>Adds a file, or replaces the one already at its path.</summary>
    /// <param name="path">Where it sits, relative to the device's root.</param>
    /// <param name="contents">Its bytes. Held, not copied: the caller must not change them afterwards.</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> names no file.</exception>
    public void Add(string path, ReadOnlyMemory<byte> contents)
    {
        string key = Key(path);
        if (key.Length == 0)
        {
            throw new ArgumentException("A file needs a name.", nameof(path));
        }

        lock (_gate)
        {
            _files[key] = new Entry(key, contents);
        }
    }

    /// <summary>Lets a file go.</summary>
    /// <param name="path">Where it sits.</param>
    /// <returns><see langword="true"/> when there was one.</returns>
    public bool Remove(string path)
    {
        lock (_gate)
        {
            return _files.Remove(Key(path));
        }
    }

    /// <inheritdoc/>
    public bool Exists(string path)
    {
        lock (_gate)
        {
            return _files.ContainsKey(Key(path));
        }
    }

    /// <inheritdoc/>
    /// <remarks>A read-only stream over the bytes; nothing is copied.</remarks>
    public bool TryOpenRead(string path, out Stream? stream)
    {
        Entry entry;
        lock (_gate)
        {
            if (!_files.TryGetValue(Key(path), out entry))
            {
                stream = null;
                return false;
            }
        }

        stream = new ReadOnlyMemoryStream(entry.Contents);
        return true;
    }

    /// <inheritdoc/>
    public IEnumerable<string> EnumerateFiles(string path, string searchPattern)
    {
        ArgumentNullException.ThrowIfNull(searchPattern);

        string directory = Key(path);
        List<string> found = [];

        lock (_gate)
        {
            foreach (Entry entry in _files.Values)
            {
                (string parent, string name) = Split(entry.Path);

                if (string.Equals(parent, directory, StringComparison.OrdinalIgnoreCase)
                    && FileSystemName.MatchesSimpleExpression(searchPattern, name, ignoreCase: true))
                {
                    found.Add(entry.Path);
                }
            }
        }

        return found;
    }

    /// <inheritdoc/>
    /// <remarks>Every directory any file lies under, one level below <paramref name="path"/>.</remarks>
    public IEnumerable<string> EnumerateDirectories(string path)
    {
        string directory = Key(path);
        string prefix = directory.Length == 0 ? string.Empty : directory + ResourcePath.DirectorySeparator;
        Dictionary<string, string> children = new(StringComparer.OrdinalIgnoreCase);

        lock (_gate)
        {
            foreach (Entry entry in _files.Values)
            {
                if (!entry.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int separator = entry.Path.IndexOf(ResourcePath.DirectorySeparator, prefix.Length);
                if (separator > prefix.Length)
                {
                    string child = entry.Path[..separator];
                    children.TryAdd(child, child);
                }
            }
        }

        return [.. children.Values];
    }

    /// <inheritdoc/>
    public override string ToString() => $"{Name}: -> {Count} file(s) in memory";

    /// <summary>A path as the device keys it: forward slashes, no leading or trailing separator.</summary>
    private static string Key(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return ResourcePath.Normalize(path).Trim(ResourcePath.DirectorySeparator);
    }

    private static (string Parent, string Name) Split(string path)
    {
        int separator = path.LastIndexOf(ResourcePath.DirectorySeparator);
        return separator < 0 ? (string.Empty, path) : (path[..separator], path[(separator + 1)..]);
    }

    private readonly record struct Entry(string Path, ReadOnlyMemory<byte> Contents);

    /// <summary>A read-only, seekable stream over memory it does not own.</summary>
    private sealed class ReadOnlyMemoryStream(ReadOnlyMemory<byte> contents) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => contents.Length;

        public override long Position
        {
            get => _position;
            set => _position = (int)Math.Clamp(value, 0, contents.Length);
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            int available = Math.Min(buffer.Length, contents.Length - _position);
            if (available <= 0)
            {
                return 0;
            }

            contents.Span.Slice(_position, available).CopyTo(buffer);
            _position += available;
            return available;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long target = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => contents.Length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            };

            Position = target;
            return _position;
        }

        public override void Flush()
        {
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
