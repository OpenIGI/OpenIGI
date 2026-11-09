using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World.Fracture;

/// <summary>How a building is broken into slabs: how finely, and how many pieces it may give.</summary>
/// <param name="FarCellMetres">The width of a slab far from the blow, in metres.</param>
/// <param name="NearCellMetres">The width of a slab right where it was struck.</param>
/// <param name="MostPieces">The most pieces the whole building gives.</param>
/// <param name="ChipLargest">How many of the largest slabs have a corner knocked off them, which the pile keeps as rubble.</param>
public readonly record struct PanelFractureOptions(float FarCellMetres, float NearCellMetres, int MostPieces, int ChipLargest)
{
    /// <summary>Gets the full breaking: slabs a couple of metres across, a metre where it was struck.</summary>
    public static PanelFractureOptions Full => new(2.3f, 1.0f, 300, 40);

    /// <summary>Gets the light tier's: larger slabs, a quarter as many.</summary>
    public static PanelFractureOptions Light => new(3.6f, 2.0f, 70, 0);
}

/// <summary>
/// Breaks a building as buildings break: wall by wall and floor by floor into slabs as thick as the walls, rather than into
/// solid blocks of the whole building.
/// </summary>
/// <remarks>
/// <para>
/// A building's mesh is a shell -- its walls are sheets, often one face thick, and its rooms are air. Breaking the whole of
/// it by one Voronoi diagram fills each cut with the building's whole outline (<see cref="VoronoiFracture"/>'s caps are the
/// convex section of what it breaks), so the rooms come out solid. Here the faces are first gathered into <b>panels</b>:
/// the triangles on one plane that touch one another. A panel is paired with the sheet behind it when the mesh has one --
/// a wall drawn both sides -- and is otherwise given a back and a rim of its own, a slab of <see cref="SheetThickness"/>.
/// Each closed panel is then broken by cells whose seeds lie in its own plane, so every cut runs through the slab and every
/// piece is a slab of it: and for a slab, a thin solid, the convex section is exactly right.
/// </para>
/// <para>
/// The cells are smaller toward the blow, and the seeds are moved a little off the panel's plane so the cuts run at a
/// slant, as broken concrete does, rather than square. The largest slabs then have a corner knocked off them -- a second
/// break of their own -- which is the gravel a fallen wall lies among.
/// </para>
/// <para>Clean-room, from the geometry alone; deterministic in the seed's stream.</para>
/// </remarks>
public static class PanelFracture
{
    /// <summary>How thick a one-sided sheet is made, in metres.</summary>
    public const float SheetThickness = 0.24f;

    // The widest a wall's two sheets may stand apart and still be one wall.
    private const float MostWallThickness = 0.7f;

    // How far a face may lean off a panel's plane, as the cosine between their normals, and how far stand off it, metres.
    private const float SameFacing = 0.996f;
    private const float SamePlane = 0.04f;

    // A panel smaller than this, square metres, is not broken: a sill, a step, a trim; it falls as one piece.
    private const float LeastBrokenArea = 0.6f;

