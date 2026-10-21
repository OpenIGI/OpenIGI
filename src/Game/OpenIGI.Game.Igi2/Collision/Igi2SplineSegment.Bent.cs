using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Collision;

/// <content>
/// A segment bent along its curve, <c>SplineObjSegment</c> with its linear flag clear: its collision meshes are bent once, when it
/// is made, into the segment's own space, and lines are traced against them there.
/// </content>
/// <remarks>
/// <para>
/// <c>SplineObjSegment_TransformVertex</c> (<c>igi2 0x5BA740</c>) takes a model vertex's X as a place along the curve: from the
/// model's X extent onto the segment's areas, which picks an area and a fraction into it. The vertex's Y and Z are an offset in
/// the area's cross-section, turned by the area's frame and added to the area's point, at each end of the area; the two are mixed
/// by the fraction. A flipped segment runs the extent the other way.
/// </para>
/// <para>
/// The sphere tree is the model's, its centres bent the same way (<c>SplineObjSegment_TransformSpherePos</c>, <c>0x5B7140</c>), and
/// each radius grown again to reach every vertex of every face beneath it (<c>SplineObjSegment_CreateSphere</c>, <c>0x5BA4F0</c>).
/// </para>
/// <para>
/// The areas' points and frames are the curve's samples (<see cref="Igi2SplineSweep"/>), taken from the segment's midpoint.
/// </para>
/// </remarks>
public sealed partial class Igi2SplineSegment
{
    /// <summary>Gets a value indicating whether the segment is bent along its curve rather than stretched straight.</summary>
    public bool IsBent { get; private init; }

    /// <summary>Builds a bent segment from its curve, as <c>SplineObjSegment_Update</c> (<c>igi2 0x5BAB10</c>) does.</summary>
    /// <param name="owner">What a hit reports as struck.</param>
    /// <param name="sweep">The curve between the two waypoints.</param>
    /// <param name="meshes">The segment model's collision mesh levels, by <c>iMesh</c>.</param>
    /// <returns>The segment, or <see langword="null"/> when the model has no first mesh level or the curve has no area.</returns>
    /// <remarks>
    /// The model's X extent is the lowest and highest X among the first level's vertices, both from zero. The radius the bounds test
    /// uses is the farthest of the first two levels' bent vertices, measured as the original measures it, from the earlier waypoint
    /// (the bent vertices being from the midpoint), and at least one.
    /// </remarks>
    public static Igi2SplineSegment? Bent(object owner, Igi2SplineSweep sweep, IReadOnlyList<Igi2CollisionMesh?> meshes)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(sweep);
        ArgumentNullException.ThrowIfNull(meshes);
        if (meshes.Count == 0 || meshes[0] is not { } first || sweep.Frames.Count < 2)
        {
            return null;
        }

        Vector3d start = sweep.Start, stop = sweep.Stop;
        Vector3d position = new(
            (((Fpu)start.X + stop.X) * 0.5).Value,
            (((Fpu)stop.Y + start.Y) * 0.5).Value,
            (((Fpu)stop.Z + start.Z) * 0.5).Value);

        float low = 0f, high = 0f;
        float[] firstVertices = first.Arrays.Vertices;
        for (int i = 0; i < firstVertices.Length; i += 3)
        {
            float x = firstVertices[i];
            if (x < low)
            {
                low = x;
            }

            if (x > high)
            {
                high = x;
            }
        }

        Bender bender = new(sweep, low, high);
        Igi2CollisionMesh?[] bent = new Igi2CollisionMesh?[meshes.Count];
        for (int level = 0; level < meshes.Count; level++)
        {
            if (meshes[level] is { } mesh)
            {
                bent[level] = Bend(mesh, bender);
            }
        }

        // The radius from the earlier waypoint: the midpoint less the start, plus each bent vertex.
        double ox = position.X - start.X, oy = position.Y - start.Y, oz = position.Z - start.Z;
        double radius = 0.0;
        for (int level = 0; level < 2 && level < bent.Length; level++)
        {
            if (bent[level] is not { } mesh)
            {
                continue;
            }

            float[] v = mesh.Arrays.Vertices;
            for (int i = 0; i < v.Length; i += 3)
            {
                double x = ox + v[i], y = oy + v[i + 1], z = oz + v[i + 2];
                double distance = Math.Sqrt((x * x) + (y * y) + (z * z));
                if (radius <= distance)
                {
                    radius = distance;
                }
            }

            if (radius <= 1.0)
            {
                radius = 1.0;
            }
        }

