namespace OpenIGI.Audio;

/// <summary>Identifies a block of PCM the device holds ready to play.</summary>
/// <param name="Value">The opaque identifier; zero is the null handle.</param>
/// <remarks>Buffers are shared: one buffer can back any number of simultaneous voices.</remarks>
public readonly record struct AudioBufferHandle(int Value)
{
    /// <summary>Gets the handle that refers to no buffer.</summary>
    public static AudioBufferHandle None => default;

    /// <summary>Gets a value indicating whether the handle refers to a buffer.</summary>
    public bool IsValidHandle => Value != 0;
}
