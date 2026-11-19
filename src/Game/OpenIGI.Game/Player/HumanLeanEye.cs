using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Player;

/// <summary>
/// Where a lean puts the eye: the view's commit (<c>0x464BD0</c>) moves the body's eye by the lean and sweeps the move against
/// the world, stopping short of whatever it meets.
/// </summary>
/// <remarks>
/// <para>
/// The lean is three floats on the view (<c>HumanView+412..+420</c>). The first two are taken in the eye's own horizontal
/// frame -- across along the orientation's first column, ahead along its second, only their X and Y -- and the third is
/// added on world Z. Nothing is swept while all three are zero, so an unleaned eye is the body's exactly.
/// </para>
/// <para>
/// The sweep is one line from the body's eye to the leaned one. A hit pulls the eye back along that line to
/// <see cref="WallGapUnits"/> short of the hit, never behind the start: the lean can reach a wall but never put the camera in
/// it.
/// </para>
/// </remarks>
public static class HumanLeanEye
{
    /// <summary>How far short of a hit the swept eye stops, in world units: <c>0x464BD0</c>'s 204.8, five centimetres.</summary>
    public const double WallGapUnits = 204.8;

    /// <summary>Places the eye a lean moves to.</summary>
    /// <param name="eye">The body's eye, in world units.</param>
    /// <param name="orientation">The eye's orientation, whose first column is its right and second its forward.</param>
    /// <param name="across">The lean to the right (negative to the left), in world units.</param>
    /// <param name="ahead">The lean forward, in world units.</param>
    /// <param name="up">The lean up, on world Z, in world units.</param>
    /// <param name="walls">What the move is swept against, or <see langword="null"/> to place it unswept.</param>
    /// <returns>The leaned eye.</returns>
    public static Vector3d Place(Vector3d eye, in Matrix3f orientation, float across, float ahead, float up, WorldRayCast? walls)
    {
        if (across == 0f && ahead == 0f && up == 0f)
        {
            return eye;
        }

        Vector3d end = new(
            eye.X + (ahead * orientation.M12) + (across * orientation.M11),
            eye.Y + (ahead * orientation.M22) + (across * orientation.M21),
            eye.Z + up);
        if (walls is null || !walls(eye, end, out WorldRayHit hit))
        {
            return end;
        }

        Vector3d line = end - eye;
        double length = line.Length;
        if (length <= 0.0)
        {
            return eye;
        }

        double reach = Math.Max(0.0, hit.Distance - WallGapUnits);
        double share = reach / length;
        return new Vector3d(eye.X + (line.X * share), eye.Y + (line.Y * share), eye.Z + (line.Z * share));
    }
}
