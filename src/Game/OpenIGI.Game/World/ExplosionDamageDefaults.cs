namespace OpenIGI.Game.World;

/// <summary>The fallback explosion tuning used when a destructible authors none.</summary>
internal static class ExplosionDamageDefaults
{
    public const float UnitsPerMetre = 4096f;
    public const float DamageFactor = 1f;
    public const float RadiusUnits = 5f * UnitsPerMetre;
    public const float FireballScale = 1f;
    public const int FragmentCount = 6;
    public const float DamageThreshold = 1f;

    public static float ToUnits(float metres) => metres * UnitsPerMetre;
}
