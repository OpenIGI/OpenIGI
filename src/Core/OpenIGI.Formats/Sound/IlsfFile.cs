using System.Buffers.Binary;

namespace OpenIGI.Formats.Sound;

/// <summary>
/// A parsed <c>ILSF</c> sound — "InnerLoop Sound File", the form every <c>.wav</c> in the installation
/// actually holds, whether loose on disk or as a <c>BODY</c> inside <c>sounds.res</c>.
/// </summary>
/// <remarks>
/// Parsing is zero-copy: the file keeps a <see cref="ReadOnlyMemory{T}"/> view of its own bytes and
/// <see cref="Payload"/> is a slice of it. Read from the original loader <c>0x495F70</c> and confirmed
/// against the shipped files; the header layout is documented on <see cref="IlsfHeader"/> and the whole
/// system in the original.
/// </remarks>
public sealed class IlsfFile
{
    /// <summary>The size in bytes of an ILSF header.</summary>
    public const int HeaderSize = IlsfHeader.Size;

    private IlsfFile(ReadOnlyMemory<byte> data, IlsfHeader header, ReadOnlyMemory<byte> payload)
    {
        Data = data;
        Header = header;
        Payload = payload;
    }

    /// <summary>Gets the sound bytes, header included.</summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>Gets the parsed header.</summary>
    public IlsfHeader Header { get; }

    /// <summary>Gets the encoded payload, trimmed to the length <see cref="FrameCount"/> implies.</summary>
    public ReadOnlyMemory<byte> Payload { get; }

    /// <summary>Gets how the samples are stored and whether the original held the sound resident.</summary>
    public IlsfFormat Format => Header.Format;

    /// <summary>Gets the width of a decoded sample in bits: 16 for every shipped sound.</summary>
    public int BitsPerSample => Header.BitsPerSample;

    /// <summary>Gets the channel count, one or two.</summary>
    public int Channels => Header.Channels;

    /// <summary>Gets the sample rate in hertz.</summary>
    public int SampleRate => Header.SampleRate;

    /// <summary>Gets the number of frames, where a frame is one sample per channel.</summary>
    public int FrameCount => Header.FrameCount;

    /// <summary>Gets a value indicating whether the samples are ADPCM rather than raw PCM.</summary>
    public bool IsAdpcm => Header.IsAdpcm;

    /// <summary>Gets a value indicating whether the original streamed this sound instead of holding it in memory.</summary>
    public bool IsStreamed => Header.IsStreamed;

    /// <summary>Gets the size in bytes of one decoded frame.</summary>
    public int BlockAlign => Header.BlockAlign;

    /// <summary>Gets the size in bytes the sound occupies once decoded to PCM.</summary>
    public long DecodedByteCount => Header.DecodedByteCount;

    /// <summary>Gets how long the sound plays for.</summary>
    public TimeSpan Duration => Header.Duration;

    /// <summary>Determines whether <paramref name="data"/> starts with an ILSF header.</summary>
    /// <param name="data">The bytes to test.</param>
    /// <returns><see langword="true"/> when the first four bytes are <c>ILSF</c>.</returns>
    /// <remarks>Cheap enough to use as the type probe when a resource of unknown kind is registered.</remarks>
    public static bool IsIlsf(ReadOnlySpan<byte> data) => IlsfHeader.IsIlsf(data);

    /// <summary>Parses an ILSF sound.</summary>
    /// <param name="data">The sound bytes, starting at the header. Trailing bytes beyond the payload are ignored.</param>
    /// <returns>The parsed sound.</returns>
    /// <exception cref="IlsfFormatException">The bytes do not form a valid sound.</exception>
    public static IlsfFile Read(ReadOnlyMemory<byte> data)
    {
        IlsfHeader header = IlsfHeader.Read(data.Span);

        long expected = header.EncodedByteCount;
        long available = data.Length - HeaderSize;
        if (available < expected)
        {
            throw new IlsfFormatException(
                $"An ILSF sound of {header.FrameCount} frame(s) needs {expected} payload byte(s), but only {available} are available.");
        }

        // Retail files occasionally carry a byte or two of padding past the last frame; the original
        // never looks at them because it drives every read off the frame count.
        return new IlsfFile(data, header, data.Slice(HeaderSize, (int)expected));
    }

    /// <summary>Returns the sound as PCM in its declared width, channel count and rate.</summary>
    /// <returns>
    /// The payload itself when the sound is already PCM — no copy is made — or a freshly decoded buffer
    /// when it is ADPCM.
    /// </returns>
    /// <remarks>
    /// This is the resident path, the one the original takes for <see cref="IlsfFormat.ResidentPcm"/> and
    /// <see cref="IlsfFormat.ResidentAdpcm"/>. Prefer <see cref="IlsfStreamReader"/> for the streamed
    /// forms: a retail music track expands to about thirteen megabytes.
    /// </remarks>
    public ReadOnlyMemory<byte> DecodePcm()
    {
        if (!IsAdpcm)
        {
            return Payload;
        }

        int sampleCount = FrameCount * Channels;
        short[] samples = new short[sampleCount];
        new IlsfAdpcmDecoder(Channels).Decode(Payload.Span, samples, FrameCount);

        byte[] decoded = new byte[DecodedByteCount];
        for (int i = 0; i < sampleCount; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(decoded.AsSpan(i * sizeof(short)), samples[i]);
        }

        return decoded;
    }

    /// <inheritdoc/>
    public override string ToString() => Header.ToString();
}
