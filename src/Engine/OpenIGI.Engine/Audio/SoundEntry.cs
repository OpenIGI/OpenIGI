using OpenIGI.Core.Numerics;
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

    /// <summary>
    /// Gets how long I.G.I. 2 reckons the sound to last, in seconds, <c>Sound_GetSampleDuration</c> (<c>igi2ed 0x55E0E0</c>) on the
    /// x87 at single precision: an ILSF sound's frames over its rate; an MPEG track inside an archive its milliseconds, which
    /// <c>mpgsize</c> (<c>igi2ed 0x55E140</c>) reckons from the bitrate of whatever its first four bytes say and its size, times a
    /// thousandth; a loose track its size over a rate of nought.
    /// </summary>
    /// <remarks>Retail times a loose ILSF file the same way as a loose track, its size over nought; I.G.I. 2's sounds are all in archives.</remarks>
    public double SampleSeconds => Codec == SoundCodec.Ilsf
        ? ((X87Single)(double)(uint)Header.FrameCount / (double)Header.SampleRate).Value
        : !Data.IsEmpty
            ? ((X87Single)(double)(uint)MpegMilliseconds(Data.Span) * 0.001).Value
            : double.PositiveInfinity;

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

    // mpgsize's table of rates, indexed by sixteen times the layer plus three times the version, plus the header's bitrate field;
    // its first row is the rates and constants it sits beside, which a header with no layer reads.
    private static readonly int[] MpegBitrates =
    [
        0, 0, 22050, 24000, 16000, 44100, 48000, 32000, 0, 1074003968, 0, 0, 0, 1073741824, 0, 1072693248,
        0, 32000, 48000, 56000, 64000, 80000, 96000, 112000, 128000, 144000, 160000, 176000, 192000, 224000, 256000, 0,
        0, 8000, 16000, 24000, 32000, 40000, 48000, 56000, 64000, 80000, 96000, 112000, 128000, 144000, 160000, 0,
        0, 8000, 16000, 24000, 32000, 40000, 48000, 56000, 64000, 80000, 96000, 112000, 128000, 144000, 160000, 0,
        0, 32000, 64000, 96000, 128000, 160000, 192000, 224000, 256000, 288000, 320000, 352000, 384000, 416000, 448000, 0,
        0, 32000, 48000, 56000, 64000, 80000, 96000, 112000, 128000, 160000, 192000, 224000, 256000, 320000, 384000, 0,
        0, 32000, 40000, 48000, 56000, 64000, 80000, 96000, 112000, 128000, 160000, 192000, 224000, 256000, 320000, 0,
    ];

    // mpgsize: the stream's length in bytes, times eight thousand, over the bitrate its first header names, cut to a whole
    // millisecond. A version-2.5 header always reads the ninth rate of the version-1 rows; a rate of nought is the x87's
    // integer indefinite.
    private static int MpegMilliseconds(ReadOnlySpan<byte> data)
    {
        uint header = data.Length >= 4 ? (uint)((data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3]) : 0u;
        int layer = (int)(-(header >> 17) & 3);
        int version = (int)((header >> 19) & 3);
        int rate = (int)((header >> 12) & 0xF);
        int row;
        if (version == 2)
        {
            row = 0;
        }
        else
        {
            if (version == 0)
            {
                rate = 8;
            }

            row = 1;
        }

        double milliseconds = (((X87Single)(double)(long)data.Length * 8000.0) / (double)MpegBitrates[(16 * (layer + (3 * row))) + rate]).Value;
        double whole = Math.Truncate(milliseconds);
        return double.IsNaN(whole) || whole < int.MinValue || whole > int.MaxValue ? int.MinValue : (int)whole;
    }

    /// <inheritdoc/>
    public override string ToString() => Codec == SoundCodec.Ilsf
        ? $"{Name} ({Header})"
        : $"{Name} (MPEG {_sampleRate}Hz {_channels}ch {_duration:mm\\:ss})";
}
