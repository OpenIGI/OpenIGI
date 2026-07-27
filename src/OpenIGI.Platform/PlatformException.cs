namespace OpenIGI.Platform;

/// <summary>Thrown when a platform host cannot create or drive a window.</summary>
public sealed class PlatformException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="PlatformException"/> class.</summary>
    public PlatformException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PlatformException"/> class with a message.</summary>
    /// <param name="message">The message that describes the failure.</param>
    public PlatformException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PlatformException"/> class with a message and an inner exception.</summary>
    /// <param name="message">The message that describes the failure.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public PlatformException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
