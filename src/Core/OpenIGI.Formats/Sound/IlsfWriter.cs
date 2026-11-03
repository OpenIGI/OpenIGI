using System.Buffers.Binary;

namespace OpenIGI.Formats.Sound;

/// <summary>
/// Writes an <c>ILSF</c> sound from PCM: the twenty-byte header <see cref="IlsfHeader"/> describes, then the samples.
/// </summary>
/// <remarks>
/// Only the PCM storage forms are written. A sound replacing an ADPCM one keeps its residency (streamed or held in memory),
/// which is what the original's loader <c>0x495F70</c> decides from the format word, and stores PCM, which it reads as readily.
/// </remarks>
public static class IlsfWriter
{
    /// <summary>Writes a PCM sound.</summary>
    /// <param name="pcm">The samples, interleaved, little-endian, at <paramref name="bitsPerSample"/>.</param>
    /// <param name="sampleRate">The sample rate in hertz.</param>
    /// <param name="channels">One or two.</param>
    /// <param name="bitsPerSample">Eight or sixteen.</param>
    /// <param name="streamed">Whether the game streams it (music, ambience) rather than holding it in memory.</param>
    /// <returns>The file.</returns>
    /// <exception cref="ArgumentException">The shape is not one the game reads, or the samples do not fill whole frames.</exception>
    public static byte[] WritePcm(ReadOnlySpan<byte> pcm, int sampleRate, int channels, int bitsPerSample, bool streamed)
    {
        if (channels is < 1 or > IlsfAdpcmDecoder.MaximumChannels)
        {
            throw new ArgumentException($"A sound has one or {IlsfAdpcmDecoder.MaximumChannels} channels, not {channels}.", nameof(channels));
        }

        if (bitsPerSample is not (8 or 16))
        {
            throw new ArgumentException($"A sound is 8- or 16-bit, not {bitsPerSample}.", nameof(bitsPerSample));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        int frameBytes = channels * bitsPerSample / 8;
        if (pcm.Length % frameBytes != 0)
        {
            throw new ArgumentException($"{pcm.Length} byte(s) are not whole {frameBytes}-byte frames.", nameof(pcm));
        }

        byte[] file = new byte[IlsfHeader.Size + pcm.Length];
        Span<byte> header = file;
        "ILSF"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt16LittleEndian(header[4..], (ushort)(streamed ? IlsfFormat.StreamedPcm : IlsfFormat.ResidentPcm));
        BinaryPrimitives.WriteUInt16LittleEndian(header[6..], (ushort)bitsPerSample);
        BinaryPrimitives.WriteUInt16LittleEndian(header[8..], (ushort)channels);
        BinaryPrimitives.WriteUInt16LittleEndian(header[10..], 0);
        BinaryPrimitives.WriteUInt32LittleEndian(header[12..], (uint)sampleRate);
        BinaryPrimitives.WriteUInt32LittleEndian(header[16..], (uint)(pcm.Length / frameBytes));
        pcm.CopyTo(file.AsSpan(IlsfHeader.Size));
        return file;
    }
}
