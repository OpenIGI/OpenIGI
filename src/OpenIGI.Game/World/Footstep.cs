using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>One footstep, already resolved against the ground it landed on.</summary>
/// <param name="Sound">The sound definition's name, from the material's six walk sounds.</param>
/// <param name="PositionInUnits">Where the foot landed, in engine units.</param>
/// <param name="HearingRadiusInUnits">How far away an AI can hear it, in engine units.</param>
/// <remarks>
/// The position is where the foot landed and the sound stays there: the original parents a footstep to
/// a global root rather than to the walker and sets its position explicitly, so it does not follow the
/// walker away from the place the step was taken.
/// </remarks>
public readonly record struct Footstep(string Sound, Vector3d PositionInUnits, double HearingRadiusInUnits);
