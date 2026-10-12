using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Igi2.World;

/// <summary>
/// A <c>SplineObj</c> and its waypoints, read into the segments that are drawn between them.
/// </summary>
/// <remarks>
/// <para>
/// A spline is the one thing in an I.G.I. 2 level whose geometry is not declared anywhere. The waypoints
/// are declared; the segments between them are made at load, one per waypoint after the first, each
/// bending that waypoint's <c>Segment Model</c> along the curve from the waypoint before it. 678 of the
/// corpus's 713 waypoints carry one, and they are the roads, pipe runs, fences and cable trays that the
/// levels are threaded with.
/// </para>
/// <para>
/// <b>Read as the settled state, not as a replay.</b> The original arrives here through
/// <c>SplineObjWaypoint_UpdateInternalDataHandler</c> (<c>igi2ed 0x6417C0</c>), which fires once per
/// waypoint on load and then a second time for the whole spline from
/// <c>SplineObj_UpdateInternalDataAfterLoadHandler</c> (<c>0x6436C0</c>). Each firing recomputes the
/// waypoint's own orientation and then rebuilds the segments on either side of it. What it converges to
/// is what this computes directly, in one pass over the waypoints, which is the same answer without the
/// event ordering.
/// </para>
/// </remarks>
public static class Igi2SplineObject
{
    /// <summary>The task type that owns a run of waypoints.</summary>
    public const string SplineType = "SplineObj";

    /// <summary>The task type of one waypoint on that run.</summary>
    public const string WaypointType = "SplineObjWaypoint";

    /// <summary>The type name a generated segment is reported under.</summary>
    /// <remarks>
    /// The original's own name for it. Nothing in level data declares a task of this type — it exists
    /// only because <c>SplineObjSegment_New</c> (<c>igi2ed 0x611E80</c>) makes one.
    /// </remarks>
    public const string SegmentType = "SplineObjSegment";

    /// <summary>The parameter a waypoint names the model swept towards it with.</summary>
    public const string SegmentModelParameter = "Segment Model";

    /// <summary>Reads a spline: the segments it draws, and the track they lie along.</summary>
    /// <param name="spline">The <c>SplineObj</c> task.</param>
    /// <returns>The run. A spline of fewer than two waypoints has neither segments nor length.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="spline"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Both come out of one read because they are one thing seen twice: the curve between each pair of
    /// waypoints is the shape a segment's model is bent into AND the piece of track a train rides over.
    /// A pair whose waypoint names no <c>Segment Model</c> still belongs to the track — it simply draws
    /// nothing — which is why the two lists are not the same length.
    /// </remarks>
    public static Igi2SplineRun Read(GameTask spline)
    {
        ArgumentNullException.ThrowIfNull(spline);

        List<Waypoint> waypoints = [];
        foreach (GameTask child in spline.Children)
        {
            if (string.Equals(child.Type.Name, WaypointType, StringComparison.Ordinal)
                && Waypoint.TryRead(child, out Waypoint waypoint))
            {
                waypoints.Add(waypoint);
            }
        }

        if (waypoints.Count < 2)
        {
            return new Igi2SplineRun(new Igi2SplinePath(spline.Id, []), []);
        }

        bool straight = Flag(spline, "Linear Segments");
        bool automatic = Flag(spline, "Automatic Orientation");
        int splineAreas = Number(spline, "Number of Matrices / Segment", Igi2SplineSweep.MaximumAreas);

        // A waypoint that is told to orient itself does not use the orientation it was authored with. It
        // faces along the two chords it sits between, which is what keeps a road's surface flat through a
        // corner rather than twisting at every waypoint. A straight spline skips this entirely.
        if (!straight)
        {
            Matrix3f[] revised = new Matrix3f[waypoints.Count];
            for (int i = 0; i < waypoints.Count; i++)
            {
                revised[i] = automatic || waypoints[i].Automatic
                    ? Facing(
                        i > 0 ? waypoints[i - 1].Position : null,
                        waypoints[i].Position,
                        i + 1 < waypoints.Count ? waypoints[i + 1].Position : null)
                    : waypoints[i].Orientation;
            }

            for (int i = 0; i < waypoints.Count; i++)
            {
                waypoints[i] = waypoints[i] with { Orientation = revised[i] };
            }
        }

        List<Igi2Placement> segments = [];
        Igi2SplineSweep[] track = new Igi2SplineSweep[waypoints.Count - 1];
        for (int i = 1; i < waypoints.Count; i++)
        {
            Waypoint owner = waypoints[i];
            Igi2SplineSweep sweep = Igi2SplineSweep.Create(
                waypoints[i - 1].Position,
                owner.Position,
                waypoints[i - 1].Orientation,
                owner.Orientation,
                Math.Min(owner.Areas, splineAreas),

                // Either flag straightens the segment: the parent's for the whole run, the waypoint's
                // own for one of them. No waypoint in the shipped corpus sets its own.
                straight || owner.Straight,
                owner.Flipped);

            track[i - 1] = sweep;
            if (owner.Model.Length == 0)
            {
                continue;
            }

            segments.Add(new Igi2Placement(
                owner.Id,
                SegmentType,
                owner.Model,
                sweep.Midpoint,
                default,
                owner.RenderGroup,
                ModelVariant: 0,
                MountedModel: string.Empty,
                MountedOrientation: default,

                // The frames are already world facings, so the object itself is not turned at all -- it
                // only carries them to where the curve's midpoint is.
                Frame: Matrix3f.Identity,
                Geometry: sweep));
        }

        return new Igi2SplineRun(new Igi2SplinePath(spline.Id, track), segments);
    }

