using OpenIGI.Game.Weapons;
using OpenIGI.Game.Ai;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>
/// One burst, as <c>0x48B9C0</c> sets it up: how many rounds it spends and how likely each one is to
/// hit.
/// </summary>
/// <param name="Rounds">
/// How many rounds the sequence will spend — <c>HumanAI+15956</c>, counted down as the clip drains.
/// </param>
/// <param name="HitProbability">
/// What the weapon is told the chance of a hit is — <c>Human+1236</c>, which <c>0x462E50</c> hands to
/// the gun with every shot.
/// </param>
public readonly record struct AiCombatBurst(int Rounds, float HitProbability)
{
    /// <summary>Works out a burst the way <c>0x48B9C0</c> does.</summary>
    /// <param name="parameters">The AI's combat row.</param>
    /// <param name="category">The held weapon's category.</param>
    /// <param name="clipSize">The weapon's clip size — its config <c>+1608</c>.</param>
    /// <param name="burstSize">The weapon's own burst length — its config <c>+1612</c>.</param>
    /// <param name="distanceToTargetInUnits">How far away the tracked aim point is.</param>
    /// <param name="primaryViewLengthInUnits">The AI's primary view length, which the range is scaled by.</param>
    /// <param name="random">A value in <c>[0, 1)</c>.</param>
    /// <param name="halveAccuracy">The argument block's flag, which halves the hit probability.</param>
    /// <returns>The burst, or one of no rounds for a weapon that does not fire this way.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="parameters"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// A weapon whose own burst length differs from its clip size and is above one <b>overrides the
    /// clip fraction entirely</b> and is not clamped by the settings row — so a three-round-burst
    /// weapon fires three rounds however the row is tuned.
    /// </para>
    /// <para>
    /// The hit probability is a plain interpolation between the row's minimum-range and maximum-range
    /// figures over zero to the primary view length, with the distance clamped to that length first, so
    /// a target beyond the cone is scored at the maximum-range figure rather than extrapolated.
    /// </para>
    /// </remarks>
    public static AiCombatBurst Begin(
        AiCombatParameters parameters,
        WeaponCategory category,
        int clipSize,
        int burstSize,
        double distanceToTargetInUnits,
        double primaryViewLengthInUnits,
        double random,
        bool halveAccuracy = false)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        float fraction = (float)((random * parameters.RandomClipFraction) + parameters.MinimumClipFraction);

        if (category == WeaponCategory.Grenade)
        {
            return new AiCombatBurst(1, 1f);
        }

        if (!FiresInBursts(category))
        {
            return new AiCombatBurst(0, 0f);
        }

        int rounds;
        if (burstSize != clipSize && burstSize > 1)
        {
            rounds = burstSize;
        }
        else
        {
            rounds = (int)(clipSize * (double)fraction);
            rounds = rounds >= 1 ? Math.Min(rounds, parameters.MaximumRoundsPerSequence) : 1;
        }

        if (rounds == 0)
        {
            rounds = 1;
        }

        float probability;
        double distance = Math.Min(distanceToTargetInUnits, primaryViewLengthInUnits);
        if (distance < 0.0 || distance < 0.01 || primaryViewLengthInUnits <= 0.0)
        {
            probability = parameters.HitProbabilityAtMinimumRange;
        }
        else
        {
            probability = (float)(((parameters.HitProbabilityAtMaximumRange
                    - parameters.HitProbabilityAtMinimumRange)
                * (distance / primaryViewLengthInUnits))
                + parameters.HitProbabilityAtMinimumRange);
        }

        if (halveAccuracy)
        {
            probability *= 0.5f;
        }

        return new AiCombatBurst(rounds, probability);
    }

    /// <summary>Tells whether a weapon fires through the burst machinery at all.</summary>
    /// <param name="category">The category.</param>
    /// <returns><see langword="true"/> for a gun, a pistol or a shotgun.</returns>
    /// <remarks>
    /// The test is <c>config[+1552] &lt;= 2</c> in the setup and <c>&gt; 2</c> in the firing tick, so
    /// the two agree: only the first three categories run this path, and a grenade gets a burst of one
    /// but is thrown by a different channel.
    /// </remarks>
    public static bool FiresInBursts(WeaponCategory category) =>
        category is WeaponCategory.Gun or WeaponCategory.Pistol or WeaponCategory.Shotgun;
}
