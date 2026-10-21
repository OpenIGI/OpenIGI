using OpenIGI.Game.Ai;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>
/// The combat block a configured <c>HumanAI</c> carries — <c>+15684</c> through <c>+15816</c>, plus the
/// two fields that land on the owning body.
/// </summary>
/// <remarks>
/// <para>
/// <c>sub_406F70</c> is the only function that writes these, and it applies a conversion on the way in
/// for six of them: four seconds-to-ticks (× 30, truncated toward zero) and four metres-to-units
/// (× 4096). Everything else is copied verbatim. Reproducing the conversion <i>here</i> rather than at
/// each use is what the original does, and it matters — the tick counts are integers, so a delay of
/// 0.33 s is 9 ticks and not 9.9.
/// </para>
/// </remarks>
public sealed class AiCombatParameters
{
    /// <summary>How many logic ticks there are in a second.</summary>
    public const double TicksPerSecond = 30.0;

    /// <summary>How many world units there are in a metre.</summary>
    public const double UnitsPerMetre = 4096.0;

    /// <summary>Initializes a new instance of the <see cref="AiCombatParameters"/> class.</summary>
    /// <param name="item">The settings row to resolve.</param>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is <see langword="null"/>.</exception>
    public AiCombatParameters(HumanAiConfigItemTask item)
    {
        ArgumentNullException.ThrowIfNull(item);

        Source = item;

        InitialMovementDelayTicks = ToTicks(item.InitialMovementDelaySeconds);
        MovementDelayTicks = ToTicks(item.MovementDelaySeconds);
        MaximumRoundsPerSequence = item.MaximumRoundsPerSequence;
        TargetingTimeoutTicks = ToTicks(item.TargetingTimeoutSeconds);

        EvasiveActionProbability = item.EvasiveActionProbability;
        EvasiveStandRollOrLieDownProbability = item.EvasiveStandRollOrLieDownProbability;
        EvasiveKneelRollOrLieDownProbability = item.EvasiveKneelRollOrLieDownProbability;
        EvasiveStandRollSideProbability = item.EvasiveStandRollSideProbability;
        EvasiveKneelRollSideProbability = item.EvasiveKneelRollSideProbability;

        HitProbabilityAtMinimumRange = item.HitProbabilityAtMinimumRange;
        HitProbabilityAtMaximumRange = item.HitProbabilityAtMaximumRange;
        MinimumClipFraction = item.MinimumClipFraction;
        RandomClipFraction = item.RandomClipFraction;
        MinimumFiringDelaySeconds = item.MinimumFiringDelaySeconds;
        RandomFiringDelaySeconds = item.RandomFiringDelaySeconds;
        MinimumShotDelaySeconds = item.MinimumShotDelaySeconds;
        RandomShotDelaySeconds = item.RandomShotDelaySeconds;

        GrenadeThrowProbability = item.GrenadeThrowProbability;
        GrenadeThrowWhenReloadingProbability = item.GrenadeThrowWhenReloadingProbability;
        GrenadeMinimumDistanceInUnits = (float)(item.GrenadeMinimumDistanceMetres * UnitsPerMetre);
        GrenadeMaximumDistanceInUnits = (float)(item.GrenadeMaximumDistanceMetres * UnitsPerMetre);
        GrenadeMinimumFlyTimeSeconds = item.GrenadeMinimumFlyTimeSeconds;
        GrenadeRandomFlyTimeSeconds = item.GrenadeRandomFlyTimeSeconds;
        GrenadeMinimumExplodeTimeSeconds = item.GrenadeMinimumExplodeTimeSeconds;
        GrenadeRandomExplodeTimeSeconds = item.GrenadeRandomExplodeTimeSeconds;

        CloseCombatHitProbability = item.CloseCombatHitProbability;
        CloseCombatDamage = item.CloseCombatDamage;

        LieDownMinimumDistanceInUnits = item.LieDownMinimumDistanceMetres * UnitsPerMetre;
        TrackingDistanceInUnits = item.TrackingDistanceMetres * UnitsPerMetre;
        AimPrecision = item.AimPrecision;

        MaximumHealth = item.DamageScale;
        RecoilThreshold = item.RecoilThreshold;
    }

    /// <summary>Gets the row these were resolved from.</summary>
    public HumanAiConfigItemTask Source { get; }

    /// <summary><c>HumanAI+15684</c> — the delay before the first move of a fight, in ticks.</summary>
    public int InitialMovementDelayTicks { get; }

    /// <summary><c>HumanAI+15688</c> — the delay between moves in a fight, in ticks.</summary>
    public int MovementDelayTicks { get; }

    /// <summary><c>HumanAI+15692</c> — the cap on how many rounds one burst spends.</summary>
    public int MaximumRoundsPerSequence { get; }

    /// <summary><c>HumanAI+15696</c> — how many ticks of losing sight ends the engagement.</summary>
    public int TargetingTimeoutTicks { get; }

    /// <summary><c>HumanAI+15700</c> — the chance of taking evasive action at all.</summary>
    public float EvasiveActionProbability { get; }

    /// <summary><c>HumanAI+15704</c> — the standing roll-versus-lie-down split.</summary>
    public float EvasiveStandRollOrLieDownProbability { get; }

