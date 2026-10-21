using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats;
using OpenIGI.Formats.World;

namespace OpenIGI.Game.World;

/// <summary>
/// One placed collision mesh in an <see cref="ObjectCollisionIndex"/>: its vertices placed in the world, its
/// bounding sphere, its owner and placement, and the triangle and retail-arithmetic ray tests against it.
/// </summary>
internal sealed class ObjectCollisionEntry
{
    private readonly Vector3f[] _localVertices;
    private readonly Vector3d[] _closedVertices;
    private readonly Vector3f _rootCentre;
    private readonly Vector3d[] _vertices;

    private ObjectCollisionEntry(
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
        WorldPlacement placement,
        Vector3d origin,
        Matrix3f orientation)
    {
        Mesh = mesh;
        PoseOrigin = origin;
        PoseOrientation = orientation;
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
        ClosedBounds = BoundsOf(vertices);
    }

    public MefCollisionMesh Mesh { get; }

    public Vector3d Centre { get; private set; }

    public Vector3d ClosedCentre { get; private set; }

    public double Radius { get; }

    public string InstanceName { get; }

    public string Label { get; }

    public string TaskName { get; }

    public GameTask Owner { get; }

    public WorldPlacement Placement { get; }

    public bool IsDynamic { get; private set; }

    /// <summary>Gets where the mesh's origin sits now, in world units.</summary>
    public Vector3d PoseOrigin { get; private set; }

    /// <summary>Gets the mesh's current local-to-world orientation.</summary>
    public Matrix3f PoseOrientation { get; private set; }

    public (Vector3d Min, Vector3d Max) ClosedBounds { get; }

    /// <summary>Gets whether this entry belongs to the human (level-one) trace set.</summary>
    public bool IsPlayerSet { get; init; }

    /// <summary>Gets whether this entry is the placement's destroyed model, solid only once it is destroyed.</summary>
    public bool IsDestroyedModel { get; init; }

    /// <summary>Gets or sets whether the entry has been moved to its set's dynamic list.</summary>
    public bool InDynamicList { get; set; }

    /// <summary>
    /// Gets whether the original's placed-model ray (<c>0x4C1840</c>) tests this entry: every model but a bent
    /// spline segment, which the original tests with a routine of its own (<c>0x4E16E0</c>).
    /// </summary>
    public bool SupportsRetailRay => Placement.BentGeometry is null && Mesh.Faces.Length > 0;

    private RetailRayModel? _retailRay;

    /// <summary>Casts a ray at this model with the original's own arithmetic (<see cref="RetailModelRay"/>).</summary>
    /// <param name="origin">The ray start, in world units.</param>
    /// <param name="end">The ray end.</param>
    /// <param name="nearest">The nearest hit so far as a fraction of the ray; a hit must be nearer to count.</param>
    /// <param name="hit">Receives the contact, reported as <see cref="TryCast"/> reports one.</param>
    /// <returns>Whether this model was struck nearer than <paramref name="nearest"/>.</returns>
    /// <remarks>
    /// The original transforms the ray into the model's frame, rounds it to single precision and accepts a face
    /// up to and including the ray's end (<c>t ≤ 1</c> as a float). A human's wall sweep ends its re-cast
    /// exactly on the wall it has just slid off, so that boundary decides whether a slide into a corner is
    /// accepted or refused: the original's test meets the wall, the sweep slides again and again, and after
    /// ten casts it refuses the move (<c>0x461A70</c>). The double-precision test here stopped a hair short.
    /// </remarks>
    public bool TryCastRetail(Vector3d origin, Vector3d end, ref double nearest, out WorldRayHit hit)
    {
        hit = default;
        Vector3d direction = new(end.X - origin.X, end.Y - origin.Y, end.Z - origin.Z);
        double length = Math.Sqrt((direction.X * direction.X) + (direction.Y * direction.Y) + (direction.Z * direction.Z));
        if (length <= 0.0)
        {
            return false;
        }

        _retailRay ??= new RetailRayModel(Owner, new RetailRayMesh(Mesh), Placement.Model.NativeMarkerBound);
        _retailRay.Position[0] = PoseOrigin.X;
        _retailRay.Position[1] = PoseOrigin.Y;
        _retailRay.Position[2] = PoseOrigin.Z;
        PoseOrientation.CopyTo(_retailRay.M);

        RetailRayResult result = new() { Point = end };
        RetailModelRay.Cast(_retailRay, origin, end, ref result);
        if (!result.Hit)
        {
            return false;
        }

        double t = result.Distance / length;
        if (t >= nearest)
        {
            return false;
        }

        nearest = t;

        // The original's face normal is the opposite of RayTriangle's, and it is not unit length. Reported as
        // TryCast reports one: unit, turned to face the ray, with the back face that a probe leaves by marked.
        Vector3d normal = new(result.NormalX, result.NormalY, result.NormalZ);
        double normalLength = Math.Sqrt((normal.X * normal.X) + (normal.Y * normal.Y) + (normal.Z * normal.Z));
        if (normalLength > 0.0)
        {
            normal /= normalLength;
        }

        if ((normal.X * direction.X) + (normal.Y * direction.Y) + (normal.Z * direction.Z) > 0.0)
        {
            normal = new Vector3d(-normal.X, -normal.Y, -normal.Z);
        }

        hit = new WorldRayHit(
            result.Point,
            new Vector3f((float)normal.X, (float)normal.Y, (float)normal.Z),
            result.Distance,
            result.Material,
            !result.HasFrontMaterial);
        return true;
    }

