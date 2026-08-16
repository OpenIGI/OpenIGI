using System.Buffers.Binary;
using System.Collections.ObjectModel;
using OpenIGI.Core;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ilff;

namespace OpenIGI.Formats.World;

/// <summary>
/// A static model loaded from a <c>.mef</c> file: an ILFF container of form type <c>OCEM</c>.
/// </summary>
/// <remarks>
/// <para>
/// The original's <c>Model_Load</c> (<c>0x4CF390</c>) only walks the chunk list and back-patches pointers
/// into the mesh header; the renderable geometry is parsed by the backend hook
/// <c>dword_A94E84[Backend_Id()]</c> (<c>0x49FB10</c> for the D3D backend). Both halves are done here,
/// because OpenIGI's renderer is not the original's.
/// </para>
/// <para>
/// Collision, attachment and portal chunks are exposed as raw payloads: their record layouts are still
/// open and are not needed to draw a level. See <c>docs/formats/mef.md</c>.
/// </para>
/// </remarks>
public sealed class MefModel
{
    /// <summary>The mesh header size in bytes.</summary>
    public const int MeshHeaderSize = 156;

    /// <summary>The only mesh version the original accepts.</summary>
    public const float SupportedVersion = 0.1f;

    private readonly MefVertex[] _vertices;
    private readonly MefSkinInfluence[] _skinInfluences;

    private MefModel(
        IlffContainer container,
        MefMeshFormat format,
        int triangleCount,
        MefVertex[] vertices,
        IReadOnlyList<MefDrawRecord> drawRecords,
        IReadOnlyList<MefLightmapPage> lightmapPages,
        IReadOnlyList<MefAttachment> attachments,
        int baseVertexCount,
        int extraInfluenceCount,
        MefSkinInfluence[] skinInfluences,
        float nativeMarkerBound)
    {
        Container = container;
        Format = format;
        TriangleCount = triangleCount;
        _vertices = vertices;
        DrawRecords = drawRecords;
        LightmapPages = lightmapPages;
        Attachments = attachments;
        BaseVertexCount = baseVertexCount;
        ExtraInfluenceCount = extraInfluenceCount;
        _skinInfluences = skinInfluences;
        NativeMarkerBound = nativeMarkerBound;

        int highestBone = -1;
        foreach (MefSkinInfluence influence in skinInfluences)
        {
            highestBone = Math.Max(highestBone, influence.BoneIndex);
        }

        HighestSkinBone = highestBone;
    }

    /// <summary>Gets the number of vertices the skinner places directly by their one bone.</summary>
    /// <remarks>
    /// Equal to the vertex count for every format but <see cref="MefMeshFormat.Skinned"/>, where the
    /// array continues with <see cref="ExtraInfluenceCount"/> extra influences.
    /// </remarks>
    public int BaseVertexCount { get; }

    /// <summary>Gets the number of extra weighted influences that follow the base vertices.</summary>
    public int ExtraInfluenceCount { get; }

    /// <summary>Gets the highest bone index used by this model's skin bindings.</summary>
    public int HighestSkinBone { get; }

    /// <summary>Gets the skin binding of each entry in the vertex array, or empty when not skinned.</summary>
    /// <remarks>
    /// Indexed exactly like the vertex array: the first <see cref="BaseVertexCount"/> entries belong to
    /// base vertices, the rest to extra influences. See <see cref="MefSkinInfluence"/>.
    /// </remarks>
    public IReadOnlyList<MefSkinInfluence> SkinInfluences => _skinInfluences;

    /// <summary>Gets the underlying container.</summary>
    public IlffContainer Container { get; }

    /// <summary>Gets the mesh format, which decides the vertex stride and the draw-record layout.</summary>
    public MefMeshFormat Format { get; }

    /// <summary>Gets the triangle count the mesh header declares, which equals the sum over the draw records.</summary>
    public int TriangleCount { get; }

    /// <summary>
    /// Gets the authored model marker bound from <c>MESH+0x78</c>, in raw IGI engine units.
    /// </summary>
    /// <remarks>
    /// Retail <c>Human_SetModel</c> copies this field into <c>Human+0xA0</c>; the binocular detector
    /// uses that value as the half-extent of its projected target rectangle. It is deliberately kept
    /// separate from the derived vertex radius because the two values are not interchangeable.
    /// </remarks>
    public float NativeMarkerBound { get; }

