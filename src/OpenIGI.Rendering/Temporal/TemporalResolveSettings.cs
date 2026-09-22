namespace OpenIGI.Rendering.Temporal;

/// <summary>
/// What the temporal resolve is asked to do, as distinct from where the camera is.
/// </summary>
/// <param name="Enabled">Whether history is accumulated at all; off, every frame starts fresh.</param>
/// <param name="MaxSamples">
/// How many frames a texel's history may average over. The higher it is the steadier a still picture,
/// and the longer a wrong answer survives once the camera moves.
/// </param>
/// <param name="Sharpen">How much of the resolve's sharpening filter is applied, nought to one.</param>
/// <param name="Exposure">
/// The exposure the resolve will apply, which the rejection is judged in: two colours that differ by
/// less than a stop after exposure are the same colour to an eye and should not reject.
/// </param>
/// <param name="ShadingBlend">
/// How much of the shading rejection is believed, which the reference calls the theoretical blend.
/// </param>
/// <param name="ThinRelaxation">
/// How far a thin feature is allowed to relax its rejection, nought to one. A wire or a railing is
/// narrower than the filter's own kernel, so judged strictly it rejects every frame and never
/// accumulates.
/// </param>
/// <param name="HistoryScale">
/// How many history texels there are per rendered pixel on each axis. Two, and the reason is measured:
/// sizing the history from the presented output instead is finer still under an upscale, and with a
/// kernel that only reaches half an input pixel most of those texels see no sample on a given frame.
/// </param>
/// <param name="DebugView">Which intermediate the resolve shows instead of the picture, or nought.</param>
/// <remarks>
/// <b>The values live in <see cref="Default"/>, not in this constructor.</b> A record struct's parameter
/// defaults apply only to a call that leaves an argument out, and <c>new T()</c> is not such a call: it
/// is the struct's own zeroing constructor, which runs none of them. Written as parameter defaults they
/// would be silently skipped by the shortest way anyone writes — and a history scale of nought sizes the
/// history at nothing, which the resolve then reads back as a texture nobody wrote.
/// </remarks>
public readonly record struct TemporalResolveSettings(
    bool Enabled,
    float MaxSamples,
    float Sharpen,
    float Exposure,
    float ShadingBlend,
    float ThinRelaxation,
    int HistoryScale,
    float DebugView)
{
    /// <summary>Gets the settings a frame runs with when nothing has chosen otherwise.</summary>
    public static TemporalResolveSettings Default => new(
        Enabled: true,
        MaxSamples: 32f,
        Sharpen: 0f,
        Exposure: 1f,
        ShadingBlend: 1f,
        ThinRelaxation: 1f,
        HistoryScale: 2,
        DebugView: 0f);

    /// <summary>Gets the settings a frame with no temporal filtering runs with.</summary>
    /// <remarks>
    /// Everything else stays as it was: the chain still runs, and what changes is that a texel's history
    /// is not believed. Turning it off by zeroing the record would turn off the sizes as well.
    /// </remarks>
    public static TemporalResolveSettings Off => Default with { Enabled = false };

    /// <summary>Gets how many history texels there are per rendered pixel, never fewer than one.</summary>
    /// <remarks>
    /// Read through rather than taken raw, so that a zeroed record dispatches over something rather than
    /// nothing. It is the second guard on the same mistake, and the cheaper of the two to keep.
    /// </remarks>
    public int EffectiveHistoryScale => Math.Max(1, HistoryScale);
}
