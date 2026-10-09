using OpenIGI.Audio;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Audio;
using OpenIGI.Game.Player;

namespace OpenIGI.Host.Audio;

/// <summary>The host's mixer as a title's own player sounds through it.</summary>
/// <remarks>
/// The title's table replaces the host's for the mission, and every directory it names that the host has not already
/// mounted is mounted into the same bank, so a name resolves through the title's definitions to the title's sounds.
/// </remarks>
/// <param name="sounds">The mixer.</param>
/// <param name="bank">The bank the mission's sounds are mounted into.</param>
internal sealed class TitleAudio(SoundSystem sounds, SoundBank bank) : ITitleAudio
{
    /// <inheritdoc/>
    public void UseDefinitions(ISoundDefinitionLookup definitions, IReadOnlyList<string> bankDirectories)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(bankDirectories);
        foreach (string directory in bankDirectories)
        {
            if (!bank.Mounted.Contains(directory, StringComparer.OrdinalIgnoreCase))
            {
                bank.MountDirectory(directory);
            }
        }

        sounds.Definitions = definitions;
    }

    /// <inheritdoc/>
    public ITitleVoice? Play(string definition, Vector3d positionInUnits, bool? looping = null)
    {
        AudioVoiceHandle voice = sounds.PlayDefinition(definition, positionInUnits, looping: looping);
        return voice == AudioVoiceHandle.None ? null : new Voice(sounds, definition, voice, looping);
    }

    /// <summary>A voice the title holds.</summary>
    private sealed class Voice(SoundSystem sounds, string definition, AudioVoiceHandle voice, bool? looping) : ITitleVoice
    {
        public bool IsPlaying => sounds.IsPlaying(voice);

        public void MoveTo(Vector3d positionInUnits) =>
            sounds.SetDefinitionVoicePosition(definition, voice, positionInUnits, looping: looping);

        public void Silence() => sounds.Stop(voice);
    }
}
