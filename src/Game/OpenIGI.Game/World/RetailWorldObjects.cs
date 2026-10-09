using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;

namespace OpenIGI.Game.World;

/// <summary>
/// The placed models and spline segments a level's ray query tests, built from the level exactly as the original
/// places them, with a coarse grid in front for the broad phase.
/// </summary>
/// <remarks>
/// <para>
/// A model stands at its task's own <c>Position</c> and <c>Orientation</c> (<c>0x4B38E0</c>, exact) with collision
/// level 0 of its LOD 0; a spline chain lays one bent segment per waypoint pair (<see cref="RetailSplineSegment"/>).
/// Vehicles are not here: they move, and their live bodies are added by the caller.
/// </para>
/// <para>
/// The original's broad phase is a loose octree walked in a fixed order; any conservative candidate set gives the
/// same answer except for two objects at exactly the same distance, where the original's order decides.
/// </para>
/// </remarks>
public sealed class RetailWorldObjects
{
    /// <summary>The grid cell size in world units (16 m).</summary>
    private const double Cell = 65536.0;

    private static readonly HashSet<string> MovingTypes = new(StringComparer.Ordinal) { "Car", "Heli", "Train", "Plane" };

    private readonly List<object> _objects = [];
    private readonly Dictionary<(long, long), List<int>> _grid = [];
    private readonly List<object> _candidates = [];
    private readonly HashSet<int> _seen = [];

    /// <summary>Gets how many objects were placed.</summary>
    public int Count => _objects.Count;

    /// <summary>Gets how many spline segments were skipped because they are laid rigid (path B, not transcribed).</summary>
    public int SkippedRigidSegments { get; private set; }

    /// <summary>Builds the set for a level.</summary>
    /// <param name="levelRoot">The level's task tree.</param>
    /// <param name="models">Resolves a model by name (LOD files are <c>name[..^1] + lod</c>).</param>
    /// <param name="magic">
    /// Resolves an attachment's magic-object definition, so the objects a placed model attaches -- a hangar's floor, its
    /// interior -- are placed too; <see langword="null"/> places the level's own tasks only.
    /// </param>
    /// <returns>The objects.</returns>
    public static RetailWorldObjects Build(
        GameTask levelRoot,
        Func<string, MefModel?> models,
        Func<string, MagicObjectDefinition?>? magic = null)
    {
        ArgumentNullException.ThrowIfNull(levelRoot);
        ArgumentNullException.ThrowIfNull(models);
        RetailWorldObjects set = new();
        Dictionary<string, (RetailRayMesh Mesh, float Radius)?> meshes = new(StringComparer.OrdinalIgnoreCase);
        foreach (GameTask task in Walk(levelRoot))
        {
            if (task is not DeclaredTask declared)
            {
                continue;
            }

            if (string.Equals(declared.Type.Name, WorldScene.SplineObjType, StringComparison.Ordinal))
            {
                set.AddSpline(declared, models);
                continue;
            }

            if (MovingTypes.Contains(declared.Type.Name)
                || Value(declared, WorldScene.ModelParameter) is not string name
                || name.Length == 0
                || Value(declared, "Position") is not Vector3d position
                || Value(declared, "Orientation") is not Vector3f angles)
            {
                continue;
            }

            if (!meshes.TryGetValue(name, out (RetailRayMesh Mesh, float Radius)? mesh))
            {
                mesh = models(name) is { } model
                    && MefCollisionMesh.TryRead(model.Container, meshIndex: 0, out MefCollisionMesh? level0)
                    && level0 is { Faces.Length: > 0 }
                        ? (new RetailRayMesh(level0), model.NativeMarkerBound)
                        : null;
                meshes[name] = mesh;
            }

            if (mesh is not { } found)
            {
                continue;
            }

            RetailRayModel placed = new(declared, found.Mesh, found.Radius);
            placed.Position[0] = position.X;
            placed.Position[1] = position.Y;
            placed.Position[2] = position.Z;
            RetailSplineSegment.AnglesToMatrix(angles.X, angles.Y, angles.Z).CopyTo(placed.M, 0);
            set.Add(placed, position, ((X87)found.Radius * 1.0f).Value);
            if (magic is not null && models(name) is { } root)
            {
                set.AddAttachments(declared, root, placed.Position, placed.M, models, magic, meshes, depth: 0);
            }
        }

        return set;
    }