    /// <summary>Gets the vertices, widened to the 40-byte layout.</summary>
    public ReadOnlySpan<MefVertex> Vertices => _vertices;

    /// <summary>Gets the draw records, in file order.</summary>
    public IReadOnlyList<MefDrawRecord> DrawRecords { get; }

    /// <summary>Gets the baked-lightmap pages, empty on models that carry none.</summary>
    /// <remarks>
    /// Nearly every lightmapped model has exactly one page: of the 5628 in the retail corpus, 5520 have
    /// one, 96 have none, and only twelve have more.
    /// </remarks>
    public IReadOnlyList<MefLightmapPage> LightmapPages { get; }

    /// <summary>Gets the attachment points, in file order; see <see cref="MefAttachment"/>.</summary>
    public IReadOnlyList<MefAttachment> Attachments { get; }

    /// <summary>Gets a value indicating whether the model has any geometry.</summary>
    public bool IsEmpty => _vertices.Length == 0;

    /// <summary>Gets a value indicating whether the model encloses space — a room rather than a prop.</summary>
    /// <remarks>
    /// Every model carries a <c>TROP</c> chunk but only 41 of level 1's 595 fill it, and those are exactly
    /// the buildings with interiors. The original spawns such a model as a <c>Sector</c> rather than a
    /// plain object (<c>0x4DFA50</c>), which puts it on a second child list that is walked after the
    /// others — and that ordering is what the baked lightmap numbering follows.
    /// </remarks>
    public bool IsSector =>
        Container.TryFindChunk(MefTags.Portals, out IlffChunk portals) && portals.Payload.Length > 0;

    /// <summary>Parses a model.</summary>
    /// <param name="data">The <c>.mef</c> bytes, starting at the ILFF header.</param>
    /// <returns>The parsed model.</returns>
    /// <exception cref="PackFormatException">The model is malformed or uses an unsupported mesh format.</exception>
    /// <exception cref="IlffFormatException">The container itself is malformed.</exception>
    public static MefModel Read(ReadOnlyMemory<byte> data) => FromContainer(IlffContainer.Read(data));

