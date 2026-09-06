using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Player;

/// <summary>Reports the nearest human on a segment without applying damage.</summary>
public delegate bool HumanRayCast(
    Vector3d origin,
    Vector3d end,
    out HumanHit hit);

/// <summary>Reports a living human inside a circular proximity trigger.</summary>
/// <param name="center">The trigger centre, in world units.</param>
/// <param name="radiusUnits">The trigger radius, in world units.</param>
/// <param name="target">The nearest human found inside the trigger.</param>
/// <returns><see langword="true"/> when a living human is inside the trigger.</returns>
public delegate bool HumanProximityCast(
    Vector3d center,
    float radiusUnits,
    out HumanProximityTarget target);

/// <summary>The position of a human found by a proximity trigger.</summary>
public readonly record struct HumanProximityTarget(Vector3d Position, double Distance);

/// <summary>Offers a round to whatever humans are in the world.</summary>
/// <param name="origin">Where the round starts, in world units.</param>
/// <param name="end">Where it stops.</param>
/// <param name="damageFactor">The weapon's <c>Damage Factor</c>, which the round carries.</param>
/// <param name="category">
/// The firing weapon's category. It decides how a body that this round kills falls: <c>0x45D3C0</c>
/// records a shotgun kill as a <see cref="SoldierDeathMode.Thrown"/> and everything else as a
/// <see cref="SoldierDeathMode.Crumple"/>.
/// </param>
/// <param name="hit">The nearest human struck, when one is.</param>
/// <returns><see langword="false"/> when the round meets nobody.</returns>
/// <remarks>
/// A delegate for the same reason <see cref="World.WorldRayCast"/> is one: it lets the firing path be
/// tested against humans placed by hand rather than only against a loaded level, and it keeps the
/// player from having to know how the world stores its soldiers. The implementation is expected to
/// apply the damage — the caller only needs to know where the round stopped.
/// </remarks>
public delegate bool HumanShot(
    Vector3d origin,
    Vector3d end,
    float damageFactor,
    OpenIGI.Game.Weapons.WeaponCategory category,
    out HumanHit hit);
