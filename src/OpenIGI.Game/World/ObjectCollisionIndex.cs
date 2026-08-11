using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;

namespace OpenIGI.Game.World;

/// <summary>
/// The placed objects a level is solid with, and the ray cast against them.
/// </summary>
/// <remarks>
/// <para>
/// The terrain octree says where the ground is; this says where the walls are. A model's collision
/// geometry is its <c>HSMC</c> chunk group (<see cref="MefCollisionMesh"/>) — a separate, far coarser
/// mesh than the one that draws, which is why a crate can be a box to walk into and a hundred triangles
/// to look at.
/// </para>
/// <para>
/// <b>Units.</b> A <see cref="WorldPlacement"/>'s position is in <b>metres</b> — the scene builder
/// divides by <see cref="WorldScene.UnitsPerMetre"/> for the renderer — while a collision mesh's
/// vertices and the player's own position are in <b>world units</b>. The conversion happens once, here,
/// at build time. Getting this wrong puts every object 4096 times too far away, which reads as no
/// collision at all rather than as an error.
/// </para>
/// <para>
/// <b>What this is not.</b> The original reaches its objects through the broad-phase octree that
/// <c>0x4C6940</c> commits positions into, and through <c>0x4CDB20</c>'s shape dispatch. Neither is
/// read. This walks a flat list, rejecting on each object's bounding sphere first. It finds the same
/// surfaces; it does more work to do it.
/// </para>
/// </remarks>
public sealed class ObjectCollisionIndex
{
    private readonly Entry[] _entries;
    private readonly Dictionary<GameTask, Entry[]> _entriesByOwner;
    private readonly Dictionary<WorldPlacement, Entry> _entriesByPlacement;

    // Legacy/manual passability override. Mission doors no longer use it: their collision mesh follows
    // the live task transform through UpdateTransform, including every half-open pose.
    private readonly HashSet<GameTask> _passable = [];

    private ObjectCollisionIndex(Entry[] entries)
    {
        _entries = entries;
        _entriesByOwner = new Dictionary<GameTask, Entry[]>(ReferenceEqualityComparer.Instance);
        _entriesByPlacement = new Dictionary<WorldPlacement, Entry>(ReferenceEqualityComparer.Instance);

        Dictionary<GameTask, List<Entry>> grouped = new(ReferenceEqualityComparer.Instance);
        foreach (Entry entry in entries)
        {
            if (!grouped.TryGetValue(entry.Owner, out List<Entry>? owned))
            {
                owned = [];
                grouped.Add(entry.Owner, owned);
            }

            owned.Add(entry);
            _entriesByPlacement[entry.Placement] = entry;
        }

        foreach ((GameTask owner, List<Entry> owned) in grouped)
        {
            _entriesByOwner.Add(owner, [.. owned]);
        }
    }

    /// <summary>Switches a placing task's collision off or back on — an opening or closing door.</summary>
    /// <param name="owner">The exact placing task instance.</param>
    /// <param name="passable">Whether rays should now pass through it.</param>
    public void SetPassable(GameTask owner, bool passable)
    {
        if (passable)
        {
            _passable.Add(owner);
        }
        else
        {
            _passable.Remove(owner);
        }
    }

    /// <summary>Updates the live transform of a moving placed object.</summary>
    /// <param name="owner">The exact placing task instance.</param>
    /// <param name="positionMetres">Its current world position in metres.</param>
    /// <param name="orientation">Its current local-to-world orientation.</param>
    /// <returns><see langword="true"/> when at least one indexed mesh belonged to the task.</returns>
    /// <remarks>
    /// Doors in IGI keep colliding while they swing or slide; only their transform changes. This replaces
    /// the old all-or-nothing passable approximation for live callers while retaining
    /// <see cref="SetPassable"/> for compatibility and diagnostic bakes.
    /// </remarks>
    public bool UpdateTransform(GameTask owner, Vector3d positionMetres, Matrix3f orientation)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (!_entriesByOwner.TryGetValue(owner, out Entry[]? entries))
        {
            return false;
        }

        foreach (Entry entry in entries)
        {
            entry.UpdateTransform(positionMetres, orientation);
        }

