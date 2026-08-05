namespace OpenIGI.Formats.Sound;

/// <summary>Thrown when a byte range does not form a valid ILSF sound.</summary>
/// <remarks>
/// The original loader <c>0x495F70</c> printed <c>"SOUND - File %s not valid format!"</c> and carried on
/// without the sound. The format layer raises a typed exception instead and lets the caller choose:
/// tools report it per file, and the sound bank logs it and skips the entry, as the original did.
/// </remarks>
public sealed class IlsfFormatException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="IlsfFormatException"/> class.</summary>
    public IlsfFormatException()
        : base("Invalid ILSF sound.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="IlsfFormatException"/> class.</summary>
    /// <param name="message">The message describing the failure.</param>
    public IlsfFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="IlsfFormatException"/> class.</summary>
    /// <param name="message">The message describing the failure.</param>
    /// <param name="innerException">The underlying cause.</param>
    public IlsfFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
