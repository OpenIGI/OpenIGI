using System.Buffers.Binary;
using OpenIGI.Audio;

namespace OpenIGI.Engine.Audio;

/// <summary>
/// Reads a plain RIFF/WAVE file down to its PCM and format — the little the remaster's own sound
/// assets need.
/// </summary>
/// <remarks>
/// <para>
/// The retail data never ships a <c>.wav</c> — its sounds are ILSF, which <c>IlsfFile</c> reads.
/// This exists for the remaster track's <em>added</em> sounds, which are authored as ordinary PCM
/// WAV files under the application's own assets. Only uncompressed integer PCM is accepted; an
/// added asset can meet that bar by construction, so anything else is a preparation mistake worth
/// refusing loudly rather than decoding generously.
/// </para>
/// </remarks>
public static class WavPcm
{
    /// <summary>Tries to read a WAV file's format and PCM payload.</summary>
    /// <param name="bytes">The file's bytes.</param>
    /// <param name="format">Receives the sample format.</param>
    /// <param name="pcm">Receives the raw PCM, as the device uploads it.</param>
    /// <returns><see langword="false"/> when the bytes are not an uncompressed PCM WAV.</returns>
    public static bool TryRead(ReadOnlySpan<byte> bytes, out AudioFormat format, out byte[] pcm)
    {
        format = default;
        pcm = [];

        // 'RIFF' <size> 'WAVE', then chunks. The sizes are little-endian, like everything here.
        if (bytes.Length < 44
            || !bytes[..4].SequenceEqual("RIFF"u8)
            || !bytes[8..12].SequenceEqual("WAVE"u8))
        {
            return false;
        }

        int sampleRate = 0;
        int channels = 0;
        int bitsPerSample = 0;
        bool formatRead = false;

        int offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            ReadOnlySpan<byte> tag = bytes.Slice(offset, 4);
            int size = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset + 4, 4));
            offset += 8;

            if (size < 0 || offset + size > bytes.Length)
            {
                return false;
            }

            if (tag.SequenceEqual("fmt "u8) && size >= 16)
            {
                // 1 is integer PCM; 0xFFFE is the extensible header, whose first subformat word
                // spells the same thing for the files today's editors write.
                int audioFormat = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(offset, 2));
                channels = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(offset + 2, 2));
                sampleRate = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset + 4, 4));
                bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(offset + 14, 2));
                formatRead = audioFormat is 1 or 0xFFFE;
            }
            else if (tag.SequenceEqual("data"u8))
            {
                if (!formatRead || channels <= 0 || sampleRate <= 0 || bitsPerSample is not (8 or 16))
                {
                    return false;
                }

                format = new AudioFormat(sampleRate, channels, bitsPerSample);
                pcm = bytes.Slice(offset, size).ToArray();
                return pcm.Length > 0;
            }

            // Chunks are word-aligned; an odd size carries one pad byte.
            offset += size + (size & 1);
        }

        return false;
    }
}
