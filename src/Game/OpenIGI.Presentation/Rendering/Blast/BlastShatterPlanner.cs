using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Game.World.Fracture;

namespace OpenIGI.Presentation.Rendering.Blast;

/// <summary>What a blast does to one placement: shatters it whole, or blows a hole through it, and the pieces either throws.</summary>
/// <param name="Source">The placement, its mesh and its textures.</param>
/// <param name="Holed">Its mesh with the hole through it, or <see langword="null"/> for a placement shattered whole.</param>
/// <param name="Pieces">The pieces, in the model's own space, metres.</param>
/// <param name="Collapse">Whether it is a building brought down whole: its pieces slump and fall rather than fly.</param>
/// <param name="Low">The mesh's low corner, in its own space, metres: what is gone, for a placement shattered whole.</param>
/// <param name="High">Its high corner.</param>
/// <param name="HoleCentre">The hole's centre, in the mesh's space, for a holed one.</param>
/// <param name="HoleRadius">The hole's radius, metres; nought for none.</param>
public sealed record ShatterPlan(
    ShatterSource Source,
    MeshGeometry? Holed,
    IReadOnlyList<FractureChunk> Pieces,
    bool Collapse = false,
    Vector3f Low = default,
    Vector3f High = default,
    Vector3f HoleCentre = default,
    float HoleRadius = 0f);

/// <summary>How much breaking a blast may do: fewer, larger pieces on the web, phones and the lower presets.</summary>
/// <param name="CellScale">The share of each break's usual cell count.</param>
/// <param name="MostPieces">The most pieces in the world at once.</param>
/// <param name="MostPlacements">The most placements one blast breaks.</param>
/// <param name="Substeps">The physics steps a tick.</param>
public readonly record struct ShatterBudget(float CellScale, int MostPieces, int MostPlacements, int Substeps)
{
    /// <summary>The budget for a blast quality: 1 the light tier (web, phones, enhanced), 2 the full one.</summary>
    /// <param name="quality">The quality.</param>
    /// <returns>The budget.</returns>
    public static ShatterBudget For(int quality) => quality >= 2
        ? new ShatterBudget(1f, 220, 14, 2)
        : new ShatterBudget(0.4f, 56, 6, 1);
}

/// <summary>
/// Decides what a bomb's blast breaks, and breaks it: the small things near it shattered whole, the walls facing it holed,
/// and both broken into the pieces it throws -- pure geometry, safe to run off the frame's thread.
/// </summary>
/// <remarks>
/// <para>
/// A placement whose bounds are no more than a few metres across is a <b>prop</b> -- a crate, a drum, a fence panel, a
/// tower's legs -- and one the blast reaches is broken whole into cells gathered toward the blast, so the side facing it
/// goes to gravel and the far side to slabs. Anything larger is a <b>structure</b>. A building the blast is close enough to
/// (and no bigger than <see cref="BuildingRadiusMetres"/>) is brought down whole -- broken into large slabs that slump into
/// a heap. One further off stands, and where its surface comes close enough a ragged hole is cut through it there, the
/// faces inside the hole broken into its rubble.
/// </para>
/// <para>
/// Every random draw is from the blast's seed and the placement's name, so every machine breaks the same things the same
/// way. Translucent and additive faces (glass, glows) are not broken: the blast's own panes are the destructibles'.
/// </para>
/// </remarks>
public static class BlastShatterPlanner
{
    /// <summary>The widest a prop's bounds may be, in metres; anything bigger stands and is holed.</summary>
    public const float PropRadiusMetres = 4f;

    /// <summary>How far past the killing radius props are shattered, as a share of it.</summary>
    public const float PropReach = 0.85f;

    /// <summary>How far from the blast a structure's surface may be and still be holed, as a share of the killing radius.</summary>
    public const float BreachReach = 0.6f;

    /// <summary>The widest a building's bounds may be, in metres, and still be brought down whole; larger stands and is holed.</summary>
    public const float BuildingRadiusMetres = 30f;

    /// <summary>How near the blast a building's surface must be to bring it down, as a share of the killing radius.</summary>
    public const float CollapseReach = 0.45f;

    // How many faces a source may have before it is too costly to break whole.
    private const int MostPropFaces = 1500;
    private const int MostBuildingFaces = 8000;

    /// <summary>Plans one placement's break.</summary>
    /// <param name="source">The placement, its mesh and its textures.</param>
    /// <param name="blastMetres">Where the blast went off, in world metres.</param>
    /// <param name="radiusMetres">How far it kills.</param>
    /// <param name="seed">The blast's seed.</param>
    /// <returns>The plan, or <see langword="null"/> when the blast leaves it standing.</returns>
    public static ShatterPlan? Plan(ShatterSource source, Vector3d blastMetres, float radiusMetres, uint seed) =>
        Plan(source, blastMetres, radiusMetres, seed, ShatterBudget.For(2));

