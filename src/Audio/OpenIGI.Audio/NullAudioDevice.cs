namespace OpenIGI.Audio;

/// <summary>An audio device that accepts everything and makes no sound.</summary>
/// <remarks>
/// <para>
/// The null object the rest of the engine defaults to, in the same spirit as the renderer's null
/// scheduler. It is what runs on a machine with no sound hardware, in continuous integration, and in
/// every headless test — so no call site needs to ask whether audio exists.
/// </para>
/// <para>
/// It still hands out real handles and tracks how many voices are notionally sounding, so a caller that
/// checks <see cref="IsPlaying"/> before starting a looping sound behaves the same silently as it does
/// aloud. Voices retire on the next <see cref="Update"/>, since nothing is timing them.
/// </para>
/// </remarks>
public sealed class NullAudioDevice : IAudioDevice
{
    /// <summary>The shared instance.</summary>
    public static readonly NullAudioDevice Instance = new();

    private readonly MixBuses _buses = new();
    private readonly HashSet<int> _voices = [];
    private int _nextHandle = 1;

    /// <summary>Initializes a new instance of the <see cref="NullAudioDevice"/> class.</summary>
    public NullAudioDevice()
    {
    }

    /// <inheritdoc/>
    public bool IsAvailable => false;

    /// <inheritdoc/>
    public int VoiceCount => 32;

    /// <inheritdoc/>
    public int ActiveVoiceCount => _voices.Count;

    /// <inheritdoc/>
    public AudioListenerState Listener { get; set; } = AudioListenerState.Default;

    /// <inheritdoc/>
    public void SetCategoryGain(SoundCategory category, float gain) => _buses.Set(category, gain);

    /// <inheritdoc/>
    public float GetCategoryGain(SoundCategory category) => _buses.Get(category);

    /// <inheritdoc/>
    public AudioBufferHandle CreateBuffer(AudioFormat format, ReadOnlySpan<byte> pcm)
    {
        format.Validate(nameof(format));
        return new AudioBufferHandle(_nextHandle++);
    }

    /// <inheritdoc/>
    public void DestroyBuffer(AudioBufferHandle buffer)
    {
    }

    /// <inheritdoc/>
    public AudioVoiceHandle Play(AudioBufferHandle buffer, in VoiceParameters parameters) => Start();

    /// <inheritdoc/>
    public AudioVoiceHandle PlayStream(ISampleSource source, in VoiceParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(source);

        // The contract says the device takes ownership, and a silent device must honour that or a
        // caller leaks a file handle every time it starts the music.
        source.Dispose();
        return Start();
    }

    /// <inheritdoc/>
    public bool IsPlaying(AudioVoiceHandle voice) => _voices.Contains(voice.Value);

    /// <inheritdoc/>
    public void SetVoiceParameters(AudioVoiceHandle voice, in VoiceParameters parameters)
    {
    }

    /// <inheritdoc/>
    public void StopVoice(AudioVoiceHandle voice) => _voices.Remove(voice.Value);

    /// <inheritdoc/>
    public void StopAll() => _voices.Clear();

    /// <inheritdoc/>
    public void Update() => _voices.Clear();

    /// <inheritdoc/>
    public void Dispose() => _voices.Clear();

    private AudioVoiceHandle Start()
    {
        int handle = _nextHandle++;
        _voices.Add(handle);
        return new AudioVoiceHandle(handle);
    }
}
