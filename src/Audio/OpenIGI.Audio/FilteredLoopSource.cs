namespace OpenIGI.Audio;

/// <summary>
/// One tap on a shared, endlessly looping block of mono PCM, low-passed by an amount the game moves
/// while the tap is sounding.
/// </summary>
/// <remarks>
/// <para>
/// This exists for the continuous beds — rain above all — where the same recording has to be heard
/// from several directions at once and each direction has to sound like the path it took to get here.
/// Rain arriving through an open door is the recording; the same rain arriving through the wall beside
/// it is the recording with its top three octaves gone. A resident buffer cannot express that, because
/// the device offers gain and pitch and nothing else, so the filtering is done here, on the samples,
/// as they are pulled.
/// </para>
/// <para>
/// <b>The PCM is shared and never copied.</b> Every tap holds the same <see cref="ReadOnlyMemory{T}"/>
/// and only its own read cursor, so a two-megabyte loop costs two megabytes however many directions
/// are sounding. Giving each tap a different starting frame is what keeps them from being the same
/// signal six times over: six copies of one waveform sum coherently and collapse to a point between
/// the speakers, where six different stretches of the same rain sum incoherently and stay spread out.
/// </para>
/// <para>
/// <b>The cutoff is smoothed towards its target rather than assigned.</b> A filter coefficient stepped
/// once a frame is a click once a frame; approached over a fifth of a second it is a door opening.
/// Because the smoothing runs per sample inside <see cref="Read"/>, a caller may set
/// <see cref="CutoffHz"/> as often as it likes and never hear the individual steps.
/// </para>
/// <para>
/// <b>Latency.</b> A gain change reaches the ear at once, because gain belongs to the device's own
/// source; a cutoff change reaches it only when the buffers already queued ahead of it have played,
/// which is a quarter of a second at the device's default queue depth. That is the right way round:
/// level is what a listener notices being late, and a doorway takes longer than a quarter second to
/// walk through in any case.
/// </para>
/// </remarks>
public sealed class FilteredLoopSource : ISampleSource
{
    /// <summary>The cutoff that means "do not filter at all", in hertz.</summary>
    /// <remarks>
    /// At or above the sample rate's Nyquist frequency the coefficient is exactly zero and both poles
    /// pass their input through unchanged, so an outdoor tap is bit-for-bit the recording rather than
    /// the recording through a filter that is merely wide. Callers blending towards "open" should
    /// blend towards this value.
    /// </remarks>
    public const float OpenCutoffHz = SmoothedLowPass.OpenCutoffHz;

    /// <summary>The lowest cutoff a caller may ask for, in hertz.</summary>
    /// <remarks>
    /// Below this the bed stops being muffled rain and starts being a hum with no rain in it at all.
    /// Concrete and rain together do not get duller than this in life either.
    /// </remarks>
    public const float MinimumCutoffHz = SmoothedLowPass.MinimumCutoffHz;

    private readonly ReadOnlyMemory<short> _pcm;
    private readonly int _sampleRate;
    private readonly int _startFrame;
    private readonly SmoothedLowPass _filter;

    private int _cursor;

    /// <summary>Initializes a new instance of the <see cref="FilteredLoopSource"/> class.</summary>
    /// <param name="pcm">The shared mono 16-bit PCM to loop. It is read, never written or copied.</param>
    /// <param name="sampleRate">The rate <paramref name="pcm"/> was recorded at, in hertz.</param>
    /// <param name="startFrame">Where in the loop this tap begins; wrapped into range.</param>
    /// <param name="cutoffHz">The cutoff to start at, so a tap does not audibly open as it fades in.</param>
    /// <param name="smoothingSeconds">How long the cutoff takes to travel most of the way to a new target.</param>
    /// <exception cref="ArgumentException"><paramref name="pcm"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The rate or the smoothing time is not positive.</exception>
    public FilteredLoopSource(
        ReadOnlyMemory<short> pcm,
        int sampleRate,
        int startFrame = 0,
        float cutoffHz = OpenCutoffHz,
        float smoothingSeconds = 0.2f)
    {
        if (pcm.IsEmpty)
        {
            throw new ArgumentException("A looping tap needs at least one sample.", nameof(pcm));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);

        _pcm = pcm;
        _sampleRate = sampleRate;
        _startFrame = (int)(((startFrame % pcm.Length) + pcm.Length) % (long)pcm.Length);
        _cursor = _startFrame;

        _filter = new SmoothedLowPass(sampleRate, cutoffHz, smoothingSeconds);
    }

