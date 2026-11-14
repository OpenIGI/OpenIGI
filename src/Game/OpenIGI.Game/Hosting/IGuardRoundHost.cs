using OpenIGI.Game.World;

namespace OpenIGI.Game.Hosting;

/// <summary>What a guard's round asks of whatever shows it: the burst of the hits.</summary>
public interface IGuardRoundHost
{
    /// <summary>Kicks the burst of a round landing on a surface.</summary>
    /// <param name="hit">Where it landed.</param>
    void SpawnImpactBurst(in WorldRayHit hit);
}
