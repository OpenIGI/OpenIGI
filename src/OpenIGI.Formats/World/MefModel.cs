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

    private MefModel(
        IlffContainer container,
        MefMeshFormat format,
        int triangleCount,
        MefVertex[] vertices,
        IReadOnlyList<MefDrawRecord> drawRecords,
        MefLightmapInfo? lightmapInfo)
    {
        Container = container;
        Format = format;
        TriangleCount = triangleCount;
        _vertices = vertices;
        DrawRecords = drawRecords;
        LightmapInfo = lightmapInfo;
    }

    /// <summary>Gets the underlying container.</summary>
    public IlffContainer Container { get; }

    /// <summary>Gets the mesh format, which decides the vertex stride and the draw-record layout.</summary>
    public MefMeshFormat Format { get; }

    /// <summary>Gets the triangle count the mesh header declares, which equals the sum over the draw records.</summary>
    public int TriangleCount { get; }

    /// <summary>Gets the vertices, widened to the 40-byte layout.</summary>
    public ReadOnlySpan<MefVertex> Vertices => _vertices;

    /// <summary>Gets the draw records, in file order.</summary>
    public IReadOnlyList<MefDrawRecord> DrawRecords { get; }

    /// <summary>Gets the object-lightmap descriptor, present only on <see cref="MefMeshFormat.Lightmapped"/> models.</summary>
    public MefLightmapInfo? LightmapInfo { get; }

    /// <summary>Gets a value indicating whether the model has any geometry.</summary>
    public bool IsEmpty => _vertices.Length == 0;

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

        ReadOnlySpan<byte> render = RequireChunk(container, MefTags.RenderHeader).Span;
        (int vertexCount, int subMeshCount) = ReadRenderHeader(render, format);

        if (vertexCount != headerVertexCount)
        {
            throw new PackFormatException(
                $"The mesh header declares {headerVertexCount} vertices but the render header declares {vertexCount}.");
        }

        MefVertex[] vertices = ReadVertices(RequireChunk(container, MefTags.Vertices).Span, format, vertexCount);
        IReadOnlyList<MefDrawRecord> records = ReadDrawRecords(
            RequireChunk(container, MefTags.DrawRecords),
            format,
            subMeshCount,
            vertexCount,
            triangleCount);

        MefLightmapInfo? lightmap = null;
        if (container.TryFindChunk(MefTags.LightmapInfo, out IlffChunk chunk) && chunk.Payload.Length >= 4)
        {
            ReadOnlySpan<byte> span = chunk.Payload.Span;
            lightmap = new MefLightmapInfo(
                BinaryPrimitives.ReadUInt16LittleEndian(span),
                BinaryPrimitives.ReadUInt16LittleEndian(span[2..]));
        }

        return new MefModel(container, format, triangleCount, vertices, records, lightmap);
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

    /// <inheritdoc/>
    public override string ToString() =>
        $"MEF {Format} model: {_vertices.Length} vertices, {TriangleCount} triangles, {DrawRecords.Count} records";

    private static ReadOnlyMemory<byte> RequireChunk(IlffContainer container, FourCc tag) =>
        container.TryFindChunk(tag, out IlffChunk chunk)
            ? chunk.Payload
            : throw new PackFormatException($"The model has no '{tag}' chunk.");

    private static (int VertexCount, int SubMeshCount) ReadRenderHeader(ReadOnlySpan<byte> render, MefMeshFormat format)
    {
        // Field positions shift with the format; the trailing 24 bytes are the original's runtime binding
        // block and are zero on disk. Sizes are 48 bytes for Diffuse and 56 for the other two.
        (int subMeshOffset, int vertexOffset, int expectedSize) = format switch
        {
            MefMeshFormat.Diffuse => (8, 12, 48),
            MefMeshFormat.Unbatched => (8, 20, 56),
            _ => (12, 16, 56),
        };

        if (render.Length < expectedSize)
        {
            throw new PackFormatException(
                $"The '{MefTags.RenderHeader}' chunk is {render.Length} bytes, expected {expectedSize} for mesh format {(int)format}.");
        }

        return (ReadInt32(render[vertexOffset..], "the render vertex count"),
            ReadInt32(render[subMeshOffset..], "the render sub-mesh count"));
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

    private static ReadOnlyCollection<MefDrawRecord> ReadDrawRecords(
        ReadOnlyMemory<byte> chunk,
        MefMeshFormat format,
        int subMeshCount,
        int vertexCount,
        int triangleCount)
    {
        ReadOnlySpan<byte> span = chunk.Span;
        int headerSize = MefDrawRecord.HeaderSizeOf(format);

        // A model with no vertices carries a stub chunk too short for even one record and draws nothing.
        if (subMeshCount == 0 || (vertexCount == 0 && span.Length < headerSize))
        {
            return ReadOnlyCollection<MefDrawRecord>.Empty;
        }

        bool relative = format == MefMeshFormat.Lightmapped;

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
            int baseVertex = BinaryPrimitives.ReadUInt16LittleEndian(header[(relative ? 20 : 18)..]);
            int span0 = relative ? BinaryPrimitives.ReadUInt16LittleEndian(header[22..]) : 0;

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
            int limit = relative ? span0 : vertexCount;
            for (int j = 0; j < indexCount; j++)
            {
                ushort index = BinaryPrimitives.ReadUInt16LittleEndian(raw[(j * 2)..]);
                if (index >= limit)
                {
                    throw new PackFormatException(
                        $"Draw record {i} index {j} is {index}, past its {(relative ? "vertex span" : "vertex count")} of {limit}.");
                }

                indices[j] = index;
            }

            records[i] = new MefDrawRecord(centre, materialSlot, baseVertex, span0, relative, indices);
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
