using OpenIGI.Formats.Sound;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>Sounds written from PCM for the editor's sound import.</summary>
public sealed class IlsfWriterTests
{
    /// <summary>A written sound reads back with its shape, its residency and its very samples.</summary>
    [Theory]
    [InlineData(1, 16, 22050, false)]
    [InlineData(2, 16, 44100, true)]
    [InlineData(1, 8, 11025, false)]
    public void A_written_sound_reads_back(int channels, int bits, int rate, bool streamed)
    {
        byte[] pcm = [.. Enumerable.Range(0, channels * bits / 8 * 300).Select(static i => (byte)(i * 7))];
        IlsfFile sound = IlsfFile.Read(IlsfWriter.WritePcm(pcm, rate, channels, bits, streamed));

        Assert.Equal((channels, bits, rate, 300), (sound.Channels, sound.BitsPerSample, sound.SampleRate, sound.FrameCount));
        Assert.Equal(streamed, sound.IsStreamed);
        Assert.False(sound.IsAdpcm);
        Assert.Equal(pcm, sound.DecodePcm().ToArray());
    }

    /// <summary>Samples that stop part way through a frame are refused rather than cut.</summary>
    [Fact]
    public void Part_frames_are_refused() =>
        Assert.Throws<ArgumentException>(() => IlsfWriter.WritePcm(new byte[3], 22050, 2, 16, streamed: false));
}
