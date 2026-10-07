using System.Buffers.Binary;
using System.Globalization;
using System.Collections.ObjectModel;
using OpenIGI.Core;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
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
    /// <summary>The size of one bone name in an I.G.I. 2 bone mesh's <c>BNAM</c> chunk.</summary>
    public const int BoneNameSize = 16;

    /// <summary>The mesh header size in bytes.</summary>
    public const int MeshHeaderSize = 156;

    /// <summary>The mesh version retail Project I.G.I. ships.</summary>
    /// <remarks>
    /// I.G.I. 2 ships <c>0.16</c> in the same field, so which versions are acceptable is a property of
    /// the title being run rather than of this reader — see <see cref="AssetFormatProfile"/>. This
    /// remains the default so that reading a model without saying which game it came from behaves as it
    /// always has.
    /// </remarks>
    public const float SupportedVersion = 0.1f;

    private MefPortalSet? _portals;
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
        float nativeMarkerBound,
        Igi2BoneHierarchy? boneHierarchy,
        IReadOnlyList<string> boneNames)
    {
        BoneHierarchy = boneHierarchy;
        BoneNames = boneNames;
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

    /// <summary>Gets an I.G.I. 2 bone mesh's hierarchy, or <see langword="null"/> for any other model.</summary>
    /// <remarks>
    /// The one the engine poses with: <c>BoneDynCubeObj_SetLODVirModel</c> (<c>igi2 0x567BE0</c>) takes
    /// the hierarchy and the bone offsets from the mesh, never from a clip. I.G.I. 1 keeps its skeletons
    /// in the animation sets instead, so its meshes carry none.
    /// </remarks>
    public Igi2BoneHierarchy? BoneHierarchy { get; }

    /// <summary>Gets an I.G.I. 2 bone mesh's bone names, in bone order; empty for any other model.</summary>
    /// <remarks>
    /// How the engine finds a bone by what it is: <c>HumanAnim_Init</c> (<c>igi2 0x58F650</c>) asks the
    /// hierarchy for <c>center</c>, <c>lower body</c>, <c>upper body</c>, <c>shoulders</c> and
    /// <c>head</c>, and maps the limbs to damage parts by name.
    /// </remarks>
    public IReadOnlyList<string> BoneNames { get; }

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
    public ReadOnlySpan<MefSkinInfluence> SkinInfluences => _skinInfluences;

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

    /// <summary>Gets the centre of the render mesh's bounding sphere, <c>Mesh3D_Mesh_t.tRenderMeshSphere</c> at <c>MESH+0x30</c>.</summary>
    /// <remarks>
    /// I.G.I. 2's <c>Forest_UpdateInternalDataHandler</c> (<c>igi2 0x5A2F40</c>) measures a tree model's radius from it:
    /// the furthest collision vertex from this centre.
    /// </remarks>
    public Vector3f RenderSphereCentre { get; private init; }

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

    /// <summary>Gets the model's portals, decoded on first use.</summary>
    /// <exception cref="PackFormatException">The portal chunks are malformed.</exception>
    /// <remarks>
    /// Lazy because most models have none — a minority of the sequel's placed meshes are sectors, and 41
    /// of Project I.G.I.'s 595 — so decoding eagerly would charge every model for a chunk it never uses.
    /// </remarks>
    public MefPortalSet Portals => _portals ??= MefPortalSet.Read(Container);

    /// <summary>Parses a model.</summary>
    /// <param name="data">The <c>.mef</c> bytes, starting at the ILFF header.</param>
    /// <returns>The parsed model.</returns>
    /// <exception cref="PackFormatException">The model is malformed or uses an unsupported mesh format.</exception>
    /// <exception cref="IlffFormatException">The container itself is malformed.</exception>
    public static MefModel Read(ReadOnlyMemory<byte> data) => FromContainer(IlffContainer.Read(data));

    /// <summary>Parses a model, accepting the mesh versions one title ships.</summary>
    /// <param name="data">The file bytes.</param>
    /// <param name="profile">The title's format profile.</param>
    /// <returns>The parsed model.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="profile"/> is <see langword="null"/>.</exception>
    /// <exception cref="PackFormatException">The model is malformed, or is not a version the title ships.</exception>
    public static MefModel Read(ReadOnlyMemory<byte> data, AssetFormatProfile profile) =>
        FromContainer(IlffContainer.Read(data), profile);

    /// <summary>Parses a model from an already-parsed container.</summary>
    /// <param name="container">The container, whose form type must be <c>OCEM</c>.</param>
    /// <returns>The parsed model.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="container"/> is <see langword="null"/>.</exception>
    /// <exception cref="PackFormatException">The model is malformed or uses an unsupported mesh format.</exception>
    public static MefModel FromContainer(IlffContainer container) => FromContainer(container, AssetFormatProfile.Igi1);

    /// <summary>Parses a model from an already-parsed container, accepting one title's mesh versions.</summary>
    /// <param name="container">The container, whose form type must be <c>OCEM</c>.</param>
    /// <param name="profile">The title's format profile.</param>
    /// <returns>The parsed model.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="PackFormatException">The model is malformed, or is not a version the title ships.</exception>
    public static MefModel FromContainer(IlffContainer container, AssetFormatProfile profile)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(profile);

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
        if (!profile.AcceptsMesh(version))
        {
            throw new PackFormatException(
                $"Mesh version is {version:0.00}, which this title does not ship "
                + $"(it ships {string.Join(", ", profile.MeshVersions.Select(static v => v.ToString("0.00", CultureInfo.InvariantCulture)))}).");
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
        Vector3f renderSphereCentre = new(
            BinaryPrimitives.ReadSingleLittleEndian(mesh[0x30..]),
            BinaryPrimitives.ReadSingleLittleEndian(mesh[0x34..]),
            BinaryPrimitives.ReadSingleLittleEndian(mesh[0x38..]));

        ReadOnlySpan<byte> render = RequireChunk(container, MefTags.RenderHeader).Span;
        (int vertexCount, int subMeshCount, int lightmapCount) = ReadRenderHeader(render, format, profile);
        (Igi2BoneHierarchy? boneHierarchy, IReadOnlyList<string> boneNames) = ReadBones(container, mesh);

        ReadOnlySpan<byte> vertexBytes = RequireChunk(container, MefTags.Vertices).Span;
        MefVertex[] vertices = ReadVertices(vertexBytes, format, vertexCount, profile);

        // A skinned model splits its vertex array in two: the base vertices, then the extra weighted
        // influences that accumulate into them. The render header carries both counts, and they sum to
        // the array length in all 537 retail I.G.I. 1 skinned models and all 101 sampled from I.G.I. 2.
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

        // The mesh header repeats a vertex count, and the two games mean different things by it:
        // I.G.I. 1 writes the total, I.G.I. 2 writes the base. They are equal for everything but a
        // skinned model, which is the only case where the extras are non-zero. Checked rather than
        // ignored, because it is a genuine cross-check on the render header having been read correctly
        // -- reading the wrong dword there produces a plausible number, and this is what catches it.
        int expectedHeaderCount = profile.MeshHeaderCountsBaseVertices == true ? baseVertexCount : vertexCount;
        if (profile.MeshHeaderCountsBaseVertices is not null && headerVertexCount != expectedHeaderCount)
        {
            throw new PackFormatException(
                $"The mesh header declares {headerVertexCount} vertices but the render header declares "
                    + $"{expectedHeaderCount}.");
        }

        if (profile.MeshHeaderCountsBaseVertices is null
            && headerVertexCount != vertexCount && headerVertexCount != baseVertexCount)
        {
            throw new PackFormatException(
                $"The mesh header declares {headerVertexCount} vertices, which is neither the render "
                    + $"header's {vertexCount} total nor its {baseVertexCount} base.");
        }
        ReadOnlyCollection<MefLightmapPage> pages = ReadLightmapPages(container, lightmapCount);
        IReadOnlyList<MefDrawRecord> records = profile.DrawRecordStorage == MeshDrawRecordStorage.FaceGroupTable
            ? ReadFaceGroups(container, format, subMeshCount, vertexCount, triangleCount, pages.Count)
            : ReadDrawRecords(
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
            ReadAttachments(container, profile),
            baseVertexCount,
            extraInfluenceCount,
            influences,
            nativeMarkerBound,
            boneHierarchy,
            boneNames)
        {
            RenderSphereCentre = renderSphereCentre,
        };
    }

    private static (Igi2BoneHierarchy? Hierarchy, IReadOnlyList<string> Names) ReadBones(
        IlffContainer container,
        ReadOnlySpan<byte> mesh)
    {
        if (!container.TryFindChunk(MefTags.BoneHierarchy, out IlffChunk hierarchyChunk))
        {
            return (null, []);
        }

        // nBones, Mesh3D_Mesh_t +0x86. The hierarchy bytes are sized by the header, as the engine does.
        int boneCount = BinaryPrimitives.ReadInt16LittleEndian(mesh[0x86..]);
        Igi2BoneHierarchy hierarchy;
        try
        {
            hierarchy = Igi2BoneHierarchy.Read(hierarchyChunk.Payload.Span, boneCount);
        }
        catch (IlffFormatException ex)
        {
            throw new PackFormatException($"The bone mesh's hierarchy is malformed: {ex.Message}", ex);
        }

        if (!container.TryFindChunk(MefTags.BoneNames, out IlffChunk namesChunk))
        {
            return (hierarchy, []);
        }

        ReadOnlySpan<byte> names = namesChunk.Payload.Span;
        if (names.Length < boneCount * BoneNameSize)
        {
            throw new PackFormatException(
                $"The bone mesh names {names.Length / BoneNameSize} bones but its hierarchy has {boneCount}.");
        }

        string[] read = new string[boneCount];
        for (int bone = 0; bone < boneCount; bone++)
        {
            read[bone] = ReadName(names.Slice(bone * BoneNameSize, BoneNameSize));
        }

        return (hierarchy, read);
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

    /// <summary>The size of one <c>RigidDirect3D_FaceGroup_t</c> — I.G.I. 2's unskinned and skinned groups.</summary>
    private const int RigidFaceGroupStride = 32;

    /// <summary>The size of one <c>LightmapDirect3D_FaceGroup_t</c> — I.G.I. 2's lightmapped groups.</summary>
    private const int LightmapFaceGroupStride = 28;

    private static ReadOnlyMemory<byte> RequireChunk(IlffContainer container, FourCc tag) =>
        container.TryFindChunk(tag, out IlffChunk chunk)
            ? chunk.Payload
            : throw new PackFormatException($"The model has no '{tag}' chunk.");

    private static (int VertexCount, int SubMeshCount, int LightmapCount) ReadRenderHeader(
        ReadOnlySpan<byte> render,
        MefMeshFormat format,
        AssetFormatProfile profile)
    {
        // Field positions shift with the format, and with the title: the trailing bytes are the
        // original's runtime binding block, zero on disk, and I.G.I. 2's is shorter. Its skinned layout
        // also puts the vertex count four bytes earlier. Only the lightmapped layout has a page count,
        // and it sits ahead of the counts the other two share -- in both games.
        if (!profile.TryGetRenderHeader((uint)format, out MeshRenderHeaderLayout layout))
        {
            throw new PackFormatException($"This title ships no mesh of format {(int)format}.");
        }

        (int subMeshOffset, int vertexOffset, int expectedSize) =
            (layout.SubMeshOffset, layout.VertexOffset, layout.Size);

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

    private static ReadOnlyCollection<MefAttachment> ReadAttachments(IlffContainer container, AssetFormatProfile profile)
    {
        if (!container.TryFindChunk(MefTags.Attachments, out IlffChunk chunk) || chunk.Payload.Length == 0)
        {
            return ReadOnlyCollection<MefAttachment>.Empty;
        }

        int stride = profile.AttachmentStride;
        ReadOnlySpan<byte> span = chunk.Payload.Span;
        if (span.Length % stride != 0)
        {
            throw new PackFormatException(
                $"The '{MefTags.Attachments}' chunk is {span.Length} bytes, not a whole number of "
                + $"{stride}-byte records.");
        }

        MefAttachment[] attachments = new MefAttachment[span.Length / stride];
        Span<float> rotation = stackalloc float[Matrix3f.ComponentCount];
        for (int i = 0; i < attachments.Length; i++)
        {
            ReadOnlySpan<byte> record = span.Slice(i * stride, stride);
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
                BinaryPrimitives.ReadInt32LittleEndian(record[profile.AttachmentBoneOffset..]));
        }

        return new ReadOnlyCollection<MefAttachment>(attachments);
    }

    private static string ReadName(ReadOnlySpan<byte> field)
    {
        int end = field.IndexOf((byte)0);
        return System.Text.Encoding.ASCII.GetString(end < 0 ? field : field[..end]);
    }

    private static MefVertex[] ReadVertices(
        ReadOnlySpan<byte> data,
        MefMeshFormat format,
        int count,
        AssetFormatProfile profile)
    {
        if (count == 0)
        {
            return [];
        }

        if (!profile.TryGetVertexLayout((uint)format, out MeshVertexLayout layout))
        {
            throw new PackFormatException($"This title ships no mesh of format {(int)format}.");
        }

        long needed = (long)layout.Stride * count;
        if (needed != data.Length)
        {
            throw new PackFormatException(
                $"The '{MefTags.Vertices}' chunk is {data.Length} bytes but {count} vertices at a "
                    + $"{layout.Stride}-byte stride need {needed}.");
        }

        MefVertex[] vertices = new MefVertex[count];
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> v = data.Slice(i * layout.Stride, layout.Stride);

            // I.G.I. 2's lightmapped vertex carries no normal at all -- the lighting is baked and
            // nothing shades from it -- so the field reads as zero rather than as whatever happens to
            // sit at the offset I.G.I. 1 keeps it at.
            Vector3f normal = layout.NormalOffset < 0
                ? default
                : new Vector3f(
                    BinaryPrimitives.ReadSingleLittleEndian(v[layout.NormalOffset..]),
                    BinaryPrimitives.ReadSingleLittleEndian(v[(layout.NormalOffset + 4)..]),
                    BinaryPrimitives.ReadSingleLittleEndian(v[(layout.NormalOffset + 8)..]));

            vertices[i] = new MefVertex(
                new Vector3f(
                    BinaryPrimitives.ReadSingleLittleEndian(v),
                    BinaryPrimitives.ReadSingleLittleEndian(v[4..]),
                    BinaryPrimitives.ReadSingleLittleEndian(v[8..])),
                normal,
                BinaryPrimitives.ReadSingleLittleEndian(v[layout.DiffuseUvOffset..]),
                BinaryPrimitives.ReadSingleLittleEndian(v[(layout.DiffuseUvOffset + 4)..]),
                layout.LightmapUvOffset < 0
                    ? 0f
                    : BinaryPrimitives.ReadSingleLittleEndian(v[layout.LightmapUvOffset..]),
                layout.LightmapUvOffset < 0
                    ? 0f
                    : BinaryPrimitives.ReadSingleLittleEndian(v[(layout.LightmapUvOffset + 4)..]));
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

    /// <summary>Reads I.G.I. 2's face-group table and the separate index chunk it points into.</summary>
    /// <remarks>
    /// <para>
    /// The sequel keeps a flat array of fixed-size groups -- <c>RigidDirect3D_FaceGroup_t</c> at 32
    /// bytes for the unskinned and skinned formats, <c>LightmapDirect3D_FaceGroup_t</c> at 28 for the
    /// lightmapped one -- and one index array in <c>ECAF</c> that every group names a run of. I.G.I. 1
    /// instead packs each record's indices after its own header, which is why its reader varies the
    /// record length and this one does not.
    /// </para>
    /// <para>
    /// <b>Indices are absolute here, not relative to the group's start vertex.</b> Confirmed twice: 5,486
    /// of 7,535 groups sampled carry indices past their own vertex count, and the renderer binds the
    /// index buffer once per mesh before the group loop (<c>Direct3DRender_DrawRigidMesh</c>), so the
    /// base vertex it passes is that mesh's offset within a shared buffer rather than anything per
    /// group.
    /// </para>
    /// </remarks>
    private static ReadOnlyCollection<MefDrawRecord> ReadFaceGroups(
        IlffContainer container,
        MefMeshFormat format,
        int groupCount,
        int vertexCount,
        int triangleCount,
        int lightmapPageCount)
    {
        if (groupCount == 0)
        {
            return ReadOnlyCollection<MefDrawRecord>.Empty;
        }

        bool lightmapped = format == MefMeshFormat.Lightmapped;
        int stride = lightmapped ? LightmapFaceGroupStride : RigidFaceGroupStride;

        ReadOnlySpan<byte> groups = RequireChunk(container, MefTags.DrawRecords).Span;
        ReadOnlySpan<byte> indexBytes = RequireChunk(container, MefTags.FaceIndices).Span;

        long expected = (long)groupCount * stride;
        if (groups.Length != expected)
        {
            throw new PackFormatException(
                $"The '{MefTags.DrawRecords}' chunk is {groups.Length} bytes but {groupCount} face groups "
                    + $"at a {stride}-byte stride need {expected}.");
        }

        int indexCount = indexBytes.Length / 2;
        if (indexBytes.Length % 2 != 0 || indexCount != triangleCount * 3)
        {
            throw new PackFormatException(
                $"The '{MefTags.FaceIndices}' chunk holds {indexBytes.Length} bytes but {triangleCount} "
                    + "triangles need " + (triangleCount * 6) + ".");
        }

        MefDrawRecord[] records = new MefDrawRecord[groupCount];
        int triangles = 0;

        for (int i = 0; i < groupCount; i++)
        {
            ReadOnlySpan<byte> group = groups.Slice(i * stride, stride);

            // The first four bytes are material properties -- shininess, transparency and, depending on
            // the format, illumination or shininess strength. None of them is geometry.
            Vector3f centre = new(
                BinaryPrimitives.ReadSingleLittleEndian(group[4..]),
                BinaryPrimitives.ReadSingleLittleEndian(group[8..]),
                BinaryPrimitives.ReadSingleLittleEndian(group[12..]));

            int startIndex = BinaryPrimitives.ReadUInt16LittleEndian(group[16..]);
            int faceCount = BinaryPrimitives.ReadUInt16LittleEndian(group[18..]);
            int startVertex = BinaryPrimitives.ReadUInt16LittleEndian(group[20..]);
            int vertexSpan = BinaryPrimitives.ReadUInt16LittleEndian(group[22..]);
            int materialSlot = BinaryPrimitives.ReadUInt16LittleEndian(group[24..]);
            int lightmapPage = lightmapped ? BinaryPrimitives.ReadUInt16LittleEndian(group[26..]) : 0;

            if (lightmapPage >= lightmapPageCount && lightmapPage != 0)
            {
                throw new PackFormatException(
                    $"Face group {i} samples lightmap page {lightmapPage} but the model has {lightmapPageCount}.");
            }

            long end = (long)startIndex + (faceCount * 3);
            if (end > indexCount)
            {
                throw new PackFormatException(
                    $"Face group {i} names indices {startIndex}..{end} but the '{MefTags.FaceIndices}' "
                        + $"chunk holds {indexCount}.");
            }

            ushort[] indices = new ushort[faceCount * 3];
            for (int j = 0; j < indices.Length; j++)
            {
                ushort index = BinaryPrimitives.ReadUInt16LittleEndian(indexBytes[((startIndex + j) * 2)..]);
                if (index >= vertexCount)
                {
                    throw new PackFormatException(
                        $"Face group {i} index {j} is {index}, past the model's {vertexCount} vertices.");
                }

                indices[j] = index;
            }

            records[i] = new MefDrawRecord(
                centre, materialSlot, lightmapPage, startVertex, vertexSpan, indicesAreRelative: false, indices);
            triangles += faceCount;
        }

        if (triangles != triangleCount)
        {
            throw new PackFormatException(
                $"The face groups cover {triangles} triangles but the mesh header declares {triangleCount}.");
        }

        return new ReadOnlyCollection<MefDrawRecord>(records);
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

            // The compact header's tail is the face group's blend state (0x49DF60, 0x49EC70); the
            // lightmapped header lays its fields out differently.
            bool isTransparent = !lightmappedHeader && BinaryPrimitives.ReadUInt16LittleEndian(header[22..]) != 0;
            bool isAdditive = !lightmappedHeader && (header[24] & 1) != 0;
            records[i] = new MefDrawRecord(
                centre, materialSlot, lightmapPage, baseVertex, span0, true, indices, isTransparent, isAdditive);
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
