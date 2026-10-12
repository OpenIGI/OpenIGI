namespace OpenIGI.Game.Igi2.Ai.Runtime;

/// <summary>
/// Where each tuning value sits in a mind's <c>tTweakParameters</c>, in the retail order (<c>HumanAIConfigValues_t</c>), which the
/// natives and the content index.
/// </summary>
public static class Igi2AiTweakIndex
{
    /// <summary>How many values there are: 37 tunings and 17 damage multipliers.</summary>
    public const int Count = 54;

    /// <summary>Ticks of danger before it calms.</summary>
    public const int DangerTimeout = 0;

    /// <summary>Ticks an inquiry lasts.</summary>
    public const int InquireTimeout = 1;

    /// <summary>How distance slows detection.</summary>
    public const int DetectionTimeDistanceFactor = 2;

    /// <summary>The share of a clip a burst fires.</summary>
    public const int PercentageFiredPerBurst = 3;

    /// <summary>The chance of evading.</summary>
    public const int EvasiveActionProb = 4;

    /// <summary>The accuracy at close range.</summary>
    public const int MinRangeAccuracy = 5;

    /// <summary>The accuracy at middle range.</summary>
    public const int MedRangeAccuracy = 6;

    /// <summary>The accuracy at long range.</summary>
    public const int MaxRangeAccuracy = 7;

    /// <summary>Where middle range is.</summary>
    public const int AccuracyMedRange = 8;

    /// <summary>Where long range is.</summary>
    public const int AccuracyMaxRange = 9;

    /// <summary>The per-unit accuracy factor out to middle range.</summary>
    public const int MedRangeAccuracyFactor = 10;

    /// <summary>The per-unit accuracy factor beyond it.</summary>
    public const int MaxRangeAccuracyFactor = 11;

    /// <summary>The least delay between shots.</summary>
    public const int MinDelayBetweenFiring = 12;

    /// <summary>Ticks a target is tracked.</summary>
    public const int TrackingTimeout = 13;

    /// <summary>How far a target is tracked.</summary>
    public const int TrackingMaxDistance = 14;

    /// <summary>The chance of throwing a grenade.</summary>
    public const int GrenadeThrowProb = 15;

    /// <summary>A grenade's fuse.</summary>
    public const int GrenadeDetonationTimer = 16;

    /// <summary>The chance of reacting to a grenade landing.</summary>
    public const int GrenadeLandingReactionProb = 17;

    /// <summary>Close combat damage.</summary>
    public const int CloseCombatDamage = 18;

    /// <summary>Hit points.</summary>
    public const int HitPoints = 19;

    /// <summary>The share past which a hit makes it flinch.</summary>
    public const int HitRecoilThreshold = 20;

    /// <summary>The combat view's first cone.</summary>
    public const int CombatViewCone1Alpha = 21;

    /// <summary>Its reach.</summary>
    public const int CombatViewCone1Length = 22;

    /// <summary>The combat view's second cone.</summary>
    public const int CombatViewCone2Alpha = 23;

    /// <summary>Its reach.</summary>
    public const int CombatViewCone2Length = 24;

    /// <summary>The idle view's first cone.</summary>
    public const int IdleViewCone1Alpha = 25;

    /// <summary>Its reach.</summary>
    public const int IdleViewCone1Length = 26;

    /// <summary>The idle view's second cone.</summary>
    public const int IdleViewCone2Alpha = 27;

    /// <summary>Its reach.</summary>
    public const int IdleViewCone2Length = 28;

    /// <summary>Ticks between idle animations.</summary>
    public const int IdleAnimationFrequency = 29;

    /// <summary>The investigating view's first cone.</summary>
    public const int InvestigateViewCone1Alpha = 30;

    /// <summary>Its reach.</summary>
    public const int InvestigateViewCone1Length = 31;

    /// <summary>The investigating view's second cone.</summary>
    public const int InvestigateViewCone2Alpha = 32;

    /// <summary>Its reach.</summary>
    public const int InvestigateViewCone2Length = 33;

    /// <summary>Ticks between investigation shouts.</summary>
    public const int InvestigateShoutFrequency = 34;

    /// <summary>The animation speed.</summary>
    public const int AnimSpeedFactor = 35;

    /// <summary>Ticks from detection to attack.</summary>
    public const int DetectionToAttackTime = 36;

    /// <summary>The first damage multiplier; 17 follow from here in body-part order.</summary>
    public const int DamageMultipliers = 37;

    /// <summary>Copies a type's tuning into a mind's values (<c>HumanAIConfig_SetAISettingsFromConfig</c>).</summary>
    /// <param name="values">The mind's values.</param>
    /// <param name="t">The tuning.</param>
    public static void Fill(float[] values, Igi2AiTweaks t)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(t);
        float[] tuning =
        [
            t.DangerTimeout, t.InquireTimeout, t.DetectionTimeDistanceFactor, t.PercentageFiredPerBurst, t.EvasiveActionProb,
            t.MinRangeAccuracy, t.MedRangeAccuracy, t.MaxRangeAccuracy, t.AccuracyMedRange, t.AccuracyMaxRange,
            t.MedRangeAccuracyFactor, t.MaxRangeAccuracyFactor, t.MinDelayBetweenFiring, t.TrackingTimeout, t.TrackingMaxDistance,
            t.GrenadeThrowProb, t.GrenadeDetonationTimer, t.GrenadeLandingReactionProb, t.CloseCombatDamage, t.HitPoints,
            t.HitRecoilThreshold, t.CombatViewCone1Alpha, t.CombatViewCone1Length, t.CombatViewCone2Alpha, t.CombatViewCone2Length,
            t.IdleViewCone1Alpha, t.IdleViewCone1Length, t.IdleViewCone2Alpha, t.IdleViewCone2Length, t.IdleAnimationFrequency,
            t.InvestigateViewCone1Alpha, t.InvestigateViewCone1Length, t.InvestigateViewCone2Alpha, t.InvestigateViewCone2Length,
            t.InvestigateShoutFrequency, t.AnimSpeedFactor, t.DetectionToAttackTime,
        ];
        tuning.CopyTo(values, 0);
    }
}
