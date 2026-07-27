namespace OpenIGI.Scripting.Expressions;

/// <summary>
/// Thrown when an expression cannot be tokenized, parsed or evaluated.
/// </summary>
/// <remarks>
/// The original reports these to its script console and aborts the script
/// (<c>"Script error: Aborting."</c>) rather than the process; carrying the line number here keeps the
/// diagnostic as useful as the original's, which always prints it.
/// </remarks>
public sealed class ScriptExpressionException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="ScriptExpressionException"/> class.</summary>
    public ScriptExpressionException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ScriptExpressionException"/> class with a message.</summary>
    /// <param name="message">The message that describes the failure.</param>
    public ScriptExpressionException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ScriptExpressionException"/> class with a message and an inner exception.</summary>
    /// <param name="message">The message that describes the failure.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public ScriptExpressionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ScriptExpressionException"/> class for a source line.</summary>
    /// <param name="message">The message that describes the failure.</param>
    /// <param name="line">The one-based source line.</param>
    /// <param name="near">The token text the failure occurred at.</param>
    public ScriptExpressionException(string message, int line, string near)
        : base($"{message} in line {line}: '{near}'")
    {
        Line = line;
        Near = near;
    }

    /// <summary>Gets the one-based source line, or zero when unknown.</summary>
    public int Line { get; }

    /// <summary>Gets the token text the failure occurred at.</summary>
    public string Near { get; } = string.Empty;
}