    /// <summary>Parses a model from an already-parsed container.</summary>
    /// <param name="container">The container, whose form type must be <c>OCEM</c>.</param>
    /// <returns>The parsed model.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="container"/> is <see langword="null"/>.</exception>
    /// <exception cref="PackFormatException">The model is malformed or uses an unsupported mesh format.</exception>
    public static MefModel FromContainer(IlffContainer container)
    {
        ArgumentNullException.ThrowIfNull(container);

        if (container.FormType != IlffTags.Ocem)
        {
            throw new PackFormatException($"Expected form type 'OCEM', found '{container.FormType}'.");
        }

        ReadOnlySpan<byte> mesh = RequireChunk(container, MefTags.Mesh).Span;
        if (mesh.Length < MeshHeaderSize)
        {
            throw new PackFormatException($"The mesh header is {mesh.Length} bytes, expected {MeshHeaderSize}.");
        }

        float version = BinaryPrimitives.ReadSingleLittleEndian(mesh);
        if (version != SupportedVersion)
        {
            throw new PackFormatException(
                $"Mesh version is {version:0.00}, this code supports {SupportedVersion:0.00}.");
        }

        uint rawFormat = BinaryPrimitives.ReadUInt32LittleEndian(mesh[32..]);
        if (rawFormat is not (0 or 1 or 3))
        {
            throw new PackFormatException($"Mesh format {rawFormat} is not supported.");
        }

        MefMeshFormat format = (MefMeshFormat)rawFormat;
        int triangleCount = ReadInt32(mesh[96..], "the mesh triangle count");
        int headerVertexCount = ReadInt32(mesh[100..], "the mesh vertex count");
        float nativeMarkerBound = BinaryPrimitives.ReadSingleLittleEndian(mesh[0x78..]);

        ReadOnlySpan<byte> render = RequireChunk(container, MefTags.RenderHeader).Span;
        (int vertexCount, int subMeshCount, int lightmapCount) = ReadRenderHeader(render, format);

        if (vertexCount != headerVertexCount)
        {
            throw new PackFormatException(
                $"The mesh header declares {headerVertexCount} vertices but the render header declares {vertexCount}.");
        }

        ReadOnlySpan<byte> vertexBytes = RequireChunk(container, MefTags.Vertices).Span;
        MefVertex[] vertices = ReadVertices(vertexBytes, format, vertexCount);

        // A skinned model splits its vertex array in two: the base vertices, then the extra weighted
        // influences that accumulate into them. The render header carries both counts, and they sum to
        // the array length in all 537 retail skinned models.
        int baseVertexCount = vertexCount;
        int extraInfluenceCount = 0;
        MefSkinInfluence[] influences = [];
        if (format == MefMeshFormat.Skinned)
        {
            extraInfluenceCount = ReadInt32(render[12..], "the render extra-influence count");
            baseVertexCount = ReadInt32(render[16..], "the render base vertex count");
            if (extraInfluenceCount < 0 || baseVertexCount < 0
                || baseVertexCount + extraInfluenceCount != vertexCount)
            {
                throw new PackFormatException(
                    $"A skinned model declares {baseVertexCount} base vertices and {extraInfluenceCount} "
                        + $"extra influences, which do not sum to its {vertexCount} vertices.");
            }

            influences = ReadSkinInfluences(vertexBytes, vertexCount);
        }
        ReadOnlyCollection<MefLightmapPage> pages = ReadLightmapPages(container, lightmapCount);
        IReadOnlyList<MefDrawRecord> records = ReadDrawRecords(
            RequireChunk(container, MefTags.DrawRecords),
            format,
            subMeshCount,
            vertexCount,
            triangleCount,
            pages.Count);

        return new MefModel(
            container,
            format,
            triangleCount,
            vertices,
            records,
            pages,
            ReadAttachments(container),
            baseVertexCount,
            extraInfluenceCount,
            influences,
            nativeMarkerBound);
    }

    /// <summary>Gets the payload of a chunk this type does not decode, such as collision or portal data.</summary>
    /// <param name="tag">The chunk tag; see <see cref="MefTags"/>.</param>
    /// <param name="payload">Receives the payload when the chunk is present.</param>
    /// <returns><see langword="true"/> when the model contains the chunk.</returns>
    public bool TryGetRawChunk(FourCc tag, out ReadOnlyMemory<byte> payload)
    {
        if (Container.TryFindChunk(tag, out IlffChunk chunk))
        {
            payload = chunk.Payload;
            return true;
        }

        payload = default;
        return false;
    }

    /// <summary>Gets how many magic vertices the model carries in its <c>XTVM</c>/<c>MVTX</c> chunk.</summary>
    /// <remarks>Empty in most models (377 of 415 level-1 ones); non-zero on ladders, doors and mounts.</remarks>
    public int MagicVertexCount =>
        TryGetRawChunk(MefTags.LegacyVertices, out ReadOnlyMemory<byte> payload) ? payload.Length / MagicVertexStride : 0;

