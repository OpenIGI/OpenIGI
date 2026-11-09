using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World.Fracture;

/// <summary>One piece a mesh was broken into: its faces, the old surface it kept and the new faces the cuts made.</summary>
/// <param name="Faces">Its faces, in the source mesh's space.</param>
/// <param name="Centre">The middle of its corners, in the source mesh's space: where it turns about.</param>
/// <param name="Radius">How far its farthest corner is from <paramref name="Centre"/>.</param>
/// <param name="Volume">Its bounding box's volume, which its mass is weighed by.</param>
public sealed record FractureChunk(IReadOnlyList<FracturePolygon> Faces, Vector3f Centre, float Radius, float Volume);

/// <summary>
/// Breaks a mesh into pieces along the cells of a Voronoi diagram: each piece is the part of the mesh nearer one seed than
/// every other, closed where it was cut by faces of its own.
/// </summary>
/// <remarks>
/// <para>
/// A cell is the intersection of the half-spaces bisecting its seed from every other seed (and of any bounds the caller
/// adds), so it is convex and every piece is the mesh clipped by a handful of planes. The mesh's own faces are clipped into
/// it as they are, keeping their textures; then, on each plane of the cell, the points the clipping left on that plane are
/// closed into a face of their own -- the convex hull of them -- facing out of the cell: the broken face of the piece, the
/// concrete or the timber inside it. Hull faces rather than traced outlines, so an open mesh -- a wall that is two parallel
/// sheets, a crate whose lid is missing -- still breaks into pieces with sides.
/// </para>
/// <para>
/// Clean-room: written from the geometry of Voronoi cells and polygon clipping, not from any engine's fracture tool.
/// Deterministic: the same mesh and seeds always give the same pieces in the same order.
/// </para>
/// </remarks>
public static class VoronoiFracture
{
    /// <summary>How many metres of the inside's texture a metre of a broken face shows.</summary>
    public const float InteriorTextureScale = 0.5f;

    /// <summary>Breaks a mesh's faces into the pieces of the seeds' cells.</summary>
    /// <param name="surface">The faces to break, in one space.</param>
    /// <param name="seeds">The cells' seeds, in the same space; fewer than two leaves one piece.</param>
    /// <param name="bounds">Planes every cell is also clipped by: the edge of what is broken off; empty for none.</param>
    /// <param name="minimumArea">The smallest face a piece keeps; slivers below it are dropped.</param>
    /// <returns>The pieces that kept any of the surface, in seed order.</returns>
    public static List<FractureChunk> Break(
        IReadOnlyList<FracturePolygon> surface, ReadOnlySpan<Vector3f> seeds, ReadOnlySpan<FracturePlane> bounds, float minimumArea)
    {
        ArgumentNullException.ThrowIfNull(surface);
        List<FractureChunk> chunks = [];
        if (surface.Count == 0 || seeds.Length == 0)
        {
            return chunks;
        }

        Shape shape = new(surface);
        List<FracturePlane> planes = new(bounds.Length + seeds.Length);
        for (int cell = 0; cell < seeds.Length; cell++)
        {
            planes.Clear();
            planes.AddRange(bounds.ToArray());
            for (int other = 0; other < seeds.Length; other++)
            {
                if (other != cell && (seeds[other] - seeds[cell]).LengthSquared > 1e-10f)
                {
                    planes.Add(FracturePlane.Between(seeds[cell], seeds[other]));
                }
            }

            if (Cell(shape, planes, minimumArea) is { } chunk)
            {
                chunks.Add(chunk);
            }
        }

        return chunks;
    }

    // The mesh being broken, with what every cell asks of it: its size, its outline on a cutting plane (shared by the two
    // cells either side of the plane, so worked out once), and whether a point is inside it.
    private sealed class Shape
    {
        private readonly Dictionary<(int, int, int, int), List<Vector3f>?> _sections = [];

        public Shape(IReadOnlyList<FracturePolygon> surface)
        {
            Surface = surface;
            (Vector3f low, Vector3f high) = Box(surface);
            Middle = (low + high) * 0.5f;
            Size = MathF.Max((high - low).Length, 1e-4f);
            OnPlane = MathF.Max(Size * 1e-4f, 1e-5f);
        }

        public IReadOnlyList<FracturePolygon> Surface { get; }

        public Vector3f Middle { get; }

        public float Size { get; }

        public float OnPlane { get; }

