namespace OpenIGI.Formats.Sound;

/// <summary>
/// Expands the four-bit ADPCM the <see cref="IlsfFormat.StreamedAdpcm"/> and
/// <see cref="IlsfFormat.ResidentAdpcm"/> forms carry into signed 16-bit PCM.
/// </summary>
/// <remarks>
/// <para>
/// Transcribed from the original decoder <c>0x496280</c> and its state initialiser <c>0x496420</c>. The
/// scheme is plain IMA/DVI ADPCM: the index table at <c>0x543920</c> is
/// <c>{-1,-1,-1,-1,2,4,6,8}</c> repeated for the signed half, and the 89-entry step table at
/// <c>0x543930</c> is the standard one, both verified byte-for-byte against the image.
/// </para>
/// <para>
/// Two details are not obvious from the tables and cost a channel swap or a click if missed. Nibbles are
/// taken <em>high first</em> within a byte, and channels interleave <em>per sample</em>, not per block —
/// a stereo frame is one byte holding left in the high nibble and right in the low. The original keeps
/// one predictor and one step index per channel and at most two channels fit its state record, so this
/// decoder enforces the same bound.
/// </para>
/// <para>
/// <b>Divergence, deliberate:</b> the original does not store the half-consumed byte across calls. It
/// sets a flag and expects the caller to re-present that byte at the head of the next buffer, which the
/// resident path never exercises because it decodes in one call. This decoder holds the byte itself, so
/// a streaming caller can hand it strictly forward-moving buffers. The samples produced are identical.
/// </para>
/// </remarks>
public sealed class IlsfAdpcmDecoder
{
    /// <summary>The largest number of channels the original's state record can hold.</summary>
    public const int MaximumChannels = 2;

    /// <summary>The step index is clamped to this range, exactly as <c>0x496280</c> does.</summary>
    private const int MaximumStepIndex = 88;

    /// <summary>The step-index adjustment per nibble, from <c>0x543920</c>.</summary>
    private static readonly int[] IndexTable =
    [
        -1, -1, -1, -1, 2, 4, 6, 8,
        -1, -1, -1, -1, 2, 4, 6, 8,
    ];

    /// <summary>The 89 quantiser step sizes, from <c>0x543930</c>.</summary>
    private static readonly int[] StepTable =
    [
        7, 8, 9, 10, 11, 12, 13, 14, 16, 17,
        19, 21, 23, 25, 28, 31, 34, 37, 41, 45,
        50, 55, 60, 66, 73, 80, 88, 97, 107, 118,
        130, 143, 157, 173, 190, 209, 230, 253, 279, 307,
        337, 371, 408, 449, 494, 544, 598, 658, 724, 796,
        876, 963, 1060, 1166, 1282, 1411, 1552, 1707, 1878, 2066,
        2272, 2499, 2749, 3024, 3327, 3660, 4026, 4428, 4871, 5358,
        5894, 6484, 7132, 7845, 8630, 9493, 10442, 11487, 12635, 13899,
        15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767,
    ];

    private readonly int _channels;
    private readonly int[] _predictor = new int[MaximumChannels];
    private readonly int[] _stepIndex = new int[MaximumChannels];
    private byte _pending;
    private bool _hasPending;

