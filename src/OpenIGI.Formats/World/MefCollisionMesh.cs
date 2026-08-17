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
/// The fourth field: a row of this mesh's own <c>TAMC</c> table.
/// <b>Correction</b> — an earlier reading recorded this as "zero throughout the sampled models". It is
/// not: across level 1's models the selectors span <c>0</c> to <c>Materials − 1</c>, so it is a real
/// per-face index. What a <c>TAMC</c> row then says about the <i>game</i> material is still unread; see
/// the original.
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
/// Four chunk groups, headed by <c>HSMC</c>. Its 64-byte payload holds two 32-byte
/// <c>MultiMesh_Mesh_t</c> headers, one for each collision mesh level. A non-empty level has its own
/// <c>ECFC</c>, <c>XTVC</c>, <c>TAMC</c>, and <c>HPSC</c> chunk, in level order. Every stride below was
/// checked against level 1's retail packs, with no mismatch — the counts and the chunk lengths agree
/// exactly everywhere.
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
    /// <summary>The HSMC header's size: two 32-byte <c>MultiMesh_Mesh_t</c> records.</summary>
    public const int HeaderSize = 64;

    /// <summary>Bytes occupied by one collision-level header.</summary>
    public const int MeshHeaderSize = 32;

    /// <summary>How many collision mesh levels an HSMC header carries.</summary>
    public const int MeshLevels = HeaderSize / MeshHeaderSize;

    /// <summary>Bytes per face.</summary>
    public const int FaceStride = 8;

    /// <summary>Bytes per vertex.</summary>
    public const int VertexStride = 16;

    /// <summary>Bytes per material.</summary>
    public const int MaterialStride = 12;

    /// <summary>Bytes per sphere.</summary>
    public const int SphereStride = 24;

    /// <summary>The <c>iPortal</c> value that means "not a portal" — <c>0xFFFF</c>, i.e. −1.</summary>
    /// <remarks>
    /// A material row's <c>iPortal</c> (the <c>u16</c> at offset 4, IGI2 <c>MultiMesh_Material_t.iPortal</c>)
    /// indexes a portal when it is anything other than this. A portal face is a doorway or window opening:
    /// non-solid, so a body walks through it. See <see cref="IsPortal"/>.
    /// </remarks>
    public const int NotAPortal = 0xFFFF;

    private readonly bool[] _materialIsPortal;
    private readonly int[]? _materialGlobal;

    private MefCollisionMesh(
        MefCollisionFace[] faces,
        Vector3f[] vertices,
        int[] vertexBones,
        MefCollisionSphere[] spheres,
        bool[] materialIsPortal,
        int[]? materialGlobal = null)
    {
        Faces = faces;
        Vertices = vertices;
        VertexBones = vertexBones;
        Spheres = spheres;
        Materials = materialIsPortal.Length;
        _materialIsPortal = materialIsPortal;
        _materialGlobal = materialGlobal;
    }

    /// <summary>Resolves a face's per-mesh material to its global game-material row.</summary>
    /// <param name="material">The face's material index — <see cref="MefCollisionFace.Material"/>.</param>
    /// <returns>
    /// The row of the global game-material table this face is made of (0 = Air, 14 = Glass, 18 = Fence,
    /// …), read from the <c>TAMC</c> row's <c>u16</c> at offset 8. Falls back to the per-mesh index when
    /// no global mapping is present (a hand-built mesh) or the row names no global material (<c>0xFFFF</c>).
    /// </returns>
    public int GlobalMaterial(int material) =>
        _materialGlobal is { } table && (uint)material < (uint)table.Length && table[material] != NotAPortal
            ? table[material]
            : material;

    /// <summary>Gets the collision triangles.</summary>
    public IReadOnlyList<MefCollisionFace> Faces { get; }

    /// <summary>Gets the collision vertices, in the model's own space.</summary>
    public IReadOnlyList<Vector3f> Vertices { get; }

    /// <summary>Gets the bone each collision vertex is rigidly bound to — the vertex's fourth field.</summary>
    /// <remarks>
    /// The <c>XTVC</c> vertex is a <c>float3</c> position and one <c>u32</c> after it. For a static object
    /// that trailing word is zero; for a <b>skinned human</b> it is the rig bone the vertex follows, so the
    /// collision mesh deforms with the pose and a round can be scored to the exact bone it passed through
    /// (IGI2 <c>MultiMesh_Vertex_t</c>'s bone field; the whole point of <c>BoneDynCubeObj_TraceLinesHandler</c>).
    /// Verified against the retail human models: the values are the 33-bone rig's own numbering.
    /// </remarks>
    public IReadOnlyList<int> VertexBones { get; }

    /// <summary>Gets the sphere tree over the faces.</summary>
    public IReadOnlyList<MefCollisionSphere> Spheres { get; }

    /// <summary>Gets how many material rows the mesh carries.</summary>
    public int Materials { get; }

    /// <summary>Whether a face of the given material is a non-solid portal (a doorway or window opening).</summary>
    /// <param name="material">The face's material index — <see cref="MefCollisionFace.Material"/>.</param>
    /// <returns><see langword="true"/> when the material's <c>iPortal</c> marks it a portal.</returns>
    /// <remarks>
    /// This is what lets a body walk through a doorway: the opening is closed by a portal polygon in the
    /// collision mesh (for the room/visibility system), and the original's collision skips it. Colliding
    /// with it instead walls off the door — the bug this exists to prevent. An out-of-range index is
    /// treated as solid, the safe default.
    /// </remarks>
    public bool IsPortal(int material) =>
        (uint)material < (uint)_materialIsPortal.Length && _materialIsPortal[material];

    /// <summary>Whether the sphere count is the binary tree's over the faces.</summary>
    /// <remarks><c>2 × faces − 1</c>, which holds for every shipped mesh.</remarks>
    public bool SphereCountIsBinaryTree => Faces.Count > 0 && Spheres.Count == (2 * Faces.Count) - 1;

    /// <summary>Reads collision mesh level zero from a model, if it carries one.</summary>
    /// <param name="container">The model's chunks.</param>
    /// <param name="mesh">Receives the mesh.</param>
    /// <returns><see langword="false"/> when the model has no collision geometry, which is not an error.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="container"/> is <see langword="null"/>.</exception>
    /// <exception cref="PackFormatException">A chunk's length disagrees with the header's count.</exception>
    public static bool TryRead(IlffContainer container, out MefCollisionMesh? mesh)
        => TryRead(container, meshIndex: 0, out mesh);

    /// <summary>Reads one collision mesh level from a model, if it carries one.</summary>
    /// <param name="container">The model's chunks.</param>
    /// <param name="meshIndex">The retail <c>iMesh</c> collision level: zero or one.</param>
    /// <param name="mesh">Receives the mesh.</param>
    /// <returns><see langword="false"/> when that level has no collision geometry.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="meshIndex"/> is not a declared mesh level.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="container"/> is <see langword="null"/>.</exception>
    /// <exception cref="PackFormatException">A chunk's length disagrees with the header's count.</exception>
    public static bool TryRead(IlffContainer container, int meshIndex, out MefCollisionMesh? mesh)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentOutOfRangeException.ThrowIfNegative(meshIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(meshIndex, MeshLevels);
        mesh = null;

        if (!container.TryFindChunk(MefTags.CollisionMesh, out IlffChunk header) || header.Payload.Length == 0)
        {
            return false;
        }

        ReadOnlySpan<byte> head = header.Payload.Span;
        if (head.Length < HeaderSize)
        {
            throw new PackFormatException(
                $"The '{MefTags.CollisionMesh}' chunk is {head.Length} bytes, too short for its {HeaderSize}-byte mesh-level headers.");
        }

        ReadOnlySpan<byte> meshHeader = head.Slice(meshIndex * MeshHeaderSize, MeshHeaderSize);
        int faceCount = BinaryPrimitives.ReadInt32LittleEndian(meshHeader);
        int vertexCount = BinaryPrimitives.ReadInt32LittleEndian(meshHeader[4..]);
        int materialCount = BinaryPrimitives.ReadInt32LittleEndian(meshHeader[8..]);
        int sphereCount = BinaryPrimitives.ReadInt32LittleEndian(meshHeader[12..]);

        if (faceCount == 0 && vertexCount == 0)
        {
            return false;
        }

        ReadOnlySpan<byte> faceBytes = Payload(container, MefTags.CollisionFaces, meshIndex, faceCount, FaceStride);
        ReadOnlySpan<byte> vertexBytes = Payload(container, MefTags.CollisionVertices, meshIndex, vertexCount, VertexStride);
        ReadOnlySpan<byte> sphereBytes = Payload(container, MefTags.CollisionSpheres, meshIndex, sphereCount, SphereStride);
        ReadOnlySpan<byte> materialBytes = Payload(container, MefTags.CollisionMaterials, meshIndex, materialCount, MaterialStride);

        // The material rows' one field the collision needs: iPortal (u16 at offset 4). A doorway or
        // window opening is closed by a portal polygon whose material carries a portal index here; the
        // original walks through it, so a portal material is non-solid. 0xFFFF means "not a portal".
        bool[] materialIsPortal = new bool[materialCount];
        // The TAMC row's u16 at offset 8 is the global game-material row this face is made of (0 = Air,
        // 14 = Glass, 18 = Fence, …); 0xFFFF names none. This is the face->material link 
        // recorded as "still missing"; it is what lets the sight test tell glass from a solid wall.
        int[] materialGlobal = new int[materialCount];
        for (int i = 0; i < materialCount; i++)
        {
            int portal = BinaryPrimitives.ReadUInt16LittleEndian(materialBytes.Slice((i * MaterialStride) + 4, 2));
            materialIsPortal[i] = portal != NotAPortal;
            materialGlobal[i] = BinaryPrimitives.ReadUInt16LittleEndian(materialBytes.Slice((i * MaterialStride) + 8, 2));
        }

        Vector3f[] vertices = new Vector3f[vertexCount];
        int[] vertexBones = new int[vertexCount];
        for (int i = 0; i < vertexCount; i++)
        {
            ReadOnlySpan<byte> row = vertexBytes.Slice(i * VertexStride, VertexStride);
            vertices[i] = new Vector3f(
                BinaryPrimitives.ReadSingleLittleEndian(row),
                BinaryPrimitives.ReadSingleLittleEndian(row[4..]),
                BinaryPrimitives.ReadSingleLittleEndian(row[8..]));

            // The trailing word: the rig bone a skinned vertex follows (zero on static geometry).
            vertexBones[i] = BinaryPrimitives.ReadInt32LittleEndian(row[12..]);
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

        mesh = new MefCollisionMesh(faces, vertices, vertexBones, spheres, materialIsPortal, materialGlobal);
        return true;
    }

    private static ReadOnlySpan<byte> Payload(IlffContainer container, FourCc tag, int meshIndex, int count, int stride)
    {
        if (count == 0)
        {
            return default;
        }

        int currentIndex = 0;
        foreach (IlffChunk candidate in container.Chunks)
        {
            if (candidate.Id != tag)
            {
                continue;
            }

            if (currentIndex++ == meshIndex)
            {
                return ValidatePayload(candidate, tag, meshIndex, count, stride);
            }
        }

        throw new PackFormatException(
            $"The collision mesh level {meshIndex} declares {count} '{tag}' records but that level's chunk is missing.");
    }

    private static ReadOnlySpan<byte> ValidatePayload(IlffChunk chunk, FourCc tag, int meshIndex, int count, int stride)
    {
        ReadOnlySpan<byte> span = chunk.Payload.Span;
        int needed = count * stride;
        if (span.Length != needed)
        {
            throw new PackFormatException(
                $"The level {meshIndex} '{tag}' chunk is {span.Length} bytes but {count} records at a {stride}-byte stride need {needed}.");
        }

        return span;
    }
}
