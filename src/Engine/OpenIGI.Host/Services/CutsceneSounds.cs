using OpenIGI.Audio;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.World;

namespace OpenIGI.Host.Services;

/// <summary>The voices a cutscene's sound tasks start, kept so a skip can silence every one of them.</summary>
/// <param name="audio">The audio output the voices play through.</param>
internal sealed class CutsceneSounds(AudioOutput audio) : IHostService
{
    // The looping or held voice each non-simple sound task is sounding.
    private readonly Dictionary<GameTask, AudioVoiceHandle> _voices = [];

    // The one-shots simple sound tasks started, which have no falling edge of their own.
    private readonly HashSet<AudioVoiceHandle> _oneShots = [];

    /// <inheritdoc/>
    public string Name => "Cutscene sounds";

    /// <summary>Starts or stops one sound task's voice, as the cutscene runtime reports it.</summary>
    /// <param name="sound">What the task did.</param>
    public void Set(CutsceneSoundEvent sound)
    {
        if (audio.Sounds is not { } sounds || string.IsNullOrWhiteSpace(sound.Name))
        {
            return;
        }

        // Simple ConditionalSounds have no falling-edge event, but their voices still belong to the cutscene while it
        // is being skipped. Retire completed handles so this set only represents one-shots that can still be stopped
        // by the next cancellation.
        _oneShots.RemoveWhere(voice => !sounds.IsPlaying(voice));

        if (!sound.Simple
            && _voices.Remove(sound.Task, out AudioVoiceHandle old)
            && old.IsValidHandle)
        {
            sounds.Stop(old);
        }

        if (sound.Playing)
        {
            AudioVoiceHandle voice = sounds.PlayDefinition(
                sound.Name,
                sound.Position,
                priority: 320,
                looping: sound.Simple ? false : null);
            if (!sound.Simple && voice.IsValidHandle)
            {
                _voices[sound.Task] = voice;
            }
            else if (sound.Simple && voice.IsValidHandle)
            {
                _oneShots.Add(voice);
            }
        }
    }

    /// <summary>Silences every voice the cutscene started.</summary>
    public void StopAll()
    {
        if (audio.Sounds is { } sounds)
        {
            foreach (AudioVoiceHandle voice in _voices.Values)
            {
                if (voice.IsValidHandle)
                {
                    sounds.Stop(voice);
                }
            }

            foreach (AudioVoiceHandle voice in _oneShots)
            {
                if (voice.IsValidHandle)
                {
                    sounds.Stop(voice);
                }
            }
        }

        _voices.Clear();
        _oneShots.Clear();
    }

    /// <inheritdoc/>
    /// <remarks>Nothing of its own to free: the voices are the device's.</remarks>
    public void Dispose()
    {
    }
}
