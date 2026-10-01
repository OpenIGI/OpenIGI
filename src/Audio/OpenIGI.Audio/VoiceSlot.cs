namespace OpenIGI.Audio;

/// <summary>One slot in a device's fixed voice pool.</summary>
/// <remarks>
/// <para>
/// The part of a voice that every backend has: which sound it is playing, how, and whether it is in
/// use. A backend derives from it to add what only it needs — an OpenAL source name, say — and hands
/// the pool a factory for its own kind.
/// </para>
/// <para>
/// <see cref="Generation"/> is what makes a stale handle harmless. It increases every time the slot is
/// reused, so a caller holding a handle to a voice that has since been stolen finds the generation no
/// longer matches and its call is dropped rather than applied to whatever took its place.
/// </para>
/// </remarks>
public class VoiceSlot
{
    /// <summary>How many buffers a streaming voice keeps in flight.</summary>
    /// <remarks>
    /// Four is enough that a frame-time spike cannot starve the voice before the next pump, while
    /// keeping the latency of a volume change on the music down to a fraction of a second.
    /// </remarks>
    public const int StreamBufferCount = 4;

    /// <summary>How many frames of a stream each in-flight buffer holds.</summary>
    /// <remarks>About a sixteenth of a second at 48 kHz — short enough that a volume change is prompt.</remarks>
    public const int StreamFramesPerBuffer = 3072;

    /// <summary>Initializes a new instance of the <see cref="VoiceSlot"/> class.</summary>
    /// <param name="index">The slot index within its pool.</param>
    public VoiceSlot(int index) => Index = index;

    /// <summary>Gets the slot index.</summary>
    public int Index { get; }

    /// <summary>Gets the generation, incremented on every reuse.</summary>
    public int Generation { get; internal set; }

    /// <summary>Gets a value indicating whether the slot is in use.</summary>
    public bool IsActive { get; internal set; }

    /// <summary>Gets the handle a caller holds for this voice in its current generation.</summary>
    /// <remarks>The slot index and the generation, packed; a reused slot gives out a different handle.</remarks>
    public AudioVoiceHandle Handle => new((Generation << 16) | (Index + 1));

    /// <summary>Gets or sets the parameters the voice was last given.</summary>
    public VoiceParameters Parameters { get; set; }

    /// <summary>Gets or sets the streamed source feeding this voice, when it is a streaming voice.</summary>
    public ISampleSource? Stream { get; set; }

    /// <summary>Gets a value indicating whether this voice plays a stream rather than a resident buffer.</summary>
    public bool IsStreaming => Stream is not null;

    /// <summary>Gets or sets a value indicating whether the stream has run out of samples.</summary>
    public bool StreamDrained { get; set; }

    /// <summary>Gets or sets the resident buffer handle a non-streaming voice is playing.</summary>
    public AudioBufferHandle Buffer { get; set; }

    /// <summary>Pulls one buffer's worth of samples from the voice's stream.</summary>
    /// <param name="destination">Where the samples go, channel-interleaved.</param>
    /// <returns>How many samples were written; zero when the stream is finished.</returns>
    /// <remarks>
    /// A looping stream is rewound when it runs out, but only once per fill, and that bound is the
    /// point rather than an optimisation. A source that reports nothing with samples still to come —
    /// because the tail of this buffer is smaller than the block it decodes, say — would otherwise be
    /// rewound, asked again, report nothing again, and be rewound again, for ever. A loop point is worth
    /// crossing once in a buffer; twice means the source is not going to fill it.
    /// </remarks>
    public int FillFromStream(Span<short> destination)
    {
        if (Stream is not { } stream || StreamDrained)
        {
            return 0;
        }

        int written = 0;
        bool rewound = false;
        while (written < destination.Length)
        {
            int read = stream.Read(destination[written..]);
            if (read > 0)
            {
                written += read;
                continue;
            }

            if (!Parameters.Looping || !stream.CanRestart || rewound)
            {
                StreamDrained = true;
                break;
            }

            rewound = true;
            stream.Restart();
        }

        return written;
    }

    /// <summary>Clears the slot for reuse: disposes its stream and forgets what it played.</summary>
    internal void Reset()
    {
        Stream?.Dispose();
        Stream = null;
        StreamDrained = false;
        Buffer = AudioBufferHandle.None;
        IsActive = false;
        Generation++;
    }
}
