using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering;

/// <summary>
/// The player's flashlight for one frame: a spot light hung off the body, in VIEW space like the lamps.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not retail.</b> The original has no flashlight; this is the remaster's, and every graphics preset but
/// vanilla draws it. It is not a lamp slot: the lamps share one traced occlusion ratio, drawn from reservoirs
/// and averaged over thirty-two frames, and a bright light glued to the camera would take over the
/// reservoirs, ghost through the history and change every other lamp's shadow. It is shaded on its own, with
/// a shadow of its own that keeps no history -- the light moves with the view, so nothing it lit last frame
/// is where it is now.
/// </para>
/// <para>
/// The beam is a torch's rather than a cone's: a hot core, a body that falls away to the reflector's edge, a
/// faint ring where the reflector ends, and a dim spill beyond it (see <c>Common/Flashlight.slang</c>). The
/// angles are half-angles from the axis.
/// </para>
/// </remarks>
/// <param name="ViewPosition">Where the lens is, in view space, in metres.</param>
/// <param name="ViewAxis">The way the beam points, in view space; unit length.</param>
/// <param name="ViewUp">The reflector's up, in view space, which turns the lens's small irregularities with it.</param>
/// <param name="Colour">The light's linear colour, luminance one.</param>
/// <param name="Intensity">How strong the beam is on its axis: irradiance at one metre, in the lamps' units.</param>
/// <param name="Range">How far the beam reaches before its windowed falloff takes it to nothing, in metres.</param>
/// <param name="InnerDegrees">The hot core's half-angle.</param>
/// <param name="OuterDegrees">The main beam's half-angle, where the reflector's light ends.</param>
/// <param name="SpillDegrees">How far the dim spill reaches past it.</param>
/// <param name="SpillLevel">How bright the spill is, against the core.</param>
/// <param name="LensRadius">How wide the emitter is, in metres, which is what gives a shadow its penumbra.</param>
/// <param name="NearCore">
/// The distance inside which the falloff stops rising, in metres: a torch pressed to a wall lights a disc,
/// not a point of infinite brightness.
/// </param>
/// <param name="Clearance">How far short of the lens a shadow ray stops, so the body holding it never shadows it.</param>
/// <param name="Shadows">Whether the beam casts shadows.</param>
/// <param name="Volumetric">How strongly the beam lights the air it passes through; nought for not at all.</param>
/// <param name="Dust">A thin haze only the beam sees, per metre, so it shows indoors where the fog is clear.</param>
/// <param name="RigFill">What the beam puts on the hands and the gun, in the units the rig's lamp fill is in.</param>
/// <param name="Throw">
/// How far past a few metres a surface's falloff is eased: the distance past the knee raised to this multiplies
/// the inverse square, so nought is the inverse square alone, one leaves one over the distance and two holds the
/// beam level out to its range. The air it lights is never thrown.
/// </param>
public readonly record struct FlashlightLight(
    Vector3f ViewPosition,
    Vector3f ViewAxis,
    Vector3f ViewUp,
    Vector3f Colour,
    float Intensity,
    float Range,
    float InnerDegrees,
    float OuterDegrees,
    float SpillDegrees,
    float SpillLevel,
    float LensRadius,
    float NearCore,
    float Clearance,
    bool Shadows,
    float Volumetric,
    float Dust,
    Vector3f RigFill,
    float Throw = 0f)
{
    /// <summary>Gets a flashlight that is switched off.</summary>
    public static FlashlightLight Off => default;

    /// <summary>Gets whether the beam lights anything at all.</summary>
    public bool IsOn => Range > 0f && Intensity > 0f;
}
