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
    /// <returns>The objects.</returns>
    public static RetailWorldObjects Build(GameTask levelRoot, Func<string, MefModel?> models)
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
        }

        return set;
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
            DeclaredTask previous = waypoints[i - 1], waypoint = waypoints[i];
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
                continue;
            }

            // Align (+0x1A1) lays the segment rigid, 0x4E16E0's path B, which is not transcribed.
            if (Value(waypoint, "Align") is true)
            {
                SkippedRigidSegments++;
                continue;
            }

            RetailRayMesh mesh = new(level0);
            List<RetailRayMesh> levels = [mesh];
            if (MefCollisionMesh.TryRead(model.Container, meshIndex: 1, out MefCollisionMesh? level1) && level1 is not null)
            {
                levels.Add(new RetailRayMesh(level1));
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
                continue;
            }

            RetailSplineSegment segment = RetailSplineSegment.Build(
                waypoint,
                mesh,
                levels,
                p0,
                p1,
                RetailSplineSegment.AnglesToMatrix(a0.X, a0.Y, a0.Z),
                RetailSplineSegment.AnglesToMatrix(a1.X, a1.Y, a1.Z),
                count,
                x0,
                x1,
                reverse: Value(waypoint, "Flip") is true);
            Add(segment, new Vector3d(segment.Position[0], segment.Position[1], segment.Position[2]), segment.Radius);
        }
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
