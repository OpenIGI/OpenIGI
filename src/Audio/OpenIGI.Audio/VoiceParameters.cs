using OpenIGI.Core.Numerics;

namespace OpenIGI.Audio;

/// <summary>Everything the device needs to know about one playing sound.</summary>
/// <remarks>
/// <para>
/// Distances are in <b>metres</b>, matching <see cref="AudioListenerState"/>. The original stores them
/// in metres too and multiplies by 4096 in the voice update to compare against engine-unit positions;
/// keeping both sides in metres removes the conversion without changing any ratio, because every use of
/// the two distances in <c>0x496E60</c> is a ratio.
/// </para>
/// <para>
/// A voice is either positional or not. A positional voice takes <see cref="Position"/> and the two
/// distances and has its gain and pan derived; a flat one takes <see cref="Pan"/> as given. The original
/// draws the same line with two flag bits on the voice record — <c>0x2</c> selects positional and
/// <c>0x4</c> suppresses panning altogether.
/// </para>
/// </remarks>
public readonly record struct VoiceParameters
{
    /// <summary>Gets the bus this voice mixes into.</summary>
    public SoundCategory Category { get; init; }

    /// <summary>Gets the voice's own linear gain before category and distance are applied, zero to one.</summary>
    public float Gain { get; init; }

    /// <summary>Gets the playback rate multiplier. The original clamps this to 0.5 – 2.0.</summary>
    public float Pitch { get; init; }

    /// <summary>Gets the pan for a non-positional voice, −1 hard left to +1 hard right.</summary>
    public float Pan { get; init; }

    /// <summary>Gets a value indicating whether gain and pan are derived from <see cref="Position"/>.</summary>
    public bool IsPositional { get; init; }

    /// <summary>Gets the source position in metres.</summary>
    public Vector3d Position { get; init; }

    /// <summary>Gets the source velocity in metres per second, used only when doppler is enabled.</summary>
    public Vector3f Velocity { get; init; }

    /// <summary>Gets the distance in metres within which the voice plays at full gain.</summary>
    public float MinimumDistance { get; init; }

    /// <summary>Gets the distance in metres beyond which the voice is silent.</summary>
    public float MaximumDistance { get; init; }

    /// <summary>Gets a value indicating whether the voice repeats when it reaches the end.</summary>
    public bool Looping { get; init; }

    /// <summary>Gets the voice's importance when the pool is full; a higher value survives.</summary>
    /// <remarks>
    /// The original has a fixed pool of 32 and no explicit priority — a request that finds no free voice
    /// simply does not play. Priority is a divergence, and it buys the thing the fixed pool costs: a
    /// firefight full of distant rifle cracks can no longer silence the player's own weapon.
    /// </remarks>
    public int Priority { get; init; }

    /// <summary>Gets a set of sensible defaults: a flat, centred, non-looping effect at full gain.</summary>
    public static VoiceParameters Default => new()
    {
        Category = SoundCategory.Effects,
        Gain = 1f,
        Pitch = 1f,
        Pan = 0f,
        IsPositional = false,
        Position = Vector3d.Zero,
        Velocity = default,
        MinimumDistance = 1f,
        MaximumDistance = 50f,
        Looping = false,
        Priority = 0,
    };

    /// <summary>Returns these parameters with the position and distance range of a positional voice.</summary>
    /// <param name="position">The source position in metres.</param>
    /// <param name="minimumDistance">The distance within which gain is not reduced.</param>
    /// <param name="maximumDistance">The distance beyond which the voice is silent.</param>
    /// <returns>The positional parameters.</returns>
    public VoiceParameters At(Vector3d position, float minimumDistance, float maximumDistance) => this with
    {
        IsPositional = true,
        Position = position,
        MinimumDistance = minimumDistance,
        MaximumDistance = maximumDistance,
    };
}
