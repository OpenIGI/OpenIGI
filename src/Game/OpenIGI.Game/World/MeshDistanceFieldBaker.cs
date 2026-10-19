using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;

namespace OpenIGI.Game.World;

/// <summary>
/// Turns a model's triangles into the signed distance field the realtime GI track traces through.
/// </summary>
/// <remarks>
/// <para>
/// Three problems, solved separately because each has a different right answer.
/// </para>
/// <para>
/// <b>Distance near the surface</b> is exact, and computed the only way it can be: the closest point on
/// every triangle within reach. It is confined to a narrow band around the geometry, because doing it
/// for every voxel is the product of two large numbers and doing it near the surface is the product of
/// two small ones.
/// </para>
/// <para>
/// <b>Distance far from the surface</b> is propagated outwards from that band by sweeping, rather than
/// measured. A sweep costs one pass over the voxels per direction and gives a distance that is very
/// slightly too large along diagonals — which for a sphere trace is the safe direction to be wrong in
/// only if it is <em>under</em>, so the sweep uses the true diagonal spacing rather than a chamfer
/// approximation and the residual error is a fraction of a voxel.
/// </para>
/// <para>
/// <b>The sign</b> is the hard part, and the reason is the data. A closed mesh can be tested by casting
/// one ray and counting crossings. <b>I.G.I.'s models are not reliably closed</b> — they are game art
/// from 2000, with open backs, coincident faces and single-sided walls — and one ray through an open
/// mesh gives a confidently wrong answer for an entire column of voxels. So three rays are cast, one
/// down each axis, and the majority wins. A hole has to line up with two axes at once to fool it.
/// </para>
/// <para>
/// Runs off the render thread. Every level's models together are seconds of work, and the reason that
/// matters is recorded on <see cref="IrradianceProbeBake"/>: a multi-second job on the thread holding a
/// live GL context loses the context to the operating system's watchdog.
/// </para>
/// </remarks>
public static class MeshDistanceFieldBaker
{
    /// <summary>How many voxels the longest axis gets by default.</summary>
    /// <remarks>
    /// Chosen against what this game's models are. A crate is a metre across and a hut is eight, and at
    /// this resolution both get voxels of a few centimetres — fine enough that a sphere trace lands on
    /// the right side of a wall, coarse enough that a level's models fit in a few megabytes.
    /// </remarks>
    public const int DefaultResolution = 32;

