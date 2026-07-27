using System.Buffers.Binary;
using System.Text;
using OpenIGI.Core;
using OpenIGI.Formats.Ilff;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>Covers the ILFF container rules against hand-built archives.</summary>
public sealed class IlffContainerTests
{
    [Fact]
    public void ReadsHeaderFields()
    {
        byte[] bytes = new IlffWriter("IRES")
            .AddName("LOCAL:test/a.tex")
            .AddBody([1, 2, 3])
            .ToArray();

        IlffContainer container = IlffContainer.Read(bytes);

        Assert.Equal(IlffTags.Ires, container.FormType);
        Assert.Equal(4, container.Align);
        Assert.Equal(bytes.Length, container.Size);
        Assert.Equal(0, container.Next);
        Assert.Equal(2, container.Chunks.Count);
    }

    [Fact]
    public void SizeFieldCountsTheHeader()
    {
        // Retail archives set this field to the exact file length, header included.
        byte[] bytes = new IlffWriter("IRES").AddName("x").AddBody([9]).ToArray();
        Assert.Equal((uint)bytes.Length, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)));
        Assert.Equal(bytes.Length, IlffContainer.Read(bytes).Size);
    }

    [Fact]
    public void FirstChunkStartsAtTheAlignedHeaderLength()
    {
        byte[] aligned4 = new IlffWriter("IRES", align: 4).AddName("a").ToArray();
        byte[] aligned32 = new IlffWriter("IRES", align: 32).AddName("a").ToArray();

        Assert.Equal(20, IlffContainer.Read(aligned4).Chunks[0].OffsetInParent);
        Assert.Equal(32, IlffContainer.Read(aligned32).Chunks[0].OffsetInParent);
    }

    [Fact]
    public void PayloadIsAlignedRelativeToTheContainerBase()
    {
        // A 32-aligned BODY inside a 32-aligned container: its payload lands on the next multiple of 32
        // measured from the container base, not immediately after the 16-byte chunk header.
        byte[] bytes = new IlffWriter("IRES", align: 32)
            .AddName("LOCAL:test/a.tex", align: 4)
            .AddBody([0xAA, 0xBB, 0xCC, 0xDD], align: 32)
            .ToArray();

        IlffContainer container = IlffContainer.Read(bytes);
        IlffChunk body = container.Chunks[1];

        Assert.Equal(32, body.Align);
        Assert.Equal(0, body.OffsetInParent % 4);
        Assert.NotEqual(0, body.OffsetInParent % 32);
        Assert.Equal([0xAA, 0xBB, 0xCC, 0xDD], body.Payload.ToArray());
    }

    [Fact]
    public void NestedContainerAlignsItsPayloadsAgainstItsOwnBase()
    {
        // The discriminating case for the parent-relative rule: the inner container does not start on an
        // 8-byte boundary of the file, so aligning against the file would slide every inner payload.
        IlffWriter inner = new IlffWriter("IRES", align: 8)
            .AddName("LOCAL:nested/value", align: 8)
            .AddBody([0x11, 0x22, 0x33, 0x44, 0x55], align: 8);

        byte[] bytes = new IlffWriter("IRES", align: 32)
            .AddName("LOCAL:outer.tex", align: 4)
            .AddChunk("CSTR", "pad me more"u8, align: 4)
            .AddNested(inner)
            .ToArray();

        IlffContainer container = IlffContainer.Read(bytes);
        IlffChunk nestedChunk = Assert.Single(container.Chunks, c => c.IsContainer);
        Assert.NotEqual(0, nestedChunk.OffsetInParent % 8);

        IlffContainer nested = nestedChunk.AsContainer();
        Assert.Equal(IlffTags.Ires, nested.FormType);
        Assert.Equal(8, nested.Align);
        Assert.Equal(2, nested.Chunks.Count);
        Assert.Equal("LOCAL:nested/value\0"u8.ToArray(), nested.Chunks[0].Payload.ToArray());
        Assert.Equal([0x11, 0x22, 0x33, 0x44, 0x55], nested.Chunks[1].Payload.ToArray());
    }

    [Fact]
    public void ChunksAreLinkedByTheirNextField()
    {
        byte[] bytes = new IlffWriter("IRES")
            .AddName("a")
            .AddBody([1])
            .AddName("b")
            .AddBody([2, 3])
            .ToArray();

        IlffContainer container = IlffContainer.Read(bytes);

        Assert.Equal(4, container.Chunks.Count);
        for (int i = 0; i < container.Chunks.Count - 1; i++)
        {
            Assert.Equal(
                container.Chunks[i + 1].OffsetInParent,
                container.Chunks[i].OffsetInParent + container.Chunks[i].Next);
        }

        Assert.Equal(0, container.Chunks[^1].Next);
    }

    [Fact]
    public void IsIlffDetectsTheMagic()
    {
        Assert.True(IlffContainer.IsIlff(new IlffWriter("IRES").AddName("a").ToArray()));
        Assert.False(IlffContainer.IsIlff("RESOURCE not an ilff container at all"u8));
        Assert.False(IlffContainer.IsIlff("ILF"u8));
    }

    [Fact]
    public void TryFindChunkLocatesByTag()
    {
        IlffContainer container = IlffContainer.Read(new IlffWriter("FONT").AddChunk("FNTH", [1, 2, 3, 4]).ToArray());

        Assert.True(container.TryFindChunk(FourCc.FromString("FNTH"), out IlffChunk chunk));
        Assert.Equal(4, chunk.Size);
        Assert.False(container.TryFindChunk(FourCc.FromString("NOPE"), out _));
    }

    [Fact]
    public void RejectsAForeignMagic()
    {
        byte[] bytes = new IlffWriter("IRES").AddName("a").ToArray();
        Encoding.Latin1.GetBytes("XLFF").CopyTo(bytes, 0);

        IlffFormatException error = Assert.Throws<IlffFormatException>(() => IlffContainer.Read(bytes));
        Assert.Contains("ILFF", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsANonPowerOfTwoAlignment()
    {
        byte[] bytes = new IlffWriter("IRES").AddName("a").ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 6);

        Assert.Throws<IlffFormatException>(() => IlffContainer.Read(bytes));
    }

    [Fact]
    public void RejectsAChunkAlignedMoreStrictlyThanItsContainer()
    {
        byte[] bytes = new IlffWriter("IRES", align: 4).AddName("a").ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20 + 8), 32);

        IlffFormatException error = Assert.Throws<IlffFormatException>(() => IlffContainer.Read(bytes));
        Assert.Contains("exceeds", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsATruncatedContainer()
    {
        byte[] bytes = new IlffWriter("IRES").AddName("a").AddBody([1, 2, 3, 4]).ToArray();

        Assert.Throws<IlffFormatException>(() => IlffContainer.Read(bytes.AsMemory(0, bytes.Length - 4)));
        Assert.Throws<IlffFormatException>(() => IlffContainer.Read(bytes.AsMemory(0, 8)));
    }

    [Fact]
    public void RejectsAPayloadThatLeavesTheContainer()
    {
        byte[] bytes = new IlffWriter("IRES").AddName("a").ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20 + 4), (uint)bytes.Length);

        Assert.Throws<IlffFormatException>(() => IlffContainer.Read(bytes));
    }

    [Fact]
    public void RejectsANextFieldThatCannotAdvance()
    {
        byte[] bytes = new IlffWriter("IRES").AddName("a").AddBody([1]).ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20 + 12), 4);

        Assert.Throws<IlffFormatException>(() => IlffContainer.Read(bytes));
    }

    [Fact]
    public void AsContainerRejectsALeafChunk()
    {
        IlffContainer container = IlffContainer.Read(new IlffWriter("IRES").AddName("a").ToArray());
        Assert.Throws<IlffFormatException>(() => container.Chunks[0].AsContainer());
    }
}
