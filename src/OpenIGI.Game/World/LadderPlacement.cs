using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>
/// A ladder placed in a level: the world-space climb line the player can mount, the direction a climber
/// faces on it, and the proximity test that decides when it is offered.
/// </summary>
/// <remarks>
/// Built from a <c>TASKTYPE_LADDER</c> magic object's world transform and its model's magic vertices 2
/// (foot) and 3 (head). The activation test stands in for the ladder's <c>EvaluateActivate</c> handler
/// (<c>sub_4404C0</c>); the facing is the ladder's own heading plus π (<c>sub_4113E0</c> / IGI2
/// <c>HumanPlayer_ClimbLadder</c>).
/// </remarks>
public sealed class LadderPlacement
{
    /// <summary>How near the climb line the player must be, horizontally, in world units (about 1.5 m).</summary>
    public const double ActivateRadiusUnits = 6144.0;

    /// <summary>How far below the foot and above the head the ladder is still offered, in world units.</summary>
    public const double HeightMarginUnits = 4096.0;

    /// <summary>The cosine of the facing tolerance — the player must face within 45° of the ladder.</summary>
    /// <remarks><c>cos(0.785398) ≈ 0.7071</c>, the <c>&lt; 0.78539819</c> the handler tests.</remarks>
    public const double FacingCosine = 0.70710678;

    /// <summary>Initializes a new instance of the <see cref="LadderPlacement"/> class.</summary>
    /// <param name="bottom">The foot of the climb, in world units (magic vertex 2).</param>
    /// <param name="top">The head of the climb, in world units (magic vertex 3).</param>
    /// <param name="orientation">The ladder's world orientation, for the facing.</param>
    public LadderPlacement(Vector3d bottom, Vector3d top, Matrix3f orientation)
    {
        // The lower point is always the foot, whichever magic vertex it came from.
        (Vector3d low, Vector3d high) = bottom.Z <= top.Z ? (bottom, top) : (top, bottom);
        ClimbLine = new LadderClimbLine(low, high);

        Orientation = orientation;

        // sub_4113E0: the human faces the ladder's own heading. Verified against the running game
        // (Ladder_ClimbTick fwd=): the body's gamma equals the ladder's Z-heading, which inverts
        // FromEngineEulerAngles' RotationZ (M21 = sin, M11 = cos) -- with NO extra π (an earlier +π faced
        // the body away from the wall).
        Facing = (float)Math.Atan2(orientation.M21, orientation.M11);
    }

    /// <summary>Gets the ladder's climb geometry.</summary>
    public LadderClimbLine ClimbLine { get; }

    /// <summary>Gets the ladder's world orientation, whose axes the mount stand-off is applied along.</summary>
    public Matrix3f Orientation { get; }

    /// <summary>Gets the ladder's foot, in world units.</summary>
    public Vector3d Bottom => ClimbLine.Bottom;

    /// <summary>Gets the ladder's head, in world units.</summary>
    public Vector3d Top => ClimbLine.Top;

    /// <summary>Gets the yaw a climber faces on this ladder — into it, in radians.</summary>
    public float Facing { get; }

    /// <summary>Tests whether the player may mount this ladder from where it stands.</summary>
    /// <param name="playerPosition">The player's feet, in world units.</param>
    /// <param name="playerYaw">Which way the player faces, in radians.</param>
    /// <param name="atTop">Receives whether the mount is at the head (approached from above).</param>
    /// <returns><see langword="true"/> when the ladder is close enough, faced and within height.</returns>
    public bool CanActivate(Vector3d playerPosition, float playerYaw, out bool atTop)
    {
        atTop = false;

        double dx = Bottom.X - playerPosition.X;
        double dy = Bottom.Y - playerPosition.Y;
        double horizontal = Math.Sqrt((dx * dx) + (dy * dy));
        if (horizontal > ActivateRadiusUnits)
        {
            return false;
        }

        if (playerPosition.Z < Bottom.Z - HeightMarginUnits || playerPosition.Z > Top.Z + HeightMarginUnits)
        {
            return false;
        }

        // Facing the ladder: the player's forward (sin yaw, cos yaw) within 45° of the direction to it.
        if (horizontal > 1e-6)
        {
            double forwardX = Math.Sin(playerYaw);
            double forwardY = Math.Cos(playerYaw);
            double dot = ((forwardX * dx) + (forwardY * dy)) / horizontal;
            if (dot < FacingCosine)
            {
                return false;
            }
        }

        // Approaching level with the head (from a roof) mounts at the top; otherwise at the foot.
        atTop = playerPosition.Z >= Top.Z - HeightMarginUnits;
        return true;
    }
}
