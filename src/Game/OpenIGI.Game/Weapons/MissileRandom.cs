using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Weapons;

/// <summary>
/// The missiles' own random stream: seeded once when the missile module opens (Project I.G.I. <c>0x4B4720</c> on
/// <c>dword_57BC88</c>, I.G.I. 2 <c>Missile_Open</c>, <c>igi2 0x52F5B0</c>, with the same three words) and drawn by every missile's
/// smoke and wobble for as long as the game runs.
/// </summary>
public sealed class MissileRandom
{
    /// <summary>Gets the stream.</summary>
    public IgiRandom Random { get; } = IgiRandom.Seeded();
}
