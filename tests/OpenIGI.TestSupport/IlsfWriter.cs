using System.Buffers.Binary;
using System.Text;

namespace OpenIGI.TestSupport;

/// <summary>
/// Builds small ILSF sounds so the reader and the ADPCM decoder can be tested without shipping original
/// game audio.
/// </summary>
/// <remarks>
/// <para>
/// The writer reimplements the header layout and the ADPCM encoder from the format description rather
/// than calling into the production reader, so a test built on it fails when reader and description
/// disagree. In particular it re-derives the two rules that are easy to get backwards: nibbles are
/// packed <em>high first</em>, and channels interleave per sample rather than per block.
/// </para>
/// <para>
/// The encoder here is the ordinary IMA quantiser. It is not claimed to be the encoder the original
/// tools used — it exists so that encode-then-decode round-trips, which is what pins the decoder.
/// </para>
/// </remarks>
public sealed class IlsfWriter
{
    /// <summary>The size in bytes of an ILSF header.</summary>
    public const int HeaderSize = 20;

    private static readonly int[] IndexTable =
    [
        -1, -1, -1, -1, 2, 4, 6, 8,
        -1, -1, -1, -1, 2, 4, 6, 8,
    ];

    private static readonly int[] StepTable =
    [
        7, 8, 9, 10, 11, 12, 13, 14, 16, 17,
        19, 21, 23, 25, 28, 31, 34, 37, 41, 45,
        50, 55, 60, 66, 73, 80, 88, 97, 107, 118,
        130, 143, 157, 173, 190, 209, 230, 253, 279, 307,
        337, 371, 408, 449, 494, 544, 598, 658, 724, 796,
        876, 963, 1060, 1166, 1282, 1411, 1552, 1707, 1878, 2066,
        2272, 2499, 2749, 3024, 3327, 3660, 4026, 4428, 4871, 5358,
        5894, 6484, 7132, 7845, 8630, 9493, 10442, 11487, 12635, 13899,
        15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767,
    ];

    /// <summary>Renders a raw-PCM sound.</summary>
    /// <param name="samples">Channel-interleaved 16-bit samples; the length must be a multiple of <paramref name="channels"/>.</param>
    /// <param name="channels">The channel count, one or two.</param>
    /// <param name="sampleRate">The sample rate in hertz.</param>
    /// <param name="resident"><see langword="true"/> for the resident code, <see langword="false"/> for the streamed one.</param>
    /// <returns>The sound bytes, header included.</returns>
    public static byte[] WritePcm(ReadOnlySpan<short> samples, int channels = 1, int sampleRate = 22050, bool resident = true)
    {
        ValidateChannels(channels);
        if (samples.Length % channels != 0)
        {
            throw new ArgumentException(
                $"{samples.Length} sample(s) do not divide into frames of {channels} channel(s).",
                nameof(samples));
        }

        int frames = samples.Length / channels;
        byte[] result = new byte[HeaderSize + (samples.Length * sizeof(short))];
        WriteHeader(result, resident ? 1 : 0, 16, channels, sampleRate, frames);

        for (int i = 0; i < samples.Length; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(HeaderSize + (i * sizeof(short))), samples[i]);
        }

