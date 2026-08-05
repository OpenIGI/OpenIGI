using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using Silk.NET.OpenAL;

namespace OpenIGI.Audio.OpenAL;

/// <summary>An <see cref="IAudioDevice"/> on OpenAL Soft, through Silk.NET.</summary>
/// <remarks>
/// <para>
/// The original drives DirectSound directly and hand-computes every sound's apparent position into
/// volume, pan and playback frequency (<c>0x496E60</c>). This backend can do either: with
/// <see cref="AudioDeviceOptions.Spatial"/> off it feeds OpenAL the gain and pan
/// <see cref="VanillaSpatialiser"/> produces, and with it on it hands over the position and lets OpenAL
/// place the sound properly.
/// </para>
/// <para>
/// <b>The distance model is linear in both profiles.</b> OpenAL's
/// <see cref="DistanceModel.LinearDistanceClamped"/> computes exactly the fade the original does —
/// full gain inside the reference distance, a straight line to silence at the maximum, clamped either
/// side — so the min and max distances the game's own data carries keep meaning what their authors
/// intended. Choosing an inverse model instead would be more physical and less faithful, and would
/// quietly retune every sound in the game.
/// </para>
/// <para>
/// <b>Divergence, and it is audible if you go looking:</b> OpenAL has no per-channel gain for a mono
/// source, so the vanilla profile's pan is approximated by placing the source on a unit circle around
/// the listener rather than by attenuating one output channel by an exact number of millibels. The
/// <em>gain</em> curve is exact — it goes through the original's own table — but the stereo image is a
/// close likeness rather than a reproduction. Making it exact would mean owning the mixer.
/// </para>
/// <para>
/// If no device can be opened the object still constructs and behaves as silence, with
/// <see cref="IsAvailable"/> false. Nothing about a missing sound card may stop the game booting.
/// </para>
/// </remarks>
public sealed class OpenAlAudioDevice : IAudioDevice
{
    private const string LogCategory = "Audio";

    /// <summary>How many frames of a stream each queued buffer holds.</summary>
    /// <remarks>About a sixteenth of a second at 48 kHz — short enough that a volume change is prompt.</remarks>
    private const int StreamFramesPerBuffer = 3072;

    private readonly IIgiLogger _logger;
    private readonly AudioDeviceOptions _options;
    private readonly Lock _gate = new();
    private readonly float[] _categoryGains = [1f, 1f, 1f, 1f];
    private readonly Dictionary<int, BufferRecord> _buffers = [];
    private readonly List<OpenAlVoice> _voices = [];

    private AL? _al;
    private ALContext? _alc;
    private unsafe Device* _device;
    private unsafe Context* _context;
    private short[] _streamScratch = [];
    private int _nextBufferHandle = 1;
    private AudioListenerState _listener = AudioListenerState.Default;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="OpenAlAudioDevice"/> class.</summary>
    /// <param name="options">How to open the device.</param>
    /// <param name="logger">Where to report what happened, or <see langword="null"/> for silence.</param>
    public OpenAlAudioDevice(AudioDeviceOptions options, IIgiLogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
        _options = options;

        try
        {
            Open();
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException
            or InvalidOperationException or TypeInitializationException or BadImageFormatException)
        {
            // A missing or unusable OpenAL is not a fatal condition. Continuous integration has no
            // sound card, and neither do plenty of machines people will run this on.
            _logger.Warning(LogCategory, $"No audio device: {error.Message}. Running silently.");
            Shutdown();
        }
    }

    /// <inheritdoc/>
    public bool IsAvailable => _al is not null;

    /// <inheritdoc/>
    public int VoiceCount => _voices.Count;

    /// <inheritdoc/>
    public int ActiveVoiceCount
    {
        get
        {
            lock (_gate)
            {
                return _voices.Count(voice => voice.IsActive);
            }
        }
    }

    /// <inheritdoc/>
    public AudioListenerState Listener
    {
        get => _listener;
        set
        {
            _listener = value;
            ApplyListener(value);
        }
    }

    /// <inheritdoc/>
    public void SetCategoryGain(SoundCategory category, float gain)
    {
        _categoryGains[(int)category] = Math.Clamp(gain, 0f, 1f);

        // Every sounding voice on that bus has to be re-levelled; OpenAL has no bus of its own.
        lock (_gate)
        {
            foreach (OpenAlVoice voice in _voices)
            {
                if (voice.IsActive && voice.Parameters.Category == category)
                {
                    ApplyParameters(voice, voice.Parameters);
                }
            }
        }
    }

