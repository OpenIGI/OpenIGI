using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Audio.Web;

/// <summary>An <see cref="IAudioDevice"/> on the Web Audio API, through the page.</summary>
/// <remarks>
/// <para>
/// The same device the desktop has on OpenAL, over a different mixer. The voice pool, stealing, handles
/// and stream refills are <see cref="VoicePool{TVoice}"/>'s and the loudness curve is
/// <see cref="MixBuses"/>', both shared with the OpenAL backend; what is here is what the browser's
/// graph needs to be told, through <see cref="IWebAudioPage"/>.
/// </para>
/// <para>
/// <b>Every voice is a gain and a stereo pan; the page places nothing in 3D.</b> With
/// <see cref="AudioDeviceOptions.Spatial"/> on, a placed sound gets OpenAL's linear distance fade and
/// the sideways share of its direction as the pan (see <c>Place</c>); the page's own 3D panner pans by
/// azimuth alone and threw sounds below the head from one ear to the other. With it off, the gain and
/// pan the original computes (<see cref="VanillaSpatialiser"/>) go to the same stereo panner, and the
/// vanilla image is exact rather than OpenAL's unit-circle likeness: a stereo panner attenuates one
/// channel, which is what DirectSound's pan did. Either way the device re-aims the live placed voices
/// whenever the listener moves.
/// </para>
/// <para>
/// <b>Buses are real here.</b> The page has a gain node per <see cref="SoundCategory"/>, so a volume
/// slider moves one node rather than re-levelling every voice, and the voice gain handed over is the
/// quantised gain alone.
/// </para>
/// <para>
/// A browser refuses to make sound until the person has clicked or pressed a key on the page. The
/// context opens suspended and the page resumes it on the first gesture; until then the device is
/// available and every call succeeds, and everything started is held by the context and sounds once it
/// starts. The page drops a one-shot that would already have ended by then, so the menu's first clicks
/// do not arrive as a burst, while a loop or a stream — the theme — plays from its start.
/// </para>
/// <para>
/// If the page has no audio the object still constructs and behaves as silence, with
/// <see cref="IsAvailable"/> false. Nothing about a browser without sound may stop the game booting.
/// </para>
/// </remarks>
public sealed class WebAudioDevice : IAudioDevice
{
    private const string LogCategory = "Audio";

    // Within this of the head a placed sound is the player's own, and its pan eases to the centre; see Place.
    private const double NearFieldMetres = 1.0;

    private readonly IOpenIgiLogger _logger;
    private readonly AudioDeviceOptions _options;
    private readonly IWebAudioPage _page;
    private readonly MixBuses _buses = new();
    private readonly Dictionary<int, BufferRecord> _buffers = [];

    private VoicePool<VoiceSlot> _pool = VoicePool.Empty<VoiceSlot>();
    private int _nextBufferHandle = 1;
    private AudioListenerState _listener = AudioListenerState.Default;
    private bool _open;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="WebAudioDevice"/> class.</summary>
    /// <param name="options">How to open the device.</param>
    /// <param name="page">The page's audio graph.</param>
    /// <param name="logger">Where to report what happened, or <see langword="null"/> for silence.</param>
    public WebAudioDevice(AudioDeviceOptions options, IWebAudioPage page, IOpenIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(page);

        _logger = logger ?? NullLogger.Instance;
        _options = options;
        _page = page;

        try
        {
            Open();
        }
        catch (Exception error) when (error is JSException or InvalidOperationException or NotSupportedException)
        {
            // A browser without audio, or one that refused the context, is not a fatal condition.
            _logger.Warning(LogCategory, $"No audio device: {error.Message}. Running silently.");
            Shutdown();
        }
    }

    /// <inheritdoc/>
    public bool IsAvailable => _open;

    /// <inheritdoc/>
    public int VoiceCount => _pool.Count;

    /// <inheritdoc/>
    public int ActiveVoiceCount => _pool.ActiveCount;

    /// <summary>Gets the rate the context runs at, or zero when none opened.</summary>
    public int SampleRate { get; private set; }

    /// <inheritdoc/>
    public AudioListenerState Listener
    {
        get => _listener;
        set
        {
            _listener = value;
            ApplyListener();
        }
    }

    /// <inheritdoc/>
    public void SetCategoryGain(SoundCategory category, float gain)
    {
        _buses.Set(category, gain);
        if (_open)
        {
            // The bus is a node of its own on the page, so nothing per voice has to move.
            _page.SetBusGain((int)category, _buses.Get(category));
        }
    }

    /// <inheritdoc/>
    public float GetCategoryGain(SoundCategory category) => _buses.Get(category);

    /// <inheritdoc/>
    public AudioBufferHandle CreateBuffer(AudioFormat format, ReadOnlySpan<byte> pcm)
    {
        format.Validate(nameof(format));

        if (!_open)
        {
            return AudioBufferHandle.None;
        }

        int id = _page.CreateBuffer(format.SampleRate, format.Channels, format.BitsPerSample, pcm);
        if (id == 0)
        {
            return AudioBufferHandle.None;
        }

        int handle = _nextBufferHandle++;
        _buffers[handle] = new BufferRecord(id, format);
        return new AudioBufferHandle(handle);
    }

