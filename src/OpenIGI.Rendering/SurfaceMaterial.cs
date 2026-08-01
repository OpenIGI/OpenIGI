namespace OpenIGI.Rendering;

/// <summary>
/// How a surface responds to light, beyond the colour its texture gives it.
/// </summary>
/// <remarks>
/// <para>
/// The original's data has nothing of this kind: a surface is a diffuse texture and a lightmap, and
/// how shiny it is was the lighting artist's problem, not the renderer's. So these values are
/// <em>authored for the port</em>, matched onto textures by name in <c>materials.json</c> — snow is a
/// rough dielectric, a gantry is metal, a window is smooth and barely diffuse.
/// </para>
/// <para>
/// That makes them a deliberate divergence rather than a reconstruction, and they only ever apply on
/// the modern path: under the vanilla preset nothing reads them.
/// </para>
/// </remarks>
/// <param name="Roughness">
/// How wide the surface scatters a reflection: 0 is a mirror, 1 is chalk. Drives both the specular
/// highlight and, from stage R8, how sharp a screen-space reflection is allowed to be.
/// </param>
/// <param name="Metallic">
/// Whether the surface reflects its own colour rather than the light's. Physically this is nearly
/// always 0 or 1; values between exist only for surfaces whose texture mixes both.
/// </param>
/// <param name="Emissive">
/// How much light the surface gives off on its own, as a multiple of its albedo. Lamps and screens.
/// </param>
public readonly record struct SurfaceMaterial(
    float Roughness = 0.8f,
    float Metallic = 0.0f,
    float Emissive = 0.0f)
{
    /// <summary>
    /// Gets the material a surface with no rule matching it gets.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Rough and non-metallic: the reading that makes an unclassified surface behave like painted
    /// concrete, which is what most of the game is. A shiny default would put highlights on every
    /// wall in the game the first time the sun moved.
    /// </para>
    /// <para>
    /// Spelled out rather than written <c>new()</c>: for a struct that means the <em>zero</em>
    /// instance, not the primary constructor's defaults, so it would have produced a mirror-smooth
    /// surface with roughness nought — the opposite of what is wanted.
    /// </para>
    /// </remarks>
    public static SurfaceMaterial Default { get; } = new(Roughness: 0.8f, Metallic: 0.0f, Emissive: 0.0f);

    /// <summary>Gets a value indicating whether this material is the default one.</summary>
    public bool IsDefault => this == Default;
}
