using OpenIGI.Core.Numerics;
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

    private ObjectCollisionIndex(Entry[] entries) => _entries = entries;

    /// <summary>Gets how many placed objects carry collision geometry.</summary>
    public int Count => _entries.Length;

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

            entries.Add(Entry.Create(mesh, origin, placement.Orientation));
        }

        return new ObjectCollisionIndex([.. entries]);
    }

    /// <summary>Casts a ray at the placed objects.</summary>
    /// <param name="origin">Where the ray starts, in world units.</param>
    /// <param name="end">Where it stops.</param>
    /// <param name="hit">Receives the nearest contact.</param>
    /// <returns><see langword="false"/> when the ray reaches its end unobstructed.</returns>
    public bool TryCast(Vector3d origin, Vector3d end, out WorldRayHit hit)
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
            // The root sphere encloses every vertex - checked against all 254 of level 1's meshes - so
            // a ray that misses it cannot touch a triangle.
            if (!MeetsSphere(origin, direction, entry.Centre, entry.Radius))
            {
                continue;
            }

            if (entry.TryCast(origin, direction, ref nearest, out WorldRayHit candidate))
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
        private readonly Vector3d[] _vertices;

        private Entry(MefCollisionMesh mesh, Vector3d[] vertices, Vector3d centre, double radius)
        {
            Mesh = mesh;
            _vertices = vertices;
            Centre = centre;
            Radius = radius;
        }

        public MefCollisionMesh Mesh { get; }

        public Vector3d Centre { get; }

        public double Radius { get; }

        public static Entry Create(MefCollisionMesh mesh, Vector3d origin, Matrix3f orientation)
        {
            // The vertices are placed once at build time. A level's collision geometry does not move,
            // and transforming a ray into every model's own frame instead would cost more than it saves.
            Vector3d[] vertices = new Vector3d[mesh.Vertices.Count];
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3f local = orientation.Transform(mesh.Vertices[i]);
                vertices[i] = new Vector3d(origin.X + local.X, origin.Y + local.Y, origin.Z + local.Z);
            }

            Vector3d centre = origin;
            double radius = 0.0;

            if (mesh.Spheres.Count > 0)
            {
                MefCollisionSphere root = mesh.Spheres[0];
                Vector3f placed = orientation.Transform(root.Centre);
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

            return new Entry(mesh, vertices, centre, radius);
        }

        public bool TryCast(Vector3d origin, Vector3d direction, ref double nearest, out WorldRayHit hit)
        {
            hit = default;
            bool found = false;

            foreach (MefCollisionFace face in Mesh.Faces)
            {
                Vector3d a = _vertices[face.I0];
                Vector3d b = _vertices[face.I1];
                Vector3d c = _vertices[face.I2];

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
                    t * length);
            }

            return found;
        }
    }
}
