using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Player;

/// <summary>
/// A title's player the host may watch over the shoulder (<see cref="ThirdPersonCamera"/>), for a title that brings its own player
/// rather than a viewpoint of its own. <b>Not retail</b>: the remaster's third-person view.
/// </summary>
/// <remarks>
/// Optional beside <see cref="ITitlePlayer"/>, as <see cref="ITitleFirstPerson"/> is. The host moves only the camera; the title says
/// when its own state leaves the view to the eye -- its own cameras, an optic, a seat -- and is told each frame what the move
/// left showing, so it can draw its body instead of its hands and point its shots where the crosshair is.
/// </remarks>
public interface ITitleOverShoulder
{
    /// <summary>
    /// Gets a value indicating whether the player's own state lets the view leave the eye: walking in its first-person camera, alive,
    /// and nothing of the title's own holding the view. When not, the camera is cut back to the eye.
    /// </summary>
    bool AllowsOverShoulder { get; }

    /// <summary>
    /// Gets a value indicating whether the player has an optic up -- binoculars, a scope, a zoom -- which is a first-person act: the
    /// camera moves back to the eye for it, rather than cutting, and goes out again as it comes down.
    /// </summary>
    bool HasOpticUp { get; }

    /// <summary>Tells the player what the over-the-shoulder move left showing this frame.</summary>
    /// <param name="showsBody">Whether the camera is far enough from the eye for the body to be drawn.</param>
    /// <param name="aimRay">
    /// The ray the crosshair sits on, from the drawn camera, in world units; <see langword="null"/> while the camera is at the eye,
    /// which leaves the shots on the eye's own forward.
    /// </param>
    void PresentOverShoulder(bool showsBody, (Vector3d OriginUnits, Vector3f Direction)? aimRay);
}
