using System.Buffers.Binary;
using System.Text;
using OpenIGI.Formats.World;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>What an <c>INST</c> material slot indexes, on a pack built so the two answers differ.</summary>
/// <remarks>
/// Every retail pack gives <c>GTT</c> an identity texture mapping (see <see cref="RetailMtpMaterialTests"/>),
/// so no shipped file can tell "a slot is a <c>GTT</c> index" from "a slot is a <c>TEXF</c> index" apart.
/// These build one that can, which is the only place the distinction is observable at all.
/// </remarks>
public sealed class MtpPackTests
{
    /// <summary>A slot indexes <c>GTT</c>, and <c>GTT</c> names the texture.</summary>
    /// <remarks>
    /// <c>MTP_ParseMTP</c> (<c>igi2.exe 0x4F5DE0</c>) reads <c>gtt[2 * slot]</c> for the texture. The pack
    /// here maps slot 0 to texture 1 and slot 1 to texture 0, so a reader that skipped <c>GTT</c> and took
    /// the slot as a <c>TEXF</c> index would return the other name for both — this fails in both
    /// directions rather than one.
    /// </remarks>
    [Fact]
    public void A_material_slot_is_a_GTT_index_and_GTT_names_the_texture()
    {
        string[] textures = ["first", "second"];
        (uint Texture, uint Palette)[] table = [(1, NoPalette), (0, NoPalette)];

        Assert.Equal("second", ResolveSlot(textures, table, slot: 0));
        Assert.Equal("first", ResolveSlot(textures, table, slot: 1));
    }

    /// <summary>The second half of a <c>GTT</c> entry is a palette, and surfaces as one.</summary>
    /// <remarks>
    /// The original resolves it through <c>paPalInfo</c>/<c>paPalInfoExtra</c>, so it is a palette and not
    /// a lightmap. It is <see cref="PackId.None"/> in all 12,523 retail entries across both games, which
    /// is why this needs a built pack to exercise at all.
    /// </remarks>
    [Fact]
    public void A_materials_second_half_is_its_palette()
    {
        MtpPack pack = MtpPack.Read(BuildPack(["first", "second"], [(0, NoPalette), (1, 7)], slot: 1));

        Assert.True(pack.TryGetMaterial(pack.Instances[0], 0, out MtpMaterial material));
        Assert.Equal(1, material.Texture.Index);
        Assert.Equal(7, material.Palette.Index);
        Assert.False(material.Palette.IsNone);
    }

    /// <summary>A slot past the end of <c>GTT</c> is refused when the pack is read.</summary>
    /// <remarks>
    /// The bound is the material table's, because that is the table the slot indexes. Retail packs give
    /// <c>GTT</c> and <c>TEXF</c> the same length, so this too is only observable on a built pack.
    /// </remarks>
    [Fact]
    public void A_material_slot_past_the_end_of_GTT_is_refused()
    {
        PackFormatException error = Assert.Throws<PackFormatException>(
            () => MtpPack.Read(BuildPack(["first", "second"], [(0, NoPalette), (1, NoPalette)], slot: 2)));

        Assert.Contains("references material 2", error.Message, StringComparison.Ordinal);
    }

    private const uint NoPalette = uint.MaxValue;

    private static string ResolveSlot(string[] textures, (uint Texture, uint Palette)[] table, uint slot)
    {
        MtpPack pack = MtpPack.Read(BuildPack(textures, table, slot));
        Assert.True(pack.TryGetTextureName(pack.Instances[0], 0, out string? name));
        return name;
    }

    private static byte[] BuildPack(string[] textureNames, (uint Texture, uint Palette)[] table, uint slot)
    {
        List<byte> body = [.. Encoding.ASCII.GetBytes("MTP ")];
        AddNames(body, "MODS", "sniper");
        AddNames(body, "VNAM", "sniper_instance");
        AddNames(body, "TEXF", textureNames);

        // One instance: model 0, one material slot, holding `slot`.
        List<byte> inst = [];
        AddUInt32(inst, 0);
        AddUInt32(inst, 1);
        AddUInt32(inst, slot);
        AddChunk(body, "INST", inst);

        List<byte> gtt = [];
        AddUInt32(gtt, (uint)table.Length);
        foreach ((uint texture, uint palette) in table)
        {
            AddUInt32(gtt, texture);
            AddUInt32(gtt, palette);
        }

        AddChunk(body, "GTT ", gtt);

        List<byte> file = [.. Encoding.ASCII.GetBytes("FORM")];
        AddUInt32BigEndian(file, (uint)body.Count);
        file.AddRange(body);
        return [.. file];
    }

    private static void AddNames(List<byte> body, string tag, params string[] names)
    {
        List<byte> payload = [];
        AddUInt32(payload, (uint)names.Length);
        if (tag == "VNAM")
        {
            AddUInt32(payload, 0);
        }

        foreach (string name in names)
        {
            payload.AddRange(Encoding.Latin1.GetBytes(name));
            payload.Add(0);
        }

        AddChunk(body, tag, payload);
    }

    private static void AddChunk(List<byte> body, string tag, List<byte> payload)
    {
        body.AddRange(Encoding.ASCII.GetBytes(tag));
        AddUInt32BigEndian(body, (uint)payload.Count);
        body.AddRange(payload);
        while ((payload.Count & 3) != 0)
        {
            body.Add(0);
            payload.Add(0);
        }
    }

    private static void AddUInt32(List<byte> bytes, uint value)
    {
        Span<byte> target = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(target, value);
        bytes.AddRange(target.ToArray());
    }

    private static void AddUInt32BigEndian(List<byte> bytes, uint value)
    {
        Span<byte> target = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(target, value);
        bytes.AddRange(target.ToArray());
    }
}
