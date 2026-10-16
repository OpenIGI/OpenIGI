using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>
/// The missiles' own random stream, <c>Missile_tRandomState</c>: seeded once when the missile module opens (<c>Missile_Open</c>,
/// <c>igi2 0x52F5B0</c>) and drawn by every missile's smoke and wobble for as long as the game runs.
/// </summary>
public sealed class Igi2MissileRandom
{
    /// <summary>Gets the stream.</summary>
    public IgiRandom Random { get; } = new(unchecked((uint)-923067897), 971667286u, 2146516173u);
}