    /// <inheritdoc/>
    public void DestroyBuffer(AudioBufferHandle buffer)
    {
        if (!_open || !_buffers.Remove(buffer.Value, out BufferRecord record))
        {
            return;
        }

        // A voice still sounding the buffer would keep it alive on the page, so stop those first.
        foreach (VoiceSlot voice in _pool.Voices)
        {
            if (voice.IsActive && !voice.IsStreaming && voice.Buffer == buffer)
            {
                _pool.Retire(voice);
            }
        }

        _page.DestroyBuffer(record.PageId);
    }

    /// <inheritdoc/>
    public AudioVoiceHandle Play(AudioBufferHandle buffer, in VoiceParameters parameters)
    {
        if (!_open || !_buffers.TryGetValue(buffer.Value, out BufferRecord record))
        {
            return AudioVoiceHandle.None;
        }

        VoiceSlot? voice = _pool.Acquire(parameters.Priority);
        if (voice is null)
        {
            return AudioVoiceHandle.None;
        }

        voice.Buffer = buffer;
        voice.Parameters = parameters;

        ApplyParameters(voice, parameters);
        _page.Play(voice.Index, record.PageId, parameters.Looping);

        return voice.Handle;
    }

    /// <inheritdoc/>
    public IMicrophone? OpenMicrophone(int sampleRate) => _open && _page.OpenMicrophone(sampleRate) ? new PageMicrophone(_page, sampleRate) : null;

    // The page's microphone, as the game listens to it.
    private sealed class PageMicrophone(IWebAudioPage page, int sampleRate) : IMicrophone
    {
        public int SampleRate => sampleRate;

        public int Read(Span<short> destination) => page.ReadMicrophone(destination);

        public void Dispose() => page.CloseMicrophone();
    }

    /// <inheritdoc/>
    public AudioVoiceHandle PlayStream(ISampleSource source, in VoiceParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!_open)
        {
            source.Dispose();
            return AudioVoiceHandle.None;
        }

        VoiceSlot? voice = _pool.Acquire(parameters.Priority);
        if (voice is null)
        {
            source.Dispose();
            return AudioVoiceHandle.None;
        }

        voice.Stream = source;
        voice.StreamDrained = false;
        voice.Parameters = parameters;

        AudioFormat format = source.Format;
        _page.StartStream(voice.Index, format.SampleRate, format.Channels);
        ApplyParameters(voice, parameters);

        int queued = 0;
        while (queued < VoiceSlot.StreamBufferCount && QueueStreamChunk(voice))
        {
            queued++;
        }

        if (queued == 0)
        {
            _pool.Retire(voice);
            return AudioVoiceHandle.None;
        }