    /// <summary>Breaks a building's faces into slabs.</summary>
    /// <param name="faces">The faces to break, in the building's own space, metres, z up.</param>
    /// <param name="struck">Where the blow came from, in the same space.</param>
    /// <param name="random">The stream to draw from: the blast's own.</param>
    /// <param name="options">How finely.</param>
    /// <returns>The pieces, every one a slab of one panel.</returns>
    public static List<FractureChunk> Break(IReadOnlyList<FracturePolygon> faces, Vector3f struck, IgiRandom random, PanelFractureOptions options)
    {
        ArgumentNullException.ThrowIfNull(faces);
        ArgumentNullException.ThrowIfNull(random);
        List<Panel> panels = Panels(faces);
        if (panels.Count == 0)
        {
            return [];
        }

        // The sheets behind one another, paired; the rest given a back.
        Pair(panels);

        // How many cells each panel gets: by its area against the cell it is broken into there, the whole scaled into the
        // budget so a big building is broken more coarsely rather than past it.
        float buildingSize = 1f;
        foreach (Panel panel in panels)
        {
            buildingSize = MathF.Max(buildingSize, (panel.Centre - struck).Length);
        }

        var wanted = new float[panels.Count];
        float total = 0f;
        for (int i = 0; i < panels.Count; i++)
        {
            Panel panel = panels[i];
            if (panel.Area < LeastBrokenArea || panel.IsBack)
            {
                continue;
            }

            float near = Math.Clamp((panel.Centre - struck).Length / buildingSize, 0f, 1f);
            float cell = options.NearCellMetres + ((options.FarCellMetres - options.NearCellMetres) * near);
            wanted[i] = panel.Area / (cell * cell);
            total += wanted[i];
        }

        int panelsBroken = panels.Count(p => !p.IsBack);
        float scale = total <= 0f ? 1f : MathF.Min(1f, Math.Max(1, options.MostPieces - panelsBroken) / total);

        List<FractureChunk> pieces = [];
        for (int i = 0; i < panels.Count; i++)
        {
            Panel panel = panels[i];
            if (panel.IsBack)
            {
                continue;
            }

            List<FracturePolygon> solid = Close(panel);
            int cells = Math.Clamp((int)MathF.Round(wanted[i] * scale), 1, 24);
            Vector3f[] seeds = Seeds(panel, solid, struck, cells, random);
            float least = MathF.Max(1e-5f, panel.Area * 1e-4f);
            pieces.AddRange(cells <= 1 ? [Whole(solid)] : VoronoiFracture.Break(solid, seeds, [], least));
        }

        Chip(pieces, struck, random, options.ChipLargest);
        return pieces;
    }

    // The faces gathered by plane, then split into the runs of them that touch.
    private static List<Panel> Panels(IReadOnlyList<FracturePolygon> faces)
    {
        List<(FracturePolygon Face, Vector3f Normal, float Offset)> flat = [];
        foreach (FracturePolygon face in faces)
        {
            if (face.Vertices.Count < 3 || face.Area() < 1e-5f)
            {
                continue;
            }

            Vector3f n = Facing(face);
            flat.Add((face, n, Vector3f.Dot(n, face.Vertices[0].Position)));
        }

        // By plane: a face joins the first group it lies in.
        List<List<int>> planes = [];
        List<(Vector3f Normal, float Offset)> planeOf = [];
        for (int i = 0; i < flat.Count; i++)
        {
            int found = -1;
            for (int g = 0; g < planeOf.Count; g++)
            {
                if (Vector3f.Dot(planeOf[g].Normal, flat[i].Normal) >= SameFacing && MathF.Abs(planeOf[g].Offset - flat[i].Offset) <= SamePlane)
                {
                    found = g;
                    break;
                }
            }

            if (found < 0)
            {
                planes.Add([]);
                planeOf.Add((flat[i].Normal, flat[i].Offset));
                found = planes.Count - 1;
            }

            planes[found].Add(i);
        }

        // Within a plane, the faces that share a corner are one panel.
        List<Panel> panels = [];
        for (int g = 0; g < planes.Count; g++)
        {
            List<int> members = planes[g];
            var parent = new int[members.Count];
            for (int i = 0; i < parent.Length; i++)
            {
                parent[i] = i;
            }

            Dictionary<(int, int, int), int> corners = [];
            for (int m = 0; m < members.Count; m++)
            {
                foreach (Vertex3D v in flat[members[m]].Face.Vertices)
                {
                    var key = Key(v.Position);
                    if (corners.TryGetValue(key, out int other))
                    {
                        Union(parent, m, other);
                    }
                    else
                    {
                        corners[key] = m;
                    }
                }
            }

            Dictionary<int, Panel> byRoot = [];
            for (int m = 0; m < members.Count; m++)
            {
                int root = Find(parent, m);
                if (!byRoot.TryGetValue(root, out Panel? panel))
                {
                    panel = new Panel(planeOf[g].Normal, planeOf[g].Offset);
                    byRoot[root] = panel;
                    panels.Add(panel);
                }

                panel.Faces.Add(flat[members[m]].Face);
            }
        }

        foreach (Panel panel in panels)
        {
            panel.Measure();
        }

        return panels;
    }

