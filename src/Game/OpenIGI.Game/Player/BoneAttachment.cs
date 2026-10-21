using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Player;

/// <summary>
/// Where a bone puts something hung off it: a world position in engine units and a world rotation.
/// </summary>
/// <param name="Position">The bone's world position, in the game's engine units.</param>
/// <param name="Rotation">The bone's world 3×3, applied as <c>M · v</c>.</param>
/// <remarks>
/// This is the payload <c>0x4600C0</c>, <c>0x4601A0</c> and <c>0x460210</c> fill — twenty-four bytes of
/// position followed by the forty-byte orientation block. The forty bytes are a 3×3 and the drift
/// counter that rides after it; only the matrix is carried here, because the
/// counter exists to schedule renormalisation and this codebase's quaternions are already normalised
/// where it matters.
/// </remarks>
public readonly record struct BoneAttachment(Vector3d Position, Matrix3f Rotation);
