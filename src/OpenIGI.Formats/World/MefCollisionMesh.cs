using System.Buffers.Binary;
using OpenIGI.Core;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ilff;

namespace OpenIGI.Formats.World;

/// <summary>One triangle of a collision mesh.</summary>
/// <param name="I0">The first vertex index.</param>
/// <param name="I1">The second.</param>
/// <param name="I2">The third.</param>
/// <param name="Material">
/// The fourth field, believed to select a row of <see cref="MefCollisionMesh.Materials"/>. Zero
/// throughout the sampled models, so its role is recorded rather than asserted.
/// </param>
public readonly record struct MefCollisionFace(int I0, int I1, int I2, int Material);

/// <summary>One node of a collision mesh's sphere tree.</summary>
/// <param name="Centre">The sphere's centre, in the model's own space.</param>
/// <param name="Radius">Its radius.</param>
/// <param name="Field4">The fifth dword — the root carries <c>0xFFFF</c> in its low half.</param>
/// <param name="Field5">The sixth dword.</param>
/// <remarks>
/// The two trailing dwords index the tree — a parent or child link and a face reference — but which
/// is which is not settled, so they are carried unnamed rather than guessed at.
/// </remarks>
public readonly record struct MefCollisionSphere(Vector3f Centre, float Radius, int Field4, int Field5);

/// <summary>
/// A model's collision geometry: the shape the world is solid with.
/// </summary>
/// <remarks>
/// <para>
/// Four chunks, headed by <c>HSMC</c>, whose 64-byte payload opens with the four counts and is zero
/// after them. Every stride below was checked against <b>all 254</b> collision meshes in level 1's
/// packs, with no mismatch — the counts and the chunk lengths agree exactly everywhere.
/// </para>
/// <list type="table">
/// <item><term><c>ECFC</c></term><description>faces, <b>8</b> bytes: four <c>u16</c></description></item>
/// <item><term><c>XTVC</c></term><description>vertices, <b>16</b> bytes: three floats and a zero</description></item>
/// <item><term><c>TAMC</c></term><description>materials, <b>12</b> bytes</description></item>
/// <item><term><c>HPSC</c></term><description>spheres, <b>24</b> bytes: a centre, a radius, two dwords</description></item>
/// </list>
/// <para>
/// <b>The spheres are a binary tree over the faces.</b> Their count is exactly <c>2 × faces − 1</c> in
/// every model sampled — 40 faces to 79 spheres, 152 to 303, 1180 to 2359 — which is the node count of
/// a binary tree with one leaf per face. The root's fifth dword carries <c>0xFFFF</c> in its low half
/// where the others carry small ascending indices, which is what a "no parent" marker looks like.
/// </para>
/// </remarks>
public sealed class MefCollisionMesh
{
    /// <summary>The header's size, of which only the first four dwords are used.</summary>
    public const int HeaderSize = 64;

    /// <summary>Bytes per face.</summary>
    public const int FaceStride = 8;

    /// <summary>Bytes per vertex.</summary>
    public const int VertexStride = 16;

    /// <summary>Bytes per material.</summary>
    public const int MaterialStride = 12;

    /// <summary>Bytes per sphere.</summary>
    public const int SphereStride = 24;

    private MefCollisionMesh(
        MefCollisionFace[] faces,
        Vector3f[] vertices,
        MefCollisionSphere[] spheres,
        int materialCount)
    {
        Faces = faces;
        Vertices = vertices;
        Spheres = spheres;
        Materials = materialCount;
    }

    /// <summary>Gets the collision triangles.</summary>
    public IReadOnlyList<MefCollisionFace> Faces { get; }

    /// <summary>Gets the collision vertices, in the model's own space.</summary>
    public IReadOnlyList<Vector3f> Vertices { get; }

    /// <summary>Gets the sphere tree over the faces.</summary>
    public IReadOnlyList<MefCollisionSphere> Spheres { get; }

    /// <summary>Gets how many material rows the mesh carries.</summary>
    /// <remarks>The rows' 12 bytes are not yet decoded, so only the count is exposed.</remarks>
    public int Materials { get; }

    /// <summary>Whether the sphere count is the binary tree's over the faces.</summary>
    /// <remarks><c>2 × faces − 1</c>, which holds for every shipped mesh.</remarks>
    public bool SphereCountIsBinaryTree => Faces.Count > 0 && Spheres.Count == (2 * Faces.Count) - 1;

