using System.Collections.Immutable;

namespace OpenIGI.Formats;

/// <summary>How one mesh format lays its vertices out on disk.</summary>
/// <remarks>
/// <para>
/// The layouts are the vertex structs the games' own renderers declare, read from I.G.I. 2's PDB rather
/// than inferred: <c>Direct3DRender_XYZNormalTex1Vertex_t</c> (32 B: position, normal, UV),
/// <c>Direct3DRender_BoneVertex_t</c> (40 B: that plus a bone index and weight word) and
/// <c>LightmapDirect3D_Vertex_t</c> (28 B: position, UV, lightmap UV).
/// </para>
/// <para>
/// I.G.I. 1 uses the D3D flexible vertex formats <c>0x112</c> = <c>XYZ|NORMAL|TEX1</c> at 32 bytes and
/// <c>0x212</c> = <c>XYZ|NORMAL|TEX2</c> at 40.
/// </para>
/// </remarks>
/// <param name="Format">The mesh format this describes.</param>
/// <param name="Stride">The bytes one vertex occupies.</param>
/// <param name="NormalOffset">The byte offset of the normal, or -1 when the format carries none.</param>
/// <param name="DiffuseUvOffset">The byte offset of the diffuse texture coordinates.</param>
/// <param name="LightmapUvOffset">The byte offset of the lightmap coordinates, or -1 when there are none.</param>
public readonly record struct MeshVertexLayout(
    uint Format,
    int Stride,
    int NormalOffset,
    int DiffuseUvOffset,
    int LightmapUvOffset);

/// <summary>How a title stores the triangles that make up a draw record.</summary>
public enum MeshDrawRecordStorage
{
    /// <summary>
    /// Each record's header is followed immediately by its own indices, and the records vary in length.
    /// Indices are relative to the record's base vertex. Project I.G.I.'s layout.
    /// </summary>
    InlineIndices,

    /// <summary>
    /// The records are a flat array of fixed-size face groups, and each names a start and a count into a
    /// separate index chunk. Indices are absolute into the model's vertex array. I.G.I. 2's layout.
    /// </summary>
    FaceGroupTable,
}

/// <summary>Where the counts sit in one mesh format's render header.</summary>
/// <remarks>
/// The offsets are shared between the games for two of the three formats; only the skinned layout
/// moved. See <see cref="AssetFormatProfile.MeshRenderHeaders"/>.
/// </remarks>
/// <param name="Format">The mesh format this describes.</param>
/// <param name="SubMeshOffset">The byte offset of the sub-mesh count.</param>
/// <param name="VertexOffset">The byte offset of the vertex count.</param>
/// <param name="Size">The smallest header this title ships for the format.</param>
public readonly record struct MeshRenderHeaderLayout(uint Format, int SubMeshOffset, int VertexOffset, int Size);

/// <summary>Which encodings of each asset format one game title ships.</summary>
/// <remarks>
/// <para>
/// Readers parse what the bytes say; this says which bytes a title is allowed to have said. That is the
/// original's own separation — <c>Texture_LoadFile</c> (<c>0x4B24C0</c>) rejects an unexpected version
/// in the loader and the header parser downstream never sees a bad one — and keeping it means a file
/// from the wrong game fails loudly instead of half-loading into something that looks nearly right.
/// </para>
/// <para>
/// The two titles overlap more than expected, so this is mostly a correctness seam rather than an
/// enabler. Measured against a retail installation of each: I.G.I. 2's textures are all version 11 and
/// its sprites all version 9, both of which the readers already parse. <b>Models are where they
/// differ</b>: I.G.I. 1 ships mesh version 0.10 and I.G.I. 2 ships 0.16, and their render headers are
/// different sizes — see <see cref="MeshRenderHeaders"/>.
/// </para>
/// <para>
/// I.G.I. 1's sets are what its readers have always accepted, not a re-measurement, so introducing this
/// cannot narrow what already loads.
/// </para>
/// </remarks>
public sealed record AssetFormatProfile
{
    /// <summary>Gets the <c>.tex</c> versions the title ships.</summary>
    public required ImmutableArray<uint> TextureVersions { get; init; }

