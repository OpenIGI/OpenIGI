using OpenIGI.Audio.Web;

namespace OpenIGI.Audio.Tests;

/// <summary>A page that records what the device asked of it and answers as a browser would.</summary>
/// <remarks>
/// A buffer is an id and a byte count; a voice is the sources it has been given. A test finishes a
/// resident sound or a stream chunk by hand, which is what the browser's <c>onended</c> does.
/// </remarks>
internal sealed class FakeWebAudioPage : IWebAudioPage
{
    private int _nextBufferId = 1;

    /// <summary>Gets or sets the rate <see cref="Open"/> answers with; zero refuses the context.</summary>
    public int SampleRateToOpen { get; set; } = 48000;

    public int OpenCalls { get; private set; }

    public int CloseCalls { get; private set; }

    public int VoiceCount { get; private set; }

    public Dictionary<int, int> BufferBytes { get; } = [];

    public float[] BusGains { get; } = [1f, 1f, 1f, 1f];

    public Dictionary<int, VoiceState> Voices { get; } = [];

    public List<string> Log { get; } = [];

    public (double X, double Y, double Z, float ForwardX, float ForwardY, float ForwardZ, float UpX, float UpY, float UpZ) Listener { get; private set; }

    public int Open(int sampleRate, int voiceCount)
    {
        OpenCalls++;
        VoiceCount = voiceCount;
        Log.Add($"open {sampleRate} {voiceCount}");
        return SampleRateToOpen;
    }

    public void Close()
    {
        CloseCalls++;
        Log.Add("close");
    }

    public void SetListener(
        double x, double y, double z,
        float forwardX, float forwardY, float forwardZ,
        float upX, float upY, float upZ) =>
        Listener = (x, y, z, forwardX, forwardY, forwardZ, upX, upY, upZ);

    public void SetBusGain(int category, float gain)
    {
        BusGains[category] = gain;
        Log.Add($"bus {category} {gain:0.###}");
    }

    public int CreateBuffer(int sampleRate, int channels, int bitsPerSample, ReadOnlySpan<byte> pcm)
    {
        if (pcm.IsEmpty)
        {
            return 0;
        }

        int id = _nextBufferId++;
        BufferBytes[id] = pcm.Length;
        Log.Add($"buffer {id} {sampleRate} {channels} {bitsPerSample} {pcm.Length}");
        return id;
    }

    public void DestroyBuffer(int buffer)
    {
        BufferBytes.Remove(buffer);
        Log.Add($"destroy {buffer}");
    }

    public void SetVoice(
        int voice, int category, float gain, float pitch,
        bool positional, float pan,
        double x, double y, double z, float minimumDistance, float maximumDistance)
    {
        VoiceState state = State(voice);
        state.Category = category;
        state.Gain = gain;
        state.Pitch = pitch;
        state.Positional = positional;
        state.Pan = pan;
        state.Position = (x, y, z);
        state.Distances = (minimumDistance, maximumDistance);
        Log.Add($"set {voice} {category} {gain:0.###} {pitch:0.###} {(positional ? "3d" : "pan")} {pan:0.###}");
    }

    public void Play(int voice, int buffer, bool looping)
    {
        VoiceState state = State(voice);
        state.Buffer = buffer;
        state.Looping = looping;
        state.Stream = null;
        state.Chunks = 0;
        state.Ended = false;
        Log.Add($"play {voice} {buffer} {(looping ? "loop" : "once")}");
    }

    public void StartStream(int voice, int sampleRate, int channels)
    {
        VoiceState state = State(voice);
        state.Buffer = 0;
        state.Stream = (sampleRate, channels);
        state.Chunks = 0;
        state.ChunkBytes.Clear();
        state.Ended = false;
        Log.Add($"stream {voice} {sampleRate} {channels}");
    }

    public void QueueStream(int voice, ReadOnlySpan<byte> pcm)
    {
        VoiceState state = State(voice);
        state.Chunks++;
        state.ChunkBytes.Add(pcm.Length);
        Log.Add($"queue {voice} {pcm.Length}");
    }

    public int QueuedStreamChunks(int voice) => State(voice).Chunks;

    public bool HasEnded(int voice) => State(voice).Ended;

    public void StopVoice(int voice)
    {
        VoiceState state = State(voice);
        state.Buffer = 0;
        state.Stream = null;
        state.Chunks = 0;
        state.Ended = true;
        Log.Add($"stop {voice}");
    }

    /// <summary>Lets a resident sound reach its end, as the browser reports through <c>onended</c>.</summary>
    public void FinishVoice(int voice) => State(voice).Ended = true;

    /// <summary>Lets one chunk of a stream finish sounding.</summary>
    public void FinishChunk(int voice)
    {
        VoiceState state = State(voice);
        state.Chunks = Math.Max(0, state.Chunks - 1);
    }

    private VoiceState State(int voice)
    {
        if (!Voices.TryGetValue(voice, out VoiceState? state))
        {
            state = new VoiceState();
            Voices[voice] = state;
        }

        return state;
    }

    internal sealed class VoiceState
    {
        public int Category { get; set; }

        public float Gain { get; set; }

        public float Pitch { get; set; }

        public bool Positional { get; set; }

        public float Pan { get; set; }

        public (double X, double Y, double Z) Position { get; set; }

        public (float Minimum, float Maximum) Distances { get; set; }

        public int Buffer { get; set; }

        public bool Looping { get; set; }

        public (int SampleRate, int Channels)? Stream { get; set; }

        public int Chunks { get; set; }

        public List<int> ChunkBytes { get; } = [];

        public bool Ended { get; set; } = true;
    }
}
