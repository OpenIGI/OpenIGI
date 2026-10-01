namespace OpenIGI.Engine.Configuration;

/// <summary>Thrown when a <c>filesys.cfg</c> file cannot be parsed.</summary>
public sealed class FileSystemConfigException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="FileSystemConfigException"/> class.</summary>
    public FileSystemConfigException()
        : base("Invalid file system configuration.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="FileSystemConfigException"/> class.</summary>
    /// <param name="message">The message describing the failure.</param>
    public FileSystemConfigException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="FileSystemConfigException"/> class.</summary>
    /// <param name="message">The message describing the failure.</param>
    /// <param name="innerException">The underlying cause.</param>
    public FileSystemConfigException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
