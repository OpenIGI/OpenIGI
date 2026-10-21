using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.World;

/// <summary>
/// The frame an <c>Elevator</c>'s spline gives it at a point on its track, which turns the car's authored
/// orientation into the one it is drawn with.
/// </summary>
/// <remarks>
/// <para>
/// <c>0x43D960</c> places a car one of two ways. With the Elevator's <c>Linear</c> set (<c>+396</c>) it
/// interpolates between the first and last waypoint and leaves the orientation alone -- every lift shaft in the
/// game. Otherwise it walks the spline with <c>0x4FEC10</c>, which hands back a frame as well as a point, and
/// unless <c>Force orientation</c> is set (<c>+397</c>) the drawn orientation (<c>+112</c>) becomes the authored
/// one (<c>+288</c>, which <c>0x43DDB0</c> copies into <c>+112</c> on reset) times that frame.
/// </para>
/// <para>
/// The frame is the spline's own. At track position nought it is the first waypoint's matrix (<c>+272</c>,
/// <c>0x4FEC10</c>'s <c>LABEL_13</c>); past that it is the matrix of the segment the position lies in, a
/// position exactly on a waypoint belonging to the segment that ends there. A <c>Linear Segments</c> spline
/// gives each segment one piece whose matrices are all a turn about Z to the chord's heading
/// (<c>0x4E4530</c>: <c>0x4B2D00</c> on the chord, then <c>0x4B3BE0</c> on the heading) -- and that heading is
/// <c>atan2(y, x)</c> except for a chord with no X at all, which <c>0x4B2D00</c> calls heading nought rather
/// than either quarter turn.
/// </para>
/// <para>
/// Mission 12's cutscene gondola (Elevator 1667) is the car this is for: authored at 271 degrees on a cable
/// heading 91 degrees, it is drawn at about 2, side-on to the camera, and the port drew it at 271 -- a quarter
/// turn out. A curved spline's frames (slerped Hermite tangents) are not ported: no IGI1 Elevator rides one
/// unforced, and <see cref="For"/> returns <see langword="null"/> for it so the car keeps its authored frame.
/// </para>
/// </remarks>
public sealed class ElevatorSplineFrame
{
    private readonly Matrix3f _start;
    private readonly Matrix3f[] _segments;

    private ElevatorSplineFrame(Matrix3f start, Matrix3f[] segments)
    {
        _start = start;
        _segments = segments;
    }

    /// <summary>The frame at a track position, given as the elevator's continuous floor coordinate.</summary>
    /// <param name="floorPosition">The waypoint index plus the fraction of the way to the next one.</param>
    /// <returns>The spline's frame there.</returns>
    public Matrix3f At(double floorPosition)
    {
        if (floorPosition <= 0.0 || _segments.Length == 0)
        {
            return _start;
        }

        int segment = Math.Clamp((int)Math.Ceiling(floorPosition) - 1, 0, _segments.Length - 1);
        return _segments[segment];
    }

    /// <summary>The frame an Elevator draws by, or <see langword="null"/> when it keeps its authored orientation.</summary>
    /// <param name="elevator">The <c>Elevator</c> task.</param>
    /// <param name="spline">Its <c>SplineObj</c>, or <see langword="null"/>.</param>
    /// <returns>The frame, when the car is walked along the spline and takes the spline's orientation.</returns>
    public static ElevatorSplineFrame? For(DeclaredTask elevator, GameTask? spline)
    {
        ArgumentNullException.ThrowIfNull(elevator);
        if (spline is not DeclaredTask splineObj
            || Flag(elevator, "Linear")
            || Flag(elevator, "Force orientation")
            || !Flag(splineObj, "Linear Segments"))
        {
            return null;
        }

        List<DeclaredTask> waypoints = splineObj.Children
            .OfType<DeclaredTask>()
            .Where(static child => string.Equals(child.Type.Name, "SplineObjWaypoint", StringComparison.Ordinal))
            .ToList();
        if (waypoints.Count == 0)
        {
            return null;
        }

        Matrix3f[] segments = new Matrix3f[waypoints.Count - 1];
        for (int i = 1; i < waypoints.Count; i++)
        {
            Vector3d from = Position(waypoints[i - 1]);
            Vector3d to = Position(waypoints[i]);
            segments[i - 1] = HeadingFrame((float)(to.X - from.X), (float)(to.Y - from.Y));
        }

        float[] m = RetailWorldObjects.WaypointOrientation(waypoints[0]);
        Matrix3f start = new(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8]);
        return new ElevatorSplineFrame(start, segments);
    }

    /// <summary><c>0x4B2D00</c>'s heading of a chord fed to <c>0x4B3BE0</c>: a turn about Z.</summary>
    /// <param name="x">The chord's X.</param>
    /// <param name="y">The chord's Y.</param>
    /// <returns>The segment's matrix.</returns>
    internal static Matrix3f HeadingFrame(float x, float y)
    {
        float heading = x == 0f ? 0f : MathF.Atan2(y, x);
        float s = MathF.Sin(heading);
        float c = MathF.Cos(heading);
        return new Matrix3f(
            c, -s, 0f,
            s, c, 0f,
            0f, 0f, 1f);
    }

    private static bool Flag(DeclaredTask task, string name) =>
        task.TryGetValue(name, out object? value) && value is true;

    private static Vector3d Position(DeclaredTask waypoint) =>
        waypoint.TryGetValue("Position", out object? value) && value is Vector3d position ? position : default;
}
