using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>One <c>Explosion0</c> a dying car sets off (<c>0x42E5A0</c>), due once its delay has run.</summary>
/// <param name="Source">The car that is blowing up.</param>
/// <param name="PositionUnits">Where it goes off, in engine units.</param>
/// <param name="DamageFactor">The blast's damage factor.</param>
/// <param name="RadiusUnits">The full-damage radius.</param>
/// <param name="FalloffUnits">How far past the radius the damage takes to reach nothing.</param>
/// <param name="FullRangeUnits">The fireball's full range (the scorch size).</param>
/// <param name="Fragments">How many fragments it throws.</param>
/// <param name="Fireballs">How many fireballs it shows.</param>
public sealed record VehicleBlast(
    DestructibleObject Source,
    Vector3d PositionUnits,
    float DamageFactor,
    float RadiusUnits,
    float FalloffUnits,
    float FullRangeUnits,
    int Fragments,
    int Fireballs)
{
    /// <summary>Every car explosion's sound, <c>explo_03_l</c>.</summary>
    public const string Sound = "explo_03_l";

    /// <summary>Gets how many more ticks the blast waits before it goes off.</summary>
    internal int DelayTicks { get; set; }
}
