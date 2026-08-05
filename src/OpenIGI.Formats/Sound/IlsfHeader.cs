using System.Buffers.Binary;
using OpenIGI.Core;

namespace OpenIGI.Formats.Sound;

/// <summary>
/// The twenty-byte header every <c>ILSF</c> sound starts with, parsed on its own so a streaming reader
/// can learn a sound's shape without touching its payload.
/// </summary>
/// <remarks>
/// <para>
/// Read from the original loader <c>0x495F70</c>, which copies these twenty bytes verbatim into its
/// forty-eight-byte runtime record at <c>+16</c> and drives everything else off them. Layout:
/// </para>
/// <code>
/// +0  char[4] "ILSF"
/// +4  u16     format          — see IlsfFormat
/// +6  u16     bitsPerSample   — the *decoded* width; 16 for every shipped sound
/// +8  u16     channels        — 1 or 2
/// +10 u16     reserved        — zero in every shipped file
/// +12 u32     sampleRate      — mostly 22050, but 44100 and 11025 also ship
/// +16 u32     frameCount      — samples per channel, not bytes
/// </code>
/// <para>
/// <c>frameCount</c> counting frames rather than bytes is worth stating, because the header gives no
/// payload length and the two readings differ by the block size. Measurement settles it:
/// <c>m1_ambience.wav</c> (streamed PCM, stereo) has a payload of exactly <c>frameCount * 4</c> bytes,
/// and <c>game_music.wav</c> (streamed ADPCM, stereo) exactly <c>frameCount</c> bytes, which is the one
/// byte per stereo frame that four-bit ADPCM produces.
/// </para>
/// </remarks>
public readonly struct IlsfHeader : IEquatable<IlsfHeader>
{
    /// <summary>The size in bytes of an ILSF header.</summary>
    public const int Size = 20;

    /// <summary>
    /// The rate the original's output runs at: <c>Snd_Init</c> <c>0x4952F0</c> sets the primary buffer
    /// to 22050 Hz.
    /// </summary>
    /// <remarks>
    /// It is <em>not</em> the rate of every shipped sound. A census of the 759 sounds in a retail
    /// installation finds 678 at 22050, 69 at 44100 and 12 at 11025 — the original simply lets
    /// DirectSound resample a secondary buffer into the primary. Nothing may assume a single rate.
    /// </remarks>
    public const int RetailSampleRate = 22050;

    private IlsfHeader(IlsfFormat format, int bitsPerSample, int channels, int reserved, int sampleRate, int frameCount)
    {
        Format = format;
        BitsPerSample = bitsPerSample;
        Channels = channels;
        Reserved = reserved;
        SampleRate = sampleRate;
        FrameCount = frameCount;
    }

    /// <summary>Gets how the samples are stored and whether the original held the sound resident.</summary>
    public IlsfFormat Format { get; }

    /// <summary>Gets the width of a decoded sample in bits: 16 for every shipped sound.</summary>
    public int BitsPerSample { get; }

    /// <summary>Gets the channel count, one or two.</summary>
    public int Channels { get; }

    /// <summary>Gets the header word at <c>+10</c>, zero in every shipped file and unused by the original.</summary>
    public int Reserved { get; }

    /// <summary>Gets the sample rate in hertz.</summary>
    public int SampleRate { get; }

    /// <summary>Gets the number of frames, where a frame is one sample per channel.</summary>
    public int FrameCount { get; }

    /// <summary>Gets a value indicating whether the samples are ADPCM rather than raw PCM.</summary>
    public bool IsAdpcm => Format is IlsfFormat.StreamedAdpcm or IlsfFormat.ResidentAdpcm;

    /// <summary>Gets a value indicating whether the original streamed this sound instead of holding it in memory.</summary>
    public bool IsStreamed => Format is IlsfFormat.StreamedPcm or IlsfFormat.StreamedAdpcm;

    /// <summary>Gets the size in bytes of one decoded frame.</summary>
    public int BlockAlign => Channels * BitsPerSample / 8;

    /// <summary>Gets the size in bytes the payload occupies as stored.</summary>
    public long EncodedByteCount => IsAdpcm
        ? (((long)FrameCount * Channels) + 1) / 2
        : (long)FrameCount * BlockAlign;

    /// <summary>Gets the size in bytes the sound occupies once decoded to PCM.</summary>
    public long DecodedByteCount => (long)FrameCount * BlockAlign;

    /// <summary>Gets how long the sound plays for.</summary>
    public TimeSpan Duration => TimeSpan.FromSeconds((double)FrameCount / SampleRate);

    /// <summary>Determines whether two headers describe the same sound shape.</summary>
    /// <param name="left">The first header.</param>
    /// <param name="right">The second header.</param>
    /// <returns><see langword="true"/> when every field matches.</returns>
    public static bool operator ==(IlsfHeader left, IlsfHeader right) => left.Equals(right);

    /// <summary>Determines whether two headers describe different sound shapes.</summary>
    /// <param name="left">The first header.</param>
    /// <param name="right">The second header.</param>
    /// <returns><see langword="true"/> when any field differs.</returns>
    public static bool operator !=(IlsfHeader left, IlsfHeader right) => !left.Equals(right);

    /// <summary>Determines whether <paramref name="data"/> starts with an ILSF header.</summary>
    /// <param name="data">The bytes to test.</param>
    /// <returns><see langword="true"/> when the first four bytes are <c>ILSF</c>.</returns>
    /// <remarks>Cheap enough to use as the type probe when a resource of unknown kind is registered.</remarks>
    public static bool IsIlsf(ReadOnlySpan<byte> data) =>
        data.Length >= Size && FourCc.FromBytes(data) == IlsfTags.Ilsf;

    /// <summary>Parses and validates an ILSF header.</summary>
    /// <param name="data">At least <see cref="Size"/> bytes, starting at the magic. The payload need not be present.</param>
    /// <returns>The parsed header.</returns>
    /// <exception cref="IlsfFormatException">The bytes do not form a valid header.</exception>
    public static IlsfHeader Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < Size)
        {
            throw new IlsfFormatException($"An ILSF sound needs at least {Size} header bytes, got {data.Length}.");
        }

        FourCc magic = FourCc.FromBytes(data);
        if (magic != IlsfTags.Ilsf)
        {
            throw new IlsfFormatException($"Expected magic 'ILSF', found '{magic}'.");
        }

        int format = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
        int bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
        int channels = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]);
        int reserved = BinaryPrimitives.ReadUInt16LittleEndian(data[10..]);
        int sampleRate = ReadBoundedUInt32(data[12..], "sample rate");
        int frameCount = ReadBoundedUInt32(data[16..], "frame count");

        if (!Enum.IsDefined((IlsfFormat)format))
        {
            throw new IlsfFormatException($"Unknown ILSF storage format {format}; the original knows 0 to 3.");
        }

        if (bitsPerSample is not (8 or 16))
        {
            throw new IlsfFormatException($"An ILSF sound must be 8- or 16-bit, found {bitsPerSample}.");
        }

        if (channels is < 1 or > IlsfAdpcmDecoder.MaximumChannels)
        {
            throw new IlsfFormatException(
                $"An ILSF sound must have 1 or {IlsfAdpcmDecoder.MaximumChannels} channels, found {channels}.");
        }

        if (sampleRate <= 0)
        {
            throw new IlsfFormatException($"An ILSF sound must have a positive sample rate, found {sampleRate}.");
        }

        IlsfFormat storage = (IlsfFormat)format;
        if (storage is IlsfFormat.StreamedAdpcm or IlsfFormat.ResidentAdpcm && bitsPerSample != 16)
        {
            // The decoder produces 16-bit samples unconditionally and the loader sizes its output buffer
            // from this field, so any other width would mis-size it.
            throw new IlsfFormatException($"An ADPCM ILSF sound must declare 16-bit samples, found {bitsPerSample}.");
        }

        return new IlsfHeader(storage, bitsPerSample, channels, reserved, sampleRate, frameCount);
    }

    /// <inheritdoc/>
    public bool Equals(IlsfHeader other) =>
        Format == other.Format
        && BitsPerSample == other.BitsPerSample
        && Channels == other.Channels
        && Reserved == other.Reserved
        && SampleRate == other.SampleRate
        && FrameCount == other.FrameCount;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is IlsfHeader other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(Format, BitsPerSample, Channels, Reserved, SampleRate, FrameCount);

    /// <inheritdoc/>
    public override string ToString() =>
        $"ILSF {Format} {SampleRate}Hz {Channels}ch {BitsPerSample}-bit frames={FrameCount}";

    private static int ReadBoundedUInt32(ReadOnlySpan<byte> source, string what)
    {
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(source);
        if (value > int.MaxValue)
        {
            throw new IlsfFormatException($"The {what} {value} is too large to address.");
        }

        return (int)value;
    }
}
