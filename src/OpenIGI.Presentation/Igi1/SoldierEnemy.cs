using OpenIGI.Core.Numerics;

namespace OpenIGI.Presentation.Igi1;

/// <summary>The player as the soldiers see it when they update.</summary>
/// <param name="Eye">The eye the view cones are tested against, in world units.</param>
/// <param name="Alive">
/// Whether the player is still a target. A dead player is not: the detection scan (<c>0x489750</c>) unregisters a
/// human that reports itself invalid, so the guards lose it and stop firing at the corpse.
/// </param>
/// <param name="Feet">
/// The player task's position -- its feet, what combat aims at (<c>sub_451380</c>); <see langword="null"/> when
/// there is no body, and the eye stands in.
/// </param>
/// <param name="Crouching">Whether the player is crouching, which halves the aim raise.</param>
public readonly record struct SoldierEnemy(Vector3d Eye, bool Alive, Vector3d? Feet, bool Crouching);
