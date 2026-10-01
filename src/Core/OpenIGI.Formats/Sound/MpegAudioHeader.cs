using System.Buffers.Binary;

namespace OpenIGI.Formats.Sound;

/// <summary>
/// The four-byte frame header an MPEG audio stream starts every frame with, parsed on its own so the
/// sound bank can learn a track's shape without decoding a sample of it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the port needs this at all.</b> Every sound in I.G.I. 1 is <c>ILSF</c>, and so is almost every
/// sound in I.G.I. 2 — but not its music. The sequel's menu theme ships as
/// <c>menusystem/sound/menu_music.mp3</c> inside <c>menusystem/sound/sounds.res</c>, a 320 kbps
/// MPEG-1 Layer III stream, and its mission scores ship as loose <c>*_music.mp3</c> files beside each
/// level. Retail decodes them through Miles (<c>mss32.dll</c>, shipped in the installation's <c>pc</c>
/// directory); the port decodes them with a managed decoder behind
/// <see cref="MpegAudioStreamReader"/>.
/// </para>
/// <para>
/// The layout is ISO/IEC 11172-3 §2.4.1.3 and its MPEG-2 and MPEG-2.5 extensions, read big-endian:
/// </para>
/// <code>
/// 31..21  sync            — eleven set bits
/// 20..19  version         — 00 MPEG-2.5, 01 reserved, 10 MPEG-2, 11 MPEG-1
/// 18..17  layer           — 00 reserved, 01 Layer III, 10 Layer II, 11 Layer I
/// 16      protection      — zero when a 16-bit CRC follows the header
/// 15..12  bitrate index   — 0 is "free", 15 is invalid
/// 11..10  sample rate     — 3 is reserved
/// 9       padding         — one extra slot in this frame
/// 8       private
/// 7..6    channel mode    — 00 stereo, 01 joint stereo, 10 dual channel, 11 mono
/// </code>
/// <para>
/// Only the first frame is read. That is enough for the rate, the channel count and — for the constant
/// bitrate every retail track uses — a duration, and it keeps mounting an archive cheap: a scan for the
/// true length would have to walk all eight megabytes of the menu theme.
/// </para>
/// </remarks>
public readonly struct MpegAudioHeader : IEquatable<MpegAudioHeader>
{
    /// <summary>The size in bytes of a frame header.</summary>
    public const int Size = 4;

    /// <summary>The number of bytes <see cref="IsMpegAudio"/> may have to look at.</summary>
    /// <remarks>
    /// A header plus the largest ID3v2 tag header that can precede it. Enough for any probe, so a
    /// caller that only wants to classify a file need not read the whole of it.
    /// </remarks>
    public const int ProbeSize = Id3TagHeaderSize + Size;

    /// <summary>The size in bytes of an ID3v2 tag header, which some encoders write before the audio.</summary>
    private const int Id3TagHeaderSize = 10;

    // ISO/IEC 11172-3 table 3 and ISO/IEC 13818-3 table 3, in kbit/s, indexed by bitrate index 1..14.
    // Index 0 ("free") and 15 ("bad") are rejected before either table is consulted, so the zeroth slot
    // is only padding to keep the index arithmetic honest.
    private static readonly int[][] BitRatesInKilobits =
    [
        // MPEG-1 Layer I, II, III.
        [0, 32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448],
        [0, 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384],
        [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320],

        // MPEG-2 and MPEG-2.5 Layer I, then Layers II and III, which share a row.
        [0, 32, 48, 56, 64, 80, 96, 112, 128, 144, 160, 176, 192, 224, 256],
        [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160],
    ];

    // ISO/IEC 11172-3 table 4, by version then sample-rate index. MPEG-2 halves MPEG-1's rates and
    // MPEG-2.5 quarters them.
    private static readonly int[][] SampleRates =
    [
        [11025, 12000, 8000],
        [44100, 48000, 32000],
        [22050, 24000, 16000],
    ];

    private MpegAudioHeader(
        MpegAudioVersion version,
        int layer,
        int sampleRate,
        int channels,
        int bitRate,
        bool hasCrc)
    {
        Version = version;
        Layer = layer;
        SampleRate = sampleRate;
        Channels = channels;
        BitRate = bitRate;
        HasCrc = hasCrc;
    }

    /// <summary>Gets which MPEG audio standard the stream follows.</summary>
    public MpegAudioVersion Version { get; }

    /// <summary>Gets the layer, 1 to 3. Retail is Layer III throughout.</summary>
    public int Layer { get; }

    /// <summary>Gets the sample rate in hertz.</summary>
    public int SampleRate { get; }

    /// <summary>Gets the channel count, one or two.</summary>
    public int Channels { get; }

    /// <summary>Gets the bitrate of the first frame, in bits per second.</summary>
    public int BitRate { get; }

    /// <summary>Gets a value indicating whether a 16-bit CRC follows each frame header.</summary>
    public bool HasCrc { get; }

    /// <summary>Gets the size in bytes of one decoded frame: sixteen bits per channel.</summary>
    public int BlockAlign => Channels * sizeof(short);

    /// <summary>Determines whether two headers describe the same stream shape.</summary>
    /// <param name="left">The first header.</param>
    /// <param name="right">The second header.</param>
    /// <returns><see langword="true"/> when every field matches.</returns>
    public static bool operator ==(MpegAudioHeader left, MpegAudioHeader right) => left.Equals(right);

    /// <summary>Determines whether two headers describe different stream shapes.</summary>
    /// <param name="left">The first header.</param>
    /// <param name="right">The second header.</param>
    /// <returns><see langword="true"/> when any field differs.</returns>
    public static bool operator !=(MpegAudioHeader left, MpegAudioHeader right) => !left.Equals(right);

    /// <summary>Determines whether <paramref name="data"/> begins an MPEG audio stream.</summary>
    /// <param name="data">The bytes to test; <see cref="ProbeSize"/> of them is always enough.</param>
    /// <returns><see langword="true"/> when a frame header, or an ID3 tag introducing one, is present.</returns>
    /// <remarks>
    /// The type probe the sound bank classifies an archive member with, and it has to be strict: a
    /// sound archive holds nothing else, but eleven set bits alone would match a good deal of
    /// compressed data. Every reserved encoding is rejected, which leaves a false positive needing
    /// eleven set bits and then four independently valid fields.
    /// </remarks>
    public static bool IsMpegAudio(ReadOnlySpan<byte> data) => TryRead(data, out _);

    /// <summary>Parses the first frame header of an MPEG audio stream.</summary>
    /// <param name="data">The bytes, starting at the frame header or at an ID3 tag before it.</param>
    /// <returns>The parsed header.</returns>
    /// <exception cref="MpegAudioFormatException">The bytes do not begin a readable MPEG audio stream.</exception>
    public static MpegAudioHeader Read(ReadOnlySpan<byte> data)
    {
        if (!TryRead(data, out MpegAudioHeader header))
        {
            throw new MpegAudioFormatException("The bytes do not begin with a readable MPEG audio frame header.");
        }

        return header;
    }

    /// <summary>Parses the first frame header, reporting failure rather than throwing.</summary>
    /// <param name="data">The bytes, starting at the frame header or at an ID3 tag before it.</param>
    /// <param name="header">Receives the header when one is found.</param>
    /// <returns><see langword="false"/> when the bytes do not begin a readable MPEG audio stream.</returns>
    public static bool TryRead(ReadOnlySpan<byte> data, out MpegAudioHeader header)
    {
        header = default;

        // Some encoders write an ID3v2 tag before the first frame. Its declared length is seven bits per
        // byte, which is how a tag can never contain a sync word of its own.
        if (data.Length >= Id3TagHeaderSize && data[0] == (byte)'I' && data[1] == (byte)'D' && data[2] == (byte)'3')
        {
            long skip = Id3TagHeaderSize
                + (((long)(data[6] & 0x7F) << 21)
                    | ((long)(data[7] & 0x7F) << 14)
                    | ((long)(data[8] & 0x7F) << 7)
                    | (data[9] & 0x7Fu));

            if (skip >= data.Length)
            {
                // The tag runs past what the caller handed over. That is not a rejection -- a probe
                // buffer is deliberately short -- but nothing here can confirm the frame either.
                return false;
            }

            data = data[(int)skip..];
        }

        if (data.Length < Size)
        {
            return false;
        }

        uint word = BinaryPrimitives.ReadUInt32BigEndian(data);
        if ((word & 0xFFE0_0000u) != 0xFFE0_0000u)
        {
            return false;
        }

        int versionBits = (int)((word >> 19) & 0x3);
        int layerBits = (int)((word >> 17) & 0x3);
        int bitRateIndex = (int)((word >> 12) & 0xF);
        int sampleRateIndex = (int)((word >> 10) & 0x3);
        int channelMode = (int)((word >> 6) & 0x3);

        if (versionBits == 1 || layerBits == 0 || bitRateIndex is 0 or 15 || sampleRateIndex == 3)
        {
            // Reserved version, reserved layer, a free-format or invalid bitrate, or a reserved sample
            // rate. Retail uses none of them, and rejecting them is what keeps the probe honest.
            return false;
        }

        MpegAudioVersion version = versionBits switch
        {
            0 => MpegAudioVersion.Version25,
            2 => MpegAudioVersion.Version2,
            _ => MpegAudioVersion.Version1,
        };

        // The layer bits count down: 01 is Layer III and 11 is Layer I.
        int layer = 4 - layerBits;

        // MPEG-1 gets a row per layer; MPEG-2 and MPEG-2.5 share one row for Layer I and another for
        // Layers II and III.
        int bitRateRow = version == MpegAudioVersion.Version1
            ? layer - 1
            : layer == 1 ? 3 : 4;

        header = new MpegAudioHeader(
            version,
            layer,
            SampleRates[(int)version][sampleRateIndex],
            channels: channelMode == 3 ? 1 : 2,
            bitRate: BitRatesInKilobits[bitRateRow][bitRateIndex] * 1000,
            hasCrc: ((word >> 16) & 1) == 0);

        return true;
    }

    /// <summary>Estimates how long a stream of <paramref name="byteCount"/> bytes plays for.</summary>
    /// <param name="byteCount">The size of the audio data, tag bytes excluded.</param>
    /// <returns>The playing time.</returns>
    /// <remarks>
    /// Exact for the constant-bitrate encodes both games ship and an estimate for anything else, which
    /// is why it is a method rather than a property: nothing may mistake it for a measured length. The
    /// decoder reports the true one, at the cost of walking the whole stream.
    /// </remarks>
    public TimeSpan EstimateDuration(long byteCount) => BitRate <= 0 || byteCount <= 0
        ? TimeSpan.Zero
        : TimeSpan.FromSeconds((double)byteCount * 8 / BitRate);

    /// <inheritdoc/>
    public bool Equals(MpegAudioHeader other) =>
        Version == other.Version
        && Layer == other.Layer
        && SampleRate == other.SampleRate
        && Channels == other.Channels
        && BitRate == other.BitRate
        && HasCrc == other.HasCrc;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is MpegAudioHeader other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(Version, Layer, SampleRate, Channels, BitRate, HasCrc);

    /// <inheritdoc/>
    public override string ToString() =>
        $"{Version} Layer {Layer} {SampleRate}Hz {Channels}ch {BitRate / 1000}kbps";
}
