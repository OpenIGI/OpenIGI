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
/// <c>.MP3</c> is the one addition, and it is the sequel's rather than an enhancement: I.G.I. 2 ships
/// its menu theme as an MPEG stream inside <c>menusystem/sound/sounds.res</c> and its mission scores as
/// loose <c>*_music.mp3</c> files, where I.G.I. 1 ships IMA ADPCM for both. What decides which decoder
/// an entry gets is its <em>content</em> and not its name — see <see cref="SoundCodec"/> — so the
/// extension governs nothing but which loose files are worth opening.
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

    /// <summary>Mounts a directory of sounds, loading every <c>.wav</c>, <c>.mp3</c> and <c>.res</c> in it.</summary>
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
            foreach (string pattern in new[] { "*.wav", "*.mp3" })
            {
                foreach (string path in _fileSystem.EnumerateFiles(directory, pattern))
                {
                    added += AddFile(path) ? 1 : 0;
                }
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
            // An archived MPEG track has no file to reopen, so it keeps its packed bytes and is decoded
            // out of memory. The first game's archives hold no such thing; the sequel's menu theme is
            // one, and the whole difference between the two games' menu music is this branch.
            if (Add(entry.Name, entry.Data, keepStreamedBytes: true))
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
            // A loose file stays reopenable, so a streamed one keeps nothing but its path -- which for
            // I.G.I. 2's mission scores is the difference between holding five megabytes per level and
            // holding none.
            byte[] bytes = _fileSystem.ReadAllBytes(path);
            return Add(path, bytes, keepStreamedBytes: false);
        }
        catch (Exception error) when (error is IOException or EngineFatalException)
        {
            _logger.Warning(LogCategory, $"Sound '{path}' would not read: {error.Message}");
            return false;
        }
    }

    /// <summary>Files one sound, having first worked out from its bytes what kind of sound it is.</summary>
    /// <param name="path">The path the bytes came from, which is where the key is taken from.</param>
    /// <param name="bytes">The file's contents.</param>
    /// <param name="keepStreamedBytes">
    /// Whether a streamed sound must hold its packed bytes because there is no file to reopen. True for
    /// an archive member, false for a loose file.
    /// </param>
    /// <returns><see langword="true"/> when a sound was added.</returns>
    /// <remarks>
    /// The kind is read from the content rather than from the extension. The two games file the same
    /// track under <c>menu_music.wav</c> and <c>menu_music.mp3</c>, and both answer to the same key once
    /// the extension is stripped, so the name says nothing useful. The original's loader is no guide
    /// here either, since every sound it can load is ILSF.
    /// </remarks>
    private bool Add(string path, ReadOnlyMemory<byte> bytes, bool keepStreamedBytes)
    {
        bool isIlsf = IlsfHeader.IsIlsf(bytes.Span);
        if (!isIlsf && !MpegAudioHeader.IsMpegAudio(bytes.Span))
        {
            // Not a sound at all. Archives hold other kinds of member and the original's directory
            // walker ignores whatever it does not recognise, so this is silent rather than a warning.
            return false;
        }

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

        try
        {
            if (!isIlsf)
            {
                MpegAudioHeader mpeg = MpegAudioHeader.Read(bytes.Span);
                _sounds[key] = SoundEntry.Mpeg(
                    key,
                    path,
                    mpeg,
                    keepStreamedBytes ? bytes : ReadOnlyMemory<byte>.Empty,
                    bytes.Length);
                return true;
            }

            IlsfHeader header = IlsfHeader.Read(bytes.Span);

            // A streamed sound keeps its path and nothing else, which is the whole point of the storage
            // code: the music track is three megabytes packed and about thirteen expanded, and the
            // original never holds either. Retail files no streamed ILSF sound inside an archive, so the
            // keep-bytes case does not arise for one -- but honouring it costs nothing and stops a
            // replacement pack that does it from producing an entry that can never be opened.
            _sounds[key] = header.IsStreamed && !keepStreamedBytes
                ? new SoundEntry(key, path, header, ReadOnlyMemory<byte>.Empty)
                : new SoundEntry(key, path, header, bytes);

            return true;
        }
        catch (Exception error) when (error is IlsfFormatException or MpegAudioFormatException)
        {
            _logger.Warning(LogCategory, $"Sound '{path}' is not a valid format: {error.Message}");
            return false;
        }
    }
}
