using System.Buffers.Binary;
using System.Text;
using OpenIGI.Formats.World;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>Regression tests for the direct texture indices stored by an MTP INST record.</summary>
public sealed class MtpPackTests
{
    [Fact]
    public void InstSlotsResolveDirectlyAgainstTexfRatherThanGtt()
    {
        MtpPack pack = MtpPack.Read(BuildPack("unused", "sniper_level8"));

        Assert.True(pack.TryGetMaterial(pack.Instances[0], 0, out MtpMaterial material));
        Assert.Equal("sniper_level8", pack.TextureNames[material.Texture.Index]);
    }

    [Fact]
    public void InstSlotsRejectTexfOutOfRangeIndices()
    {
        Assert.Throws<PackFormatException>(() => MtpPack.Read(BuildPack("sniper_level8")));
    }

    private static byte[] BuildPack(params string[] textureNames)
    {
        List<byte> body = [.. Encoding.ASCII.GetBytes("MTP ")];
        AddNames(body, "MODS", "sniper");
        AddNames(body, "VNAM", "sniper_instance");
        AddNames(body, "TEXF", textureNames);

        List<byte> inst = [];
        AddUInt32(inst, 0);
        AddUInt32(inst, 1);
        AddUInt32(inst, 1);
        AddChunk(body, "INST", inst);

        List<byte> gtt = [];
        AddUInt32(gtt, 2);
        AddUInt32(gtt, 0);
        AddUInt32(gtt, uint.MaxValue);
        AddUInt32(gtt, 0);
        AddUInt32(gtt, uint.MaxValue);
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
