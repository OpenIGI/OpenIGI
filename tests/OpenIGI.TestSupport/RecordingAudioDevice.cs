using OpenIGI.Audio;

namespace OpenIGI.TestSupport;

/// <summary>
/// An <see cref="IAudioDevice"/> that records what it was asked to do instead of making a sound.
/// </summary>
/// <remarks>
/// The audio counterpart of <c>RecordingGraphicsDevice</c>, and the thing that makes every sound hook
/// testable without a sound card: a test can assert that firing a weapon started exactly one voice on
/// the effects bus, at the right position, without any of it being audible.
/// </remarks>
public sealed class RecordingAudioDevice : IAudioDevice
{
    private readonly float[] _categoryGains = [1f, 1f, 1f, 1f];
    private readonly Dictionary<int, VoiceParameters> _active = [];
    private readonly List<PlayedVoice> _played = [];
    private readonly List<AudioFormat> _createdBuffers = [];
    private int _nextHandle = 1;

    /// <summary>Gets every voice that has been started, in order, including ones since stopped.</summary>
    public IReadOnlyList<PlayedVoice> Played => _played;

    /// <summary>Gets the format of every buffer that has been uploaded, in order.</summary>
    public IReadOnlyList<AudioFormat> CreatedBuffers => _createdBuffers;

    /// <summary>Gets the number of times <see cref="StopAll"/> was called.</summary>
    public int StopAllCount { get; private set; }

    /// <summary>Gets the number of times <see cref="Update"/> was called.</summary>
    public int UpdateCount { get; private set; }

    /// <summary>Gets or sets a value indicating whether the device reports itself as available.</summary>
    public bool IsAvailable { get; set; } = true;

    /// <summary>Gets or sets how many voices the pool holds.</summary>
    public int VoiceCount { get; set; } = 32;

    /// <inheritdoc/>
    public int ActiveVoiceCount => _active.Count;

    /// <inheritdoc/>
    public AudioListenerState Listener { get; set; } = AudioListenerState.Default;

    /// <summary>Gets the sample sources handed to <see cref="PlayStream"/> that have not been disposed.</summary>
    public int LiveStreamCount { get; private set; }

    /// <inheritdoc/>
    public void SetCategoryGain(SoundCategory category, float gain) =>
        _categoryGains[(int)category] = gain;

    /// <inheritdoc/>
    public float GetCategoryGain(SoundCategory category) => _categoryGains[(int)category];

    /// <inheritdoc/>
    public AudioBufferHandle CreateBuffer(AudioFormat format, ReadOnlySpan<byte> pcm)
    {
        format.Validate(nameof(format));
        _createdBuffers.Add(format);
        return new AudioBufferHandle(_nextHandle++);
    }

    /// <inheritdoc/>
    public void DestroyBuffer(AudioBufferHandle buffer)
    {
    }

    /// <inheritdoc/>
    public AudioVoiceHandle Play(AudioBufferHandle buffer, in VoiceParameters parameters)
    {
        if (_active.Count >= VoiceCount)
        {
            return AudioVoiceHandle.None;
        }

        int handle = _nextHandle++;
        _active[handle] = parameters;
        _played.Add(new PlayedVoice(new AudioVoiceHandle(handle), buffer, parameters, IsStream: false));
        return new AudioVoiceHandle(handle);
    }

    /// <inheritdoc/>
    public AudioVoiceHandle PlayStream(ISampleSource source, in VoiceParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (_active.Count >= VoiceCount)
        {
            source.Dispose();
            return AudioVoiceHandle.None;
        }

        LiveStreamCount++;
        int handle = _nextHandle++;
        _active[handle] = parameters;
        _played.Add(new PlayedVoice(new AudioVoiceHandle(handle), AudioBufferHandle.None, parameters, IsStream: true));
        return new AudioVoiceHandle(handle);
    }

    /// <inheritdoc/>
    public bool IsPlaying(AudioVoiceHandle voice) => _active.ContainsKey(voice.Value);

    /// <inheritdoc/>
    public void SetVoiceParameters(AudioVoiceHandle voice, in VoiceParameters parameters)
    {
        if (_active.ContainsKey(voice.Value))
        {
            _active[voice.Value] = parameters;
        }
    }

    /// <summary>Returns the parameters a sounding voice currently has.</summary>
    /// <param name="voice">The voice.</param>
    /// <returns>The parameters, or <see langword="null"/> when the voice is not sounding.</returns>
    public VoiceParameters? Current(AudioVoiceHandle voice) =>
        _active.TryGetValue(voice.Value, out VoiceParameters parameters) ? parameters : null;

    /// <inheritdoc/>
    public void StopVoice(AudioVoiceHandle voice) => _active.Remove(voice.Value);

    /// <inheritdoc/>
    public void StopAll()
    {
        StopAllCount++;
        _active.Clear();
    }

    /// <inheritdoc/>
    public void Update() => UpdateCount++;

    /// <summary>Forgets everything recorded so far, leaving sounding voices alone.</summary>
    public void ClearRecord()
    {
        _played.Clear();
        _createdBuffers.Clear();
        StopAllCount = 0;
        UpdateCount = 0;
    }

    /// <inheritdoc/>
    public void Dispose() => _active.Clear();

    /// <summary>One voice this device was asked to start.</summary>
    /// <param name="Voice">The handle it was given.</param>
    /// <param name="Buffer">The buffer it plays, or none for a stream.</param>
    /// <param name="Parameters">The parameters it started with.</param>
    /// <param name="IsStream">Whether it came from <see cref="PlayStream"/>.</param>
    public readonly record struct PlayedVoice(
        AudioVoiceHandle Voice,
        AudioBufferHandle Buffer,
        VoiceParameters Parameters,
        bool IsStream);
}