        // The convex outline the mesh makes on a plane: every point where its faces meet the plane, hulled. Exact for a
        // closed convex mesh; for an open or hollow one, the outline filled in, as a broken wall of two sheets is solid
        // between them.
        public List<Vector3f>? Section(in FracturePlane plane)
        {
            // One key for a plane and its flip: the cells on either side share it.
            bool flip = plane.Normal.X < 0f || (plane.Normal.X == 0f && (plane.Normal.Y < 0f || (plane.Normal.Y == 0f && plane.Normal.Z < 0f)));
            Vector3f n = flip ? plane.Normal * -1f : plane.Normal;
            float offset = flip ? -plane.Offset : plane.Offset;
            const float Grain = 1e5f;
            var key = ((int)MathF.Round(n.X * Grain), (int)MathF.Round(n.Y * Grain), (int)MathF.Round(n.Z * Grain), (int)MathF.Round(offset / OnPlane));
            if (_sections.TryGetValue(key, out List<Vector3f>? known))
            {
                return known;
            }

            List<Vector3f> points = [];
            foreach (FracturePolygon face in Surface)
            {
                List<Vertex3D> corners = face.Vertices;
                for (int i = 0; i < corners.Count; i++)
                {
                    Vector3f a = corners[i].Position;
                    Vector3f b = corners[(i + 1) % corners.Count].Position;
                    float da = plane.Distance(a);
                    float db = plane.Distance(b);
                    if (MathF.Abs(da) <= OnPlane)
                    {
                        points.Add(a);
                    }
                    else if ((da < -OnPlane && db > OnPlane) || (da > OnPlane && db < -OnPlane))
                    {
                        points.Add(a + ((b - a) * (da / (da - db))));
                    }
                }
            }

            List<Vector3f>? section = null;
            if (points.Count >= 3)
            {
                (Vector3f across, Vector3f up) = Axes(plane.Normal);
                List<(float U, float V, Vector3f P)> flat = new(points.Count);
                foreach (Vector3f p in points)
                {
                    flat.Add((Vector3f.Dot(p, across), Vector3f.Dot(p, up), p));
                }

                List<(float U, float V, Vector3f P)> hull = Hull(flat);
                if (hull.Count >= 3)
                {
                    section = [.. hull.Select(static h => h.P)];
                }
            }

            _sections[key] = section;
            return section;
        }

        // Whether a point is inside the mesh: a ray from it crosses the surface an odd number of times. Asked only of a
        // cell none of the surface reaches, to tell a piece from the middle of a solid from a cell of empty air.
        public bool Contains(Vector3f point)
        {
            Vector3f ray = new Vector3f(0.5773f, 0.5774f, 0.5776f).Normalized();
            int crossings = 0;
            foreach (FracturePolygon face in Surface)
            {
                Vector3f origin = face.Vertices[0].Position;
                for (int i = 1; i + 1 < face.Vertices.Count; i++)
                {
                    if (Crosses(point, ray, origin, face.Vertices[i].Position, face.Vertices[i + 1].Position))
                    {
                        crossings++;
                    }
                }
            }

            return (crossings & 1) == 1;
        }

        // Moller-Trumbore, ahead of the point only.
        private static bool Crosses(Vector3f from, Vector3f ray, Vector3f a, Vector3f b, Vector3f c)
        {
            Vector3f ab = b - a;
            Vector3f ac = c - a;
            Vector3f p = Vector3f.Cross(ray, ac);
            float det = Vector3f.Dot(ab, p);
            if (MathF.Abs(det) < 1e-12f)
            {
                return false;
            }

            float inverse = 1f / det;
            Vector3f t = from - a;
            float u = Vector3f.Dot(t, p) * inverse;
            if (u < 0f || u > 1f)
            {
                return false;
            }

            Vector3f q = Vector3f.Cross(t, ab);
            float v = Vector3f.Dot(ray, q) * inverse;
            return v >= 0f && u + v <= 1f && Vector3f.Dot(ac, q) * inverse > 0f;
        }
    }

    // A plane's own axes, right-handed about its normal: counter-clockwise in (across, up) is counter-clockwise about it.
    private static (Vector3f Across, Vector3f Up) Axes(Vector3f n)
    {
        Vector3f helper = MathF.Abs(n.Z) < 0.9f ? Vector3f.UnitZ : Vector3f.UnitX;
        Vector3f across = Vector3f.Cross(helper, n).Normalized();
        return (across, Vector3f.Cross(n, across));
    }

