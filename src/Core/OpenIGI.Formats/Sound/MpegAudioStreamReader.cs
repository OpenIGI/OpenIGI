using System.Buffers.Binary;
using NLayer;

namespace OpenIGI.Formats.Sound;

/// <summary>
/// Pulls 16-bit PCM out of an MPEG audio stream a slice at a time, without ever holding the decoded
/// sound in memory.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart of <see cref="IlsfStreamReader"/> for I.G.I. 2's music, and it streams for the same
/// reason: the sequel's menu theme is three and a half minutes of 320 kbps stereo — eight megabytes
/// packed and about thirty-six expanded — and retail never holds either, because Miles decodes it a
/// buffer at a time behind <c>mss32.dll</c>.
/// </para>
/// <para>
/// Samples come out as 16-bit whatever the stream's internal precision, so every caller stays on one
/// sample type and the audio layer needs to know nothing about which of the two formats it is hearing.
/// </para>
/// </remarks>
public sealed class MpegAudioStreamReader : IDisposable
{
    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private readonly MpegFile _file;
    private byte[] _scratch = [];
    private int _pendingOffset;
    private int _pendingBytes;
    private long _framesRead;
    private bool _disposed;

    private MpegAudioStreamReader(Stream stream, bool leaveOpen, MpegFile file, MpegAudioHeader header)
    {
        _stream = stream;
        _leaveOpen = leaveOpen;
        _file = file;
        Header = header;
    }

    /// <summary>Gets the first frame's header, which gives the stream's shape.</summary>
    public MpegAudioHeader Header { get; }

    /// <summary>Gets the channel count, one or two.</summary>
    /// <remarks>
    /// The decoder's rather than the header's. They agree for everything retail ships, and where a
    /// stream changes its mind mid-file it is the decoder's answer that describes the samples produced.
    /// </remarks>
    public int Channels => _file.Channels;

    /// <summary>Gets the sample rate in hertz.</summary>
    public int SampleRate => _file.SampleRate;

    /// <summary>Gets the number of frames handed out so far, where a frame is one sample per channel.</summary>
    public long FramesRead => _framesRead;

    /// <summary>Gets a value indicating whether the stream can be rewound for looping.</summary>
    public bool CanRestart => _file.CanSeek;

    /// <summary>Opens a reader over an MPEG audio stream.</summary>
    /// <param name="stream">A readable stream positioned at the first frame, or at an ID3 tag before it.</param>
    /// <param name="leaveOpen"><see langword="true"/> to leave <paramref name="stream"/> open when this reader is disposed.</param>
    /// <returns>The reader.</returns>
    /// <exception cref="MpegAudioFormatException">The stream does not hold readable MPEG audio.</exception>
    /// <remarks>
    /// The header is read from the front of the stream before the decoder is handed it, which is what
    /// turns a stream of something else into a typed failure rather than whatever the decoder happens
    /// to throw on the way through.
    /// </remarks>
    public static MpegAudioStreamReader Open(Stream stream, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanSeek)
        {
            throw new MpegAudioFormatException("An MPEG audio stream must be seekable to be decoded.");
        }

        long start = stream.Position;
        byte[] probe = new byte[MpegAudioHeader.ProbeSize];
        int read = stream.ReadAtLeast(probe, MpegAudioHeader.Size, throwOnEndOfStream: false);
        stream.Position = start;

        if (!MpegAudioHeader.TryRead(probe.AsSpan(0, read), out MpegAudioHeader header))
        {
            throw new MpegAudioFormatException("The stream does not begin with a readable MPEG audio frame header.");
        }

        MpegFile file;
        try
        {
            file = new MpegFile(stream);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or FormatException)
        {
            throw new MpegAudioFormatException($"The MPEG audio stream would not open: {error.Message}", error);
        }

