using System.Buffers.Binary;
using OpenIGI.Engine.Audio;
using Xunit;

namespace OpenIGI.Engine.Tests;

/// <summary>The little WAV reader behind the remaster's own added sounds.</summary>
public sealed class WavPcmTests
{
    [Fact]
    public void ReadsAPlainPcmWave()
    {
        byte[] pcm = [1, 2, 3, 4, 5, 6];
        byte[] file = Build(sampleRate: 44100, channels: 1, bitsPerSample: 16, pcm);

        Assert.True(WavPcm.TryRead(file, out var format, out byte[] read));
        Assert.Equal(44100, format.SampleRate);
        Assert.Equal(1, format.Channels);
        Assert.Equal(16, format.BitsPerSample);
        Assert.Equal(pcm, read);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(44)]
    public void RefusesWhatIsNotAWave(int length)
    {
        Assert.False(WavPcm.TryRead(new byte[length], out _, out _));
    }

    [Fact]
    public void RefusesACompressedFormat()
    {
        // Format tag 85 is MP3-in-WAV; the reader takes integer PCM only.
        byte[] file = Build(44100, 2, 16, [1, 2, 3, 4], formatTag: 85);
        Assert.False(WavPcm.TryRead(file, out _, out _));
    }

    private static byte[] Build(int sampleRate, int channels, int bitsPerSample, byte[] pcm, int formatTag = 1)
    {
        byte[] file = new byte[44 + pcm.Length];
        "RIFF"u8.CopyTo(file);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(4), 36 + pcm.Length);
        "WAVE"u8.CopyTo(file.AsSpan(8));
        "fmt "u8.CopyTo(file.AsSpan(12));
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(16), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(20), (ushort)formatTag);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(22), (ushort)channels);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(24), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(28), sampleRate * channels * bitsPerSample / 8);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(32), (ushort)(channels * bitsPerSample / 8));
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(34), (ushort)bitsPerSample);
        "data"u8.CopyTo(file.AsSpan(36));
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(40), pcm.Length);
        pcm.CopyTo(file.AsSpan(44));
        return file;
    }
}