        return result;
    }

    /// <summary>Renders an ADPCM sound by quantising <paramref name="samples"/>.</summary>
    /// <param name="samples">Channel-interleaved 16-bit samples; the length must be a multiple of <paramref name="channels"/>.</param>
    /// <param name="channels">The channel count, one or two.</param>
    /// <param name="sampleRate">The sample rate in hertz.</param>
    /// <param name="resident"><see langword="true"/> for the resident code, <see langword="false"/> for the streamed one.</param>
    /// <returns>The sound bytes, header included.</returns>
    public static byte[] WriteAdpcm(ReadOnlySpan<short> samples, int channels = 1, int sampleRate = 22050, bool resident = true)
    {
        ValidateChannels(channels);
        if (samples.Length % channels != 0)
        {
            throw new ArgumentException(
                $"{samples.Length} sample(s) do not divide into frames of {channels} channel(s).",
                nameof(samples));
        }

        int frames = samples.Length / channels;
        int payload = (samples.Length + 1) / 2;
        byte[] result = new byte[HeaderSize + payload];
        WriteHeader(result, resident ? 3 : 2, 16, channels, sampleRate, frames);

        int[] predictor = new int[channels];
        int[] index = new int[channels];
        Span<byte> encoded = result.AsSpan(HeaderSize);

        for (int i = 0; i < samples.Length; i++)
        {
            int channel = i % channels;
            int nibble = Quantise(samples[i], ref predictor[channel], ref index[channel]);

            // High nibble first, matching the decoder at 0x496280.
            if (i % 2 == 0)
            {
                encoded[i / 2] = (byte)(nibble << 4);
            }
            else
            {
                encoded[i / 2] |= (byte)nibble;
            }
        }

        return result;
    }

    /// <summary>Renders a sound whose header claims more frames than its payload holds.</summary>
    /// <param name="frameCount">The frame count to declare.</param>
    /// <param name="payloadBytes">The number of payload bytes to actually write.</param>
    /// <param name="channels">The channel count, one or two.</param>
    /// <returns>The malformed sound bytes.</returns>
    /// <remarks>Used to pin the reader's truncation check.</remarks>
    public static byte[] WriteTruncated(int frameCount, int payloadBytes, int channels = 1)
    {
        ValidateChannels(channels);
        byte[] result = new byte[HeaderSize + payloadBytes];
        WriteHeader(result, 1, 16, channels, 22050, frameCount);
        return result;
    }

    /// <summary>Renders a header with arbitrary field values, for the reader's validation tests.</summary>
    /// <param name="format">The storage format code.</param>
    /// <param name="bitsPerSample">The declared sample width.</param>
    /// <param name="channels">The declared channel count.</param>
    /// <param name="sampleRate">The declared sample rate.</param>
    /// <param name="frameCount">The declared frame count.</param>
    /// <param name="payloadBytes">The number of payload bytes to append.</param>
    /// <returns>The sound bytes.</returns>
    public static byte[] WriteRaw(int format, int bitsPerSample, int channels, int sampleRate, int frameCount, int payloadBytes = 0)
    {
        byte[] result = new byte[HeaderSize + payloadBytes];
        WriteHeader(result, format, bitsPerSample, channels, sampleRate, frameCount);
        return result;
    }

    private static void ValidateChannels(int channels)
    {
        if (channels is < 1 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(channels), channels, "An ILSF sound has one or two channels.");
        }
    }

    private static void WriteHeader(byte[] buffer, int format, int bitsPerSample, int channels, int sampleRate, int frameCount)
    {
        Encoding.Latin1.GetBytes("ILSF").CopyTo(buffer, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4), (ushort)format);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(6), (ushort)bitsPerSample);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(8), (ushort)channels);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(10), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(12), (uint)sampleRate);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(16), (uint)frameCount);
    }

    /// <summary>Quantises one sample and advances the encoder state exactly as the decoder will.</summary>
    private static int Quantise(short sample, ref int predictor, ref int index)
    {
        int step = StepTable[index];
        int delta = sample - predictor;

        int nibble = 0;
        if (delta < 0)
        {
            nibble = 8;
            delta = -delta;
        }

        int magnitude = step >> 3;
        if (delta >= step)
        {
            nibble |= 4;
            delta -= step;
            magnitude += step;
        }

        if (delta >= step >> 1)
        {
            nibble |= 2;
            delta -= step >> 1;
            magnitude += step >> 1;
        }

        if (delta >= step >> 2)
        {
            nibble |= 1;
            magnitude += step >> 2;
        }

        predictor = Math.Clamp(
            (nibble & 8) != 0 ? predictor - magnitude : predictor + magnitude,
            short.MinValue,
            short.MaxValue);
        index = Math.Clamp(index + IndexTable[nibble], 0, 88);
        return nibble;
    }
}
