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

    /// <summary>How wide a model must be before its interior is disregarded.</summary>
    /// <remarks>
    /// Matched to the innermost clipmap's own reach. A model wider than the volume the trace works in
    /// is not a prop the trace is meant to resolve; it is the thing the props stand inside. Level 1 has
    /// a shell spanning nine thousand units and another spanning ninety thousand -- skybox and
    /// level-bounds volumes. They are watertight, so ray parity marks their whole interior solid, which
    /// is geometrically right and useless here: the union takes a minimum, so the entire playable
    /// volume read as rock. Measured, the level called 100% of itself solid and open sky ten thousand
    /// units up sat three thousand units inside stone.
    /// <para>
    /// Left unsigned rather than dropped. Their surfaces still occlude -- a ceiling should stop a ray
    /// that reaches it -- but nothing they enclose is called solid.
    /// </para>
    /// <para>
    /// <b>This does not go far enough, and the remainder is recorded rather than guessed at.</b> It
    /// takes the level from 100% solid to 59%, which is still absurd for open ground: the rest is
    /// ordinary buildings, whose interiors are where the player walks. Making every field unsigned
    /// takes it to 0% with every clipmap level agreeing with the exact field on sign across 235,358
    /// samples, which says this game's single-sided shells have no meaningful interior at all. That is
    /// a larger decision than a shadow ray needs -- it would retire the sign tests and change what L5's
    /// hit lighting can ask -- so it is left for its own change.
    /// </para>
    /// </remarks>
    public const float ContainerSpan = 4096f;

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

                        float d = Distance(p, a, b, c);
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
        // A model this big is a container, and a container's interior is where the game happens.
        //
        // Level 1 has a shell spanning nine thousand units and another spanning ninety thousand --
        // skybox and level-bounds volumes. They are watertight, so ray parity marks their entire
        // interior solid, which is geometrically right and catastrophic here: the union takes a
        // minimum, so the whole playable volume read as rock. Measured, the level called 100% of itself
        // solid and open sky ten thousand units up sat three thousand units inside stone.
        //
        // Left unsigned instead of dropped. Its surfaces still occlude -- a skybox ceiling should stop
        // a ray that reaches it -- but nothing it encloses is called solid. That is the same treatment
        // a two-sided surface gets, and for the same reason: there is no meaningful interior.
        bool container = span > ContainerSpan;

        bool[] inside = container
            ? new bool[count]
            : MarkInside(
                vertices, indices, triangles, min, voxelSize, sizeX, sizeY, sizeZ,
                IsClosed(indices, triangles));

        byte[] encoded = new byte[count];

        for (int i = 0; i < count; i++)
        {
            float signed = inside[i] ? -distance[i] : distance[i];
            encoded[i] = MeshDistanceField.Encode(signed, range);
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

    /// <summary>Decides which voxels are inside the surface, by casting along all three axes.</summary>
    /// <remarks>
    /// One ray per voxel per axis would be three casts times tens of thousands of voxels. Instead each
    /// <em>row</em> is done at once: every triangle crossing that row's line is collected, sorted, and
    /// the voxels along it flip between outside and inside at each crossing. That is one pass over the
    /// triangles per row rather than per voxel.
    /// <para>
    /// The majority vote across the three axes is what tolerates this game's open geometry. A single
    /// axis through a model with an open back reports the whole column behind it as inside; for the
    /// error to survive the vote, two axes have to pass through a hole at once.
    /// </para>
    /// </remarks>
    private static bool IsClosed(ReadOnlySpan<ushort> indices, int triangles)
    {
        // Every edge shared by exactly two triangles. That is what makes ray parity trustworthy: a ray
        // through a watertight mesh crosses the boundary an even number of times from outside, and no
        // axis can slip through a gap because there are none.
        Dictionary<(ushort Low, ushort High), int> edges = [];

        for (int t = 0; t < triangles; t++)
        {
            for (int e = 0; e < 3; e++)
            {
                ushort a = indices[(t * 3) + e];
                ushort b = indices[(t * 3) + ((e + 1) % 3)];

                if (a == b)
                {
                    return false;
                }

                (ushort, ushort) key = a < b ? (a, b) : (b, a);
                edges[key] = edges.TryGetValue(key, out int seen) ? seen + 1 : 1;
            }
        }

        foreach (int seen in edges.Values)
        {
            if (seen != 2)
            {
                return false;
            }
        }

        return true;
    }

    private static bool[] MarkInside(
        ReadOnlySpan<Vertex3D> vertices,
        ReadOnlySpan<ushort> indices,
        int triangles,
        Vector3f min,
        Vector3f voxelSize,
        int sizeX,
        int sizeY,
        int sizeZ,
        bool closed)
    {
        int count = sizeX * sizeY * sizeZ;
        byte[] votes = new byte[count];

        Vector3d[] a = new Vector3d[triangles];
        Vector3d[] b = new Vector3d[triangles];
        Vector3d[] c = new Vector3d[triangles];

        for (int t = 0; t < triangles; t++)
        {
            a[t] = ToDouble(vertices[indices[t * 3]].Position);
            b[t] = ToDouble(vertices[indices[(t * 3) + 1]].Position);
            c[t] = ToDouble(vertices[indices[(t * 3) + 2]].Position);
        }

        List<double> crossings = [];

        int[] sizes = [sizeX, sizeY, sizeZ];
        float[] origins = [min.X, min.Y, min.Z];
        float[] spacing = [voxelSize.X, voxelSize.Y, voxelSize.Z];

        // Each axis in turn. Written as a loop over the three rather than three times over, because the
        // only thing that differs is which index varies -- and three near-identical copies of a parity
        // sweep is three places for an axis to be transposed.
        for (int axis = 0; axis < 3; axis++)
        {
            int outerA = (axis + 1) % 3;
            int outerB = (axis + 2) % 3;

            // The segment the ray is cast along, running from just outside the volume to just outside
            // the far side. RayTriangle.Intersect is a SEGMENT test, not an infinite ray: it reports a
            // hit as a fraction of the direction vector and rejects anything at or past one. Handing it
            // a unit direction therefore searches exactly one unit ahead, which found no crossings at
            // all and reported the inside of every model as empty air.
            double start = origins[axis] - spacing[axis];
            double end = origins[axis] + (spacing[axis] * (sizes[axis] + 1));
            double span = end - start;

            for (int outer = 0; outer < sizes[outerB]; outer++)
            {
                for (int inner = 0; inner < sizes[outerA]; inner++)
                {
                    double[] position = new double[3];
                    position[axis] = start;
                    position[outerA] = origins[outerA] + ((inner + 0.5) * spacing[outerA]);
                    position[outerB] = origins[outerB] + ((outer + 0.5) * spacing[outerB]);

                    double[] direction = new double[3];
                    direction[axis] = span;

                    Collect(
                        crossings,
                        new Vector3d(position[0], position[1], position[2]),
                        new Vector3d(direction[0], direction[1], direction[2]),
                        a,
                        b,
                        c,
                        triangles);

                    if (crossings.Count == 0)
                    {
                        continue;
                    }

                    for (int step = 0; step < sizes[axis]; step++)
                    {
                        // As a fraction of the segment, because that is the space the crossings are in.
                        double at = ((origins[axis] + ((step + 0.5) * spacing[axis])) - start) / span;

                        if (!IsInside(crossings, at))
                        {
                            continue;
                        }

                        int[] voxel = new int[3];
                        voxel[axis] = step;
                        voxel[outerA] = inner;
                        voxel[outerB] = outer;

                        votes[(((voxel[2] * sizeY) + voxel[1]) * sizeX) + voxel[0]]++;
                    }
                }
            }
        }

        bool[] inside = new bool[count];

        for (int i = 0; i < count; i++)
        {
            // A closed mesh can trust a majority; an open one cannot, and this is measured.
            //
            // Parity along an axis that exits through a hole reports the whole column behind it as
            // solid, and this game's large geometry has open backs and missing roofs, so the wrong
            // answer routinely won two of three votes. Reverting to a majority took the clipmap
            // straight back to reading solid everywhere; unanimity is what makes it agree with the
            // exact field.
            //
            // Unanimity errs the other way: a voxel genuinely inside a mesh open on any axis reads as
            // air. That is the safe direction -- air where there is rock costs a missed shadow, rock
            // where there is air costs the whole level.
            //
            // Parity along an axis that exits through a hole reports the whole column behind it as
            // solid, and this game's large geometry has open backs and missing roofs, so the wrong
            // answer routinely won two of three votes. Letting a majority decide those took the clipmap
            // straight back to reading solid everywhere.
            //
            // Demanding all three is the safe reading for them -- it errs towards air, and air where
            // there is rock costs a missed shadow where the reverse cost the whole level. But it is the
            // WRONG reading for a watertight mesh, where all three axes are already reliable and
            // unanimity only throws away interiors that a single blocked axis should not veto: it made
            // a closed cube's own centre read as empty.
            //
            // So the mesh decides. Watertight, majority; open, unanimous.
            inside[i] = votes[i] >= (closed ? 2 : 3);
        }

        return inside;
    }

    /// <summary>Collects, in order, how far along a ray each triangle crossing is.</summary>
    private static void Collect(
        List<double> crossings,
        Vector3d origin,
        Vector3d direction,
        Vector3d[] a,
        Vector3d[] b,
        Vector3d[] c,
        int triangles)
    {
        crossings.Clear();

        for (int t = 0; t < triangles; t++)
        {
            if (RayTriangle.Intersect(origin, direction, a[t], b[t], c[t], out double hit))
            {
                crossings.Add(hit);
            }
        }

        crossings.Sort();
    }

    /// <summary>Counts how many crossings lie before a point, which is odd exactly when it is inside.</summary>
    private static bool IsInside(List<double> crossings, double at)
    {
        int before = 0;

        foreach (double crossing in crossings)
        {
            if (crossing > at)
            {
                break;
            }

            before++;
        }

        return (before & 1) == 1;
    }

    /// <summary>The distance from a point to a triangle.</summary>
    /// <remarks>
    /// The closest point on a triangle lies in its interior, on one of its three edges, or at one of its
    /// three vertices, and which of the seven it is falls out of the barycentric region the point
    /// projects into. Ericson's formulation, which decides that with comparisons rather than by solving
    /// and rejecting.
    /// </remarks>
    private static float Distance(Vector3f p, Vector3f a, Vector3f b, Vector3f c)
    {
        Vector3f ab = Subtract(b, a);
        Vector3f ac = Subtract(c, a);
        Vector3f ap = Subtract(p, a);

        float d1 = Dot(ab, ap);
        float d2 = Dot(ac, ap);

        if (d1 <= 0f && d2 <= 0f)
        {
            return Length(ap);
        }

        Vector3f bp = Subtract(p, b);
        float d3 = Dot(ab, bp);
        float d4 = Dot(ac, bp);

        if (d3 >= 0f && d4 <= d3)
        {
            return Length(bp);
        }

        float vc = (d1 * d4) - (d3 * d2);

        if (vc <= 0f && d1 >= 0f && d3 <= 0f)
        {
            float v = d1 / (d1 - d3);
            return Length(Subtract(ap, Scale(ab, v)));
        }

        Vector3f cp = Subtract(p, c);
        float d5 = Dot(ab, cp);
        float d6 = Dot(ac, cp);

        if (d6 >= 0f && d5 <= d6)
        {
            return Length(cp);
        }

        float vb = (d5 * d2) - (d1 * d6);

        if (vb <= 0f && d2 >= 0f && d6 <= 0f)
        {
            float w = d2 / (d2 - d6);
            return Length(Subtract(ap, Scale(ac, w)));
        }

        float va = (d3 * d6) - (d5 * d4);

        if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
        {
            float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
            return Length(Subtract(Subtract(p, b), Scale(Subtract(c, b), w)));
        }

        float denom = 1f / (va + vb + vc);
        float vv = vb * denom;
        float ww = vc * denom;

        return Length(Subtract(ap, Add(Scale(ab, vv), Scale(ac, ww))));
    }

    private static Vector3d ToDouble(Vector3f v) => new(v.X, v.Y, v.Z);

    private static Vector3f Add(Vector3f a, Vector3f b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    private static Vector3f Subtract(Vector3f a, Vector3f b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    private static Vector3f Scale(Vector3f a, float s) => new(a.X * s, a.Y * s, a.Z * s);

    private static float Dot(Vector3f a, Vector3f b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

    private static float Length(Vector3f a) => MathF.Sqrt(Dot(a, a));
}
