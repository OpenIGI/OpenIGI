using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>A placed IGI1 ladder, including its activation and authored mount geometry.</summary>
/// <remarks>
/// The three points are magic vertices 1 (top activation reference), 2 (foot) and 3 (head), transformed
/// by the ladder task. <c>sub_4404C0</c> evaluates activation and <c>sub_4113E0</c> computes both mount
/// positions directly from those points and the task's orientation.
/// </remarks>
public sealed class LadderPlacement
{
    /// <summary>The activation radius at <c>HumanPlayer</c>'s evaluate context <c>+120</c>, two metres.</summary>
    public const double ActivateRadiusUnits = 8192.0;

    /// <summary>The half-width accepted across the face of the ladder.</summary>
    /// <remarks>The strict <c>−2867.2 &lt; localX &lt; 2867.2</c> test in <c>sub_4404C0</c>.</remarks>
    public const double ActivateHalfWidthUnits = 2867.2;

    /// <summary>The cosine of the 45-degree facing tolerance.</summary>
    public const double FacingCosine = 0.70710678;

    /// <summary>The bottom mount's stand-off along raw matrix column one (<see cref="Matrix3f.Column2"/>).</summary>
    public const double BottomStandoffUnits = 1515.52;

    /// <summary>The bottom mount's lift along raw matrix column two (<see cref="Matrix3f.Column3"/>).</summary>
    public const double BottomVerticalOffsetUnits = 3915.776;

    /// <summary>The top mount's lift along raw matrix column two (<see cref="Matrix3f.Column3"/>).</summary>
    public const double TopVerticalOffsetUnits = 3481.6;

    /// <summary>The top mount's reverse stand-off along raw matrix column one (<see cref="Matrix3f.Column2"/>).</summary>
    public const double TopStandoffUnits = 1638.4;

    /// <summary>Initializes a placed ladder from its task and three retail magic vertices.</summary>
    /// <param name="origin">The ladder task's world position.</param>
    /// <param name="activationTop">Magic vertex 1 in world space.</param>
    /// <param name="bottom">Magic vertex 2 in world space.</param>
    /// <param name="top">Magic vertex 3 in world space.</param>
    /// <param name="orientation">The ladder task's world orientation.</param>
    public LadderPlacement(
        Vector3d origin,
        Vector3d activationTop,
        Vector3d bottom,
        Vector3d top,
        Matrix3f orientation)
    {
        Origin = origin;
        ActivationTop = activationTop;
        Orientation = orientation;

        ClimbLine = new LadderClimbLine(bottom, top);

        // Real32x9_GetAngles(..., &gamma), followed by gamma += PI in sub_4113E0.
        Facing = (float)Math.Atan2(orientation.M21, orientation.M11) + MathF.PI;
        PlayerYaw = -Facing;

        // The retail Real32x9 reads are zero-based columns: +116/+128/+140 is raw column one and
        // +120/+132/+144 is raw column two. Matrix3f's public names are one-based, hence Column2/3.
        Vector3f standoffAxis = orientation.Column2;
        Vector3f verticalAxis = orientation.Column3;
        BottomMount = Offset(
            Bottom,
            standoffAxis,
            BottomStandoffUnits,
            verticalAxis,
            BottomVerticalOffsetUnits);
        TopMount = Offset(
            Top,
            verticalAxis,
            TopVerticalOffsetUnits,
            standoffAxis,
            -TopStandoffUnits);
    }

    /// <summary>Gets the task origin used by the bottom height test.</summary>
    public Vector3d Origin { get; }

    /// <summary>Gets magic vertex 1, used to resolve both activation height and top approach.</summary>
    public Vector3d ActivationTop { get; }

    /// <summary>Gets the ladder's climb line between magic vertices 2 and 3.</summary>
    public LadderClimbLine ClimbLine { get; }

    /// <summary>Gets the ladder task's orientation.</summary>
    public Matrix3f Orientation { get; }

    /// <summary>Gets magic vertex 2, the foot of the climb.</summary>
    public Vector3d Bottom => ClimbLine.Bottom;

    /// <summary>Gets magic vertex 3, the head of the climb.</summary>
    public Vector3d Top => ClimbLine.Top;

    /// <summary>Gets the exact bottom position committed by <c>sub_4113E0</c>.</summary>
    public Vector3d BottomMount { get; }

    /// <summary>Gets the exact top position committed by <c>sub_4113E0</c>.</summary>
    public Vector3d TopMount { get; }

