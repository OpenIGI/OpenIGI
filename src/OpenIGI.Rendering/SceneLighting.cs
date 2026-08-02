using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering;

/// <summary>
/// The light the whole scene is lit by, for one frame.
/// </summary>
/// <remarks>
/// <para>
/// A property of the frame rather than of any draw, so it travels as one value set once rather than as
/// device state that could be left behind.
/// </para>
/// <para>
/// <b><see cref="SunDirection"/> is in view space</b>, pointing <em>towards</em> the sun, because the
/// G-buffer's normals are. The shell rotates the level's world-space sun direction by the camera's
/// orientation before setting it — the alternative, storing world normals, would cost the G-buffer
/// precision for no gain when every consumer is a screen-space pass.
/// </para>
/// </remarks>
/// <param name="Model">Where surfaces take their light from.</param>
/// <param name="SunDirection">The view-space direction towards the sun. Expected to be unit length.</param>
/// <param name="SunColour">The sun's radiance. Larger than one is meaningful; the tone map handles it.</param>
/// <param name="AmbientColour">
/// A flat stand-in for bounced light, used only by <see cref="LightingModel.Dynamic"/> until the probe
/// volume of stage R7 replaces it with something directional.
/// </param>
/// <param name="Scale">
/// Scales the whole dynamically-computed irradiance, and is ignored under
/// <see cref="LightingModel.Baked"/>, which has to reproduce the original exactly.
/// <para>
/// It exists because this game's diffuse textures are not albedo. They were painted to be multiplied
/// by a lightmap that averages well below one, so they already carry lighting; treating them as albedo
/// and lighting them at unit irradiance counts that painted-in brightness a second time. The error is
/// proportional to how bright the texture already was, which is why it shows up first on sand and snow
/// and hardly at all on concrete.
/// </para>
/// </param>
public readonly record struct SceneLighting(
    LightingModel Model,
    Vector3f SunDirection,
    Vector3f SunColour,
    Vector3f AmbientColour,
    float Scale = 1f)
{
    /// <summary>
    /// Gets the lighting that reproduces the original: the bakes and nothing else.
    /// </summary>
    /// <remarks>
    /// The sun is black rather than absent, so that a caller which forgets to set the model still
    /// gets the original's picture instead of a scene lit by an accidental default.
    /// </remarks>
    public static SceneLighting Baked { get; } = new(
        LightingModel.Baked,
        new Vector3f(0f, 0f, 1f),
        new Vector3f(0f, 0f, 0f),
        new Vector3f(0f, 0f, 0f));
}
