namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>How an explosion's damage falls away past its full range, <c>ExplosionDamageFalloffType_e</c>.</summary>
public enum Igi2ExplosionFalloff
{
    /// <summary>In a straight line to nothing at the end of the falloff.</summary>
    Linear = 0,

    /// <summary>As the square of the share left, <c>EXPLOSIONDAMAGE_EXPONENTIAL_FALLOFF</c>.</summary>
    Exponential = 1,
}
