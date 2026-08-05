using OpenIGI.Audio;
using Xunit;

namespace OpenIGI.Audio.Tests;

/// <summary>
/// Covers the vanilla gate: what <c>-vanilla</c> actually does to the mix.
/// </summary>
/// <remarks>
/// These write the process-wide console variables, so the fixture restores them. They are worth having
/// anyway: "vanilla restores the original" is a promise, and a promise nothing asserts is a comment.
/// </remarks>
public sealed class AudioProfileTests : IDisposable
{
    private readonly bool _spatial = AudioCVars.Spatial.Value;
    private readonly int _voices = AudioCVars.Voices.Value;
    private readonly int _rate = AudioCVars.SampleRate.Value;
    private readonly AudioProfile _preset = AudioCVars.Preset.Value;

    public void Dispose()
    {
        AudioCVars.Spatial.Value = _spatial;
        AudioCVars.Voices.Value = _voices;
        AudioCVars.SampleRate.Value = _rate;
        AudioCVars.Preset.Value = _preset;
    }

    [Fact]
    public void TheVanillaProfileIsTheOriginalsThreeNumbers()
    {
        AudioCVars.Apply(AudioProfile.Vanilla);

        // 32 voices and a 22050 Hz primary buffer are both literals in Snd_Init at 0x4952F0, and the
        // original's sample buffers carry no CTRL3D at all -- so no positioning either.
        Assert.Equal(32, AudioCVars.Voices.Value);
        Assert.Equal(22050, AudioCVars.SampleRate.Value);
        Assert.False(AudioCVars.Spatial.Value);
    }

    [Fact]
    public void TheEnhancedProfileTurnsThemBackUp()
    {
        AudioCVars.Apply(AudioProfile.Vanilla);
        AudioCVars.Apply(AudioProfile.Enhanced);

        Assert.True(AudioCVars.Voices.Value > 32);
        Assert.True(AudioCVars.SampleRate.Value > 22050);
        Assert.True(AudioCVars.Spatial.Value);
    }

    [Fact]
    public void SettingThePresetVariableAppliesIt()
    {
        // This is the path -vanilla actually takes: the flag emits "snd_preset vanilla" as a start-up
        // statement, so assigning the variable has to be what does the work.
        AudioCVars.Apply(AudioProfile.Enhanced);
        AudioCVars.Preset.Value = AudioProfile.Vanilla;

        Assert.Equal(32, AudioCVars.Voices.Value);
        Assert.False(AudioCVars.Spatial.Value);
    }

    [Fact]
    public void TheCustomProfileChangesNothing()
    {
        AudioCVars.Apply(AudioProfile.Vanilla);
        AudioCVars.Apply(AudioProfile.Custom);

        Assert.Equal(32, AudioCVars.Voices.Value);
    }

    [Fact]
    public void TheVanillaProfileReachesTheDeviceOptions()
    {
        AudioCVars.Apply(AudioProfile.Vanilla);
        AudioDeviceOptions options = AudioDeviceOptions.FromCVars();

        // The options are what the backend is actually built from, so this is the end of the chain.
        Assert.Equal(AudioDeviceOptions.Vanilla.VoiceCount, options.VoiceCount);
        Assert.Equal(AudioDeviceOptions.Vanilla.SampleRate, options.SampleRate);
        Assert.Equal(AudioDeviceOptions.Vanilla.Spatial, options.Spatial);
    }
}
