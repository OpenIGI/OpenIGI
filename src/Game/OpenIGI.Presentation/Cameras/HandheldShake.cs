using OpenIGI.Core.Numerics;

namespace OpenIGI.Presentation.Cameras;

/// <summary>
/// The small, slow wander of a camera somebody is holding rather than bolting down.
/// </summary>
/// <remarks>
/// <para>
/// Two octaves of smoothed value noise per axis, read out of one table drawn at construction. Smoothed
/// rather than linear because linear value noise has a corner at every lattice point, and a camera that
/// changes direction on a corner reads as a mechanism ticking rather than as a person breathing. The
/// second octave is at <b>2.37</b> times the first, not twice it: an integer ratio makes both octaves
/// return to phase together on a common period, and the sum acquires an audible beat.
/// </para>
/// <para>
/// The amplitudes are deliberately tiny — a fifth to a quarter of a degree. This is a long lens on a
/// braced operator, not a chase camera. It is expressed as <em>rotation only</em>, with no positional
/// term, because at the altitude the mission entry holds at, the subject is a hundred metres or more
/// away and an angular disturbance already moves the picture by metres; adding a positional wander would
/// double-count it and would have to be retuned for every altitude, where an angular one is correct at
/// all of them by construction.
/// </para>
/// <para>
/// It is a pure function of a time and a weight, holds no state between samples, and allocates nothing —
/// so the same second of a sequence looks the same every time it is played, which is what lets a
/// <c>-lockstep</c> capture of the entry be compared frame for frame.
/// </para>
/// </remarks>
public sealed class HandheldShake
{
    /// <summary>How many noise samples the table holds; a power of two so the index masks.</summary>
    private const int TableSize = 64;

    /// <summary>The base rate of each axis, in cycles per second.</summary>
    private const float YawFrequency = 0.55f;
    private const float PitchFrequency = 0.47f;
    private const float RollFrequency = 0.23f;

    /// <summary>The widest each axis wanders, in radians.</summary>
    private const float YawAmplitude = 0.0045f;    // about 0.26 degrees
    private const float PitchAmplitude = 0.0035f;  // about 0.20 degrees
    private const float RollAmplitude = 0.0030f;   // about 0.17 degrees

    /// <summary>The second octave's rate relative to the first, and how much of it is mixed in.</summary>
    private const float OctaveRatio = 2.37f;
    private const float OctaveWeight = 0.35f;

    private readonly float[] _noise = new float[TableSize];

    /// <summary>Initializes a new instance of the <see cref="HandheldShake"/> class.</summary>
    /// <param name="seed">The seed for the noise table.</param>
    /// <remarks>
    /// Seeded rather than time-seeded, through the engine's own generator, so a capture of the same
    /// sequence is reproducible.
    /// </remarks>
    public HandheldShake(uint seed = 0x16114D)
    {
        var random = new IgiRandom(seed);
        for (int i = 0; i < _noise.Length; i++)
        {
            _noise[i] = (random.NextSingle() * 2f) - 1f;
        }
    }

    /// <summary>Gets the widest the yaw wanders, in radians.</summary>
    public static float MaximumYaw => YawAmplitude;

    /// <summary>Gets the widest the pitch wanders, in radians.</summary>
    public static float MaximumPitch => PitchAmplitude;

    /// <summary>Gets the widest the roll wanders, in radians.</summary>
    public static float MaximumRoll => RollAmplitude;

    /// <summary>Samples the wander at a moment.</summary>
    /// <param name="seconds">How far into the shot this is.</param>
    /// <param name="weight">How much of the wander applies, 0 none to 1 all.</param>
    /// <param name="yaw">Receives the yaw offset in radians.</param>
    /// <param name="pitch">Receives the pitch offset in radians.</param>
    /// <param name="roll">Receives the roll offset in radians.</param>
    /// <remarks>
    /// At a weight of exactly zero every output is exactly zero, which is what lets a shot hand the view
    /// over to the player without the last frame differing from the first by a fraction of a degree.
    /// </remarks>
    public void Sample(float seconds, float weight, out float yaw, out float pitch, out float roll)
    {
        if (weight <= 0f)
        {
            yaw = 0f;
            pitch = 0f;
            roll = 0f;
            return;
        }

        float scale = Math.Clamp(weight, 0f, 1f);
        yaw = Channel(seconds, YawFrequency, 0f) * YawAmplitude * scale;
        pitch = Channel(seconds, PitchFrequency, 17.3f) * PitchAmplitude * scale;
        roll = Channel(seconds, RollFrequency, 41.7f) * RollAmplitude * scale;
    }

    /// <summary>Rolls an orientation about the direction it is already looking.</summary>
    /// <param name="look">The orientation to roll.</param>
    /// <param name="roll">The roll in radians.</param>
    /// <returns>The rolled orientation.</returns>
    /// <remarks>
    /// <see cref="OpenIGI.Core.Graphics.Camera3D.LookRotation"/> cannot express a roll — it is built from a yaw and a
    /// pitch alone — but a roll is exactly a rotation about the view's forward axis, and in this
    /// convention the orientation's rows <em>are</em> the view axes: right, up, forward. So it mixes the
    /// first two rows and leaves the third alone. Six lines, and it is most of the difference between a
    /// camera that is shaking and a camera somebody is holding.
    /// <para>
    /// A roll of exactly zero returns the orientation unchanged rather than multiplying by an identity
    /// built from a cosine and a sine, so a shot that has ramped its shake out ends bit-identical to the
    /// pose it was aiming at.
    /// </para>
    /// </remarks>
    public static Matrix3f ApplyRoll(in Matrix3f look, float roll)
    {
        if (roll == 0f)
        {
            return look;
        }

        float c = MathF.Cos(roll);
        float s = MathF.Sin(roll);
        Vector3f right = look.Row1;
        Vector3f up = look.Row2;

        return Matrix3f.FromRows(
            new Vector3f((right.X * c) + (up.X * s), (right.Y * c) + (up.Y * s), (right.Z * c) + (up.Z * s)),
            new Vector3f((up.X * c) - (right.X * s), (up.Y * c) - (right.Y * s), (up.Z * c) - (right.Z * s)),
            look.Row3);
    }

    /// <summary>One axis: two octaves of smoothed value noise, normalised back to roughly [-1, 1].</summary>
    private float Channel(float seconds, float frequency, float phase) =>
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

        // Smoothstep the interpolant, not the values: this is what removes the slope discontinuity at
        // each lattice point that makes plain linear value noise read as mechanical.
        float w = u * u * (3f - (2f * u));
        return a + ((b - a) * w);
    }
}
