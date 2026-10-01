namespace OpenIGI.Audio;

/// <summary>A source of PCM the device pulls from a buffer at a time.</summary>
/// <remarks>
/// <para>
/// This is the seam the original's streamed storage formats need. A sound stored as
/// <c>StreamedPcm</c> or <c>StreamedAdpcm</c> is never resident — the loader keeps only its file name —
/// because the long ones are the music track and the ambience beds, and a retail track is about
/// thirteen megabytes expanded.
/// </para>
/// <para>
/// Implementations are pulled from the device's own thread rather than the game thread, so an
/// implementation must not touch game state. It is read by exactly one thread at a time.
/// </para>
/// </remarks>
public interface ISampleSource : IDisposable
{
    /// <summary>Gets the shape of the PCM this source produces.</summary>
    AudioFormat Format { get; }

    /// <summary>Gets a value indicating whether the source can be rewound to its start.</summary>
    bool CanRestart { get; }

    /// <summary>Fills <paramref name="destination"/> with the next samples.</summary>
    /// <param name="destination">Receives channel-interleaved 16-bit samples.</param>
    /// <returns>The number of samples written, or zero at the end of the source.</returns>
    int Read(Span<short> destination);

    /// <summary>Rewinds to the start so the source can loop.</summary>
    /// <exception cref="NotSupportedException"><see cref="CanRestart"/> is <see langword="false"/>.</exception>
    void Restart();
}
