using OpenIGI.Audio;

namespace OpenIGI.Audio.OpenAL;

/// <summary>One slot in the device's voice pool: a <see cref="VoiceSlot"/> that owns an OpenAL source.</summary>
/// <remarks>
/// A slot owns its source for the whole life of the device — sources are created once at startup and
/// reused, because creating one per sound would make a firefight allocate hundreds a second. The
/// allocation, stealing and handle bookkeeping is the pool's; only the OpenAL names live here.
/// </remarks>
internal sealed class OpenAlVoice : VoiceSlot
{
    /// <summary>Initializes a new instance of the <see cref="OpenAlVoice"/> class.</summary>
    /// <param name="index">The slot index.</param>
    /// <param name="source">The OpenAL source name this slot owns.</param>
    public OpenAlVoice(int index, uint source)
        : base(index) => Source = source;

    /// <summary>Gets the OpenAL source name.</summary>
    public uint Source { get; }

    /// <summary>Gets or sets the OpenAL buffers a streaming voice cycles through.</summary>
    public uint[]? StreamBuffers { get; set; }
}
