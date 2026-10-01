namespace OpenIGI.Formats.Textures;

/// <summary>Thrown when texture, sprite or font bytes cannot be parsed.</summary>
public sealed class TextureFormatException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="TextureFormatException"/> class.</summary>
    public TextureFormatException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="TextureFormatException"/> class with a message.</summary>
    /// <param name="message">The message that describes the failure.</param>
    public TextureFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="TextureFormatException"/> class with a message and an inner exception.</summary>
    /// <param name="message">The message that describes the failure.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public TextureFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
