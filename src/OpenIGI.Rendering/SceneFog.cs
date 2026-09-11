using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering;

/// <summary>The original's distance fog: how fast the air closes, and what colour it closes to.</summary>
/// <param name="Colour">The colour a fully fogged surface takes, linear, from the level's <c>FlatSky</c>.</param>
/// <param name="DensityPerMetre">
/// The extinction the exponential is evaluated with, per metre of view depth. Zero is no fog.
/// </param>
/// <remarks>
/// <para>
/// The original fogs per vertex and lets Direct3D do the blend. <c>sub_512C30</c> computes the factor at
/// <c>0x513462</c> and packs it into the vertex's specular alpha —
/// </para>
/// <code>
/// f = max(exp(-z * flt_BCABEC * flt_A7DA18), 0) * 255
/// </code>
/// <para>
/// — where <c>z</c> is the view-space depth in game units, <c>flt_BCABEC</c> is the <c>0.001</c> that
/// <c>sub_493580</c> writes at start-up (<c>0x4936E3</c>), and <c>flt_A7DA18</c> is the density
/// <c>sub_50FD20</c> (<c>0x50FD20</c>) publishes. With <c>FOGTABLEMODE</c> left at <c>D3DFOG_NONE</c> and
/// every vertex already transformed, that alpha <em>is</em> the fog factor the hardware blends with:
/// <c>colour = f · surface + (1 - f) · fogColour</c>.
/// </para>
/// <para>
/// The density is a level's own. The weather update <c>sub_527520</c> hands <c>sub_50FD20</c> the
/// <c>FlatSky</c> task's fog colour and its <b>Fog Amount × 0.0005</b> (<c>0x527B41</c>), so with 4096
/// game units to the metre the whole chain collapses to <see cref="DensityFor"/>: <c>0.002048</c> per
/// metre per unit of fog amount. The thirteen shipping levels ask for <c>0.70</c> to <c>0.975</c>, which
/// is half the surface gone by 350–480 m and all but a hundredth of it by 2.3–3.2 km, and every one of
/// them fogs to the same <c>(0.2, 0.288, 0.4)</c> slate blue.
/// </para>
/// <para>
/// <b>The fog colour follows the blend, not the level</b> (<c>sub_5144E0</c>): an opaque draw fogs to the
/// level's colour, an additive one (<c>ONE, ONE</c>) to black (<c>0x5145BB</c>), and a modulating one
/// (<c>ZERO, SRCCOLOR</c>) to white (<c>0x514529</c>) — each blend's own identity, so that a surface
/// drawn as several passes fogs once rather than once per pass. <see cref="ColourFor"/> is that rule.
/// </para>
/// <para>
/// The sky dome is not fogged at all: <c>sub_52D060</c> sets <c>FOGENABLE</c> to zero along with its
/// depth states before drawing it, which is what leaves the horizon a gradient rather than a flat wash.
/// </para>
/// </remarks>
public readonly record struct SceneFog(Vector3f Colour, float DensityPerMetre)
{
    /// <summary>The scale <c>sub_493580</c> writes to <c>flt_BCABEC</c>, applied to the view depth.</summary>
    public const float DistanceScale = 0.001f;

    /// <summary>What <c>sub_527520</c> multiplies a level's <c>Fog Amount</c> by before publishing it.</summary>
    public const float AmountScale = 0.0005f;

    /// <summary>The ceiling <c>sub_50FD20</c> clamps the published density to.</summary>
    public const float MaximumDensity = 16384f;

    /// <summary>Gets the fog a frame draws with when the level asks for none.</summary>
    public static SceneFog None => new(default, 0f);

    /// <summary>Gets a value indicating whether anything is fogged.</summary>
    public bool IsEnabled => DensityPerMetre > 0f;

    /// <summary>Turns a level's <c>Fog Amount</c> into the extinction per metre the shader wants.</summary>
    /// <param name="amount">The <c>FlatSky</c> task's <c>Fog Amount</c>.</param>
    /// <param name="unitsPerMetre">Game units to the metre; the world's own 4096.</param>
    /// <returns>The extinction per metre, never negative.</returns>
    /// <remarks>
    /// <c>amount × 0.0005</c> is the density the original publishes, clamped to
    /// <see cref="MaximumDensity"/> as <c>sub_50FD20</c> clamps it; the <c>0.001</c> and the unit scale
    /// are what turn it from per-game-unit into per-metre.
    /// </remarks>
    public static float DensityFor(float amount, float unitsPerMetre = 4096f)
    {
        if (!(amount > 0f))
        {
            return 0f;
        }

        float density = MathF.Min(amount * AmountScale, MaximumDensity);
        return density * DistanceScale * unitsPerMetre;
    }

    /// <summary>Builds the fog a level's <c>FlatSky</c> asks for.</summary>
    /// <param name="colour">The task's <c>Fog Color</c>, which the original clamps to <c>[0, 1]</c>.</param>
    /// <param name="amount">The task's <c>Fog Amount</c>.</param>
    /// <param name="unitsPerMetre">Game units to the metre.</param>
    /// <returns>The fog.</returns>
    public static SceneFog FromFlatSky(Vector3f colour, float amount, float unitsPerMetre = 4096f) => new(
        new Vector3f(
            Math.Clamp(colour.X, 0f, 1f),
            Math.Clamp(colour.Y, 0f, 1f),
            Math.Clamp(colour.Z, 0f, 1f)),
        DensityFor(amount, unitsPerMetre));

    /// <summary>Gets the colour a draw of this blend fogs towards.</summary>
    /// <param name="blend">The blend the draw is submitted with.</param>
    /// <returns>The level's colour for an opaque or translucent draw, black for an additive one.</returns>
    /// <remarks>
    /// An additive pass must add nothing at full fog or the ground would be fogged once per material
    /// pass; black is what makes the passes sum to one fogged surface. <c>sub_5144E0</c> sets exactly
    /// this, blend by blend, and it is why the terrain's several passes come out fogged like one.
    /// </remarks>
    public Vector3f ColourFor(BlendMode blend) => blend == BlendMode.Additive ? default : Colour;

    /// <summary>Gets how much of a surface survives at a view depth.</summary>
    /// <param name="viewDepthMetres">The depth along the view axis, in metres.</param>
    /// <returns>One at the eye, falling to zero with distance.</returns>
    public float FactorAt(float viewDepthMetres) => IsEnabled
        ? Math.Clamp(MathF.Exp(-DensityPerMetre * MathF.Max(viewDepthMetres, 0f)), 0f, 1f)
        : 1f;
}
