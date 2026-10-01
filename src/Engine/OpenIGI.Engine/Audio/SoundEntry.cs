using OpenIGI.Formats.Sound;

namespace OpenIGI.Engine.Audio;

/// <summary>One sound the bank knows about.</summary>
/// <remarks>
/// <para>
/// A resident sound carries its bytes; a streamed one carries only the path it was found at, exactly as
/// the original does — the loader at <c>0x495F70</c> stores a file name at record <c>+40</c> for the
/// streamed storage codes and never allocates for them. That is what keeps the music track and the
/// ambience beds out of memory until something asks to hear them.
/// </para>
/// <para>
/// I.G.I. 2's music adds a third case the original has no code for: an MPEG stream that is <em>inside</em>
/// an archive, so there is no file to reopen, and far too long to expand. Such an entry keeps its
/// <em>packed</em> bytes and is decoded a buffer at a time out of memory — eight megabytes held rather
/// than thirty-six, and nothing decoded until the track is asked for. <see cref="IsStreamed"/> is
/// therefore about how a sound is played rather than about where it is read from, and
/// <see cref="Data"/> being non-empty is what says which.
/// </para>
/// </remarks>
public sealed class SoundEntry
{
    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly TimeSpan _duration;

    /// <summary>Initializes a new instance of the <see cref="SoundEntry"/> class for an ILSF sound.</summary>
    /// <param name="name">The lookup key, from <see cref="SoundName.ToKey"/>.</param>
    /// <param name="origin">The virtual file system path the sound was found at.</param>
    /// <param name="header">The sound's parsed header.</param>
    /// <param name="data">The whole ILSF bytes for a resident sound, or empty for a streamed one.</param>
    public SoundEntry(string name, string origin, IlsfHeader header, ReadOnlyMemory<byte> data)
    {
        Name = name;
        Origin = origin;
        Codec = SoundCodec.Ilsf;
        Header = header;
        Data = data;
        IsStreamed = header.IsStreamed;
        _sampleRate = header.SampleRate;
        _channels = header.Channels;
        _duration = header.Duration;
    }

    private SoundEntry(
        string name,
        string origin,
        SoundCodec codec,
        int sampleRate,
        int channels,
        TimeSpan duration,
        ReadOnlyMemory<byte> data)
    {
        Name = name;
        Origin = origin;
        Codec = codec;
        Data = data;
        IsStreamed = true;
        _sampleRate = sampleRate;
        _channels = channels;
        _duration = duration;
    }

    /// <summary>Gets the lookup key, upper-cased.</summary>
    public string Name { get; }

    /// <summary>Gets the virtual file system path the sound was found at.</summary>
    public string Origin { get; }

    /// <summary>Gets which decoder the sound needs.</summary>
    public SoundCodec Codec { get; }

    /// <summary>Gets the sound's ILSF header; meaningless unless <see cref="Codec"/> is <see cref="SoundCodec.Ilsf"/>.</summary>
    public IlsfHeader Header { get; }

    /// <summary>
    /// Gets the sound's bytes as stored: the whole ILSF file for a resident sound, the packed stream for
    /// a sound decoded out of memory, and empty for one reopened from <see cref="Origin"/>.
    /// </summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>Gets a value indicating whether the sound is decoded as it plays rather than held as PCM.</summary>
    public bool IsStreamed { get; }

    /// <summary>Gets the sample rate in hertz.</summary>
    public int SampleRate => _sampleRate;

    /// <summary>Gets the channel count, one or two.</summary>
    public int Channels => _channels;

    /// <summary>Gets how long the sound plays for.</summary>
    public TimeSpan Duration => _duration;

    /// <summary>Creates an entry for an MPEG audio track.</summary>
    /// <param name="name">The lookup key, from <see cref="SoundName.ToKey"/>.</param>
    /// <param name="origin">The virtual file system path the track was found at.</param>
    /// <param name="header">The first frame's header, which gives the track's shape.</param>
    /// <param name="data">
    /// The packed stream when the track lives inside an archive and must be decoded out of memory, or
    /// empty when it is a loose file the player can reopen.
    /// </param>
    /// <param name="encodedByteCount">
    /// How long the packed stream is, which for a loose file is its length on disk rather than
    /// <paramref name="data"/>'s. Only the duration is read off it.
    /// </param>
    /// <returns>The entry.</returns>
    public static SoundEntry Mpeg(
        string name,
        string origin,
        MpegAudioHeader header,
        ReadOnlyMemory<byte> data,
        long encodedByteCount) =>
        new(
            name,
            origin,
            SoundCodec.Mpeg,
            header.SampleRate,
            header.Channels,
            header.EstimateDuration(encodedByteCount),
            data);

    /// <inheritdoc/>
    public override string ToString() => Codec == SoundCodec.Ilsf
        ? $"{Name} ({Header})"
        : $"{Name} (MPEG {_sampleRate}Hz {_channels}ch {_duration:mm\\:ss})";
}