    /// <summary>
    /// Places the objects a model attaches, and theirs, as <c>0x4DFA50</c> spawns them: each at the parent's orientation
    /// times its offset plus the parent's position, turned by the parent's orientation times the attachment's rebuilt
    /// frame, re-orthonormalised. The spawning task owns them all, so its container decides whether rays find them.
    /// </summary>
    private void AddAttachments(
        DeclaredTask owner,
        MefModel model,
        double[] position,
        float[] m,
        Func<string, MefModel?> models,
        Func<string, MagicObjectDefinition?> magic,
        Dictionary<string, (RetailRayMesh Mesh, float Radius)?> meshes,
        int depth)
    {
        // The original has no guard against an attachment naming its own model; its data has no such cycle.
        if (depth > 16)
        {
            return;
        }

        foreach (MefAttachment attachment in model.Attachments)
        {
            if (magic(attachment.Name) is not { } definition
                || definition.Model.Length == 0
                || models(definition.Model) is not { } child)
            {
                continue;
            }

            double ax = attachment.Position.X, ay = attachment.Position.Y, az = attachment.Position.Z;
            double wx = X87.Add(((X87)m[1] * ay + (X87)m[2] * az + (X87)m[0] * ax).Value, position[0]);
            double wy = X87.Add(((X87)m[4] * ay + (X87)m[5] * az + (X87)m[3] * ax).Value, position[1]);
            double wz = X87.Add(((X87)m[7] * ay + (X87)m[8] * az + (X87)m[6] * ax).Value, position[2]);
            float[] n = RetailPlaneSpawn.AttachmentBasis(attachment.Orientation);
            float[] turned =
            [
                ((X87)n[3] * m[1] + (X87)n[6] * m[2] + (X87)n[0] * m[0]).F,
                ((X87)n[4] * m[1] + (X87)n[7] * m[2] + (X87)n[1] * m[0]).F,
                ((X87)n[5] * m[1] + (X87)n[8] * m[2] + (X87)n[2] * m[0]).F,
                ((X87)n[3] * m[4] + (X87)n[6] * m[5] + (X87)n[0] * m[3]).F,
                ((X87)n[4] * m[4] + (X87)n[7] * m[5] + (X87)n[1] * m[3]).F,
                ((X87)n[5] * m[4] + (X87)n[8] * m[5] + (X87)n[2] * m[3]).F,
                ((X87)n[3] * m[7] + (X87)n[6] * m[8] + (X87)n[0] * m[6]).F,
                ((X87)n[4] * m[7] + (X87)n[7] * m[8] + (X87)n[1] * m[6]).F,
                ((X87)n[5] * m[7] + (X87)n[8] * m[8] + (X87)n[2] * m[6]).F,
            ];
            float[] frame = Orthonormalise(turned);

            if (!meshes.TryGetValue(definition.Model, out (RetailRayMesh Mesh, float Radius)? mesh))
            {
                mesh = MefCollisionMesh.TryRead(child.Container, meshIndex: 0, out MefCollisionMesh? level0)
                    && level0 is { Faces.Length: > 0 }
                        ? (new RetailRayMesh(level0), child.NativeMarkerBound)
                        : null;
                meshes[definition.Model] = mesh;
            }

            double[] placedPosition = [wx, wy, wz];
            if (mesh is { } found)
            {
                RetailRayModel placed = new(owner, found.Mesh, found.Radius);
                placed.Position[0] = wx;
                placed.Position[1] = wy;
                placed.Position[2] = wz;
                frame.CopyTo(placed.M, 0);
                Add(placed, new Vector3d(wx, wy, wz), ((X87)found.Radius * 1.0f).Value);
            }

            AddAttachments(owner, child, placedPosition, frame, models, magic, meshes, depth + 1);
        }
    }

