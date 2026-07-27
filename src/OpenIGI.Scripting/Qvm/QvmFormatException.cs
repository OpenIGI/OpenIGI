namespace OpenIGI.Scripting.Qvm;

/// <summary>
/// Thrown when a byte sequence is not a well-formed QVM program.
/// </summary>
/// <remarks>
/// Signals a malformed <em>file</em>. Faults raised while a well-formed program runs are reported as
/// <see cref="Runtime.QvmError"/> values instead, matching the original VM, which never throws.
/// </remarks>
public sealed class QvmFormatException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="QvmFormatException"/> class.</summary>
    public QvmFormatException()
        : base("The data is not a valid QVM program.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="QvmFormatException"/> class.</summary>
    /// <param name="message">A description of what is wrong with the data.</param>
    public QvmFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="QvmFormatException"/> class.</summary>
    /// <param name="message">A description of what is wrong with the data.</param>
    /// <param name="innerException">The underlying failure.</param>
    public QvmFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