    // The face's outward normal: its winding's, turned to agree with the normals its corners were authored with.
    private static Vector3f Facing(FracturePolygon face)
    {
        Vector3f origin = face.Vertices[0].Position;
        Vector3f sum = default;
        Vector3f authored = default;
        for (int i = 1; i + 1 < face.Vertices.Count; i++)
        {
            sum += Vector3f.Cross(face.Vertices[i].Position - origin, face.Vertices[i + 1].Position - origin);
        }

        foreach (Vertex3D v in face.Vertices)
        {
            authored += v.Normal;
        }

        Vector3f n = sum.LengthSquared > 1e-20f ? sum.Normalized() : Vector3f.UnitZ;
        return Vector3f.Dot(n, authored) < 0f ? n * -1f : n;
    }

    // Each panel with the one behind it, facing away within a wall's thickness and overlapping it: the two sheets of one wall.
    private static void Pair(List<Panel> panels)
    {
        for (int i = 0; i < panels.Count; i++)
        {
            Panel front = panels[i];
            if (front.IsBack || front.Back is not null)
            {
                continue;
            }

            Panel? best = null;
            float bestGap = float.MaxValue;
            for (int j = 0; j < panels.Count; j++)
            {
                Panel other = panels[j];
                if (j == i || other.IsBack || other.Back is not null || Vector3f.Dot(front.Normal, other.Normal) > -SameFacing)
                {
                    continue;
                }

                // Behind: the other's plane is that far back along the front's normal.
                float gap = front.Offset + other.Offset;
                if (gap < 0.02f || gap > MostWallThickness || !front.Overlaps(other))
                {
                    continue;
                }

                if (gap < bestGap)
                {
                    bestGap = gap;
                    best = other;
                }
            }

            if (best is not null)
            {
                front.Back = best;
                front.Thickness = bestGap;
                best.IsBack = true;
            }
        }
    }

    // The panel closed into a solid: with the sheet behind it, or with a back and a rim of its own.
    private static List<FracturePolygon> Close(Panel panel)
    {
        List<FracturePolygon> solid = [.. panel.Faces];
        if (panel.Back is { } back)
        {
            solid.AddRange(back.Faces);
            return solid;
        }

        Vector3f n = panel.Normal;
        Vector3f depth = n * -SheetThickness;
        foreach (FracturePolygon face in panel.Faces)
        {
            List<Vertex3D> reversed = new(face.Vertices.Count);
            for (int i = face.Vertices.Count - 1; i >= 0; i--)
            {
                Vertex3D v = face.Vertices[i];
                reversed.Add(new Vertex3D(v.Position + depth, n * -1f, v.DiffuseU, v.DiffuseV, colour: v.Colour));
            }

            solid.Add(new FracturePolygon(reversed, FracturePolygon.Interior));
        }

        // The rim along the panel's outline: every edge only one of its faces has.
        Dictionary<((int, int, int), (int, int, int)), int> edges = [];
        foreach (FracturePolygon face in panel.Faces)
        {
            for (int i = 0; i < face.Vertices.Count; i++)
            {
                var a = Key(face.Vertices[i].Position);
                var b = Key(face.Vertices[(i + 1) % face.Vertices.Count].Position);
                var undirected = a.CompareTo(b) < 0 ? (a, b) : (b, a);
                edges[undirected] = edges.GetValueOrDefault(undirected) + 1;
            }
        }

        foreach (FracturePolygon face in panel.Faces)
        {
            Vector3f middle = default;
            foreach (Vertex3D corner in face.Vertices)
            {
                middle += corner.Position;
            }

            middle *= 1f / face.Vertices.Count;
            for (int i = 0; i < face.Vertices.Count; i++)
            {
                Vector3f a = face.Vertices[i].Position;
                Vector3f b = face.Vertices[(i + 1) % face.Vertices.Count].Position;
                var ka = Key(a);
                var kb = Key(b);
                if (edges[ka.CompareTo(kb) < 0 ? (ka, kb) : (kb, ka)] != 1)
                {
                    continue;
                }

                Vector3f edge = b - a;
                float length = edge.Length;
                if (length < 1e-4f)
                {
                    continue;
                }

                // Away from the face's middle, square to the edge and in the panel's plane.
                Vector3f across = Vector3f.Cross(edge, n).Normalized();
                Vector3f outward = Vector3f.Dot(across, ((a + b) * 0.5f) - middle) < 0f ? across * -1f : across;
                float u = length * VoronoiFracture.InteriorTextureScale;
                float v = SheetThickness * VoronoiFracture.InteriorTextureScale;
                solid.Add(new FracturePolygon(
                    [
                        new Vertex3D(a, outward, 0f, 0f),
                        new Vertex3D(a + depth, outward, 0f, v),
                        new Vertex3D(b + depth, outward, u, v),
                        new Vertex3D(b, outward, u, 0f),
                    ],
                    FracturePolygon.Interior));
            }
        }

        return solid;
    }

