using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.Sound;

namespace OpenIGI.Engine.Audio;

/// <summary>
/// Every sound the game can name, gathered from the directories and archives the original mounts.
/// </summary>
/// <remarks>
/// <para>
/// The original mounts a sound directory with <c>0x4E68D0</c>, a thin wrapper over the virtual file
/// system's directory walk. Its per-file callback <c>0x4E68F0</c> looks at the extension and does one of
/// two things: <c>.WAV</c> goes to the ILSF loader <c>0x495F70</c>, and <c>.RES</c> is opened as an
/// archive whose bodies are themselves sounds. Anything else is ignored.
/// </para>
/// <para>
/// Sounds are filed under the bare stem of their path — <c>ak47_1</c>, not
/// <c>LOCAL:common/sounds/ak47_1.wav</c> — because that is all a script ever says. See
/// <see cref="SoundName"/> for how the key is extracted and hashed.
/// </para>
/// <para>
/// <b>First mount wins.</b> The original's insert does not check for an existing entry, but its loader
/// does: it looks the key up first and reports <c>"SOUND - File %s already loaded!"</c> without
/// replacing anything. So a mission's own sounds cannot shadow a common one of the same name, and the
/// mount order in <c>Mission_Load</c> is what decides. The port keeps that, and logs the same way.
/// </para>
/// </remarks>
public sealed class SoundBank
{
    private const string LogCategory = "Audio";

    private readonly VirtualFileSystem _fileSystem;
    private readonly IIgiLogger _logger;
    private readonly Dictionary<string, SoundEntry> _sounds = new(StringComparer.Ordinal);
    private readonly List<string> _mounted = [];

    /// <summary>Initializes a new instance of the <see cref="SoundBank"/> class.</summary>
    /// <param name="fileSystem">The file system paths are resolved through.</param>
    /// <param name="logger">Where to report what was mounted and what would not load.</param>
    /// <exception cref="ArgumentNullException"><paramref name="fileSystem"/> is <see langword="null"/>.</exception>
    public SoundBank(VirtualFileSystem fileSystem, IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _fileSystem = fileSystem;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Gets the number of sounds in the bank.</summary>
    public int Count => _sounds.Count;

    /// <summary>Gets the keys of every sound in the bank.</summary>
    public IReadOnlyCollection<string> Names => _sounds.Keys;

    /// <summary>Gets the paths that have been mounted, in order.</summary>
    public IReadOnlyList<string> Mounted => _mounted;

    /// <summary>Finds a sound by the name a script uses.</summary>
    /// <param name="name">The sound name, matched without regard to case.</param>
    /// <param name="entry">Receives the sound when one is found.</param>
    /// <returns><see langword="true"/> when the bank holds a sound of that name.</returns>
    public bool TryGet(string name, out SoundEntry entry)
    {
        entry = null!;
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        return _sounds.TryGetValue(name.ToUpperInvariant(), out entry!);
    }

    /// <summary>Mounts a directory of sounds, loading every <c>.wav</c> and <c>.res</c> in it.</summary>
    /// <param name="directory">A virtual file system directory such as <c>LOCAL:common/sounds</c>.</param>
    /// <returns>The number of sounds added.</returns>
    /// <remarks>
    /// A directory that is not there is not an error — the original's walker simply finds nothing, and
    /// most missions ship no sounds of their own.
    /// </remarks>
    public int MountDirectory(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        int added = 0;
        try
        {
            foreach (string path in _fileSystem.EnumerateFiles(directory, "*.wav"))
            {
                added += AddFile(path) ? 1 : 0;
            }

            foreach (string path in _fileSystem.EnumerateFiles(directory, "*.res"))
            {
                added += MountArchive(path);
            }
        }
        catch (EngineFatalException error)
        {
            // An unbound alias or an unregistered device. Fatal when the game asks for a file it needs,
            // but a sound directory that cannot be resolved just means no sounds -- and taking the
            // process down over it would make an audio gap into a crash.
            _logger.Warning(LogCategory, $"Sound directory '{directory}' did not resolve: {error.Message}");
            return added;
        }

        _mounted.Add(directory);
        _logger.Debug(LogCategory, $"Mounted '{directory}': {added} sound(s).");
        return added;
    }

    /// <summary>Mounts one archive of sounds.</summary>
    /// <param name="path">A virtual file system path to a <c>.res</c> archive.</param>
    /// <returns>The number of sounds added.</returns>
    public int MountArchive(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        IresArchive archive;
        try
        {
            if (!_fileSystem.Exists(path))
            {
                return 0;
            }

            archive = IresArchive.Read(_fileSystem.ReadAllBytes(path));
        }
        catch (Exception error) when (error is IlffFormatException or IOException or EngineFatalException)
        {
            _logger.Warning(LogCategory, $"Sound archive '{path}' would not open: {error.Message}");
            return 0;
        }

        int added = 0;
        foreach (IresEntry entry in archive.Entries)
        {
            if (IlsfFile.IsIlsf(entry.Data.Span) && Add(entry.Name, entry.Data))
            {
                added++;
            }
        }

        if (!_mounted.Contains(path))
        {
            _mounted.Add(path);
        }

        return added;
    }

    /// <summary>Forgets every sound.</summary>
    /// <remarks>Called between missions, where the original tears its table down and rebuilds it.</remarks>
    public void Clear()
    {
        _sounds.Clear();
        _mounted.Clear();
    }

    private bool AddFile(string path)
    {
        try
        {
            byte[] bytes = _fileSystem.ReadAllBytes(path);
            return IlsfFile.IsIlsf(bytes) && Add(path, bytes);
        }
        catch (Exception error) when (error is IOException or EngineFatalException)
        {
            _logger.Warning(LogCategory, $"Sound '{path}' would not read: {error.Message}");
            return false;
        }
    }

    private bool Add(string path, ReadOnlyMemory<byte> bytes)
    {
        string key = SoundName.ToKey(path);
        if (key.Length == 0)
        {
            // The original's own wording for a path it cannot take a key out of.
            _logger.Warning(LogCategory, $"Invalid sound filename '{path}'.");
            return false;
        }

        if (_sounds.ContainsKey(key))
        {
            _logger.Debug(LogCategory, $"Sound '{key}' already loaded; keeping the first.");
            return false;
        }

        IlsfHeader header;
        try
        {
            header = IlsfHeader.Read(bytes.Span);
        }
        catch (IlsfFormatException error)
        {
            _logger.Warning(LogCategory, $"Sound '{path}' is not a valid format: {error.Message}");
            return false;
        }

        // A streamed sound keeps its path and nothing else, which is the whole point of the storage
        // code: the music track is three megabytes packed and about thirteen expanded, and the original
        // never holds either.
        _sounds[key] = header.IsStreamed
            ? new SoundEntry(key, path, header, ReadOnlyMemory<byte>.Empty)
            : new SoundEntry(key, path, header, bytes);

        return true;
    }
}
