using OpenIGI.Game.Weapons.Ballistics;

namespace OpenIGI.Game.Player;

/// <summary>A title player whose rounds fly as ballistic rounds, which says where they landed this tick for the host to show.</summary>
/// <remarks>Optional beside <see cref="ITitlePlayer"/>: the host bursts the sparks and dust of each, of the struck material.</remarks>
public interface ITitleBallisticImpacts
{
    /// <summary>Gets where the player's ballistic rounds landed this tick.</summary>
    IReadOnlyList<BallisticImpact> BallisticImpacts { get; }
}
