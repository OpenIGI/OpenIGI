using System.Buffers.Binary;
using System.Text;

namespace OpenIGI.Core.Audio;

/// <summary>
/// Writes uncompressed RIFF/WAVE files.
/// </summary>
/// <remarks>
/// <para>
/// This exists so decoded audio can leave the project and be listened to. The game's sounds are locked
/// inside a container format nothing else reads, and a decoder that has never been heard has not really
/// been verified — a byte count can be right while the samples are noise.
/// </para>
/// <para>
/// Deliberately minimal, in the same spirit as <see cref="Graphics.PngWriter"/>: one chunk layout, PCM
/// only, no metadata. That is the format's actual requirement and nothing beyond it.
/// </para>
/// </remarks>
public static class WaveWriter
{
    private const int FormatChunkSize = 16;
    private const int PcmFormatTag = 1;
    private const int HeaderSize = 44;

    /// <summary>Writes PCM samples to a WAV file, replacing anything already there.</summary>
    /// <param name="path">Where to write.</param>
    /// <param name="pcm">The sample bytes, channel-interleaved, in the file's declared width.</param>
    /// <param name="sampleRate">The sample rate in hertz.</param>
    /// <param name="channels">The channel count.</param>
    /// <param name="bitsPerSample">The sample width in bits, 8 or 16.</param>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is outside the range WAV allows.</exception>
    /// <exception cref="IOException">The file could not be written.</exception>
    public static void Write(string path, ReadOnlySpan<byte> pcm, int sampleRate, int channels, int bitsPerSample = 16)
    {
        ArgumentNullException.ThrowIfNull(path);

        using FileStream stream = File.Create(path);
        Write(stream, pcm, sampleRate, channels, bitsPerSample);
    }

    /// <summary>Writes PCM samples to a stream as a WAV file.</summary>
    /// <param name="stream">The destination, written from its current position.</param>
    /// <param name="pcm">The sample bytes, channel-interleaved, in the file's declared width.</param>
    /// <param name="sampleRate">The sample rate in hertz.</param>
    /// <param name="channels">The channel count.</param>
    /// <param name="bitsPerSample">The sample width in bits, 8 or 16.</param>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is outside the range WAV allows.</exception>
    public static void Write(Stream stream, ReadOnlySpan<byte> pcm, int sampleRate, int channels, int bitsPerSample = 16)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(sampleRate, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(channels, 0);
        if (bitsPerSample is not (8 or 16))
        {
            throw new ArgumentOutOfRangeException(nameof(bitsPerSample), bitsPerSample, "A PCM WAV is 8- or 16-bit.");
        }

        int blockAlign = channels * bitsPerSample / 8;
        Span<byte> header = stackalloc byte[HeaderSize];

        Encoding.ASCII.GetBytes("RIFF").CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], (uint)(HeaderSize - 8 + pcm.Length));
        Encoding.ASCII.GetBytes("WAVE").CopyTo(header[8..]);

        Encoding.ASCII.GetBytes("fmt ").CopyTo(header[12..]);
        BinaryPrimitives.WriteUInt32LittleEndian(header[16..], FormatChunkSize);
        BinaryPrimitives.WriteUInt16LittleEndian(header[20..], PcmFormatTag);
        BinaryPrimitives.WriteUInt16LittleEndian(header[22..], (ushort)channels);
        BinaryPrimitives.WriteUInt32LittleEndian(header[24..], (uint)sampleRate);
        BinaryPrimitives.WriteUInt32LittleEndian(header[28..], (uint)(sampleRate * blockAlign));
        BinaryPrimitives.WriteUInt16LittleEndian(header[32..], (ushort)blockAlign);
        BinaryPrimitives.WriteUInt16LittleEndian(header[34..], (ushort)bitsPerSample);

        Encoding.ASCII.GetBytes("data").CopyTo(header[36..]);
        BinaryPrimitives.WriteUInt32LittleEndian(header[40..], (uint)pcm.Length);

        stream.Write(header);
        stream.Write(pcm);
    }
}
