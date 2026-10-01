using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering;

/// <summary>What a night sky holds that the atmosphere's scattering does not.</summary>
/// <remarks>
/// <para>
/// A clear moonless sky is not black. Most of its light is airglow, the upper atmosphere's own faint
/// emission, which is brightest a little above the horizon, where the eye looks through the most of that
/// layer. Starlight adds a little and the moon a great deal more (Jensen et al., "A Physically-Based Night
/// Sky Model", SIGGRAPH 2001). The atmosphere march only knows about a single source it scatters, so it
/// supplies the moonlit blue and none of the rest. This record carries the rest.
/// </para>
/// <para>
/// All radiances are in the atmosphere's own units: the scale its source illuminance is written in, before
/// the frame's light scale. That keeps them in proportion to the moonlit sky they are drawn over, whatever
/// the exposure does to both.
/// </para>
/// </remarks>
/// <param name="Airglow">The zenith radiance of the airglow floor; nought by day.</param>
/// <param name="CloudCover">How overcast the sky is, 0 to 1, as far as the night sky is concerned.</param>
/// <param name="StarRadiance">How bright the brightest star is drawn; nought hides the star field.</param>
/// <param name="MoonDirection">The world direction towards the moon, which may be under the horizon.</param>
/// <param name="MoonRadiance">How bright the moon's disc is drawn; nought hides it and its halo.</param>
/// <param name="Seconds">The game's running clock, which the stars twinkle on.</param>
public readonly record struct NightSky(
    float Airglow,
    float CloudCover,
    float StarRadiance,
    Vector3f MoonDirection,
    float MoonRadiance,
    float Seconds = 0f)
{
    /// <summary>Gets the sky that holds nothing beyond the atmosphere: a day's.</summary>
    public static NightSky None => default;
}
