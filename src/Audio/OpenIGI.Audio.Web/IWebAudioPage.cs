namespace OpenIGI.Audio.Web;

/// <summary>The page's audio graph, as the browser's device sees it.</summary>
/// <remarks>
/// <para>
/// The device's whole view of the Web Audio API. The page owns the context, the buses, one node chain
/// per voice slot and the decoded buffers; the device names them by number — a voice by its slot index,
/// a buffer by the id the page gave it — and tells the page what to do with each. Nothing crosses back
/// but numbers and flags, which is what keeps the interop cheap and the page free of game types.
/// </para>
/// <para>
/// An interface so the device's bookkeeping runs and is tested off a browser; the one implementation
/// that talks to a page is <see cref="WebAudioPage"/>.
/// </para>
/// </remarks>
public interface IWebAudioPage
{
    /// <summary>Opens the audio context and makes the voice chains.</summary>
    /// <param name="sampleRate">The output rate to ask for, or zero for the browser's own.</param>
    /// <param name="voiceCount">How many voice slots to make.</param>
    /// <returns>The rate the context runs at, or zero when the browser has no audio.</returns>
    /// <remarks>
    /// The context may open suspended: browsers refuse to make sound before the person has clicked or
    /// pressed a key on the page. The page resumes it on the first gesture, and the device need not
    /// know — a suspended context accepts every call and sounds once allowed to.
    /// </remarks>
    int Open(int sampleRate, int voiceCount);

    /// <summary>Closes the context and drops everything the page holds.</summary>
    void Close();

    /// <summary>Places the listener.</summary>
    /// <param name="x">The position's X, in metres.</param>
    /// <param name="y">The position's Y, in metres.</param>
    /// <param name="z">The position's Z, in metres.</param>
    /// <param name="forwardX">The facing direction's X.</param>
    /// <param name="forwardY">The facing direction's Y.</param>
    /// <param name="forwardZ">The facing direction's Z.</param>
    /// <param name="upX">The up direction's X.</param>
    /// <param name="upY">The up direction's Y.</param>
    /// <param name="upZ">The up direction's Z.</param>
    void SetListener(
        double x, double y, double z,
        float forwardX, float forwardY, float forwardZ,
        float upX, float upY, float upZ);

    /// <summary>Sets the gain of one bus.</summary>
    /// <param name="category">The bus, as a <see cref="SoundCategory"/> ordinal.</param>
    /// <param name="gain">The linear gain.</param>
    void SetBusGain(int category, float gain);

    /// <summary>Decodes PCM into a buffer the page keeps.</summary>
    /// <param name="sampleRate">The PCM's rate.</param>
    /// <param name="channels">One or two.</param>
    /// <param name="bitsPerSample">Eight (unsigned) or sixteen (signed).</param>
    /// <param name="pcm">The samples, channel-interleaved. Read during the call only.</param>
    /// <returns>The buffer's id, or zero when the page could not make one.</returns>
    int CreateBuffer(int sampleRate, int channels, int bitsPerSample, ReadOnlySpan<byte> pcm);

    /// <summary>Drops a buffer.</summary>
    /// <param name="buffer">The buffer's id; an unknown one is ignored.</param>
    void DestroyBuffer(int buffer);

    /// <summary>Aims a voice: its bus, level, rate and placement.</summary>
    /// <param name="voice">The slot index.</param>
    /// <param name="category">The bus, as a <see cref="SoundCategory"/> ordinal.</param>
    /// <param name="gain">The linear gain, already through the original's curve.</param>
    /// <param name="pitch">The playback rate multiplier.</param>
    /// <param name="positional">Whether the voice is placed in the world (<paramref name="x"/> and the distances apply) or panned flat (<paramref name="pan"/> applies).</param>
    /// <param name="pan">The flat pan, minus one (left) to one (right).</param>
    /// <param name="x">The world position's X, in metres.</param>
    /// <param name="y">The world position's Y, in metres.</param>
    /// <param name="z">The world position's Z, in metres.</param>
    /// <param name="minimumDistance">Inside this the voice is at full gain.</param>
    /// <param name="maximumDistance">At this the voice has faded to silence.</param>
    /// <remarks>Called before a voice starts and again whenever it moves, so it must be cheap.</remarks>
    void SetVoice(
        int voice, int category, float gain, float pitch,
        bool positional, float pan,
        double x, double y, double z, float minimumDistance, float maximumDistance);

    /// <summary>Starts a resident buffer on a voice.</summary>
    /// <param name="voice">The slot index.</param>
    /// <param name="buffer">The buffer's id.</param>
    /// <param name="looping">Whether it repeats until stopped.</param>
    void Play(int voice, int buffer, bool looping);

    /// <summary>Makes a voice a streaming one, ready to be fed chunks.</summary>
    /// <param name="voice">The slot index.</param>
    /// <param name="sampleRate">The chunks' rate.</param>
    /// <param name="channels">The chunks' channel count.</param>
    void StartStream(int voice, int sampleRate, int channels);

    /// <summary>Schedules one chunk of a stream after the ones already queued.</summary>
    /// <param name="voice">The slot index.</param>
    /// <param name="pcm">Sixteen-bit signed samples, channel-interleaved. Read during the call only.</param>
    void QueueStream(int voice, ReadOnlySpan<byte> pcm);

    /// <summary>Counts the chunks of a stream that have not finished sounding.</summary>
    /// <param name="voice">The slot index.</param>
    /// <returns>The number playing or waiting to play.</returns>
    int QueuedStreamChunks(int voice);

    /// <summary>Gets whether a resident voice has played to its end.</summary>
    /// <param name="voice">The slot index.</param>
    /// <returns><see langword="true"/> once nothing on the voice is sounding or scheduled.</returns>
    bool HasEnded(int voice);

    /// <summary>Stops whatever a voice is sounding.</summary>
    /// <param name="voice">The slot index.</param>
    void StopVoice(int voice);

    /// <summary>Starts listening to the microphone, asking the browser's leave the first time.</summary>
    /// <param name="sampleRate">The rate the samples are wanted at.</param>
    /// <returns><see langword="true"/> when there is a context to listen through.</returns>
    bool OpenMicrophone(int sampleRate) => false;

    /// <summary>Takes the microphone's samples heard since the last call.</summary>
    /// <param name="destination">Where to.</param>
    /// <returns>How many were taken.</returns>
    int ReadMicrophone(Span<short> destination) => 0;

    /// <summary>Stops listening to the microphone.</summary>
    void CloseMicrophone()
    {
    }
}
