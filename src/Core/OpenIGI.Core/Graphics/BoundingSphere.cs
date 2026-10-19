using OpenIGI.Core.Numerics;

namespace OpenIGI.Core.Graphics;

/// <summary>A conservative local-space sphere used to reject whole meshes before rasterisation.</summary>
public readonly record struct BoundingSphere(Vector3f Centre, float Radius)
{
    /// <summary>Calculates an axis-aligned-box-derived sphere enclosing every vertex.</summary>
    /// <param name="vertices">The local-space vertices to enclose.</param>
    /// <returns>The enclosing sphere, or an empty sphere for no vertices.</returns>
    public static BoundingSphere FromVertices(ReadOnlySpan<Vertex3D> vertices)
    {
        if (vertices.IsEmpty)
        {
            return default;
        }

        Vector3f minimum = vertices[0].Position;
        Vector3f maximum = minimum;
        for (int i = 1; i < vertices.Length; i++)
        {
            Vector3f point = vertices[i].Position;
            minimum = new(MathF.Min(minimum.X, point.X), MathF.Min(minimum.Y, point.Y), MathF.Min(minimum.Z, point.Z));
            maximum = new(MathF.Max(maximum.X, point.X), MathF.Max(maximum.Y, point.Y), MathF.Max(maximum.Z, point.Z));
        }

        Vector3f centre = (minimum + maximum) * 0.5f;
        float radiusSquared = 0f;
        foreach (Vertex3D vertex in vertices)
        {
            Vector3f delta = vertex.Position - centre;
            radiusSquared = MathF.Max(radiusSquared, Vector3f.Dot(delta, delta));
        }

        return new(centre, MathF.Sqrt(radiusSquared));
    }
}