    /// <summary><c>HumanAI+15708</c> — the kneeling roll-versus-lie-down split.</summary>
    public float EvasiveKneelRollOrLieDownProbability { get; }

    /// <summary><c>HumanAI+15712</c> — which way a standing roll goes.</summary>
    public float EvasiveStandRollSideProbability { get; }

    /// <summary><c>HumanAI+15716</c> — which way a kneeling roll goes.</summary>
    public float EvasiveKneelRollSideProbability { get; }

    /// <summary><c>HumanAI+15720</c> — the chance a round hits at point-blank range.</summary>
    public float HitProbabilityAtMinimumRange { get; }

    /// <summary><c>HumanAI+15724</c> — the chance a round hits at the end of the view cone.</summary>
    public float HitProbabilityAtMaximumRange { get; }

    /// <summary><c>HumanAI+15728</c> — the smallest fraction of a clip a burst spends.</summary>
    public float MinimumClipFraction { get; }

    /// <summary><c>HumanAI+15732</c> — the random extra fraction of a clip a burst spends.</summary>
    public float RandomClipFraction { get; }

    /// <summary><c>HumanAI+15736</c> — the smallest gap between bursts, in seconds.</summary>
    /// <remarks>
    /// <b>The counter these feed is write-only in the shipped engine.</b> <c>0x48D080</c> stores
    /// <c>(int)(rand01() * random + minimum)</c> into <c>HumanAI+15828</c> without the × 30 every other
    /// duration gets, and no instruction anywhere reads <c>+15828</c> back — eleven writes, no reads.
    /// So the pair is carried faithfully and, like the original, changes nothing. See
    /// the original.
    /// </remarks>
    public float MinimumFiringDelaySeconds { get; }

    /// <summary><c>HumanAI+15740</c> — the random extra gap between bursts, in seconds.</summary>
    /// <remarks>See <see cref="MinimumFiringDelaySeconds"/>: the counter it feeds is never read.</remarks>
    public float RandomFiringDelaySeconds { get; }

    /// <summary><c>HumanAI+15744</c> — the smallest gap between single shots, in seconds.</summary>
    /// <remarks><c>0x48D8E0</c> does multiply this pair by 30, so it really is seconds at the use site.</remarks>
    public float MinimumShotDelaySeconds { get; }

    /// <summary><c>HumanAI+15748</c> — the random extra gap between single shots, in seconds.</summary>
    public float RandomShotDelaySeconds { get; }

    /// <summary><c>HumanAI+15752</c> — the chance of throwing a grenade.</summary>
    public float GrenadeThrowProbability { get; }

    /// <summary><c>HumanAI+15756</c> — the chance of throwing one instead of reloading.</summary>
    public float GrenadeThrowWhenReloadingProbability { get; }

    /// <summary><c>HumanAI+15760</c> — the closest a grenade will be thrown, in world units.</summary>
    public float GrenadeMinimumDistanceInUnits { get; }

    /// <summary><c>HumanAI+15764</c> — the furthest a grenade will be thrown, in world units.</summary>
    public float GrenadeMaximumDistanceInUnits { get; }

    /// <summary><c>HumanAI+15768</c> — the smallest grenade flight time, in seconds.</summary>
    public float GrenadeMinimumFlyTimeSeconds { get; }

    /// <summary><c>HumanAI+15772</c> — the random extra grenade flight time, in seconds.</summary>
    public float GrenadeRandomFlyTimeSeconds { get; }

    /// <summary><c>HumanAI+15776</c> — the smallest grenade fuse, in seconds.</summary>
    public float GrenadeMinimumExplodeTimeSeconds { get; }

    /// <summary><c>HumanAI+15780</c> — the random extra grenade fuse, in seconds.</summary>
    public float GrenadeRandomExplodeTimeSeconds { get; }

    /// <summary><c>HumanAI+15784</c> — the chance a melee attack connects.</summary>
    public float CloseCombatHitProbability { get; }

    /// <summary><c>HumanAI+15788</c> — how much damage a melee attack deals.</summary>
    public float CloseCombatDamage { get; }

    /// <summary><c>HumanAI+15800</c> — how close a target must be before the owner lies down, in units.</summary>
    public double LieDownMinimumDistanceInUnits { get; }

    /// <summary><c>HumanAI+15808</c> — the tracking distance, in world units.</summary>
    public double TrackingDistanceInUnits { get; }

    /// <summary><c>HumanAI+15816</c> — how fast the owner's aim converges on its target.</summary>
    public double AimPrecision { get; }

    /// <summary><c>Human+600</c> — the owner's maximum health, the script's "Damage scale".</summary>
    public float MaximumHealth { get; }

    /// <summary><c>Human+3332</c> — how much damage makes the owner flinch.</summary>
    /// <remarks>Applied only to a <c>HumanSoldier</c>; the three sibling classes never get one.</remarks>
    public float RecoilThreshold { get; }

    /// <summary>Converts a duration in seconds to ticks the way <c>sub_406F70</c> does.</summary>
    /// <param name="seconds">The duration.</param>
    /// <returns>The tick count, truncated toward zero.</returns>
    public static int ToTicks(float seconds) => (int)(seconds * TicksPerSecond);
}
