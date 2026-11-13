namespace OpenIGI.Rendering.OpenGL;

/// <summary>The machine's OpenGL is short of what this backend needs, said in words a player can act on.</summary>
/// <remarks>
/// Thrown before any resource exists, so the host can say why and give up cleanly. OpenGL is the last API the
/// host falls back to, so this one reaches the player: the message names the version found and the one needed.
/// </remarks>
public sealed class OpenGLUnavailableException : Exception
{
    /// <summary>Initialises a new instance of the <see cref="OpenGLUnavailableException"/> class.</summary>
    public OpenGLUnavailableException()
    {
    }

    /// <summary>Initialises a new instance of the <see cref="OpenGLUnavailableException"/> class.</summary>
    /// <param name="message">What is missing.</param>
    public OpenGLUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>Initialises a new instance of the <see cref="OpenGLUnavailableException"/> class.</summary>
    /// <param name="message">What is missing.</param>
    /// <param name="innerException">What failed underneath.</param>
    public OpenGLUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
