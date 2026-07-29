using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Player;

/// <summary>Offers a round to whatever humans are in the world.</summary>
/// <param name="origin">Where the round starts, in world units.</param>
/// <param name="end">Where it stops.</param>
/// <param name="damageFactor">The weapon's <c>Damage Factor</c>, which the round carries.</param>
/// <param name="hit">The nearest human struck, when one is.</param>
/// <returns><see langword="false"/> when the round meets nobody.</returns>
/// <remarks>
/// A delegate for the same reason <see cref="World.WorldRayCast"/> is one: it lets the firing path be
/// tested against humans placed by hand rather than only against a loaded level, and it keeps the
/// player from having to know how the world stores its soldiers. The implementation is expected to
/// apply the damage — the caller only needs to know where the round stopped.
/// </remarks>
public delegate bool HumanShot(Vector3d origin, Vector3d end, float damageFactor, out HumanHit hit);