    // Seeds in the panel's middle plane, gathered toward the blow, each moved a little off it so the cuts slant.
    private static Vector3f[] Seeds(Panel panel, List<FracturePolygon> solid, Vector3f struck, int cells, IgiRandom random)
    {
        (Vector3f low, Vector3f high) = Box(solid);
        Vector3f[] seeds = VoronoiFracture.SeedsToward(low, high, struck, cells, random);
        float thickness = panel.Back is not null ? panel.Thickness : SheetThickness;
        float middle = panel.Offset - (thickness * 0.5f);
        for (int i = 0; i < seeds.Length; i++)
        {
            float off = Vector3f.Dot(panel.Normal, seeds[i]) - middle;
            float slant = (random.NextSingle() - 0.5f) * thickness * 3f;
            seeds[i] -= panel.Normal * (off - slant);
        }

        return seeds;
    }

    // A corner knocked off each of the largest slabs: the slab broken again by two seeds, one near a corner of it.
    private static void Chip(List<FractureChunk> pieces, Vector3f struck, IgiRandom random, int count)
    {
        if (count <= 0 || pieces.Count == 0)
        {
            return;
        }

        List<int> largest = [.. Enumerable.Range(0, pieces.Count).OrderByDescending(i => pieces[i].Volume).Take(count)];
        List<FractureChunk> chips = [];
        foreach (int i in largest)
        {
            FractureChunk slab = pieces[i];
            if (slab.Radius < 0.6f)
            {
                continue;
            }

            // The corner nearest the blow -- the edge it hit -- or another, at random.
            Vector3f corner = slab.Faces[0].Vertices[0].Position;
            float best = float.MaxValue;
            bool nearest = random.NextSingle() < 0.6f;
            foreach (FracturePolygon face in slab.Faces)
            {
                foreach (Vertex3D v in face.Vertices)
                {
                    float d = nearest ? (v.Position - struck).LengthSquared : random.NextSingle();
                    if (d < best)
                    {
                        best = d;
                        corner = v.Position;
                    }
                }
            }

            float share = 0.22f + (random.NextSingle() * 0.18f);
            Vector3f chipSeed = corner + ((slab.Centre - corner) * share);
            Vector3f bodySeed = slab.Centre + ((slab.Centre - corner) * 0.3f);
            List<FractureChunk> split = VoronoiFracture.Break(slab.Faces, [bodySeed, chipSeed], [], 1e-5f);
            if (split.Count == 2)
            {
                pieces[i] = split[0];
                chips.Add(split[1]);
            }
        }

        pieces.AddRange(chips);
    }

    private static FractureChunk Whole(List<FracturePolygon> faces)
    {
        (Vector3f low, Vector3f high) = Box(faces);
        Vector3f centre = (low + high) * 0.5f;
        Vector3f size = high - low;
        return new FractureChunk(faces, centre, size.Length * 0.5f, MathF.Max(size.X, 0.02f) * MathF.Max(size.Y, 0.02f) * MathF.Max(size.Z, 0.02f));
    }