        return voice.Handle;
    }

    /// <inheritdoc/>
    public bool IsPlaying(AudioVoiceHandle voice) => _pool.TryResolve(voice, out _);

    /// <inheritdoc/>
    public void SetVoiceParameters(AudioVoiceHandle voice, in VoiceParameters parameters)
    {
        if (!_pool.TryResolve(voice, out VoiceSlot? slot))
        {
            return;
        }

        slot.Parameters = parameters;
        ApplyParameters(slot, parameters);
    }

    /// <inheritdoc/>
    public void StopVoice(AudioVoiceHandle voice)
    {
        if (_pool.TryResolve(voice, out VoiceSlot? slot))
        {
            _pool.Retire(slot);
        }
    }

    /// <inheritdoc/>
    public void StopAll() => _pool.RetireAll();

    /// <inheritdoc/>
    public void Update()
    {
        if (!_open)
        {
            return;
        }

        foreach (VoiceSlot voice in _pool.Voices)
        {
            if (!voice.IsActive)
            {
                continue;
            }

            if (voice.IsStreaming)
            {
                PumpStream(voice);
            }
            else if (_page.HasEnded(voice.Index))
            {
                _pool.Retire(voice);
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Shutdown();
    }

    private void Open()
    {
        int count = Math.Max(1, _options.VoiceCount);
        SampleRate = _page.Open(_options.SampleRate, count);
        if (SampleRate <= 0)
        {
            _logger.Warning(LogCategory, "The page opened no audio context. Running silently.");
            Shutdown();
            return;
        }

        _open = true;
        _pool = new VoicePool<VoiceSlot>(count, index => new VoiceSlot(index), Silence);

        foreach (SoundCategory category in Enum.GetValues<SoundCategory>())
        {
            _page.SetBusGain((int)category, _buses.Get(category));
        }

        _logger.Information(
            LogCategory,
            $"Web Audio ready: {count} voices at {SampleRate} Hz, {(_options.Spatial ? "3D" : "vanilla pan")}.");
    }

    private void Shutdown()
    {
        // The pool disposes streams as it retires them; a pool that never opened holds none.
        _pool.RetireAll();
        _pool = VoicePool.Empty<VoiceSlot>();
        _buffers.Clear();

        if (_open)
        {
            _open = false;
            _page.Close();
        }
    }

    /// <summary>Stops a voice's sound on the page, before the pool clears the slot for reuse.</summary>
    private void Silence(VoiceSlot voice)
    {
        if (_open)
        {
            _page.StopVoice(voice.Index);
        }
    }

    private void ApplyParameters(VoiceSlot voice, in VoiceParameters parameters)
    {
        if (!_open)
        {
            return;
        }

        float pitch = VanillaSpatialiser.ClampPitch(parameters.Pitch);

        float gain;
        float pan;
        if (_options.Spatial && parameters.IsPositional)
        {
            // The gain goes through the original's quantised curve before the distance fade, as OpenAL's
            // source gain does, so the loudness matches the desktop's.
            (float fade, pan) = Place(parameters, _listener);
            gain = MixBuses.Quantise(parameters.Gain) * fade;
        }
        else
        {
            // Vanilla, or a flat voice: gain and pan exactly as 0x496E60 works them out. See the class
            // remarks for why a stereo panner makes this exact here.
            (float vanillaGain, pan) = VanillaSpatialiser.Spatialise(parameters, _listener);
            gain = MixBuses.Quantise(vanillaGain);
            if (_options.ReverseStereo)
            {
                pan = -pan;
            }
        }

        _page.SetVoice(voice.Index, (int)parameters.Category, gain, pitch, pan);
    }

    /// <summary>The distance fade and stereo pan of a placed sound, as OpenAL positions one.</summary>
    /// <remarks>
    /// <para>
    /// <b>Not the page's 3D panner.</b> A Web Audio panner's <c>equalpower</c> model pans by azimuth
    /// alone: it drops the elevation, so a sound straight below the head -- the player's own footsteps,
    /// a dropped shell -- is panned by whatever sliver of sideways offset is left, and a few centimetres
    /// either way throws it hard left or hard right. That was one channel going silent for a moment and
    /// then the other. The pan here is the sideways share of the full direction, which is how OpenAL
    /// pans a source onto two speakers: beside the head is hard over; above, below, ahead and behind are
    /// centred.
    /// </para>
    /// <para>
    /// A source within <see cref="NearFieldMetres"/> of the head is the player's own sound, and its
    /// direction is mostly the noise of the listener and the source being placed on different ticks; its
    /// pan eases to the centre there rather than flipping with that noise.
    /// </para>
    /// </remarks>
    private static (float Fade, float Pan) Place(in VoiceParameters parameters, in AudioListenerState listener)
    {
        // OpenAL's linear model keeps the two distances apart; the same here so the fade never divides by zero.
        float reference = Math.Max(0.0001f, parameters.MinimumDistance);
        float farthest = Math.Max(parameters.MaximumDistance, reference * 1.0001f);

        Vector3d delta = parameters.Position - listener.Position;
        double distance = delta.Length;
        float fade = VanillaSpatialiser.DistanceGain(1f, distance, reference, farthest);
        if (distance <= 0.0)
        {
            return (fade, 0f);
        }

        // The engine's v * M convention, as VanillaSpatialiser.Pan applies it: X is the listener's right.
        Vector3d local = listener.Orientation.Transform(delta);
        double sideways = local.X / distance;
        double nearness = Math.Min(1.0, distance / NearFieldMetres);
        return (fade, Math.Clamp((float)(sideways * nearness), -1f, 1f));
    }

    private void ApplyListener()
    {
        // Every placed sound's gain and pan is worked out here against the head as it is now -- the
        // original's per-tick recompute (0x496E60) under vanilla, OpenAL's placement under 3D (see
        // Place) -- so the live positional voices are re-aimed whenever the listener moves.
        foreach (VoiceSlot voice in _pool.Voices)
        {
            if (voice.IsActive && voice.Parameters.IsPositional)
            {
                ApplyParameters(voice, voice.Parameters);
            }
        }
    }

    /// <summary>Keeps a stream's queue topped up, and frees the voice once it has all played.</summary>
    private void PumpStream(VoiceSlot voice)
    {
        int queued = _page.QueuedStreamChunks(voice.Index);
        while (queued < VoiceSlot.StreamBufferCount && QueueStreamChunk(voice))
        {
            queued++;
        }

        if (queued == 0)
        {
            _pool.Retire(voice);
        }
    }

    /// <summary>Pulls one chunk from a voice's stream and schedules it on the page.</summary>
    /// <returns><see langword="false"/> when the stream is finished and nothing was queued.</returns>
    private bool QueueStreamChunk(VoiceSlot voice)
    {
        ReadOnlySpan<short> samples = _pool.FillStreamBuffer(voice);
        if (samples.IsEmpty)
        {
            return false;
        }

        _page.QueueStream(voice.Index, MemoryMarshal.AsBytes(samples));
        return true;
    }

    private readonly record struct BufferRecord(int PageId, AudioFormat Format);
}