    /// <summary>Reads a model's collision mesh, if it carries one.</summary>
    /// <param name="container">The model's chunks.</param>
    /// <param name="mesh">Receives the mesh.</param>
    /// <returns><see langword="false"/> when the model has no collision geometry, which is not an error.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="container"/> is <see langword="null"/>.</exception>
    /// <exception cref="PackFormatException">A chunk's length disagrees with the header's count.</exception>
    public static bool TryRead(IlffContainer container, out MefCollisionMesh? mesh)
    {
        ArgumentNullException.ThrowIfNull(container);
        mesh = null;

        if (!container.TryFindChunk(MefTags.CollisionMesh, out IlffChunk header) || header.Payload.Length == 0)
        {
            return false;
        }

        ReadOnlySpan<byte> head = header.Payload.Span;
        if (head.Length < 16)
        {
            throw new PackFormatException(
                $"The '{MefTags.CollisionMesh}' chunk is {head.Length} bytes, too short for its four counts.");
        }

        int faceCount = BinaryPrimitives.ReadInt32LittleEndian(head);
        int vertexCount = BinaryPrimitives.ReadInt32LittleEndian(head[4..]);
        int materialCount = BinaryPrimitives.ReadInt32LittleEndian(head[8..]);
        int sphereCount = BinaryPrimitives.ReadInt32LittleEndian(head[12..]);

        if (faceCount == 0 && vertexCount == 0)
        {
            return false;
        }

        ReadOnlySpan<byte> faceBytes = Payload(container, MefTags.CollisionFaces, faceCount, FaceStride);
        ReadOnlySpan<byte> vertexBytes = Payload(container, MefTags.CollisionVertices, vertexCount, VertexStride);
        ReadOnlySpan<byte> sphereBytes = Payload(container, MefTags.CollisionSpheres, sphereCount, SphereStride);
        _ = Payload(container, MefTags.CollisionMaterials, materialCount, MaterialStride);

        Vector3f[] vertices = new Vector3f[vertexCount];
        for (int i = 0; i < vertexCount; i++)
        {
            ReadOnlySpan<byte> row = vertexBytes.Slice(i * VertexStride, VertexStride);
            vertices[i] = new Vector3f(
                BinaryPrimitives.ReadSingleLittleEndian(row),
                BinaryPrimitives.ReadSingleLittleEndian(row[4..]),
                BinaryPrimitives.ReadSingleLittleEndian(row[8..]));
        }

        MefCollisionFace[] faces = new MefCollisionFace[faceCount];
        for (int i = 0; i < faceCount; i++)
        {
            ReadOnlySpan<byte> row = faceBytes.Slice(i * FaceStride, FaceStride);
            faces[i] = new MefCollisionFace(
                BinaryPrimitives.ReadUInt16LittleEndian(row),
                BinaryPrimitives.ReadUInt16LittleEndian(row[2..]),
                BinaryPrimitives.ReadUInt16LittleEndian(row[4..]),
                BinaryPrimitives.ReadUInt16LittleEndian(row[6..]));
        }

        MefCollisionSphere[] spheres = new MefCollisionSphere[sphereCount];
        for (int i = 0; i < sphereCount; i++)
        {
            ReadOnlySpan<byte> row = sphereBytes.Slice(i * SphereStride, SphereStride);
            spheres[i] = new MefCollisionSphere(
                new Vector3f(
                    BinaryPrimitives.ReadSingleLittleEndian(row),
                    BinaryPrimitives.ReadSingleLittleEndian(row[4..]),
                    BinaryPrimitives.ReadSingleLittleEndian(row[8..])),
                BinaryPrimitives.ReadSingleLittleEndian(row[12..]),
                BinaryPrimitives.ReadInt32LittleEndian(row[16..]),
                BinaryPrimitives.ReadInt32LittleEndian(row[20..]));
        }

        mesh = new MefCollisionMesh(faces, vertices, spheres, materialCount);
        return true;
    }

    private static ReadOnlySpan<byte> Payload(IlffContainer container, FourCc tag, int count, int stride)
    {
        if (count == 0)
        {
            return default;
        }

        if (!container.TryFindChunk(tag, out IlffChunk chunk))
        {
            throw new PackFormatException($"The collision header declares {count} '{tag}' records but the chunk is missing.");
        }

        ReadOnlySpan<byte> span = chunk.Payload.Span;
        int needed = count * stride;
        if (span.Length != needed)
        {
            throw new PackFormatException(
                $"The '{tag}' chunk is {span.Length} bytes but {count} records at a {stride}-byte stride need {needed}.");
        }

        return span;
    }
}
