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
/// <param name="CompleteBake">
/// Whether the level's bakes already carry its direct light, so <see cref="LightingModel.Hybrid"/> must
/// not add a sun on top of them. See <c>DeferredLighting.CompleteBake</c>.
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
/// <param name="SkyLightIntensity">
/// How brightly the sky itself lights the world, where one is the sky's own physical brightness.
/// </param>
/// <param name="AtmosphereSunColour">
/// The sun colour and source intensity used to build an atmospheric sky, before <paramref name="Scale"/>.
/// When absent, <paramref name="SunColour"/> is used. Kept separate because calibrating the direct surface
/// response must not also dim the sky and its irradiance harmonics.
/// </param>
/// <param name="SkyColour">
/// The sky as a light source for the volumetric fog -- the level's own ambient, which the fog was
/// calibrated against. Distinct from <see cref="AmbientColour"/> only in that it is NOT scaled by the
/// <c>r_ambient_scale</c> diagnostic, so the haze does not collapse to black when the flat surface ambient
/// is dialled to zero to watch the GI carry the surfaces. Deliberately not the <c>r_sky_light</c>-boosted
/// surface fill: boosting the fog inscatter whites the frame out.
/// </param>
/// <param name="Night">
/// What the night sky holds beyond the atmosphere's own scattering: the airglow floor, the cloud cover,
/// the stars and the moon's disc. <see cref="NightSky.None"/> by day, which leaves the sky exactly as the
/// atmosphere alone draws it.
/// </param>
/// <param name="AtmosphereSunDirection">
/// The WORLD direction towards the sun the atmosphere scatters, when it is not the direct light's. A day
/// cycle hands the direct slot to the moon at night, but the sky still has to be lit by the sun from
/// where it really is -- under the horizon, through twilight -- so the dusk fades rather than cuts.
/// </param>
/// <param name="AtmosphereMoonColour">
/// The moon's light above the atmosphere and any cloud, before <paramref name="Scale"/>, towards
/// <see cref="NightSky.MoonDirection"/>: what the sky's cloud deck is lit by, and, scaled by
/// <paramref name="AtmosphereMoonScattering"/>, what its clear air scatters. Nought by day and wherever the
/// cycle hangs no moon.
/// </param>
/// <param name="AtmosphereMoonScattering">How much of that moon the clear air scatters; one is all of it.</param>
public readonly record struct SceneLighting(
    LightingModel Model,
    Vector3f SunDirection,
    Vector3f SunColour,
    Vector3f AmbientColour,
    float Scale = 1f,
    bool CompleteBake = false,
    Vector3f SkyColour = default,
    float SkyLightIntensity = 1f,
    Vector3f? AtmosphereSunColour = null,
    NightSky Night = default,
    Vector3f? AtmosphereSunDirection = null,
    Vector3f AtmosphereMoonColour = default,
    float AtmosphereMoonScattering = 1f)
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

    /// <summary>Gets the direction towards the sun in world space, for whatever works in the world's.</summary>
    /// <param name="viewToWorld">The camera's rotation from view space into world space.</param>
    /// <returns>The unit direction towards the sun, or straight up when there is no direction to turn.</returns>
    /// <remarks>
    /// <see cref="SunDirection"/> is in view space because the G-buffer's normals are. The atmosphere
    /// and the traced passes are worlds of their own, so they want it turned back, once, rather than
    /// inside everything that reads it.
    /// </remarks>
    public Vector3f WorldSunDirection(in Matrix3f viewToWorld)
    {
        Vector3f view = SunDirection;
        Vector3f world = new(
            (viewToWorld.M11 * view.X) + (viewToWorld.M12 * view.Y) + (viewToWorld.M13 * view.Z),
            (viewToWorld.M21 * view.X) + (viewToWorld.M22 * view.Y) + (viewToWorld.M23 * view.Z),
            (viewToWorld.M31 * view.X) + (viewToWorld.M32 * view.Y) + (viewToWorld.M33 * view.Z));

        float length = MathF.Sqrt((world.X * world.X) + (world.Y * world.Y) + (world.Z * world.Z));

        return length > 1e-5f
            ? new Vector3f(world.X / length, world.Y / length, world.Z / length)
            : new Vector3f(0f, 0f, 1f);
    }
}