    /// <inheritdoc/>
    public AudioFormat Format => new(_sampleRate, 1, 16);

    /// <inheritdoc/>
    /// <remarks>
    /// True, though nothing will call it: <see cref="Read"/> never reports a short fill, because the
    /// loop has no end to run off.
    /// </remarks>
    public bool CanRestart => true;

    /// <summary>Gets or sets the low-pass cutoff this tap is heading towards, in hertz.</summary>
    /// <remarks>
    /// Clamped into <see cref="MinimumCutoffHz"/> … <see cref="OpenCutoffHz"/>. Set from the game
    /// thread and read from whichever thread is pumping the device, so it goes through
    /// <see cref="Volatile"/> in both directions; a torn float here would be a single wrong
    /// coefficient the smoothing would swallow anyway, but the contract is cheap to honour.
    /// </remarks>
    public float CutoffHz
    {
        get => _filter.CutoffHz;
        set => _filter.CutoffHz = value;
    }

    /// <summary>Puts the cutoff at a value immediately, with no glide.</summary>
    /// <param name="cutoffHz">The cutoff, in hertz.</param>
    /// <remarks>
    /// For the frame a tap starts on, where there is no previous value to glide from and the smoothing
    /// would otherwise be heard as the sound opening up over its first fifth of a second.
    /// </remarks>
    public void SnapCutoff(float cutoffHz) => _filter.Snap(cutoffHz);

    /// <inheritdoc/>
    /// <remarks>
    /// Always fills the whole span: the cursor wraps rather than stopping, so the device's stream pump
    /// never sees a short read and never has to call <see cref="Restart"/>. Wrapping mid-buffer is
    /// silent because the loop the caller supplies is crossfaded closed — see the rain asset's own
    /// preparation notes.
    /// </remarks>
    public int Read(Span<short> destination)
    {
        ReadOnlySpan<short> pcm = _pcm.Span;
        int cursor = _cursor;
        for (int i = 0; i < destination.Length; i++)
        {
            destination[i] = (short)Math.Clamp(_filter.Process(pcm[cursor]), short.MinValue, short.MaxValue);
            if (++cursor >= pcm.Length)
            {
                cursor = 0;
            }
        }

        _cursor = cursor;
        return destination.Length;
    }

    /// <inheritdoc/>
    public void Restart() => _cursor = _startFrame;

    /// <inheritdoc/>
    /// <remarks>Nothing is owned: the PCM belongs to whoever handed it over and outlives every tap.</remarks>
    public void Dispose()
    {
        // Deliberately empty. The device disposes a source when its voice ends, and a tap that holds
        // only a borrowed span has nothing to release.
    }

    /// <summary>Turns a cutoff frequency into the one-pole coefficient that realises it; see <see cref="SmoothedLowPass.CoefficientFor"/>.</summary>
    /// <param name="cutoffHz">The cutoff, in hertz.</param>
    /// <param name="sampleRate">The sample rate, in hertz.</param>
    /// <returns>The coefficient, zero (no filtering) to just under one.</returns>
    public static float CoefficientFor(float cutoffHz, int sampleRate) => SmoothedLowPass.CoefficientFor(cutoffHz, sampleRate);

    /// <summary>Turns a one-pole coefficient back into the cutoff it stands for.</summary>
    /// <param name="coefficient">The coefficient, zero to just under one.</param>
    /// <param name="sampleRate">The sample rate, in hertz.</param>
    /// <returns>The cutoff, in hertz.</returns>
    public static float CutoffFor(float coefficient, int sampleRate) => SmoothedLowPass.CutoffFor(coefficient, sampleRate);
}
