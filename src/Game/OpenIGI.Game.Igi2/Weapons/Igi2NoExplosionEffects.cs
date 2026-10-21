using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.Physics;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>An explosion's effects that do nothing.</summary>
public sealed class Igi2NoExplosionEffects : IIgi2ExplosionEffects
{
    /// <inheritdoc/>
    public void BurnMark(Igi2Intersection ground, float radius)
    {
    }

    /// <inheritdoc/>
    public void Sound(string sound, Vector3d position)
    {
    }

    /// <inheritdoc/>
    public void AiEvent(int aiEvent, int flags, Vector3d position, float range, int team = 1)
    {
    }
}
