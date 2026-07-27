using System.Buffers.Binary;
using OpenIGI.Formats.Legacy;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>
/// Covers the inferred <c>"RESOURCE"</c> flat archive against archives built to the documented layout.
/// </summary>
/// <remarks>
/// No retail file uses this container, so these tests pin the reader to the spec rather than to shipped
/// data. A real sample turning up is a reason to revisit both.
/// </remarks>
public sealed class FlatResourceArchiveTests
{
    [Fact]
    public void ReadsEntriesInFileOrder()
    {
        byte[] bytes = new FlatResourceWriter()
            .Add("textures/a.tex", [1, 2, 3])
            .Add("textures/b.tex", [4, 5])
            .ToArray();

        FlatResourceArchive archive = FlatResourceArchive.Read(bytes);

        Assert.Equal(2, archive.Entries.Count);
        Assert.Equal("textures/a.tex", archive.Entries[0].Name);
        Assert.Equal([1, 2, 3], archive.Entries[0].Data.ToArray());
        Assert.Equal("textures/b.tex", archive.Entries[1].Name);
        Assert.Equal([4, 5], archive.Entries[1].Data.ToArray());
    }

    [Fact]
    public void HandlesNameLengthsThatCrossTheAlignmentBoundary()
    {
        // The payload sits at alignUp(nameLength + 12, 32), so a name either side of the boundary must
        // still land on the right bytes.
        byte[] bytes = new FlatResourceWriter()
            .Add(new string('a', 19), [0xAA])
            .Add(new string('b', 20), [0xBB])
            .Add(new string('c', 21), [0xCC])
            .ToArray();

        FlatResourceArchive archive = FlatResourceArchive.Read(bytes);

        Assert.Equal([0xAA], archive.Entries[0].Data.ToArray());
        Assert.Equal([0xBB], archive.Entries[1].Data.ToArray());
        Assert.Equal([0xCC], archive.Entries[2].Data.ToArray());
    }

    [Fact]
    public void NormalizesBackslashesInNames()
    {
        byte[] bytes = new FlatResourceWriter().Add(@"menusystem\mainmenu.qsc", [1]).ToArray();

        Assert.Equal("menusystem/mainmenu.qsc", FlatResourceArchive.Read(bytes).Entries[0].Name);
    }

    [Fact]
    public void DetectsTheMagic()
    {
        Assert.True(FlatResourceArchive.IsFlatArchive(new FlatResourceWriter().Add("a", [1]).ToArray()));
        Assert.False(FlatResourceArchive.IsFlatArchive(new IlffWriter("IRES").AddName("a").ToArray()));
        Assert.False(FlatResourceArchive.IsFlatArchive("RESOURCE"u8));
    }

    [Fact]
    public void ReadsAnEmptyArchive() => Assert.Empty(FlatResourceArchive.Read(new FlatResourceWriter().ToArray()).Entries);

    [Fact]
    public void RejectsAForeignMagic() =>
        Assert.Throws<FlatResourceFormatException>(() => FlatResourceArchive.Read(new byte[64]));

    [Fact]
    public void RejectsAPayloadThatLeavesTheArchive()
    {
        byte[] bytes = new FlatResourceWriter().Add("a.dat", [1, 2, 3]).ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(32 + 4), (uint)bytes.Length);

        Assert.Throws<FlatResourceFormatException>(() => FlatResourceArchive.Read(bytes));
    }

    [Fact]
    public void RejectsAnEntryCountThatOutrunsTheData()
    {
        byte[] bytes = new FlatResourceWriter().Add("a.dat", [1]).ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 4);

        Assert.Throws<FlatResourceFormatException>(() => FlatResourceArchive.Read(bytes));
    }
}
