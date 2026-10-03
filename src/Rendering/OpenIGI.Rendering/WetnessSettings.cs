namespace OpenIGI.Rendering;

/// <summary>
/// How wet the world is: what the rain has done to the ground, as opposed to the rain itself.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is not part of the storm.</b> Falling drops are particles and belong to whoever draws
/// particles. Standing water is a property of every surface in the frame — it darkens albedo, collapses
/// roughness, flattens the normal and puts the sky in the floor — and none of that can be done by
/// drawing anything on top. It has to happen where the surface is shaded, so it arrives at the device
/// as its own state and is applied in the deferred lighting pass to whatever the G-buffer holds.
/// </para>
/// <para>
/// <b>Applied at shading, not in the G-buffer.</b> The obvious place is the G-buffer fragment shader,
/// which is where a surface's material is decided. It cannot go there: that shader has no world
/// position and no room in its push constants to be given one, and a puddle is a function of world XY
/// before it is a function of anything else. The lighting pass already reconstructs world position from
/// depth for the probe lookup, so it gets the puddles for free. The cost of that choice is that passes
/// reading the G-buffer normal — the gather, the temporal filter — do not see the ripples; what they
/// see is the dry surface, which is stable, and it is the specular response that sells wet ground
/// anyway.
/// </para>
/// <para>
/// <b>Everything is in metres and seconds</b>, matching <see cref="PrecipitationSettings"/> rather than
/// the world's own units, and converted once where it is used.
/// </para>
/// </remarks>
/// <param name="Amount">
/// How wet the world is, 0 dry to 1 soaked. Scales every other effect here, so a single number turns
/// the whole thing off and a storm can bring it up as it settles in.
/// </param>
/// <param name="PuddleCoverage">
/// What share of level ground stands under water, 0 to 1. Not a multiplier on the wetness: a road can
/// be soaked and hold no puddles at all, and one that is merely damp can still have every hollow full.
/// </param>
/// <param name="PuddleSizeMetres">
/// How far across one hollow in the ground is. The puddle mask is a height field at this scale, and
/// water fills what is below the level <see cref="PuddleCoverage"/> sets.
/// </param>
/// <param name="RippleStrength">
/// How hard the rain strikes standing water, 0 to 1. This is the rain's own intensity rather than a
/// look control: it decides how many of the staggered ripple layers are running, so a drizzle rings a
/// few slow circles and a downpour boils.
/// </param>
/// <param name="RippleSizeMetres">The spacing of the ripple lattice: how far apart drops strike.</param>
/// <param name="WindRippleStrength">
/// How much the wind textures the water, 0 to 1. Separate from the rain's ripples because it outlasts
/// them — a puddle goes on being ruffled after the shower has passed, and water that is glass-flat the
/// instant the rain stops reads as a mirror lying in the road.
/// </param>
/// <param name="Seconds">
/// Elapsed seconds, which the ripples and the wind are animated from. Supplied rather than read from a
/// clock inside the shader so that a captured frame is reproducible.
/// </param>
public readonly record struct WetnessSettings(
    float Amount,
    float PuddleCoverage,
    float PuddleSizeMetres,
    float RippleStrength,
    float RippleSizeMetres,
    float WindRippleStrength,
    float Seconds)
{
    /// <summary>A dry world: what a level with no weather submits, and what every backend starts at.</summary>
    public static WetnessSettings Dry { get; }

    /// <summary>A street in steady rain: what the storm brings the ground up to at full intensity.</summary>
    /// <remarks>
    /// <para>
    /// Soaked but not drowned. The wetness stops short of one so that the damp darkening and the puddle
    /// mask keep some headroom, and a little under half of the level ground stands under water, which is
    /// enough for the yard to read as flooded in patches without turning into a lake.
    /// </para>
    /// <para>
    /// The ripple lattice is half a metre, which puts a few strikes in every puddle a stride across and
    /// keeps each ring big enough to see from standing height. The wind's ruffle is kept low: it is the
    /// texture a puddle keeps between strikes, not a wave pattern of its own.
    /// </para>
    /// </remarks>
    public static WetnessSettings Storm { get; } = new(
        Amount: 0.7f,
        PuddleCoverage: 0.45f,
        PuddleSizeMetres: 3.2f,
        RippleStrength: 1f,
        RippleSizeMetres: 0.5f,
        WindRippleStrength: 0.25f,
        Seconds: 0f);

    /// <summary>Gets whether anything would be drawn differently because of this.</summary>
    public bool IsWet => Amount > 0.001f;

    /// <summary>Returns these settings scaled towards dry by a storm's intensity.</summary>
    /// <param name="intensity">How hard it is raining, 0 to 1.</param>
    /// <param name="seconds">Elapsed seconds for the animation.</param>
    /// <returns>The scaled settings.</returns>
    /// <remarks>
    /// The coverage and the ripples scale, the puddle and ripple sizes do not: a lighter shower makes
    /// smaller puddles and quieter water, it does not make the hollows in the road move further apart.
    /// </remarks>
    public WetnessSettings AtIntensity(float intensity, float seconds)
    {
        float clamped = Math.Clamp(intensity, 0f, 1f);
        return this with
        {
            Amount = Amount * clamped,
            PuddleCoverage = PuddleCoverage * clamped,
            RippleStrength = RippleStrength * clamped,
            Seconds = seconds,
        };
    }
}
