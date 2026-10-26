namespace OpenIGI.Game.Igi2.Animation;

/// <summary>
/// A bone object whose face can be moved, <c>BoneDynCubeObj_s.avMorphWeights</c>: a status message opens the mouth of each one it
/// names (the first channel) and a figure blinks (the second).
/// </summary>
public interface IIgi2MorphTargets
{
    /// <summary>Gets the weights of the face's morph channels, sixteen of them.</summary>
    float[] MorphWeights { get; }
}

/// <summary>What the morph weights are drawn with.</summary>
public static class Igi2MorphWeights
{
    /// <summary>The mouth's channel, which a status message moves by its sound's lip sync.</summary>
    public const int Mouth = 0;

    /// <summary>The eyelids' channel, which a figure moves as it blinks.</summary>
    public const int Eyelids = 1;

    /// <summary>The weights as a frame draws them: a copy, or none when no channel moves, as if the face had no morphs.</summary>
    /// <param name="weights">The weights.</param>
    /// <returns>The copy, or <see langword="null"/>.</returns>
    public static float[]? Drawn(float[] weights)
    {
        ArgumentNullException.ThrowIfNull(weights);
        return Array.Exists(weights, static w => w != 0f) ? [.. weights] : null;
    }
}
