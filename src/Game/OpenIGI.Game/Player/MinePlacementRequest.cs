using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Player;

/// <summary>A mine the weapon asked to lay this tick (<c>0x4755A0</c>), for its owner to trace.</summary>
/// <param name="Eye">The eye, which the trace starts from.</param>
/// <param name="Direction">The view's forward axis.</param>
/// <param name="Remote">Whether it was laid with the alternate fire, so waits for the computer map.</param>
public readonly record struct MinePlacementRequest(Vector3d Eye, Vector3f Direction, bool Remote);