    /// <inheritdoc/>
    public float GetCategoryGain(SoundCategory category) => _categoryGains[(int)category];

    /// <inheritdoc/>
    public AudioBufferHandle CreateBuffer(AudioFormat format, ReadOnlySpan<byte> pcm)
    {
        format.Validate(nameof(format));

        if (_al is not { } al)
        {
            return AudioBufferHandle.None;
        }

        lock (_gate)
        {
            uint name = al.GenBuffer();
            unsafe
            {
                fixed (byte* samples = pcm)
                {
                    al.BufferData(name, ToBufferFormat(format), samples, pcm.Length, format.SampleRate);
                }
            }

            int handle = _nextBufferHandle++;
            _buffers[handle] = new BufferRecord(name, format);
            return new AudioBufferHandle(handle);
        }
    }

    /// <inheritdoc/>
    public void DestroyBuffer(AudioBufferHandle buffer)
    {
        if (_al is not { } al)
        {
            return;
        }

        lock (_gate)
        {
            if (!_buffers.Remove(buffer.Value, out BufferRecord record))
            {
                return;
            }

            // A source still bound to the buffer would keep it alive and leak it, so stop those first.
            foreach (OpenAlVoice voice in _voices)
            {
                if (voice.IsActive && voice.Stream is null && voice.Buffer == buffer)
                {
                    Retire(voice);
                }
            }

            al.DeleteBuffer(record.Name);
        }
    }

    /// <inheritdoc/>
    public AudioVoiceHandle Play(AudioBufferHandle buffer, in VoiceParameters parameters)
    {
        if (_al is not { } al)
        {
            return AudioVoiceHandle.None;
        }

        lock (_gate)
        {
            if (!_buffers.TryGetValue(buffer.Value, out BufferRecord record))
            {
                return AudioVoiceHandle.None;
            }

            OpenAlVoice? voice = Acquire(parameters.Priority);
            if (voice is null)
            {
                return AudioVoiceHandle.None;
            }

            voice.Buffer = buffer;
            voice.Parameters = parameters;

            al.SetSourceProperty(voice.Source, SourceInteger.Buffer, (int)record.Name);
            al.SetSourceProperty(voice.Source, SourceBoolean.Looping, parameters.Looping);
            ApplyParameters(voice, parameters);
            al.SourcePlay(voice.Source);

            return MakeHandle(voice);
        }
    }

    /// <inheritdoc/>
    public AudioVoiceHandle PlayStream(ISampleSource source, in VoiceParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (_al is not { } al)
        {
            source.Dispose();
            return AudioVoiceHandle.None;
        }

        lock (_gate)
        {
            OpenAlVoice? voice = Acquire(parameters.Priority);
            if (voice is null)
            {
                source.Dispose();
                return AudioVoiceHandle.None;
            }

            voice.Stream = source;
            voice.StreamDrained = false;
            voice.Parameters = parameters;
            voice.StreamBuffers ??= al.GenBuffers(OpenAlVoice.StreamBufferCount);

            // A queued source must not have AL_LOOPING set: looping applies to the whole queue and
            // would replay stale buffers instead of asking the stream to rewind.
            al.SetSourceProperty(voice.Source, SourceBoolean.Looping, false);
            al.SetSourceProperty(voice.Source, SourceInteger.Buffer, 0);
            ApplyParameters(voice, parameters);

            int filled = 0;
            foreach (uint name in voice.StreamBuffers)
            {
                if (!FillStreamBuffer(al, voice, name))
                {
                    break;
                }

                filled++;
            }

            if (filled == 0)
            {
                Retire(voice);
                return AudioVoiceHandle.None;
            }

            unsafe
            {
                fixed (uint* names = voice.StreamBuffers)
                {
                    al.SourceQueueBuffers(voice.Source, filled, names);
                }
            }

            al.SourcePlay(voice.Source);
            return MakeHandle(voice);
        }
    }

    /// <inheritdoc/>
    public bool IsPlaying(AudioVoiceHandle voice)
    {
        lock (_gate)
        {
            return TryResolve(voice, out _);
        }
    }

    /// <inheritdoc/>
    public void SetVoiceParameters(AudioVoiceHandle voice, in VoiceParameters parameters)
    {
        lock (_gate)
        {
            if (!TryResolve(voice, out OpenAlVoice? slot))
            {
                return;
            }

            slot.Parameters = parameters;
            ApplyParameters(slot, parameters);
        }
    }

