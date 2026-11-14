using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering;

/// <summary>A list of camera-facing quads with a mesh of its own, for sprites drawn about points in the world.</summary>
/// <remarks>
/// Shared by everything that draws a glow at a point: a security camera's lamp, an alarm light's flare and
/// the glows a model carries. Each keeps a batch of its own, so each keeps the vertices it was drawn with.
/// </remarks>
/// <param name="capacity">The most quads one draw holds; what is added beyond it is dropped.</param>
public sealed class GlowQuadBatch(int capacity)
{
    /// <summary>The vertices one quad takes.</summary>
    public const int VerticesPerQuad = 4;

    /// <summary>The indices one quad takes.</summary>
    public const int IndicesPerQuad = 6;

    private int _lastUsed;

    /// <summary>Gets the vertices, four a quad.</summary>
    public Vertex3D[] Vertices { get; } = new Vertex3D[capacity * VerticesPerQuad];

    /// <summary>Gets or sets the mesh the quads are uploaded to, made on the first draw.</summary>
    public MeshHandle Mesh { get; set; } = MeshHandle.None;

    /// <summary>Gets or sets the left edge of the sprite's frame.</summary>
    public float U0 { get; set; }

    /// <summary>Gets or sets the top edge of the sprite's frame.</summary>
    public float V0 { get; set; }

    /// <summary>Gets or sets the right edge of the sprite's frame.</summary>
    public float U1 { get; set; } = 1f;

    /// <summary>Gets or sets the bottom edge of the sprite's frame.</summary>
    public float V1 { get; set; } = 1f;

    /// <summary>Adds a quad facing the eye about a point.</summary>
    /// <param name="used">How many vertices the batch holds so far.</param>
    /// <param name="pointMetres">The point, in metres.</param>
    /// <param name="half">The quad's half-size, in metres.</param>
    /// <param name="colour">The colour the sprite is drawn in.</param>
    /// <param name="biasMetres">How far the quad is pulled toward the eye along its own ray.</param>
    /// <param name="eye">The eye.</param>
    /// <returns>How many vertices the batch holds now.</returns>
    public int Add(int used, Vector3d pointMetres, float half, Bgra32 colour, double biasMetres, in Camera3D eye)
    {
        if (used + VerticesPerQuad > Vertices.Length)
        {
            return used;
        }

        Vector3d centre = pointMetres - eye.Position;
        double distance = centre.Length;
        if (!(distance > 1e-6))
        {
            return used;
        }

        Vector3d toEye = centre / -distance;
        Vector3d reference = Math.Abs(toEye.Z) < 0.9 ? new Vector3d(0, 0, 1) : new Vector3d(1, 0, 0);
        Vector3d rightD = Vector3d.Cross(reference, toEye);
        double rightLength = rightD.Length;
        if (!(rightLength > 1e-9))
        {
            return used;
        }

        rightD /= rightLength;
        Vector3d upD = Vector3d.Cross(toEye, rightD);
        centre += toEye * biasMetres;
        Vector3f right = new((float)(rightD.X * half), (float)(rightD.Y * half), (float)(rightD.Z * half));
        Vector3f up = new((float)(upD.X * half), (float)(upD.Y * half), (float)(upD.Z * half));
        Vector3f centreF = new((float)centre.X, (float)centre.Y, (float)centre.Z);
        Vector3f normal = new((float)toEye.X, (float)toEye.Y, (float)toEye.Z);

        Vertices[used] = new Vertex3D(centreF - right - up, normal, U0, V0, colour: colour);
        Vertices[used + 1] = new Vertex3D(centreF + right - up, normal, U1, V0, colour: colour);
        Vertices[used + 2] = new Vertex3D(centreF + right + up, normal, U1, V1, colour: colour);
        Vertices[used + 3] = new Vertex3D(centreF - right + up, normal, U0, V1, colour: colour);
        return used + VerticesPerQuad;
    }

    /// <summary>Clears what an earlier, fuller draw left past this one's quads.</summary>
    /// <param name="used">How many vertices this draw holds.</param>
    public void Trim(int used)
    {
        if (_lastUsed > used)
        {
            Array.Clear(Vertices, used, _lastUsed - used);
        }

        _lastUsed = used;
    }

    /// <summary>The indices for every quad the batch can hold.</summary>
    /// <returns>Two triangles a quad.</returns>
    public ushort[] BuildIndices()
    {
        int quads = Vertices.Length / VerticesPerQuad;
        ushort[] indices = new ushort[quads * IndicesPerQuad];
        for (int quad = 0; quad < quads; quad++)
        {
            int vertex = quad * VerticesPerQuad;
            int index = quad * IndicesPerQuad;
            indices[index] = (ushort)vertex;
            indices[index + 1] = (ushort)(vertex + 1);
            indices[index + 2] = (ushort)(vertex + 2);
            indices[index + 3] = (ushort)vertex;
            indices[index + 4] = (ushort)(vertex + 2);
            indices[index + 5] = (ushort)(vertex + 3);
        }

        return indices;
    }
}