    /// <summary><c>0x4DFA50</c>'s last pass: row 0 normalised, row 1 = row 2 × row 0, row 2 = row 0 × row 1, each normalised.</summary>
    private static float[] Orthonormalise(float[] v)
    {
        float[] n = new float[9];
        n[0] = v[0];
        n[1] = v[1];
        n[2] = v[2];
        if (!(v[0] == 0f && v[1] == 0f && v[2] == 0f))
        {
            X87 inverse = (X87)1.0 / X87.Sqrt((X87)v[0] * v[0] + (X87)v[1] * v[1] + (X87)v[2] * v[2]);
            n[0] = ((X87)v[0] * inverse).F;
            n[1] = ((X87)n[1] * inverse).F;
            n[2] = ((X87)n[2] * inverse).F;
        }

        float r1x = ((X87)v[2] * v[7] - (X87)v[1] * v[8]).F;
        float r1y = (-((X87)v[6] * v[2] - (X87)v[0] * v[8])).F;
        float r1z = ((X87)v[6] * v[1] - (X87)v[0] * v[7]).F;
        float r2x = ((X87)r1z * n[1] - (X87)r1y * n[2]).F;
        float r2y = (-((X87)r1z * n[0] - (X87)r1x * n[2])).F;
        float r2z = ((X87)r1y * n[0] - (X87)r1x * n[1]).F;
        n[3] = r1x;
        n[4] = r1y;
        n[5] = r1z;
        n[6] = r2x;
        n[7] = r2y;
        n[8] = r2z;
        if (!(r1x == 0f && r1y == 0f && r1z == 0f))
        {
            X87 inverse = (X87)1.0 / X87.Sqrt((X87)r1z * r1z + (X87)r1y * r1y + (X87)r1x * r1x);
            n[3] = ((X87)r1x * inverse).F;
            n[4] = ((X87)n[4] * inverse).F;
            n[5] = ((X87)n[5] * inverse).F;
        }

        if (!(r2x == 0f && r2y == 0f && r2z == 0f))
        {
            X87 inverse = (X87)1.0 / X87.Sqrt((X87)r2x * r2x + (X87)r2y * r2y + (X87)r2z * r2z);
            n[6] = ((X87)r2x * inverse).F;
            n[7] = ((X87)n[7] * inverse).F;
            n[8] = ((X87)n[8] * inverse).F;
        }

        return n;
    }

    /// <summary>Casts a batch of rays against every object whose box can reach them, in placement order.</summary>
    /// <param name="starts">The ray starts.</param>
    /// <param name="ends">The ray ends.</param>
    /// <param name="results">The results, initialised by the caller (no hit, point at the end).</param>
    /// <param name="excluded">An owner the query skips (the casting car), or <see langword="null"/>.</param>
    /// <param name="extra">Further objects to test first -- other vehicles at their live poses.</param>
    /// <param name="present">
    /// Whether an object's owner is in the world now: a task inside a dormant <c>ConditionalContainer</c> is not in
    /// the original's object tree, so its rays never find it. <see langword="null"/> takes every object as present.
    /// </param>
    public void Cast(
        ReadOnlySpan<Vector3d> starts,
        ReadOnlySpan<Vector3d> ends,
        Span<RetailRayResult> results,
        object? excluded,
        IReadOnlyList<RetailRayModel>? extra = null,
        Func<object, bool>? present = null)
    {
        if (starts.Length == 0)
        {
            return;
        }

        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        for (int i = 0; i < starts.Length; i++)
        {
            minX = Math.Min(minX, Math.Min(starts[i].X, ends[i].X));
            maxX = Math.Max(maxX, Math.Max(starts[i].X, ends[i].X));
            minY = Math.Min(minY, Math.Min(starts[i].Y, ends[i].Y));
            maxY = Math.Max(maxY, Math.Max(starts[i].Y, ends[i].Y));
        }

        _candidates.Clear();
        if (extra is not null)
        {
            foreach (RetailRayModel model in extra)
            {
                if (!ReferenceEquals(model.Owner, excluded))
                {
                    _candidates.Add(model);
                }
            }
        }

        _seen.Clear();
        List<int> hits = [];
        for (long cx = CellOf(minX - 2); cx <= CellOf(maxX + 2); cx++)
        {
            for (long cy = CellOf(minY - 2); cy <= CellOf(maxY + 2); cy++)
            {
                if (_grid.TryGetValue((cx, cy), out List<int>? list))
                {
                    foreach (int index in list)
                    {
                        if (_seen.Add(index))
                        {
                            hits.Add(index);
                        }
                    }
                }
            }
        }

        hits.Sort();
        foreach (int index in hits)
        {
            object candidate = _objects[index];
            object owner = candidate is RetailRayModel placed ? placed.Owner : ((RetailSplineSegment)candidate).Owner;
            if (ReferenceEquals(owner, excluded) || (present is not null && !present(owner)))
            {
                continue;
            }

            _candidates.Add(candidate);
        }

        foreach (object candidate in _candidates)
        {
            if (candidate is RetailSplineSegment segment)
            {
                // The segment handler runs the batch itself and leaves it at the first ray outside its box.
                segment.Cast(starts, ends, results);
                continue;
            }

            for (int i = 0; i < starts.Length; i++)
            {
                if (candidate is RetailRayModel model)
                {
                    RetailModelRay.Cast(model, starts[i], ends[i], ref results[i]);
                }
            }
        }
    }