    /// <summary>Gets the texture-atlas versions the title ships.</summary>
    public required ImmutableArray<uint> TextureAtlasVersions { get; init; }

    /// <summary>Gets the <c>.spr</c> versions the title ships.</summary>
    public required ImmutableArray<uint> SpriteVersions { get; init; }

    /// <summary>Gets the <c>MESH</c> versions the title ships.</summary>
    public required ImmutableArray<float> MeshVersions { get; init; }

    /// <summary>Gets the QVM bytecode versions the title ships.</summary>
    public required ImmutableArray<int> QvmVersions { get; init; }

    /// <summary>Gets the QVM instruction-set id the title emits.</summary>
    /// <remarks>
    /// Both titles emit 8, checked across every <c>.qvm</c> in a retail installation of each. It is
    /// carried anyway because it is the field that would tell us a build had changed instruction set,
    /// and finding that out from a garbled disassembly would be much worse than reading it here.
    /// </remarks>
    public required int QvmInstructionSet { get; init; }

    /// <summary>Gets the extensions a title's terrain files use, in the order the loader tries them.</summary>
    public required ImmutableArray<string> TerrainExtensions { get; init; }

    /// <summary>Gets where the counts sit in a model's render header, per mesh format.</summary>
    /// <remarks>
    /// The one place the two games' model format genuinely differs, and it differs less than it first
    /// appears: <b>only the size changes</b>. I.G.I. 2's trailing runtime binding block is shorter — 36,
    /// 40 and 44 bytes against 48, 56 and 56 — and every field sits where I.G.I. 1 puts it.
    /// <para>
    /// The skinned layout is the one to be careful with. Its vertex count at offset 20 is the
    /// <em>total</em> rows in the vertex chunk, base vertices plus extra influences; the mesh header's
    /// own count is the base alone. Matching the header's dwords against that count picks offset 16 and
    /// then fails on every skinned model, which is how this was got wrong once already.
    /// </para>
    /// </remarks>
    public required ImmutableArray<MeshRenderHeaderLayout> MeshRenderHeaders { get; init; }

    /// <summary>Gets the size of one attachment record.</summary>
    /// <remarks>
    /// <c>Mesh3D_AttachObject_t</c>: a 16-byte name, a position, an orientation and a bone index.
    /// I.G.I. 2 pads the orientation field to 40 bytes where I.G.I. 1 stores the nine floats tightly,
    /// which pushes the bone index from 64 to 68 and the record from 68 to 72. Nothing else moves.
    /// </remarks>
    public required int AttachmentStride { get; init; }

    /// <summary>Gets the byte offset of an attachment's bone index.</summary>
    public required int AttachmentBoneOffset { get; init; }

    /// <summary>Gets how a title stores the triangles of each draw record.</summary>
    /// <remarks>
    /// Structurally different rather than a matter of offsets, which is why this selects a reader rather
    /// than parameterising one.
    /// </remarks>
    public required MeshDrawRecordStorage DrawRecordStorage { get; init; }

    /// <summary>Gets how each mesh format lays its vertices out.</summary>
    /// <remarks>
    /// The lightmapped format is where the games differ: I.G.I. 1 writes 40 bytes with a normal and two
    /// UV sets, I.G.I. 2 writes 28 with two UV sets and <b>no normal</b>, because the lighting is baked
    /// and nothing shades from it. The other two formats are byte-identical between the games.
    /// </remarks>
    public required ImmutableArray<MeshVertexLayout> MeshVertexLayouts { get; init; }

    /// <summary>Looks up the vertex layout for a mesh format.</summary>
    /// <param name="format">The mesh format from the mesh header.</param>
    /// <param name="layout">Receives the layout when the title has one.</param>
    /// <returns><see langword="false"/> when this title ships no mesh of that format.</returns>
    public bool TryGetVertexLayout(uint format, out MeshVertexLayout layout)
    {
        foreach (MeshVertexLayout candidate in MeshVertexLayouts)
        {
            if (candidate.Format == format)
            {
                layout = candidate;
                return true;
            }
        }

        layout = default;
        return false;
    }

