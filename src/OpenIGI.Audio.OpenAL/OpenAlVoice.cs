using OpenIGI.Audio;

namespace OpenIGI.Audio.OpenAL;

/// <summary>One slot in the device's fixed voice pool.</summary>
/// <remarks>
/// <para>
/// A slot owns an OpenAL source for the whole life of the device — sources are created once at startup
/// and reused, because creating one per sound would make a firefight allocate hundreds a second.
/// </para>
/// <para>
/// <see cref="Generation"/> is what makes a stale handle harmless. It increases every time the slot is
/// reused, so a caller holding a handle to a voice that has since been stolen finds the generation no
/// longer matches and its call is dropped rather than applied to whatever took its place.
/// </para>
/// </remarks>
internal sealed class OpenAlVoice
{
    /// <summary>How many buffers a streaming voice keeps in flight.</summary>
    /// <remarks>
    /// Four is enough that a frame-time spike cannot starve the source before the next pump, while
    /// keeping the latency of a volume change on the music down to a fraction of a second.
    /// </remarks>
    public const int StreamBufferCount = 4;

    /// <summary>Initializes a new instance of the <see cref="OpenAlVoice"/> class.</summary>
    /// <param name="index">The slot index.</param>
    /// <param name="source">The OpenAL source name this slot owns.</param>
    public OpenAlVoice(int index, uint source)
    {
        Index = index;
        Source = source;
    }

    /// <summary>Gets the slot index.</summary>
    public int Index { get; }

    /// <summary>Gets the OpenAL source name.</summary>
    public uint Source { get; }

    /// <summary>Gets or sets the generation, incremented on every reuse.</summary>
    public int Generation { get; set; }

    /// <summary>Gets or sets a value indicating whether the slot is in use.</summary>
    public bool IsActive { get; set; }

    /// <summary>Gets or sets the parameters the voice was last given.</summary>
    public VoiceParameters Parameters { get; set; }

    /// <summary>Gets or sets the streamed source feeding this voice, when it is a streaming voice.</summary>
    public ISampleSource? Stream { get; set; }

    /// <summary>Gets or sets the OpenAL buffers a streaming voice cycles through.</summary>
    public uint[]? StreamBuffers { get; set; }

    /// <summary>Gets or sets a value indicating whether the stream has run out of samples.</summary>
    public bool StreamDrained { get; set; }

    /// <summary>Gets or sets the resident buffer handle a non-streaming voice is playing.</summary>
    public AudioBufferHandle Buffer { get; set; }
}
