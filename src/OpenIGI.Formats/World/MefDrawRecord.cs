using OpenIGI.Core.Numerics;

namespace OpenIGI.Formats.World;

/// <summary>
/// One draw record from a model's <c>DNER</c> chunk: a run of triangles that share a material slot.
/// </summary>
/// <remarks>
/// <para>
/// Records are packed back to back and fill the chunk exactly; there are as many as the render header's
/// sub-mesh count. The header is 28 bytes for <see cref="MefMeshFormat.Diffuse"/> and
/// <see cref="MefMeshFormat.Unbatched"/> models and 32 bytes for <see cref="MefMeshFormat.Lightmapped"/>
/// ones, followed by <c>indexCount</c> 16-bit indices.
/// </para>
/// <para>
/// Indices are <em>relative</em> to <see cref="BaseVertex"/> in every format. An earlier reading had
/// the Diffuse and Unbatched formats indexing the whole vertex array; measurement over mission 1's
/// multi-record models (the truck <c>622_01_1</c> most visibly) showed every record's indices span
/// exactly the run up to the next record's base. <see cref="IndicesAreRelative"/> remains for the
/// record's own bookkeeping.
/// </para>
/// </remarks>
public readonly record struct MefDrawRecord
{
    /// <summary>The material slot value that means "no material", stored as <c>0xFFFF</c>.</summary>
    public const int NoMaterialSlot = 0xFFFF;

    /// <summary>The header size in bytes for <see cref="MefMeshFormat.Diffuse"/> and <see cref="MefMeshFormat.Unbatched"/> models.</summary>
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
    public MefDrawRecord(
        Vector3f centre,
        int materialSlot,
        int lightmapPage,
        int baseVertex,
        int vertexSpan,
        bool indicesAreRelative,
        ReadOnlyMemory<ushort> indices)
    {
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
