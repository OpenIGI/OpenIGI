using System.Buffers.Binary;
using OpenIGI.Formats.Sound;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>Covers the ILSF header, the ADPCM decoder and the streaming reader.</summary>
public sealed class IlsfFileTests
{
    [Fact]
    public void APcmSoundRoundTripsThroughTheReader()
    {
        short[] samples = [0, 1000, -1000, 32767, -32768, 7];
        byte[] bytes = IlsfWriter.WritePcm(samples, channels: 2, sampleRate: 22050);

        IlsfFile sound = IlsfFile.Read(bytes);

        Assert.Equal(IlsfFormat.ResidentPcm, sound.Format);
        Assert.Equal(16, sound.BitsPerSample);
        Assert.Equal(2, sound.Channels);
        Assert.Equal(22050, sound.SampleRate);
        Assert.Equal(3, sound.FrameCount);
        Assert.Equal(4, sound.BlockAlign);
        Assert.False(sound.IsAdpcm);
        Assert.False(sound.IsStreamed);
        Assert.Equal(12, sound.Payload.Length);

        // PCM decoding is the payload itself, with no copy.
        Assert.True(sound.DecodePcm().Span.SequenceEqual(sound.Payload.Span));
    }

    [Fact]
    public void TheStorageCodeSplitsResidentFromStreamedAndPcmFromAdpcm()
    {
        // Bit 0 selects resident, bit 1 selects ADPCM — the reading of 0x495F70's four switch arms.
        Assert.True(IlsfFile.Read(IlsfWriter.WritePcm([1], resident: false)).IsStreamed);
        Assert.False(IlsfFile.Read(IlsfWriter.WritePcm([1], resident: true)).IsStreamed);
        Assert.True(IlsfFile.Read(IlsfWriter.WriteAdpcm([1], resident: false)).IsAdpcm);
        Assert.True(IlsfFile.Read(IlsfWriter.WriteAdpcm([1], resident: false)).IsStreamed);
        Assert.True(IlsfFile.Read(IlsfWriter.WriteAdpcm([1], resident: true)).IsAdpcm);
        Assert.False(IlsfFile.Read(IlsfWriter.WriteAdpcm([1], resident: true)).IsStreamed);
    }

    [Fact]
    public void TheDecoderTakesTheHighNibbleOfAByteFirst()
    {
        // One byte 0x7F, mono. Starting from predictor 0 and step index 0 (step 7):
        //   nibble 7 = 0b0111 -> magnitude 7/8 + 7 + 7/2 + 7/4 = 0 + 7 + 3 + 1 = 11, predictor +11,
        //              index 0 + 8 = 8 (step 16)
        //   nibble 15 = 0b1111 -> sign set, magnitude 16/8 + 16 + 8 + 4 = 30, predictor 11 - 30 = -19
        // Taking the low nibble first would give -11 then +30, so this pins the nibble order.
        short[] decoded = new short[2];
        int consumed = new IlsfAdpcmDecoder(1).Decode([0x7F], decoded, frameCount: 2);

        Assert.Equal(1, consumed);
        Assert.Equal([11, -19], decoded);
    }

    [Fact]
    public void EachChannelKeepsItsOwnPredictorAndStepIndex()
    {
        // The same byte 0x7F read as one stereo frame: nibble 7 to the left channel and nibble 15 to
        // the right, each starting from a fresh predictor and step index. Left gets +11 as above;
        // right gets the same magnitude 11 with the sign bit set, so -11 — not the -19 a single shared
        // state would produce.
        short[] decoded = new short[2];
        new IlsfAdpcmDecoder(2).Decode([0x7F], decoded, frameCount: 1);

        Assert.Equal([11, -11], decoded);
    }

    [Fact]
    public void DecodingInSlicesMatchesDecodingInOneCall()
    {
        short[] source = MakeTone(frames: 64, channels: 1);
        byte[] bytes = IlsfWriter.WriteAdpcm(source);
        ReadOnlyMemory<byte> payload = IlsfFile.Read(bytes).Payload;

        short[] whole = new short[source.Length];
        new IlsfAdpcmDecoder(1).Decode(payload.Span, whole, source.Length);

        // Odd slice lengths leave a nibble pending inside the decoder, which is the case a streaming
        // caller hits on every buffer boundary.
        short[] sliced = new short[source.Length];
        IlsfAdpcmDecoder decoder = new(1);
        int written = 0;
        int read = 0;
        foreach (int frames in (int[])[7, 1, 24, 32])
        {
            int needed = ((frames - (decoder.HasPendingNibble ? 1 : 0)) + 1) / 2;
            read += decoder.Decode(payload.Span[read..], sliced.AsSpan(written), frames);
            written += frames;
            Assert.True(needed >= 0);
        }

        Assert.Equal(whole, sliced);
    }

    [Fact]
    public void AnAdpcmRoundTripStaysCloseToTheSource()
    {
        // Encoder and decoder are written independently from the same table description, so a small
        // round-trip error is evidence that both tables are right — a wrong step table diverges wildly.
        short[] source = MakeTone(frames: 512, channels: 2, period: 256);
        IlsfFile sound = IlsfFile.Read(IlsfWriter.WriteAdpcm(source, channels: 2));

        ReadOnlyMemory<byte> pcm = sound.DecodePcm();
        Assert.Equal(source.Length * sizeof(short), pcm.Length);

        // The first frames are skipped on purpose. ADPCM starts at step index 0, whose step is 7, so
        // the predictor can only move about 13 per frame until the index climbs — the codec is
        // slew-rate limited out of the gate and the opening error says nothing about the tables.
        const int settled = 64;
        int worst = 0;
        for (int i = settled * 2; i < source.Length; i++)
        {
            short decoded = BinaryPrimitives.ReadInt16LittleEndian(pcm.Span[(i * sizeof(short))..]);
            worst = Math.Max(worst, Math.Abs(decoded - source[i]));
        }

        Assert.True(worst < 512, $"Worst-case ADPCM error after settling was {worst}.");
    }

