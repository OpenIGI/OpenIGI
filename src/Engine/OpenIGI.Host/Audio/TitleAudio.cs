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
/// <param name="borrowFrom">The host's own sounds, to play a name the title's banks never define; <see langword="null"/> for none.</param>
internal sealed class TitleAudio(SoundSystem sounds, SoundBank bank, Func<SoundSystem?>? borrowFrom = null) : ITitleAudio
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
        if (voice != AudioVoiceHandle.None)
        {
            return new Voice(sounds, definition, voice, looping) { Position = positionInUnits };
        }

        // A name the title's own banks never define, borrowed from the host's where it does: I.G.I. 2's clips name their
        // reload clicks after Project I.G.I.'s and ship next to none of them.
        if (borrowFrom?.Invoke() is { } host && !ReferenceEquals(host, sounds))
        {
            AudioVoiceHandle borrowed = host.PlayDefinition(definition, positionInUnits, looping: looping);
            if (borrowed != AudioVoiceHandle.None)
            {
                return new Voice(host, definition, borrowed, looping) { Position = positionInUnits };
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public bool CanBorrow(string definition) =>
        borrowFrom?.Invoke() is { Definitions: { } theirs } host && !ReferenceEquals(host, sounds) && theirs.TryResolve(definition, out _);

    /// <inheritdoc/>
    public double SampleSeconds(string bankKey)
    {
        return bank.TryGet(bankKey, out SoundEntry entry) ? entry.SampleSeconds : 0.0;
    }

    /// <summary>A voice the title holds.</summary>
    private sealed class Voice(SoundSystem sounds, string definition, AudioVoiceHandle voice, bool? looping) : ITitleVoice
    {
        private float _gain = 1f;

        public Vector3d Position { get; set; }

        public bool IsPlaying => sounds.IsPlaying(voice);

        public void MoveTo(Vector3d positionInUnits)
        {
            Position = positionInUnits;
            sounds.SetDefinitionVoicePosition(definition, voice, positionInUnits, _gain, looping: looping);
        }

        public void SetGain(float gain)
        {
            _gain = gain;
            sounds.SetDefinitionVoicePosition(definition, voice, Position, _gain, looping: looping);
        }

        public void Silence() => sounds.Stop(voice);
    }
}
