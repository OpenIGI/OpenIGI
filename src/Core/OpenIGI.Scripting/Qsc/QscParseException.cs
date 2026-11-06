namespace OpenIGI.Scripting.Qsc;

/// <summary>Thrown when QSC source does not parse.</summary>
public sealed class QscParseException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="QscParseException"/> class.</summary>
    public QscParseException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="QscParseException"/> class.</summary>
    /// <param name="message">The message.</param>
    public QscParseException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="QscParseException"/> class.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public QscParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="QscParseException"/> class.</summary>
    /// <param name="position">Where in the source the problem is.</param>
    /// <param name="problem">What the problem is.</param>
    public QscParseException(QscSourcePosition position, string problem)
        : base($"Line {position.Line}, column {position.Column}: {problem}.")
    {
        Position = position;
        Problem = problem;
    }

    /// <summary>Gets where in the source the problem is.</summary>
    public QscSourcePosition Position { get; }

    /// <summary>Gets the problem without its position, for a diagnostics list that shows the position itself.</summary>
    public string Problem { get; } = string.Empty;
}
