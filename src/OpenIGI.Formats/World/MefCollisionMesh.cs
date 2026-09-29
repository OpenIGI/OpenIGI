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
/// checked against the retail packs. The complete <c>XTVC</c> payload, not the HSMC header, is the
/// authoritative vertex row count: a header that undercounts its rows is tolerated. No model in the
/// fourteen retail missions' packs does (a full scan finds every header equal to its rows); the
/// tolerance exists for a reported install whose human model declared fewer rows than it stored.
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

    /// <summary>Bytes per face, in the first game's layout.</summary>
    /// <remarks>
    /// The strides below are Project I.G.I.'s and are what a reader given no profile uses, which keeps
    /// every existing caller reading exactly what it read before. I.G.I. 2 widens three of the four and
    /// moves a field inside the material row; see <see cref="CollisionLayout"/>.
    /// </remarks>
    public const int FaceStride = 8;

    /// <summary>Bytes per vertex.</summary>
    public const int VertexStride = 16;

    /// <summary>Bytes per material.</summary>
    public const int MaterialStride = 12;

    /// <summary>Bytes per sphere.</summary>
    public const int SphereStride = 24;

    /// <summary>Where a material row keeps its portal index, in the first game's layout.</summary>
    public const int MaterialPortalOffset = 4;

    /// <summary>Where a material row keeps the global game-material row, in the first game's layout.</summary>
    public const int MaterialGlobalOffset = 8;

    /// <summary>The <c>iPortal</c> value that means "not a portal" — <c>0xFFFF</c>, i.e. −1.</summary>
    /// <remarks>
    /// A material row's <c>iPortal</c> (the <c>u16</c> at offset 4, IGI2 <c>MultiMesh_Material_t.iPortal</c>)
    /// indexes a portal when it is anything other than this. A portal face is a doorway or window opening:
    /// non-solid, so a body walks through it. See <see cref="IsPortal"/>.
    /// </remarks>
    public const int NotAPortal = 0xFFFF;

    private readonly MefCollisionFace[] _faces;
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
        _faces = faces;
        Vertices = vertices;
        VertexBones = vertexBones;
        Spheres = spheres;
        Materials = materialIsPortal.Length;
        _materialIsPortal = materialIsPortal;
        _materialGlobal = materialGlobal;
    }

    /// <summary>A copy of this mesh level with its vertices and sphere tree moved, its faces and materials unchanged.</summary>
    /// <param name="vertices">The new vertices, one per original vertex.</param>
    /// <param name="spheres">The new sphere tree, one node per original node.</param>
    /// <returns>The moved mesh.</returns>
    /// <remarks>For geometry the original deforms at load, such as a spline segment bent along its waypoints.</remarks>
    /// <exception cref="ArgumentException">A count does not match the original's.</exception>
    public MefCollisionMesh WithVertices(IReadOnlyList<Vector3f> vertices, IReadOnlyList<MefCollisionSphere> spheres)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(spheres);
        if (vertices.Count != Vertices.Count || spheres.Count != Spheres.Count)
        {
            throw new ArgumentException("A moved collision mesh keeps its vertex and sphere counts.");
        }

        return new MefCollisionMesh(
            _faces, [.. vertices], [.. VertexBones], [.. spheres], _materialIsPortal, _materialGlobal);
    }

    /// <summary>Resolves a face's per-mesh material to its global game-material row.</summary>
    /// <param name="material">The face's material index — <see cref="MefCollisionFace.Material"/>.</param>
    /// <returns>
    /// The row of the global game-material table this face is made of (0 = Air, 14 = Glass, 18 = Fence,
    /// …), read from the <c>TAMC</c> row's <c>u16</c> at offset 8.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>A row of <c>0xFFFF</c> means <see cref="DefaultGlobalMaterial"/>, not the per-mesh index.</b>
    /// <c>0x4C1840</c> writes the trace's material as
    /// <c>scene[3388 + 4·u16]</c>, and takes the branch <c>scene[3392]</c> — which is that same array at
    /// index <b>1</b> — when the u16 is <c>0xFFFF</c>. Returning the per-mesh index there would name an
    /// arbitrary row instead.
    /// </para>
    /// <para>
    /// A mesh with no global table at all is a hand-built one, which has no retail answer; it keeps its
    /// own index so tests can name materials directly.
    /// </para>
    /// </remarks>
    public int GlobalMaterial(int material)
    {
        if (_materialGlobal is not { } table || (uint)material >= (uint)table.Length)
        {
            return material;
        }

        return table[material] == NotAPortal ? DefaultGlobalMaterial : table[material];
    }

    /// <summary>The row a face falls back to when it names no global material — <c>Ground</c>.</summary>
    /// <remarks><c>scene+3392</c> in <c>0x4C1840</c>, which is index 1 of the per-level material array.</remarks>
    public const int DefaultGlobalMaterial = 1;

    /// <summary>Gets the collision triangles.</summary>
    public ReadOnlySpan<MefCollisionFace> Faces => _faces;

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
    public bool SphereCountIsBinaryTree => _faces.Length > 0 && Spheres.Count == (2 * _faces.Length) - 1;

    /// <summary>Reads collision mesh level zero from a model, if it carries one.</summary>
    /// <param name="container">The model's chunks.</param>
    /// <param name="mesh">Receives the mesh.</param>
    /// <param name="layout">How this title lays the rows out, or null for the first game's.</param>
    /// <returns><see langword="false"/> when the model has no collision geometry, which is not an error.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="container"/> is <see langword="null"/>.</exception>
    /// <exception cref="PackFormatException">A chunk's length disagrees with the header's count.</exception>
    public static bool TryRead(IlffContainer container, out MefCollisionMesh? mesh, CollisionLayout? layout = null)
        => TryRead(container, meshIndex: 0, out mesh, layout);

    /// <summary>Reads one collision mesh level from a model, if it carries one.</summary>
    /// <param name="container">The model's chunks.</param>
    /// <param name="meshIndex">The retail <c>iMesh</c> collision level: zero or one.</param>
    /// <param name="mesh">Receives the mesh.</param>
    /// <param name="layout">How this title lays the rows out, or null for the first game's.</param>
    /// <returns><see langword="false"/> when that level has no collision geometry.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="meshIndex"/> is not a declared mesh level.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="container"/> is <see langword="null"/>.</exception>
    /// <exception cref="PackFormatException">A chunk's length disagrees with the header's count.</exception>
    public static bool TryRead(
        IlffContainer container,
        int meshIndex,
        out MefCollisionMesh? mesh,
        CollisionLayout? layout = null)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentOutOfRangeException.ThrowIfNegative(meshIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(meshIndex, MeshLevels);
        mesh = null;

        if (!container.TryFindChunk(MefTags.CollisionMesh, out IlffChunk header) || header.Payload.Length == 0)
        {
            return false;
        }

        // The title's layout, or the first game's when none is given.
        CollisionLayout rows = layout ?? new CollisionLayout(
            FaceStride, VertexStride, MaterialStride, SphereStride, MaterialPortalOffset, MaterialGlobalOffset);

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

        ReadOnlySpan<byte> faceBytes = Payload(container, MefTags.CollisionFaces, meshIndex, faceCount, rows.FaceStride);

        // A collision mesh whose HSMC vertex count is smaller than its XTVC rows keeps every row: faces
        // may reference the rows past the header value. The retail missions' packs never do this (every
        // header matches its rows), but one reported install's human model did. Trust the
        // self-delimiting payload for vertices while retaining exact count checks for the face,
        // material, and sphere tables below.
        ReadOnlySpan<byte> vertexBytes = Payload(
            container, MefTags.CollisionVertices, meshIndex, vertexCount, rows.VertexStride,
            allowAdditionalRecords: true);
        if (vertexBytes.Length % rows.VertexStride != 0)
        {
            throw new PackFormatException(
                $"The level {meshIndex} '{MefTags.CollisionVertices}' chunk is {vertexBytes.Length} bytes, "
                + $"not a whole number of {rows.VertexStride}-byte records.");
        }

        vertexCount = vertexBytes.Length / rows.VertexStride;
        ReadOnlySpan<byte> sphereBytes = Payload(container, MefTags.CollisionSpheres, meshIndex, sphereCount, rows.SphereStride);
        ReadOnlySpan<byte> materialBytes = Payload(container, MefTags.CollisionMaterials, meshIndex, materialCount, rows.MaterialStride);
        int payloadVertexCount = vertexBytes.Length / rows.VertexStride;

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
            int portal = BinaryPrimitives.ReadUInt16LittleEndian(materialBytes.Slice((i * rows.MaterialStride) + rows.MaterialPortalOffset, 2));
            materialIsPortal[i] = portal != NotAPortal;
            materialGlobal[i] = BinaryPrimitives.ReadUInt16LittleEndian(materialBytes.Slice((i * rows.MaterialStride) + rows.MaterialGlobalOffset, 2));
        }

        Vector3f[] vertices = new Vector3f[payloadVertexCount];
        int[] vertexBones = new int[payloadVertexCount];
        for (int i = 0; i < payloadVertexCount; i++)
        {
            ReadOnlySpan<byte> row = vertexBytes.Slice(i * rows.VertexStride, rows.VertexStride);
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
            ReadOnlySpan<byte> row = faceBytes.Slice(i * rows.FaceStride, rows.FaceStride);
            faces[i] = new MefCollisionFace(
                BinaryPrimitives.ReadUInt16LittleEndian(row),
                BinaryPrimitives.ReadUInt16LittleEndian(row[2..]),
                BinaryPrimitives.ReadUInt16LittleEndian(row[4..]),
                BinaryPrimitives.ReadUInt16LittleEndian(row[6..]));
        }

        MefCollisionSphere[] spheres = new MefCollisionSphere[sphereCount];
        for (int i = 0; i < sphereCount; i++)
        {
            ReadOnlySpan<byte> row = sphereBytes.Slice(i * rows.SphereStride, rows.SphereStride);
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

    private static ReadOnlySpan<byte> Payload(
        IlffContainer container,
        FourCc tag,
        int meshIndex,
        int count,
        int stride,
        bool allowAdditionalRecords = false)
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
                return ValidatePayload(candidate, tag, meshIndex, count, stride, allowAdditionalRecords);
            }
        }

        throw new PackFormatException(
            $"The collision mesh level {meshIndex} declares {count} '{tag}' records but that level's chunk is missing.");
    }

    private static ReadOnlySpan<byte> ValidatePayload(
        IlffChunk chunk,
        FourCc tag,
        int meshIndex,
        int count,
        int stride,
        bool allowAdditionalRecords)
    {
        ReadOnlySpan<byte> span = chunk.Payload.Span;
        long needed = (long)count * stride;
        if (allowAdditionalRecords)
        {
            // Keep the full aligned payload when it holds more rows than the header declares.
            // Truncated or partially-recorded chunks remain errors.
            if (span.Length >= needed && span.Length % stride == 0)
            {
                return span;
            }
        }
        else if (span.Length == needed)
        {
            return span;
        }

        if (allowAdditionalRecords)
        {
            throw new PackFormatException(
                $"The level {meshIndex} '{tag}' chunk is {span.Length} bytes; it must contain at least "
                + $"{count} records at a {stride}-byte stride and end on a record boundary.");
        }

        throw new PackFormatException(
            $"The level {meshIndex} '{tag}' chunk is {span.Length} bytes but {count} records at a {stride}-byte stride need {needed}.");
    }
}