    /// <summary>Gets a model-space magic vertex — a point in the <c>XTVM</c>/<c>MVTX</c> chunk (<c>MESH+136</c>).</summary>
    /// <param name="index">The magic-vertex index; a ladder's foot is 2 and its head is 3.</param>
    /// <param name="vertex">Receives the point, in model units.</param>
    /// <returns><see langword="false"/> when the model has no magic vertex at that index.</returns>
    /// <remarks>
    /// The original indexes these by number through <c>GetMagicVertex</c> (<c>sub_4D0A50</c>): each is a
    /// 16-byte record of three floats and a pad, in the model's own units. The caller scales and rotates
    /// them by the object and adds its position.
    /// </remarks>
    public bool TryGetMagicVertex(int index, out Vector3f vertex)
    {
        vertex = default;
        if (index < 0 || !TryGetRawChunk(MefTags.LegacyVertices, out ReadOnlyMemory<byte> payload))
        {
            return false;
        }

        ReadOnlySpan<byte> span = payload.Span;
        int offset = index * MagicVertexStride;
        if (offset + 12 > span.Length)
        {
            return false;
        }

        vertex = new Vector3f(
            BinaryPrimitives.ReadSingleLittleEndian(span[offset..]),
            BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 4)..]),
            BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 8)..]));
        return true;
    }

    /// <inheritdoc/>
    public override string ToString() =>
        $"MEF {Format} model: {_vertices.Length} vertices, {TriangleCount} triangles, {DrawRecords.Count} records";

    /// <summary>The size in bytes of one magic-vertex record: three floats and a four-byte pad.</summary>
    private const int MagicVertexStride = 16;

    private static ReadOnlyMemory<byte> RequireChunk(IlffContainer container, FourCc tag) =>
        container.TryFindChunk(tag, out IlffChunk chunk)
            ? chunk.Payload
            : throw new PackFormatException($"The model has no '{tag}' chunk.");

    private static (int VertexCount, int SubMeshCount, int LightmapCount) ReadRenderHeader(
        ReadOnlySpan<byte> render,
        MefMeshFormat format)
    {
        // Field positions shift with the format; the trailing 24 bytes are the original's runtime binding
        // block and are zero on disk. Sizes are 48 bytes for Diffuse and 56 for the other two. Only the
        // lightmapped layout has a page count, and it sits ahead of the counts the other two share.
        (int subMeshOffset, int vertexOffset, int expectedSize) = format switch
        {
            MefMeshFormat.Diffuse => (8, 12, 48),
            MefMeshFormat.Skinned => (8, 20, 56),
            _ => (12, 16, 56),
        };

        if (render.Length < expectedSize)
        {
            throw new PackFormatException(
                $"The '{MefTags.RenderHeader}' chunk is {render.Length} bytes, expected {expectedSize} for mesh format {(int)format}.");
        }

        return (ReadInt32(render[vertexOffset..], "the render vertex count"),
            ReadInt32(render[subMeshOffset..], "the render sub-mesh count"),
            format == MefMeshFormat.Lightmapped ? ReadInt32(render[4..], "the render lightmap count") : 0);
    }

    private static ReadOnlyCollection<MefLightmapPage> ReadLightmapPages(IlffContainer container, int declared)
    {
        if (!container.TryFindChunk(MefTags.LightmapPages, out IlffChunk chunk))
        {
            return declared == 0
                ? ReadOnlyCollection<MefLightmapPage>.Empty
                : throw new PackFormatException(
                    $"The render header declares {declared} lightmap pages but there is no '{MefTags.LightmapPages}' chunk.");
        }

        ReadOnlySpan<byte> span = chunk.Payload.Span;
        if (span.Length != declared * MefLightmapPage.Stride)
        {
            throw new PackFormatException(
                $"The '{MefTags.LightmapPages}' chunk is {span.Length} bytes but {declared} pages need "
                + $"{declared * MefLightmapPage.Stride}.");
        }

        MefLightmapPage[] pages = new MefLightmapPage[declared];
        for (int i = 0; i < declared; i++)
        {
            ReadOnlySpan<byte> entry = span.Slice(i * MefLightmapPage.Stride, MefLightmapPage.Stride);
            pages[i] = new MefLightmapPage(
                BinaryPrimitives.ReadUInt16LittleEndian(entry),
                BinaryPrimitives.ReadUInt16LittleEndian(entry[2..]));
        }

        return new ReadOnlyCollection<MefLightmapPage>(pages);
    }

    private static ReadOnlyCollection<MefAttachment> ReadAttachments(IlffContainer container)
    {
        if (!container.TryFindChunk(MefTags.Attachments, out IlffChunk chunk) || chunk.Payload.Length == 0)
        {
            return ReadOnlyCollection<MefAttachment>.Empty;
        }

        ReadOnlySpan<byte> span = chunk.Payload.Span;
        if (span.Length % MefAttachment.Stride != 0)
        {
            throw new PackFormatException(
                $"The '{MefTags.Attachments}' chunk is {span.Length} bytes, not a whole number of "
                + $"{MefAttachment.Stride}-byte records.");
        }

        MefAttachment[] attachments = new MefAttachment[span.Length / MefAttachment.Stride];
        Span<float> rotation = stackalloc float[Matrix3f.ComponentCount];
        for (int i = 0; i < attachments.Length; i++)
        {
            ReadOnlySpan<byte> record = span.Slice(i * MefAttachment.Stride, MefAttachment.Stride);
            for (int j = 0; j < rotation.Length; j++)
            {
                rotation[j] = BinaryPrimitives.ReadSingleLittleEndian(record[(28 + (j * 4))..]);
            }

            attachments[i] = new MefAttachment(
                ReadName(record[..MefAttachment.NameSize]),
                new Vector3f(
                    BinaryPrimitives.ReadSingleLittleEndian(record[16..]),
                    BinaryPrimitives.ReadSingleLittleEndian(record[20..]),
                    BinaryPrimitives.ReadSingleLittleEndian(record[24..])),
                Matrix3f.FromRowMajor(rotation),
                BinaryPrimitives.ReadInt32LittleEndian(record[64..]));
        }

        return new ReadOnlyCollection<MefAttachment>(attachments);
    }

    private static string ReadName(ReadOnlySpan<byte> field)
    {
        int end = field.IndexOf((byte)0);
        return System.Text.Encoding.ASCII.GetString(end < 0 ? field : field[..end]);
    }

    private static MefVertex[] ReadVertices(ReadOnlySpan<byte> data, MefMeshFormat format, int count)
    {
        if (count == 0)
        {
            return [];
        }

        int stride = MefVertex.StrideOf(format);
        long needed = (long)stride * count;
        if (needed != data.Length)
        {
            throw new PackFormatException(
                $"The '{MefTags.Vertices}' chunk is {data.Length} bytes but {count} vertices at a {stride}-byte stride need {needed}.");
        }

        bool hasLightmapUv = format != MefMeshFormat.Diffuse;
        MefVertex[] vertices = new MefVertex[count];
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> v = data.Slice(i * stride, stride);
            vertices[i] = new MefVertex(
                new Vector3f(
                    BinaryPrimitives.ReadSingleLittleEndian(v),
                    BinaryPrimitives.ReadSingleLittleEndian(v[4..]),
                    BinaryPrimitives.ReadSingleLittleEndian(v[8..])),
                new Vector3f(
                    BinaryPrimitives.ReadSingleLittleEndian(v[12..]),
                    BinaryPrimitives.ReadSingleLittleEndian(v[16..]),
                    BinaryPrimitives.ReadSingleLittleEndian(v[20..])),
                BinaryPrimitives.ReadSingleLittleEndian(v[24..]),
                BinaryPrimitives.ReadSingleLittleEndian(v[28..]),
                hasLightmapUv ? BinaryPrimitives.ReadSingleLittleEndian(v[32..]) : 0f,
                hasLightmapUv ? BinaryPrimitives.ReadSingleLittleEndian(v[36..]) : 0f);
        }

        return vertices;
    }

    private static MefSkinInfluence[] ReadSkinInfluences(ReadOnlySpan<byte> data, int count)
    {
        MefSkinInfluence[] influences = new MefSkinInfluence[count];
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> binding =
                data.Slice((i * MefVertex.LightmappedStride) + MefSkinInfluence.OffsetInVertex,
                    MefSkinInfluence.SizeInBytes);
            influences[i] = new MefSkinInfluence(
                BinaryPrimitives.ReadSingleLittleEndian(binding),
                BinaryPrimitives.ReadUInt16LittleEndian(binding[4..]),
                BinaryPrimitives.ReadUInt16LittleEndian(binding[6..]));
        }

        return influences;
    }

    private static ReadOnlyCollection<MefDrawRecord> ReadDrawRecords(
        ReadOnlyMemory<byte> chunk,
        MefMeshFormat format,
        int subMeshCount,
        int vertexCount,
        int triangleCount,
        int lightmapPageCount)
    {
        ReadOnlySpan<byte> span = chunk.Span;
        int headerSize = MefDrawRecord.HeaderSizeOf(format);

        // A model with no vertices carries a stub chunk too short for even one record and draws nothing.
        if (subMeshCount == 0 || (vertexCount == 0 && span.Length < headerSize))
        {
            return ReadOnlyCollection<MefDrawRecord>.Empty;
        }

        // The 32-byte lightmapped header carries a page and a span the 28-byte compact one lacks; but
        // the *indices* are relative to the record's base vertex in every format. That was measured,
        // not assumed: over mission 1's nine multi-record Diffuse models every record's indices run
        // exactly [0, next record's base - own base), and none is consistent with whole-array
        // indexing — the truck (622_01_1, thirty records) is the model that showed it, drawing
        // scrambled under the earlier absolute reading. Single-record models draw the same either way.
        bool lightmappedHeader = format == MefMeshFormat.Lightmapped;

        MefDrawRecord[] records = new MefDrawRecord[subMeshCount];
        int offset = 0;
        int triangles = 0;
        for (int i = 0; i < subMeshCount; i++)
        {
            if (offset + headerSize > span.Length)
            {
                throw new PackFormatException(
                    $"The '{MefTags.DrawRecords}' chunk ran out after {i} of {subMeshCount} records ({span.Length} bytes).");
            }

            ReadOnlySpan<byte> header = span[offset..];
            Vector3f centre = new(
                BinaryPrimitives.ReadSingleLittleEndian(header),
                BinaryPrimitives.ReadSingleLittleEndian(header[4..]),
                BinaryPrimitives.ReadSingleLittleEndian(header[8..]));
            int indexCount = BinaryPrimitives.ReadUInt16LittleEndian(header[12..]);
            int materialSlot = BinaryPrimitives.ReadUInt16LittleEndian(header[16..]);
            int lightmapPage = lightmappedHeader ? BinaryPrimitives.ReadUInt16LittleEndian(header[18..]) : 0;
            int baseVertex = BinaryPrimitives.ReadUInt16LittleEndian(header[(lightmappedHeader ? 20 : 18)..]);
            int span0 = lightmappedHeader ? BinaryPrimitives.ReadUInt16LittleEndian(header[22..]) : 0;

            if (lightmapPage >= lightmapPageCount && lightmapPage != 0)
            {
                throw new PackFormatException(
                    $"Draw record {i} samples lightmap page {lightmapPage} but the model has {lightmapPageCount}.");
            }

            if (indexCount % 3 != 0)
            {
                throw new PackFormatException(
                    $"Draw record {i} declares {indexCount} indices, which is not a whole number of triangles.");
            }

            int indexBytes = indexCount * 2;
            if (offset + headerSize + indexBytes > span.Length)
            {
                throw new PackFormatException(
                    $"Draw record {i} declares {indexCount} indices, which do not fit the '{MefTags.DrawRecords}' chunk.");
            }

            ushort[] indices = new ushort[indexCount];
            ReadOnlySpan<byte> raw = span.Slice(offset + headerSize, indexBytes);
            // The compact header states no span, so the bound is the rest of the vertex array.
            int limit = span0 > 0 ? span0 : vertexCount - baseVertex;
            for (int j = 0; j < indexCount; j++)
            {
                ushort index = BinaryPrimitives.ReadUInt16LittleEndian(raw[(j * 2)..]);
                if (index >= limit)
                {
                    throw new PackFormatException(
                        $"Draw record {i} index {j} is {index}, past its vertex span of {limit}.");
                }

                indices[j] = index;
            }

            records[i] = new MefDrawRecord(centre, materialSlot, lightmapPage, baseVertex, span0, true, indices);
            triangles += indexCount / 3;
            offset += headerSize + indexBytes;
        }

        // Both invariants hold for all 6795 retail models; a mismatch means the walk desynced.
        if (offset != span.Length)
        {
            throw new PackFormatException(
                $"The '{MefTags.DrawRecords}' walk consumed {offset} of {span.Length} bytes after {subMeshCount} records.");
        }

        if (triangles != triangleCount)
        {
            throw new PackFormatException(
                $"The draw records total {triangles} triangles but the mesh header declares {triangleCount}.");
        }

        return new ReadOnlyCollection<MefDrawRecord>(records);
    }

    private static int ReadInt32(ReadOnlySpan<byte> source, string what)
    {
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(source);
        if (value > int.MaxValue)
        {
            throw new PackFormatException($"The value of {what} is {value}, too large to address.");
        }

        return (int)value;
    }
}