    /// <summary>Initializes a new instance of the <see cref="IlsfAdpcmDecoder"/> class.</summary>
    /// <param name="channels">The channel count, one or two.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="channels"/> is outside one to <see cref="MaximumChannels"/>.</exception>
    /// <remarks>Mirrors <c>0x496420</c>, which zeroes both predictors, both step indices and the nibble flag.</remarks>
    public IlsfAdpcmDecoder(int channels)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(channels, MaximumChannels);
        _channels = channels;
    }

    /// <summary>Gets the channel count this decoder was created for.</summary>
    public int Channels => _channels;

    /// <summary>
    /// Gets a value indicating whether half a byte from the previous call is still waiting to be used.
    /// </summary>
    /// <remarks>
    /// A streaming caller needs this to size its next read: with an odd number of nibbles outstanding,
    /// the next batch of samples starts inside a byte this decoder already holds.
    /// </remarks>
    public bool HasPendingNibble => _hasPending;

    /// <summary>Returns the number of encoded bytes <paramref name="frameCount"/> frames occupy.</summary>
    /// <param name="frameCount">The number of frames, where a frame is one sample per channel.</param>
    /// <returns>The encoded size in bytes, rounded up because a mono frame is half a byte.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="frameCount"/> is negative.</exception>
    public long EncodedByteCount(long frameCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frameCount);
        long nibbles = frameCount * _channels;
        return (nibbles + 1) / 2;
    }

    /// <summary>Decodes <paramref name="frameCount"/> frames.</summary>
    /// <param name="source">The encoded nibbles, high nibble of each byte first.</param>
    /// <param name="destination">Receives <c>frameCount * Channels</c> samples, channel-interleaved.</param>
    /// <param name="frameCount">The number of frames to produce.</param>
    /// <returns>The number of source bytes consumed.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="frameCount"/> is negative.</exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too small, or <paramref name="source"/> holds too few nibbles.</exception>
    /// <remarks>
    /// State carries across calls, so a stream can be decoded in arbitrary slices as long as the source
    /// bytes are presented in order.
    /// </remarks>
    public int Decode(ReadOnlySpan<byte> source, Span<short> destination, int frameCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frameCount);

        int sampleCount = frameCount * _channels;
        if (destination.Length < sampleCount)
        {
            throw new ArgumentException(
                $"Decoding {frameCount} frame(s) of {_channels} channel(s) needs {sampleCount} samples, but only {destination.Length} fit.",
                nameof(destination));
        }

        int nibblesNeeded = sampleCount - (_hasPending ? 1 : 0);
        int bytesNeeded = Math.Max(0, (nibblesNeeded + 1) / 2);
        if (source.Length < bytesNeeded)
        {
            throw new ArgumentException(
                $"Decoding {frameCount} frame(s) needs {bytesNeeded} encoded byte(s), but only {source.Length} were supplied.",
                nameof(source));
        }

        int read = 0;
        int written = 0;
        for (int frame = 0; frame < frameCount; frame++)
        {
            for (int channel = 0; channel < _channels; channel++)
            {
                int nibble;
                if (_hasPending)
                {
                    nibble = _pending & 0xF;
                }
                else
                {
                    _pending = source[read++];
                    nibble = _pending >> 4;
                }

                _hasPending = !_hasPending;
                destination[written++] = Step(channel, nibble);
            }
        }

        return read;
    }

    /// <summary>Advances one channel by one nibble and returns the sample it produces.</summary>
    private short Step(int channel, int nibble)
    {
        // The step in force is the one the *previous* index selected; the index moves first but the new
        // step only applies to the next sample. Reading the step before updating the index reproduces
        // the original's cached-step variable exactly.
        int step = StepTable[_stepIndex[channel]];
        _stepIndex[channel] = Math.Clamp(_stepIndex[channel] + IndexTable[nibble], 0, MaximumStepIndex);

        // difference = step/8 + step/4*b0 + step/2*b1 + step*b2, built by shifts as the original does.
        int magnitude = step >> 3;
        if ((nibble & 4) != 0)
        {
            magnitude += step;
        }

        if ((nibble & 2) != 0)
        {
            magnitude += step >> 1;
        }

        if ((nibble & 1) != 0)
        {
            magnitude += step >> 2;
        }

        int predictor = (nibble & 8) != 0
            ? _predictor[channel] - magnitude
            : _predictor[channel] + magnitude;

        predictor = Math.Clamp(predictor, short.MinValue, short.MaxValue);
        _predictor[channel] = predictor;
        return (short)predictor;
    }
}
