namespace OpenIGI.Rendering;

/// <summary>
/// How the sun's shadows are drawn.
/// </summary>
/// <param name="CascadeCount">How many shadow maps the view distance is split across.</param>
/// <param name="Resolution">The side length in texels of each cascade's map.</param>
/// <param name="Distance">How far from the camera, in metres, shadows are drawn at all.</param>
/// <param name="SplitBlend">
/// How the split distances are chosen between an even division and a logarithmic one. Zero divides the
/// range evenly, which wastes almost all the near cascade's resolution on ground the player can see in
/// detail; one is logarithmic, which is right for the near cascade and leaves the far one starved.
/// </param>
/// <param name="DepthBias">
/// Depth offset applied when testing, <b>in texels of the cascade being sampled</b>, measured along the
/// light. A shadow map's error is one of its texels projected onto the receiver, so that is the unit the
/// bias belongs in — and the unit that makes one number right for every cascade, since a far cascade's
/// texel covers more ground and wants proportionally more.
/// <para>
/// It was a constant fraction of the cascade's depth range, which <see cref="ShadowCascades.CasterReach"/>
/// makes about 250 metres deep whatever the cascade — so 0.0015 of it was some 0.4 metres of bias along
/// the sun, and at a low sun that slid every shadow half a metre across the ground. That is peter-panning,
/// and it was enough to read as the ray-traced and rasterised shadows disagreeing about where a shadow
/// falls, because the ray traces from the surface itself and needs no bias at all.
/// </para>
/// </param>
/// <param name="ContactReach">
/// How far, in metres, a screen-space ray is marched towards the sun to catch contact a shadow map
/// cannot resolve. Zero disables it.
/// </param>
/// <param name="ContactThickness">
/// How thick an occluder found by that ray is assumed to be. Beyond this the ray is looking past a
/// surface's edge at something unrelated, and treating that as an occluder draws long false shadows.
/// </param>
/// <param name="FilterRadius">
/// The shadow filter's radius in texels. Above 1.5 a wider kernel is used.
/// </param>
/// <param name="NormalBias">
/// How far the sample is pushed along the surface's own normal before testing, <b>in texels of the
/// cascade being sampled</b>. The error a shadow map makes grows with the angle between the surface and
/// the light, and moving along the normal scales with exactly that, which removes the striping a depth
/// bias alone cannot. In texels for the same reason as <paramref name="DepthBias"/>: a constant in metres
/// is ten texels of the near cascade and a fraction of one in the far.
/// </param>
public readonly record struct ShadowSettings(
    int CascadeCount = 3,
    int Resolution = 2048,
    float Distance = 220f,
    float SplitBlend = 0.75f,
    float DepthBias = 2f,
    float NormalBias = 1.5f,
    float ContactReach = 0.35f,
    float ContactThickness = 0.5f,
    float FilterRadius = 1f)
{
    /// <summary>The most cascades any backend has to support.</summary>
    public const int MaxCascades = 4;

    /// <summary>
    /// Gets a value indicating whether the sun's shadow may be traced against the scene's acceleration
    /// structure instead of read from the cascades, on a backend that can trace at all.
    /// </summary>
    /// <remarks>
    /// Off falls back to the cascaded map everywhere — the software path. The backend never reads the
    /// console itself, so the app decides here and every consumer (the deferred sun, the gathers that
    /// share the flag's spirit) follows the same answer.
    /// </remarks>
    public bool TracedSun { get; init; } = true;

    /// <summary>Gets settings with shadows switched off.</summary>
    public static ShadowSettings Off { get; } = new(CascadeCount: 0);

    /// <summary>Gets a value indicating whether any shadow is drawn.</summary>
    public bool Enabled => CascadeCount > 0 && Resolution > 0 && Distance > 0f;

    /// <summary>Gets the cascade count clamped to what a backend must support.</summary>
    public int EffectiveCascades => Math.Clamp(CascadeCount, 0, MaxCascades);
}
