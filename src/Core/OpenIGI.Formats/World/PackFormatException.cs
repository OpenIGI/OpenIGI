namespace OpenIGI.Formats.World;

/// <summary>The exception thrown when pack or model bytes do not match the documented layout.</summary>
public sealed class PackFormatException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="PackFormatException"/> class.</summary>
    public PackFormatException()
        : base("The pack data is malformed.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PackFormatException"/> class.</summary>
    /// <param name="message">The message that describes the error.</param>
    public PackFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PackFormatException"/> class.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public PackFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
