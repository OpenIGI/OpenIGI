namespace OpenIGI.Formats.Sound;

/// <summary>Thrown when a byte range does not form an MPEG audio stream the decoder can read.</summary>
/// <remarks>
/// The counterpart of <see cref="IlsfFormatException"/> for the one retail sound format that is not
/// ILSF. It exists so callers can treat a bad sound the same way whichever of the two it claimed to be:
/// the sound bank logs it and skips the entry, exactly as the original's loader does for a file it
/// cannot make sense of.
/// </remarks>
public sealed class MpegAudioFormatException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="MpegAudioFormatException"/> class.</summary>
    public MpegAudioFormatException()
        : base("Invalid MPEG audio stream.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="MpegAudioFormatException"/> class.</summary>
    /// <param name="message">The message describing the failure.</param>
    public MpegAudioFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="MpegAudioFormatException"/> class.</summary>
    /// <param name="message">The message describing the failure.</param>
    /// <param name="innerException">The underlying cause.</param>
    public MpegAudioFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