    /// <summary>Plans one placement's break, within a budget.</summary>
    /// <param name="source">The placement, its mesh and its textures.</param>
    /// <param name="blastMetres">Where the blast went off, in world metres.</param>
    /// <param name="radiusMetres">How far it kills.</param>
    /// <param name="seed">The blast's seed.</param>
    /// <param name="budget">How much breaking it may do.</param>
    /// <returns>The plan, or <see langword="null"/> when the blast leaves it standing.</returns>
    public static ShatterPlan? Plan(ShatterSource source, Vector3d blastMetres, float radiusMetres, uint seed, ShatterBudget budget)
    {
        ArgumentNullException.ThrowIfNull(source);
        MeshGeometry mesh = source.Geometry;
        WorldPlacement placement = source.Placement;

        // The blast in the model's own space: the inverse of the draw's local-to-world turn.
        Vector3d relative = blastMetres - placement.Position;
        Vector3f local = placement.Orientation.Transform(new Vector3f((float)relative.X, (float)relative.Y, (float)relative.Z));
        var random = new IgiRandom(seed ^ Hash(placement.TaskName) ^ Hash(placement.Instance.Name));

        if (mesh.Bounds.Radius <= PropRadiusMetres)
        {
            float distance = MathF.Max(0f, (mesh.Bounds.Centre - local).Length - mesh.Bounds.Radius);
            if (distance > radiusMetres * PropReach || mesh.TriangleCount > MostPropFaces)
            {
                return null;
            }

            List<FracturePolygon> faces = Faces(mesh);
            if (faces.Count == 0)
            {
                return null;
            }

            (Vector3f low, Vector3f high) = Box(faces);
            int cells = Scaled(Math.Clamp((int)MathF.Round(5f + (mesh.Bounds.Radius * 7f)), 5, 26), budget, 3);
            Vector3f[] seeds = VoronoiFracture.SeedsToward(low, high, local, cells, random);
            List<FractureChunk> pieces = VoronoiFracture.Break(faces, seeds, [], MinimumArea(mesh.Bounds.Radius));
            return pieces.Count == 0 ? null : new ShatterPlan(source, null, pieces, Low: low, High: high);
        }

        // A structure: holed where its surface comes nearest the blast, if that is near enough.
        (Vector3f nearest, float gap) = Nearest(mesh, local);
        float reach = radiusMetres * BreachReach;
        if (gap > reach)
        {
            return null;
        }

        // Near enough, and small enough, to bring down: the whole building into slabs, smaller toward the blast.
        if (gap <= radiusMetres * CollapseReach && mesh.Bounds.Radius <= BuildingRadiusMetres && mesh.TriangleCount <= MostBuildingFaces)
        {
            List<FracturePolygon> walls = Faces(mesh);
            if (walls.Count > 0)
            {
                (Vector3f low, Vector3f high) = Box(walls);
                int cells = Scaled(Math.Clamp((int)MathF.Round(14f + (mesh.Bounds.Radius * 1.6f)), 16, 48), budget, 8);
                Vector3f[] seeds = VoronoiFracture.SeedsToward(low, high, local, cells, random);
                List<FractureChunk> slabs = VoronoiFracture.Break(walls, seeds, [], MinimumArea(mesh.Bounds.Radius * 0.3f));
                if (slabs.Count > 0)
                {
                    return new ShatterPlan(source, null, slabs, Collapse: true, Low: low, High: high);
                }
            }
        }

        float hole = Math.Clamp(1.4f + (radiusMetres * 0.22f * (1f - (gap / reach))), 1.4f, 6f);

        // The hole's middle a little into the wall from its face, so it goes right through.
        Vector3f into = gap > 1e-3f ? (nearest - local).Normalized() : Vector3f.UnitZ;
        Vector3f centre = nearest + (into * (hole * 0.25f));
        FracturePlane[] ball = BreachCutter.RaggedBall(centre, hole, random);
        if (BreachCutter.Cut(mesh, ball) is not { } cut)
        {
            return null;
        }

        List<FracturePolygon> removed = Breakable(cut.Removed, mesh);
        if (removed.Count == 0)
        {
            return new ShatterPlan(source, cut.Kept, [], HoleCentre: centre, HoleRadius: hole);
        }

        (Vector3f holeLow, Vector3f holeHigh) = Box(removed);
        int rubble = Scaled(Math.Clamp((int)MathF.Round(hole * 6f), 8, 30), budget, 4);
        Vector3f[] rubbleSeeds = VoronoiFracture.SeedsToward(holeLow, holeHigh, local, rubble, random);
        List<FractureChunk> chunks = VoronoiFracture.Break(removed, rubbleSeeds, ball, MinimumArea(hole));
        return new ShatterPlan(source, cut.Kept, chunks, HoleCentre: centre, HoleRadius: hole);
    }