    [Fact]
    public void AMonoAdpcmSoundPacksTwoFramesIntoOneByte()
    {
        IlsfFile odd = IlsfFile.Read(IlsfWriter.WriteAdpcm(new short[5]));
        Assert.Equal(5, odd.FrameCount);
        Assert.Equal(3, odd.Payload.Length);

        IlsfFile even = IlsfFile.Read(IlsfWriter.WriteAdpcm(new short[6]));
        Assert.Equal(3, even.Payload.Length);
    }

    [Fact]
    public void TrailingBytesPastTheLastFrameAreIgnored()
    {
        // game_music.wav carries one byte more than its frame count needs.
        byte[] bytes = [.. IlsfWriter.WritePcm([1, 2, 3]), 0xAB];
        IlsfFile sound = IlsfFile.Read(bytes);

        Assert.Equal(3, sound.FrameCount);
        Assert.Equal(6, sound.Payload.Length);
    }

    [Theory]
    [InlineData(4, 16, 1, 22050, "storage format")]
    [InlineData(1, 24, 1, 22050, "8- or 16-bit")]
    [InlineData(1, 16, 3, 22050, "channels")]
    [InlineData(1, 16, 1, 0, "sample rate")]
    [InlineData(3, 8, 1, 22050, "ADPCM")]
    public void AMalformedHeaderIsRejected(int format, int bits, int channels, int rate, string expected)
    {
        byte[] bytes = IlsfWriter.WriteRaw(format, bits, channels, rate, frameCount: 0);
        IlsfFormatException error = Assert.Throws<IlsfFormatException>(() => IlsfFile.Read(bytes));
        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASoundThatDoesNotStartWithTheMagicIsRejected()
    {
        byte[] bytes = IlsfWriter.WritePcm([1]);
        bytes[1] = (byte)'X';

        Assert.False(IlsfFile.IsIlsf(bytes));
        Assert.Contains("Expected magic", Assert.Throws<IlsfFormatException>(() => IlsfFile.Read(bytes)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ATruncatedPayloadIsRejected()
    {
        byte[] bytes = IlsfWriter.WriteTruncated(frameCount: 100, payloadBytes: 8);
        Assert.Contains("payload byte", Assert.Throws<IlsfFormatException>(() => IlsfFile.Read(bytes)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TooFewBytesForAHeaderIsRejected()
    {
        Assert.False(IlsfFile.IsIlsf(new byte[8]));
        Assert.Throws<IlsfFormatException>(() => IlsfFile.Read(new byte[8]));
    }

    [Fact]
    public void TheStreamReaderProducesTheSameSamplesAsTheResidentPath()
    {
        short[] source = MakeTone(frames: 300, channels: 2);
        byte[] bytes = IlsfWriter.WriteAdpcm(source, channels: 2, resident: false);

        ReadOnlyMemory<byte> resident = IlsfFile.Read(bytes).DecodePcm();

        using MemoryStream stream = new(bytes);
        using IlsfStreamReader reader = IlsfStreamReader.Open(stream);

        Assert.Equal(IlsfFormat.StreamedAdpcm, reader.Format);
        Assert.Equal(300, reader.FrameCount);

        short[] streamed = new short[source.Length];
        int written = 0;
        short[] buffer = new short[2 * 17];
        int frames;
        while ((frames = reader.ReadFrames(buffer)) > 0)
        {
            buffer.AsSpan(0, frames * 2).CopyTo(streamed.AsSpan(written));
            written += frames * 2;
        }

        Assert.Equal(source.Length, written);
        Assert.True(reader.EndOfSound);

        for (int i = 0; i < streamed.Length; i++)
        {
            Assert.Equal(BinaryPrimitives.ReadInt16LittleEndian(resident.Span[(i * sizeof(short))..]), streamed[i]);
        }
    }

    [Fact]
    public void RestartingRewindsAndResetsTheDifferentialState()
    {
        short[] source = MakeTone(frames: 64, channels: 1);
        using MemoryStream stream = new(IlsfWriter.WriteAdpcm(source));
        using IlsfStreamReader reader = IlsfStreamReader.Open(stream);

        short[] first = new short[64];
        Assert.Equal(64, reader.ReadFrames(first));
        Assert.Equal(0, reader.ReadFrames(first));

        Assert.True(reader.CanRestart);
        reader.Restart();

        short[] second = new short[64];
        Assert.Equal(64, reader.ReadFrames(second));
        Assert.Equal(first, second);
    }

    [Fact]
    public void TheStreamReaderReadsAPcmSoundToo()
    {
        short[] source = [5, -5, 100, -100];
        using MemoryStream stream = new(IlsfWriter.WritePcm(source, resident: false));
        using IlsfStreamReader reader = IlsfStreamReader.Open(stream);

        short[] read = new short[4];
        Assert.Equal(4, reader.ReadFrames(read));
        Assert.Equal(source, read);
    }

    private static short[] MakeTone(int frames, int channels, int period = 32)
    {
        short[] samples = new short[frames * channels];
        for (int frame = 0; frame < frames; frame++)
        {
            for (int channel = 0; channel < channels; channel++)
            {
                double phase = (2 * Math.PI * frame / period) + (channel * 0.7);
                samples[(frame * channels) + channel] = (short)(Math.Sin(phase) * 12000);
            }
        }

        return samples;
    }
}
