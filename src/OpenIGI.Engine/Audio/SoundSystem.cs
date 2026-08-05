using OpenIGI.Audio;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Sound;

namespace OpenIGI.Engine.Audio;

/// <summary>
/// Plays sounds by the names the game uses: the join between the <see cref="SoundBank"/> and an
/// <see cref="IAudioDevice"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is what every game-side call site talks to. It owns the decision the original's loader makes
/// once at load time and this port makes at play time — whether a sound is handed over as a resident
/// buffer or opened as a stream — and it caches the uploads, so the hundredth rifle shot of a firefight
/// costs a voice and nothing else.
/// </para>
/// <para>
/// Distances are in metres, as <see cref="VoiceParameters"/> requires. A caller holding engine units
/// divides by <see cref="UnitsPerMetre"/> first.
/// </para>
/// </remarks>
public sealed class SoundSystem : IDisposable
{
    /// <summary>Engine units per metre, the factor the whole port scales world data by.</summary>
    public const double UnitsPerMetre = 4096.0;

    private const string LogCategory = "Audio";

    private readonly IAudioDevice _device;
    private readonly SoundBank _bank;
    private readonly VirtualFileSystem _fileSystem;
    private readonly IIgiLogger _logger;
    private readonly Dictionary<string, AudioBufferHandle> _buffers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _reportedMissing = new(StringComparer.Ordinal);
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="SoundSystem"/> class.</summary>
    /// <param name="device">Where sounds are played.</param>
    /// <param name="bank">Where sounds are found.</param>
    /// <param name="fileSystem">Used to reopen streamed sounds, which the bank holds only a path for.</param>
    /// <param name="logger">Where to report a name nothing answers to.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public SoundSystem(IAudioDevice device, SoundBank bank, VirtualFileSystem fileSystem, IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(bank);
        ArgumentNullException.ThrowIfNull(fileSystem);

        _device = device;
        _bank = bank;
        _fileSystem = fileSystem;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Gets the device sounds are played on.</summary>
    public IAudioDevice Device => _device;

    /// <summary>Gets the bank sounds are found in.</summary>
    public SoundBank Bank => _bank;

    /// <summary>Gets or sets where the listener is.</summary>
    public AudioListenerState Listener
    {
        get => _device.Listener;
        set => _device.Listener = value;
    }

    /// <summary>Converts a world position in engine units to the metres the device works in.</summary>
    /// <param name="units">A position in engine units.</param>
    /// <returns>The same position in metres.</returns>
    public static Vector3d ToMetres(Vector3d units) => units / UnitsPerMetre;

    /// <summary>Plays a sound by name.</summary>
    /// <param name="name">The sound name, as a script or a data file spells it.</param>
    /// <param name="parameters">How to play it.</param>
    /// <returns>The voice, or <see cref="AudioVoiceHandle.None"/> when the sound is unknown or no voice was free.</returns>
    public AudioVoiceHandle Play(string name, in VoiceParameters parameters)
    {
        if (string.IsNullOrEmpty(name))
        {
            return AudioVoiceHandle.None;
        }

        if (!_bank.TryGet(name, out SoundEntry entry))
        {
            // Once per name. A missing sound is usually a gap in what has been mounted rather than a
            // fault at the call site, and a per-shot warning would bury everything else in the log.
            if (_reportedMissing.Add(name.ToUpperInvariant()))
            {
                _logger.Debug(LogCategory, $"No sound named '{name}'.");
            }

            return AudioVoiceHandle.None;
        }

        return entry.IsStreamed ? PlayStreamed(entry, parameters) : PlayResident(entry, parameters);
    }

