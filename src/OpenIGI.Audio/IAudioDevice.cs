namespace OpenIGI.Audio;

/// <summary>
/// The seam between the game and whatever is making noise — the audio counterpart of
/// <c>IGraphicsDevice</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>OpenIGI.Engine</c> and <c>OpenIGI.Game</c> talk to this and never to a backend. The backend is
/// chosen in the composition root — OpenAL in <c>apps/OpenIGI.Desktop</c>, the Web Audio API in
/// <c>apps/OpenIGI.Web</c> — exactly as the renderer is.
/// </para>
/// <para>
/// The model is the original's, generalised only where it had to be. There is a fixed pool of voices —
/// the original allocates 32 of 76 bytes each in <c>Snd_Init</c> <c>0x4952F0</c> — and a request that
/// finds none free either steals a less important one or is dropped. Buffers are shared, so the same
/// sound backs any number of voices at once.
/// </para>
/// <para>
/// Everything here is called from the game thread. A device that needs a worker of its own — a streaming
/// pump, for instance — owns it privately and must not call back into game code.
/// </para>
/// <para>
/// <b>A device is always usable.</b> When no hardware can be opened, the implementation degrades to
/// silence rather than throwing: <see cref="IsAvailable"/> goes <see langword="false"/> and every call
/// still succeeds. A machine with no sound card must boot, play and pass its tests.
/// </para>
/// </remarks>
public interface IAudioDevice : IDisposable
{
    /// <summary>Gets a value indicating whether real hardware is behind this device.</summary>
    /// <remarks>
    /// <see langword="false"/> means the device is a working silence — the game runs, the calls all
    /// succeed and nothing is heard.
    /// </remarks>
    bool IsAvailable { get; }

    /// <summary>Gets the number of voices that can sound at once.</summary>
    /// <remarks>The original's number is 32. The enhanced profile raises it; the vanilla profile does not.</remarks>
    int VoiceCount { get; }

    /// <summary>Gets the number of voices currently sounding.</summary>
    int ActiveVoiceCount { get; }

    /// <summary>Gets or sets where the listener is and which way it faces.</summary>
    AudioListenerState Listener { get; set; }

    /// <summary>Sets the gain of one mixing bus.</summary>
    /// <param name="category">The bus.</param>
    /// <param name="gain">The linear gain, zero to one.</param>
    /// <remarks>These are the player profile's own volume settings, applied as the original applies them.</remarks>
    void SetCategoryGain(SoundCategory category, float gain);

    /// <summary>Gets the gain of one mixing bus.</summary>
    /// <param name="category">The bus.</param>
    /// <returns>The linear gain.</returns>
    float GetCategoryGain(SoundCategory category);

    /// <summary>Uploads PCM the device will keep until it is destroyed.</summary>
    /// <param name="format">The shape of <paramref name="pcm"/>.</param>
    /// <param name="pcm">The samples, channel-interleaved.</param>
    /// <returns>A handle to the buffer, or <see cref="AudioBufferHandle.None"/> when the device is unavailable.</returns>
    /// <exception cref="ArgumentException"><paramref name="format"/> is not one the device accepts.</exception>
    AudioBufferHandle CreateBuffer(AudioFormat format, ReadOnlySpan<byte> pcm);

    /// <summary>Releases a buffer.</summary>
    /// <param name="buffer">The buffer to release; an unknown handle is ignored.</param>
    /// <remarks>Voices still playing the buffer are stopped first.</remarks>
    void DestroyBuffer(AudioBufferHandle buffer);

    /// <summary>Starts a resident sound on a voice.</summary>
    /// <param name="buffer">The buffer to play.</param>
    /// <param name="parameters">How to play it.</param>
    /// <returns>
    /// A handle to the voice, or <see cref="AudioVoiceHandle.None"/> when no voice was free and none
    /// could be stolen.
    /// </returns>
    AudioVoiceHandle Play(AudioBufferHandle buffer, in VoiceParameters parameters);

    /// <summary>Starts a streamed sound on a voice.</summary>
    /// <param name="source">The source to play. The device takes ownership and disposes it when the voice ends.</param>
    /// <param name="parameters">How to play it.</param>
    /// <returns>
    /// A handle to the voice, or <see cref="AudioVoiceHandle.None"/> when no voice was free — in which
    /// case <paramref name="source"/> is disposed.
    /// </returns>
    AudioVoiceHandle PlayStream(ISampleSource source, in VoiceParameters parameters);

    /// <summary>Determines whether a voice is still sounding.</summary>
    /// <param name="voice">The voice; a stale or unknown handle reads as not playing.</param>
    /// <returns><see langword="true"/> while the voice sounds.</returns>
    bool IsPlaying(AudioVoiceHandle voice);

    /// <summary>Re-aims a sounding voice.</summary>
    /// <param name="voice">The voice; a stale or unknown handle is ignored.</param>
    /// <param name="parameters">The new parameters.</param>
    /// <remarks>
    /// This is the per-tick call for anything that moves. The original does the same work in
    /// <c>0x496E60</c>, recomputing gain, pan and frequency for every sounding voice.
    /// </remarks>
    void SetVoiceParameters(AudioVoiceHandle voice, in VoiceParameters parameters);

    /// <summary>Stops a voice and frees it.</summary>
    /// <param name="voice">The voice; a stale or unknown handle is ignored.</param>
    void StopVoice(AudioVoiceHandle voice);

    /// <summary>Stops every voice.</summary>
    /// <remarks>Used on mission teardown, where the original tears down its whole voice array.</remarks>
    void StopAll();

    /// <summary>Advances the device: retires finished voices and feeds streams.</summary>
    /// <remarks>Called once a frame from the game thread.</remarks>
    void Update();
}
