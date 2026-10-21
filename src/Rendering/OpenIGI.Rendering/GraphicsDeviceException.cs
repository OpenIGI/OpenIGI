namespace OpenIGI.Rendering;

/// <summary>
/// Thrown when a graphics backend cannot satisfy a request: a mode that will not apply, a texture that
/// will not allocate, a shader that will not compile.
/// </summary>
/// <remarks>
/// The original ends every one of these paths in a fatal message box followed by <c>while (1);</c>.
/// OpenIGI throws instead so the host can report the failure and shut down cleanly.
/// </remarks>
public sealed class GraphicsDeviceException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="GraphicsDeviceException"/> class.</summary>
    public GraphicsDeviceException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="GraphicsDeviceException"/> class with a message.</summary>
    /// <param name="message">The message that describes the failure.</param>
    public GraphicsDeviceException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="GraphicsDeviceException"/> class with a message and an inner exception.</summary>
    /// <param name="message">The message that describes the failure.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public GraphicsDeviceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
