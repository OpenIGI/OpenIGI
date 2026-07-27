using OpenIGI.Formats.Ilff;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>Covers the stateful NAME/BODY walk that turns a container into named resources.</summary>
public sealed class IresArchiveTests
{
    [Fact]
    public void PairsEachNameWithTheChunksThatFollowIt()
    {
        byte[] bytes = new IlffWriter("IRES")
            .AddName("LOCAL:common/textures/a.tex")
            .AddBody([1, 2, 3])
            .AddName("LOCAL:common/textures/b.tex")
            .AddBody([4, 5])
            .ToArray();

        IresArchive archive = IresArchive.Read(bytes);

        Assert.Equal(2, archive.Entries.Count);
        Assert.Equal("LOCAL:common/textures/a.tex", archive.Entries[0].Name);
        Assert.Equal([1, 2, 3], archive.Entries[0].Data.ToArray());
        Assert.Equal("LOCAL:common/textures/b.tex", archive.Entries[1].Name);
        Assert.Equal([4, 5], archive.Entries[1].Data.ToArray());
        Assert.All(archive.Entries, e => Assert.Equal(IresEntryKind.Body, e.Kind));
    }

    [Fact]
    public void NormalizesBackslashesInNames()
    {
        byte[] bytes = new IlffWriter("IRES")
            .AddName(@"LOCAL:menusystem\mainmenu.qsc")
            .AddBody([1])
            .ToArray();

        IresArchive archive = IresArchive.Read(bytes);

        Assert.Equal("LOCAL:menusystem/mainmenu.qsc", archive.Entries[0].Name);
        Assert.True(archive.TryGetEntry(@"LOCAL:menusystem\mainmenu.qsc", out _));
        Assert.True(archive.TryGetEntry("LOCAL:menusystem/mainmenu.qsc", out _));
    }

    [Fact]
    public void ReadsContextStringEntriesAsTheLanguageArchivesUseThem()
    {
        // objectives.res is NAME/CSTR pairs: the key is the name and the value is the context string.
        byte[] bytes = new IlffWriter("IRES")
            .AddName("OBJ_1_COMPLETE")
            .AddContextString("Objective 1 Complete.")
            .ToArray();

        IresArchive archive = IresArchive.Read(bytes);
        IresEntry entry = Assert.Single(archive.Entries);

        Assert.Equal(IresEntryKind.ContextString, entry.Kind);
        Assert.Equal("Objective 1 Complete.", entry.AsText());
    }

    [Fact]
    public void ReadsPathEntries()
    {
        byte[] bytes = new IlffWriter("IRES")
            .AddName("LOCAL:common")
            .AddChunk("PATH", "common/\0"u8)
            .ToArray();

        IresEntry entry = Assert.Single(IresArchive.Read(bytes).Entries);

        Assert.Equal(IresEntryKind.Path, entry.Kind);
        Assert.Equal("common/", entry.AsText());
    }

    [Fact]
    public void EmitsEveryChunkThatSharesAName()
    {
        byte[] bytes = new IlffWriter("IRES")
            .AddName("weapons/strings/Medipack")
            .AddContextString("Medipack")
            .AddBody([7, 7])
            .ToArray();

        IresArchive archive = IresArchive.Read(bytes);

        Assert.Equal(2, archive.Entries.Count);
        Assert.Equal(IresEntryKind.ContextString, archive.Entries[0].Kind);
        Assert.Equal(IresEntryKind.Body, archive.Entries[1].Kind);

        // The first registration wins, as it does in the original's resource hash table.
        Assert.True(archive.TryGetEntry("weapons/strings/Medipack", out IresEntry? found));
        Assert.Equal(IresEntryKind.ContextString, found.Kind);
    }

    [Fact]
    public void FlattensNestedArchives()
    {
        IlffWriter inner = new IlffWriter("IRES")
            .AddName("LOCAL:nested/inner.tex")
            .AddBody([0xFE]);

        byte[] bytes = new IlffWriter("IRES")
            .AddName("LOCAL:outer.tex")
            .AddBody([0x01])
            .AddNested(inner)
            .ToArray();

        IresArchive archive = IresArchive.Read(bytes);

        Assert.Equal(2, archive.Entries.Count);
        Assert.True(archive.TryGetEntry("LOCAL:nested/inner.tex", out IresEntry? inner1));
        Assert.Equal([0xFE], inner1.Data.ToArray());
    }

    [Fact]
    public void IgnoresNestedDebugSubForms()
    {
        IlffWriter debug = new IlffWriter("DBUG").AddContextString("built by someone");

        byte[] bytes = new IlffWriter("IRES")
            .AddName("LOCAL:a.tex")
            .AddBody([1])
            .AddNested(debug)
            .ToArray();

        Assert.Single(IresArchive.Read(bytes).Entries);
    }

    [Fact]
    public void LookupIsCaseInsensitive()
    {
        byte[] bytes = new IlffWriter("IRES")
            .AddName("LOCAL:Common/Textures/Shadow1.tex")
            .AddBody([1])
            .ToArray();

        Assert.True(IresArchive.Read(bytes).TryGetEntry("local:common/textures/shadow1.TEX", out _));
    }

    [Fact]
    public void RejectsAPayloadBeforeAnyName()
    {
        byte[] bytes = new IlffWriter("IRES").AddBody([1, 2]).ToArray();

        IlffFormatException error = Assert.Throws<IlffFormatException>(() => IresArchive.Read(bytes));
        Assert.Contains("NAME", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsAContainerThatIsNotAnArchive()
    {
        byte[] bytes = new IlffWriter("FONT").AddChunk("FNTH", [1, 2, 3, 4]).ToArray();

        Assert.Throws<IlffFormatException>(() => IresArchive.Read(bytes));
    }

    [Fact]
    public void MissingNamesReportNotFound()
    {
        IresArchive archive = IresArchive.Read(new IlffWriter("IRES").AddName("a").AddBody([1]).ToArray());

        Assert.False(archive.TryGetEntry("b", out IresEntry? entry));
        Assert.Null(entry);
    }
}
