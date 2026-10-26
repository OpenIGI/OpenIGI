using OpenIGI.Core.Numerics;

namespace OpenIGI.Formats.World;

/// <summary>
/// One draw record from a model's <c>DNER</c> chunk: a run of triangles that share a material slot.
/// </summary>
/// <remarks>
/// <para>
/// Records are packed back to back and fill the chunk exactly; there are as many as the render header's
/// sub-mesh count. The header is 28 bytes for <see cref="MefMeshFormat.Diffuse"/> and
/// <see cref="MefMeshFormat.Skinned"/> models and 32 bytes for <see cref="MefMeshFormat.Lightmapped"/>
/// ones, followed by <c>indexCount</c> 16-bit indices.
/// </para>
/// <para>
/// Indices are <em>relative</em> to <see cref="BaseVertex"/> in every format. An earlier reading had
/// the Diffuse and Skinned formats indexing the whole vertex array; measurement over mission 1's
/// multi-record models (the truck <c>622_01_1</c> most visibly) showed every record's indices span
/// exactly the run up to the next record's base. <see cref="IndicesAreRelative"/> remains for the
/// record's own bookkeeping.
/// </para>
/// </remarks>
public readonly record struct MefDrawRecord
{
    /// <summary>The material slot value that means "no material", stored as <c>0xFFFF</c>.</summary>
    public const int NoMaterialSlot = 0xFFFF;

    /// <summary>The header size in bytes for <see cref="MefMeshFormat.Diffuse"/> and <see cref="MefMeshFormat.Skinned"/> models.</summary>
    public const int CompactHeaderSize = 28;

    /// <summary>The header size in bytes for <see cref="MefMeshFormat.Lightmapped"/> models.</summary>
    public const int LightmappedHeaderSize = 32;

    /// <summary>Initializes a new instance of the <see cref="MefDrawRecord"/> struct.</summary>
    /// <param name="centre">The record's centre point.</param>
    /// <param name="materialSlot">The material slot, or <see cref="NoMaterialSlot"/>.</param>
    /// <param name="lightmapPage">The lightmap page this record samples, or zero when it has none.</param>
    /// <param name="baseVertex">The first vertex this record uses.</param>
    /// <param name="vertexSpan">The number of vertices from <paramref name="baseVertex"/>, or zero when unknown.</param>
    /// <param name="indicesAreRelative">Whether indices are relative to <paramref name="baseVertex"/>.</param>
    /// <param name="indices">The triangle-list indices.</param>
    /// <param name="isTransparent">Whether the record is a sorted, blended face group.</param>
    /// <param name="isAdditive">Whether a transparent record adds its texture rather than blending it.</param>
    /// <param name="isAlphaTested">Whether an opaque record cuts its texture out by alpha.</param>
    public MefDrawRecord(
        Vector3f centre,
        int materialSlot,
        int lightmapPage,
        int baseVertex,
        int vertexSpan,
        bool indicesAreRelative,
        ReadOnlyMemory<ushort> indices,
        bool isTransparent = false,
        bool isAdditive = false,
        bool isAlphaTested = false)
    {
        IsAlphaTested = !isTransparent && isAlphaTested;
        IsTransparent = isTransparent;
        IsAdditive = isTransparent && isAdditive;
        Centre = centre;
        MaterialSlot = materialSlot;
        LightmapPage = lightmapPage;
        BaseVertex = baseVertex;
        VertexSpan = vertexSpan;
        IndicesAreRelative = indicesAreRelative;
        Indices = indices;
    }

    /// <summary>Gets the record's centre point.</summary>
    public Vector3f Centre { get; }

    /// <summary>Gets the material slot, an index into the drawing instance's slot list, or <see cref="NoMaterialSlot"/>.</summary>
    /// <remarks>
    /// This is the whole material binding: the texture comes from the <em>instance</em>, not the model, so
    /// the same model drawn by two instances can use different textures. Records may share a slot.
    /// </remarks>
    public int MaterialSlot { get; }

    /// <summary>Gets the index into <see cref="MefModel.LightmapPages"/> of the page this record samples.</summary>
    /// <remarks>
    /// Only <see cref="MefMeshFormat.Lightmapped"/> models have this field, at offset 18 of the record; the
    /// other formats have no lightmap and report zero. The original selects records by comparing it to the
    /// page it is baking (<c>0x523D40</c>), and across the whole retail corpus the largest value a model
    /// uses is exactly one less than its page count.
    /// </remarks>
    public int LightmapPage { get; }

    /// <summary>Gets the first vertex this record uses.</summary>
    public int BaseVertex { get; }

    /// <summary>Gets the number of vertices from <see cref="BaseVertex"/>, or zero when the format does not state one.</summary>
    public int VertexSpan { get; }

    /// <summary>Gets a value indicating whether <see cref="Indices"/> are relative to <see cref="BaseVertex"/>.</summary>
    public bool IndicesAreRelative { get; }

    /// <summary>Gets a value indicating whether the record is a transparent face group.</summary>
    /// <remarks>
    /// The word at offset 22 of the compact header. The model draw (<c>0x49DF60</c>) draws a record
    /// with it clear straight away, opaque and lit, and defers one with it set to the depth-sorted
    /// transparent pass (<c>0x49EC70</c>). A lightmapped record keeps it at offset 24 (<c>0x49F1C0</c>).
    /// </remarks>
    public bool IsTransparent { get; }

    /// <summary>Gets a value indicating whether a transparent record is drawn additively.</summary>
    /// <remarks>
    /// Bit 0 of the byte at offset 24. The transparent pass (<c>0x49EC70</c>) then draws the texture
    /// alone -- lighting off, the texture colour selected, fog off -- at <c>ONE, ONE</c>; without it,
    /// lit, at <c>SRCALPHA, INVSRCALPHA</c>. The explosion FireBall (<c>1006_01_1</c>) is one such
    /// record: a flame painted on black that only reads added to the scene.
    /// </remarks>
    public bool IsAdditive { get; }

    /// <summary>Gets a value indicating whether an opaque record cuts its texture out where its alpha is low.</summary>
    /// <remarks>
    /// I.G.I. 2's <c>0x40</c> in the face group's <c>bMaterialProperties</c>: <c>Direct3DRender_DrawRigidFacegroup</c> then draws it
    /// with the alpha-test blend state, which keeps texels of alpha 64 and over. Project I.G.I.'s records carry no such flag.
    /// </remarks>
    public bool IsAlphaTested { get; }

    /// <summary>Gets the triangle-list indices; the count is always a multiple of three.</summary>
    public ReadOnlyMemory<ushort> Indices { get; }

    /// <summary>Gets the number of triangles this record draws.</summary>
    public int TriangleCount => Indices.Length / 3;

    /// <summary>Gets a value indicating whether this record draws with a material.</summary>
    public bool HasMaterial => MaterialSlot != NoMaterialSlot;

    /// <summary>Returns the header size in bytes for a mesh format.</summary>
    /// <param name="format">The mesh format.</param>
    /// <returns>32 for <see cref="MefMeshFormat.Lightmapped"/>, otherwise 28.</returns>
    public static int HeaderSizeOf(MefMeshFormat format) =>
        format == MefMeshFormat.Lightmapped ? LightmappedHeaderSize : CompactHeaderSize;

    /// <summary>Resolves an index to a position in the model's vertex array.</summary>
    /// <param name="index">One of the values in <see cref="Indices"/>.</param>
    /// <returns>The absolute vertex index.</returns>
    public int ToVertexIndex(ushort index) => IndicesAreRelative ? BaseVertex + index : index;

    /// <inheritdoc/>
    public override string ToString() =>
        $"slot={MaterialSlot} base={BaseVertex} span={VertexSpan} tris={TriangleCount}";
}
