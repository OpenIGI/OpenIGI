namespace OpenIGI.Game.Weapons;

/// <summary>
/// How a tracer is coloured and faded along its length, as <c>0x47ABD0</c> draws it.
/// </summary>
/// <remarks>
/// <para>
/// This is what the six colour components of an <see cref="AmmoType"/> are for. Each live round has a
/// head and a tail parameter along the shot; the colour is interpolated from
/// <see cref="AmmoType.NearColour"/> towards <see cref="AmmoType.FarColour"/> by that parameter and
/// clamped to 0–1 per component. So a 9×19 round runs amber at the muzzle to yellow at the tip.
/// </para>
/// <para>
/// The brightness then falls off with distance and is floored, so a tracer never disappears entirely.
/// </para>
/// </remarks>
public static class AmmoTracer
{
    /// <summary>The distance at which the fade reaches its full extent, in world units.</summary>
    /// <remarks>2048000 units is 500 metres.</remarks>
    public const float MaximumDistance = 2048000f;

    /// <summary>The reciprocal of <see cref="MaximumDistance"/>, as the original spells it.</summary>
    public const float DistanceScale = 0.00000048828127f;

    /// <summary>How much of the brightness the distance fade can take.</summary>
    /// <remarks>
    /// Only a tenth: at the full 500 metres a tracer is still at 90% of its intensity before the floor
    /// applies, so this is a very gentle fade.
    /// </remarks>
    public const float FadeStrength = 0.1f;

    /// <summary>The dimmest a tracer is ever drawn.</summary>
    public const float MinimumIntensity = 0.2f;

    /// <summary>Interpolates a tracer's colour at a point along the shot.</summary>
    /// <param name="ammunition">The ammunition being fired.</param>
    /// <param name="t">
    /// How far along the shot, as the original computes it — a distance over the round's total length.
    /// Not clamped on the way in; each resulting component is clamped instead.
    /// </param>
    /// <returns>The colour, each component in 0–1.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="ammunition"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The original recomputes the near colour and the delta per component per round rather than
    /// lifting them out of the loop, and clamps <i>after</i> interpolating, so a <paramref name="t"/>
    /// outside 0–1 saturates rather than extrapolating.
    /// </remarks>
    public static (float Red, float Green, float Blue) ColourAt(AmmoType ammunition, float t)
    {
        ArgumentNullException.ThrowIfNull(ammunition);

        TracerColour near = ammunition.NearColour;
        TracerColour far = ammunition.FarColour;

        float red = near.RedFraction + (t * ((far.Red - near.Red) * TracerColour.ComponentScale));
        float green = near.GreenFraction + (t * ((far.Green - near.Green) * TracerColour.ComponentScale));
        float blue = near.BlueFraction + (t * ((far.Blue - near.Blue) * TracerColour.ComponentScale));

        return (Clamp(red), Clamp(green), Clamp(blue));
    }

    /// <summary>Computes the brightness a tracer is drawn at, given how far away it is.</summary>
    /// <param name="baseIntensity">The round's own intensity before the fade.</param>
    /// <param name="distanceInUnits">The distance, in world units.</param>
    /// <returns>The faded intensity, never below <see cref="MinimumIntensity"/>.</returns>
    /// <remarks>
    /// The distance is clamped to <see cref="MaximumDistance"/> first, and the floor is applied with
    /// <c>&lt;=</c> rather than <c>&lt;</c> — which makes no difference to the value but is what the
    /// original writes.
    /// </remarks>
    public static float IntensityAt(float baseIntensity, float distanceInUnits)
    {
        float distance = distanceInUnits < 0f ? 0f
            : distanceInUnits > MaximumDistance ? MaximumDistance
            : distanceInUnits;

        float intensity = (1f - (distance * DistanceScale * FadeStrength)) * baseIntensity;
        return intensity <= MinimumIntensity ? MinimumIntensity : intensity;
    }

    private static float Clamp(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
}