    /// <summary>Seeds for a mesh's cells, gathered toward where it was struck so the pieces there are small.</summary>
    /// <param name="minimum">The low corner of the box to seed.</param>
    /// <param name="maximum">The high corner.</param>
    /// <param name="struck">Where it was struck, in the same space.</param>
    /// <param name="count">How many seeds.</param>
    /// <param name="random">The stream to draw from: the blast's own.</param>
    /// <returns>The seeds.</returns>
    public static Vector3f[] SeedsToward(Vector3f minimum, Vector3f maximum, Vector3f struck, int count, IgiRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        var seeds = new Vector3f[Math.Max(count, 1)];
        Vector3f size = maximum - minimum;
        Vector3f near = new(
            Math.Clamp(struck.X, minimum.X, maximum.X),
            Math.Clamp(struck.Y, minimum.Y, maximum.Y),
            Math.Clamp(struck.Z, minimum.Z, maximum.Z));
        for (int i = 0; i < seeds.Length; i++)
        {
            Vector3f anywhere = minimum + new Vector3f(size.X * random.NextSingle(), size.Y * random.NextSingle(), size.Z * random.NextSingle());

            // A square root pulls the draw toward the near end: more cells, so smaller pieces, close to the blow.
            float reach = MathF.Sqrt(random.NextSingle());
            seeds[i] = near + ((anywhere - near) * reach);
        }

        return seeds;
    }

    // One cell's piece: the surface clipped into it, closed on each of its planes.
    private static FractureChunk? Cell(Shape shape, List<FracturePlane> planes, float minimumArea)
    {
        List<FracturePolygon> faces = [];
        foreach (FracturePolygon source in shape.Surface)
        {
            FracturePolygon? kept = source;
            foreach (FracturePlane plane in planes)
            {
                kept!.Split(plane, out FracturePolygon? inside, out _);
                kept = inside;
                if (kept is null)
                {
                    break;
                }
            }

            if (kept is not null && kept.Area() >= minimumArea)
            {
                faces.Add(kept);
            }
        }

        // The cell's own face on each of its planes; a plane that is no face of the cell (another cell's bisector that the
        // cell stops short of) has none.
        var walls = new FracturePolygon?[planes.Count];
        bool anyWall = false;
        for (int i = 0; i < planes.Count; i++)
        {
            walls[i] = Wall(shape, planes, i);
            anyWall |= walls[i] is not null;
        }

        // A cell the surface never reaches is a piece of the solid's middle, or empty air beside it.
        if (faces.Count == 0 && (!anyWall || !shape.Contains(Middle(walls))))
        {
            return null;
        }

        for (int i = 0; i < planes.Count; i++)
        {
            if (walls[i] is { } wall && Cap(shape, wall, planes[i], minimumArea) is { } cap)
            {
                faces.Add(cap);
            }
        }

        return faces.Count == 0 ? null : Describe(faces);
    }

    // The cell's face on one of its planes: a square on the plane, far bigger than the mesh, cut back by every other plane.
    private static FracturePolygon? Wall(Shape shape, List<FracturePlane> planes, int index)
    {
        FracturePlane plane = planes[index];
        Vector3f n = plane.Normal;
        (Vector3f across, Vector3f up) = Axes(n);
        Vector3f centre = shape.Middle - (n * plane.Distance(shape.Middle));
        float half = shape.Size * 2f;
        List<Vertex3D> square =
        [
            new(centre - (across * half) - (up * half), n, 0f, 0f),
            new(centre + (across * half) - (up * half), n, 0f, 0f),
            new(centre + (across * half) + (up * half), n, 0f, 0f),
            new(centre - (across * half) + (up * half), n, 0f, 0f),
        ];

        FracturePolygon? wall = new(square, FracturePolygon.Interior);
        for (int i = 0; i < planes.Count && wall is not null; i++)
        {
            if (i != index)
            {
                wall.Split(planes[i], out wall, out _);
            }
        }

        return wall;
    }

    // The middle of a cell's faces' corners: a point inside the convex cell.
    private static Vector3f Middle(FracturePolygon?[] walls)
    {
        Vector3f sum = default;
        int count = 0;
        foreach (FracturePolygon? wall in walls)
        {
            if (wall is null)
            {
                continue;
            }

            foreach (Vertex3D corner in wall.Vertices)
            {
                sum += corner.Position;
                count++;
            }
        }

        return sum * (1f / Math.Max(count, 1));
    }