    /// <summary>Plays a sound at a place in the world.</summary>
    /// <param name="name">The sound name.</param>
    /// <param name="positionInUnits">Where it is, in engine units.</param>
    /// <param name="minimumDistanceInMetres">The distance within which it plays at full gain.</param>
    /// <param name="maximumDistanceInMetres">The distance beyond which it is silent.</param>
    /// <param name="category">The bus it mixes into.</param>
    /// <param name="gain">Its own gain before distance.</param>
    /// <param name="priority">Its importance when the voice pool is full.</param>
    /// <returns>The voice, or <see cref="AudioVoiceHandle.None"/>.</returns>
    public AudioVoiceHandle PlayAt(
        string name,
        Vector3d positionInUnits,
        float minimumDistanceInMetres,
        float maximumDistanceInMetres,
        SoundCategory category = SoundCategory.Effects,
        float gain = 1f,
        int priority = 0)
    {
        VoiceParameters parameters = (VoiceParameters.Default with
        {
            Category = category,
            Gain = gain,
            Priority = priority,
        }).At(ToMetres(positionInUnits), minimumDistanceInMetres, maximumDistanceInMetres);

        return Play(name, parameters);
    }

    /// <summary>Determines whether a voice is still sounding.</summary>
    /// <param name="voice">The voice.</param>
    /// <returns><see langword="true"/> while it sounds.</returns>
    public bool IsPlaying(AudioVoiceHandle voice) => _device.IsPlaying(voice);

    /// <summary>Re-aims a sounding voice.</summary>
    /// <param name="voice">The voice.</param>
    /// <param name="parameters">The new parameters.</param>
    public void SetVoiceParameters(AudioVoiceHandle voice, in VoiceParameters parameters) =>
        _device.SetVoiceParameters(voice, parameters);

    /// <summary>Stops a voice.</summary>
    /// <param name="voice">The voice.</param>
    public void Stop(AudioVoiceHandle voice) => _device.StopVoice(voice);

    /// <summary>Stops every voice.</summary>
    public void StopAll() => _device.StopAll();

    /// <summary>Advances the device.</summary>
    public void Update() => _device.Update();

    /// <summary>Releases every uploaded buffer, leaving the bank alone.</summary>
    /// <remarks>Called when a mission is torn down; the next one uploads what it uses again.</remarks>
    public void UnloadBuffers()
    {
        _device.StopAll();

        foreach (AudioBufferHandle buffer in _buffers.Values)
        {
            _device.DestroyBuffer(buffer);
        }

        _buffers.Clear();
        _reportedMissing.Clear();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UnloadBuffers();
    }

    private AudioVoiceHandle PlayResident(SoundEntry entry, in VoiceParameters parameters)
    {
        if (!_buffers.TryGetValue(entry.Name, out AudioBufferHandle buffer))
        {
            IlsfFile sound;
            try
            {
                sound = IlsfFile.Read(entry.Data);
            }
            catch (IlsfFormatException error)
            {
                _logger.Warning(LogCategory, $"Sound '{entry.Name}' would not decode: {error.Message}");
                return AudioVoiceHandle.None;
            }

            AudioFormat format = new(sound.SampleRate, sound.Channels, sound.BitsPerSample);
            buffer = _device.CreateBuffer(format, sound.DecodePcm().Span);
            _buffers[entry.Name] = buffer;
        }

        return buffer.IsValidHandle ? _device.Play(buffer, parameters) : AudioVoiceHandle.None;
    }

    private AudioVoiceHandle PlayStreamed(SoundEntry entry, in VoiceParameters parameters)
    {
        if (!_fileSystem.TryOpenRead(entry.Origin, out Stream? stream) || stream is null)
        {
            _logger.Warning(LogCategory, $"Streamed sound '{entry.Name}' is no longer at '{entry.Origin}'.");
            return AudioVoiceHandle.None;
        }

        try
        {
            return _device.PlayStream(IlsfSampleSource.Open(stream), parameters);
        }
        catch (IlsfFormatException error)
        {
            stream.Dispose();
            _logger.Warning(LogCategory, $"Streamed sound '{entry.Name}' would not open: {error.Message}");
            return AudioVoiceHandle.None;
        }
    }
}