    public static ObjectCollisionEntry Create(
        MefCollisionMesh mesh,
        Vector3d origin,
        Matrix3f orientation,
        string instanceName,
        string label,
        string taskName,
        GameTask owner,
        WorldPlacement placement,
        bool isPlayerSet = false,
        bool isDestroyedModel = false)
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

        return new ObjectCollisionEntry(
            mesh, localVertices, vertices, centre, radius, rootCentre, instanceName, label, taskName, owner,
            placement, origin, orientation)
        {
            IsPlayerSet = isPlayerSet,
            IsDestroyedModel = isDestroyedModel,
        };
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
        PoseOrigin = origin;
        PoseOrientation = orientation;
        IsDynamic = true;
    }

    public void Translate(Vector3d displacementUnits)
    {
        for (int i = 0; i < _vertices.Length; i++)
        {
            _vertices[i] += displacementUnits;
            _closedVertices[i] += displacementUnits;
        }

        Centre += displacementUnits;
        ClosedCentre += displacementUnits;
        PoseOrigin += displacementUnits;
        IsDynamic = true;
    }

    private static (Vector3d Min, Vector3d Max) BoundsOf(Vector3d[] vertices)
    {
        Vector3d min = new(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity);
        Vector3d max = new(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity);
        foreach (Vector3d vertex in vertices)
        {
            min = new(Math.Min(min.X, vertex.X), Math.Min(min.Y, vertex.Y), Math.Min(min.Z, vertex.Z));
            max = new(Math.Max(max.X, vertex.X), Math.Max(max.Y, vertex.Y), Math.Max(max.Z, vertex.Z));
        }

        return (min, max);
    }

    public void CollectTriangles(Vector3d min, Vector3d max, List<CollisionTriangle> into)
    {
        foreach (MefCollisionFace face in Mesh.Faces)
        {
            if (Mesh.IsPortal(face.Material))
            {
                continue;
            }

            CollisionTriangle triangle = new(_vertices[face.I0], _vertices[face.I1], _vertices[face.I2]);
            if (triangle.Overlaps(min, max))
            {
                into.Add(triangle);
            }
        }
    }

    public bool TryCast(
        Vector3d origin,
        Vector3d direction,
        ref double nearest,
        bool useClosedTransform,
        bool includePortals,
        Func<Vector3d, bool>? portalIsOpening,
        out WorldRayHit hit)
    {
        hit = default;
        bool found = false;
        Vector3d[] vertices = useClosedTransform ? _closedVertices : _vertices;

        foreach (MefCollisionFace face in Mesh.Faces)
        {
            // Portal polygons are visibility boundaries, not solid building geometry. Generic
            // doorway/window portals stay clear for sight, while a Door task may opt its own
            // portal faces into the cast so the authored closed/open state remains effective.
            if (Mesh.IsPortal(face.Material))
            {
                if (!includePortals)
                {
                    continue;
                }

                Vector3d a0 = vertices[face.I0];
                Vector3d b0 = vertices[face.I1];
                Vector3d c0 = vertices[face.I2];
                Vector3d mid = new(
                    (a0.X + b0.X + c0.X) / 3.0,
                    (a0.Y + b0.Y + c0.Y) / 3.0,
                    (a0.Z + b0.Z + c0.Z) / 3.0);
                if (portalIsOpening?.Invoke(mid) == true)
                {
                    continue;
                }
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
            //
            // The sign before that flip is not noise: it is the front/back test the original's
            // surface routine reports as its entry and exit rows (0x4CD300 sets them as the two
            // halves of one condition, so exactly one is ever set). A round leaving a surface it
            // had entered crosses a back face, and the shot resolution neither sounds nor marks
            // that crossing the way it does an entry.
            double facing = (normal.X * direction.X) + (normal.Y * direction.Y) + (normal.Z * direction.Z);
            bool towardsTheRay = facing > 0.0;
            if (towardsTheRay)
            {
                normal = new Vector3d(-normal.X, -normal.Y, -normal.Z);
            }

            // Which side that is was measured, not assumed. Fanning 64 rays in from outside the
            // geometry around the level-one doorway, ALL 64 first crossings have a positive facing,
            // so these meshes wind their normals INTO the solid and a positive facing is the face a
            // round enters by. The flip above is independent of that: it only turns the reported
            // normal to oppose the ray so a decal sits proud of the surface.
            bool leaving = !towardsTheRay;

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
                // Report the global game-material row, not the per-mesh face index, so callers can
                // name the surface (glass, fence, concrete) against the shared material table.
                Mesh.GlobalMaterial(face.Material),
                leaving);
        }

        return found;
    }
}
