using System.Buffers.Binary;

namespace OpenIGI.Formats.Sound;

/// <summary>
/// Pulls 16-bit PCM out of an ILSF sound a slice at a time, without ever holding the whole sound in
/// memory.
/// </summary>
/// <remarks>
/// <para>
/// This is the path the <see cref="IlsfFormat.StreamedPcm"/> and <see cref="IlsfFormat.StreamedAdpcm"/>
/// forms exist for. The original kept only the file name for those two codes (loader <c>0x495F70</c>,
/// record <c>+40</c>) precisely so the long sounds — the music track and the ambience beds — would not
/// sit in memory: a retail music track is three megabytes packed and about thirteen expanded.
/// </para>
/// <para>
/// Samples come out as 16-bit whatever the file declares. Nothing in retail is 8-bit, but the header
/// allows it and widening here keeps every caller on one sample type.
/// </para>
/// </remarks>
public sealed class IlsfStreamReader : IDisposable
{
    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private readonly long _payloadStart;
    private IlsfAdpcmDecoder? _decoder;
    private byte[] _scratch = [];
    private long _framesRead;
    private bool _disposed;

    private IlsfStreamReader(Stream stream, bool leaveOpen, IlsfHeader header, long payloadStart)
    {
        _stream = stream;
        _leaveOpen = leaveOpen;
        _payloadStart = payloadStart;
        Header = header;
        _decoder = header.IsAdpcm ? new IlsfAdpcmDecoder(header.Channels) : null;
    }

    /// <summary>Gets the parsed header.</summary>
    public IlsfHeader Header { get; }

    /// <summary>Gets how the samples are stored in the file.</summary>
    public IlsfFormat Format => Header.Format;

    /// <summary>Gets the channel count, one or two.</summary>
    public int Channels => Header.Channels;

    /// <summary>Gets the sample rate in hertz.</summary>
    public int SampleRate => Header.SampleRate;

    /// <summary>Gets the total number of frames in the sound.</summary>
    public int FrameCount => Header.FrameCount;

    /// <summary>Gets the number of frames handed out so far.</summary>
    public long FramesRead => _framesRead;

    /// <summary>Gets a value indicating whether every frame has been read.</summary>
    public bool EndOfSound => _framesRead >= FrameCount;

    /// <summary>Gets a value indicating whether the sound can be rewound for looping.</summary>
    public bool CanRestart => _stream.CanSeek;

    /// <summary>Opens a reader over an ILSF stream, consuming its header.</summary>
    /// <param name="stream">A readable stream positioned at the ILSF magic.</param>
    /// <param name="leaveOpen"><see langword="true"/> to leave <paramref name="stream"/> open when this reader is disposed.</param>
    /// <returns>The reader.</returns>
    /// <exception cref="IlsfFormatException">The stream does not begin with a valid ILSF header.</exception>
    public static IlsfStreamReader Open(Stream stream, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);

        long start = stream.CanSeek ? stream.Position : 0;
        byte[] header = new byte[IlsfHeader.Size];
        try
        {
            stream.ReadExactly(header);
        }
        catch (EndOfStreamException error)
        {
            throw new IlsfFormatException($"An ILSF sound needs at least {IlsfHeader.Size} bytes.", error);
        }

        return new IlsfStreamReader(stream, leaveOpen, IlsfHeader.Read(header), start + IlsfHeader.Size);
    }

    /// <summary>Reads the next frames into <paramref name="destination"/>.</summary>
    /// <param name="destination">Receives channel-interleaved 16-bit samples.</param>
    /// <returns>The number of frames written, or zero at the end of the sound.</returns>
    /// <exception cref="ArgumentException"><paramref name="destination"/> cannot hold a whole frame.</exception>
    /// <exception cref="IlsfFormatException">The stream ended before the declared frame count.</exception>
    public int ReadFrames(Span<short> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int capacity = destination.Length / Channels;
        if (capacity == 0)
        {
            throw new ArgumentException(
                $"A destination of {destination.Length} sample(s) cannot hold a frame of {Channels} channel(s).",
                nameof(destination));
        }

        int frames = (int)Math.Min(capacity, FrameCount - _framesRead);
        if (frames <= 0)
        {
            return 0;
        }

        // A half-used byte held by the decoder covers the first nibble of this batch, so the read is one
        // nibble shorter than the frame count alone would suggest.
        int needed = _decoder is null
            ? frames * Header.BlockAlign
            : ((frames * Channels) - (_decoder.HasPendingNibble ? 1 : 0) + 1) / 2;

        if (_scratch.Length < needed)
        {
            _scratch = new byte[needed];
        }

        Span<byte> encoded = _scratch.AsSpan(0, needed);
        try
        {
            _stream.ReadExactly(encoded);
        }
        catch (EndOfStreamException error)
        {
            throw new IlsfFormatException(
                $"The sound declares {FrameCount} frame(s) but the stream ended after {_framesRead}.",
                error);
        }

        Span<short> output = destination[..(frames * Channels)];
        if (_decoder is not null)
        {
            _decoder.Decode(encoded, output, frames);
        }
        else if (Header.BitsPerSample == 16)
        {
            for (int i = 0; i < output.Length; i++)
            {
                output[i] = BinaryPrimitives.ReadInt16LittleEndian(encoded[(i * sizeof(short))..]);
            }
        }
        else
        {
            // 8-bit PCM is unsigned with 0x80 as silence, the WAV convention the header inherits.
            for (int i = 0; i < output.Length; i++)
            {
                output[i] = (short)((encoded[i] - 0x80) << 8);
            }
        }

        _framesRead += frames;
        return frames;
    }

    /// <summary>Rewinds to the first frame so the sound can loop.</summary>
    /// <exception cref="NotSupportedException">The underlying stream cannot seek.</exception>
    public void Restart()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_stream.CanSeek)
        {
            throw new NotSupportedException("This sound is on a stream that cannot seek, so it cannot loop.");
        }

        _stream.Position = _payloadStart;
        _framesRead = 0;

        // ADPCM is differential: resuming needs the predictor and step index back at the values
        // 0x496420 sets, or the frames just after the loop point drift audibly.
        if (_decoder is not null)
        {
            _decoder = new IlsfAdpcmDecoder(Channels);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (!_leaveOpen)
        {
            _stream.Dispose();
        }
    }
}
