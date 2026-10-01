using OpenIGI.Audio;
using OpenIGI.Formats.Sound;

namespace OpenIGI.Engine.Audio;

/// <summary>Feeds an <see cref="IAudioDevice"/> from an ILSF file on disk, a buffer at a time.</summary>
/// <remarks>
/// The adapter between the format layer's <see cref="IlsfStreamReader"/> and the audio layer's
/// <see cref="ISampleSource"/>. It exists in the engine rather than in either of them because
/// <c>OpenIGI.Audio</c> deliberately knows nothing about the game's file formats and
/// <c>OpenIGI.Formats</c> knows nothing about playback.
/// </remarks>
public sealed class IlsfSampleSource : ISampleSource
{
    private readonly IlsfStreamReader _reader;

    /// <summary>Initializes a new instance of the <see cref="IlsfSampleSource"/> class.</summary>
    /// <param name="reader">The reader to pull from. This source takes ownership of it.</param>
    public IlsfSampleSource(IlsfStreamReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _reader = reader;
    }

    /// <summary>Opens an ILSF sound on a stream and wraps it.</summary>
    /// <param name="stream">A readable stream positioned at the ILSF magic. Ownership passes to the source.</param>
    /// <returns>The source.</returns>
    /// <exception cref="IlsfFormatException">The stream does not begin with a valid ILSF header.</exception>
    public static IlsfSampleSource Open(Stream stream) => new(IlsfStreamReader.Open(stream));

    /// <inheritdoc/>
    public AudioFormat Format => new(_reader.SampleRate, _reader.Channels);

    /// <inheritdoc/>
    public bool CanRestart => _reader.CanRestart;

    /// <inheritdoc/>
    public int Read(Span<short> destination) => _reader.ReadFrames(destination) * _reader.Channels;

    /// <inheritdoc/>
    public void Restart() => _reader.Restart();

    /// <inheritdoc/>
    public void Dispose() => _reader.Dispose();
}
