using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Player;

/// <summary>A title player that can resume walking beneath the developer fly camera.</summary>
public interface ITitlePlayerPlacement
{
    /// <summary>Places the player on the nearest ground below the camera, preserving the player if no ground is found.</summary>
    /// <param name="cameraPosition">The fly camera position in title world units.</param>
    /// <param name="yaw">The fly camera's heading, in radians, clockwise from world Y.</param>
    /// <returns>Whether the player could be placed.</returns>
    bool TryPlace(Vector3d cameraPosition, float yaw);
}
