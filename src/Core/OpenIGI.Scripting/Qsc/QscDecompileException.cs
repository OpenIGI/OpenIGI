namespace OpenIGI.Scripting.Qsc;

/// <summary>Thrown when a QVM program holds code the QSC decompiler cannot turn back into source.</summary>
public sealed class QscDecompileException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="QscDecompileException"/> class.</summary>
    public QscDecompileException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="QscDecompileException"/> class.</summary>
    /// <param name="message">The message.</param>
    public QscDecompileException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="QscDecompileException"/> class.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public QscDecompileException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="QscDecompileException"/> class.</summary>
    /// <param name="offset">The code offset the problem was found at.</param>
    /// <param name="problem">What was found there.</param>
    public QscDecompileException(int offset, string problem)
        : base($"Code offset {offset}: {problem}.")
    {
        Offset = offset;
    }

    /// <summary>Gets the code offset the problem was found at, or -1 when none was given.</summary>
    public int Offset { get; } = -1;
}
