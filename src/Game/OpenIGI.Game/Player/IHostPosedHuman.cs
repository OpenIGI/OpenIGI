using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;

namespace OpenIGI.Game.Player;

/// <summary>
/// A human of one game standing in another game's level that the level's own game may move and pose for a while: through a
/// cutscene, the level's game runs a body of its own for the human, as its scripts direct, and the human stands where that body
/// stands and as it is posed.
/// </summary>
/// <remarks>
/// The body is the level's game's, on its own rig; the human's game carries the pose onto its own (both games' human rigs are one
/// rig, their bones matched by shape: <see cref="Igi2ClipRetarget"/>). While it follows, the human's own mind does not run.
/// </remarks>
public interface IHostPosedHuman
{
    /// <summary>Gets where the human stands now, in world units, for the level's body to start from.</summary>
    Vector3d Position { get; }

    /// <summary>Gets the way the human faces now, about the vertical, in radians.</summary>
    float Facing { get; }

    /// <summary>Gets whether the human has died: a dead one follows nothing.</summary>
    bool IsDead { get; }

    /// <summary>Stands and poses the human as the level's body is this tick.</summary>
    /// <param name="pose">The body.</param>
    void Follow(in HostPose pose);

    /// <summary>Gives the human back its own mind, standing where the level's body last stood.</summary>
    /// <param name="position">Where it stands, in world units.</param>
    /// <param name="orientation">The way it faces.</param>
    void Release(Vector3d position, Matrix3f orientation);
}

/// <summary>A body of the level's game as a human standing in follows it.</summary>
/// <param name="Position">Where the body stands, in world units.</param>
/// <param name="Orientation">The way it faces.</param>
/// <param name="Pose">Its bones as posed, in its own frame (unturned by <paramref name="Orientation"/>).</param>
/// <param name="Rig">The rig it is posed on, whose bones' rest offsets match it to the human's.</param>
public readonly record struct HostPose(Vector3d Position, Matrix3f Orientation, BonePose Pose, BoneSkeleton Rig);
