namespace OpenIGI.Game.Igi2.Detection;

/// <summary>
/// How well detectors see a player, <c>humanplayer.c HumanPlayer_UpdateVisibilityFactor</c>'s steps: fully while a shot has lit
/// it; otherwise half the light it stands in, to the visibility gamma; less keeping low; more moving; and kept between the
/// minimum and the maximum.
/// </summary>
/// <remarks>
/// I.G.I. 2's own player works its posture and movement out from its pose and its locomotion state; a player of another game
/// hosting I.G.I. 2's soldiers works them out from its own body (<see cref="Igi2HostPlayerVisibility"/>). The steps are the same.
/// </remarks>
public static class Igi2VisibilityFactor
{
    /// <summary>What the factor is multiplied by crouched.</summary>
    public const double Crouched = 0.800000011920929;

    /// <summary>What the factor is multiplied by lying flat.</summary>
    public const double Lying = 0.6000000238418579;

    /// <summary>What turning on the spot or crawling adds.</summary>
    public const float Turning = 0.05000000074505806f;

    /// <summary>What walking adds.</summary>
    public const float Walking = 0.10000000149011612f;

    /// <summary>What running adds.</summary>
    public const float Running = 0.15000000596046448f;

    /// <summary>Works the factor out.</summary>
    /// <param name="lightIgnored">Whether a loud shot keeps the player seen whatever the light.</param>
    /// <param name="light">The light the player stands in, on the lightmaps' scale.</param>
    /// <param name="gamma">The visibility gamma.</param>
    /// <param name="posture">What keeping low multiplies the factor by (<see cref="Crouched"/>, <see cref="Lying"/>), or <see langword="null"/> standing.</param>
    /// <param name="movement">What moving adds (<see cref="Turning"/>, <see cref="Walking"/>, <see cref="Running"/>), or nought.</param>
    /// <param name="minimum">The least the factor may be.</param>
    /// <param name="maximum">The most the factor may be.</param>
    /// <returns>The factor.</returns>
    public static float Measure(bool lightIgnored, float light, float gamma, double? posture, float movement, float minimum, float maximum)
    {
        float factor = 1f;
        if (!lightIgnored)
        {
            Fpu half = (Fpu)light * 0.5;
            double clamped = half.Value >= 0.0 ? (half.Value <= 1.0 ? half.Value : 1.0) : 0.0;
            factor = (float)Math.Pow(clamped, gamma);
        }

        if (posture is { } low)
        {
            factor = ((Fpu)factor * low).F;
        }

        if (movement != 0f)
        {
            factor = ((Fpu)factor + movement).F;
        }

        return minimum <= factor ? (maximum >= factor ? factor : maximum) : minimum;
    }
}