    // The face a cut leaves on one plane: the cell's face there, cut back to the mesh's outline on the plane, facing out of
    // the cell. The cell's face brings the corners where cuts meet inside the solid, which no surface face reaches.
    private static FracturePolygon? Cap(Shape shape, FracturePolygon wall, in FracturePlane plane, float minimumArea)
    {
        if (shape.Section(plane) is not { } section)
        {
            return null;
        }

        Vector3f n = plane.Normal;
        (Vector3f across, Vector3f up) = Axes(n);
        List<(float U, float V)> outline = [.. section.Select(p => (Vector3f.Dot(p, across), Vector3f.Dot(p, up)))];
        float signed = 0f;
        for (int i = 0; i < outline.Count; i++)
        {
            (float U, float V) a = outline[i];
            (float U, float V) b = outline[(i + 1) % outline.Count];
            signed += (a.U * b.V) - (b.U * a.V);
        }

        if (signed < 0f)
        {
            outline.Reverse();
        }

        // Sutherland-Hodgman in the plane: keep what is left of each edge of the counter-clockwise outline.
        List<(float U, float V)> kept = [.. wall.Vertices.Select(c => (Vector3f.Dot(c.Position, across), Vector3f.Dot(c.Position, up)))];
        for (int e = 0; e < outline.Count && kept.Count >= 3; e++)
        {
            (float U, float V) a = outline[e];
            (float U, float V) b = outline[(e + 1) % outline.Count];
            float Side((float U, float V) p) => ((b.U - a.U) * (p.V - a.V)) - ((b.V - a.V) * (p.U - a.U));
            List<(float U, float V)> next = new(kept.Count + 2);
            for (int i = 0; i < kept.Count; i++)
            {
                (float U, float V) p = kept[i];
                (float U, float V) q = kept[(i + 1) % kept.Count];
                float sp = Side(p);
                float sq = Side(q);
                if (sp >= 0f)
                {
                    next.Add(p);
                }

                if ((sp >= 0f) != (sq >= 0f))
                {
                    float t = sp / (sp - sq);
                    next.Add((p.U + ((q.U - p.U) * t), p.V + ((q.V - p.V) * t)));
                }
            }

            kept = next;
        }

        if (kept.Count < 3)
        {
            return null;
        }

        // Back onto the plane, counter-clockwise about the outward normal as the cell's face was.
        Vector3f origin = n * plane.Offset;
        List<Vertex3D> corners = new(kept.Count);
        foreach ((float u, float v) in kept)
        {
            corners.Add(new Vertex3D(origin + (across * u) + (up * v), n, u * InteriorTextureScale, v * InteriorTextureScale));
        }

        var cap = new FracturePolygon(corners, FracturePolygon.Interior);
        return cap.Area() >= minimumArea ? cap : null;
    }

    // Andrew's monotone chain, counter-clockwise, collinear points dropped.
    private static List<(float U, float V, Vector3f P)> Hull(List<(float U, float V, Vector3f P)> points)
    {
        points.Sort(static (a, b) => a.U != b.U ? a.U.CompareTo(b.U) : a.V.CompareTo(b.V));
        var hull = new List<(float U, float V, Vector3f P)>(points.Count + 1);

        static float Turn((float U, float V, Vector3f P) o, (float U, float V, Vector3f P) a, (float U, float V, Vector3f P) b) =>
            ((a.U - o.U) * (b.V - o.V)) - ((a.V - o.V) * (b.U - o.U));

        for (int pass = 0; pass < 2; pass++)
        {
            int start = hull.Count;
            for (int i = 0; i < points.Count; i++)
            {
                var p = points[pass == 0 ? i : points.Count - 1 - i];
                while (hull.Count >= start + 2 && Turn(hull[^2], hull[^1], p) <= 1e-9f)
                {
                    hull.RemoveAt(hull.Count - 1);
                }

                hull.Add(p);
            }

            hull.RemoveAt(hull.Count - 1);
        }

        return hull;
    }

    private static (Vector3f Low, Vector3f High) Box(IReadOnlyList<FracturePolygon> faces)
    {
        Vector3f low = new(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3f high = new(float.MinValue, float.MinValue, float.MinValue);
        foreach (FracturePolygon face in faces)
        {
            foreach (Vertex3D vertex in face.Vertices)
            {
                Vector3f p = vertex.Position;
                low = new Vector3f(MathF.Min(low.X, p.X), MathF.Min(low.Y, p.Y), MathF.Min(low.Z, p.Z));
                high = new Vector3f(MathF.Max(high.X, p.X), MathF.Max(high.Y, p.Y), MathF.Max(high.Z, p.Z));
            }
        }

        return (low, high);
    }

    private static FractureChunk Describe(List<FracturePolygon> faces)
    {
        (Vector3f low, Vector3f high) = Box(faces);
        Vector3f centre = (low + high) * 0.5f;
        float radius = 0f;
        foreach (FracturePolygon face in faces)
        {
            foreach (Vertex3D vertex in face.Vertices)
            {
                radius = MathF.Max(radius, (vertex.Position - centre).Length);
            }
        }

        Vector3f size = high - low;
        float volume = MathF.Max(size.X, 0.02f) * MathF.Max(size.Y, 0.02f) * MathF.Max(size.Z, 0.02f);
        return new FractureChunk(faces, centre, radius, volume);
    }
}
