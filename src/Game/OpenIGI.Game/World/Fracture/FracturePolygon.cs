using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World.Fracture;

/// <summary>A plane that cuts a mesh: the points with <c>dot(Normal, p) &lt;= Offset</c> lie inside it.</summary>
/// <param name="Normal">The plane's normal, unit length, pointing out of the inside.</param>
/// <param name="Offset">How far along the normal the plane stands from the origin.</param>
public readonly record struct FracturePlane(Vector3f Normal, float Offset)
{
    /// <summary>How far outside the plane a point is: negative inside, positive outside.</summary>
    /// <param name="point">The point.</param>
    /// <returns>The signed distance.</returns>
    public float Distance(Vector3f point) => Vector3f.Dot(Normal, point) - Offset;

    /// <summary>The plane halfway between two points, its inside the side nearer the first.</summary>
    /// <param name="near">The point whose side is inside.</param>
    /// <param name="far">The other.</param>
    /// <returns>The bisecting plane.</returns>
    public static FracturePlane Between(Vector3f near, Vector3f far)
    {
        Vector3f normal = (far - near).Normalized();
        Vector3f middle = (near + far) * 0.5f;
        return new FracturePlane(normal, Vector3f.Dot(normal, middle));
    }
}

/// <summary>One convex polygon of a mesh being cut, with the subset (material) it belongs to.</summary>
/// <remarks>
/// A polygon rather than a triangle because clipping a triangle by a plane leaves a quadrilateral, and by several planes a
/// polygon of more sides: it is fanned back into triangles only once every cut is made, so no cut works on slivers another
/// made.
/// </remarks>
public sealed class FracturePolygon
{
    /// <summary>Initializes a new instance of the <see cref="FracturePolygon"/> class.</summary>
    /// <param name="vertices">Its corners, in order.</param>
    /// <param name="subset">The subset of the source mesh it came from, or <see cref="Interior"/> for a face a cut made.</param>
    public FracturePolygon(List<Vertex3D> vertices, int subset)
    {
        Vertices = vertices;
        Subset = subset;
    }

    /// <summary>The subset a face a cut made belongs to: the inside of what was cut, which no source subset textures.</summary>
    public const int Interior = -1;

    /// <summary>Gets its corners, in order.</summary>
    public List<Vertex3D> Vertices { get; }

    /// <summary>Gets the subset of the source mesh it came from, or <see cref="Interior"/>.</summary>
    public int Subset { get; }

    /// <summary>Splits it by a plane.</summary>
    /// <param name="plane">The plane.</param>
    /// <param name="inside">Receives the part inside the plane, or <see langword="null"/> for none.</param>
    /// <param name="outside">Receives the part outside, or <see langword="null"/> for none.</param>
    /// <remarks>
    /// Sutherland-Hodgman, both halves at once: every attribute of a vertex the plane makes -- the texture's coordinates,
    /// the lightmap's, the normal, the colour -- is interpolated where the edge crosses, so a cut face shows the texture it
    /// showed before it was cut.
    /// </remarks>
    public void Split(in FracturePlane plane, out FracturePolygon? inside, out FracturePolygon? outside)
    {
        int count = Vertices.Count;
        Span<float> distance = count <= 64 ? stackalloc float[count] : new float[count];
        bool anyIn = false;
        bool anyOut = false;
        for (int i = 0; i < count; i++)
        {
            distance[i] = plane.Distance(Vertices[i].Position);
            anyIn |= distance[i] <= 0f;
            anyOut |= distance[i] > 0f;
        }

        if (!anyOut)
        {
            inside = this;
            outside = null;
            return;
        }

        if (!anyIn)
        {
            inside = null;
            outside = this;
            return;
        }

        List<Vertex3D> keptIn = new(count + 2);
        List<Vertex3D> keptOut = new(count + 2);
        for (int i = 0; i < count; i++)
        {
            int next = (i + 1) % count;
            Vertex3D a = Vertices[i];
            float da = distance[i];
            float db = distance[next];
            (da <= 0f ? keptIn : keptOut).Add(a);
            if ((da <= 0f) != (db <= 0f))
            {
                Vertex3D cut = Lerp(a, Vertices[next], da / (da - db));
                keptIn.Add(cut);
                keptOut.Add(cut);
            }
        }

        inside = keptIn.Count >= 3 ? new FracturePolygon(keptIn, Subset) : null;
        outside = keptOut.Count >= 3 ? new FracturePolygon(keptOut, Subset) : null;
    }

    /// <summary>Gets its area, for weighing what is cut and dropping slivers.</summary>
    /// <returns>The area, in the mesh's units squared.</returns>
    public float Area()
    {
        Vector3f sum = default;
        Vector3f origin = Vertices[0].Position;
        for (int i = 1; i + 1 < Vertices.Count; i++)
        {
            sum += Vector3f.Cross(Vertices[i].Position - origin, Vertices[i + 1].Position - origin);
        }

        return sum.Length * 0.5f;
    }

    /// <summary>A vertex between two others, every attribute interpolated.</summary>
    /// <param name="a">The first.</param>
    /// <param name="b">The second.</param>
    /// <param name="t">How far from the first to the second.</param>
    /// <returns>The vertex.</returns>
    public static Vertex3D Lerp(in Vertex3D a, in Vertex3D b, float t)
    {
        Vector3f normal = a.Normal + ((b.Normal - a.Normal) * t);
        return new Vertex3D(
            a.Position + ((b.Position - a.Position) * t),
            normal.LengthSquared > 1e-12f ? normal.Normalized() : a.Normal,
            a.DiffuseU + ((b.DiffuseU - a.DiffuseU) * t),
            a.DiffuseV + ((b.DiffuseV - a.DiffuseV) * t),
            a.LightmapU + ((b.LightmapU - a.LightmapU) * t),
            a.LightmapV + ((b.LightmapV - a.LightmapV) * t),
            LerpColour(a.Colour, b.Colour, t),
            t < 0.5f ? a.SurfaceNormal : b.SurfaceNormal);
    }

    private static Bgra32 LerpColour(Bgra32 a, Bgra32 b, float t) => new(
        (byte)Math.Clamp(MathF.Round(a.B + ((b.B - a.B) * t)), 0, 255),
        (byte)Math.Clamp(MathF.Round(a.G + ((b.G - a.G) * t)), 0, 255),
        (byte)Math.Clamp(MathF.Round(a.R + ((b.R - a.R) * t)), 0, 255),
        (byte)Math.Clamp(MathF.Round(a.A + ((b.A - a.A) * t)), 0, 255));
}
