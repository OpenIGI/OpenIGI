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
/// <param name="stopDistance">
/// How far from <paramref name="origin"/>, in world units, the world lets the round reach: the distance
/// to the nearest surface the caller's own trace met, or <see cref="double.PositiveInfinity"/> when it
/// met none. A human at or beyond it is behind that surface and is never struck.
/// </param>
/// <param name="alreadyHeard">
/// Whether the guards have already been offered this round's near miss. Retail offers it once per round,
/// over the whole range, before the march (<c>0x47B690</c>); a march that casts again past a surface or a
/// body passes <see langword="true"/> for every cast after its first.
/// </param>
/// <param name="hit">
/// The nearest crossing of a human's skin, when there is one. An exit (<see cref="HumanHit.IsExit"/>) is
/// the round leaving a body it went into, and is reported without damage.
/// </param>
/// <returns><see langword="false"/> when the round meets nobody short of <paramref name="stopDistance"/>.</returns>
/// <remarks>
/// <para>
/// A delegate for the same reason <see cref="World.WorldRayCast"/> is one: it lets the firing path be
/// tested against humans placed by hand rather than only against a loaded level, and it keeps the
/// player from having to know how the world stores its soldiers. The implementation is expected to
/// apply the damage — the caller only needs to know where the round stopped.
/// </para>
/// <para>
/// The stop is passed in rather than applied by the caller afterwards because the damage happens during
/// the call. <c>0x47B750</c> traces each pass once (<c>sub_4CC2A0</c>) and dispatches the gunshot handler
/// (<c>0x47BA54</c>; the human's is <c>0x45F930</c>) on that trace's nearest contact alone, so a wall in
/// front of a guard takes the round and the guard is untouched. The whole segment is still the round's for
/// hearing: the near-miss handler <c>0x4606F0</c> runs from <c>0x47B690</c> before the march, over each
/// round's full remaining range (<c>0x47B290</c>), so a guard behind the wall still hears it go by.
/// </para>
/// <para>
/// A human on the shooter's own side is not there at all: the trace's evaluate (<c>0x45E3E0</c>) rejects
/// it, and the narrow test is never run (<c>0x4D76E6</c>), so the round goes on to whatever is behind.
/// </para>
/// </remarks>
public delegate bool HumanShot(
    Vector3d origin,
    Vector3d end,
    float damageFactor,
    OpenIGI.Game.Weapons.WeaponCategory category,
    double stopDistance,
    bool alreadyHeard,
    out HumanHit hit);
