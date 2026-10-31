namespace OpenIGI.Audio;

/// <summary>A microphone being listened to: mono 16-bit samples, as they come.</summary>
public interface IMicrophone : IDisposable
{
    /// <summary>Gets the rate the samples come at.</summary>
    int SampleRate { get; }

    /// <summary>Takes the samples heard since the last call, as many as fit.</summary>
    /// <param name="destination">Where to.</param>
    /// <returns>How many were taken.</returns>
    int Read(Span<short> destination);
}