    /// <summary>Gets the yaw committed on mounting, including the original's added π.</summary>
    public float Facing { get; }

    /// <summary>Gets the equivalent yaw in the port camera's mirrored horizontal convention.</summary>
    /// <remarks>
    /// IGI1's human orientation sends local +Y to <c>(−sin gamma, cos gamma)</c>; the current player
    /// camera sends its forward to <c>(sin yaw, cos yaw)</c>. Negation represents the same direction
    /// until that pre-existing global camera/body convention is unified.
    /// </remarks>
    public float PlayerYaw { get; }

    /// <summary>Tests whether this ladder wins the IGI1 ladder activation geometry.</summary>
    /// <param name="playerPosition">The human view position from the activation context.</param>
    /// <param name="playerYaw">The human's gamma.</param>
    /// <param name="atTop">Receives whether magic vertex 1 resolved as a top mount.</param>
    /// <returns><see langword="true"/> when either the bottom or top path accepts the human.</returns>
    public bool CanActivate(Vector3d playerPosition, float playerYaw, out bool atTop) =>
        CanActivate(playerPosition, playerYaw, out atTop, out _);

    /// <summary>Tests activation and returns its head-on score for candidate selection.</summary>
    /// <param name="playerPosition">The human view position from the activation context.</param>
    /// <param name="playerYaw">The human's gamma.</param>
    /// <param name="atTop">Receives whether this is a top mount.</param>
    /// <param name="angle">Receives the angular error in radians; smaller is more head-on.</param>
    /// <returns><see langword="true"/> when the ladder is activatable.</returns>
    public bool CanActivate(Vector3d playerPosition, float playerYaw, out bool atTop, out double angle)
    {
        atTop = false;
        angle = double.MaxValue;

        Vector3d fromOrigin = Subtract(playerPosition, Origin);
        Vector3f columnOne = Orientation.Column1;
        Vector3f columnTwo = Orientation.Column2;
        double across = Vector3d.Dot(fromOrigin, Widen(columnOne));
        double normalDistance = Vector3d.Dot(fromOrigin, Widen(columnTwo));

        double forwardX = Math.Sin(playerYaw);
        double forwardY = Math.Cos(playerYaw);
        double targetX = normalDistance > 0.0 ? -columnTwo.X : columnTwo.X;
        double targetY = normalDistance > 0.0 ? -columnTwo.Y : columnTwo.Y;
        double targetLength = Math.Sqrt((targetX * targetX) + (targetY * targetY));
        double facingDot = targetLength > 0.0
            ? ((forwardX * targetX) + (forwardY * targetY)) / targetLength
            : -1.0;

        bool bottomCandidate = Math.Abs(normalDistance) < ActivateRadiusUnits
            && across > -ActivateHalfWidthUnits
            && across < ActivateHalfWidthUnits
            && facingDot > FacingCosine
            && playerPosition.Z > Origin.Z
            && playerPosition.Z < ActivationTop.Z;

        if (bottomCandidate)
        {
            angle = Math.Acos(Math.Clamp(facingDot, -1.0, 1.0));
        }

        // The top branch then calls the generic resolver on magic vertex 1. That resolver requires the
        // point within the same radius and in front of the human, and chooses the most head-on result.
        Vector3d fromTop = Subtract(playerPosition, ActivationTop);
        Vector3d toTop = Subtract(ActivationTop, playerPosition);
        double topDistance = toTop.Length;
        double topForward = (forwardX * toTop.X) + (forwardY * toTop.Y);
        bool topCandidate = Vector3d.Dot(fromTop, Widen(columnTwo)) < 0.0
            && topDistance < ActivateRadiusUnits
            && topForward > 0.0;

        if (topCandidate)
        {
            atTop = true;
            angle = Math.Acos(Math.Clamp(topForward / topDistance, -1.0, 1.0));
            return true;
        }

        return bottomCandidate;
    }

    private static Vector3d Offset(
        Vector3d origin,
        Vector3f first,
        double firstScale,
        Vector3f second,
        double secondScale) =>
        new(
            origin.X + (first.X * firstScale) + (second.X * secondScale),
            origin.Y + (first.Y * firstScale) + (second.Y * secondScale),
            origin.Z + (first.Z * firstScale) + (second.Z * secondScale));

    private static Vector3d Subtract(Vector3d left, Vector3d right) =>
        new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    private static Vector3d Widen(Vector3f axis) => new(axis.X, axis.Y, axis.Z);

}
