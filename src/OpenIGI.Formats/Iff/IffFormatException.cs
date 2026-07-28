namespace OpenIGI.Formats.Iff;

/// <summary>Thrown when a file is not a well-formed EA IFF-85 container.</summary>
public sealed class IffFormatException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="IffFormatException"/> class.</summary>
    public IffFormatException()
        : base("The data is not a well-formed IFF container.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="IffFormatException"/> class.</summary>
    /// <param name="message">The reason the container was rejected.</param>
    public IffFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="IffFormatException"/> class.</summary>
    /// <param name="message">The reason the container was rejected.</param>
    /// <param name="innerException">The underlying failure.</param>
    public IffFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
