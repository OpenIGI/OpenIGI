using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>A puff of smoke particles an explosion's smoke starts, <c>Explosion0_AddSmoke</c> (<c>igi2 0x508610</c>).</summary>
/// <param name="Flags">The particle system's graphics flags.</param>
/// <param name="Position">Where.</param>
/// <param name="Size">How big.</param>
/// <param name="SizeDelta">How much it grows a tick.</param>
/// <param name="Lifetime">The ticks it lives.</param>
/// <param name="Fadetime">The ticks it fades over.</param>
/// <param name="Intensity">How bright.</param>
/// <param name="Rotation">Its turn.</param>
/// <param name="RotationDelta">How fast it turns.</param>
public sealed record Igi2SmokePuff(
    int Flags,
    Vector3d Position,
    float Size,
    float SizeDelta,
    int Lifetime,
    int Fadetime,
    float Intensity,
    float Rotation,
    float RotationDelta);
