using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>The fallback explosion tuning used when a destructible authors none.</summary>
public static class ExplosionDamageDefaults
{
    /// <summary>World units in a metre.</summary>
    public const float UnitsPerMetre = (float)WorldUnits.PerMetre;
    /// <summary>The blast's damage factor when the level declares none.</summary>
    public const float DamageFactor = 1f;
    /// <summary>The blast radius an explode object has when its level declares none, world units.</summary>
    public const float RadiusUnits = 5f * UnitsPerMetre;
    /// <summary>The fireball's scale when the level declares none.</summary>
    public const float FireballScale = 1f;
    /// <summary>How many fragments an explosion throws when its level declares none.</summary>
    public const int FragmentCount = 6;
    /// <summary>The damage that sets an explode object off when its level declares none.</summary>
    public const float DamageThreshold = 1f;

    /// <summary>Converts metres to world units.</summary>
    public static float ToUnits(float metres) => metres * UnitsPerMetre;
}
