using OpenIGI.Game.World;
using OpenIGI.Host.Audio;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// Tests for <see cref="LampSounds"/>: every lamp sound is there, is a sound, and fits what it is the sound of.
/// </summary>
public sealed class LampSoundsTests
{
    /// <summary>Every name the player plays by has a sound behind it, and no name is registered twice.</summary>
    [Fact]
    public void EveryLampSoundIsMade()
    {
        string[] expected =
        [
            LampSounds.SwitchName, LampSounds.StarterName, LampSounds.StarterAltName, LampSounds.TubeHumName,
            LampSounds.IgnitorName, LampSounds.DischargeHumName, LampSounds.FilamentName, LampSounds.OffName,
            LampSounds.SputterName,
        ];

        string[] made = [.. LampSounds.All.Select(sound => sound.Name)];

        Assert.Equal(expected.Order(), made.Order());
        Assert.Equal(made.Length, made.Distinct().Count());
    }

    /// <summary>Each one is audible, whole sixteen-bit samples, and peaks under full scale.</summary>
    [Fact]
    public void EachSoundIsSoundAndNeverClips()
    {
        foreach ((string name, byte[] pcm) in LampSounds.All)
        {
            Assert.True(pcm.Length > 0 && pcm.Length % 2 == 0, $"{name} is not whole sixteen-bit samples.");

            int peak = 0;
            for (int i = 0; i < pcm.Length; i += 2)
            {
                peak = Math.Max(peak, Math.Abs((int)(short)(pcm[i] | (pcm[i + 1] << 8))));
            }

            Assert.InRange(peak, short.MaxValue / 4, (short.MaxValue * 6) / 10);

            // Ends at silence, so it is never cut off with a tick of its own.
            short last = (short)(pcm[^2] | (pcm[^1] << 8));
            Assert.InRange(Math.Abs((int)last), 0, 64);
        }
    }

    /// <summary>The ignitor's sound lasts as long as the light's ignitor burst, and the hums outlast a tick.</summary>
    [Fact]
    public void TheSoundsAreAsLongAsWhatTheyAccompany()
    {
        Dictionary<string, double> seconds = LampSounds.All.ToDictionary(
            sound => sound.Name,
            sound => sound.Pcm.Length / 2.0 / LampSounds.SampleRate);

        Assert.True(seconds[LampSounds.IgnitorName] >= LampSwitchboard.IgnitorSeconds);
        Assert.True(seconds[LampSounds.TubeHumName] > 1.0);
        Assert.True(seconds[LampSounds.DischargeHumName] > seconds[LampSounds.TubeHumName]);
        Assert.True(seconds[LampSounds.StarterName] < 0.2);
    }
}