    // The mesh's breakable faces as polygons: opaque and cut-out subsets only.
    private static List<FracturePolygon> Faces(MeshGeometry mesh)
    {
        List<FracturePolygon> faces = new(mesh.TriangleCount);
        for (int s = 0; s < mesh.Subsets.Length; s++)
        {
            MeshSubset subset = mesh.Subsets[s];
            if (!IsBreakable(subset))
            {
                continue;
            }

            for (int i = subset.IndexOffset; i + 2 < subset.IndexOffset + subset.IndexCount; i += 3)
            {
                faces.Add(new FracturePolygon(
                    [mesh.Vertices[mesh.Indices[i]], mesh.Vertices[mesh.Indices[i + 1]], mesh.Vertices[mesh.Indices[i + 2]]], s));
            }
        }

        return faces;
    }

    private static List<FracturePolygon> Breakable(IReadOnlyList<FracturePolygon> faces, MeshGeometry mesh)
    {
        List<FracturePolygon> kept = new(faces.Count);
        foreach (FracturePolygon face in faces)
        {
            if (face.Subset >= 0 && face.Subset < mesh.Subsets.Length && IsBreakable(mesh.Subsets[face.Subset]))
            {
                kept.Add(face);
            }
        }

        return kept;
    }

    private static bool IsBreakable(MeshSubset subset) =>
        subset.IndexCount > 0 && subset.Blend is SubsetBlend.Opaque or SubsetBlend.AlphaTested;

    // The point of the mesh's breakable faces nearest a point, and how far it is.
    private static (Vector3f Point, float Distance) Nearest(MeshGeometry mesh, Vector3f point)
    {
        float best = float.MaxValue;
        Vector3f nearest = mesh.Bounds.Centre;
        foreach (MeshSubset subset in mesh.Subsets)
        {
            if (!IsBreakable(subset))
            {
                continue;
            }

            for (int i = subset.IndexOffset; i + 2 < subset.IndexOffset + subset.IndexCount; i += 3)
            {
                Vector3f on = ClosestOnTriangle(
                    point, mesh.Vertices[mesh.Indices[i]].Position, mesh.Vertices[mesh.Indices[i + 1]].Position, mesh.Vertices[mesh.Indices[i + 2]].Position);
                float d = (on - point).LengthSquared;
                if (d < best)
                {
                    best = d;
                    nearest = on;
                }
            }
        }

        return (nearest, MathF.Sqrt(best));
    }

    /// <summary>The point of a triangle nearest a point: by which of its regions -- a corner, an edge, the face -- the point falls in.</summary>
    /// <param name="p">The point.</param>
    /// <param name="a">The triangle's first corner.</param>
    /// <param name="b">Its second.</param>
    /// <param name="c">Its third.</param>
    /// <returns>The nearest point on it.</returns>
    public static Vector3f ClosestOnTriangle(Vector3f p, Vector3f a, Vector3f b, Vector3f c)
    {
        Vector3f ab = b - a;
        Vector3f ac = c - a;
        Vector3f ap = p - a;
        float d1 = Vector3f.Dot(ab, ap);
        float d2 = Vector3f.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f)
        {
            return a;
        }

        Vector3f bp = p - b;
        float d3 = Vector3f.Dot(ab, bp);
        float d4 = Vector3f.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3)
        {
            return b;
        }

        float vc = (d1 * d4) - (d3 * d2);
        if (vc <= 0f && d1 >= 0f && d3 <= 0f)
        {
            return a + (ab * (d1 / (d1 - d3)));
        }

        Vector3f cp = p - c;
        float d5 = Vector3f.Dot(ab, cp);
        float d6 = Vector3f.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6)
        {
            return c;
        }

        float vb = (d5 * d2) - (d1 * d6);
        if (vb <= 0f && d2 >= 0f && d6 <= 0f)
        {
            return a + (ac * (d2 / (d2 - d6)));
        }

        float va = (d3 * d6) - (d5 * d4);
        if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
        {
            return b + ((c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6))));
        }

        float denominator = 1f / (va + vb + vc);
        return a + (ab * (vb * denominator)) + (ac * (vc * denominator));
    }

    private static (Vector3f Low, Vector3f High) Box(List<FracturePolygon> faces)
    {
        Vector3f low = new(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3f high = new(float.MinValue, float.MinValue, float.MinValue);
        foreach (FracturePolygon face in faces)
        {
            foreach (Vertex3D v in face.Vertices)
            {
                low = new Vector3f(MathF.Min(low.X, v.Position.X), MathF.Min(low.Y, v.Position.Y), MathF.Min(low.Z, v.Position.Z));
                high = new Vector3f(MathF.Max(high.X, v.Position.X), MathF.Max(high.Y, v.Position.Y), MathF.Max(high.Z, v.Position.Z));
            }
        }

        return (low, high);
    }

    private static int Scaled(int cells, ShatterBudget budget, int least) =>
        Math.Max(least, (int)MathF.Round(cells * budget.CellScale));

    // The smallest face a piece keeps: a sliver a thousandth of the size squared.
    private static float MinimumArea(float size) => MathF.Max(1e-5f, size * size * 1e-4f);

    private static uint Hash(string text)
    {
        unchecked
        {
            uint h = 2166136261u;
            foreach (char ch in text)
            {
                h = (h ^ ch) * 16777619u;
            }

            return h;
        }
    }
}
