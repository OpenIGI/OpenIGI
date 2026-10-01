namespace OpenIGI.Game.Player;

/// <summary>How a soldier class answers a hit that does not kill.</summary>
public enum HumanRecoilRule
{
    /// <summary>
    /// Accumulate the hit's damage and flinch once the total passes the threshold — the soldier's
    /// <c>0x45D3C0</c>, <c>0x45A3D0</c> and <c>0x45A5E0</c>.
    /// </summary>
    Threshold,

    /// <summary>
    /// Accumulate and reset at the threshold like <see cref="Threshold"/>, but never flinch — the female
    /// soldier's <c>0x4553E0</c>, <c>0x455570</c> and <c>0x455780</c>.
    /// </summary>
    ThresholdWithoutFlinch,

    /// <summary>
    /// No accumulator: every hit flinches — the RPG soldier's <c>0x4572D0</c>, <c>0x457430</c> and
    /// <c>0x457620</c>.
    /// </summary>
    EveryHit,
}

/// <summary>What a non-lethal hit did to a soldier's recoil.</summary>
/// <param name="Reacted">
/// The hit passed the threshold, or the class reacts to every hit. An explosion plays <c>ai_xplhit_</c>
/// only on this.
/// </param>
/// <param name="Flinches">The hit raises the one-tick flinch bit <c>0x10000</c>.</param>
public readonly record struct HumanRecoilOutcome(bool Reacted, bool Flinches);

/// <summary>
/// A soldier's recoil — the damage accumulator at <c>Human+3336</c> against the threshold at <c>+3332</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every non-lethal hit adds the damage it dealt; once the total is <b>strictly</b> over the threshold the
/// accumulator empties and the class's rule decides whether the body flinches. The threshold is
/// <see cref="DefaultThreshold"/> from the constructor, and <c>0x406F70</c> overwrites it with the settings
/// row's "Recoil threshold" — for a <c>HumanSoldier</c> only.
/// </para>
/// </remarks>
public sealed class HumanRecoil
{
    /// <summary>The constructor's threshold — the float bits <c>0x3EA8F5C3</c>.</summary>
    public const float DefaultThreshold = 0.33f;

    /// <summary>Initializes a recoil accumulator.</summary>
    /// <param name="rule">The class's rule.</param>
    /// <param name="threshold">The threshold the accumulator has to pass.</param>
    public HumanRecoil(HumanRecoilRule rule, float threshold = DefaultThreshold)
    {
        Rule = rule;
        Threshold = threshold;
    }

    /// <summary>Gets the class's rule.</summary>
    public HumanRecoilRule Rule { get; }

    /// <summary>Gets the threshold — <c>Human+3332</c>.</summary>
    public float Threshold { get; }

    /// <summary>Gets the damage accumulated since the last reaction — <c>Human+3336</c>.</summary>
    public float Accumulated { get; private set; }

    /// <summary>Takes a non-lethal hit.</summary>
    /// <param name="damage">The damage the hit dealt.</param>
    /// <returns>Whether the body reacts and whether it flinches.</returns>
    public HumanRecoilOutcome Absorb(float damage)
    {
        if (Rule == HumanRecoilRule.EveryHit)
        {
            return new HumanRecoilOutcome(Reacted: true, Flinches: true);
        }

        Accumulated += damage;
        if (Accumulated <= Threshold)
        {
            return default;
        }

        Accumulated = 0f;
        return new HumanRecoilOutcome(Reacted: true, Flinches: Rule == HumanRecoilRule.Threshold);
    }
}
