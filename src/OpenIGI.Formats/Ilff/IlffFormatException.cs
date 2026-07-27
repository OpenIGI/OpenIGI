namespace OpenIGI.Formats.Ilff;

/// <summary>Thrown when a byte range does not form a valid ILFF container.</summary>
/// <remarks>
/// The original engine reported this as the fatal <c>"Error in resource file: '%s'"</c>. The format
/// layer raises a typed exception instead and lets the caller decide: tools report it per file, while
/// the engine's resource loader promotes it to a fatal engine error, as the original did.
/// </remarks>
public sealed class IlffFormatException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="IlffFormatException"/> class.</summary>
    public IlffFormatException()
        : base("Invalid ILFF container.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="IlffFormatException"/> class.</summary>
    /// <param name="message">The message describing the failure.</param>
    public IlffFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="IlffFormatException"/> class.</summary>
    /// <param name="message">The message describing the failure.</param>
    /// <param name="innerException">The underlying cause.</param>
    public IlffFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