    private static long CellOf(double v) => (long)Math.Floor(v / Cell);

    private void Add(object candidate, Vector3d centre, double radius)
    {
        int index = _objects.Count;
        _objects.Add(candidate);
        for (long cx = CellOf(centre.X - radius); cx <= CellOf(centre.X + radius); cx++)
        {
            for (long cy = CellOf(centre.Y - radius); cy <= CellOf(centre.Y + radius); cy++)
            {
                if (!_grid.TryGetValue((cx, cy), out List<int>? list))
                {
                    list = [];
                    _grid[(cx, cy)] = list;
                }

                list.Add(index);
            }
        }
    }

    /// <summary>One chain: a segment from each waypoint to the next, the later waypoint naming the model (<c>0x500EF0</c>).</summary>
    private void AddSpline(DeclaredTask spline, Func<string, MefModel?> models)
    {
        List<DeclaredTask> waypoints = [];
        foreach (GameTask child in spline.Children)
        {
            if (child is DeclaredTask waypoint
                && string.Equals(waypoint.Type.Name, WorldScene.SplineWaypointType, StringComparison.Ordinal))
            {
                waypoints.Add(waypoint);
            }
        }

        for (int i = 1; i < waypoints.Count; i++)
        {
            if (BuildSegment(waypoints[i - 1], waypoints[i], models, meshIndex: 0, out bool rigid) is { } segment)
            {
                Add(segment, new Vector3d(segment.Position[0], segment.Position[1], segment.Position[2]), segment.Radius);
            }
            else if (rigid)
            {
                SkippedRigidSegments++;
            }
        }
    }

    /// <summary>Builds and bends the segment a waypoint lays from the one before it, on one collision level.</summary>
    /// <param name="previous">The previous waypoint.</param>
    /// <param name="waypoint">The waypoint naming the segment model.</param>
    /// <param name="models">Resolves a model by name (LOD files are <c>name[..^1] + lod</c>).</param>
    /// <param name="meshIndex">
    /// The collision level to bend: 0, which rounds and cars' rays meet, or 1, which a human's movement meets
    /// (<c>0x461A70</c> and <c>0x462390</c> cast with <c>iMesh = 1</c>).
    /// </param>
    /// <param name="rigid">Receives whether the waypoint lays the segment rigid (path B, not transcribed).</param>
    /// <returns>The segment, or <see langword="null"/> when it cannot be built.</returns>
    public static RetailSplineSegment? BuildSegment(
        DeclaredTask previous,
        DeclaredTask waypoint,
        Func<string, MefModel?> models,
        int meshIndex,
        out bool rigid)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(waypoint);
        ArgumentNullException.ThrowIfNull(models);
        rigid = false;
        if (Value(waypoint, WorldScene.SegmentModelParameter) is not string name
            || name.Length == 0
            || models(name) is not { } model
            || !MefCollisionMesh.TryRead(model.Container, meshIndex: 0, out MefCollisionMesh? level0)
            || level0 is null
            || Value(previous, "Position") is not Vector3d p0
            || Value(waypoint, "Position") is not Vector3d p1
            || Value(previous, "Orientation") is not Vector3f a0
            || Value(waypoint, "Orientation") is not Vector3f a1
            || Value(waypoint, "NumAreas") is not int count
            || count < 1)
        {
            return null;
        }

