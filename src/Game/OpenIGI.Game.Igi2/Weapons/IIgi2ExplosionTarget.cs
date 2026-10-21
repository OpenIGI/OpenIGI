using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.Physics;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>Something an explosion can hurt: a member of <c>GameFunctions_ptExplosionQTaskList</c>.</summary>
/// <remarks>
/// The engine's <see cref="IExplosionHitTarget"/>, read in this game's terms: an explosion this game set off is taken whole, and
/// another game's as <see cref="Igi2ExplosionHit.FromShared"/> reads it.
/// </remarks>
public interface IIgi2ExplosionTarget : IExplosionHitTarget
{
    /// <summary>An explosion reaches it, its <c>ExplosionHit</c> handler.</summary>
    /// <param name="hit">The explosion.</param>
    void ExplosionHit(in Igi2ExplosionHit hit);

    /// <inheritdoc/>
    void IExplosionHitTarget.ExplosionHit(in ExplosionHit hit)
    {
        ExplosionHit(hit.Context is Igi2ExplosionHit own ? own : Igi2ExplosionHit.FromShared(hit));
    }
}