    /// <summary>
    /// Builds the orientation a self-orienting waypoint takes from the waypoints on either side of it.
    /// </summary>
    /// <param name="previous">The waypoint before, if there is one.</param>
    /// <param name="position">This waypoint's position.</param>
    /// <param name="next">The waypoint after, if there is one.</param>
    /// <returns>The rotation, with the direction of travel in its first column.</returns>
    /// <remarks>
    /// <c>SplineObjWaypoint_UpdateInternalDataHandler</c> sums the chord coming in and the chord going
    /// out, takes that as the direction, and builds an orthonormal frame around it by two cross products.
    /// The second axis starts horizontal — the perpendicular in the ground plane — so a road banks only
    /// where the ground makes it, not wherever the curve happens to twist.
    /// </remarks>
    public static Matrix3f Facing(Vector3d? previous, Vector3d position, Vector3d? next)
    {
        Vector3f along = default;
        if (previous is { } before)
        {
            along = new Vector3f(
                (float)(position.X - before.X),
                (float)(position.Y - before.Y),
                (float)(position.Z - before.Z));
        }

        if (next is { } after)
        {
            along = new Vector3f(
                along.X + (float)(after.X - position.X),
                along.Y + (float)(after.Y - position.Y),
                along.Z + (float)(after.Z - position.Z));
        }

        if (along.X == 0f && along.Y == 0f && along.Z == 0f)
        {
            along = new Vector3f(1f, 0f, 0f);
        }

        Vector3f side = along.X == 0f && along.Y == 0f
            ? new Vector3f(0f, 1f, 0f)
            : Normalize(new Vector3f(along.Y, -along.X, 0f));

        Vector3f first = Normalize(along);
        Vector3f second = Normalize(Cross(Cross(side, along), along));
        Vector3f third = Normalize(Cross(first, second));

        // Stored transposed, which puts the three axes in the COLUMNS -- and the direction of travel in
        // the first column, where the curve reads its tangent from.
        return new Matrix3f(
            first.X, second.X, third.X,
            first.Y, second.Y, third.Y,
            first.Z, second.Z, third.Z);
    }

    private static bool Flag(GameTask task, string name) =>
        task.TryGetScriptValue(name, out object? raw) && raw is bool value && value;

    private static int Number(GameTask task, string name, int fallback) =>
        task.TryGetScriptValue(name, out object? raw) && raw is int value ? value : fallback;

    private static Vector3f Normalize(Vector3f value)
    {
        float length = MathF.Sqrt((value.X * value.X) + (value.Y * value.Y) + (value.Z * value.Z));
        return length > 0f
            ? new Vector3f(value.X / length, value.Y / length, value.Z / length)
            : value;
    }

    private static Vector3f Cross(Vector3f left, Vector3f right) => new(
        (left.Y * right.Z) - (left.Z * right.Y),
        (left.Z * right.X) - (left.X * right.Z),
        (left.X * right.Y) - (left.Y * right.X));

    /// <summary>One waypoint, as the segments either side of it need it.</summary>
    private readonly record struct Waypoint(
        int Id,
        Vector3d Position,
        Matrix3f Orientation,
        string Model,
        int Areas,
        int RenderGroup,
        bool Straight,
        bool Flipped,
        bool Automatic)
    {
        public static bool TryRead(GameTask task, out Waypoint waypoint)
        {
            waypoint = default;
            if (!task.TryGetScriptValue(Igi2WorldScene.PositionParameter, out object? rawPosition)
                || rawPosition is not Vector3d position)
            {
                return false;
            }

            Vector3f angles = task.TryGetScriptValue(Igi2WorldScene.OrientationParameter, out object? rawAngles)
                && rawAngles is Vector3f read
                    ? read
                    : default;

            string model = task.TryGetScriptValue(SegmentModelParameter, out object? rawModel)
                && rawModel is string name
                && !string.IsNullOrWhiteSpace(name)
                    ? name
                    : string.Empty;

            waypoint = new Waypoint(
                task.Id,
                position,
                Matrix3f.FromEngineEulerAngles(angles.X, angles.Y, angles.Z),
                model,
                Number(task, "NumAreas", Igi2SplineSweep.MaximumAreas),
                Number(task, Igi2WorldScene.RenderGroupParameter, 0),
                Flag(task, "Align"),
                Flag(task, "Flip"),
                Flag(task, "Automatic Orientation"));
            return true;
        }
    }
}
