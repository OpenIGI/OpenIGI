using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering;

/// <summary>
/// The participating medium the scene is seen through.
/// </summary>
/// <remarks>
/// <para>
/// The original's fog is a single exponential in view depth, packed into a vertex alpha byte — a
/// distance haze and nothing more. It cannot pool in a valley, it does not know the sun is there, and
/// nothing can cast a shaft through it, because it has no notion of where anything is. This describes
/// a medium instead: something with a density that varies through the world and that scatters the
/// sun's light towards the eye.
/// </para>
/// <para>
/// The faithful <c>exp2(-z·k)</c> path is a separate, later piece of work and is what
/// <c>-vanilla</c> restores; nothing here tries to be it.
/// </para>
/// </remarks>
/// <param name="Density">
/// Extinction per metre at the base height. How quickly the medium swallows what is behind it.
/// </param>
/// <param name="HeightFalloff">
/// The height in metres over which density falls to a third. This is what makes fog <em>pool</em>:
/// without it a medium is uniform and reads as a flat wash rather than as air.
/// </param>
/// <param name="BaseHeight">The world height, in metres, at which <see cref="Density"/> applies.</param>
/// <param name="Albedo">
/// How much of what the medium takes out of a beam it scatters rather than absorbs, per channel. Near
/// white for water droplets; lower and tinted for smoke and dust.
/// </param>
/// <param name="Anisotropy">
/// The Henyey–Greenstein parameter, from −1 to 1. Positive scatters forward, which is why a fog bank
/// glows when you look towards the sun and stays flat when you look away — and is most of what makes
/// volumetric fog read as light rather than as a grey filter. Around 0.7 for atmospheric haze.
/// </param>
/// <param name="Distance">
/// How far, in metres, the medium is marched. Beyond it the cheaper analytic haze takes over, because
/// marching to a 4 km far plane would spend the whole budget on air nobody can resolve.
/// </param>
/// <param name="Steps">How many samples each ray takes. The single biggest cost, and the quality dial.</param>
/// <param name="MultipleScattering">
/// How much of the phase function is spread back towards isotropic, from 0 to 1, standing in for the
/// multiple scattering a single-scattering march does not simulate. Without it the medium is blinding
/// towards the sun and invisible away from it.
/// </param>
/// <param name="UniformFraction">
/// The share of <see cref="Density"/>, from 0 to 1, that does <em>not</em> thin with height. A purely
/// height-banded medium vanishes the moment the camera climbs out of it — which is right for a valley
/// seen from a ridge, and wrong for the ridge itself, because real air hazes the distance at altitude
/// too. This is that floor: the level's own atmosphere keeps its depth wherever the player stands,
/// while the banded remainder still pools in the low ground.
/// </param>
/// <param name="SunIntensity">A multiplier on the sun's contribution to in-scattering.</param>
/// <param name="AmbientIntensity">A multiplier on the sky's contribution.</param>
/// <param name="Dust">
/// The density, per metre, of the fine dust out of doors near the eye: it scatters the sun and the lamps
/// and nothing else, and is what a shaft of light or a floodlight's cone is seen in.
/// </param>
/// <param name="DustIndoor">The same dust's density indoors, where the medium itself is nought.</param>
public readonly record struct VolumetricFogSettings(
    float Density = 0.014f,
    float HeightFalloff = 24f,
    float BaseHeight = 0f,
    Vector3f Albedo = default,
    float Anisotropy = 0.7f,
    float Distance = 500f,
    int Steps = 32,
    float MultipleScattering = 0.35f,
    float UniformFraction = 0.12f,
    float SunIntensity = 1f,
    float AmbientIntensity = 1f,
    float Dust = 0f,
    float DustIndoor = 0f)
{
    /// <summary>Gets settings with no fog at all.</summary>
    public static VolumetricFogSettings Off { get; } = new(Density: 0f, Steps: 0);

    /// <summary>Gets a value indicating whether the pass would do anything.</summary>
    public bool Enabled => (Density > 0f || Dust > 0f || DustIndoor > 0f) && Steps > 0 && Distance > 0f;

    /// <summary>Gets the scattering albedo, defaulting to the near-white of water droplets.</summary>
    /// <remarks>
    /// A record struct's default is the zero one, and a zero albedo is a medium that absorbs
    /// everything and scatters nothing — black fog. Reading it through here means an
    /// <c>Albedo</c> nobody set behaves like air rather than like soot.
    /// </remarks>
    public Vector3f ScatteringAlbedo =>
        Albedo.LengthSquared > 0f ? Albedo : new Vector3f(0.92f, 0.94f, 0.97f);
}
