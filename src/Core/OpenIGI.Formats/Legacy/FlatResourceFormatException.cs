namespace OpenIGI.Formats.Legacy;

/// <summary>Thrown when a byte range does not form a valid <c>"RESOURCE"</c> flat archive.</summary>
public sealed class FlatResourceFormatException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="FlatResourceFormatException"/> class.</summary>
    public FlatResourceFormatException()
        : base("Invalid RESOURCE archive.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="FlatResourceFormatException"/> class.</summary>
    /// <param name="message">The message describing the failure.</param>
    public FlatResourceFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="FlatResourceFormatException"/> class.</summary>
    /// <param name="message">The message describing the failure.</param>
    /// <param name="innerException">The underlying cause.</param>
    public FlatResourceFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
