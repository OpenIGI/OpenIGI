namespace OpenIGI.Audio;

/// <summary>
/// A voice heard live: samples pushed as they arrive from the network, played as a stream that never ends -- silence fills
/// whatever has not come yet, and a backlog grown past a few frames is cut so the voice never falls behind.
/// </summary>
/// <param name="sampleRate">The rate, in samples a second.</param>
/// <param name="framesPerBuffer">How many samples each buffer the device queues holds.</param>
public sealed class LiveVoice(int sampleRate, int framesPerBuffer) : ISampleSource
{
    private readonly Queue<short> _waiting = new();
    private readonly SmoothedLowPass _muffle = new(sampleRate);
    private readonly object _gate = new();

    /// <inheritdoc/>
    public AudioFormat Format { get; } = new(sampleRate, 1);

    /// <inheritdoc/>
    public bool CanRestart => false;

    /// <inheritdoc/>
    public int FramesPerBuffer => framesPerBuffer;

    /// <summary>Gets or sets the cutoff the voice is heard through: open in the clear, low behind a wall.</summary>
    public float CutoffHz
    {
        get => _muffle.CutoffHz;
        set => _muffle.CutoffHz = value;
    }

    /// <summary>Adds samples that came, to be played after those still waiting.</summary>
    /// <param name="samples">The samples.</param>
    public void Push(ReadOnlySpan<short> samples)
    {
        lock (_gate)
        {
            foreach (short sample in samples)
            {
                _waiting.Enqueue(sample);
            }

            // More than a fifth of a second behind: the oldest go, so the voice is now rather than then.
            while (_waiting.Count > Format.SampleRate / 5)
            {
                _waiting.Dequeue();
            }
        }
    }

    /// <inheritdoc/>
    public int Read(Span<short> destination)
    {
        lock (_gate)
        {
            int i = 0;
            for (; i < destination.Length && _waiting.Count > 0; i++)
            {
                destination[i] = _waiting.Dequeue();
            }

            destination[i..].Clear();
        }

        for (int i = 0; i < destination.Length; i++)
        {
            destination[i] = (short)Math.Clamp(_muffle.Process(destination[i]), short.MinValue, short.MaxValue);
        }

        return destination.Length;
    }

    /// <inheritdoc/>
    public void Restart()
    {
    }

    /// <inheritdoc/>
    public void Dispose()
    {
    }
}