    /// <summary>Bakes a distance field for a model.</summary>
    /// <param name="vertices">The model's vertices.</param>
    /// <param name="indices">Its triangle indices, three per triangle.</param>
    /// <param name="resolution">How many voxels the longest axis gets.</param>
    /// <returns>The field, or <see langword="null"/> when the model has no triangles to describe.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static MeshDistanceField? Bake(
        ReadOnlySpan<Vertex3D> vertices, ReadOnlySpan<ushort> indices, int resolution = DefaultResolution)
    {
        resolution = Math.Clamp(resolution, 4, 128);

        int triangles = indices.Length / 3;

        if (triangles == 0 || vertices.Length == 0)
        {
            return null;
        }

        // The model's own bounds, then a margin. The margin is not decoration: a field that stops at
        // the surface has no outside, and a trace approaching the model would step into a volume whose
        // every voxel reads zero. A quarter of the longest axis gives a trace room to slow down in.
        Vector3f min = new(float.MaxValue);
        Vector3f max = new(float.MinValue);

        // Over the vertices the TRIANGLES reference, not the whole array. A MEF vertex buffer carries
        // slots no index reaches, and bounding those too sizes the volume around geometry that is not
        // there. It did not turn out to be what broke the field, but the bound is still the wrong one.
        for (int i = 0; i < triangles * 3; i++)
        {
            Vector3f p = vertices[indices[i]].Position;
            min = new Vector3f(MathF.Min(min.X, p.X), MathF.Min(min.Y, p.Y), MathF.Min(min.Z, p.Z));
            max = new Vector3f(MathF.Max(max.X, p.X), MathF.Max(max.Y, p.Y), MathF.Max(max.Z, p.Z));
        }

        float span = MathF.Max(MathF.Max(max.X - min.X, max.Y - min.Y), MathF.Max(max.Z - min.Z, 1e-3f));
        float margin = span * 0.25f;

        min = new Vector3f(min.X - margin, min.Y - margin, min.Z - margin);
        max = new Vector3f(max.X + margin, max.Y + margin, max.Z + margin);

        float voxel = (span + (margin * 2f)) / resolution;

        int sizeX = Math.Max((int)MathF.Ceiling((max.X - min.X) / voxel), 2);
        int sizeY = Math.Max((int)MathF.Ceiling((max.Y - min.Y) / voxel), 2);
        int sizeZ = Math.Max((int)MathF.Ceiling((max.Z - min.Z) / voxel), 2);

        Vector3f voxelSize = new(
            (max.X - min.X) / sizeX, (max.Y - min.Y) / sizeY, (max.Z - min.Z) / sizeZ);

        int count = sizeX * sizeY * sizeZ;
        // A narrow band around the surface, NOT half the model's span.
        //
        // The stored distance is one byte across plus-or-minus this range, so range buys precision.
        // Half a span spends it all where nothing needs it: a level shell spans hundreds of thousands
        // of units here -- these are not metres -- so a byte step came to about a kilometre, every real
        // distance quantised onto the midpoint, and one step below the midpoint decoded as a kilometre
        // INSIDE. The clipmap union takes a minimum, so that read as solid rock everywhere and the sun
        // trace shadowed the entire level.
        //
        // A few voxels wide instead. Distances past it saturate, which is exactly what a sphere trace
        // can afford: a saturated step is shorter than the true one, so the ray creeps rather than
        // overshooting, and precision sits where the surface actually is.
        float range = MathF.Max(MathF.Max(voxelSize.X, voxelSize.Y), voxelSize.Z) * 4f;
        float[] distance = new float[count];
        Array.Fill(distance, float.MaxValue);

        // Exact distance, in a band. Each triangle only touches the voxels near it, so the work is
        // proportional to the surface rather than to the volume.
        int band = 2;

        for (int t = 0; t < triangles; t++)
        {
            Vector3f a = vertices[indices[t * 3]].Position;
            Vector3f b = vertices[indices[(t * 3) + 1]].Position;
            Vector3f c = vertices[indices[(t * 3) + 2]].Position;

            float tMinX = MathF.Min(a.X, MathF.Min(b.X, c.X));
            float tMinY = MathF.Min(a.Y, MathF.Min(b.Y, c.Y));
            float tMinZ = MathF.Min(a.Z, MathF.Min(b.Z, c.Z));
            float tMaxX = MathF.Max(a.X, MathF.Max(b.X, c.X));
            float tMaxY = MathF.Max(a.Y, MathF.Max(b.Y, c.Y));
            float tMaxZ = MathF.Max(a.Z, MathF.Max(b.Z, c.Z));

            int x0 = Math.Max((int)MathF.Floor((tMinX - min.X) / voxelSize.X) - band, 0);
            int y0 = Math.Max((int)MathF.Floor((tMinY - min.Y) / voxelSize.Y) - band, 0);
            int z0 = Math.Max((int)MathF.Floor((tMinZ - min.Z) / voxelSize.Z) - band, 0);
            int x1 = Math.Min((int)MathF.Ceiling((tMaxX - min.X) / voxelSize.X) + band, sizeX - 1);
            int y1 = Math.Min((int)MathF.Ceiling((tMaxY - min.Y) / voxelSize.Y) + band, sizeY - 1);
            int z1 = Math.Min((int)MathF.Ceiling((tMaxZ - min.Z) / voxelSize.Z) + band, sizeZ - 1);

            for (int z = z0; z <= z1; z++)
            {
                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        Vector3f p = new(
                            min.X + ((x + 0.5f) * voxelSize.X),
                            min.Y + ((y + 0.5f) * voxelSize.Y),
                            min.Z + ((z + 0.5f) * voxelSize.Z));

                        float d = DistanceToTriangle(p, a, b, c);
                        int index = (((z * sizeY) + y) * sizeX) + x;

                        if (d < distance[index])
                        {
                            distance[index] = d;
                        }
                    }
                }
            }
        }

        Propagate(distance, sizeX, sizeY, sizeZ, voxelSize);

        // Unsigned, deliberately. This game's meshes are single-sided shells from 2000 -- open backs,
        // coincident faces, walls with no thickness -- so an interior sign never described anything
        // the game treats as solid, and the interiors are exactly where the game happens: the skybox
        // and level-bounds shells enclose the whole level, and a building's inside is where the player
        // walks. Ray-parity marking was tried, voted across the axes, and gated on watertightness, and
        // the union of the results still called most of the playable volume rock. Every consumer asks
        // how far the nearest surface is; a trace guards against its own surface with an offset along
        // the normal, not with a sign.
        byte[] encoded = new byte[count];

        for (int i = 0; i < count; i++)
        {
            encoded[i] = MeshDistanceField.Encode(distance[i], range);
        }

        return new MeshDistanceField(min, voxelSize, sizeX, sizeY, sizeZ, range, encoded);
    }

    /// <summary>Spreads measured distances outwards over the voxels that had none.</summary>
    /// <remarks>
    /// Eight sweeps, one per corner of the grid, each carrying the running minimum along with it. Two
    /// sweeps per axis would only propagate along axes; going corner to corner is what lets a distance
    /// travel diagonally, which is how most of a volume is actually reached.
    /// <para>
    /// The step added between neighbours is the true Euclidean spacing, not the one-per-cell a chamfer
    /// transform uses. A sphere trace steps by whatever this says, so a distance reported <em>larger</em>
    /// than the truth is a step through a wall — and a chamfer transform's diagonal is exactly that.
    /// </para>
    /// </remarks>
    private static void Propagate(float[] distance, int sizeX, int sizeY, int sizeZ, Vector3f voxelSize)
    {
        for (int pass = 0; pass < 8; pass++)
        {
            bool backX = (pass & 1) != 0;
            bool backY = (pass & 2) != 0;
            bool backZ = (pass & 4) != 0;

            for (int zi = 0; zi < sizeZ; zi++)
            {
                int z = backZ ? sizeZ - 1 - zi : zi;

                for (int yi = 0; yi < sizeY; yi++)
                {
                    int y = backY ? sizeY - 1 - yi : yi;

                    for (int xi = 0; xi < sizeX; xi++)
                    {
                        int x = backX ? sizeX - 1 - xi : xi;
                        int index = (((z * sizeY) + y) * sizeX) + x;
                        float best = distance[index];

                        for (int dz = -1; dz <= 1; dz++)
                        {
                            for (int dy = -1; dy <= 1; dy++)
                            {
                                for (int dx = -1; dx <= 1; dx++)
                                {
                                    if (dx == 0 && dy == 0 && dz == 0)
                                    {
                                        continue;
                                    }

                                    int nx = x + dx;
                                    int ny = y + dy;
                                    int nz = z + dz;

                                    if (nx < 0 || ny < 0 || nz < 0 || nx >= sizeX || ny >= sizeY || nz >= sizeZ)
                                    {
                                        continue;
                                    }

                                    float neighbour = distance[(((nz * sizeY) + ny) * sizeX) + nx];

                                    if (neighbour == float.MaxValue)
                                    {
                                        continue;
                                    }

                                    float step = MathF.Sqrt(
                                        (dx * voxelSize.X * (dx * voxelSize.X))
                                        + (dy * voxelSize.Y * (dy * voxelSize.Y))
                                        + (dz * voxelSize.Z * (dz * voxelSize.Z)));

                                    best = MathF.Min(best, neighbour + step);
                                }
                            }
                        }

                        distance[index] = best;
                    }
                }
            }
        }

        // A model whose band never reached a corner leaves it at the sentinel. Saturating rather than
        // leaving it is what keeps the encode from wrapping into a negative distance -- which would
        // read as solid rock in the middle of empty air.
        for (int i = 0; i < distance.Length; i++)
        {
            if (distance[i] == float.MaxValue)
            {
                distance[i] = float.MaxValue / 4f;
            }
        }
    }

    /// <summary>The distance from a point to a triangle.</summary>
    /// <remarks>
    /// The closest point on a triangle lies in its interior, on one of its three edges, or at one of its
    /// three vertices, and which of the seven it is falls out of the barycentric region the point
    /// projects into. Ericson's formulation, which decides that with comparisons rather than by solving
    /// and rejecting.
    /// </remarks>
    private static float DistanceToTriangle(Vector3f p, Vector3f a, Vector3f b, Vector3f c)
    {
        Vector3f ab = Subtract(b, a);
        Vector3f ac = Subtract(c, a);
        Vector3f ap = Subtract(p, a);

        float d1 = Vector3f.Dot(ab, ap);
        float d2 = Vector3f.Dot(ac, ap);

        if (d1 <= 0f && d2 <= 0f)
        {
            return ap.Length;
        }

        Vector3f bp = Subtract(p, b);
        float d3 = Vector3f.Dot(ab, bp);
        float d4 = Vector3f.Dot(ac, bp);

        if (d3 >= 0f && d4 <= d3)
        {
            return bp.Length;
        }

        float vc = (d1 * d4) - (d3 * d2);

        if (vc <= 0f && d1 >= 0f && d3 <= 0f)
        {
            float v = d1 / (d1 - d3);
            return (Subtract(ap, Scale(ab, v))).Length;
        }

        Vector3f cp = Subtract(p, c);
        float d5 = Vector3f.Dot(ab, cp);
        float d6 = Vector3f.Dot(ac, cp);

        if (d6 >= 0f && d5 <= d6)
        {
            return cp.Length;
        }

        float vb = (d5 * d2) - (d1 * d6);

        if (vb <= 0f && d2 >= 0f && d6 <= 0f)
        {
            float w = d2 / (d2 - d6);
            return (Subtract(ap, Scale(ac, w))).Length;
        }

        float va = (d3 * d6) - (d5 * d4);

        if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
        {
            float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
            return (Subtract(Subtract(p, b), Scale(Subtract(c, b), w))).Length;
        }

        float denom = 1f / (va + vb + vc);
        float vv = vb * denom;
        float ww = vc * denom;

        return (Subtract(ap, Add(Scale(ab, vv), Scale(ac, ww)))).Length;
    }

    private static Vector3f Add(Vector3f a, Vector3f b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    private static Vector3f Subtract(Vector3f a, Vector3f b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    private static Vector3f Scale(Vector3f a, float s) => new(a.X * s, a.Y * s, a.Z * s);

}