        float[] identity = [1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f];
        return new Igi2SplineSegment(owner, bent, position, identity, identity, 0f, (float)radius, sweep.IsFlipped)
        {
            IsBent = true,
        };
    }

    // The mesh bent: each vertex and each sphere's centre, then each sphere's radius regrown over the faces beneath it.
    private static Igi2CollisionMesh Bend(Igi2CollisionMesh mesh, Bender bender)
    {
        RetailRayMesh source = mesh.Arrays;
        float[] vertices = new float[source.Vertices.Length];
        for (int i = 0; i < vertices.Length; i += 3)
        {
            Vector3f p = bender.Transform(source.Vertices[i], source.Vertices[i + 1], source.Vertices[i + 2]);
            vertices[i] = p.X;
            vertices[i + 1] = p.Y;
            vertices[i + 2] = p.Z;
        }

        float[] spheres = new float[source.Spheres.Length];
        for (int i = 0; i < spheres.Length; i += 4)
        {
            Vector3f c = bender.Transform(source.Spheres[i], source.Spheres[i + 1], source.Spheres[i + 2]);
            spheres[i] = c.X;
            spheres[i + 1] = c.Y;
            spheres[i + 2] = c.Z;
            spheres[i + 3] = source.Spheres[i + 3];
        }

        RetailRayMesh arrays = new(source, vertices, spheres);
        if (arrays.SphereCount > 0)
        {
            List<int> stack = [];
            GrowSphere(arrays, 0, stack);
        }

        return new Igi2CollisionMesh(mesh, arrays);
    }

    // SplineObjSegment_CreateSphere: the sphere's radius from nothing; its children first, then each vertex of each of its faces
    // reaching it and every sphere above it.
    private static void GrowSphere(RetailRayMesh mesh, int sphere, List<int> stack)
    {
        float[] s = mesh.Spheres;
        int[] links = mesh.Links;
        s[(4 * sphere) + 3] = 0f;
        stack.Add(sphere);
        int child = links[(4 * sphere) + 2];
        if (child != 0)
        {
            GrowSphere(mesh, child, stack);
            for (int next = links[(4 * child) + 3]; next != 0; next = links[(4 * next) + 3])
            {
                GrowSphere(mesh, next, stack);
            }
        }

        int firstFace = links[4 * sphere], faceCount = links[(4 * sphere) + 1];
        float[] v = mesh.Vertices;
        int[] faces = mesh.Faces;
        for (int face = firstFace; face < firstFace + faceCount; face++)
        {
            for (int k = 0; k < 3; k++)
            {
                int at = 3 * faces[(4 * face) + k];
                foreach (int held in stack)
                {
                    int c = 4 * held;
                    Fpu dy = (Fpu)v[at + 1] - s[c + 1], dx = (Fpu)v[at] - s[c], dz = (Fpu)v[at + 2] - s[c + 2];
                    Fpu reach = Fpu.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
                    if (reach.Value < s[c + 3])
                    {
                        reach = s[c + 3];
                    }

                    s[c + 3] = reach.F;
                }
            }
        }

        stack.RemoveAt(stack.Count - 1);
    }

    /// <summary>The curve a vertex is bent along, <c>SplineObjSegment_TransformVertex</c>'s bent path.</summary>
    private sealed class Bender(Igi2SplineSweep sweep, float startX, float endX)
    {
        private readonly IReadOnlyList<Igi2SplineFrame> _frames = sweep.Frames;
        private readonly int _areas = sweep.Frames.Count - 1;
        private readonly bool _flip = sweep.IsFlipped;

        public Vector3f Transform(float x, float y, float z)
        {
            float inverse = ((Fpu)1.0 / ((Fpu)endX - startX)).F;
            Fpu origin = -(Fpu)(_flip ? endX : startX);
            Fpu scale = (Fpu)(double)_areas * inverse;
            if (_flip)
            {
                scale = -scale;
            }

            Fpu along = scale * (origin + x);
            int area = (int)Math.Truncate(along.Value);
            if (area == _areas)
            {
                area--;
            }

            area = Math.Clamp(area, 0, _areas - 1);
            Fpu fraction = along - (double)area;
            fraction = fraction.Value >= 0.0 ? (fraction.Value > 1.0 ? (Fpu)1.0 : fraction) : (Fpu)0.0;

            Igi2SplineFrame near = _frames[area], far = _frames[area + 1];
            Matrix3f a = near.Orientation, b = far.Orientation;
            float ax = ((((Fpu)a.M13 * z) + ((Fpu)a.M12 * y)) + near.Position.X).F;
            Fpu ay = (((Fpu)a.M23 * z) + ((Fpu)a.M22 * y)) + near.Position.Y;
            float azTurn = (((Fpu)a.M33 * z) + ((Fpu)a.M32 * y)).F;
            float az = ((Fpu)azTurn + near.Position.Z).F;
            float bzTurn = (((Fpu)b.M33 * z) + ((Fpu)b.M32 * y)).F;
            float bx = ((((Fpu)b.M13 * z) + ((Fpu)b.M12 * y)) + far.Position.X).F;
            Fpu by = (((Fpu)b.M23 * z) + ((Fpu)b.M22 * y)) + far.Position.Y;
            float bz = ((Fpu)bzTurn + far.Position.Z).F;
            return new Vector3f(
                ((((Fpu)bx - ax) * fraction) + ax).F,
                (((by - ay) * fraction) + ay).F,
                ((((Fpu)bz - az) * fraction) + az).F);
        }
    }
}
