namespace OpenIGI.Formats.Ai;

/// <summary>The exception thrown when a <c>graph&lt;n&gt;.dat</c> stream does not match the documented layout.</summary>
public sealed class GraphFormatException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="GraphFormatException"/> class.</summary>
    public GraphFormatException()
        : base("The graph data is malformed.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="GraphFormatException"/> class.</summary>
    /// <param name="message">The message that describes the error.</param>
    public GraphFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="GraphFormatException"/> class.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public GraphFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
