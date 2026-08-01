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
/// <param name="DepthBias">Constant depth offset applied when testing, in light-space depth units.</param>
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
/// How far a sample is pushed along its surface normal before being tested, in metres. This is what
/// removes the striping a constant bias alone cannot: the error a shadow map makes grows with the
/// angle between the surface and the light, and moving along the normal scales with exactly that.
/// </param>
public readonly record struct ShadowSettings(
    int CascadeCount = 3,
    int Resolution = 2048,
    float Distance = 220f,
    float SplitBlend = 0.75f,
    float DepthBias = 0.0015f,
    float NormalBias = 0.06f,
    float ContactReach = 0.35f,
    float ContactThickness = 0.5f,
    float FilterRadius = 1f)
{
    /// <summary>The most cascades any backend has to support.</summary>
    public const int MaxCascades = 4;

    /// <summary>Gets settings with shadows switched off.</summary>
    public static ShadowSettings Off { get; } = new(CascadeCount: 0);

    /// <summary>Gets a value indicating whether any shadow is drawn.</summary>
    public bool Enabled => CascadeCount > 0 && Resolution > 0 && Distance > 0f;

    /// <summary>Gets the cascade count clamped to what a backend must support.</summary>
    public int EffectiveCascades => Math.Clamp(CascadeCount, 0, MaxCascades);
}
