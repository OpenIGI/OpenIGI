namespace OpenIGI.Core.Content;

/// <summary>A mod could not be read or loaded.</summary>
public sealed class ModLoadException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="ModLoadException"/> class.</summary>
    public ModLoadException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ModLoadException"/> class.</summary>
    /// <param name="message">What went wrong.</param>
    public ModLoadException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ModLoadException"/> class.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The cause.</param>
    public ModLoadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
