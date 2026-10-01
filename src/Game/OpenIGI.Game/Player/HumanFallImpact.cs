namespace OpenIGI.Game.Player;

/// <summary>The effects produced when the player reaches the ground after a fall.</summary>
/// <param name="SpeedMetresPerSecond">The absolute vertical impact speed.</param>
/// <param name="Damage">Damage added to the player's upward-running damage accumulator.</param>
/// <param name="SoundName">The retail sound definition, or an empty string for a safe landing.</param>
/// <param name="ViewKickUnits">The vertical camera kick installed by the landing.</param>
/// <param name="HearingRadiusUnits">How far the resulting ground-impact AI event travels.</param>
public readonly record struct HumanFallImpact(
    float SpeedMetresPerSecond,
    float Damage,
    string SoundName,
    float ViewKickUnits,
    float HearingRadiusUnits);

/// <summary>The player's landing calculation, transcribed from <c>0x408460</c>.</summary>
/// <remarks>
/// The original measures the absolute vertical velocity in metres per second. The first 14 m/s are
/// harmless; the following 13 m/s consume one complete maximum-health bar. Damage is deliberately not
/// capped, so an impact above 27 m/s can carry the accumulator past death exactly as retail does.
/// </remarks>
public static class HumanFallDamage
{
    /// <summary>The largest landing speed that adds no health damage.</summary>
    public const float SafeSpeedMetresPerSecond = 14f;

    /// <summary>The speed range above <see cref="SafeSpeedMetresPerSecond"/> that consumes one life.</summary>
    public const float LethalSpeedRangeMetresPerSecond = 13f;

    /// <summary>The first damage-sound boundary, as a fraction of maximum health.</summary>
    public const float MediumSoundDamageFraction = 0.1f;

    /// <summary>The second damage-sound boundary, as a fraction of maximum health.</summary>
    public const float HeavySoundDamageFraction = 0.2f;

    /// <summary>The largest downward camera displacement a landing installs.</summary>
    public const float MaximumViewKickUnits = 1024f;

    /// <summary>The ground-impact hearing radius for a landing that did no damage — five metres.</summary>
    public const float SafeImpactHearingRadiusUnits = 20480f;

    /// <summary>The ground-impact hearing radius for a damaging landing — ten metres.</summary>
    public const float DamagingImpactHearingRadiusUnits = 40960f;

    /// <summary>Builds all landing effects from the vertical velocity and the player's maximum health.</summary>
    /// <param name="verticalVelocityUnitsPerTick">The post-gravity vertical velocity, in engine units per tick.</param>
    /// <param name="maximumHealth">The player's maximum-health accumulator value.</param>
    public static HumanFallImpact Calculate(float verticalVelocityUnitsPerTick, float maximumHealth)
    {
        float speed = MathF.Abs(verticalVelocityUnitsPerTick) * (1f / 4096f) * 30f;
        float viewKick = -Math.Clamp(speed * 0.125f, 0f, 1f) * MaximumViewKickUnits;
        float damage = MathF.Max(0f, speed - SafeSpeedMetresPerSecond)
            * (1f / LethalSpeedRangeMetresPerSecond)
            * maximumHealth;

        string sound = damage switch
        {
            <= 0f => string.Empty,
            _ when damage < maximumHealth * MediumSoundDamageFraction => "player_fall_1",
            _ when damage < maximumHealth * HeavySoundDamageFraction => "player_fall_2",
            _ => "player_fall_3",
        };

        return new HumanFallImpact(
            speed,
            damage,
            sound,
            viewKick,
            damage > 0f ? DamagingImpactHearingRadiusUnits : SafeImpactHearingRadiusUnits);
    }
}
