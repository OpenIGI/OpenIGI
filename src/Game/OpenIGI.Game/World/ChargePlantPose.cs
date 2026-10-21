using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>Where state 36's entry (<c>0x40D180</c>) stands the body, and where it puts the charge.</summary>
/// <param name="Position">The body origin before the ground snap, in world units.</param>
/// <param name="Facing">The body's gamma.</param>
/// <param name="Contact">The traced point the charge model is moved to, in world units.</param>
public readonly record struct ChargePlantPose(Vector3d Position, float Facing, Vector3d Contact);
