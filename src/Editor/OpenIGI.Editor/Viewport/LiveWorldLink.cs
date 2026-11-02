using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using OpenIGI.Host.Editor;

namespace OpenIGI.Editor.Viewport;

/// <summary>
/// Keeps the running level's picture in step with the document: a task moved in the editor is drawn where it now is,
/// from the next frame, without reloading the level.
/// </summary>
/// <remarks>
/// <para>
/// The document and the running level are two trees built from the same script, so each document task with a place
/// is matched to what it placed (<see cref="PlacementKey"/>): by its level id where the title tags placements with one
/// (I.G.I. 2), otherwise to the running task of the same type and label whose placement is nearest the task's position
/// as loaded (I.G.I.). From then on a position edit — a gizmo drag, a typed value, an undo — becomes an
/// offset the renderer applies to everything that task placed.
/// </para>
/// <para>
/// Only the picture follows. Collision, lighting and AI read the level as it was built, and catch up when the level
/// is reloaded from what was saved.
/// </para>
/// </remarks>
internal sealed class LiveWorldLink(IEditorHost host, MissionWorkspace workspace)
{
    private const double MatchToleranceMetres = 0.05;

    private readonly Dictionary<MissionTask, (Vector3d Loaded, Matrix3f Turned, PlacementKey Key)> _links = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<PlacementKey, MissionTask> _tasksByKey = [];
    private readonly Dictionary<PlacementKey, EditorPose> _poses = [];
    private readonly Dictionary<MefModel, (Vector3d Centre, Vector3d Half)> _bounds = new(ReferenceEqualityComparer.Instance);
    private MissionDocument? _bound;
    private IReadOnlyList<WorldPlacement>? _boundPlacements;

    /// <summary>Gets how many document tasks are linked to the running level.</summary>
    public int LinkedCount => _links.Count;

    /// <summary>Gets the radius, in metres, of everything a task placed, measured from the task's position.</summary>
    /// <param name="task">The task.</param>
    /// <returns>The radius, or <see langword="null"/> when the task is not linked to anything placed.</returns>
    public double? RadiusMetres(MissionTask task)
    {
        if (!_links.TryGetValue(task, out (Vector3d Loaded, Matrix3f Turned, PlacementKey Key) link) || _boundPlacements is not { } placements)
        {
            return null;
        }

        double radius = 0.0;
        foreach (WorldPlacement placement in placements)
        {
            if (PlacementKey.Of(placement) == link.Key)
            {
                // The model's sphere is in world units; the placement's position is in metres.
                double reach = (placement.Position - link.Loaded).Length + WorldUnits.ToMetres(placement.Model.RenderSphereRadius);
                radius = Math.Max(radius, reach);
            }
        }

        return radius > 0.0 ? radius : null;
    }

    /// <summary>Binds to a newly opened document or rebuilt level. Called once a frame.</summary>
    public void Update()
    {
        MissionDocument? document = workspace.Document;
        IReadOnlyList<WorldPlacement> placements = host.Placements;
        if (ReferenceEquals(document, _bound) && ReferenceEquals(placements, _boundPlacements))
        {
            return;
        }

        if (_bound is not null)
        {
            _bound.Changed -= OnChanged;
        }

        _bound = document;
        _boundPlacements = placements;
        _links.Clear();
        _tasksByKey.Clear();
        _poses.Clear();
        if (document is null || placements.Count == 0)
        {
            return;
        }

        document.Changed += OnChanged;
        Bind(document, placements);
    }

