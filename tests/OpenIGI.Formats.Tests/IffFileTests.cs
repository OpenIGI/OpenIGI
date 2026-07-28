using System.Buffers.Binary;
using OpenIGI.Core;
using OpenIGI.Formats.Iff;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>
/// Covers the EA IFF-85 reader on hand-built data, including the size disagreement the retail
/// animation sets ship with.
/// </summary>
public sealed class IffFileTests
{
    [Fact]
    public void ItReadsAGroupAndItsChildren()
    {
        byte[] data = Group("FORM", "TEST", Leaf("AAAA", 1, 2), Leaf("BBBB", 3));

        IffChunk root = IffFile.ReadForm(data, FourCc.FromString("TEST"));

        Assert.True(root.IsGroup);
        Assert.Equal(2, root.Children.Count);
        Assert.Equal([1, 2], root.RequireChild(FourCc.FromString("AAAA")).Payload.ToArray());
        Assert.Equal([3], root.RequireChild(FourCc.FromString("BBBB")).Payload.ToArray());
    }

    [Fact]
    public void AnOddSizedChunkIsFollowedByAPadByte()
    {
        // Without honouring the pad, the next chunk's id reads one byte early and everything after it
        // is garbage — the classic IFF mistake.
        byte[] data = Group("FORM", "TEST", Leaf("ODDC", 9), Leaf("NEXT", 7));

        IffChunk root = IffFile.ReadForm(data, FourCc.FromString("TEST"));

        Assert.Equal(2, root.Children.Count);
        Assert.Equal([9], root.RequireChild(FourCc.FromString("ODDC")).Payload.ToArray());
        Assert.Equal([7], root.RequireChild(FourCc.FromString("NEXT")).Payload.ToArray());
    }

    [Fact]
    public void SizesAreBigEndian()
    {
        byte[] data = Group("FORM", "TEST", Leaf("AAAA", 1, 2, 3, 4));

        // A little-endian reading of 4 is 0x04000000, which would run past the end and throw.
        IffChunk root = IffFile.ReadForm(data, FourCc.FromString("TEST"));
        Assert.Equal(4, root.RequireChild(FourCc.FromString("AAAA")).Payload.Length);
    }

    [Fact]
    public void AChildsSizeWinsOverAWrongParentSize()
    {
        // The retail animation sets' exact shape: the outer FORM's declared end lands *inside* a
        // nested group — just past its header and form type — rather than at a chunk boundary. (A
        // declared end that falls on a boundary is ordinary IFF: what follows really is a sibling.)
        byte[] inner = Group("FORM", "INNR", Leaf("DEEP", 1, 2, 3, 4, 5, 6, 7, 8));
        byte[] first = Leaf("HEAD", 42);
        byte[] body = [.. first, .. inner];

        // 4 form type + the first child + the nested group's 8-byte header and 4-byte form type.
        byte[] outer = GroupWithSize("FORM", "TEST", 4 + first.Length + 12, body);

        IReadOnlyList<IffChunk> roots = IffFile.Read(outer);

        // One root, not two: the scan must not resume inside the group it just read.
        Assert.Single(roots);

        IffChunk root = roots[0];
        Assert.Equal(2, root.Children.Count);
        Assert.Equal([42], root.RequireChild(FourCc.FromString("HEAD")).Payload.ToArray());

        IffChunk? nested = root.FirstGroup(FourCc.FromString("INNR"));
        Assert.NotNull(nested);
        Assert.Equal(8, nested.RequireChild(FourCc.FromString("DEEP")).Payload.Length);
    }

    [Fact]
    public void AChunkPastTheEndOfTheDataIsRejected()
    {
        byte[] data = GroupWithSize("FORM", "TEST", 4096, []);

        IffFormatException error = Assert.Throws<IffFormatException>(() => IffFile.Read(data));
        Assert.Contains("past the end", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWrongFormTypeIsReportedSeparatelyFromMalformedData()
    {
        byte[] data = Group("FORM", "TEST", Leaf("AAAA", 1));

        IffFormatException error = Assert.Throws<IffFormatException>(
            () => IffFile.ReadForm(data, FourCc.FromString("OTHR")));
        Assert.Contains("not of type", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyDataIsRejected()
    {
        Assert.Throws<IffFormatException>(() => IffFile.Read(ReadOnlyMemory<byte>.Empty));
    }

    private static byte[] Leaf(string id, params byte[] payload)
    {
        byte[] chunk = new byte[8 + payload.Length + (payload.Length & 1)];
        System.Text.Encoding.Latin1.GetBytes(id).CopyTo(chunk, 0);
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(4), (uint)payload.Length);
        payload.CopyTo(chunk, 8);
        return chunk;
    }

    private static byte[] Group(string id, string formType, params byte[][] children)
    {
        byte[] body = [.. children.SelectMany(c => c)];
        return GroupWithSize(id, formType, 4 + body.Length, body);
    }

    private static byte[] GroupWithSize(string id, string formType, int declaredSize, byte[] body)
    {
        byte[] chunk = new byte[8 + 4 + body.Length];
        System.Text.Encoding.Latin1.GetBytes(id).CopyTo(chunk, 0);
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(4), (uint)declaredSize);
        System.Text.Encoding.Latin1.GetBytes(formType).CopyTo(chunk, 8);
        body.CopyTo(chunk, 12);
        return chunk;
    }
}
