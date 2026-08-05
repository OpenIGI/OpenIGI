using OpenIGI.Formats.Sound;

namespace OpenIGI.Engine.Audio;

/// <summary>One sound the bank knows about.</summary>
/// <remarks>
/// A resident sound carries its bytes; a streamed one carries only the path it was found at, exactly as
/// the original does — the loader at <c>0x495F70</c> stores a file name at record <c>+40</c> for the
/// streamed storage codes and never allocates for them. That is what keeps the music track and the
/// ambience beds out of memory until something asks to hear them.
/// </remarks>
public sealed class SoundEntry
{
    /// <summary>Initializes a new instance of the <see cref="SoundEntry"/> class.</summary>
    /// <param name="name">The lookup key, from <see cref="SoundName.ToKey"/>.</param>
    /// <param name="origin">The virtual file system path the sound was found at.</param>
    /// <param name="header">The sound's parsed header.</param>
    /// <param name="data">The whole ILSF bytes for a resident sound, or empty for a streamed one.</param>
    public SoundEntry(string name, string origin, IlsfHeader header, ReadOnlyMemory<byte> data)
    {
        Name = name;
        Origin = origin;
        Header = header;
        Data = data;
    }

    /// <summary>Gets the lookup key, upper-cased.</summary>
    public string Name { get; }

    /// <summary>Gets the virtual file system path the sound was found at.</summary>
    public string Origin { get; }

    /// <summary>Gets the sound's header.</summary>
    public IlsfHeader Header { get; }

    /// <summary>Gets the whole ILSF bytes for a resident sound; empty for a streamed one.</summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>Gets a value indicating whether the sound is played from its file rather than from memory.</summary>
    public bool IsStreamed => Header.IsStreamed;

    /// <summary>Gets how long the sound plays for.</summary>
    public TimeSpan Duration => Header.Duration;

    /// <inheritdoc/>
    public override string ToString() => $"{Name} ({Header})";
}
