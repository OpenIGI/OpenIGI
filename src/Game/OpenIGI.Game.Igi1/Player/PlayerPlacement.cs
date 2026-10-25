using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Player;

/// <summary>Puts the player's body down in the world: at a point the scene names, or at the level's own spawn.</summary>
internal static class PlayerPlacement
{
    /// <summary>Puts the player on the ground under a point.</summary>
    /// <param name="body">The body to place.</param>
    /// <param name="terrain">The level's terrain, or <see langword="null"/> for an object-only interior.</param>
    /// <param name="at">Where to try standing, in the renderer's metres.</param>
    /// <param name="yaw">Which way to face.</param>
    /// <returns><see langword="false"/> when there is no ground under the point.</returns>
    internal static bool Place(PlayerBody body, IGroundHeightField? terrain, Vector3d at, float yaw)
    {
        body.Position = new Vector3d(
            at.X * PlayerCamera.UnitsPerMetre,
            at.Y * PlayerCamera.UnitsPerMetre,
            at.Z * PlayerCamera.UnitsPerMetre);
        body.Yaw = yaw;
        return body.PlaceOnGround(terrain);
    }

    /// <summary>Puts the player at a spawn the level declared.</summary>
    /// <param name="body">The body to place.</param>
    /// <param name="terrain">The level's terrain, or <see langword="null"/> for an object-only interior.</param>
    /// <param name="spawn">The level's own <c>HumanPlayer</c> start.</param>
    /// <param name="registrationGround">
    /// Optional object collision view that includes the gameplay container before an opening cutscene
    /// registers it. Ordinary movement continues to use the live collision supplied by <see cref="PlayerCamera.UseWalls"/>.
    /// </param>
    /// <returns><see langword="false"/> when there is no ground under it.</returns>
    /// <remarks>
    /// A spawn's position is already in the game's world units — unlike a scene placement, which the
    /// scene builder has divided into metres for the renderer — so it goes to the body untouched.
    /// </remarks>
    internal static bool PlaceAt(
        PlayerBody body,
        IGroundHeightField? terrain,
        PlayerSpawnPoint spawn,
        WorldRayCast? registrationGround)
    {
        body.Position = spawn.Position;
        // HumanPlayer's objects.qvm Gamma is the engine object's facing angle. The camera stores a
        // world-to-view yaw, whose horizontal convention is its inverse; this is the same conversion
        // used when authored interaction poses hand the player's facing back to the camera.
        body.Yaw = -spawn.Gamma;
        body.Pitch = 0f;

        WorldRayCast? liveWalls = body.Walls;
        if (registrationGround is not null)
        {
            body.Walls = registrationGround;
        }

        try
        {
            return body.PlaceOnGround(terrain);
        }
        finally
        {
            body.Walls = liveWalls;
        }
    }
}
