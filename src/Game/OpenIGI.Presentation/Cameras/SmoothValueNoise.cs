using OpenIGI.Core.Numerics;

namespace OpenIGI.Presentation.Cameras;

/// <summary>
/// One table of seeded value noise, read as two smoothed octaves: the continuous wander every shaken camera is built on.
/// </summary>
/// <remarks>
/// <para>
/// Smoothed rather than linear because linear value noise has a corner at every lattice point, and a camera that changes
/// direction on a corner reads as a mechanism ticking rather than as something moving. The second octave is at <b>2.37</b>
/// times the first, not twice it: an integer ratio makes both octaves return to phase together on a common period, and the
/// sum acquires an audible beat.
/// </para>
/// <para>
/// Continuous, not random per frame: a shake drawn from fresh random numbers every frame jitters at the frame rate and reads
/// as noise on the picture, where a continuous signal reads as the camera being moved. Pure in its time argument, so the
/// same second looks the same every time it is played.
/// </para>
/// </remarks>
public sealed class SmoothValueNoise
{
    /// <summary>How many noise samples the table holds; a power of two so the index masks.</summary>
    private const int TableSize = 64;

    /// <summary>The second octave's rate relative to the first, and how much of it is mixed in.</summary>
    private const float OctaveRatio = 2.37f;
    private const float OctaveWeight = 0.35f;

    private readonly float[] _noise = new float[TableSize];

    /// <summary>Initializes a new instance of the <see cref="SmoothValueNoise"/> class.</summary>
    /// <param name="seed">The seed for the table, drawn through the engine's own generator.</param>
    public SmoothValueNoise(uint seed)
    {
        var random = new IgiRandom(seed);
        for (int i = 0; i < _noise.Length; i++)
        {
            _noise[i] = (random.NextSingle() * 2f) - 1f;
        }
    }

    /// <summary>One channel at a moment: two octaves, normalised back to roughly <c>[-1, 1]</c>.</summary>
    /// <param name="seconds">The moment.</param>
    /// <param name="frequency">The channel's base rate, in cycles per second.</param>
    /// <param name="phase">Where in the table the channel reads, so channels at one rate do not move together.</param>
    /// <returns>The channel's value.</returns>
    public float Channel(float seconds, float frequency, float phase) =>
        (Octave(seconds, frequency, phase)
            + (OctaveWeight * Octave(seconds, frequency * OctaveRatio, phase + 11f)))
        / (1f + OctaveWeight);

    private float Octave(float seconds, float frequency, float phase)
    {
        float x = (seconds * frequency) + phase;
        float floor = MathF.Floor(x);
        int i = (int)floor;
        float u = x - floor;

        float a = _noise[((i % TableSize) + TableSize) % TableSize];
        float b = _noise[(((i + 1) % TableSize) + TableSize) % TableSize];

        // Smoothstep the interpolant, not the values: this is what removes the slope discontinuity at each lattice point
        // that makes plain linear value noise read as mechanical.
        float w = u * u * (3f - (2f * u));
        return a + ((b - a) * w);
    }
}
