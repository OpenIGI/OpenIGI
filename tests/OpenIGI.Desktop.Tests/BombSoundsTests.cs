using OpenIGI.Audio;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.Host.Audio;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>Tests for <see cref="BombSounds"/> and <see cref="BombSoundDesign"/>: the bomb's layered sounds are made, and fit.</summary>
public sealed class BombSoundsTests
{
    private static readonly AudioFormat Mono16 = new(22050, 1);

    /// <summary>The kit's sounds are there, and the two layered out of its explosions with them, each once.</summary>
    [Fact]
    public void TheLayeredSoundsAreMadeFromTheKit()
    {
        List<string> names = [.. BombSounds.All(NullLogger.Instance).Select(static s => s.Name)];

        Assert.Contains(BombSounds.Explosion, names);
        Assert.Contains(BombSounds.DistantExplosion, names);
        Assert.Contains(BombSounds.FarThump, names);
        Assert.Contains(BombSounds.FrontRush, names);
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>The rush is quiet until it swells into the strike at its lead, and never clips.</summary>
    [Fact]
    public void TheRushSwellsIntoItsStrikeAtTheLead()
    {
        byte[] noise = Noise(Mono16.SampleRate * 2);
        byte[] rush = BombSoundDesign.FrontRush(Mono16, noise, noise, (float)BombWave.CueLeadSeconds);

        int lead = (int)(BombWave.CueLeadSeconds * Mono16.SampleRate);
        Assert.Equal((lead + (int)(1.5f * Mono16.SampleRate)) * 2, rush.Length);
        float early = Peak(rush, 0, lead / 4);
        float strike = Peak(rush, lead, lead + (Mono16.SampleRate / 10));
        Assert.True(early < strike * 0.3f, $"early {early}, strike {strike}");
        Assert.True(Peak(rush, 0, rush.Length / 2) <= 0.93f);
    }

    /// <summary>The far thump is longer than what it was made from (slowed, so lower), and never clips.</summary>
    [Fact]
    public void TheThumpIsSlowedAndLimited()
    {
        byte[] noise = Noise(Mono16.SampleRate);
        byte[] thump = BombSoundDesign.FarThump(Mono16, noise);
        Assert.True(thump.Length > noise.Length * 1.3);
        Assert.True(Peak(thump, 0, thump.Length / 2) <= 0.93f);
    }

    /// <summary>A format the design does not read is left out rather than mangled.</summary>
    [Fact]
    public void AnotherFormatIsLeftOut()
    {
        Assert.Empty(BombSoundDesign.FarThump(new AudioFormat(22050, 1, 8), new byte[100]));
    }

    private static byte[] Noise(int frames)
    {
        var random = new Random(5);
        var pcm = new byte[frames * 2];
        for (int i = 0; i < frames; i++)
        {
            short value = (short)random.Next(-20000, 20000);
            pcm[i * 2] = (byte)(value & 0xFF);
            pcm[(i * 2) + 1] = (byte)((value >> 8) & 0xFF);
        }

        return pcm;
    }

    private static float Peak(byte[] pcm, int fromFrame, int toFrame)
    {
        float peak = 0f;
        for (int f = fromFrame; f < toFrame && (f * 2) + 1 < pcm.Length; f++)
        {
            peak = MathF.Max(peak, MathF.Abs((short)(pcm[f * 2] | (pcm[(f * 2) + 1] << 8)) / 32768f));
        }

        return peak;
    }
}
