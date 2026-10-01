using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Player;

/// <summary>
/// The slide a slope imposes on a human standing on it.
/// </summary>
/// <remarks>
/// <para>
/// From the free-movement integrator (<c>0x4614B0</c>). The ground normal gives a slope angle, and four
/// bands decide how hard the human is pushed down it. The two steep bands <b>set</b> the accumulator
/// outright — a cliff face gives you no say — while the two shallow ones <b>add</b> to it, so a gentle
/// slope builds up drift over several ticks instead of snapping.
/// </para>
/// <para>
/// The four speeds are hard-coded constants. The two shallow ones are <b>exactly</b> 1.5 and 0.5 km/h
/// through the same 37.9259 units-per-tick factor <see cref="HumanPlayerParameters"/> uses — and the
/// shallowest is bit-for-bit the value general argument 3 converts to. The two steep ones are round
/// numbers of <em>units</em> rather than of km/h: 948 and 1137 work out at 25.00 and 29.98 km/h.
/// </para>
/// </remarks>
public static class HumanSlopeSlide
{
    /// <summary>Slide speed above <see cref="VerySteepDegrees"/>, in units per tick (29.98 km/h).</summary>
    public const float VerySteepSpeed = 1137.0f;

    /// <summary>Slide speed above <see cref="SteepDegrees"/>, in units per tick (25.00 km/h).</summary>
    public const float SteepSpeed = 948.0f;

    /// <summary>Slide added above <see cref="ModerateDegrees"/>, in units per tick (1.5 km/h).</summary>
    public const float ModerateSpeed = 56.888889f;

    /// <summary>Slide added above <see cref="ShallowDegrees"/>, in units per tick (0.5 km/h).</summary>
    public const float ShallowSpeed = 18.962963f;

    /// <summary>Above this angle the slide replaces the accumulator at <see cref="VerySteepSpeed"/>.</summary>
    public const float VerySteepDegrees = 80f;

    /// <summary>Above this angle the slide replaces the accumulator at <see cref="SteepSpeed"/>.</summary>
    public const float SteepDegrees = 60f;

    /// <summary>Above this angle <see cref="ModerateSpeed"/> is added each tick.</summary>
    public const float ModerateDegrees = 50f;

    /// <summary>Above this angle <see cref="ShallowSpeed"/> is added each tick. Below it, nothing slides.</summary>
    public const float ShallowDegrees = 45f;

    /// <summary>The per-tick damping applied to the slide accumulator.</summary>
    public const float Damping = 0.89999998f;

    /// <summary>Converts a ground normal to the slope angle the bands are compared against.</summary>
    /// <param name="normal">The ground normal; need not be unit length.</param>
    /// <returns>The angle in degrees, <c>0</c> for flat ground and <c>90</c> for a vertical face.</returns>
    /// <remarks>
    /// The original is <c>90 - asin(normal.z) * 57.295776</c>, using a helper (<c>0x4B27D0</c>) that
    /// <b>clamps its argument to [-1, 1] before the asin</b> — so a denormalised normal cannot produce
    /// a NaN. It is <c>asin</c>, not <c>acos</c>: flat ground has to come out at zero degrees, and the
    /// two differ by exactly the sign of the whole scale.
    /// </remarks>
    public static float SlopeDegrees(Vector3f normal)
    {
        float length = MathF.Sqrt((normal.X * normal.X) + (normal.Y * normal.Y) + (normal.Z * normal.Z));
        if (length == 0f)
        {
            return 90f;
        }

        float z = Math.Clamp(normal.Z / length, -1f, 1f);
        return 90f - (MathF.Asin(z) * 57.295776f);
    }

    /// <summary>Advances a slide accumulator by one tick.</summary>
    /// <param name="accumulator">The current slide, which the caller keeps between ticks.</param>
    /// <param name="normal">The ground normal, or <see langword="null"/> when the human is not grounded.</param>
    /// <returns>The new accumulator, before damping.</returns>
    /// <remarks>
    /// A human with no ground has its accumulator cleared outright — sliding is a property of standing
    /// on something, not of falling.
    /// </remarks>
    public static Vector3f Accumulate(Vector3f accumulator, Vector3f? normal)
    {
        if (normal is not Vector3f ground)
        {
            return default;
        }

        float length = MathF.Sqrt((ground.X * ground.X) + (ground.Y * ground.Y) + (ground.Z * ground.Z));
        Vector3f unit = length == 0f
            ? ground
            : new Vector3f(ground.X / length, ground.Y / length, ground.Z / length);

        float slope = SlopeDegrees(ground);

        // The steep bands replace; the shallow ones add. Note the Z component is left alone in every
        // case — the slide is horizontal, and gravity is somebody else's job.
        if (slope > VerySteepDegrees)
        {
            return new Vector3f(unit.X * VerySteepSpeed, unit.Y * VerySteepSpeed, 0f);
        }

        if (slope > SteepDegrees)
        {
            return new Vector3f(unit.X * SteepSpeed, unit.Y * SteepSpeed, 0f);
        }

        if (slope > ModerateDegrees)
        {
            return new Vector3f(
                accumulator.X + (unit.X * ModerateSpeed),
                accumulator.Y + (unit.Y * ModerateSpeed),
                accumulator.Z);
        }

        if (slope > ShallowDegrees)
        {
            return new Vector3f(
                accumulator.X + (unit.X * ShallowSpeed),
                accumulator.Y + (unit.Y * ShallowSpeed),
                accumulator.Z);
        }

        return default;
    }

    /// <summary>Applies the per-tick damping.</summary>
    /// <param name="accumulator">The accumulated slide.</param>
    /// <returns>The damped value.</returns>
    public static Vector3f Damp(Vector3f accumulator) =>
        new(accumulator.X * Damping, accumulator.Y * Damping, accumulator.Z * Damping);
}