    /// <summary>Gets whether a mesh header's vertex count is the base count rather than the total.</summary>
    /// <remarks>
    /// <para>
    /// A skinned model's vertex chunk holds its base vertices followed by the extra weighted influences
    /// that accumulate into them, and the render header carries both counts and their sum. What the
    /// <em>mesh</em> header holds differs between the games: I.G.I. 1 puts the total there and I.G.I. 2
    /// puts the base.
    /// </para>
    /// <para>
    /// Measured across 2,048 models from ten retail I.G.I. 2 archives: all 101 skinned models carry the
    /// base count, and in both games the base and the extras sum to the total and the vertex chunk holds
    /// exactly that many rows. For unskinned models there is no difference, because the two are equal.
    /// </para>
    /// <para>
    /// <see langword="null"/> means "either" — for a tool reading a file without knowing which game it
    /// came from, where the cross-check is not available rather than satisfied.
    /// </para>
    /// </remarks>
    public required bool? MeshHeaderCountsBaseVertices { get; init; }

    /// <summary>Looks up the render-header layout for a mesh format.</summary>
    /// <param name="format">The mesh format from the mesh header.</param>
    /// <param name="layout">Receives the layout when the title has one.</param>
    /// <returns><see langword="false"/> when this title ships no mesh of that format.</returns>
    public bool TryGetRenderHeader(uint format, out MeshRenderHeaderLayout layout)
    {
        foreach (MeshRenderHeaderLayout candidate in MeshRenderHeaders)
        {
            if (candidate.Format == format)
            {
                layout = candidate;
                return true;
            }
        }

        layout = default;
        return false;
    }

    /// <summary>Gets the profile for retail Project I.G.I. (2000).</summary>
    public static AssetFormatProfile Igi1 { get; } = new()
    {
        TextureVersions = [2, 11],
        TextureAtlasVersions = [7, 9],
        SpriteVersions = [1, 7, 9],
        MeshVersions = [0.1f],
        QvmVersions = [5],
        QvmInstructionSet = 8,
        TerrainExtensions = [".bit", ".hmp", ".cmd", ".ctr", ".lmp"],
        MeshRenderHeaders =
        [
            new(0, SubMeshOffset: 8, VertexOffset: 12, Size: 48),
            new(1, SubMeshOffset: 8, VertexOffset: 20, Size: 56),
            new(3, SubMeshOffset: 12, VertexOffset: 16, Size: 56),
        ],
        MeshVertexLayouts =
        [
            new(0, Stride: 32, NormalOffset: 12, DiffuseUvOffset: 24, LightmapUvOffset: -1),
            new(1, Stride: 40, NormalOffset: 12, DiffuseUvOffset: 24, LightmapUvOffset: 32),
            new(3, Stride: 40, NormalOffset: 12, DiffuseUvOffset: 24, LightmapUvOffset: 32),
        ],
        MeshHeaderCountsBaseVertices = false,
        DrawRecordStorage = MeshDrawRecordStorage.InlineIndices,
        AttachmentStride = 68,
        AttachmentBoneOffset = 64,
    };

    /// <summary>Gets the profile for I.G.I. 2: Covert Strike (2003).</summary>
    /// <remarks>
    /// Measured against a retail 1.1 installation: 1,784 <c>.qvm</c> files all version 7 and instruction
    /// set 8; 128 <c>.tex</c> all version 11; 11 <c>.spr</c> all version 9; 228 models all mesh version
    /// 0.16. Terrain is not loose files — the heightmaps, lightmaps and material maps live inside
    /// <c>heightmaps.res</c> and <c>lightmaps.res</c> — so the extension list is empty and the archive
    /// names belong to the title's own layout.
    /// </remarks>
    public static AssetFormatProfile Igi2 { get; } = new()
    {
        TextureVersions = [11],
        TextureAtlasVersions = [7, 9],
        SpriteVersions = [9],
        MeshVersions = [0.16f],
        QvmVersions = [7],
        QvmInstructionSet = 8,
        TerrainExtensions = [],
        MeshRenderHeaders =
        [
            new(0, SubMeshOffset: 8, VertexOffset: 12, Size: 36),
            new(1, SubMeshOffset: 8, VertexOffset: 20, Size: 40),
            new(3, SubMeshOffset: 12, VertexOffset: 16, Size: 44),
        ],
        MeshVertexLayouts =
        [
            new(0, Stride: 32, NormalOffset: 12, DiffuseUvOffset: 24, LightmapUvOffset: -1),
            new(1, Stride: 40, NormalOffset: 12, DiffuseUvOffset: 24, LightmapUvOffset: -1),
            new(3, Stride: 28, NormalOffset: -1, DiffuseUvOffset: 12, LightmapUvOffset: 20),
        ],
        MeshHeaderCountsBaseVertices = true,
        DrawRecordStorage = MeshDrawRecordStorage.FaceGroupTable,
        AttachmentStride = 72,
        AttachmentBoneOffset = 68,
    };

