namespace OpenIGI.Audio;

/// <summary>Makes voice pools.</summary>
public static class VoicePool
{
    /// <summary>Makes an empty pool, for a device that opened nothing.</summary>
    /// <typeparam name="TVoice">The backend's slot type.</typeparam>
    /// <returns>A pool with no voices.</returns>
    public static VoicePool<TVoice> Empty<TVoice>()
        where TVoice : VoiceSlot =>
        new(0, _ => throw new InvalidOperationException("An empty pool makes no voices."), _ => { });
}

/// <summary>A device's fixed pool of voices: allocation, stealing, handles and stream refills.</summary>
/// <typeparam name="TVoice">The backend's slot type.</typeparam>
/// <remarks>
/// <para>
/// The model is the original's — a fixed number of voices allocated once (<c>Snd_Init</c>
/// <c>0x4952F0</c> makes 32) and reused for the life of the device — with the bookkeeping every backend
/// needs done once here: which slots are free, which to steal, what a handle encodes, and how a stream
/// is refilled. A backend supplies the two things only it knows: how to make a slot of its own kind
/// and how to silence one.
/// </para>
/// <para>
/// A handle packs the slot index and its generation, so a handle to a voice that has since been stolen
/// resolves to nothing rather than to whatever took its place.
/// </para>
/// <para>
/// Not thread-safe. The device that owns the pool is called from the game thread only, and keeps its own
/// lock where it wants one.
/// </para>
/// </remarks>
public sealed class VoicePool<TVoice>
    where TVoice : VoiceSlot
{
    private readonly TVoice[] _voices;
    private readonly Action<TVoice> _silence;
    private short[] _streamScratch = [];

    /// <summary>Initializes a new instance of the <see cref="VoicePool{TVoice}"/> class.</summary>
    /// <param name="count">How many voices to make; fewer than one makes an empty pool.</param>
    /// <param name="create">Makes the slot at an index.</param>
    /// <param name="silence">Stops whatever a slot is sounding, before the pool clears it for reuse.</param>
    public VoicePool(int count, Func<int, TVoice> create, Action<TVoice> silence)
    {
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(silence);

        _silence = silence;
        _voices = new TVoice[Math.Max(0, count)];
        for (int i = 0; i < _voices.Length; i++)
        {
            _voices[i] = create(i);
        }
    }

    /// <summary>Gets the number of voices.</summary>
    public int Count => _voices.Length;

    /// <summary>Gets the number of voices in use.</summary>
    public int ActiveCount
    {
        get
        {
            int active = 0;
            foreach (TVoice voice in _voices)
            {
                if (voice.IsActive)
                {
                    active++;
                }
            }

            return active;
        }
    }

    /// <summary>Gets every voice, in slot order.</summary>
    public ReadOnlySpan<TVoice> Voices => _voices;

    /// <summary>Finds a free slot, or steals the least important active one.</summary>
    /// <param name="priority">The priority of the sound that wants a voice.</param>
    /// <returns>An active slot, or <see langword="null"/> when none was free and none could be stolen.</returns>
    /// <remarks>
    /// The original has no equivalent: with all 32 of its voices busy, whatever asked next simply does
    /// not play, however important it was. Stealing is a divergence, and it is the reason the player's
    /// own weapon cannot be silenced by a crowd of distant ones. A slot is stolen only from a strictly
    /// less important sound; an equal one keeps its voice.
    /// </remarks>
    public TVoice? Acquire(int priority)
    {
        TVoice? weakest = null;

        foreach (TVoice voice in _voices)
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

    /// <summary>Resolves a handle to the voice it names, if that voice is still the one it named.</summary>
    /// <param name="handle">The handle; a stale or unknown one resolves to nothing.</param>
    /// <param name="voice">The voice, when it resolved.</param>
    /// <returns><see langword="true"/> when the handle names an active voice of the same generation.</returns>
    public bool TryResolve(AudioVoiceHandle handle, out TVoice voice)
    {
        voice = null!;
        if (!handle.IsValidHandle)
        {
            return false;
        }

        int index = (handle.Value & 0xFFFF) - 1;
        if (index < 0 || index >= _voices.Length)
        {
            return false;
        }

        TVoice candidate = _voices[index];
        if (!candidate.IsActive || candidate.Generation != (handle.Value >> 16))
        {
            return false;
        }

        voice = candidate;
        return true;
    }

    /// <summary>Silences a voice and frees its slot.</summary>
    /// <param name="voice">The voice.</param>
    /// <remarks>Its stream, if any, is disposed: the device owns a stream it was handed.</remarks>
    public void Retire(TVoice voice)
    {
        ArgumentNullException.ThrowIfNull(voice);
        _silence(voice);
        voice.Reset();
    }

    /// <summary>Silences and frees every active voice.</summary>
    public void RetireAll()
    {
        foreach (TVoice voice in _voices)
        {
            if (voice.IsActive)
            {
                Retire(voice);
            }
        }
    }

    /// <summary>Pulls one in-flight buffer's worth of samples from a streaming voice.</summary>
    /// <param name="voice">The voice.</param>
    /// <returns>
    /// The samples, channel-interleaved at the stream's format, valid until the next call; empty when
    /// the stream is finished.
    /// </returns>
    public ReadOnlySpan<short> FillStreamBuffer(TVoice voice)
    {
        ArgumentNullException.ThrowIfNull(voice);
        if (voice.Stream is not { } stream)
        {
            return [];
        }

        int wanted = VoiceSlot.StreamFramesPerBuffer * stream.Format.Channels;
        if (_streamScratch.Length < wanted)
        {
            _streamScratch = new short[wanted];
        }

        int written = voice.FillFromStream(_streamScratch.AsSpan(0, wanted));
        return _streamScratch.AsSpan(0, written);
    }
}