        return true;
    }

    /// <summary>Updates one model placement owned by a multi-model task.</summary>
    /// <param name="owner">The task owning the placement.</param>
    /// <param name="instanceName">The exact pack-instance name to move.</param>
    /// <param name="positionMetres">Its current origin in metres.</param>
    /// <param name="orientation">Its current orientation.</param>
    /// <returns><see langword="true"/> when a matching collision mesh was indexed.</returns>
    /// <remarks>
    /// An <c>SCamera</c> owns a fixed holder and an articulated head. Updating by owner alone would
    /// incorrectly move the holder onto the head, so camera ticks use this placement-specific form.
    /// </remarks>
    public bool UpdateTransform(
        GameTask owner,
        string instanceName,
        Vector3d positionMetres,
        Matrix3f orientation)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrEmpty(instanceName);
        if (!_entriesByOwner.TryGetValue(owner, out Entry[]? entries))
        {
            return false;
        }

        bool updated = false;
        foreach (Entry entry in entries)
        {
            if (!string.Equals(entry.InstanceName, instanceName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            entry.UpdateTransform(positionMetres, orientation);
            updated = true;
        }

        return updated;
    }

    /// <summary>Updates one exact placement, including duplicate attached instance names.</summary>
    public bool UpdateTransform(WorldPlacement placement, Vector3d positionMetres, Matrix3f orientation)
    {
        ArgumentNullException.ThrowIfNull(placement);
        if (!_entriesByPlacement.TryGetValue(placement, out Entry? entry))
        {
            return false;
        }

        entry.UpdateTransform(positionMetres, orientation);
        return true;
    }

    /// <summary>Gets how many placed objects carry collision geometry.</summary>
    public int Count => _entries.Length;

    /// <summary>
    /// Finds the moving collision object whose broad-phase volume already contains a point.
    /// </summary>
    /// <param name="position">The point in world units.</param>
    /// <param name="includeInactiveOwners">Whether dormant conditional-container objects participate.</param>
    /// <param name="owner">Receives the most specific containing moving owner.</param>
    /// <returns><see langword="true"/> when a moving-object volume contains the point.</returns>
    /// <remarks>
    /// Retail staged humans acquire their vehicle from BoneDynCubeObj's existing contact before
    /// Human_GroundCollision considers its fallback GetNextZ query. The collision sphere is this
    /// index's broad-phase equivalent of that existing dynamic-cube contact.
    /// </remarks>
    public bool TryFindContainingDynamicOwner(
        Vector3d position,
        bool includeInactiveOwners,
        out GameTask? owner)
    {
        owner = null;
        double bestScore = double.MaxValue;

        foreach (Entry entry in _entries)
        {
            if (!entry.IsDynamic
                || _passable.Contains(entry.Owner)
                || (!includeInactiveOwners && !ConditionalContainerGate.IsTaskActive(entry.Owner))
                || entry.Radius <= 0.0)
            {
                continue;
            }

            double dx = position.X - entry.Centre.X;
            double dy = position.Y - entry.Centre.Y;
            double dz = position.Z - entry.Centre.Z;
            double distanceSquared = (dx * dx) + (dy * dy) + (dz * dz);
            double radiusSquared = entry.Radius * entry.Radius;
            if (distanceSquared > radiusSquared)
            {
                continue;
            }

            // Prefer the tightest containing volume. Equal volumes deliberately select the later
            // placement: retail World_RegisterObj inserts every dynamic object at the HEAD of its
            // spatial list, and the rigid-object trace preserves the first equal-distance front-face
            // hit. WorldScene emits placements in task creation order, so the later entry is the same
            // object retail visits first. Mission 1 relies on this: the opening train and landing train
            // start with identical freight cars at the same transform, and Human 1666 must acquire the
            // later-created landing car 1888 rather than the opening train's anonymous car.
            double score = distanceSquared / radiusSquared;
            if (score <= bestScore)
            {
                bestScore = score;
                owner = entry.Owner;
            }
        }

        return owner is not null;
    }

    /// <summary>Gets how many triangles the index holds in total.</summary>
    public int TriangleCount
    {
        get
        {
            int total = 0;
            foreach (Entry entry in _entries)
            {
                total += entry.Mesh.Faces.Count;
            }

            return total;
        }
    }

    /// <summary>Builds an index over a scene's placements.</summary>
    /// <param name="placements">The level's placed objects.</param>
    /// <returns>The index; empty when nothing placed carries collision geometry.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="placements"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Placements whose model has no <c>HSMC</c> group are skipped rather than treated as solid. Most
    /// scenery is like that — a model is solid only if its author gave it a collision mesh.
    /// </remarks>
    public static ObjectCollisionIndex Build(IEnumerable<WorldPlacement> placements)
    {
        ArgumentNullException.ThrowIfNull(placements);

        List<Entry> entries = [];
        Dictionary<MefModel, MefCollisionMesh?> cache = [];

        foreach (WorldPlacement placement in placements)
        {
            if (!cache.TryGetValue(placement.Model, out MefCollisionMesh? mesh))
            {
                mesh = MefCollisionMesh.TryRead(placement.Model.Container, out MefCollisionMesh? read) ? read : null;
                cache[placement.Model] = mesh;
            }

            if (mesh is null || mesh.Faces.Count == 0)
            {
                continue;
            }

            Vector3d origin = new(
                placement.Position.X * WorldScene.UnitsPerMetre,
                placement.Position.Y * WorldScene.UnitsPerMetre,
                placement.Position.Z * WorldScene.UnitsPerMetre);

            string describedModel = ModelNameRegistry.DescribeModel(placement.Instance.Name);
            string label = string.IsNullOrEmpty(placement.TaskName)
                ? describedModel
                : $"{describedModel} [{placement.TaskName}]";

            entries.Add(Entry.Create(
                mesh, origin, placement.Orientation, placement.Instance.Name, label, placement.TaskName,
                placement.Owner, placement));
        }

        return new ObjectCollisionIndex([.. entries]);
    }

    /// <summary>Casts a ray at the placed objects.</summary>
    /// <param name="origin">Where the ray starts, in world units.</param>
    /// <param name="end">Where it stops.</param>
    /// <param name="hit">Receives the nearest contact.</param>
    /// <returns><see langword="false"/> when the ray reaches its end unobstructed.</returns>
    public bool TryCast(Vector3d origin, Vector3d end, out WorldRayHit hit) =>
        Cast(origin, end, honourPassable: true, out hit);

    /// <summary>Casts a rich request at the indexed placed objects.</summary>
    public bool TryTrace(in WorldTraceRequest request, out WorldTraceHit hit)
    {
        hit = default;

        Vector3d direction = new(
            request.End.X - request.Origin.X,
            request.End.Y - request.Origin.Y,
            request.End.Z - request.Origin.Z);
        double length = Math.Sqrt(
            (direction.X * direction.X) + (direction.Y * direction.Y) + (direction.Z * direction.Z));
        if (length <= 0.0)
        {
            return false;
        }

        double nearest = double.MaxValue;
        bool found = false;
        foreach (Entry entry in _entries)
        {
            if ((request.IgnoredOwner is not null && ReferenceEquals(entry.Owner, request.IgnoredOwner))
                || (request.IgnoredSubject is not null && ReferenceEquals(entry.Owner, request.IgnoredSubject))
                || _passable.Contains(entry.Owner)
                || (!request.IncludeInactiveOwners && !ConditionalContainerGate.IsTaskActive(entry.Owner)))
            {
                continue;
            }

            WorldTraceHitKind kind = entry.IsDynamic
                ? WorldTraceHitKind.DynamicObject
                : WorldTraceHitKind.StaticObject;
            WorldTraceMask category = entry.IsDynamic
                ? WorldTraceMask.DynamicObjects
                : WorldTraceMask.StaticObjects;
            if ((request.Mask & category) == 0
                || !MeetsSphere(request.Origin, direction, entry.Centre, entry.Radius)
                || !entry.TryCast(request.Origin, direction, ref nearest, useClosedTransform: false, out WorldRayHit candidate))
            {
                continue;
            }

            hit = WorldTraceHit.FromRayHit(request, candidate, kind, entry.Owner, entry.Label);
            found = true;
        }

        return found;
    }

    /// <summary>Casts a ray with every door treated as shut, whatever it is doing right now.</summary>
    /// <param name="origin">Where the ray starts, in world units.</param>
    /// <param name="end">Where it stops.</param>
    /// <param name="hit">Receives the nearest contact.</param>
    /// <returns><see langword="false"/> when the ray reaches its end unobstructed.</returns>
    /// <remarks>
    /// <para>
    /// For the probe bake, which wants both halves of that sentence. A door standing open when the bake
    /// runs would be traced as a hole in the wall it is part of, and the field is static, so the hole
    /// would still be there once the door shut.
    /// </para>
    /// <para>
    /// It is also the only cast safe to run off the main thread. <see cref="TryCast"/> reads
    /// <see cref="_passable"/>, which <see cref="SetPassable"/> mutates whenever a door moves; this
    /// touches nothing but the entry array, which never changes after
    /// <see cref="Build"/>. Ordering the bake before the doors open would answer the first point and
    /// not the second.
    /// </para>
    /// </remarks>
    public bool TryCastWithDoorsShut(Vector3d origin, Vector3d end, out WorldRayHit hit) =>
        Cast(origin, end, honourPassable: false, out hit);

    private bool Cast(Vector3d origin, Vector3d end, bool honourPassable, out WorldRayHit hit)
    {
        hit = default;

        Vector3d direction = new(end.X - origin.X, end.Y - origin.Y, end.Z - origin.Z);
        double length = Math.Sqrt((direction.X * direction.X) + (direction.Y * direction.Y) + (direction.Z * direction.Z));

        if (length <= 0.0)
        {
            return false;
        }

        double nearest = double.MaxValue;
        bool found = false;

        foreach (Entry entry in _entries)
        {
            if (honourPassable
                && (_passable.Contains(entry.Owner) || !ConditionalContainerGate.IsTaskActive(entry.Owner)))
            {
                continue;
            }

            // The root sphere encloses every vertex - checked against all 254 of level 1's meshes - so
            // a ray that misses it cannot touch a triangle.
            Vector3d broadCentre = honourPassable ? entry.Centre : entry.ClosedCentre;
            if (!MeetsSphere(origin, direction, broadCentre, entry.Radius))
            {
                continue;
            }

            if (entry.TryCast(origin, direction, ref nearest, useClosedTransform: !honourPassable, out WorldRayHit candidate))
            {
                hit = candidate;
                found = true;
            }
        }

        return found;
    }

    /// <summary>The bounding sphere of one indexed object, in world units.</summary>
    /// <param name="index">Its position in the index.</param>
    /// <returns>The sphere's centre and radius.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the index.</exception>
    public (Vector3d Centre, double Radius) Bounds(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _entries.Length);

        Entry entry = _entries[index];
        return (entry.Centre, entry.Radius);
    }

    /// <summary>The index of the object with the largest bounding sphere.</summary>
    /// <returns>Its position in the index, or −1 when nothing is indexed.</returns>
    /// <remarks>A building rather than a doorknob — the least ambiguous thing to aim a test at.</remarks>
    public int LargestIndex()
    {
        int best = -1;
        double radius = -1.0;

        for (int i = 0; i < _entries.Length; i++)
        {
            if (_entries[i].Radius > radius)
            {
                radius = _entries[i].Radius;
                best = i;
            }
        }

        return best;
    }

    /// <summary>Gets a cast delegate bound to this index.</summary>
    /// <returns>The delegate the sweep takes.</returns>
    public WorldRayCast Caster() => TryCast;

    /// <summary>Gets the rich trace delegate bound to this index.</summary>
    public WorldTraceCast Tracer() => TryTrace;

    /// <summary>Gets a cast delegate that treats every door as shut and reads no mutable state.</summary>
    /// <returns>The delegate the probe bake takes.</returns>
    /// <remarks>See <see cref="TryCastWithDoorsShut"/> for why the bake wants both properties.</remarks>
    public WorldRayCast CasterWithDoorsShut() => TryCastWithDoorsShut;

    /// <summary>Casts a ray and names the object it meets — a diagnostic for "what am I walking into".</summary>
    /// <param name="origin">Where the ray starts, in world units.</param>
    /// <param name="end">Where it stops.</param>
    /// <param name="label">Receives the struck object's instance name (and placing task).</param>
    /// <param name="hit">Receives the nearest contact.</param>
    /// <returns><see langword="false"/> when the ray reaches its end unobstructed.</returns>
    /// <remarks>
    /// Unlike <see cref="TryCast"/> this reports <i>which</i> placement was hit, so a blocked doorway can
    /// be traced back to the exact model and task that walled it off. It walks the same flat list.
    /// </remarks>
    public bool TryIdentify(Vector3d origin, Vector3d end, out string label, out WorldRayHit hit)
    {
        hit = default;
        label = string.Empty;

        Vector3d direction = new(end.X - origin.X, end.Y - origin.Y, end.Z - origin.Z);
        double length = Math.Sqrt((direction.X * direction.X) + (direction.Y * direction.Y) + (direction.Z * direction.Z));
        if (length <= 0.0)
        {
            return false;
        }

        double nearest = double.MaxValue;
        bool found = false;

        foreach (Entry entry in _entries)
        {
            if (!ConditionalContainerGate.IsTaskActive(entry.Owner))
            {
                continue;
            }

            if (!MeetsSphere(origin, direction, entry.Centre, entry.Radius))
            {
                continue;
            }

            if (entry.TryCast(origin, direction, ref nearest, useClosedTransform: false, out WorldRayHit candidate))
            {
                hit = candidate;
                label = entry.Label;
                found = true;
            }
        }

        return found;
    }

    private static bool MeetsSphere(Vector3d origin, Vector3d direction, Vector3d centre, double radius)
    {
        double ox = origin.X - centre.X;
        double oy = origin.Y - centre.Y;
        double oz = origin.Z - centre.Z;

        double a = (direction.X * direction.X) + (direction.Y * direction.Y) + (direction.Z * direction.Z);
        double b = 2.0 * ((ox * direction.X) + (oy * direction.Y) + (oz * direction.Z));
        double c = (ox * ox) + (oy * oy) + (oz * oz) - (radius * radius);

        if (c <= 0.0)
        {
            return true;
        }

        double discriminant = (b * b) - (4.0 * a * c);
        if (discriminant < 0.0)
        {
            return false;
        }

        // Only the segment counts, not the whole line.
        double root = Math.Sqrt(discriminant);
        double t0 = (-b - root) / (2.0 * a);
        double t1 = (-b + root) / (2.0 * a);

        return (t0 >= 0.0 && t0 <= 1.0) || (t1 >= 0.0 && t1 <= 1.0) || (t0 < 0.0 && t1 > 1.0);
    }

    private sealed class Entry
    {
        private readonly Vector3f[] _localVertices;
        private readonly Vector3d[] _closedVertices;
        private readonly Vector3f _rootCentre;
        private readonly Vector3d[] _vertices;

        private Entry(
            MefCollisionMesh mesh,
            Vector3f[] localVertices,
            Vector3d[] vertices,
            Vector3d centre,
            double radius,
            Vector3f rootCentre,
            string instanceName,
            string label,
            string taskName,
            GameTask owner,
            WorldPlacement placement)
        {
            Mesh = mesh;
            _localVertices = localVertices;
            _vertices = vertices;
            _closedVertices = (Vector3d[])vertices.Clone();
            _rootCentre = rootCentre;
            Centre = centre;
            ClosedCentre = centre;
            Radius = radius;
            InstanceName = instanceName;
            Label = label;
            TaskName = taskName;
            Owner = owner;
            Placement = placement;
        }

        public MefCollisionMesh Mesh { get; }

        public Vector3d Centre { get; private set; }

        public Vector3d ClosedCentre { get; }

        public double Radius { get; }

        public string InstanceName { get; }

        public string Label { get; }

        public string TaskName { get; }

        public GameTask Owner { get; }

        public WorldPlacement Placement { get; }

        public bool IsDynamic { get; private set; }

        public static Entry Create(
            MefCollisionMesh mesh,
            Vector3d origin,
            Matrix3f orientation,
            string instanceName,
            string label,
            string taskName,
            GameTask owner,
            WorldPlacement placement)
        {
            // Local-to-world is the COLUMN-vector product M·v — the renderer's `uModelView * v`, and
            // the convention the original itself uses for a placement's Real32x9 (it reads elements
            // [0],[3],[6], the first column, as the local X axis in world space). Matrix3f.Transform
            // is the row-vector product v·M, the inverse rotation: using it here spun every rotated
            // object's collision the wrong way round its origin, so fences could be walked through
            // and doorways sat behind solid wall. The transpose makes Transform compute M·v.
            Matrix3f rotation = orientation.Transposed();

            // The vertices are placed once at build time. A level's collision geometry does not move,
            // and transforming a ray into every model's own frame instead would cost more than it saves.
            Vector3f[] localVertices = new Vector3f[mesh.Vertices.Count];
            Vector3d[] vertices = new Vector3d[mesh.Vertices.Count];
            for (int i = 0; i < vertices.Length; i++)
            {
                localVertices[i] = mesh.Vertices[i];
                Vector3f local = rotation.Transform(localVertices[i]);
                vertices[i] = new Vector3d(origin.X + local.X, origin.Y + local.Y, origin.Z + local.Z);
            }

            Vector3d centre = origin;
            double radius = 0.0;
            Vector3f rootCentre = default;

            if (mesh.Spheres.Count > 0)
            {
                MefCollisionSphere root = mesh.Spheres[0];
                rootCentre = root.Centre;
                Vector3f placed = rotation.Transform(root.Centre);
                centre = new Vector3d(origin.X + placed.X, origin.Y + placed.Y, origin.Z + placed.Z);
                radius = root.Radius;
            }
            else
            {
                foreach (Vector3d vertex in vertices)
                {
                    double dx = vertex.X - centre.X;
                    double dy = vertex.Y - centre.Y;
                    double dz = vertex.Z - centre.Z;
                    radius = Math.Max(radius, Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz)));
                }
            }

            return new Entry(
                mesh, localVertices, vertices, centre, radius, rootCentre, instanceName, label, taskName, owner,
                placement);
        }

        public void UpdateTransform(Vector3d positionMetres, Matrix3f orientation)
        {
            Vector3d origin = new(
                positionMetres.X * WorldScene.UnitsPerMetre,
                positionMetres.Y * WorldScene.UnitsPerMetre,
                positionMetres.Z * WorldScene.UnitsPerMetre);
            Matrix3f rotation = orientation.Transposed();

            // This entry is single-thread-owned during live simulation. Reuse its transformed storage:
            // doors, trains and cutscene objects can move every tick, and allocating one array per pose
            // creates a GC feedback loop precisely while their animations are playing.
            Vector3d[] vertices = _vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3f placed = rotation.Transform(_localVertices[i]);
                vertices[i] = new Vector3d(origin.X + placed.X, origin.Y + placed.Y, origin.Z + placed.Z);
            }

            Vector3f centre = rotation.Transform(_rootCentre);
            Centre = new Vector3d(origin.X + centre.X, origin.Y + centre.Y, origin.Z + centre.Z);
            IsDynamic = true;
        }

        public bool TryCast(
            Vector3d origin,
            Vector3d direction,
            ref double nearest,
            bool useClosedTransform,
            out WorldRayHit hit)
        {
            hit = default;
            bool found = false;
            Vector3d[] vertices = useClosedTransform ? _closedVertices : _vertices;

            foreach (MefCollisionFace face in Mesh.Faces)
            {
                // A portal face is a doorway or window opening, not a wall. The original walks through it;
                // colliding with it walls off the door. See MefCollisionMesh.IsPortal.
                if (Mesh.IsPortal(face.Material))
                {
                    continue;
                }

                Vector3d a = vertices[face.I0];
                Vector3d b = vertices[face.I1];
                Vector3d c = vertices[face.I2];

                if (!RayTriangle.Intersect(origin, direction, a, b, c, out double t) || t >= nearest)
                {
                    continue;
                }

                Vector3d normal = RayTriangle.Normal(a, b, c);
                if (normal is { X: 0.0, Y: 0.0, Z: 0.0 })
                {
                    continue;
                }

                // Face the ray. The winding of a collision mesh is not established, and a normal
                // pointing away would push the body the wrong side of the wall.
                double facing = (normal.X * direction.X) + (normal.Y * direction.Y) + (normal.Z * direction.Z);
                if (facing > 0.0)
                {
                    normal = new Vector3d(-normal.X, -normal.Y, -normal.Z);
                }

                nearest = t;
                found = true;

                double length = Math.Sqrt(
                    (direction.X * direction.X) + (direction.Y * direction.Y) + (direction.Z * direction.Z));

                hit = new WorldRayHit(
                    new Vector3d(
                        origin.X + (direction.X * t),
                        origin.Y + (direction.Y * t),
                        origin.Z + (direction.Z * t)),
                    new Vector3f((float)normal.X, (float)normal.Y, (float)normal.Z),
                    t * length,
                    face.Material);
            }

            return found;
        }
    }
}
