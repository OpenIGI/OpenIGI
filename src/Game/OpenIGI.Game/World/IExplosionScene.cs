namespace OpenIGI.Game.World;

/// <summary>What of a title's explosions is there to be drawn: its live FireBalls and the fragments its blasts threw.</summary>
public interface IExplosionScene
{
    /// <summary>Gets the FireBalls, waiting or showing.</summary>
    IReadOnlyList<ExplosionFireBall> FireBalls { get; }

    /// <summary>Gets the fragment containers, each a blast's thrown pieces.</summary>
    IReadOnlyList<ExplosionFragments> FragmentGroups { get; }
}
