using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering.OpenGL;

/// <summary>
/// One real-time local light handed to the deferred lighting for a single frame — a lamp fixture a
/// level's emissive geometry implies, treated as a punctual source.
/// </summary>
/// <remarks>
/// <para>
/// I.G.I. declares no point lights at all; these are inferred from where a level's emissive textures are
/// drawn (see the game layer's light-fixture proposer). This struct is the renderer-facing form of one:
/// the position is already in <b>view space</b>, the frame the lighting pass shades in, so the game layer
/// does the world-to-view transform in double precision — the level sits at large world coordinates and
/// subtracting the camera in float first would cost most of the mantissa.
/// </para>
/// <para>
/// The set is uploaded whole each frame and cleared after the pass, so a frame that submits none lights
/// from none. <see cref="Maximum"/> is the shader's array size; a longer list is truncated.
/// </para>
/// </remarks>
/// <param name="ViewPosition">The light's position in view space, in metres.</param>
/// <param name="Radius">How far the light reaches before its windowed falloff takes it to zero, in metres.</param>
/// <param name="Colour">The light's linear colour, its brightest channel around one.</param>
/// <param name="Intensity">The light's relative strength, already scaled by whatever global knob the caller applies.</param>
public readonly record struct DeferredLocalLight(
    Vector3f ViewPosition,
    float Radius,
    Vector3f Colour,
    float Intensity)
{
    /// <summary>The most local lights a single frame can carry, matching the lighting shader's array size.</summary>
    public const int Maximum = 32;
}