    /// <inheritdoc/>
    public void StopVoice(AudioVoiceHandle voice)
    {
        lock (_gate)
        {
            if (TryResolve(voice, out OpenAlVoice? slot))
            {
                Retire(slot);
            }
        }
    }

    /// <inheritdoc/>
    public void StopAll()
    {
        lock (_gate)
        {
            foreach (OpenAlVoice voice in _voices)
            {
                if (voice.IsActive)
                {
                    Retire(voice);
                }
            }
        }
    }

    /// <inheritdoc/>
    public void Update()
    {
        if (_al is not { } al)
        {
            return;
        }

        lock (_gate)
        {
            foreach (OpenAlVoice voice in _voices)
            {
                if (!voice.IsActive)
                {
                    continue;
                }

                if (voice.Stream is not null)
                {
                    PumpStream(al, voice);
                    continue;
                }

                al.GetSourceProperty(voice.Source, GetSourceInteger.SourceState, out int state);
                if ((SourceState)state == SourceState.Stopped)
                {
                    Retire(voice);
                }
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

    private static BufferFormat ToBufferFormat(AudioFormat format) => (format.Channels, format.BitsPerSample) switch
    {
        (1, 8) => BufferFormat.Mono8,
        (1, 16) => BufferFormat.Mono16,
        (2, 8) => BufferFormat.Stereo8,
        _ => BufferFormat.Stereo16,
    };

    private unsafe void Open()
    {
        _alc = ALContext.GetApi();
        _al = AL.GetApi();

        _device = _alc.OpenDevice(string.Empty);
        if (_device is null)
        {
            _logger.Warning(LogCategory, "OpenAL opened no device. Running silently.");
            Shutdown();
            return;
        }

        // A requested rate the device cannot honour is not an error -- OpenAL picks the nearest it can
        // and carries on, which is why nothing here checks that it got what it asked for.
        int[] attributes = _options.SampleRate > 0
            ? [(int)ContextAttributes.Frequency, _options.SampleRate, 0]
            : [0];

        fixed (int* attributePointer = attributes)
        {
            _context = _alc.CreateContext(_device, attributePointer);
        }

        if (_context is null)
        {
            _logger.Warning(LogCategory, "OpenAL created no context. Running silently.");
            Shutdown();
            return;
        }

        _alc.MakeContextCurrent(_context);

        // Linear-clamped is the model the original's own numbers were authored against; see the class
        // remarks. Setting it once here is enough, since it is global to the context.
        _al.DistanceModel(Silk.NET.OpenAL.DistanceModel.LinearDistanceClamped);

        int count = Math.Max(1, _options.VoiceCount);
        for (int i = 0; i < count; i++)
        {
            _voices.Add(new OpenAlVoice(i, _al.GenSource()));
        }

        ApplyListener(_listener);
        _logger.Information(LogCategory, $"OpenAL ready: {count} voices, {(_options.Spatial ? "3D" : "vanilla pan")}.");
    }

    private unsafe void Shutdown()
    {
        lock (_gate)
        {
            if (_al is { } al)
            {
                foreach (OpenAlVoice voice in _voices)
                {
                    al.SourceStop(voice.Source);
                    al.DeleteSource(voice.Source);

                    voice.Stream?.Dispose();
                    voice.Stream = null;

                    if (voice.StreamBuffers is { } streamBuffers)
                    {
                        al.DeleteBuffers(streamBuffers);
                    }
                }

                foreach (BufferRecord record in _buffers.Values)
                {
                    al.DeleteBuffer(record.Name);
                }
            }
            else
            {
                foreach (OpenAlVoice voice in _voices)
                {
                    voice.Stream?.Dispose();
                    voice.Stream = null;
                }
            }

            _voices.Clear();
            _buffers.Clear();

            if (_alc is { } alc)
            {
                if (_context is not null)
                {
                    alc.MakeContextCurrent(null);
                    alc.DestroyContext(_context);
                    _context = null;
                }

                if (_device is not null)
                {
                    alc.CloseDevice(_device);
                    _device = null;
                }
            }

            _al?.Dispose();
            _alc?.Dispose();
            _al = null;
            _alc = null;
        }
    }

    /// <summary>Finds a free slot, or steals the least important active one.</summary>
    /// <remarks>
    /// The original has no equivalent: with all 32 of its voices busy, whatever asked next simply does
    /// not play, however important it was. Stealing is a divergence, and it is the reason the player's
    /// own weapon cannot be silenced by a crowd of distant ones.
    /// </remarks>
    private OpenAlVoice? Acquire(int priority)
    {
        OpenAlVoice? weakest = null;

        foreach (OpenAlVoice voice in _voices)
        {
            if (!voice.IsActive)
            {
                voice.IsActive = true;
                return voice;
            }

            if (weakest is null || voice.Parameters.Priority < weakest.Parameters.Priority)
            {
                weakest = voice;
            }
        }

        if (weakest is null || weakest.Parameters.Priority >= priority)
        {
            return null;
        }

        Retire(weakest);
        weakest.IsActive = true;
        return weakest;
    }

    private void Retire(OpenAlVoice voice)
    {
        if (_al is { } al)
        {
            al.SourceStop(voice.Source);

            // Detaching also releases every buffer still queued on a streaming source, which is what
            // makes the slot reusable without deleting and regenerating its buffers.
            al.SetSourceProperty(voice.Source, SourceInteger.Buffer, 0);
        }

        voice.Stream?.Dispose();
        voice.Stream = null;
        voice.StreamDrained = false;
        voice.Buffer = AudioBufferHandle.None;
        voice.IsActive = false;
        voice.Generation++;
    }

    private static AudioVoiceHandle MakeHandle(OpenAlVoice voice) =>
        new((voice.Generation << 16) | (voice.Index + 1));

    private bool TryResolve(AudioVoiceHandle handle, out OpenAlVoice voice)
    {
        voice = null!;
        if (!handle.IsValidHandle)
        {
            return false;
        }

        int index = (handle.Value & 0xFFFF) - 1;
        if (index < 0 || index >= _voices.Count)
        {
            return false;
        }

        OpenAlVoice candidate = _voices[index];
        if (!candidate.IsActive || candidate.Generation != (handle.Value >> 16))
        {
            return false;
        }

        voice = candidate;
        return true;
    }

    private void ApplyParameters(OpenAlVoice voice, in VoiceParameters parameters)
    {
        if (_al is not { } al)
        {
            return;
        }

        float categoryGain = _categoryGains[(int)parameters.Category];
        al.SetSourceProperty(voice.Source, SourceFloat.Pitch, VanillaSpatialiser.ClampPitch(parameters.Pitch));

        if (_options.Spatial && parameters.IsPositional)
        {
            // Hand the geometry over and let OpenAL do it. The gain still goes through the original's
            // quantised curve so the loudness matches; only the placement is the backend's.
            al.SetSourceProperty(voice.Source, SourceBoolean.SourceRelative, false);
            al.SetSourceProperty(
                voice.Source,
                SourceVector3.Position,
                (float)parameters.Position.X,
                (float)parameters.Position.Y,
                (float)parameters.Position.Z);
            al.SetSourceProperty(
                voice.Source,
                SourceVector3.Velocity,
                parameters.Velocity.X,
                parameters.Velocity.Y,
                parameters.Velocity.Z);
            al.SetSourceProperty(voice.Source, SourceFloat.ReferenceDistance, Math.Max(0.0001f, parameters.MinimumDistance));
            al.SetSourceProperty(voice.Source, SourceFloat.MaxDistance, Math.Max(0.0002f, parameters.MaximumDistance));
            al.SetSourceProperty(voice.Source, SourceFloat.RolloffFactor, 1f);
            al.SetSourceProperty(voice.Source, SourceFloat.Gain, Gain(parameters.Gain, categoryGain));
            return;
        }

        // Vanilla, or a flat voice: work out gain and pan ourselves exactly as 0x496E60 does, then park
        // the source on a unit circle around the listener's head to realise the pan. See the class
        // remarks for why the pan is a likeness rather than a reproduction.
        (float gain, float pan) = VanillaSpatialiser.Spatialise(parameters, _listener);
        if (_options.ReverseStereo)
        {
            pan = -pan;
        }

        float angle = MathF.Asin(Math.Clamp(pan, -1f, 1f));
        al.SetSourceProperty(voice.Source, SourceBoolean.SourceRelative, true);
        al.SetSourceProperty(voice.Source, SourceVector3.Position, MathF.Sin(angle), 0f, -MathF.Cos(angle));
        al.SetSourceProperty(voice.Source, SourceVector3.Velocity, 0f, 0f, 0f);
        al.SetSourceProperty(voice.Source, SourceFloat.ReferenceDistance, 1f);
        al.SetSourceProperty(voice.Source, SourceFloat.MaxDistance, 1f);
        al.SetSourceProperty(voice.Source, SourceFloat.RolloffFactor, 0f);
        al.SetSourceProperty(voice.Source, SourceFloat.Gain, Gain(gain, categoryGain));
    }

    /// <summary>Puts a gain through the original's curve, then applies the bus and master gains.</summary>
    /// <remarks>
    /// The quantisation is the point: the original looks its gain up in a 10001-entry table whose unit
    /// is a thousand per halving, which is appreciably steeper than decibels. Going through
    /// <see cref="AudioVolumeCurve"/> is what carries that loudness shape onto a backend that has never
    /// heard of DirectSound.
    /// </remarks>
    private static float Gain(float gain, float categoryGain) =>
        AudioVolumeCurve.Quantise(Math.Clamp(gain, 0f, 1f)) * categoryGain;

    private unsafe void ApplyListener(in AudioListenerState listener)
    {
        if (_al is not { } al)
        {
            return;
        }

        lock (_gate)
        {
            al.SetListenerProperty(
                ListenerVector3.Position,
                (float)listener.Position.X,
                (float)listener.Position.Y,
                (float)listener.Position.Z);
            al.SetListenerProperty(
                ListenerVector3.Velocity,
                listener.Velocity.X,
                listener.Velocity.Y,
                listener.Velocity.Z);

            Vector3f forward = listener.Forward;
            Vector3f up = listener.Up;
            float* orientation = stackalloc float[6];
            orientation[0] = forward.X;
            orientation[1] = forward.Y;
            orientation[2] = forward.Z;
            orientation[3] = up.X;
            orientation[4] = up.Y;
            orientation[5] = up.Z;
            al.SetListenerProperty(ListenerFloatArray.Orientation, orientation);
        }
    }

    /// <summary>Refills processed buffers and keeps a starved source moving.</summary>
    private void PumpStream(AL al, OpenAlVoice voice)
    {
        al.GetSourceProperty(voice.Source, GetSourceInteger.BuffersProcessed, out int processed);

        for (int i = 0; i < processed; i++)
        {
            uint name = 0;
            unsafe
            {
                al.SourceUnqueueBuffers(voice.Source, 1, &name);
            }

            if (FillStreamBuffer(al, voice, name))
            {
                unsafe
                {
                    al.SourceQueueBuffers(voice.Source, 1, &name);
                }
            }
        }

        al.GetSourceProperty(voice.Source, GetSourceInteger.BuffersQueued, out int queued);
        if (queued == 0)
        {
            Retire(voice);
            return;
        }

        // A source that ran dry between pumps goes to Stopped on its own. Restarting it is what turns a
        // frame-time spike into a click instead of the music simply ending.
        al.GetSourceProperty(voice.Source, GetSourceInteger.SourceState, out int state);
        if ((SourceState)state != SourceState.Playing)
        {
            al.SourcePlay(voice.Source);
        }
    }

    /// <summary>Pulls one buffer's worth of samples from a voice's stream.</summary>
    /// <returns><see langword="false"/> when the stream is finished and the buffer was not filled.</returns>
    private bool FillStreamBuffer(AL al, OpenAlVoice voice, uint name)
    {
        if (voice.Stream is not { } stream || voice.StreamDrained)
        {
            return false;
        }

        AudioFormat format = stream.Format;
        int wanted = StreamFramesPerBuffer * format.Channels;
        if (_streamScratch.Length < wanted)
        {
            _streamScratch = new short[wanted];
        }

        int written = 0;
        while (written < wanted)
        {
            int read = stream.Read(_streamScratch.AsSpan(written, wanted - written));
            if (read > 0)
            {
                written += read;
                continue;
            }

            if (!voice.Parameters.Looping || !stream.CanRestart)
            {
                voice.StreamDrained = true;
                break;
            }

            stream.Restart();
        }

        if (written == 0)
        {
            return false;
        }

        unsafe
        {
            fixed (short* samples = _streamScratch)
            {
                al.BufferData(name, ToBufferFormat(format), samples, written * sizeof(short), format.SampleRate);
            }
        }

        return true;
    }

    private readonly record struct BufferRecord(uint Name, AudioFormat Format);
}