        return new MpegAudioStreamReader(stream, leaveOpen, file, header);
    }

    /// <summary>Reads the next frames into <paramref name="destination"/>.</summary>
    /// <param name="destination">Receives channel-interleaved 16-bit samples.</param>
    /// <returns>The number of frames written, or zero at the end of the stream.</returns>
    /// <exception cref="ArgumentException"><paramref name="destination"/> cannot hold a whole frame.</exception>
    /// <remarks>
    /// <para>
    /// <b>The destination is filled, not merely written to.</b> A decoder produces whole granules —
    /// 1152 frames at a time for Layer III — and a caller asks for whatever its buffer holds, so the
    /// two never line up. What is decoded past the end of one call is kept and served to the next
    /// rather than dropped, which is what lets this answer a small buffer at all.
    /// </para>
    /// <para>
    /// That matters more than it sounds. The device's fill loop reads until its buffer is full and
    /// treats a zero as the end of the sound — so a reader that returned zero with samples still to
    /// come would have a looping track rewind, read zero again, and rewind again: a hang rather than a
    /// glitch. Returning zero here means the stream really is exhausted.
    /// </para>
    /// </remarks>
    public int ReadFrames(Span<short> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int channels = Channels;
        int capacity = destination.Length / channels;
        if (capacity == 0)
        {
            throw new ArgumentException(
                $"A destination of {destination.Length} sample(s) cannot hold a frame of {channels} channel(s).",
                nameof(destination));
        }

        int wanted = capacity * channels;
        int written = 0;

        while (written < wanted)
        {
            if (_pendingBytes < sizeof(short) && !Decode())
            {
                break;
            }

            int take = Math.Min(wanted - written, _pendingBytes / sizeof(short));

            // The decoder writes little-endian pairs whatever the host is, so they are read as such
            // rather than reinterpreted in place.
            for (int i = 0; i < take; i++)
            {
                destination[written + i] = BinaryPrimitives.ReadInt16LittleEndian(
                    _scratch.AsSpan(_pendingOffset + (i * sizeof(short))));
            }

            written += take;
            _pendingOffset += take * sizeof(short);
            _pendingBytes -= take * sizeof(short);
        }

        // Only whole frames leave here. A lone channel can only be left over at the very end of the
        // stream, and dropping it there beats shifting the interleave of everything after it.
        int frames = written / channels;
        _framesRead += frames;
        return frames;
    }

    /// <summary>Rewinds to the first frame so the sound can loop.</summary>
    /// <exception cref="NotSupportedException">The underlying stream cannot seek.</exception>
    public void Restart()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_file.CanSeek)
        {
            throw new NotSupportedException("This sound is on a stream that cannot seek, so it cannot loop.");
        }

        _file.Position = 0;
        _framesRead = 0;
        _pendingOffset = 0;
        _pendingBytes = 0;
    }

    /// <summary>Pulls the next chunk out of the decoder into the pending buffer.</summary>
    /// <returns><see langword="false"/> when the stream is exhausted.</returns>
    /// <remarks>
    /// The request is four bytes per sample rather than two, and deliberately: NLayer sizes its output
    /// from the buffer as though it held 32-bit samples and then writes 16-bit ones, so it fills half
    /// of whatever it is given. Asking for double gets a full chunk in one call, and a buffer that big
    /// is safe either way — a decoder that filled the whole request would still be within it.
    /// </remarks>
    private bool Decode()
    {
        const int ChunkFrames = 4096;

        int request = ChunkFrames * Channels * sizeof(float);
        if (_scratch.Length < request)
        {
            _scratch = new byte[request];
        }

        int bytes = _file.ReadSamplesInt16(_scratch, 0, request);
        _pendingOffset = 0;
        _pendingBytes = Math.Max(bytes, 0);
        return _pendingBytes >= sizeof(short);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // The decoder was handed a stream it does not own -- only its own file-name constructor takes
        // ownership -- so closing it is this reader's job, and doing it twice is not.
        _file.Dispose();
        if (!_leaveOpen)
        {
            _stream.Dispose();
        }
    }
}
