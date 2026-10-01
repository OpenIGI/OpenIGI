using OpenIGI.Audio;
using OpenIGI.Formats.Sound;

namespace OpenIGI.Engine.Audio;

/// <summary>Feeds an <see cref="IAudioDevice"/> from an MPEG audio stream, a buffer at a time.</summary>
/// <remarks>
/// The sibling of <see cref="IlsfSampleSource"/>, and it sits here for the same reason:
/// <c>OpenIGI.Audio</c> deliberately knows nothing about the game's file formats and
/// <c>OpenIGI.Formats</c> knows nothing about playback. Which of the two a track needs is decided by
/// the bank from the bytes, so nothing above this point has to care that I.G.I. 2's music is the one
/// retail sound that is not ILSF.
/// </remarks>
public sealed class MpegSampleSource : ISampleSource
{
    private readonly MpegAudioStreamReader _reader;

    /// <summary>Initializes a new instance of the <see cref="MpegSampleSource"/> class.</summary>
    /// <param name="reader">The reader to pull from. This source takes ownership of it.</param>
    public MpegSampleSource(MpegAudioStreamReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _reader = reader;
    }

    /// <summary>Opens an MPEG audio stream and wraps it.</summary>
    /// <param name="stream">A readable, seekable stream positioned at the first frame. Ownership passes to the source.</param>
    /// <returns>The source.</returns>
    /// <exception cref="MpegAudioFormatException">The stream does not hold readable MPEG audio.</exception>
    public static MpegSampleSource Open(Stream stream) => new(MpegAudioStreamReader.Open(stream));

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