    private void Bind(MissionDocument document, IReadOnlyList<WorldPlacement> placements)
    {
        // What each placing task put in the world: by level id where the title tags its placements with one, and
        // otherwise every running owner with the placement it put nearest its own spot, grouped by type and label.
        Dictionary<int, PlacementKey> byId = [];
        Dictionary<(string Type, string Label), List<(PlacementKey Key, Vector3d At)>> byName = [];
        HashSet<PlacementKey> seen = [];
        foreach (WorldPlacement placement in placements)
        {
            PlacementKey key = PlacementKey.Of(placement);
            if (!seen.Add(key))
            {
                continue;
            }

            if (key.LevelId >= 0)
            {
                byId.TryAdd(key.LevelId, key);
                continue;
            }

            (string, string) name = (placement.Owner.Type.Name, placement.Owner.Label);
            if (!byName.TryGetValue(name, out List<(PlacementKey Key, Vector3d At)>? list))
            {
                byName[name] = list = [];
            }

            list.Add((key, placement.Position));
        }

        foreach (MissionTask task in document.EnumerateTasks())
        {
            if (TaskPresentation.PositionMetres(task) is not { } loaded)
            {
                continue;
            }

            if (task.Id >= 0 && byId.TryGetValue(task.Id, out PlacementKey tagged))
            {
                Link(task, loaded, tagged);
                continue;
            }

            if (!byName.TryGetValue((task.TypeName, task.Label), out List<(PlacementKey Key, Vector3d At)>? candidates) || candidates.Count == 0)
            {
                continue;
            }

            (PlacementKey Key, Vector3d At) best = candidates.MinBy(c => (c.At - loaded).LengthSquared);
            if ((best.At - loaded).Length <= MatchToleranceMetres)
            {
                Link(task, loaded, best.Key);
                candidates.Remove(best);
            }
        }
    }

    private void Link(MissionTask task, Vector3d loaded, PlacementKey key)
    {
        _links[task] = (loaded, TaskPresentation.OrientationMatrix(task), key);
        _tasksByKey[key] = task;
    }

    private void OnChanged(MissionChange change)
    {
        if (change.Kind != MissionChangeKind.Parameter
            || !_links.TryGetValue(change.Task, out (Vector3d Loaded, Matrix3f Turned, PlacementKey Key) link)
            || TaskPresentation.PositionMetres(change.Task) is not { } now)
        {
            return;
        }

        // The turn in world space from the orientation the level was built with to the one the document has now.
        Matrix3f turn = TaskPresentation.OrientationMatrix(change.Task).Transposed() * link.Turned;
        EditorPose pose = new(link.Loaded, now - link.Loaded, turn);
        _poses[link.Key] = pose;
        host.SetPlacementPose(link.Key, pose);
    }

    /// <summary>Gets the boxes around everything a task placed, where the editor has it now, in world metres.</summary>
    /// <param name="task">The task.</param>
    /// <returns>One box per placed model; none for a task not linked to anything drawn.</returns>
    public IEnumerable<BoxGizmo.Box> Boxes(MissionTask task)
    {
        if (!_links.TryGetValue(task, out (Vector3d Loaded, Matrix3f Turned, PlacementKey Key) link) || _boundPlacements is not { } placements)
        {
            yield break;
        }

        _poses.TryGetValue(link.Key, out EditorPose pose);
        foreach (WorldPlacement placement in placements)
        {
            if (placement.IsSuppressed || PlacementKey.Of(placement) != link.Key)
            {
                continue;
            }

            (Vector3d centre, Vector3d half) = BoundsOf(placement.Model);
            Vector3d middle = placement.Position + placement.Orientation.Transposed().Transform(centre / WorldUnits.PerMetre);
            Matrix3f orientation = placement.Orientation;
            if (pose.Turn != default)
            {
                middle = pose.Pivot + pose.Offset + pose.Turn.Transform(middle - pose.Pivot);
                orientation = orientation * pose.Turn.Transposed();
            }

            yield return new BoxGizmo.Box(middle, orientation, half / WorldUnits.PerMetre);
        }
    }

    // A model's bounds in its own space and units, its vertices' extent, worked out once.
    private (Vector3d Centre, Vector3d Half) BoundsOf(MefModel model)
    {
        if (_bounds.TryGetValue(model, out (Vector3d, Vector3d) known))
        {
            return known;
        }

        Vector3d min = new(double.MaxValue, double.MaxValue, double.MaxValue);
        Vector3d max = new(double.MinValue, double.MinValue, double.MinValue);
        foreach (MefVertex vertex in model.Vertices)
        {
            Vector3f p = vertex.Position;
            min = new Vector3d(Math.Min(min.X, p.X), Math.Min(min.Y, p.Y), Math.Min(min.Z, p.Z));
            max = new Vector3d(Math.Max(max.X, p.X), Math.Max(max.Y, p.Y), Math.Max(max.Z, p.Z));
        }

        (Vector3d, Vector3d) bounds = model.Vertices.Length == 0 ? (Vector3d.Zero, Vector3d.Zero) : ((min + max) * 0.5, (max - min) * 0.5);
        _bounds[model] = bounds;
        return bounds;
    }