    private static (Vector3f Low, Vector3f High) Box(IEnumerable<FracturePolygon> faces)
    {
        Vector3f low = new(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3f high = new(float.MinValue, float.MinValue, float.MinValue);
        foreach (FracturePolygon face in faces)
        {
            foreach (Vertex3D v in face.Vertices)
            {
                Vector3f q = v.Position;
                low = new Vector3f(MathF.Min(low.X, q.X), MathF.Min(low.Y, q.Y), MathF.Min(low.Z, q.Z));
                high = new Vector3f(MathF.Max(high.X, q.X), MathF.Max(high.Y, q.Y), MathF.Max(high.Z, q.Z));
            }
        }

        return (low, high);
    }

    // A corner's place to the centimetre, so faces that share it find each other.
    private static (int, int, int) Key(Vector3f p) =>
        ((int)MathF.Round(p.X * 100f), (int)MathF.Round(p.Y * 100f), (int)MathF.Round(p.Z * 100f));

    private static int Find(int[] parent, int i)
    {
        while (parent[i] != i)
        {
            parent[i] = parent[parent[i]];
            i = parent[i];
        }

        return i;
    }

    private static void Union(int[] parent, int a, int b)
    {
        int ra = Find(parent, a);
        int rb = Find(parent, b);
        if (ra != rb)
        {
            parent[Math.Max(ra, rb)] = Math.Min(ra, rb);
        }
    }

    // One run of faces on one plane, and what it was paired with.
    private sealed class Panel(Vector3f normal, float offset)
    {
        public Vector3f Normal { get; } = normal;

        public float Offset { get; } = offset;

        public List<FracturePolygon> Faces { get; } = [];

        public float Area { get; private set; }

        public Vector3f Centre { get; private set; }

        public Vector3f Low { get; private set; }

        public Vector3f High { get; private set; }

        public Panel? Back { get; set; }

        public float Thickness { get; set; }

        public bool IsBack { get; set; }

        public void Measure()
        {
            Area = Faces.Sum(f => f.Area());
            (Low, High) = Box(Faces);
            Centre = (Low + High) * 0.5f;
        }

        // Whether another panel, flattened onto this one's plane, covers most of the smaller of the two.
        public bool Overlaps(Panel other)
        {
            Vector3f helper = MathF.Abs(Normal.Z) < 0.9f ? Vector3f.UnitZ : Vector3f.UnitX;
            Vector3f across = Vector3f.Cross(helper, Normal).Normalized();
            Vector3f up = Vector3f.Cross(Normal, across);
            (float U0, float V0, float U1, float V1) mine = Flat(Faces, across, up);
            (float U0, float V0, float U1, float V1) theirs = Flat(other.Faces, across, up);
            float u = MathF.Min(mine.U1, theirs.U1) - MathF.Max(mine.U0, theirs.U0);
            float v = MathF.Min(mine.V1, theirs.V1) - MathF.Max(mine.V0, theirs.V0);
            if (u <= 0f || v <= 0f)
            {
                return false;
            }

            float smaller = MathF.Min((mine.U1 - mine.U0) * (mine.V1 - mine.V0), (theirs.U1 - theirs.U0) * (theirs.V1 - theirs.V0));
            return u * v >= 0.5f * smaller;
        }

        private static (float U0, float V0, float U1, float V1) Flat(List<FracturePolygon> faces, Vector3f across, Vector3f up)
        {
            float u0 = float.MaxValue, v0 = float.MaxValue, u1 = float.MinValue, v1 = float.MinValue;
            foreach (FracturePolygon face in faces)
            {
                foreach (Vertex3D vertex in face.Vertices)
                {
                    float u = Vector3f.Dot(vertex.Position, across);
                    float v = Vector3f.Dot(vertex.Position, up);
                    u0 = MathF.Min(u0, u);
                    u1 = MathF.Max(u1, u);
                    v0 = MathF.Min(v0, v);
                    v1 = MathF.Max(v1, v);
                }
            }

            return (u0, v0, u1, v1);
        }
    }
}
