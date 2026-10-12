using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Audio;

namespace OpenIGI.Game.Player;

/// <summary>The host's mixer, as a title's own player sounds through it.</summary>
/// <remarks>
/// <para>
/// A title whose sounds are defined by its own natives brings its own table and the directories its banks are in; the
/// host mounts those and resolves every name through that table instead of its own. What is played is still the host's
/// voice: the title only says which definition, and where.
/// </para>
/// <para>
/// Nothing the title decides may depend on what comes back. Whether a sound <i>exists</i> is the title's own
/// table's answer, so a game with no audio device runs exactly as one with.
/// </para>
/// </remarks>
public interface ITitleAudio
{
    /// <summary>Takes the title's definitions in place of the host's, and mounts the banks they name sounds from.</summary>
    /// <param name="definitions">The title's table.</param>
    /// <param name="bankDirectories">The directories whose sound archives and files the definitions play, in mount order.</param>
    void UseDefinitions(ISoundDefinitionLookup definitions, IReadOnlyList<string> bankDirectories);

    /// <summary>Plays a definition.</summary>
    /// <param name="definition">The definition's name.</param>
    /// <param name="positionInUnits">Where it sounds, in world units.</param>
    /// <param name="looping">Whether it loops, or <see langword="null"/> for as the definition says.</param>
    /// <returns>The voice, or <see langword="null"/> when nothing could be played.</returns>
    ITitleVoice? Play(string definition, Vector3d positionInUnits, bool? looping = null);

    /// <summary>
    /// Gets how long a bank's sound lasts, in seconds, from its data rather than from any device: its frames over its rate,
    /// as <c>Sound_GetSampleDuration</c> (<c>igi2ed 0x55E0E0</c>) reads them from the sample's header.
    /// </summary>
    /// <param name="bankKey">The sound's name in the bank.</param>
    /// <returns>Its length, or nought for a sound the bank does not hold.</returns>
    double SampleSeconds(string bankKey) => 0.0;
}

/// <summary>A sound the host is playing for a title.</summary>
public interface ITitleVoice
{
    /// <summary>Gets a value indicating whether it still sounds.</summary>
    bool IsPlaying { get; }

    /// <summary>Moves it.</summary>
    /// <param name="positionInUnits">Where it now sounds, in world units.</param>
    void MoveTo(Vector3d positionInUnits);

    /// <summary>Stops it sounding.</summary>
    void Silence();

    /// <summary>Sets how loud it sounds, as a share of its definition's volume.</summary>
    /// <param name="gain">The share, nought to one.</param>
    void SetGain(float gain)
    {
    }
}