    /// <summary>Gets a profile that accepts every encoding the readers understand.</summary>
    /// <remarks>
    /// For tools. <c>ResExtract</c> pointed at an archive should read it, not ask which game it came
    /// from; a profile is a statement about a title, and a tool is not running one.
    /// </remarks>
    public static AssetFormatProfile Permissive { get; } = new()
    {
        TextureVersions = [2, 11],
        TextureAtlasVersions = [7, 9],
        SpriteVersions = [1, 7, 9],
        MeshVersions = [0.1f, 0.16f],
        QvmVersions = [5, 7],
        QvmInstructionSet = 8,
        TerrainExtensions = [".bit", ".hmp", ".cmd", ".ctr", ".lmp"],
        MeshRenderHeaders =
        [
            new(0, SubMeshOffset: 8, VertexOffset: 12, Size: 48),
            new(1, SubMeshOffset: 8, VertexOffset: 20, Size: 56),
            new(3, SubMeshOffset: 12, VertexOffset: 16, Size: 56),
        ],
        MeshVertexLayouts =
        [
            new(0, Stride: 32, NormalOffset: 12, DiffuseUvOffset: 24, LightmapUvOffset: -1),
            new(1, Stride: 40, NormalOffset: 12, DiffuseUvOffset: 24, LightmapUvOffset: 32),
            new(3, Stride: 40, NormalOffset: 12, DiffuseUvOffset: 24, LightmapUvOffset: 32),
        ],
        MeshHeaderCountsBaseVertices = null,
        DrawRecordStorage = MeshDrawRecordStorage.InlineIndices,
        AttachmentStride = 68,
        AttachmentBoneOffset = 64,
    };

    /// <summary>Determines whether the title ships a <c>.tex</c> of this version.</summary>
    /// <param name="version">The version word read from the file.</param>
    /// <returns><see langword="true"/> when the title ships it.</returns>
    public bool AcceptsTexture(uint version) => TextureVersions.Contains(version);

    /// <summary>Determines whether the title ships a texture atlas of this version.</summary>
    /// <param name="version">The version word read from the file.</param>
    /// <returns><see langword="true"/> when the title ships it.</returns>
    public bool AcceptsTextureAtlas(uint version) => TextureAtlasVersions.Contains(version);

    /// <summary>Determines whether the title ships a <c>.spr</c> of this version.</summary>
    /// <param name="version">The version word read from the file.</param>
    /// <returns><see langword="true"/> when the title ships it.</returns>
    public bool AcceptsSprite(uint version) => SpriteVersions.Contains(version);

    /// <summary>Determines whether the title ships a mesh of this version.</summary>
    /// <param name="version">The version float read from the <c>MESH</c> chunk.</param>
    /// <returns><see langword="true"/> when the title ships it.</returns>
    /// <remarks>
    /// Compared on the exact bit pattern rather than with a tolerance. The value is read from the file
    /// and never computed, so every model of a given version carries an identical word; a tolerance
    /// would invent a range the format does not have and would let 0.16 pass for 0.1 the moment anyone
    /// widened it.
    /// </remarks>
    public bool AcceptsMesh(float version)
    {
        foreach (float supported in MeshVersions)
        {
            if (BitConverter.SingleToInt32Bits(supported) == BitConverter.SingleToInt32Bits(version))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Determines whether the title ships a QVM of this version.</summary>
    /// <param name="version">The version word from the QVM header.</param>
    /// <returns><see langword="true"/> when the title ships it.</returns>
    public bool AcceptsQvm(int version) => QvmVersions.Contains(version);
}