        // Align (+0x1A1) lays the segment rigid, 0x4E16E0's path B, which is not transcribed.
        if (Value(waypoint, "Align") is true)
        {
            rigid = true;
            return null;
        }

        RetailRayMesh mesh0 = new(level0);
        List<RetailRayMesh> levels = [mesh0];
        RetailRayMesh? mesh1 = null;
        if (MefCollisionMesh.TryRead(model.Container, meshIndex: 1, out MefCollisionMesh? level1) && level1 is not null)
        {
            mesh1 = new RetailRayMesh(level1);
            levels.Add(mesh1);
        }

        RetailRayMesh? bent = meshIndex == 0 ? mesh0 : mesh1;
        if (bent is null)
        {
            return null;
        }

        // The model's x extent over every LOD's render vertices, from 0 (0x4E4530's tail).
        float x0 = 0f, x1 = 0f;
        for (int lod = 1; lod <= 9 && models(name[..^1] + lod) is { } level; lod++)
        {
            foreach (MefVertex vertex in level.Vertices)
            {
                x0 = vertex.Position.X < x0 ? vertex.Position.X : x0;
                x1 = vertex.Position.X > x1 ? vertex.Position.X : x1;
            }
        }

        if (!(x1 > x0))
        {
            return null;
        }

        return RetailSplineSegment.Build(
            waypoint,
            bent,
            levels,
            p0,
            p1,
            WaypointOrientation(previous),
            WaypointOrientation(waypoint),
            count,
            x0,
            x1,
            reverse: Value(waypoint, "Flip") is true);
    }

    /// <summary>A waypoint's orientation as the original leaves it, row-major: authored, or rebuilt from its neighbours.</summary>
    /// <param name="waypoint">The <c>SplineObjWaypoint</c>.</param>
    /// <returns>The 3x3 its segments are built from.</returns>
    /// <remarks>
    /// The waypoint update (<c>0x500EF0</c>) replaces the authored frame when the SplineObj's or the waypoint's own
    /// "Automatic Orientation" is set (and the SplineObj's Snap Length is not): the direction is
    /// <c>(p - previous) + (next - p)</c>, the side its horizontal perpendicular, the frame rebuilt by cross products
    /// and transposed, so the direction is the first column -- the tangent <c>0x4E4530</c> reads. Mission 10's rail
    /// (870) is laid this way; its authored orientations are not what the original runs on.
    /// </remarks>
    public static float[] WaypointOrientation(DeclaredTask waypoint)
    {
        ArgumentNullException.ThrowIfNull(waypoint);
        Vector3f angles = Value(waypoint, "Orientation") is Vector3f authored ? authored : default;
        if (!IsAutomaticallyOriented(waypoint)
            || waypoint.Parent is not DeclaredTask spline
            || Value(waypoint, "Position") is not Vector3d position)
        {
            return RetailSplineSegment.AnglesToMatrix(angles.X, angles.Y, angles.Z);
        }

        DeclaredTask? previous = null, next = null;
        bool passed = false;
        foreach (GameTask child in spline.Children)
        {
            if (child is not DeclaredTask sibling || !string.Equals(sibling.Type.Name, waypoint.Type.Name, StringComparison.Ordinal))
            {
                continue;
            }

            if (ReferenceEquals(sibling, waypoint))
            {
                passed = true;
            }
            else if (!passed)
            {
                previous = sibling;
            }
            else
            {
                next = sibling;
                break;
            }
        }

        Vector3d? before = previous is not null && Value(previous, "Position") is Vector3d p0 ? p0 : null;
        Vector3d? after = next is not null && Value(next, "Position") is Vector3d p1 ? p1 : null;
        if (before is null && after is null)
        {
            return RetailSplineSegment.AnglesToMatrix(angles.X, angles.Y, angles.Z);
        }

        float dx = 0f, dy = 0f, dz = 0f;
        if (before is { } b)
        {
            dx = (float)(position.X - b.X);
            dy = (float)(position.Y - b.Y);
            dz = (float)(position.Z - b.Z);
        }

        if (after is { } n)
        {
            dx = (float)((n.X - position.X) + dx);
            dy = (float)((n.Y - position.Y) + dy);
            dz = (float)((n.Z - position.Z) + dz);
        }

        // Row 0 is the direction, row 1 its horizontal perpendicular, row 2 = row1 x row0.
        float r0x = dx, r0y = dy, r0z = dz;
        if (r0x == 0f && r0y == 0f && r0z == 0f)
        {
            r0x = 1f;
        }

        float r1x, r1y;
        if (r0x == 0f && r0y == 0f)
        {
            r1x = 0f;
            r1y = 1f;
        }
        else
        {
            r1x = r0y;
            r1y = -r0x;
            if (r1x != 0f || r1y != 0f)
            {
                double inverse = 1.0 / Math.Sqrt(((double)r1x * r1x) + ((double)r1y * r1y));
                r1x = (float)(r1x * inverse);
                r1y = (float)(r1y * inverse);
            }
        }

        float r2x = (float)((double)r1y * r0z);
        float r2y = (float)-((double)r1x * r0z);
        float r2z = (float)(((double)r1x * r0y) - ((double)r1y * r0x));

        // Then row0 normalised, row1 = row2 x row0, row2 = row0 x row1 -- each from the unnormalised one before.
        (float n0x, float n0y, float n0z) = Normalised(r0x, r0y, r0z);
        float bx = (float)(((double)r0z * r2y) - ((double)r2z * r0y));
        float by = (float)-(((double)r0z * r2x) - ((double)r2z * r0x));
        float bz = (float)(((double)r0y * r2x) - ((double)r0x * r2y));
        float cx = (float)(((double)bz * n0y) - ((double)by * n0z));
        float cy = (float)-(((double)bz * n0x) - ((double)bx * n0z));
        float cz = (float)(((double)by * n0x) - ((double)bx * n0y));
        (float n1x, float n1y, float n1z) = Normalised(bx, by, bz);
        (float n2x, float n2y, float n2z) = Normalised(cx, cy, cz);

        // Stored transposed: the direction is the first column.
        return [n0x, n1x, n2x, n0y, n1y, n2y, n0z, n1z, n2z];

        static (float X, float Y, float Z) Normalised(float x, float y, float z)
        {
            if (x == 0f && y == 0f && z == 0f)
            {
                return (x, y, z);
            }

            double inverse = 1.0 / Math.Sqrt(((double)x * x) + ((double)y * y) + ((double)z * z));
            return ((float)(x * inverse), (float)(y * inverse), (float)(z * inverse));
        }
    }

    /// <summary>Whether <see cref="WaypointOrientation"/> rebuilds this waypoint's frame rather than keeping the authored one.</summary>
    /// <param name="waypoint">The <c>SplineObjWaypoint</c>.</param>
    /// <returns><see langword="true"/> under the SplineObj's or its own "Automatic Orientation", unless the SplineObj snaps.</returns>
    public static bool IsAutomaticallyOriented(DeclaredTask waypoint)
    {
        ArgumentNullException.ThrowIfNull(waypoint);
        return waypoint.Parent is DeclaredTask spline
            && Value(spline, "Snap Length") is not true
            && (Value(spline, "Automatic Orientation") is true || Value(waypoint, "Automatic Orientation") is true);
    }

    private static object? Value(DeclaredTask task, string name) => task.TryGetValue(name, out object? value) ? value : null;

    private static IEnumerable<GameTask> Walk(GameTask root)
    {
        foreach (GameTask child in root.Children)
        {
            yield return child;
            foreach (GameTask nested in Walk(child))
            {
                yield return nested;
            }
        }
    }
}
