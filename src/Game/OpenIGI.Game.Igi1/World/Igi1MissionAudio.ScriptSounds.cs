using OpenIGI.Audio;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi1.World;

/// <content>The sounds soldiers' scripts and animations ask for.</content>
public sealed partial class Igi1MissionAudio
{
    private const int ScriptSoundPriority = 200;

    private readonly List<(string Name, AudioVoiceHandle Voice, Func<Vector3d> Follow)> _followingScriptSounds = [];

    /// <summary>Plays a sound a soldier's script or animation asks for (<c>AIAction_PlaySound</c>, <c>0x51B4F0</c>).</summary>
    /// <param name="name">The definition.</param>
    /// <param name="position">Where, in world units.</param>
    /// <param name="follow">
    /// Where the soldier is now, for a sound attached to them, which the original plays on its owner and so moves with
    /// it; <see langword="null"/> for one that stays where it was played.
    /// </param>
    public void PlayScriptSound(string name, Vector3d position, Func<Vector3d>? follow)
    {
        if (host.SoundOutput is not { } sounds)
        {
            return;
        }

        AudioVoiceHandle voice = sounds.PlayDefinition(name, position, priority: ScriptSoundPriority);
        if (follow is not null && voice != AudioVoiceHandle.None)
        {
            _followingScriptSounds.Add((name, voice, follow));
        }
    }

    /// <summary>Keeps each attached script sound on its soldier until it ends.</summary>
    public void UpdateScriptSoundPositions()
    {
        if (host.SoundOutput is not { } sounds)
        {
            _followingScriptSounds.Clear();
            return;
        }

        for (int i = _followingScriptSounds.Count - 1; i >= 0; i--)
        {
            (string name, AudioVoiceHandle voice, Func<Vector3d> follow) = _followingScriptSounds[i];
            if (!sounds.IsPlaying(voice))
            {
                _followingScriptSounds.RemoveAt(i);
                continue;
            }

            sounds.SetDefinitionVoicePosition(name, voice, follow(), priority: ScriptSoundPriority);
        }
    }
}
