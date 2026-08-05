using OpenIGI.Audio;
using OpenIGI.Audio.OpenAL;
using OpenIGI.Core.Configuration;

namespace OpenIGI.Desktop;

/// <summary>The audio half of the composition root: opening the device and keeping the buses levelled.</summary>
internal sealed partial class DesktopGame
{
    /// <summary>Opens the audio backend and binds the volume variables to it.</summary>
    /// <remarks>
    /// <para>
    /// Called once, after the console has read <c>settings.cfg</c> and the command line's
    /// <c>+statements</c>, because the voice count and the output rate are fixed when the device opens.
    /// The volumes are not — those are pushed on change, so dragging a slider is heard immediately.
    /// </para>
    /// <para>
    /// A backend that cannot open is not an error. <see cref="OpenAlAudioDevice"/> degrades to silence
    /// on its own, so there is nothing here to handle: the game runs either way.
    /// </para>
    /// </remarks>
    private void OpenAudioDevice()
    {
        _audio.Dispose();

        _audio = AudioCVars.Enabled.Value
            ? new OpenAlAudioDevice(AudioDeviceOptions.FromCVars(), _logger)
            : new NullAudioDevice();

        ApplyCategoryGains();

        foreach (ICVar variable in (ICVar[])
                 [
                     AudioCVars.MasterVolume,
                     AudioCVars.EffectsVolume,
                     AudioCVars.MusicVolume,
                     AudioCVars.SpeechVolume,
                     AudioCVars.AmbienceVolume,
                 ])
        {
            variable.Changed += (_, _) => ApplyCategoryGains();
        }
    }

    /// <summary>Pushes the volume variables onto the device's buses.</summary>
    /// <remarks>
    /// The master gain is folded into each bus rather than applied separately, because the device model
    /// has one gain per bus and no master of its own — the original has no master either, only the three
    /// per-profile volumes.
    /// </remarks>
    private void ApplyCategoryGains()
    {
        float master = AudioCVars.MasterVolume.Value;

        _audio.SetCategoryGain(SoundCategory.Effects, master * AudioCVars.EffectsVolume.Value);
        _audio.SetCategoryGain(SoundCategory.Music, master * AudioCVars.MusicVolume.Value);
        _audio.SetCategoryGain(SoundCategory.Speech, master * AudioCVars.SpeechVolume.Value);
        _audio.SetCategoryGain(SoundCategory.Ambience, master * AudioCVars.AmbienceVolume.Value);
    }
}
