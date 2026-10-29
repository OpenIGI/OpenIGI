using System.Runtime.InteropServices;
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
    private readonly IOpenIgiLogger _logger;
    private readonly Dictionary<string, AudioBufferHandle> _buffers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (AudioFormat Format, byte[] Pcm)> _external = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AudioBufferHandle> _externalBuffers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _reportedMissing = new(StringComparer.Ordinal);
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="SoundSystem"/> class.</summary>
    /// <param name="device">Where sounds are played.</param>
    /// <param name="bank">Where sounds are found.</param>
    /// <param name="fileSystem">Used to reopen streamed sounds, which the bank holds only a path for.</param>
    /// <param name="logger">Where to report a name nothing answers to.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public SoundSystem(IAudioDevice device, SoundBank bank, VirtualFileSystem fileSystem, IOpenIgiLogger? logger = null)
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

    /// <summary>Registers a sound that belongs to the port itself rather than to the retail data.</summary>
    /// <param name="name">The name <see cref="PlayExternal"/> plays it by.</param>
    /// <param name="format">The PCM's format.</param>
    /// <param name="pcm">The raw PCM, kept so a mission teardown can re-upload it lazily.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="pcm"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The remaster track's own assets — an added impact sound, say — arrive this way instead of
    /// being spliced into the retail bank, so the bank remains exactly what the game data says.
    /// </remarks>
    public void RegisterExternal(string name, AudioFormat format, byte[] pcm)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(pcm);

        if (_externalBuffers.Remove(name, out AudioBufferHandle stale))
        {
            _device.DestroyBuffer(stale);
        }

        _external[name] = (format, pcm);
    }

    /// <summary>Gets whether an external sound is registered under a name.</summary>
    /// <param name="name">The name.</param>
    /// <returns><see langword="true"/> when <see cref="PlayExternal"/> would find it.</returns>
    public bool HasExternal(string name) => _external.ContainsKey(name);

    /// <summary>Plays a registered external sound, positionally when given a place.</summary>
    /// <param name="name">The name it was registered under.</param>
    /// <param name="positionInUnits">Where the emitter is, in engine units, or <see langword="null"/> for flat.</param>
    /// <param name="gain">The gain before distance.</param>
    /// <param name="priority">Its importance when the voice pool is full.</param>
    /// <param name="pitch">The playback ratio; 1 is the sound's own rate.</param>
    /// <param name="minimumDistanceInMetres">The distance within which it plays at full gain.</param>
    /// <param name="maximumDistanceInMetres">The distance beyond which it is silent.</param>
    /// <returns>The voice, or <see cref="AudioVoiceHandle.None"/> when the name is unregistered.</returns>
    public AudioVoiceHandle PlayExternal(
        string name,
        Vector3d? positionInUnits = null,
        float gain = 1f,
        int priority = 0,
        float pitch = 1f,
        float minimumDistanceInMetres = 2f,
        float maximumDistanceInMetres = 60f)
    {
        if (string.IsNullOrEmpty(name) || !_external.TryGetValue(name, out (AudioFormat Format, byte[] Pcm) sound))
        {
            return AudioVoiceHandle.None;
        }

        if (!_externalBuffers.TryGetValue(name, out AudioBufferHandle buffer))
        {
            buffer = _device.CreateBuffer(sound.Format, sound.Pcm);
            _externalBuffers[name] = buffer;
        }

        if (!buffer.IsValidHandle)
        {
            return AudioVoiceHandle.None;
        }

        VoiceParameters parameters = VoiceParameters.Default with
        {
            Gain = gain,
            Priority = priority,
            Pitch = pitch,
        };

        if (positionInUnits is { } emitter)
        {
            parameters = parameters.At(ToMetres(emitter), minimumDistanceInMetres, maximumDistanceInMetres);
        }

        return _device.Play(buffer, parameters);
    }

    /// <summary>Gets or sets the definitions names are resolved through.</summary>
    /// <remarks>
    /// Until this is set, <see cref="PlayDefinition"/> can find nothing: game code names definitions,
    /// never bank keys.
    /// </remarks>
    public ISoundDefinitionLookup? Definitions { get; set; }

    /// <summary>Gets or sets a diagnostic observer told every definition asked for by name, before it is resolved.</summary>
    /// <remarks>What a differential trace sets to line the port's sounds up with the original's <c>Sound_Play</c> (<c>0x4E6B00</c>).</remarks>
    public Action<string>? DefinitionPlayed { get; set; }

    /// <summary>Plays a sound the way game code names it — through the definition layer.</summary>
    /// <param name="name">The definition name, such as <c>ai_death_1</c> or <c>jump_1</c>.</param>
    /// <param name="positionInUnits">
    /// Where the emitter is, in engine units, or <see langword="null"/> for a sound with no place in
    /// the world — the player's own weapon, an interface click.
    /// </param>
    /// <param name="gain">A further gain on top of the definition's own.</param>
    /// <param name="priority">Its importance when the voice pool is full.</param>
    /// <returns>The voice, or <see cref="AudioVoiceHandle.None"/>.</returns>
    /// <remarks>
    /// <para>
    /// This is the port's <c>0x4E7200</c>: resolve the name, refuse anything but a plain sound, then
    /// play the definition's bank key with the definition's own distances, gain and loop flag. The
    /// distances are <b>authored in metres</b> and used as such.
    /// </para>
    /// <para>
    /// The definition's <see cref="SoundDefinition.Offset"/> is added to the emitter position, because
    /// that is what it is — an offset in the emitter's frame, not a world position. The original also
    /// rotates it by the emitter's orientation; every retail offset read so far is zero, so nothing
    /// yet depends on the rotation and it is not applied here.
    /// </para>
    /// </remarks>
    /// <param name="pitch">The playback ratio; 1 is the sound's own rate.</param>
    /// <param name="looping">An optional call-site override for direct one-shot playback.</param>
    public AudioVoiceHandle PlayDefinition(
        string name,
        Vector3d? positionInUnits = null,
        float gain = 1f,
        int priority = 0,
        float pitch = 1f,
        bool? looping = null)
    {
        DefinitionPlayed?.Invoke(name);
        if (Definitions is null || !Definitions.TryResolve(name, out SoundDefinition? definition) || definition is null)
        {
            if (_reportedMissing.Add(name ?? string.Empty))
            {
                _logger.Debug(LogCategory, $"No sound definition named '{name}'.");
            }

            return AudioVoiceHandle.None;
        }

        if (!definition.IsPlayable)
        {
            // 0x4E7200 rejects a group, a graph and a trigger outright rather than playing something
            // arbitrary from inside them.
            return AudioVoiceHandle.None;
        }

        return Play(definition.BankKey, DefinitionParameters(
            definition,
            positionInUnits,
            gain,
            priority,
            pitch,
            looping));
    }

    /// <summary>Updates a definition-backed voice without restarting its playback.</summary>
    /// <remarks>
    /// Retail's sound update asks the owning task for its current transform every tick. Moving
    /// platforms therefore carry their looped sound with them; updating only at the start leaves a
    /// multi-floor elevator's travel sound at the floor where it began.
    /// </remarks>
    public void SetDefinitionVoicePosition(
        string name,
        AudioVoiceHandle voice,
        Vector3d positionInUnits,
        float gain = 1f,
        int priority = 0,
        float pitch = 1f,
        bool? looping = null)
    {
        if (Definitions is null
            || !Definitions.TryResolve(name, out SoundDefinition? definition)
            || definition is null
            || !definition.IsPlayable)
        {
            return;
        }

        SetVoiceParameters(voice, DefinitionParameters(
            definition,
            positionInUnits,
            gain,
            priority,
            pitch,
            looping));
    }

    private static VoiceParameters DefinitionParameters(
        SoundDefinition definition,
        Vector3d? positionInUnits,
        float gain,
        int priority,
        float pitch,
        bool? looping)
    {
        VoiceParameters parameters = VoiceParameters.Default with
        {
            Gain = definition.Volume * gain,
            Looping = looping ?? definition.Loop,
            Priority = priority,
            Pitch = pitch,
        };

        return positionInUnits is { } emitter
            ? parameters.At(
                ToMetres(emitter + definition.Offset),
                definition.MinimumDistanceMetres,
                definition.MaximumDistanceMetres)
            : parameters;
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

        // The uploads go; the registered PCM stays, so the next mission re-uploads lazily exactly
        // like the bank's own sounds.
        foreach (AudioBufferHandle buffer in _externalBuffers.Values)
        {
            _device.DestroyBuffer(buffer);
        }

        _buffers.Clear();
        _externalBuffers.Clear();
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
        Stream? stream = OpenStreamed(entry);
        if (stream is null)
        {
            _logger.Warning(LogCategory, $"Streamed sound '{entry.Name}' is no longer at '{entry.Origin}'.");
            return AudioVoiceHandle.None;
        }

        try
        {
            ISampleSource source = entry.Codec == SoundCodec.Mpeg
                ? MpegSampleSource.Open(stream)
                : IlsfSampleSource.Open(stream);

            return _device.PlayStream(source, parameters);
        }
        catch (Exception error) when (error is IlsfFormatException or MpegAudioFormatException)
        {
            stream.Dispose();
            _logger.Warning(LogCategory, $"Streamed sound '{entry.Name}' would not open: {error.Message}");
            return AudioVoiceHandle.None;
        }
    }

    /// <summary>Opens the bytes a streamed sound decodes from, wherever they are.</summary>
    /// <remarks>
    /// Two cases, and which one applies is what <see cref="SoundEntry.Data"/> says. A sound the bank
    /// left on disk is reopened by path, as the original does. A sound the bank had to keep -- an MPEG
    /// track inside an archive, which has no file of its own -- is read from the bytes it kept, over
    /// the archive's own array rather than a copy of it, so playing a three-minute track does not
    /// duplicate eight megabytes every time it starts.
    /// </remarks>
    private Stream? OpenStreamed(SoundEntry entry)
    {
        if (entry.Data.IsEmpty)
        {
            return _fileSystem.TryOpenRead(entry.Origin, out Stream? file) ? file : null;
        }

        return MemoryMarshal.TryGetArray(entry.Data, out ArraySegment<byte> segment) && segment.Array is not null
            ? new MemoryStream(segment.Array, segment.Offset, segment.Count, writable: false)
            : new MemoryStream(entry.Data.ToArray(), writable: false);
    }
}
