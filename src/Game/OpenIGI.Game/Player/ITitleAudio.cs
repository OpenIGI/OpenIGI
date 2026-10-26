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

    /// <summary>
    /// Whether a name the title's own definitions lack can still be heard, borrowed from another game's bank that defines it:
    /// I.G.I. 2's clips name Project I.G.I.'s reload clicks and ship two of them. A mixer with no other bank borrows nothing.
    /// </summary>
    /// <param name="definition">The definition's name.</param>
    /// <returns>Whether <see cref="Play"/> would sound it.</returns>
    bool CanBorrow(string definition) => false;

    /// <summary>
    /// Instantiates a definition as a whole set its owner drives -- a group's every member, a graph or a trigger, which
    /// <see cref="Play"/> refuses -- for an owner that hands its sounds parameters every tick.
    /// </summary>
    /// <param name="definition">The definition's name.</param>
    /// <returns>The set, or <see langword="null"/> when nothing could be played.</returns>
    ITitleSoundSet? PlaySet(string definition) => null;
}

/// <summary>A sound set its owner drives: its sounds follow the owner and read the owner's parameters, once a tick.</summary>
public interface ITitleSoundSet
{
    /// <summary>Advances the set a tick.</summary>
    /// <param name="positionInUnits">Where its owner is, in world units.</param>
    /// <param name="parameters">What the owner hands its sounds, in the owner's order.</param>
    void Tick(Vector3d positionInUnits, IReadOnlyList<float> parameters);

    /// <summary>Stops every sound of the set.</summary>
    void Silence();
}

/// <summary>
/// Opens audio for another game's content standing in a game: its own sound table and banks, read from its own files, mixed
/// through the same device as the game's own sounds.
/// </summary>
/// <remarks>
/// A game's sounds are its definition table over its banks; the host's sound system holds the running game's. Content that
/// brings another game's -- that game's soldiers in this one's level -- cannot take that table over, or every sound of the
/// running game would go, so it is given a mixer of its own over the one device and listener.
/// </remarks>
public interface ITitleAudioMixer
{
    /// <summary>Opens a game's sounds over its files.</summary>
    /// <param name="files">The game's own files, which its banks are read from.</param>
    /// <param name="definitions">The game's definition table.</param>
    /// <param name="bankDirectories">The directories whose sound archives and files the definitions play, in mount order.</param>
    /// <returns>The audio, or <see langword="null"/> where nothing is heard.</returns>
    ITitleAudio? Open(Engine.Vfs.VirtualFileSystem files, ISoundDefinitionLookup definitions, IReadOnlyList<string> bankDirectories);
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