    /// <summary>Finds the task whose drawn model a ray meets first, where the editor has it now.</summary>
    /// <param name="origin">The ray's start, in world metres.</param>
    /// <param name="direction">Its direction; need not be unit length.</param>
    /// <param name="reachMetres">How far to look.</param>
    /// <param name="distanceMetres">Receives how far along the ray the hit is.</param>
    /// <param name="ignore">A task whose models the ray passes through, such as one being dragged; none when null.</param>
    /// <returns>The task, or <see langword="null"/> when the ray meets none of the models.</returns>
    /// <remarks>
    /// Each placement's bounding sphere is tested first and its triangles only when the sphere is met. The ray is taken
    /// into the placement's own space rather than every vertex out of it: the editor's offset for the task undone, then
    /// into the model's frame (a placement maps model to world through its orientation's transpose) and its units.
    /// </remarks>
    public MissionTask? Pick(Vector3d origin, Vector3d direction, double reachMetres, out double distanceMetres, MissionTask? ignore = null)
    {
        distanceMetres = double.MaxValue;
        double length = direction.Length;
        if (_boundPlacements is not { } placements || length <= 0.0)
        {
            return null;
        }

        Vector3d unit = direction / length;
        MissionTask? found = null;
        foreach (WorldPlacement placement in placements)
        {
            PlacementKey key = PlacementKey.Of(placement);
            if (placement.IsSuppressed || !_tasksByKey.TryGetValue(key, out MissionTask? task) || ReferenceEquals(task, ignore))
            {
                continue;
            }

            Vector3d from = origin;
            Vector3d along = unit;
            if (_poses.TryGetValue(key, out EditorPose pose))
            {
                Matrix3f back = pose.Turn.Transposed();
                from = pose.Pivot + back.Transform(origin - pose.Pivot - pose.Offset);
                along = back.Transform(unit);
            }

            MefModel model = placement.Model;
            Vector3d localFrom = placement.Orientation.Transform((from - placement.Position) * WorldUnits.PerMetre);
            Vector3d localAlong = placement.Orientation.Transform(along) * (reachMetres * WorldUnits.PerMetre);
            if (!MeetsSphere(localFrom, localAlong, model.RenderSphereCentre, model.RenderSphereRadius))
            {
                continue;
            }

            ReadOnlySpan<MefVertex> vertices = model.Vertices;
            foreach ((int a, int b, int c) in model.Triangles())
            {
                if (RayTriangle.Intersect(localFrom, localAlong, Point(vertices[a]), Point(vertices[b]), Point(vertices[c]), out double t)
                    && t >= 0.0 && t * reachMetres < distanceMetres)
                {
                    distanceMetres = t * reachMetres;
                    found = task;
                }
            }
        }

        return found;

        static Vector3d Point(in MefVertex vertex) => new(vertex.Position.X, vertex.Position.Y, vertex.Position.Z);
    }

    // Whether a segment from a point along a vector passes within a radius of a centre.
    private static bool MeetsSphere(Vector3d from, Vector3d along, Vector3f centre, float radius)
    {
        Vector3d toCentre = new Vector3d(centre.X, centre.Y, centre.Z) - from;
        double lengthSquared = Vector3d.Dot(along, along);
        double t = lengthSquared > 0.0 ? Math.Clamp(Vector3d.Dot(toCentre, along) / lengthSquared, 0.0, 1.0) : 0.0;
        Vector3d nearest = from + (along * t) - new Vector3d(centre.X, centre.Y, centre.Z);
        return Vector3d.Dot(nearest, nearest) <= (double)radius * radius;
    }
}
