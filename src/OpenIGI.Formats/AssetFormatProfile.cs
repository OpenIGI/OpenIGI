using System.Collections.Immutable;

namespace OpenIGI.Formats;

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
/// its sprites all version 9, both of which the readers already parse. **The mesh version is the one
/// that differs** — I.G.I. 1 ships 0.10 and I.G.I. 2 ships 0.16 — and it is the one place a reader
/// refuses the other game's data outright.
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
